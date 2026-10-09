using Brickcraft.Mods;
using MoonSharp.Interpreter;
using UnityEngine;

namespace Brickcraft.Scripting
{
    /// <summary>What scripts can call, installed in each mod's Lua state (see ModScripts).</summary>
    public static class LuaApi
    {
        public static void Install(Script script, ModInfo mod) {
            // log(...) writes to the server log, like print
            script.Globals["log"] = DynValue.NewCallback((context, args) => {
                string[] parts = new string[args.Count];
                for (int i = 0; i < args.Count; i++) {
                    parts[i] = args[i].ToPrintString();
                }
                Debug.Log("[" + mod.id + "] " + string.Join(" ", parts));
                return DynValue.Nil;
            });
        }

        /// <summary>Forgets what scripts left running, when they stop.</summary>
        public static void Reset() {
        }
    }
}
