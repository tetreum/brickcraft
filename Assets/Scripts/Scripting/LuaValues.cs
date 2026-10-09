using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.World;
using Mirror;
using MoonSharp.Interpreter;
using UnityEngine;

namespace Brickcraft.Scripting
{
    /// <summary>
    /// A position in the brick grid (studs sideways, plates up; a 2x2 world block is 2 x 3 x 2 cells)
    /// as scripts see it: pos.x, pos.y, pos.z. Scripts can pass one of these or a table {x=, y=, z=}.
    /// </summary>
    [MoonSharpUserData]
    public class LuaVector
    {
        public int x;
        public int y;
        public int z;

        public LuaVector(Vector3Int cell) {
            x = cell.x;
            y = cell.y;
            z = cell.z;
        }

        public override string ToString() {
            return "(" + x + ", " + y + ", " + z + ")";
        }

        [MoonSharpHidden]
        public Vector3Int ToCell() {
            return new Vector3Int(x, y, z);
        }

        /// <summary>A position given by a script, null (and a Lua error) if it isn't one.</summary>
        [MoonSharpHidden]
        public static Vector3Int Read(DynValue value, string what) {
            if (value.Type == DataType.UserData && value.UserData.Object is LuaVector) {
                return ((LuaVector)value.UserData.Object).ToCell();
            }
            if (value.Type == DataType.Table) {
                DynValue x = value.Table.Get("x"), y = value.Table.Get("y"), z = value.Table.Get("z");
                if (x.Type == DataType.Number && y.Type == DataType.Number && z.Type == DataType.Number) {
                    return new Vector3Int(Mathf.RoundToInt((float)x.Number), Mathf.RoundToInt((float)y.Number), Mathf.RoundToInt((float)z.Number));
                }
            }
            throw new ScriptRuntimeException(what + " should be a position, like {x = 0, y = 0, z = 0}");
        }
    }

    /// <summary>
    /// A brick or world block as scripts see it. World blocks are bricks too (2x2 ones that are part of
    /// the terrain): isBlock tells them apart, their position is their first cell.
    /// </summary>
    [MoonSharpUserData]
    public class BrickHandle
    {
        /// <summary>Its item's id, like "my_mod:catapult".</summary>
        public string item;
        /// <summary>Its colour (see BrickColorPalette), -1 for blocks drawn with their textures.</summary>
        public int color;
        /// <summary>It's part of the terrain, not a loose brick.</summary>
        public bool isBlock;
        /// <summary>Quarter turns, 0 to 3.</summary>
        public int rotation;
        /// <summary>Id of the player that placed it, 0 if no player did.</summary>
        public int placedBy;
        /// <summary>When it was placed, unix seconds (0 if no player placed it).</summary>
        public double placedAt;

        [MoonSharpHidden] public string brickId;
        [MoonSharpHidden] public Vector3Int cell;

        public LuaVector position {
            get { return new LuaVector(cell); }
        }

        /// <summary>It's still in the world (scripts can keep handles of bricks that were removed since).</summary>
        public bool exists {
            get {
                if (!isBlock) {
                    return Server.bricks.ContainsKey(brickId);
                }
                BlockDefinition block;
                return WorldBehaviour.Instance != null && BlockDatabase.TryGet(item, out block)
                    && WorldBehaviour.Instance.GetBlockType(BrickGrid.CellToBlock(cell)) == (BlockType)block.id;
            }
        }

        public override string ToString() {
            return (isBlock ? "block " : "brick ") + item + " at " + position;
        }

