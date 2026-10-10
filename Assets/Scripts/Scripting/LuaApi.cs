using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Mods;
using Brickcraft.Network;
using Brickcraft.World;
using Mirror;
using MoonSharp.Interpreter;
using UnityEngine;

namespace Brickcraft.Scripting
{
    /// <summary>
    /// What scripts can call, installed in each mod's Lua state (see ModScripts):
    ///   log(...)                                   the server log, like print
    ///   world.spawn(item, position, options)       a new loose brick (options: rotation 0-3, color), nil if it doesn't fit
    ///   world.brickAt(position)                    the brick or world block in that cell, nil if it's empty
    ///   world.getBlock(position)                   the item of the world block there, nil for air
    ///   world.setBlock(position, item, color)      changes the world block there (item nil for air), false if it can't
    ///   world.bricksIn(from, to)                   the loose bricks whose first cell is in the box
    ///   world.players()                            the players in the game
    ///   world.isLoaded(position)                   whether that part of the world is loaded (changes need it)
    ///   timer.after(seconds, fn), timer.every(seconds, fn), timer.cancel(id)
    ///   ui.*                                       popups, toasts, titles and HUD panels, see LuaUi
    /// Bricks (BrickHandle) also have move(position) and remove(), players (PlayerHandle) give(item, count, color).
    /// Positions are grid cells: {x =, y =, z =} tables or what bricks and players return.
    /// Modders' documentation is in docs/lua (a page per section): keep it in step with this.
    /// </summary>
    public static class LuaApi
    {
        /// <summary>Timers a mod can have at once.</summary>
        public const int MaxTimers = 256;
        /// <summary>Shortest interval of a repeating timer, in seconds.</summary>
        public const float MinInterval = 0.05f;

        private class Timer
        {
            public int id;
            public Script script;
            public string mod;
            public DynValue function;
            public double due;
            public double interval; // 0 for timers that run once
        }

        private static readonly List<Timer> timers = new List<Timer>();
        private static int nextTimerId = 1;

        public static void Install(Script script, ModInfo mod) {
            script.Globals["log"] = DynValue.NewCallback((context, args) => {
                string[] parts = new string[args.Count];
                for (int i = 0; i < args.Count; i++) {
                    parts[i] = args[i].ToPrintString();
                }
                Debug.Log("[" + mod.id + "] " + string.Join(" ", parts));
                return DynValue.Nil;
            });
            script.Globals["world"] = createWorld(script);
            script.Globals["timer"] = createTimer(script, mod);
            LuaUi.Install(script, mod);
        }

        /// <summary>Forgets the timers, when the scripts stop.</summary>
        public static void Reset() {
            timers.Clear();
            LuaUi.Reset();
        }

        /// <summary>Runs the timers that are due, every frame on the server.</summary>
        public static void Update() {
            if (timers.Count == 0) {
                return;
            }
            double now = Time.timeAsDouble;
            foreach (Timer timer in timers.ToArray()) {
                if (timer.due > now || !timers.Contains(timer)) {
                    continue;
                }
                if (timer.interval > 0) {
                    timer.due = now + timer.interval;
                } else {
                    timers.Remove(timer);
                }
                ModScripts.CallFunction(timer.script, timer.function, "timer " + timer.id);
            }
        }

        /// <summary>The item with that id, or a Lua error.</summary>
        public static Item ItemOf(string id) {
            Item item;
            if (id == null || !Server.items.TryGetValue(id, out item)) {
                throw new ScriptRuntimeException("There's no item \"" + id + "\"");
            }
            return item;
        }

        /// <summary>The chunk of that cell is loaded on the server (scenes without a generated world always are).</summary>
        public static bool IsLoaded(Vector3Int cell) {
            return WorldBehaviour.Instance == null || WorldBehaviour.Instance.IsGenerated(WorldChanges.ChunkOfBrick(cell));
        }

