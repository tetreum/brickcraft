using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Brickcraft.Bricks;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>Who placed a block or brick and when.</summary>
    public struct Placer
    {
        /// <summary>The player's id in players.db, 0 when no player placed it (the test scene).</summary>
        public int playerId;
        /// <summary>Unix time, in seconds.</summary>
        public long placedAt;

        public static Placer Now(int playerId) {
            return new Placer() { playerId = playerId, placedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        }
    }

    /// <summary>A brick placed by a player, as stored in a save or sent to joining clients.</summary>
    public struct SavedBrick
    {
        public Guid id;
        /// <summary>Its item's slug, see Slugs.</summary>
        public string itemId;
        public Vector3Int origin;
        public byte rotation;
        /// <summary>Its colour, see BrickColorPalette.</summary>
        public int color;
        public Placer placer;
    }

    /// <summary>
    /// What players changed in a chunk since it was generated: blocks (by their index inside the
    /// chunk, see <see cref="BlockIndex"/>) and bricks (whose origin cell is in the chunk).
    /// Placed blocks also know who placed them, dug ones (air) don't.
    /// </summary>
    public class ChunkChanges
    {
        public readonly Dictionary<ushort, byte> blocks = new Dictionary<ushort, byte>();
        /// <summary>Who placed the blocks players placed, by block index.</summary>
        public readonly Dictionary<ushort, Placer> placers = new Dictionary<ushort, Placer>();
        /// <summary>Colours of the blocks placed with one (drawn with it instead of their texture), by block index.</summary>
        public readonly Dictionary<ushort, int> colors = new Dictionary<ushort, int>();
        public readonly Dictionary<Guid, SavedBrick> bricks = new Dictionary<Guid, SavedBrick>();

        public bool IsEmpty {
            get { return blocks.Count == 0 && bricks.Count == 0; }
        }

        public static ushort BlockIndex(int x, int y, int z) {
            return (ushort)(y << 8 | z << 4 | x);
        }

        public static Vector3Int BlockFromIndex(ushort index) {
            return new Vector3Int(index & 0xF, index >> 8, (index >> 4) & 0xF);
        }
    }

    /// <summary>
    /// Changes players made to the world, grouped by chunk. Only changes are kept, the rest of
    /// the world comes from the seed. The server keeps the regions around players (see
    /// <see cref="WorldStorage"/>), clients the chunks the server sent them.
    ///
    /// Thread safe: the generation threads read it while the main thread may change it.
    /// </summary>
    public class WorldChanges
    {
        private readonly object sync = new object();
        private readonly Dictionary<Vector2Int, ChunkChanges> chunks = new Dictionary<Vector2Int, ChunkChanges>();
        private readonly HashSet<Vector2Int> dirtyChunks = new HashSet<Vector2Int>();

        public static Vector2Int ChunkOfBlock(Vector3Int block) {
            return new Vector2Int(block.x >> 4, block.z >> 4);
        }

        public static Vector2Int ChunkOfBrick(Vector3Int origin) {
            return ChunkOfBlock(BrickGrid.CellToBlock(origin));
        }

        public bool HasUnsavedChanges {
            get {
                lock (sync) {
                    return dirtyChunks.Count > 0;
                }
            }
        }

        /// <summary>Changes a block, placer is who placed it (null when it was dug), color its colour (BrickColor.None for its texture).</summary>
        public void SetBlock(Vector3Int block, BlockType type, Placer? placer, int color = Bricks.BrickColor.None) {
            lock (sync) {
                Vector2Int coords = ChunkOfBlock(block);
                ChunkChanges changes = getOrCreate(coords);
                ushort index = ChunkChanges.BlockIndex(block.x & 0xF, block.y, block.z & 0xF);

                changes.blocks[index] = (byte)type;
                if (placer.HasValue) {
                    changes.placers[index] = placer.Value;
                } else {
                    changes.placers.Remove(index);
                }
                if (color != Bricks.BrickColor.None) {
                    changes.colors[index] = color;
                } else {
                    changes.colors.Remove(index);
                }
                dirtyChunks.Add(coords);
            }
        }

        /// <summary>Who placed a block, false when no one did (it was generated, or dug).</summary>
        public bool TryGetPlacer(Vector3Int block, out Placer placer) {
            lock (sync) {
                placer = default(Placer);
                return chunks.TryGetValue(ChunkOfBlock(block), out ChunkChanges changes)
                    && changes.placers.TryGetValue(ChunkChanges.BlockIndex(block.x & 0xF, block.y, block.z & 0xF), out placer);
            }
        }

        /// <summary>A placed brick as saved, false if there's none with that id there.</summary>
        public bool TryGetBrick(Guid id, Vector3Int origin, out SavedBrick brick) {
            lock (sync) {
                brick = default(SavedBrick);
                return chunks.TryGetValue(ChunkOfBrick(origin), out ChunkChanges changes) && changes.bricks.TryGetValue(id, out brick);
            }
        }

        public void AddBrick(SavedBrick brick) {
            lock (sync) {
                Vector2Int coords = ChunkOfBrick(brick.origin);
                getOrCreate(coords).bricks[brick.id] = brick;
                dirtyChunks.Add(coords);
            }
        }

        public void RemoveBrick(Guid id, Vector3Int origin) {
            lock (sync) {
                Vector2Int coords = ChunkOfBrick(origin);

                if (chunks.TryGetValue(coords, out ChunkChanges changes) && changes.bricks.Remove(id)) {
                    dirtyChunks.Add(coords);
                }
            }
        }

        /// <summary>Merges changes loaded from a save or received from the server.</summary>
        public void Merge(Vector2Int coords, ChunkChanges loaded) {
            lock (sync) {
                ChunkChanges changes = getOrCreate(coords);

                foreach (KeyValuePair<ushort, byte> block in loaded.blocks) {
                    changes.blocks[block.Key] = block.Value;
                    changes.placers.Remove(block.Key);
                    changes.colors.Remove(block.Key);
                }
                foreach (KeyValuePair<ushort, Placer> placer in loaded.placers) {
                    changes.placers[placer.Key] = placer.Value;
                }
                foreach (KeyValuePair<ushort, int> color in loaded.colors) {
                    changes.colors[color.Key] = color.Value;
                }
                foreach (KeyValuePair<Guid, SavedBrick> brick in loaded.bricks) {
                    changes.bricks[brick.Key] = brick.Value;
                }
            }
        }

        /// <summary>Applies the block changes of a chunk to its freshly generated blocks.</summary>
        public void ApplyTo(Chunk chunk) {
            lock (sync) {
                if (!chunks.TryGetValue(new Vector2Int(chunk.X, chunk.Z), out ChunkChanges changes)) {
                    return;
                }
                foreach (KeyValuePair<ushort, byte> block in changes.blocks) {
                    Vector3Int local = ChunkChanges.BlockFromIndex(block.Key);
                    chunk.SetType(local.x, local.y, local.z, (BlockType)block.Value, false);
                    chunk.SetColor(local.x, local.y, local.z, changes.colors.TryGetValue(block.Key, out int color) ? color : Bricks.BrickColor.None);
                }
            }
        }

        public List<SavedBrick> GetBricks(Vector2Int coords) {
            lock (sync) {
                return chunks.TryGetValue(coords, out ChunkChanges changes)
                    ? new List<SavedBrick>(changes.bricks.Values)
                    : new List<SavedBrick>();
            }
        }

        /// <summary>The changes of a chunk serialized in pages, to send it to a client. Always at least one page.</summary>
        public List<byte[]> GetPages(Vector2Int coords) {
            lock (sync) {
                return ChunkChangesSerializer.SerializePages(chunks.TryGetValue(coords, out ChunkChanges changes) ? changes : new ChunkChanges());
            }
        }

        /// <summary>Forgets the changes of the chunks matching the filter, once they're saved or no longer needed.</summary>
        public void Remove(Predicate<Vector2Int> filter) {
            lock (sync) {
                List<Vector2Int> removed = new List<Vector2Int>();

                foreach (Vector2Int coords in chunks.Keys) {
                    if (filter(coords)) {
                        removed.Add(coords);
                    }
                }
                foreach (Vector2Int coords in removed) {
                    chunks.Remove(coords);
                    dirtyChunks.Remove(coords);
                }
            }
        }

        public List<KeyValuePair<Vector2Int, byte[]>> TakeDirtyChunks() {
            return TakeDirtyChunks(coords => true);
        }

        /// <summary>
        /// Serializes the chunks matching the filter that changed since they were last taken, and
        /// forgets they were dirty. Chunks with nothing left come with a null payload, so their
        /// record can be erased.
        /// </summary>
        public List<KeyValuePair<Vector2Int, byte[]>> TakeDirtyChunks(Predicate<Vector2Int> filter) {
            lock (sync) {
                List<KeyValuePair<Vector2Int, byte[]>> dirty = new List<KeyValuePair<Vector2Int, byte[]>>();

                foreach (Vector2Int coords in dirtyChunks) {
                    if (filter(coords)) {
                        ChunkChanges changes = chunks[coords];
                        dirty.Add(new KeyValuePair<Vector2Int, byte[]>(coords, changes.IsEmpty ? null : ChunkChangesSerializer.Serialize(changes)));
                    }
                }
                foreach (KeyValuePair<Vector2Int, byte[]> chunk in dirty) {
                    dirtyChunks.Remove(chunk.Key);
                }
                return dirty;
            }
        }

        private ChunkChanges getOrCreate(Vector2Int coords) {
            if (!chunks.TryGetValue(coords, out ChunkChanges changes)) {
                changes = new ChunkChanges();
                chunks.Add(coords, changes);
            }
            return changes;
        }
    }

    /// <summary>
    /// Binary format of the changes of a chunk, used both by region files and the network.
    /// Deflate compressed:
    ///   u8 version
    ///   varint block name count, then the names (length prefixed UTF-8 slugs) of the blocks used below
    ///   varint block count, then per block: varint index delta (sorted), varint position of its name in that list,
    ///     zigzag varint colour (-1 for none, see BrickColorPalette), placer
    ///   varint brick count, then per brick: 16 bytes id, string item (length prefixed UTF-8 slug), zigzag varint colour,
    ///     zigzag varint x y z, u8 rotation, placer
    /// where placer is a varint player id (0 when no player placed it) followed, when not 0, by varint unix seconds.
    /// Blocks are saved by name, the numbers chunks use are only valid in the game that gave them (see
    /// BlockDatabase).
    /// </summary>
    public static class ChunkChangesSerializer
    {
        // change it when the format changes, records of other versions are rejected
        public const byte Version = 5;

        // pages keep network messages small, even for heavily modified chunks
        private const int BlocksPerPage = 8192;
        private const int BricksPerPage = 512;

        public static byte[] Serialize(ChunkChanges changes) {
            return serialize(new List<KeyValuePair<ushort, byte>>(changes.blocks), changes, new List<SavedBrick>(changes.bricks.Values));
        }

        public static List<byte[]> SerializePages(ChunkChanges changes) {
            List<KeyValuePair<ushort, byte>> blocks = new List<KeyValuePair<ushort, byte>>(changes.blocks);
            List<SavedBrick> bricks = new List<SavedBrick>(changes.bricks.Values);
            List<byte[]> pages = new List<byte[]>();

            for (int b = 0, k = 0; b < blocks.Count || k < bricks.Count || pages.Count == 0; b += BlocksPerPage, k += BricksPerPage) {
                pages.Add(serialize(
                    blocks.GetRange(Math.Min(b, blocks.Count), Math.Max(0, Math.Min(BlocksPerPage, blocks.Count - b))),
                    changes,
                    bricks.GetRange(Math.Min(k, bricks.Count), Math.Max(0, Math.Min(BricksPerPage, bricks.Count - k)))
                ));
            }
            return pages;
        }

        public static ChunkChanges Deserialize(byte[] data) {
            ChunkChanges changes = new ChunkChanges();

            using (MemoryStream compressed = new MemoryStream(data))
            using (DeflateStream deflate = new DeflateStream(compressed, CompressionMode.Decompress))
            using (BinaryReader reader = new BinaryReader(deflate)) {
                byte version = reader.ReadByte();
                if (version != Version) {
                    throw new InvalidDataException("Unknown chunk changes version " + version);
                }

                // this game's number of each block name the record uses
                byte[] numbers = new byte[(int)readVarUInt(reader)];
                for (int i = 0; i < numbers.Length; i++) {
                    numbers[i] = BlockDatabase.IdOf(reader.ReadString());
                }

                int blockCount = (int)readVarUInt(reader);
                int index = 0;
                for (int i = 0; i < blockCount; i++) {
                    index += (int)readVarUInt(reader);
                    changes.blocks[(ushort)index] = numbers[(int)readVarUInt(reader)];

                    int color = readVarInt(reader);
                    if (color != Bricks.BrickColor.None) {
                        // a colour removed from the palette: the block's own texture
                        if (Bricks.BrickColorPalette.Get(color) != null) {
                            changes.colors[(ushort)index] = color;
                        }
                    }

                    Placer? placer = readPlacer(reader);
                    if (placer.HasValue) {
                        changes.placers[(ushort)index] = placer.Value;
                    }
                }

                int brickCount = (int)readVarUInt(reader);
                for (int i = 0; i < brickCount; i++) {
                    SavedBrick brick = new SavedBrick() {
                        id = new Guid(reader.ReadBytes(16)),
                        itemId = reader.ReadString(),
                        color = readVarInt(reader),
                        origin = new Vector3Int(readVarInt(reader), readVarInt(reader), readVarInt(reader)),
                        rotation = reader.ReadByte(),
                        placer = readPlacer(reader) ?? default(Placer),
                    };
                    // a colour removed from the palette (or the item no longer has it): its item's default one
                    if (Server.items.TryGetValue(brick.itemId, out Item item)) {
                        brick.color = item.ValidColor(brick.color);
                    }
                    changes.bricks[brick.id] = brick;
                }
            }
            return changes;
        }

        private static byte[] serialize(List<KeyValuePair<ushort, byte>> blocks, ChunkChanges changes, List<SavedBrick> bricks) {
            blocks.Sort((a, b) => a.Key.CompareTo(b.Key));

            using (MemoryStream output = new MemoryStream()) {
                using (DeflateStream deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
                using (BinaryWriter writer = new BinaryWriter(deflate)) {
                    writer.Write(Version);

                    // the names of the blocks used, each changed block points into this list
                    List<string> names = new List<string>();
                    Dictionary<byte, int> nameIndexes = new Dictionary<byte, int>();
                    foreach (KeyValuePair<ushort, byte> block in blocks) {
                        if (!nameIndexes.ContainsKey(block.Value)) {
                            nameIndexes[block.Value] = names.Count;
                            names.Add(BlockDatabase.Get(block.Value).name);
                        }
                    }
                    writeVarUInt(writer, (uint)names.Count);
                    foreach (string name in names) {
                        writer.Write(name);
                    }

                    writeVarUInt(writer, (uint)blocks.Count);
                    int previous = 0;
                    foreach (KeyValuePair<ushort, byte> block in blocks) {
                        writeVarUInt(writer, (uint)(block.Key - previous));
                        writeVarUInt(writer, (uint)nameIndexes[block.Value]);
                        writeVarInt(writer, changes.colors.TryGetValue(block.Key, out int color) ? color : Bricks.BrickColor.None);
                        writePlacer(writer, changes.placers.TryGetValue(block.Key, out Placer placer) ? placer : default(Placer));
                        previous = block.Key;
                    }

                    writeVarUInt(writer, (uint)bricks.Count);
                    foreach (SavedBrick brick in bricks) {
                        writer.Write(brick.id.ToByteArray());
                        writer.Write(brick.itemId);
                        writeVarInt(writer, brick.color);
                        writeVarInt(writer, brick.origin.x);
                        writeVarInt(writer, brick.origin.y);
                        writeVarInt(writer, brick.origin.z);
                        writer.Write(brick.rotation);
                        writePlacer(writer, brick.placer);
                    }
                }
                return output.ToArray();
            }
        }

        private static void writePlacer(BinaryWriter writer, Placer placer) {
            writeVarUInt(writer, (uint)placer.playerId);
            if (placer.playerId != 0) {
                writeVarULong(writer, (ulong)placer.placedAt);
            }
        }

        private static Placer? readPlacer(BinaryReader reader) {
            int playerId = (int)readVarUInt(reader);
            if (playerId == 0) {
                return null;
            }
            return new Placer() { playerId = playerId, placedAt = (long)readVarULong(reader) };
        }

        private static void writeVarULong(BinaryWriter writer, ulong value) {
            while (value >= 0x80) {
                writer.Write((byte)(value | 0x80));
                value >>= 7;
            }
            writer.Write((byte)value);
        }

        private static ulong readVarULong(BinaryReader reader) {
            ulong value = 0;
            int shift = 0;
            byte b;
            do {
                if (shift > 63) {
                    throw new InvalidDataException("Invalid varint");
                }
                b = reader.ReadByte();
                value |= (ulong)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            return value;
        }

        private static void writeVarUInt(BinaryWriter writer, uint value) {
            while (value >= 0x80) {
                writer.Write((byte)(value | 0x80));
                value >>= 7;
            }
            writer.Write((byte)value);
        }

        private static uint readVarUInt(BinaryReader reader) {
            uint value = 0;
            int shift = 0;
            byte b;
            do {
                if (shift > 28) {
                    throw new InvalidDataException("Invalid varint");
                }
                b = reader.ReadByte();
                value |= (uint)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            return value;
        }

        // zigzag so small negative coordinates stay small
        private static void writeVarInt(BinaryWriter writer, int value) {
            writeVarUInt(writer, (uint)((value << 1) ^ (value >> 31)));
        }

        private static int readVarInt(BinaryReader reader) {
            uint value = readVarUInt(reader);
            return (int)(value >> 1) ^ -(int)(value & 1);
        }
    }
}
