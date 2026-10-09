using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// A saved world on disk, owned by the server:
    ///   world.dat                 header: format version, seed, generator version, timestamps,
    ///                             game versions that created and last saved it, name, difficulty,
    ///                             mods (id and version last played with)
    ///   regions/r.[x].[z].bcr     changes of each chunk, see <see cref="RegionFile"/>
    ///
    /// The terrain itself is never stored, it's generated again from the seed. Saves only contain
    /// what players changed, so they stay small however big the world gets.
    ///
    /// Regions are streamed: the changes of a region are read when the first of its chunks is
    /// needed and dropped (after saving them) when its last chunk is unloaded, so memory only
    /// holds the areas around players. All disk writes go through a single ordered queue, so a
    /// chunk is never overwritten by an older version, and reads wait for the writes before them.
    /// </summary>
    /// <summary>A mod a world is played with, see ModDatabase.</summary>
    public struct WorldMod
    {
        public string id;
        /// <summary>The version it was last played with.</summary>
        public string version;
    }

    public class WorldStorage : IDisposable
    {
        public const string HeaderFile = "world.dat";
        public const string RegionsFolder = "regions";

        // bump when the generator changes how a seed turns into terrain, saved changes would no longer line up
        // 2: deserts get their cacti (generating them used to fail and leave the chunk's flora unfinished)
        public const ushort GeneratorVersion = 2;

        private const uint Magic = 0x44574342; // "BCWD"
        // change it when the header changes, worlds with another one aren't opened
        private const ushort FormatVersion = 4;

        public string Folder { get; private set; }
        public long Seed { get; private set; }
        public long CreatedAt { get; private set; }
        /// <summary>When it was last played (saved, or opened), unix seconds.</summary>
        public long LastPlayedAt { get; private set; }
        /// <summary>What players called it.</summary>
        public string Name { get; private set; }
        public Difficulty Difficulty { get; private set; }
        /// <summary>The mods it's played with, chosen when it was created.</summary>
        public List<WorldMod> Mods { get; private set; } = new List<WorldMod>();

        /// <summary>The ids of its mods.</summary>
        public List<string> ModIds {
            get { return Mods.ConvertAll(m => m.id); }
        }

        /// <summary>The game version that created the world.</summary>
        public string CreatedWithVersion { get; private set; }
        /// <summary>The game version that played the world last, before this session.</summary>
        public string LastSavedWithVersion { get; private set; }

        /// <summary>The changes of the loaded regions.</summary>
        public WorldChanges Changes { get; private set; }

        private class Region
        {
            public RegionFile file;
            public bool isLoaded;     // its changes are in Changes
            public int loadedChunks;  // chunks of the world using it
        }

        private readonly object sync = new object();
        private readonly Dictionary<Vector2Int, Region> regions = new Dictionary<Vector2Int, Region>();

        // every disk write, in order
        private Task ioQueue = Task.CompletedTask;

        /// <summary>Opens the world saved in the folder, or creates it with what's given.</summary>
        public static WorldStorage OpenOrCreate(string folder, long newSeed, string newName, Difficulty newDifficulty) {
            if (!File.Exists(Path.Combine(folder, HeaderFile))) {
                Create(folder, newName, newSeed, newDifficulty);
            }
            WorldStorage storage = new WorldStorage() { Folder = folder, Changes = new WorldChanges() };
            Directory.CreateDirectory(Path.Combine(folder, RegionsFolder));
            storage.readHeader();
            storage.writeHeader(); // records that it was played now, and with which version
            return storage;
        }

        /// <summary>Creates a new world in the folder (nothing is generated until it's played).</summary>
        public static void Create(string folder, string name, long seed, Difficulty difficulty, List<WorldMod> mods = null) {
            WorldStorage storage = new WorldStorage() {
                Folder = folder,
                Name = name,
                Seed = seed,
                Difficulty = difficulty,
                Mods = mods ?? new List<WorldMod>(),
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                CreatedWithVersion = GameVersion.Current,
            };
            Directory.CreateDirectory(Path.Combine(folder, RegionsFolder));
            storage.writeHeader();
            UnityEngine.Debug.Log("Created world \"" + name + "\" in " + folder + " with seed " + seed);
        }

        /// <summary>Reads only the header of the world in the folder (to list saves), null if it isn't one.</summary>
        public static WorldStorage ReadInfo(string folder) {
            if (!File.Exists(Path.Combine(folder, HeaderFile))) {
                return null;
            }
            WorldStorage storage = new WorldStorage() { Folder = folder };
            try {
                storage.readHeader();
            } catch (Exception e) {
                UnityEngine.Debug.LogWarning("Can't read the world in " + folder + ": " + e.Message);
                return null;
            }
            return storage;
        }

        public static Vector2Int RegionOf(Vector2Int chunk) {
            return new Vector2Int(chunk.x >> 5, chunk.y >> 5);
        }

        // -------- streaming --------

        /// <summary>A chunk of the region is loaded in the world (main thread).</summary>
        public void AcquireChunk(Vector2Int chunk) {
            lock (sync) {
                getRegion(RegionOf(chunk)).loadedChunks++;
            }
        }

        /// <summary>
        /// Reads the changes of the chunk's region if they aren't in memory yet. Blocks while
        /// reading, meant for the generation threads.
        /// </summary>
        public void EnsureLoaded(Vector2Int chunk) {
            Vector2Int coords = RegionOf(chunk);
            Region region;
            Task writesBefore;

            lock (sync) {
                region = getRegion(coords);
                writesBefore = ioQueue;
            }

            lock (region) {
                if (region.isLoaded) {
                    return;
                }
                // its last changes may still be on their way to disk
                writesBefore.Wait();
                region.isLoaded = true;

                // regions nobody changed have no file, and get none just for being visited
                if (region.file == null && !File.Exists(regionPath(coords))) {
                    return;
                }

                RegionFile file = openFile(coords, region);
                foreach (int index in file.GetStoredChunks()) {
                    Vector2Int stored = new Vector2Int(
                        coords.x * RegionFile.ChunksPerSide + index % RegionFile.ChunksPerSide,
                        coords.y * RegionFile.ChunksPerSide + index / RegionFile.ChunksPerSide
                    );
                    try {
                        Changes.Merge(stored, ChunkChangesSerializer.Deserialize(file.Read(index)));
                    } catch (Exception e) {
                        UnityEngine.Debug.LogError("Chunk " + stored + " of the world save is corrupted, its changes are lost: " + e.Message);
                    }
                }
            }
        }

        /// <summary>
        /// A chunk of the region was unloaded (main thread). When it was the last one, the region's
        /// changes are saved and dropped from memory.
        /// </summary>
        public void ReleaseChunk(Vector2Int chunk) {
            Vector2Int coords = RegionOf(chunk);
            Region region;

            lock (sync) {
                region = getRegion(coords);
                region.loadedChunks--;

                if (region.loadedChunks > 0) {
                    return;
                }
            }

            lock (region) {
                if (!region.isLoaded) {
                    return;
                }
                List<KeyValuePair<Vector2Int, byte[]>> dirty = Changes.TakeDirtyChunks(c => RegionOf(c) == coords);
                Changes.Remove(c => RegionOf(c) == coords);
                region.isLoaded = false;

                enqueueWrite(dirty, () => closeIfUnused(coords));
            }
        }

        // -------- saving --------

        /// <summary>Writes the chunks changed since the last save, on a background thread.</summary>
        public void SaveInBackground() {
            if (Changes.HasUnsavedChanges) {
                enqueueWrite(Changes.TakeDirtyChunks(), null);
            }
        }

        /// <summary>Writes everything that's left and waits for it, for when the server stops.</summary>
        public void SaveNow() {
            enqueueWrite(Changes.TakeDirtyChunks(), null);
            Task last;
            lock (sync) {
                last = ioQueue;
            }
            last.Wait();
        }

        public void Dispose() {
            SaveNow();

            lock (sync) {
                foreach (Region region in regions.Values) {
                    if (region.file != null) {
                        region.file.Dispose();
                    }
                }
                regions.Clear();
            }
        }

        // the payloads are serialized by the caller, the queue only does disk work
        private void enqueueWrite(List<KeyValuePair<Vector2Int, byte[]>> dirty, Action after) {
            if (dirty.Count == 0 && after == null) {
                return;
            }
            lock (sync) {
                ioQueue = ioQueue.ContinueWith(_ => {
                    write(dirty);
                    if (after != null) {
                        after();
                    }
                }, TaskScheduler.Default);
            }
        }

        private void write(List<KeyValuePair<Vector2Int, byte[]>> dirty) {
            if (dirty.Count == 0) {
                return;
            }
            HashSet<RegionFile> touched = new HashSet<RegionFile>();

            try {
                foreach (KeyValuePair<Vector2Int, byte[]> chunk in dirty) {
                    Vector2Int coords = RegionOf(chunk.Key);
                    Region region;
                    lock (sync) {
                        region = getRegion(coords);
                    }
                    RegionFile file = openFile(coords, region);
                    file.Write(RegionFile.LocalIndex(chunk.Key.x, chunk.Key.y), chunk.Value);
                    touched.Add(file);
                }
                foreach (RegionFile file in touched) {
                    file.Flush();
                }
                writeHeader();
            } catch (Exception e) {
                UnityEngine.Debug.LogError("Couldn't save the world: " + e);
            }
        }

        // regions nobody uses don't keep their file open
        private void closeIfUnused(Vector2Int coords) {
            lock (sync) {
                if (regions.TryGetValue(coords, out Region region) && region.loadedChunks <= 0 && !region.isLoaded) {
                    if (region.file != null) {
                        region.file.Dispose();
                    }
                    regions.Remove(coords);
                }
            }
        }

        private Region getRegion(Vector2Int coords) {
            if (!regions.TryGetValue(coords, out Region region)) {
                region = new Region();
                regions.Add(coords, region);
            }
            return region;
        }

        private RegionFile openFile(Vector2Int coords, Region region) {
            lock (sync) {
                if (region.file == null) {
                    region.file = new RegionFile(regionPath(coords));
                }
                return region.file;
            }
        }

        private string regionPath(Vector2Int coords) {
            return Path.Combine(Folder, RegionsFolder, "r." + coords.x + "." + coords.y + RegionFile.Extension);
        }

        // -------- header --------

        private void readHeader() {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(Path.Combine(Folder, HeaderFile)))) {
                if (reader.ReadUInt32() != Magic) {
                    throw new InvalidDataException(HeaderFile + " isn't a world file");
                }
                ushort format = reader.ReadUInt16();
                if (format != FormatVersion) {
                    throw new InvalidDataException("The world was saved by another version of the game");
                }
                Seed = reader.ReadInt64();
                ushort generator = reader.ReadUInt16();
                CreatedAt = reader.ReadInt64();
                LastPlayedAt = reader.ReadInt64();
                CreatedWithVersion = reader.ReadString();
                LastSavedWithVersion = reader.ReadString();
                Name = reader.ReadString();
                Difficulty = (Difficulty)reader.ReadByte();

                int modCount = reader.ReadUInt16();
                Mods = new List<WorldMod>(modCount);
                for (int i = 0; i < modCount; i++) {
                    Mods.Add(new WorldMod() { id = reader.ReadString(), version = reader.ReadString() });
                }

                if (generator != GeneratorVersion) {
                    UnityEngine.Debug.LogWarning("The world was created with another version of the generator, saved changes may not line up with the terrain");
                }
            }
        }

        // written to a temporary file first, so the header is never half written
        private void writeHeader() {
            string path = Path.Combine(Folder, HeaderFile);
            string temporary = path + ".tmp";

            using (BinaryWriter writer = new BinaryWriter(File.Create(temporary))) {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(Seed);
                writer.Write(GeneratorVersion);
                writer.Write(CreatedAt);
                writer.Write(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // last played
                writer.Write(CreatedWithVersion ?? "unknown");
                writer.Write(GameVersion.Current);
                writer.Write(Name ?? "");
                writer.Write((byte)Difficulty);

                writer.Write((ushort)Mods.Count);
                foreach (WorldMod mod in Mods) {
                    // the version of the mod it's being played with, if it's active
                    Brickcraft.Mods.ModInfo active = null;
                    foreach (Brickcraft.Mods.ModInfo candidate in Brickcraft.Mods.ModDatabase.Active) {
                        if (candidate.id == mod.id) {
                            active = candidate;
                        }
                    }
                    writer.Write(mod.id);
                    writer.Write(active != null ? active.version : mod.version ?? "");
                }
            }

            if (File.Exists(path)) {
                File.Replace(temporary, path, null);
            } else {
                File.Move(temporary, path);
            }
        }
    }
}
