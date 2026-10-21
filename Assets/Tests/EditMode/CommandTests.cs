using Brickcraft.Network;
using NUnit.Framework;

namespace Brickcraft.Tests
{
    /// <summary>Chat commands: what the game has (the mods' own are in ModCommandTests, which can run Lua).</summary>
    public class CommandTests
    {
        [SetUp]
        public void SetUp() {
            GameData.Load();
        }

        [Test]
        public void TheGameKnowsItsCommands() {
            foreach (string name in new[] { "players", "kick", "ban", "role", "additem", "tp", "pvp", "npcs", "time",
                    "daylength", "spawnnpc", "removenpcs", "navdebug", "unban", "closest", "reposition" }) {
                Assert.IsTrue(ChatCommands.Knows(name), name);
            }
            Assert.IsFalse(ChatCommands.Knows("dragon"));
        }
    }
}