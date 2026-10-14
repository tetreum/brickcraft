using System;

namespace Brickcraft.Network
{
    /// <summary>
    /// Who a player joining a server is (see BrickcraftAuthenticator): players are their machine, whatever their name.
    /// A machine joins as its player (the one that joined most), renamed if it asks for another name; a name of
    /// another machine's player is refused; a new machine gets a new player. Banned players, and machines a banned
    /// player played from, are refused (admins of the machine aside). Decided from what the database says, which
    /// it doesn't change: the authenticator does what it says.
    /// </summary>
    public static class JoinRules
    {
        public struct Decision
        {
            /// <summary>Why it can't join, empty if it can.</summary>
            public GameText error;
            /// <summary>The player it joins as, null for a new one (create it).</summary>
            public PlayerRecord player;
            /// <summary>Its player has another name: it's renamed to the one asked for.</summary>
            public bool rename;
            /// <summary>Another player of the machine (from an older save) has the name asked for: it gets another first.</summary>
            public PlayerRecord moveAside;
        }

        /// <param name="name">The name it asks for.</param>
        /// <param name="machineIdHash">Its machine (see PlayerIdentity.HashMachineId).</param>
        /// <param name="named">The player with that name, null if there's none.</param>
        /// <param name="machinePlayer">The machine's player (see GameDatabase.FindPlayerByMachine), null if there's none.</param>
        /// <param name="machineBanned">A banned player played from that machine.</param>
        /// <param name="isOnline">Whether the player with that id is playing.</param>
        public static Decision Decide(string name, string machineIdHash, PlayerRecord named, PlayerRecord machinePlayer,
                bool machineBanned, Func<int, bool> isOnline) {
            // the machine's player, whatever its name; the name's if it's another machine's
            PlayerRecord player = named != null && named.TokenHash != machineIdHash ? named : machinePlayer;
            // admins are never locked out by the ban of someone using the same machine
            bool isAdmin = player != null && player.IsAdmin && player.TokenHash == machineIdHash;

            if ((player != null && player.Banned) || (!isAdmin && machineBanned)) {
                if (player != null && !string.IsNullOrEmpty(player.BanReason)) {
                    return new Decision() { error = GameText.Of("join.banned_reason", player.BanReason) };
                }
                return new Decision() { error = GameText.Of("join.banned") };
            }
            if (player == null) {
                return new Decision();
            }
            if (player.TokenHash != machineIdHash) {
                return new Decision() { error = GameText.Of("join.name_taken", player.Name) };
            }
            if (isOnline(player.Id)) {
                return new Decision() { error = GameText.Of("join.already_playing", player.Name) };
            }
            Decision decision = new Decision() { player = player };
            if (player.Name != name) {
                decision.rename = true;
                if (named != null && named.Id != player.Id) {
                    decision.moveAside = named;
                }
            }
            return decision;
        }
    }
}
