using Brickcraft.Network;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>A mod's own chat command (see Scripting.LuaApi and Network.ChatCommands).</summary>
    public class ModCommandTests
    {
        [SetUp]
        public void SetUp() {
            Scripting.LuaApi.Reset();
        }

        [TearDown]
        public void TearDown() {
            Scripting.LuaApi.Reset();
        }

        [Test]
        public void AModAddsItsOwnCommand() {
            MoonSharp.Interpreter.Script script = new MoonSharp.Interpreter.Script(MoonSharp.Interpreter.CoreModules.Preset_SoftSandbox);
            script.Globals["shared"] = new MoonSharp.Interpreter.Table(script);
            Scripting.LuaApi.Install(script, new Mods.ModInfo() { id = "test_mod", name = "Test mod" });
            script.DoString("chat.command('heal', '/heal AMOUNT', function(player, args) shared.healed = args end)");

            Assert.IsFalse(ChatCommands.Knows("heal"), "the game's own list doesn't change");
            CollectionAssert.Contains(Scripting.LuaApi.CommandUsages(), "/heal AMOUNT");
            Assert.IsFalse(Scripting.LuaApi.TryCommand("kick", null, ""), "kick is the game's");
            Assert.IsTrue(Scripting.LuaApi.TryCommand("heal", null, "5"));
            Assert.AreEqual("5", script.Globals.Get("shared").Table.Get("healed").String);
        }

        [Test]
        public void ACommandTheGameHasCantBeTaken() {
            MoonSharp.Interpreter.Script script = new MoonSharp.Interpreter.Script(MoonSharp.Interpreter.CoreModules.Preset_SoftSandbox);
            Scripting.LuaApi.Install(script, new Mods.ModInfo() { id = "test_mod", name = "Test mod" });

            Assert.Throws<MoonSharp.Interpreter.ScriptRuntimeException>(() =>
                script.DoString("chat.command('kick', '/kick', function() end)"));
        }
    }
}