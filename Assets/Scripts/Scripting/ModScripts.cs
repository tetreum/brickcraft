using System;
using System.Collections.Generic;
using System.IO;
using Brickcraft.Mods;
using MoonSharp.Interpreter;
using UnityEngine;

namespace Brickcraft.Scripting
{
    /// <summary>
    /// Runs the Lua scripts of the active mods, on the server only: the server owns the world, so what
    /// scripts do goes through it and reaches the players like any other change (and players can't
    /// cheat with them).
    ///
    /// Each mod has its own Lua state, sandboxed (no files, no loading code, no os but the time).
    /// Its scripts are:
    ///   items/[item]/script.lua   the item's events: onPlaced, onHit, onBroken, onInteract
    ///   scripts/*.lua             the mod's events: onLoad, onPlayerJoined, onPlayerLeft, onChat
    /// Each file has its own globals (so two items can both have an onPlaced); the mod's shared ones,
    /// like the table "shared" and the API (see LuaApi), are read through them.
    ///
    /// Every call gets an instruction budget: a script stuck in a loop is stopped and logged, and a
    /// handler that keeps failing is turned off. Errors never reach the game.
    /// </summary>
    public static class ModScripts
    {
        public const string ItemScriptFile = "script.lua";
        public const string ScriptsFolder = "scripts";

        /// <summary>Lua instructions a single event call can take.</summary>
        public const int InstructionBudget = 1000000;
        /// <summary>Failures after which a handler is turned off.</summary>
        public const int MaxFailures = 5;

        private class ScriptFile
        {
            public ModInfo mod;
            public Script script;
            public Table env;
            public string path;
            public readonly Dictionary<string, int> failures = new Dictionary<string, int>();
        }

        private static readonly Dictionary<string, Script> scripts = new Dictionary<string, Script>();
        private static readonly Dictionary<string, ScriptFile> itemScripts = new Dictionary<string, ScriptFile>();
        private static readonly List<ScriptFile> modScripts = new List<ScriptFile>();

        public static bool IsRunning { get; private set; }

        static ModScripts() {
            UserData.RegisterAssembly(typeof(ModScripts).Assembly);
        }

        /// <summary>Loads the scripts of the active mods (see ModDatabase.Activate) and calls their onLoad.</summary>
        public static void Start() {
            Stop();
            IsRunning = true;

            foreach (ModInfo mod in ModDatabase.Active) {
                Script script = createScript(mod);
                scripts[mod.id] = script;

                if (Directory.Exists(mod.ItemsFolder)) {
                    foreach (string itemFolder in Directory.GetDirectories(mod.ItemsFolder)) {
                        string file = Path.Combine(itemFolder, ItemScriptFile);
                        string itemId = mod.id + ":" + Path.GetFileName(itemFolder);
                        if (File.Exists(file) && Server.items.ContainsKey(itemId)) {
                            ScriptFile loaded = load(mod, script, file);
                            if (loaded != null) {
                                itemScripts[itemId] = loaded;
                            }
                        }
                    }
                }
                string folder = Path.Combine(mod.folder, ScriptsFolder);
                if (Directory.Exists(folder)) {
                    string[] files = Directory.GetFiles(folder, "*.lua");
                    Array.Sort(files, StringComparer.Ordinal);
                    foreach (string file in files) {
                        ScriptFile loaded = load(mod, script, file);
                        if (loaded != null) {
                            modScripts.Add(loaded);
                        }
                    }
                }
            }
            ModEvent("onLoad");
        }

        public static void Stop() {
            IsRunning = false;
            scripts.Clear();
            itemScripts.Clear();
            modScripts.Clear();
            LuaApi.Reset();
        }

        /// <summary>Whether the item has a script with that event, to skip building its arguments.</summary>
        public static bool HasItemEvent(string itemId, string handler) {
            ScriptFile file;
            return IsRunning && itemId != null && itemScripts.TryGetValue(itemId, out file) && file.env.Get(handler).Type == DataType.Function;
        }

