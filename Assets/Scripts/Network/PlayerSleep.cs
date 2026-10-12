using System.Collections.Generic;
using Brickcraft.Combat;
using Brickcraft.World;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>Server to clients: how many players are asleep, out of how many, and whether the night is being skipped.</summary>
    public struct SleepStatusMessage : NetworkMessage
    {
        public int sleeping;
        public int players;
        public bool isSkipping;
    }

    /// <summary>
    /// Players sleeping in beds (bricks of items tagged "bed", see Item.tags), on the server. A click on a bed
    /// at night (18:00 to 06:00) lies the player in it, unless someone else is in it or something is after
    /// them. When at least half the players are asleep, after SkipDelay seconds the night is skipped: the
    /// time goes to 06:00 and everyone wakes up. Being hurt, the bed going away, morning or asking to stop
    /// (PlayerNetwork.CmdStopSleeping) wakes a player. Clients get SleepStatusMessage when the counts change.
    ///
    /// The bed a player sleeps in becomes its spawn point: it comes back beside it when it dies (see
    /// PlayerNetwork.CmdRespawn), until it sleeps in another or the bed goes away (BedRemoved).
    /// </summary>
    public static class PlayerSleep
    {
        public const float RequiredShare = 0.5f;
        public const float SkipDelay = 3;
        public const float Dawn = 6;
        // something after the player this close keeps it awake
        private const float DangerDistance = 16;

        private static float skipAt = -1;
        private static SleepStatusMessage lastStatus;

        public static void Start() {
            skipAt = -1;
            lastStatus = new SleepStatusMessage() { players = -1 };
            Health.ServerAnyDamaged += onAnyDamaged;
        }

        public static void Stop() {
            Health.ServerAnyDamaged -= onAnyDamaged;
        }

        /// <summary>How many players it takes to skip the night with that many in the game.</summary>
        public static int Needed(int players) {
            return Mathf.Max(1, Mathf.CeilToInt(players * RequiredShare));
        }

        /// <summary>Why the player can't sleep in that bed now, null if it can.</summary>
        public static string WhyNot(PlayerNetwork player, Brick bed) {
            if (!Npcs.NpcBeds.IsBed(bed)) {
                return "That isn't a bed";
            }
            if (WorldTime.IsDay) {
                return "You can only sleep at night";
            }
            if (IsTakenByPlayer(bed.id, player)) {
                return "Someone is sleeping in this bed";
            }
            foreach (Npcs.Npc npc in Npcs.NpcSystem.All) {
                if (npc.isLying && npc.BedId == bed.id) {
                    return npc.Info.name + " is sleeping in this bed";
                }
                if (npc.Brain != null && npc.Brain.Target == player.GetComponent<Health>()
                        && Vector3.Distance(npc.transform.position, player.transform.position) <= DangerDistance) {
                    return "You can't sleep now, something is after you";
                }
            }
            return null;
        }

        /// <summary>Another player sleeps in that bed.</summary>
        public static bool IsTakenByPlayer(string bedId, PlayerNetwork except = null) {
            foreach (PlayerNetwork player in players()) {
                if (player != except && player.sleepingIn == bedId) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Lies the player in the bed: on its SleepPoint (feet there, head the other way from where it faces).</summary>
        public static void Sleep(PlayerNetwork player, Brick bed) {
            Transform point = Npcs.NpcBeds.SleepPointOf(bed);
            player.sleepingIn = bed.id;
            player.ServerLieDown(point.position, point.rotation);
            setSpawn(player, bed);
        }

        // where it stands up beside the bed is where it comes back
        private static void setSpawn(PlayerNetwork player, Brick bed) {
            if (!(player.connectionToClient.authenticationData is ConnectedPlayer connected) || bed.gameObject == null
                    || !Npcs.NpcSystem.SpotNear(bed.gameObject.transform, 0.35f, 1.8f, out Vector3 spot)) {
                return;
            }
            bool isNew = connected.record.SpawnBed != bed.id;
            FloatingOrigin.ToAbsolute(spot + Vector3.up * 0.9f, out double x, out double y, out double z); // the player's position is its middle
            BrickcraftNetworkManager.Instance.Database.SetSpawnBed(connected.record, bed.id, x, y, z);
            if (isNew) {
                player.connectionToClient.Send(new ChatMessage() { sender = ChatCommands.ServerName, text = "Spawn point set: you'll come back to this bed" });
            }
        }

        /// <summary>A bed went away (broken, moved, a script): whoever had their spawn point there loses it.</summary>
        public static void BedRemoved(Brick bed) {
            BrickcraftNetworkManager.Instance.Database.ClearSpawnBed(bed.id);
            // the players in the game have their record in memory (written back whole): theirs too
            foreach (PlayerNetwork player in players()) {
                ConnectedPlayer connected = (ConnectedPlayer)player.connectionToClient.authenticationData;
                if (connected.record.SpawnBed == bed.id) {
                    connected.record.SpawnBed = null;
                    connected.record.SpawnX = connected.record.SpawnY = connected.record.SpawnZ = null;
                    player.connectionToClient.Send(new ChatMessage() { sender = ChatCommands.ServerName, text = "Your bed is gone: you lost your spawn point" });
                }
            }
        }

        /// <summary>Where the player comes back when it dies: beside its bed (local position), false for the world's spawn.</summary>
        public static bool SpawnPoint(PlayerNetwork player, out Vector3 position) {
            position = Vector3.zero;
            if (!(player.connectionToClient.authenticationData is ConnectedPlayer connected) || !connected.record.HasSpawnBed) {
                return false;
            }
            PlayerRecord record = connected.record;
            Vector3 spot = FloatingOrigin.ToLocal(record.SpawnX.Value, record.SpawnY.Value, record.SpawnZ.Value);
            // its bed's part of the world is there and the bed isn't: it went away some other way
            if (WorldBehaviour.Instance != null && WorldBehaviour.Instance.IsWalkable(WorldBehaviour.ChunkAt(spot)) && !Server.bricks.ContainsKey(record.SpawnBed)) {
                BrickcraftNetworkManager.Instance.Database.ClearSpawnBed(record);
                return false;
            }
            position = spot;
            return true;
        }

        /// <summary>Gets the player up, beside its bed.</summary>
        public static void Wake(PlayerNetwork player) {
            if (string.IsNullOrEmpty(player.sleepingIn)) {
                return;
            }
            Server.bricks.TryGetValue(player.sleepingIn, out Brick bed);
            player.sleepingIn = "";
            Transform beside = bed != null && bed.gameObject != null ? bed.gameObject.transform : player.transform;
            if (Npcs.NpcSystem.SpotNear(beside, 0.35f, 1.8f, out Vector3 spot)) {
                player.ServerTeleport(spot + Vector3.up * 0.9f); // the player's position is its middle
            }
        }

        public static void Update() {
            int sleeping = 0, total = 0;
            foreach (PlayerNetwork player in players()) {
                total++;
                if (string.IsNullOrEmpty(player.sleepingIn)) {
                    continue;
                }
                // morning, a bed that went away, or death
                if (WorldTime.IsDay || !Server.bricks.ContainsKey(player.sleepingIn) || player.GetComponent<Health>().isDead) {
                    Wake(player);
                    continue;
                }
                sleeping++;
            }

            bool enough = sleeping > 0 && sleeping >= Needed(total);
            if (!enough) {
                skipAt = -1;
            } else if (skipAt < 0) {
                skipAt = Time.unscaledTime + SkipDelay;
            } else if (Time.unscaledTime >= skipAt) {
                skipAt = -1;
                WorldNetwork.ServerSetTime(WorldTime.NextTimeAt(Dawn));
                foreach (PlayerNetwork player in players()) {
                    Wake(player);
                }
                NetworkServer.SendToReady(new ChatMessage() { sender = ChatCommands.ServerName, text = "The night was slept through. Good morning!" });
                sleeping = 0;
            }

            SleepStatusMessage status = new SleepStatusMessage() { sleeping = sleeping, players = total, isSkipping = skipAt >= 0 };
            if (status.sleeping != lastStatus.sleeping || status.players != lastStatus.players || status.isSkipping != lastStatus.isSkipping) {
                lastStatus = status;
                NetworkServer.SendToReady(status);
            }
        }

        /// <summary>A player who joined gets the counts.</summary>
        public static void SendStatus(NetworkConnectionToClient conn) {
            if (lastStatus.players >= 0) {
                conn.Send(lastStatus);
            }
        }

        // being hurt wakes a sleeper
        private static void onAnyDamaged(Health victim, DamageInfo damage) {
            PlayerNetwork player = victim.GetComponent<PlayerNetwork>();
            if (player != null) {
                Wake(player);
            }
        }

        // the players in the game
        private static IEnumerable<PlayerNetwork> players() {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn.identity != null && conn.authenticationData is ConnectedPlayer connected && connected.hasJoined && !connected.hasLeft) {
                    PlayerNetwork player = conn.identity.GetComponent<PlayerNetwork>();
                    if (player != null) {
                        yield return player;
                    }
                }
            }
        }
    }
}