        /// <summary>
        /// Moves it (bricks anywhere their cells are free, blocks to another block's place that's free).
        /// False if it can't: not there anymore, no room, or that part of the world isn't loaded.
        /// </summary>
        public bool move(DynValue to) {
            Vector3Int target = LuaVector.Read(to, "move's position");
            if (!exists || !LuaApi.IsLoaded(target)) {
                return false;
            }
            if (!isBlock) {
                Brick brick = Server.bricks[brickId];
                if (!WorldNetwork.ServerMoveBrick(brick, new BrickPlacement(brick.model, target, brick.placement.rotation))) {
                    return false;
                }
                cell = target;
                return true;
            }
            Vector3Int from = BrickGrid.CellToBlock(cell);
            Vector3Int block = BrickGrid.CellToBlock(target);
            BlockType type = WorldBehaviour.Instance.GetBlockType(from);
            if (block == from || !BlockDatabase.Get(WorldBehaviour.Instance.GetBlockType(block)).isReplaceable || !BrickGrid.IsFree(blockCells(block))) {
                return false;
            }
            Placer placer = WorldNetwork.ServerPlacerOf(from);
            WorldNetwork.ServerSetBlock(from, BlockType.Air, null);
            WorldNetwork.ServerSetBlock(block, type, placer.playerId != 0 ? placer : (Placer?)null, color);
            cell = BrickGrid.BlockToCell(block);
            return true;
        }

        /// <summary>Takes it out of the world (nobody gets it). False if it wasn't there anymore.</summary>
        public bool remove() {
            if (!exists) {
                return false;
            }
            if (isBlock) {
                return WorldNetwork.ServerSetBlock(BrickGrid.CellToBlock(cell), BlockType.Air, null);
            }
            WorldNetwork.ServerRemoveBrick(Server.bricks[brickId]);
            return true;
        }

        // the cells of a world block, to check no loose brick is in the way
        private static BrickPlacement blockCells(Vector3Int block) {
            return new BrickPlacement(Server.brickModels[3003], BrickGrid.BlockToCell(block));
        }

        [MoonSharpHidden]
        public static BrickHandle ForBrick(Brick brick, Placer placer) {
            return new BrickHandle() {
                item = brick.itemId,
                color = brick.color,
                rotation = brick.placement.rotation,
                brickId = brick.id,
                cell = brick.placement.origin,
                placedBy = placer.playerId,
                placedAt = placer.placedAt,
            };
        }

        [MoonSharpHidden]
        public static BrickHandle ForBlock(Vector3Int block, BlockType type, int color, Placer placer) {
            return new BrickHandle() {
                item = BlockDatabase.Get(type).itemId ?? BlockDatabase.Get(type).name,
                color = color,
                isBlock = true,
                cell = BrickGrid.BlockToCell(block),
                placedBy = placer.playerId,
                placedAt = placer.placedAt,
            };
        }
    }

    /// <summary>A player as scripts see it.</summary>
    [MoonSharpUserData]
    public class PlayerHandle
    {
        /// <summary>Its id in players.db, the one bricks remember as placedBy.</summary>
        public int id;
        public string name;

        [MoonSharpHidden] public NetworkConnectionToClient connection;

        /// <summary>Where its feet are, null if it left.</summary>
        public LuaVector position {
            get {
                if (connection == null || connection.identity == null) {
                    return null;
                }
                return new LuaVector(BrickGrid.WorldToCell(connection.identity.transform.position));
            }
        }

        /// <summary>Gives it items (in the item's default colour, or the given one). False if they don't fit.</summary>
        public bool give(string itemId, int count = 1, int color = BrickColor.None) {
            Item item = LuaApi.ItemOf(itemId);
            if (count < 1) {
                throw new ScriptRuntimeException("give's count should be at least 1");
            }
            if (color != BrickColor.None && !item.AllowsColor(color)) {
                throw new ScriptRuntimeException(item.id + " can't have the colour " + color);
            }
            PlayerInventory inventory = connection != null && connection.identity != null ? connection.identity.GetComponent<PlayerInventory>() : null;
            return inventory != null && inventory.ServerAdd(item.id, color == BrickColor.None ? item.color : color, count);
        }

        /// <summary>Shows a message in its chat.</summary>
        public void message(string text) {
            if (connection != null && text != null) {
                connection.Send(new ChatMessage() { sender = ChatCommands.ServerName, text = text });
            }
        }

        public override string ToString() {
            return "player " + name;
        }

        [MoonSharpHidden]
        public static PlayerHandle For(NetworkConnectionToClient conn) {
            ConnectedPlayer player = conn != null ? conn.authenticationData as ConnectedPlayer : null;
            if (player == null) {
                return null;
            }
            return new PlayerHandle() { id = player.record.Id, name = player.record.Name, connection = conn };
        }
    }
}