        /// <summary>
        /// Calls an event of the item's script. Returns false only if the script returned false (to
        /// cancel what's happening), true otherwise: no script, no handler, an error...
        /// </summary>
        public static bool ItemEvent(string itemId, string handler, params object[] args) {
            ScriptFile file;
            if (!IsRunning || itemId == null || !itemScripts.TryGetValue(itemId, out file)) {
                return true;
            }
            DynValue result = call(file, handler, args);
            return !(result != null && result.Type == DataType.Boolean && !result.Boolean);
        }

        /// <summary>Calls an event of every mod script. False if any returned false.</summary>
        public static bool ModEvent(string handler, params object[] args) {
            bool allowed = true;
            if (!IsRunning) {
                return true;
            }
            foreach (ScriptFile file in modScripts.ToArray()) {
                DynValue result = call(file, handler, args);
                if (result != null && result.Type == DataType.Boolean && !result.Boolean) {
                    allowed = false;
                }
            }
            return allowed;
        }

        /// <summary>Calls a Lua function a script handed to the API (timers), with the same protections.</summary>
        public static void CallFunction(Script script, DynValue function, string what, params object[] args) {
            run(script, function, what, args);
        }

        /// <summary>The mod a Lua state belongs to, for the API's messages.</summary>
        public static string ModOf(Script script) {
            foreach (KeyValuePair<string, Script> entry in scripts) {
                if (entry.Value == script) {
                    return entry.Key;
                }
            }
            return "?";
        }

        private static Script createScript(ModInfo mod) {
            Script script = new Script(CoreModules.Preset_SoftSandbox);
            script.Options.DebugPrint = text => Debug.Log("[" + mod.id + "] " + text);
            script.Globals["shared"] = new Table(script);
            LuaApi.Install(script, mod);
            return script;
        }

        private static ScriptFile load(ModInfo mod, Script script, string file) {
            Table env = new Table(script);
            Table meta = new Table(script);
            meta["__index"] = script.Globals;
            env.MetaTable = meta;

            ScriptFile loaded = new ScriptFile() { mod = mod, script = script, env = env, path = relativePath(mod, file) };
            try {
                DynValue chunk = script.LoadString(File.ReadAllText(file), env, loaded.path);
                if (run(script, chunk, loaded.path) == null) {
                    return null;
                }
            } catch (InterpreterException e) {
                Debug.LogError("[" + mod.id + "] " + (e.DecoratedMessage ?? e.Message));
                return null;
            }
            return loaded;
        }

        private static DynValue call(ScriptFile file, string handler, object[] args) {
            DynValue function = file.env.Get(handler);
            if (function.Type != DataType.Function) {
                return null;
            }
            int failures;
            file.failures.TryGetValue(handler, out failures);
            if (failures >= MaxFailures) {
                return null;
            }
            DynValue result = run(file.script, function, file.path + " " + handler, args);
            if (result == null) {
                file.failures[handler] = failures + 1;
                if (failures + 1 == MaxFailures) {
                    Debug.LogError("[" + file.mod.id + "] " + file.path + " " + handler + " failed " + MaxFailures + " times, it's turned off");
                }
            }
            return result;
        }

        // runs a function as a coroutine that yields when it uses up its budget; null if it failed
        private static DynValue run(Script script, DynValue function, string what, params object[] args) {
            try {
                DynValue coroutine = script.CreateCoroutine(function);
                coroutine.Coroutine.AutoYieldCounter = InstructionBudget;
                DynValue result = coroutine.Coroutine.Resume(args);

                if (coroutine.Coroutine.State == CoroutineState.ForceSuspended) {
                    Debug.LogError("[" + ModOf(script) + "] " + what + " took too long (more than " + InstructionBudget + " instructions), it was stopped");
                    return null;
                }
                return result;
            } catch (InterpreterException e) {
                Debug.LogError("[" + ModOf(script) + "] " + what + ": " + (e.DecoratedMessage ?? e.Message));
            } catch (Exception e) {
                Debug.LogError("[" + ModOf(script) + "] " + what + ": " + e.Message);
            }
            return null;
        }

        private static string relativePath(ModInfo mod, string file) {
            return file.Substring(mod.folder.Length).TrimStart('\\', '/').Replace('\\', '/');
        }
    }
}
