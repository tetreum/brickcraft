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
        /// <summary>See Brick.state.</summary>
        public int state;
        /// <summary>The brick it's attached to (see Brick.attachedTo), Guid.Empty for bricks on the grid.</summary>
        public Guid attachedTo;
        /// <summary>How a connected brick is held by attachedTo (see Bricks.BrickJoints), null for others.</summary>
        public Bricks.BrickJoint? joint;
    }

    /// <summary>An NPC left in a chunk, as stored in a save (see Npcs.NpcSystem; never sent to clients).</summary>
    public struct SavedNpc
    {
        /// <summary>Its kind, see Npcs.NpcInfo.id.</summary>
        public string type;
        /// <summary>Where it stood, absolute (see FloatingOrigin).</summary>
        public double x, y, z;
        /// <summary>Which way it faced, degrees around Y.</summary>
        public float yaw;
        public int health;
        /// <summary>Its owner's players.db id if it's a pet (one told to stay: following pets are saved with their owner), 0 otherwise.</summary>
        public int owner;
        public string ownerName;
        /// <summary>A pet's attack mode, see Npcs.NpcBrain.PetAttack.</summary>
        public byte attacks;
        /// <summary>Its pack (see Npcs.NpcGroups), Guid.Empty if it has none.</summary>
        public Guid pack;
        /// <summary>It appeared by itself (see Npcs.NpcSpawner), it may go away.</summary>
        public bool natural;
        /// <summary>Its home, an absolute cell (see Npcs.Npc.Home), when it has one.</summary>
        public bool hasHome;
        public Vector3Int home;
        /// <summary>The bed it sleeps in (a brick id), Guid.Empty if none.</summary>
        public Guid bed;
    }

    /// <summary>Items lying on the ground in a chunk, as stored in a save (see Network.DroppedItems; never sent to clients).</summary>
    public struct SavedDrop
    {
        public string itemId;
        public int color;
        public int quantity;
        public int health;
        /// <summary>Where it lay, absolute (see FloatingOrigin).</summary>
        public double x, y, z;
        /// <summary>Seconds it had left before going away.</summary>
        public float lifetime;
    }

    /// <summary>A vehicle left in a chunk, as stored in a save (see Vehicles.VehicleSystem; never sent to clients).</summary>
    public struct SavedVehicle
    {
        public Guid id;
        /// <summary>Where it was, absolute (see FloatingOrigin), and how it was turned.</summary>
        public double x, y, z;
        public Quaternion rotation;
        public List<SavedVehiclePart> parts;
        /// <summary>Its seats' wires (see Vehicles.VehicleWire); null or empty if none.</summary>
        public List<SavedWire> wires;
    }

    /// <summary>A seat of a saved vehicle wired to one of its parts (see Vehicles.VehicleWire).</summary>
    public struct SavedWire
    {
        public Guid seat;
        public Guid part;
        public byte action;
        public byte source;
        public int key;
        public bool invert;
    }

    /// <summary>A brick of a saved vehicle, in its cells (see Vehicles.VehicleGrid).</summary>
    public struct SavedVehiclePart
    {
        public Guid id;
        public string itemId;
        public int color;
        public Vector3Int origin;
        public byte rotation;
        public int state;
        /// <summary>A connected part (see Bricks.BrickJoints): the part holding it, Guid.Empty for parts on the vehicle's cells.</summary>
        public Guid holder;
        public Bricks.BrickJoint joint;
    }

    /// <summary>
    /// What players changed in a chunk since it was generated: blocks (by their index inside the
    /// chunk, see <see cref="BlockIndex"/>) and bricks (whose origin cell is in the chunk).
    /// Placed blocks also know who placed them, dug ones (air) don't. The server also keeps there the NPCs
    /// that were in the chunk when it was last saved.
    /// </summary>
    public class ChunkChanges
    {
        public readonly Dictionary<ushort, byte> blocks = new Dictionary<ushort, byte>();
        /// <summary>Who placed the blocks players placed, by block index.</summary>
        public readonly Dictionary<ushort, Placer> placers = new Dictionary<ushort, Placer>();
        /// <summary>Colours of the blocks placed with one (drawn with it instead of their texture), by block index.</summary>
        public readonly Dictionary<ushort, int> colors = new Dictionary<ushort, int>();
        public readonly Dictionary<Guid, SavedBrick> bricks = new Dictionary<Guid, SavedBrick>();
        public List<SavedNpc> npcs = new List<SavedNpc>();
        /// <summary>What storage bricks keep (see Network.BrickStorage), by brick id: server only, never sent.</summary>
        public readonly Dictionary<Guid, List<InventoryItem>> storage = new Dictionary<Guid, List<InventoryItem>>();
        /// <summary>Items on the ground: server only, never sent.</summary>
        public List<SavedDrop> drops = new List<SavedDrop>();
        /// <summary>Vehicles: server only, never sent (they're network objects).</summary>
        public List<SavedVehicle> vehicles = new List<SavedVehicle>();

        public bool IsEmpty {
            get { return blocks.Count == 0 && bricks.Count == 0 && npcs.Count == 0 && storage.Count == 0 && drops.Count == 0 && vehicles.Count == 0; }
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
        /// <summary>The blocks of a chunk that changed (world block coordinates) to one of those that pass the test.</summary>
        public List<Vector3Int> ChangedBlocks(Vector2Int chunk, Func<byte, bool> test) {
            List<Vector3Int> found = new List<Vector3Int>();
            lock (sync) {
                if (!chunks.TryGetValue(chunk, out ChunkChanges changes)) {
                    return found;
                }
                foreach (KeyValuePair<ushort, byte> block in changes.blocks) {
                    if (test(block.Value)) {
                        Vector3Int local = ChunkChanges.BlockFromIndex(block.Key);
                        found.Add(new Vector3Int((chunk.x << 4) + local.x, local.y, (chunk.y << 4) + local.z));
                    }
                }
            }
            return found;
        }

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
                    changes.storage.Remove(id);
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
                if (loaded.npcs.Count > 0) {
                    changes.npcs = new List<SavedNpc>(loaded.npcs);
                }
                foreach (KeyValuePair<Guid, List<InventoryItem>> kept in loaded.storage) {
                    changes.storage[kept.Key] = new List<InventoryItem>(kept.Value);
                }
                if (loaded.drops.Count > 0) {
                    changes.drops = new List<SavedDrop>(loaded.drops);
                }
                if (loaded.vehicles.Count > 0) {
                    changes.vehicles = new List<SavedVehicle>(loaded.vehicles);
                }
            }
        }

        /// <summary>The vehicles saved in a chunk.</summary>
        public List<SavedVehicle> GetVehicles(Vector2Int coords) {
            lock (sync) {
                return chunks.TryGetValue(coords, out ChunkChanges changes) ? new List<SavedVehicle>(changes.vehicles) : new List<SavedVehicle>();
            }
        }

        /// <summary>The vehicles in a chunk now, saved with it.</summary>
        public void SetVehicles(Vector2Int coords, List<SavedVehicle> vehicles) {
            lock (sync) {
                chunks.TryGetValue(coords, out ChunkChanges changes);
                if (vehicles.Count == 0 && (changes == null || changes.vehicles.Count == 0)) {
                    return;
                }
                changes = changes ?? getOrCreate(coords);
                changes.vehicles = new List<SavedVehicle>(vehicles);
                dirtyChunks.Add(coords);
            }
        }

        /// <summary>The items saved on the ground of a chunk.</summary>
        public List<SavedDrop> GetDrops(Vector2Int coords) {
            lock (sync) {
                return chunks.TryGetValue(coords, out ChunkChanges changes) ? new List<SavedDrop>(changes.drops) : new List<SavedDrop>();
            }
        }

        /// <summary>The items on the ground of a chunk now, saved with it.</summary>
        public void SetDrops(Vector2Int coords, List<SavedDrop> drops) {
            lock (sync) {
                chunks.TryGetValue(coords, out ChunkChanges changes);
                if (drops.Count == 0 && (changes == null || changes.drops.Count == 0)) {
                    return;
                }
                changes = changes ?? getOrCreate(coords);
                changes.drops = new List<SavedDrop>(drops);
                dirtyChunks.Add(coords);
            }
        }

        /// <summary>What a storage brick keeps (a copy), empty if nothing.</summary>
        public List<InventoryItem> GetStorage(Guid id, Vector3Int origin) {
            lock (sync) {
                return chunks.TryGetValue(ChunkOfBrick(origin), out ChunkChanges changes) && changes.storage.TryGetValue(id, out List<InventoryItem> items)
                    ? new List<InventoryItem>(items) : new List<InventoryItem>();
            }
        }

        /// <summary>What a storage brick keeps now, saved with its chunk.</summary>
        public void SetStorage(Guid id, Vector3Int origin, IEnumerable<InventoryItem> items) {
            lock (sync) {
                Vector2Int coords = ChunkOfBrick(origin);
                ChunkChanges changes = getOrCreate(coords);
                List<InventoryItem> list = new List<InventoryItem>(items);
                if (list.Count == 0) {
                    changes.storage.Remove(id);
                } else {
                    changes.storage[id] = list;
                }
                dirtyChunks.Add(coords);
            }
        }

        /// <summary>The NPCs saved in a chunk.</summary>
        public List<SavedNpc> GetNpcs(Vector2Int coords) {
            lock (sync) {
                return chunks.TryGetValue(coords, out ChunkChanges changes) ? new List<SavedNpc>(changes.npcs) : new List<SavedNpc>();
            }
        }

        /// <summary>The NPCs that are in a chunk now, saved with it.</summary>
        public void SetNpcs(Vector2Int coords, List<SavedNpc> npcs) {
            lock (sync) {
                chunks.TryGetValue(coords, out ChunkChanges changes);
                if (npcs.Count == 0 && (changes == null || changes.npcs.Count == 0)) {
                    return;
                }
                changes = changes ?? getOrCreate(coords);
                changes.npcs = new List<SavedNpc>(npcs);
                dirtyChunks.Add(coords);
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
    ///     zigzag varint x y z, u8 rotation, placer, zigzag varint state, u8 attached flag (then 16 bytes id of what it's attached to)
    ///   varint NPC count (always 0 in what's sent to clients), then per NPC: string type, double x y z (absolute),
    ///     float yaw, varint health, varint owner (then, when not 0, string owner name and u8 attack mode),
    ///     u8 pack flag (then 16 bytes pack id)
    ///   varint count of the NPCs' extras (0, or the NPC count), then per NPC in the same order: u8 flags
    ///     (1 natural, 2 home, 4 bed), then for a home zigzag varint x y z, for a bed its 16 bytes id
    ///   varint count of storage bricks with something in them (always 0 to clients), then per brick: its 16 bytes
    ///     id, varint stack count, per stack: varint slot, string item, zigzag varint colour, varint quantity, varint health
    ///   varint count of items on the ground (always 0 to clients), then per one: string item, zigzag varint colour,
    ///     varint quantity, varint health, double x y z (absolute), float seconds left
    ///   varint count of vehicles (always 0 to clients), then per one: 16 bytes id, double x y z (absolute), float
    ///     rotation x y z w, varint part count, per part: 16 bytes id, string item, zigzag varint colour, zigzag varint
    ///     x y z (its cells), u8 rotation, zigzag varint state
    ///   varint count of the vehicles' wire lists: 0, or one for each vehicle above, in order: varint count, per wire: 16
    ///     bytes seat id, 16 bytes part id, u8 action, u8 source, zigzag varint key, bool invert
    ///   varint count of connected bricks (see Bricks.BrickJoints; those above that have a joint), per one: 16 bytes id,
    ///     joint; then varint count of connected vehicle parts, per one: 16 bytes part id, 16 bytes holder part id, joint
    ///     (joint: varint holder connector, varint connector, zigzag varint slide, u8 spin + 4 if flipped)
    ///   varint count of the connected bricks and parts above held on their holder's studs instead (see
    ///     BrickJoint.onStuds), per one: 16 bytes id, zigzag varint x y z (its cell on the holder's grid)
    /// where placer is a varint player id (0 when no player placed it) followed, when not 0, by varint unix seconds.
    /// Blocks are saved by name, the numbers chunks use are only valid in the game that gave them (see
    /// BlockDatabase).
    /// </summary>
    public static class ChunkChangesSerializer
    {
        // change it when the format changes, records of other versions are rejected
        public const byte Version = 13;

        // pages keep network messages small, even for heavily modified chunks
        private const int BlocksPerPage = 8192;
        private const int BricksPerPage = 512;

        public static byte[] Serialize(ChunkChanges changes) {
            return serialize(new List<KeyValuePair<ushort, byte>>(changes.blocks), changes, new List<SavedBrick>(changes.bricks.Values), changes.npcs, changes.storage, changes.drops,
                changes.vehicles);
        }

        public static List<byte[]> SerializePages(ChunkChanges changes) {
            List<KeyValuePair<ushort, byte>> blocks = new List<KeyValuePair<ushort, byte>>(changes.blocks);
            List<SavedBrick> bricks = new List<SavedBrick>(changes.bricks.Values);
            List<byte[]> pages = new List<byte[]>();

            for (int b = 0, k = 0; b < blocks.Count || k < bricks.Count || pages.Count == 0; b += BlocksPerPage, k += BricksPerPage) {
                pages.Add(serialize(
                    blocks.GetRange(Math.Min(b, blocks.Count), Math.Max(0, Math.Min(BlocksPerPage, blocks.Count - b))),
                    changes,
                    bricks.GetRange(Math.Min(k, bricks.Count), Math.Max(0, Math.Min(BricksPerPage, bricks.Count - k))),
                    new List<SavedNpc>(), // NPCs are network objects of their own, clients don't get them here
                    new Dictionary<Guid, List<InventoryItem>>(), // nor what chests keep: it's sent when they're opened
                    new List<SavedDrop>(), // nor what's on the ground: drops are network objects
                    new List<SavedVehicle>() // nor vehicles: network objects too
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
                        state = readVarInt(reader),
                        attachedTo = reader.ReadByte() != 0 ? new Guid(reader.ReadBytes(16)) : Guid.Empty,
                    };
                    // a colour removed from the palette (or the item no longer has it): its item's default one
                    if (Server.items.TryGetValue(brick.itemId, out Item item)) {
                        brick.color = item.ValidColor(brick.color);
                    }
                    changes.bricks[brick.id] = brick;
                }

                int npcCount = (int)readVarUInt(reader);
                for (int i = 0; i < npcCount; i++) {
                    SavedNpc npc = new SavedNpc() {
                        type = reader.ReadString(),
                        x = reader.ReadDouble(),
                        y = reader.ReadDouble(),
                        z = reader.ReadDouble(),
                        yaw = reader.ReadSingle(),
                        health = (int)readVarUInt(reader),
                        owner = (int)readVarUInt(reader),
                    };
                    if (npc.owner != 0) {
                        npc.ownerName = reader.ReadString();
                        npc.attacks = reader.ReadByte();
                    }
                    npc.pack = reader.ReadByte() != 0 ? new Guid(reader.ReadBytes(16)) : Guid.Empty;
                    changes.npcs.Add(npc);
                }

                // what came after (records from before have no extras: 0)
                int extras = (int)readVarUInt(reader);
                for (int i = 0; i < extras; i++) {
                    byte flags = reader.ReadByte();
                    SavedNpc npc = i < changes.npcs.Count ? changes.npcs[i] : default(SavedNpc);
                    npc.natural = (flags & 1) != 0;
                    npc.hasHome = (flags & 2) != 0;
                    if (npc.hasHome) {
                        npc.home = new Vector3Int(readVarInt(reader), readVarInt(reader), readVarInt(reader));
                    }
                    npc.bed = (flags & 4) != 0 ? new Guid(reader.ReadBytes(16)) : Guid.Empty;
                    if (i < changes.npcs.Count) {
                        changes.npcs[i] = npc;
                    }
                }

                int stores = (int)readVarUInt(reader);
                for (int i = 0; i < stores; i++) {
                    Guid id = new Guid(reader.ReadBytes(16));
                    int stacks = (int)readVarUInt(reader);
                    List<InventoryItem> items = new List<InventoryItem>(stacks);
                    for (int s = 0; s < stacks; s++) {
                        InventoryItem stack = new InventoryItem() {
                            slot = (int)readVarUInt(reader),
                            itemId = reader.ReadString(),
                            color = readVarInt(reader),
                            quantity = (int)readVarUInt(reader),
                            health = (int)readVarUInt(reader),
                        };
                        // an item of a mod that isn't there now is lost, like in inventories
                        if (Server.items.TryGetValue(stack.itemId, out Item item)) {
                            stack.color = item.ValidColor(stack.color);
                            items.Add(stack);
                        }
                    }
                    changes.storage[id] = items;
                }

                int drops = (int)readVarUInt(reader);
                for (int i = 0; i < drops; i++) {
                    SavedDrop drop = new SavedDrop() {
                        itemId = reader.ReadString(),
                        color = readVarInt(reader),
                        quantity = (int)readVarUInt(reader),
                        health = (int)readVarUInt(reader),
                        x = reader.ReadDouble(),
                        y = reader.ReadDouble(),
                        z = reader.ReadDouble(),
                        lifetime = reader.ReadSingle(),
                    };
                    if (Server.items.ContainsKey(drop.itemId)) {
                        changes.drops.Add(drop);
                    }
                }

                int vehicles = (int)readVarUInt(reader);
                List<SavedVehicle> read = new List<SavedVehicle>();
                for (int i = 0; i < vehicles; i++) {
                    SavedVehicle vehicle = new SavedVehicle() {
                        id = new Guid(reader.ReadBytes(16)),
                        x = reader.ReadDouble(),
                        y = reader.ReadDouble(),
                        z = reader.ReadDouble(),
                        rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                        parts = new List<SavedVehiclePart>(),
                    };
                    int parts = (int)readVarUInt(reader);
                    for (int p = 0; p < parts; p++) {
                        SavedVehiclePart part = new SavedVehiclePart() {
                            id = new Guid(reader.ReadBytes(16)),
                            itemId = reader.ReadString(),
                            color = readVarInt(reader),
                            origin = new Vector3Int(readVarInt(reader), readVarInt(reader), readVarInt(reader)),
                            rotation = reader.ReadByte(),
                            state = readVarInt(reader),
                        };
                        // a part of a mod that isn't there now is lost, like the world's bricks
                        if (Server.items.ContainsKey(part.itemId)) {
                            vehicle.parts.Add(part);
                        }
                    }
                    read.Add(vehicle);
                }

                int wireLists = (int)readVarUInt(reader);
                for (int i = 0; i < wireLists; i++) {
                    List<SavedWire> wires = new List<SavedWire>();
                    int count = (int)readVarUInt(reader);
                    for (int w = 0; w < count; w++) {
                        wires.Add(new SavedWire() {
                            seat = new Guid(reader.ReadBytes(16)),
                            part = new Guid(reader.ReadBytes(16)),
                            action = reader.ReadByte(),
                            source = reader.ReadByte(),
                            key = readVarInt(reader),
                            invert = reader.ReadBoolean(),
                        });
                    }
                    if (i < read.Count) {
                        SavedVehicle vehicle = read[i];
                        vehicle.wires = wires;
                        read[i] = vehicle;
                    }
                }
                int connectedBricks = (int)readVarUInt(reader);
                for (int i = 0; i < connectedBricks; i++) {
                    Guid id = new Guid(reader.ReadBytes(16));
                    Bricks.BrickJoint joint = readJoint(reader);
                    if (changes.bricks.TryGetValue(id, out SavedBrick brick)) {
                        brick.joint = joint;
                        changes.bricks[id] = brick;
                    }
                }
                int connectedParts = (int)readVarUInt(reader);
                for (int i = 0; i < connectedParts; i++) {
                    Guid id = new Guid(reader.ReadBytes(16));
                    Guid holder = new Guid(reader.ReadBytes(16));
                    Bricks.BrickJoint joint = readJoint(reader);
                    foreach (SavedVehicle vehicle in read) {
                        for (int p = 0; p < vehicle.parts.Count; p++) {
                            if (vehicle.parts[p].id == id) {
                                SavedVehiclePart part = vehicle.parts[p];
                                part.holder = holder;
                                part.joint = joint;
                                vehicle.parts[p] = part;
                            }
                        }
                    }
                }

                int onStuds = (int)readVarUInt(reader);
                for (int i = 0; i < onStuds; i++) {
                    Guid id = new Guid(reader.ReadBytes(16));
                    Vector3Int origin = new Vector3Int(readVarInt(reader), readVarInt(reader), readVarInt(reader));
                    if (changes.bricks.TryGetValue(id, out SavedBrick brick) && brick.joint.HasValue) {
                        brick.joint = Bricks.BrickJoint.OnStuds(origin, brick.joint.Value.spin);
                        changes.bricks[id] = brick;
                    }
                    foreach (SavedVehicle vehicle in read) {
                        for (int p = 0; p < vehicle.parts.Count; p++) {
                            if (vehicle.parts[p].id == id && vehicle.parts[p].holder != Guid.Empty) {
                                SavedVehiclePart part = vehicle.parts[p];
                                part.joint = Bricks.BrickJoint.OnStuds(origin, part.joint.spin);
                                vehicle.parts[p] = part;
                            }
                        }
                    }
                }

                foreach (SavedVehicle vehicle in read) {
                    if (vehicle.parts.Count > 0) {
                        changes.vehicles.Add(vehicle);
                    }
                }
            }
            return changes;
        }

        private static byte[] serialize(List<KeyValuePair<ushort, byte>> blocks, ChunkChanges changes, List<SavedBrick> bricks, List<SavedNpc> npcs,
                Dictionary<Guid, List<InventoryItem>> storage, List<SavedDrop> drops, List<SavedVehicle> vehicles) {
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
                        writeVarInt(writer, brick.state);
                        // a flag, followed by the brick it's attached to
                        writer.Write((byte)(brick.attachedTo != Guid.Empty ? 1 : 0));
                        if (brick.attachedTo != Guid.Empty) {
                            writer.Write(brick.attachedTo.ToByteArray());
                        }
                    }

                    writeVarUInt(writer, (uint)npcs.Count);
                    foreach (SavedNpc npc in npcs) {
                        writer.Write(npc.type);
                        writer.Write(npc.x);
                        writer.Write(npc.y);
                        writer.Write(npc.z);
                        writer.Write(npc.yaw);
                        writeVarUInt(writer, (uint)Math.Max(0, npc.health));
                        writeVarUInt(writer, (uint)npc.owner);
                        if (npc.owner != 0) {
                            writer.Write(npc.ownerName ?? "");
                            writer.Write(npc.attacks);
                        }
                        writer.Write((byte)(npc.pack != Guid.Empty ? 1 : 0));
                        if (npc.pack != Guid.Empty) {
                            writer.Write(npc.pack.ToByteArray());
                        }
                    }

                    writeVarUInt(writer, (uint)npcs.Count);
                    foreach (SavedNpc npc in npcs) {
                        writer.Write((byte)((npc.natural ? 1 : 0) | (npc.hasHome ? 2 : 0) | (npc.bed != Guid.Empty ? 4 : 0)));
                        if (npc.hasHome) {
                            writeVarInt(writer, npc.home.x);
                            writeVarInt(writer, npc.home.y);
                            writeVarInt(writer, npc.home.z);
                        }
                        if (npc.bed != Guid.Empty) {
                            writer.Write(npc.bed.ToByteArray());
                        }
                    }

                    writeVarUInt(writer, (uint)storage.Count);
                    foreach (KeyValuePair<Guid, List<InventoryItem>> kept in storage) {
                        writer.Write(kept.Key.ToByteArray());
                        writeVarUInt(writer, (uint)kept.Value.Count);
                        foreach (InventoryItem stack in kept.Value) {
                            writeVarUInt(writer, (uint)stack.slot);
                            writer.Write(stack.itemId);
                            writeVarInt(writer, stack.color);
                            writeVarUInt(writer, (uint)Math.Max(0, stack.quantity));
                            writeVarUInt(writer, (uint)Math.Max(0, stack.health));
                        }
                    }

                    writeVarUInt(writer, (uint)drops.Count);
                    foreach (SavedDrop drop in drops) {
                        writer.Write(drop.itemId);
                        writeVarInt(writer, drop.color);
                        writeVarUInt(writer, (uint)Math.Max(0, drop.quantity));
                        writeVarUInt(writer, (uint)Math.Max(0, drop.health));
                        writer.Write(drop.x);
                        writer.Write(drop.y);
                        writer.Write(drop.z);
                        writer.Write(drop.lifetime);
                    }

                    writeVarUInt(writer, (uint)vehicles.Count);
                    foreach (SavedVehicle vehicle in vehicles) {
                        writer.Write(vehicle.id.ToByteArray());
                        writer.Write(vehicle.x);
                        writer.Write(vehicle.y);
                        writer.Write(vehicle.z);
                        writer.Write(vehicle.rotation.x);
                        writer.Write(vehicle.rotation.y);
                        writer.Write(vehicle.rotation.z);
                        writer.Write(vehicle.rotation.w);
                        writeVarUInt(writer, (uint)vehicle.parts.Count);
                        foreach (SavedVehiclePart part in vehicle.parts) {
                            writer.Write(part.id.ToByteArray());
                            writer.Write(part.itemId);
                            writeVarInt(writer, part.color);
                            writeVarInt(writer, part.origin.x);
                            writeVarInt(writer, part.origin.y);
                            writeVarInt(writer, part.origin.z);
                            writer.Write(part.rotation);
                            writeVarInt(writer, part.state);
                        }
                    }

                    writeVarUInt(writer, (uint)vehicles.Count);
                    foreach (SavedVehicle vehicle in vehicles) {
                        writeVarUInt(writer, (uint)(vehicle.wires != null ? vehicle.wires.Count : 0));
                        if (vehicle.wires == null) {
                            continue;
                        }
                        foreach (SavedWire wire in vehicle.wires) {
                            writer.Write(wire.seat.ToByteArray());
                            writer.Write(wire.part.ToByteArray());
                            writer.Write(wire.action);
                            writer.Write(wire.source);
                            writeVarInt(writer, wire.key);
                            writer.Write(wire.invert);
                        }
                    }

                    List<SavedBrick> connected = bricks.FindAll(b => b.joint.HasValue);
                    writeVarUInt(writer, (uint)connected.Count);
                    foreach (SavedBrick brick in connected) {
                        writer.Write(brick.id.ToByteArray());
                        writeJoint(writer, brick.joint.Value);
                    }
                    List<SavedVehiclePart> connectedParts = new List<SavedVehiclePart>();
                    foreach (SavedVehicle vehicle in vehicles) {
                        connectedParts.AddRange(vehicle.parts.FindAll(p => p.holder != Guid.Empty));
                    }
                    writeVarUInt(writer, (uint)connectedParts.Count);
                    foreach (SavedVehiclePart part in connectedParts) {
                        writer.Write(part.id.ToByteArray());
                        writer.Write(part.holder.ToByteArray());
                        writeJoint(writer, part.joint);
                    }
                    List<KeyValuePair<Guid, Vector3Int>> onStuds = new List<KeyValuePair<Guid, Vector3Int>>();
                    foreach (SavedBrick brick in connected) {
                        if (brick.joint.Value.onStuds) {
                            onStuds.Add(new KeyValuePair<Guid, Vector3Int>(brick.id, brick.joint.Value.origin));
                        }
                    }
                    foreach (SavedVehiclePart part in connectedParts) {
                        if (part.joint.onStuds) {
                            onStuds.Add(new KeyValuePair<Guid, Vector3Int>(part.id, part.joint.origin));
                        }
                    }
                    writeVarUInt(writer, (uint)onStuds.Count);
                    foreach (KeyValuePair<Guid, Vector3Int> held in onStuds) {
                        writer.Write(held.Key.ToByteArray());
                        writeVarInt(writer, held.Value.x);
                        writeVarInt(writer, held.Value.y);
                        writeVarInt(writer, held.Value.z);
                    }
                }
                return output.ToArray();
            }
        }

        private static void writeJoint(BinaryWriter writer, Bricks.BrickJoint joint) {
            writeVarUInt(writer, (uint)joint.holderConnector);
            writeVarUInt(writer, (uint)joint.connector);
            writeVarInt(writer, joint.slide);
            writer.Write((byte)(Bricks.BrickJoints.Mod(joint.spin, 4) | (joint.flip ? 4 : 0)));
        }

        private static Bricks.BrickJoint readJoint(BinaryReader reader) {
            Bricks.BrickJoint joint = new Bricks.BrickJoint() {
                holderConnector = (int)readVarUInt(reader),
                connector = (int)readVarUInt(reader),
                slide = readVarInt(reader),
            };
            byte turns = reader.ReadByte();
            joint.spin = turns & 3;
            joint.flip = (turns & 4) != 0;
            return joint;
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