        private static Table createWorld(Script script) {
            Table world = new Table(script);

            world["spawn"] = DynValue.NewCallback((context, args) => {
                Item item = ItemOf(args.AsType(0, "spawn", DataType.String).String);
                Vector3Int cell = LuaVector.Read(args[1], "spawn's position");
                Table options = args.Count > 2 && args[2].Type == DataType.Table ? args[2].Table : null;

                if (item.type != Item.Type.Brick) {
                    throw new ScriptRuntimeException(item.id + " isn't a brick, it can't be placed");
                }
                int rotation = options != null && options.Get("rotation").Type == DataType.Number ? ((int)options.Get("rotation").Number % 4 + 4) % 4 : 0;
                int color = item.color;
                if (options != null && options.Get("color").Type == DataType.Number) {
                    color = (int)options.Get("color").Number;
                    if (!item.AllowsColor(color)) {
                        throw new ScriptRuntimeException(item.id + " can't have the colour " + color);
                    }
                }
                BrickPlacement placement = new BrickPlacement(item.brickModel, cell, rotation);
                if (!IsLoaded(cell) || !BrickGrid.IsFree(placement)) {
                    return DynValue.Nil;
                }
                Brick brick = WorldNetwork.ServerPlaceBrick(item, color, placement, default(Placer));
                return DynValue.FromObject(script, BrickHandle.ForBrick(brick, default(Placer)));
            });

            world["brickAt"] = DynValue.NewCallback((context, args) => {
                Vector3Int cell = LuaVector.Read(args[0], "brickAt's position");
                Brick brick = BrickGrid.GetBrickAt(cell);
                if (brick != null) {
                    return DynValue.FromObject(script, BrickHandle.ForBrick(brick, WorldNetwork.ServerPlacerOf(brick)));
                }
                if (WorldBehaviour.Instance == null || !IsLoaded(cell)) {
                    return DynValue.Nil;
                }
                Vector3Int block = BrickGrid.CellToBlock(cell);
                BlockType type = WorldBehaviour.Instance.GetBlockType(block);
                if (BlockDatabase.Get(type).itemId == null) {
                    return DynValue.Nil; // air, or what isn't loaded
                }
                return DynValue.FromObject(script, BrickHandle.ForBlock(block, type, WorldBehaviour.Instance.GetBlockColor(block), WorldNetwork.ServerPlacerOf(block)));
            });

            world["getBlock"] = DynValue.NewCallback((context, args) => {
                Vector3Int cell = LuaVector.Read(args[0], "getBlock's position");
                if (WorldBehaviour.Instance == null || !IsLoaded(cell)) {
                    return DynValue.Nil;
                }
                string itemId = BlockDatabase.Get(WorldBehaviour.Instance.GetBlockType(BrickGrid.CellToBlock(cell))).itemId;
                return itemId != null ? DynValue.NewString(itemId) : DynValue.Nil;
            });

            world["setBlock"] = DynValue.NewCallback((context, args) => {
                Vector3Int cell = LuaVector.Read(args[0], "setBlock's position");
                if (WorldBehaviour.Instance == null || !IsLoaded(cell)) {
                    return DynValue.False;
                }
                Vector3Int block = BrickGrid.CellToBlock(cell);
                if (args.Count < 2 || args[1].IsNil()) {
                    return DynValue.NewBoolean(WorldNetwork.ServerSetBlock(block, BlockType.Air, null));
                }
                Item item = ItemOf(args.AsType(1, "setBlock", DataType.String).String);
                if (!item.blockType.HasValue) {
                    throw new ScriptRuntimeException(item.id + " has no world block");
                }
                // loose bricks in the block's place would end up inside it
                foreach (Vector3Int blockCell in new BrickPlacement(Server.brickModels[3003], BrickGrid.BlockToCell(block)).Cells) {
                    if (BrickGrid.GetBrickAt(blockCell) != null) {
                        return DynValue.False;
                    }
                }
                int color = BrickColor.None;
                if (args.Count > 2 && args[2].Type == DataType.Number) {
                    color = (int)args[2].Number;
                    if (!item.AllowsColor(color)) {
                        throw new ScriptRuntimeException(item.id + " can't have the colour " + color);
                    }
                    if (color == item.color) {
                        color = BrickColor.None; // drawn with its textures
                    }
                }
                return DynValue.NewBoolean(WorldNetwork.ServerSetBlock(block, item.blockType.Value, null, color));
            });

            world["bricksIn"] = DynValue.NewCallback((context, args) => {
                Vector3Int a = LuaVector.Read(args[0], "bricksIn's first corner");
                Vector3Int b = LuaVector.Read(args[1], "bricksIn's second corner");
                Vector3Int min = Vector3Int.Min(a, b), max = Vector3Int.Max(a, b);
                Table found = new Table(script);
                foreach (Brick brick in Server.bricks.Values) {
                    Vector3Int o = brick.placement.origin;
                    if (o.x >= min.x && o.x <= max.x && o.y >= min.y && o.y <= max.y && o.z >= min.z && o.z <= max.z) {
                        found.Append(DynValue.FromObject(script, BrickHandle.ForBrick(brick, WorldNetwork.ServerPlacerOf(brick))));
                    }
                }
                return DynValue.NewTable(found);
            });

            world["players"] = DynValue.NewCallback((context, args) => {
                Table players = new Table(script);
                foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                    ConnectedPlayer player = conn.authenticationData as ConnectedPlayer;
                    if (player != null && player.hasJoined && !player.hasLeft) {
                        players.Append(DynValue.FromObject(script, PlayerHandle.For(conn)));
                    }
                }
                return DynValue.NewTable(players);
            });

            world["isLoaded"] = DynValue.NewCallback((context, args) => {
                return DynValue.NewBoolean(IsLoaded(LuaVector.Read(args[0], "isLoaded's position")));
            });

            return world;
        }

        private static Table createTimer(Script script, ModInfo mod) {
            Table timer = new Table(script);

            timer["after"] = DynValue.NewCallback((context, args) => {
                return DynValue.NewNumber(addTimer(script, mod, args, false));
            });
            timer["every"] = DynValue.NewCallback((context, args) => {
                return DynValue.NewNumber(addTimer(script, mod, args, true));
            });
            timer["cancel"] = DynValue.NewCallback((context, args) => {
                int id = (int)args.AsType(0, "cancel", DataType.Number).Number;
                return DynValue.NewBoolean(timers.RemoveAll(t => t.id == id && t.script == script) > 0);
            });
            return timer;
        }

        private static int addTimer(Script script, ModInfo mod, CallbackArguments args, bool repeat) {
            double seconds = args.AsType(0, repeat ? "every" : "after", DataType.Number).Number;
            DynValue function = args.AsType(1, repeat ? "every" : "after", DataType.Function);

            if (timers.FindAll(t => t.script == script).Count >= MaxTimers) {
                throw new ScriptRuntimeException("A mod can have up to " + MaxTimers + " timers");
            }
            // MinInterval is a float, 0.05 written in Lua is a bit less than it
            if (repeat && seconds < MinInterval - 0.0001) {
                throw new ScriptRuntimeException("timer.every runs at most every " + MinInterval.ToString(System.Globalization.CultureInfo.InvariantCulture) + " seconds");
            }
            Timer timer = new Timer() {
                id = nextTimerId++,
                script = script,
                mod = mod.id,
                function = function,
                due = Time.timeAsDouble + System.Math.Max(0, seconds),
                interval = repeat ? seconds : 0,
            };
            timers.Add(timer);
            return timer.id;
        }
    }
}
