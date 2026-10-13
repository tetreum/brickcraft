using Brickcraft.Network;
using NUnit.Framework;

namespace Brickcraft.Tests
{
    /// <summary>Who a joining player is (JoinRules): players are their machine, whatever their name.</summary>
    public class JoinRulesTests
    {
        private const string Mine = "my-machine";
        private const string Theirs = "their-machine";

        private static PlayerRecord player(int id, string name, string machine, string role = PlayerRoles.User) {
            return new PlayerRecord() { Id = id, Name = name, NameKey = name.ToLowerInvariant(), TokenHash = machine, Role = role };
        }

        private static bool nobodyOnline(int id) {
            return false;
        }

        [Test]
        public void ANewMachineGetsANewPlayer() {
            JoinRules.Decision decision = JoinRules.Decide("Newbie", Mine, null, null, false, nobodyOnline);

            Assert.IsNull(decision.error);
            Assert.IsNull(decision.player, "created by the authenticator");
        }

        [Test]
        public void AMachineJoinsAsItsPlayer() {
            PlayerRecord me = player(1, "Me", Mine);
            JoinRules.Decision decision = JoinRules.Decide("Me", Mine, me, me, false, nobodyOnline);

            Assert.IsNull(decision.error);
            Assert.AreSame(me, decision.player);
            Assert.IsFalse(decision.rename);
        }

        [Test]
        public void AnotherNameRenamesItsPlayer() {
            PlayerRecord me = player(1, "Me", Mine);
            JoinRules.Decision decision = JoinRules.Decide("NewName", Mine, null, me, false, nobodyOnline);

            Assert.IsNull(decision.error);
            Assert.AreSame(me, decision.player);
            Assert.IsTrue(decision.rename);
            Assert.IsNull(decision.moveAside);
        }

        [Test]
        public void AnotherMachinesNameIsRefused() {
            PlayerRecord them = player(2, "Them", Theirs);
            JoinRules.Decision decision = JoinRules.Decide("Them", Mine, them, player(1, "Me", Mine), false, nobodyOnline);

            StringAssert.Contains("belongs to someone else", decision.error);
        }

        [Test]
        public void AnotherPlayerOfTheMachineWithTheNameMovesAside() {
            PlayerRecord me = player(1, "Me", Mine);
            PlayerRecord old = player(3, "Old", Mine); // from a save before players were their machine
            JoinRules.Decision decision = JoinRules.Decide("Old", Mine, old, me, false, nobodyOnline);

            Assert.IsNull(decision.error);
            Assert.AreSame(me, decision.player, "the machine's player, not the old one");
            Assert.IsTrue(decision.rename);
            Assert.AreSame(old, decision.moveAside);
        }

        [Test]
        public void APlayerAlreadyPlayingIsRefused() {
            PlayerRecord me = player(1, "Me", Mine);
            JoinRules.Decision decision = JoinRules.Decide("Me", Mine, me, me, false, id => id == 1);

            StringAssert.Contains("already playing", decision.error);
        }

        [Test]
        public void ABannedPlayerIsRefusedWithTheReason() {
            PlayerRecord me = player(1, "Me", Mine);
            me.Banned = true;
            me.BanReason = "griefing";
            JoinRules.Decision decision = JoinRules.Decide("Me", Mine, me, me, true, nobodyOnline);

            StringAssert.Contains("banned", decision.error);
            StringAssert.Contains("griefing", decision.error);
        }

        [Test]
        public void ABannedMachineIsRefused() {
            JoinRules.Decision decision = JoinRules.Decide("Newbie", Mine, null, null, true, nobodyOnline);

            StringAssert.Contains("banned", decision.error);
        }

        [Test]
        public void TheMachinesAdminIsntLockedOutByItsBan() {
            PlayerRecord admin = player(1, "Admin", Mine, PlayerRoles.Admin);
            JoinRules.Decision decision = JoinRules.Decide("Admin", Mine, admin, admin, true, nobodyOnline);

            Assert.IsNull(decision.error);
            Assert.AreSame(admin, decision.player);
        }
    }
}
