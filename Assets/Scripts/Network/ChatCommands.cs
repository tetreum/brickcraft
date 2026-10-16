using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Brickcraft.Bricks;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>Server to client: why the server is about to disconnect it.</summary>
    public struct DisconnectReasonMessage : NetworkMessage
    {
        public GameText reason;
    }

    /// <summary>
    /// Chat messages starting with "/" are commands, run on the server:
    ///   /players                    online players and their ids
    ///   /kick NICK|ID [reason]      disconnects an online player
    ///   /ban NICK|ID [reason]       bans a player (and its machine), online or not
    ///   /unban NICK|ID
    ///   /tp NICK|ID                 takes you to an online player
    ///   /pvp [on|off]               whether players can hurt each other, saved with the world
    ///   /npcs [on|off]              whether NPCs appear by themselves (off takes them away, but pets), saved with the world
    ///   /time [day|noon|night|midnight|HH:MM]   shows the game's time, or moves it on to that hour
    ///   /daylength [minutes]        shows or changes how long a day lasts, saved with the world
    ///   /spawnnpc NPC [count]       puts NPCs in front of you (several are a pack)
    ///   /removenpcs                 takes every NPC out of the world
    ///   /navdebug                   draws where NPCs can walk and their paths in the editor's scene view
    ///   /role NICK|ID [role]        shows or changes a player's role (user, admin)
    ///   /additem ITEM[@COLOR] [count] [NICK|ID]   gives items to a player (yourself by default), online or not
    ///   /reposition                 puts the vehicle you're sitting in back on its wheels, above the ground
    /// NICK|ID is a player name (any case) or, if no name matches, a player id.
    /// Only admins can use them, but /reposition.
    /// </summary>
    public static class ChatCommands
    {
        public const string ServerName = "Server";

        private const float DisconnectDelay = 0.5f;
        private const int MaxAddedItems = 10000;

        public static bool IsCommand(string text) {
            return text.StartsWith("/");
        }

        public static void Run(NetworkConnectionToClient sender, string text) {
            ConnectedPlayer admin = sender.authenticationData as ConnectedPlayer;
            string[] parts = text.Substring(1).Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
            string target = parts.Length > 1 ? parts[1] : null;
            string reason = parts.Length > 2 ? parts[2].Trim() : null;

            // anyone in a vehicle
            if (command == "reposition") {
                PlayerNetwork player = sender.identity != null ? sender.identity.GetComponent<PlayerNetwork>() : null;
                if (player == null || !Vehicles.VehicleSystem.ServerReposition(player)) {
                    reply(sender, "vehicle.reposition_not_seated");
                }
                return;
            }

            if (admin == null || !admin.record.IsAdmin) {
                reply(sender, "cmd.only_admins");
                return;
            }

            switch (command) {
                case "players":
                    listPlayers(sender);
                    break;
                case "kick":
                    if (target == null) {
                        reply(sender, "cmd.usage_kick");
                    } else {
                        kick(sender, admin, target, reason);
                    }
                    break;
                case "ban":
                    if (target == null) {
                        reply(sender, "cmd.usage_ban");
                    } else {
                        ban(sender, admin, target, reason);
                    }
                    break;
                case "role":
                    if (target == null) {
                        reply(sender, "cmd.usage_role", PlayerRoles.User, PlayerRoles.Admin);
                    } else {
                        role(sender, admin, target, reason);
                    }
                    break;
                case "additem":
                    if (target == null) {
                        reply(sender, "cmd.usage_additem");
                    } else {
                        addItem(sender, admin, target, reason);
                    }
                    break;
                case "tp":
                    if (target == null) {
                        reply(sender, "cmd.usage_tp");
                    } else {
                        teleport(sender, admin, target);
                    }
                    break;
                case "pvp":
                    pvp(sender, admin, target);
                    break;
                case "npcs":
                    npcs(sender, admin, target);
                    break;
                case "time":
                    time(sender, admin, target);
                    break;
                case "daylength":
                    dayLength(sender, admin, target);
                    break;
                case "spawnnpc":
                    if (target == null) {
                        reply(sender, "cmd.usage_spawnnpc");
                    } else {
                        spawnNpc(sender, target, reason);
                    }
                    break;
                case "removenpcs":
                    reply(sender, "cmd.removed_npcs", Npcs.NpcSystem.ServerRemoveAll());
                    break;
                case "navdebug":
                    Npcs.Navigation.NavGrid.DebugDraw = !Npcs.Navigation.NavGrid.DebugDraw;
                    reply(sender, Npcs.Navigation.NavGrid.DebugDraw ? "cmd.navdebug_on" : "cmd.navdebug_off");
                    break;
                case "unban":
                    if (target == null) {
                        reply(sender, "cmd.usage_unban");
                    } else {
                        unban(sender, target);
                    }
                    break;
                default:
                    reply(sender, "cmd.unknown");
                    break;
            }
        }

        private static void listPlayers(NetworkConnectionToClient sender) {
            // names and ids, the same in every language
            StringBuilder list = new StringBuilder();

            foreach (KeyValuePair<NetworkConnectionToClient, ConnectedPlayer> online in onlinePlayers()) {
                list.Append(list.Length > 0 ? " " : "").Append(online.Value.record.Name).Append(" (").Append(online.Value.record.Id).Append(')');
            }
            reply(sender, "cmd.online", list.ToString());
        }

        private static void kick(NetworkConnectionToClient sender, ConnectedPlayer admin, string target, string reason) {
            NetworkConnectionToClient conn = findOnline(target, out ConnectedPlayer player);

            if (conn == null) {
                reply(sender, "cmd.nobody_online", target);
                return;
            }
            GameText refusal = refuse(admin, player.record, conn);
            if (!refusal.IsEmpty) {
                GameTexts.Send(sender, refusal);
                return;
            }

            player.leaveAnnounced = true;
            ChatEvents.Send(new ChatEventMessage() { type = ChatEventType.Kicked, player = player.record.Name, by = admin.record.Name, reason = reason });
            disconnect(conn, string.IsNullOrEmpty(reason) ? GameText.Of("cmd.kicked_you", admin.record.Name) : GameText.Of("cmd.kicked_you_reason", admin.record.Name, reason));
        }

        private static void ban(NetworkConnectionToClient sender, ConnectedPlayer admin, string target, string reason) {
            NetworkConnectionToClient conn = findOnline(target, out ConnectedPlayer online);
            PlayerRecord player = online != null ? online.record : findRecord(target);

            if (player == null) {
                reply(sender, "cmd.no_player", target);
                return;
            }
            GameText refusal = refuse(admin, player, conn);
            if (!refusal.IsEmpty) {
                GameTexts.Send(sender, refusal);
                return;
            }
            if (player.TokenHash == admin.record.TokenHash) {
                reply(sender, "cmd.same_machine", player.Name);
                return;
            }
            if (player.Banned) {
                reply(sender, "cmd.already_banned", player.Name);
                return;
            }

            BrickcraftNetworkManager.Instance.Database.SetBanned(player, true, reason);
            if (online != null) {
                online.leaveAnnounced = true;
            }
            ChatEvents.Send(new ChatEventMessage() { type = ChatEventType.Banned, player = player.Name, by = admin.record.Name, reason = reason });
            Debug.Log(player.Name + " (" + player.Id + ") was banned by " + admin.record.Name + withReason(reason));

            if (conn != null) {
                disconnect(conn, string.IsNullOrEmpty(reason) ? GameText.Of("cmd.banned_you", admin.record.Name) : GameText.Of("cmd.banned_you_reason", admin.record.Name, reason));
            }
        }

        private static void unban(NetworkConnectionToClient sender, string target) {
            PlayerRecord player = findRecord(target);

            if (player == null) {
                reply(sender, "cmd.no_player", target);
                return;
            }
            if (!player.Banned) {
                reply(sender, "cmd.not_banned", player.Name);
                return;
            }
            BrickcraftNetworkManager.Instance.Database.SetBanned(player, false, null);
            reply(sender, "cmd.unbanned", player.Name);
        }

        private const int MaxSpawnedNpcs = 20;

        // a few meters in front of the admin, side by side
        private static void spawnNpc(NetworkConnectionToClient sender, string npcId, string countText) {
            Npcs.NpcInfo info = Npcs.NpcDatabase.Get(npcId.ToLowerInvariant());
            if (info == null) {
                List<string> ids = new List<string>();
                foreach (Npcs.NpcInfo npc in Npcs.NpcDatabase.All) {
                    ids.Add(npc.id);
                }
                reply(sender, "cmd.no_npc", npcId, string.Join(", ", ids));
                return;
            }
            int count = 1;
            if (countText != null && (!int.TryParse(countText, out count) || count < 1 || count > MaxSpawnedNpcs)) {
                reply(sender, "cmd.count_range", MaxSpawnedNpcs);
                return;
            }
            if (sender.identity == null) {
                return;
            }
            Transform player = sender.identity.transform;
            Vector3 forward = player.forward;
            forward.y = 0;
            forward = forward.sqrMagnitude > 0 ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            int spawned = 0;
            Npcs.Npc first = null;
            for (int i = 0; i < count; i++) {
                Vector3 position = player.position + forward * 4 + right * ((i - (count - 1) / 2f) * 2) + Vector3.up;
                Npcs.Npc npc = Npcs.NpcSystem.ServerSpawn(info, position, Quaternion.LookRotation(-forward).eulerAngles.y);
                if (npc != null) {
                    spawned++;
                    // spawned together, they're a pack
                    if (first == null) {
                        first = npc;
                    } else {
                        Npcs.NpcGroups.Join(npc, first);
                    }
                }
            }
            reply(sender, spawned < count ? "cmd.spawned_npcs_too_many" : "cmd.spawned_npcs", spawned, info);
        }

        private static void pvp(NetworkConnectionToClient sender, ConnectedPlayer admin, string value) {
            if (string.IsNullOrEmpty(value)) {
                reply(sender, "cmd.pvp_is", GameText.Of(WorldNetwork.Pvp ? "word.on" : "word.off"));
                return;
            }
            value = value.ToLowerInvariant();
            if (value != "on" && value != "off") {
                reply(sender, "cmd.usage_pvp");
                return;
            }
            WorldNetwork.Pvp = value == "on";
            Debug.Log(admin.record.Name + " turned PvP " + value);
            GameTexts.SendToAll(WorldNetwork.Pvp ? "cmd.pvp_turned_on" : "cmd.pvp_turned_off", admin.record.Name);
        }

        private static void npcs(NetworkConnectionToClient sender, ConnectedPlayer admin, string value) {
            if (string.IsNullOrEmpty(value)) {
                reply(sender, "cmd.npcs_is", GameText.Of(WorldNetwork.NpcsOn ? "word.on" : "word.off"));
                return;
            }
            value = value.ToLowerInvariant();
            if (value != "on" && value != "off") {
                reply(sender, "cmd.usage_npcs");
                return;
            }
            WorldNetwork.NpcsOn = value == "on";
            int removed = WorldNetwork.NpcsOn ? 0 : Npcs.NpcSystem.ServerRemoveWild();
            Debug.Log(admin.record.Name + " turned NPCs " + value);
            if (WorldNetwork.NpcsOn) {
                GameTexts.SendToAll("cmd.npcs_turned_on", admin.record.Name);
            } else {
                GameTexts.SendToAll("cmd.npcs_turned_off", admin.record.Name, removed);
            }
        }

        // time only goes forward: to the next time it's that hour
        private static void time(NetworkConnectionToClient sender, ConnectedPlayer admin, string value) {
            if (string.IsNullOrEmpty(value)) {
                reply(sender, World.WorldTime.IsPaused ? "cmd.time_is_paused" : "cmd.time_is", World.WorldTime.DayNumber, World.WorldTime.Clock);
                return;
            }
            float hours;
            switch (value.ToLowerInvariant()) {
                case "day": hours = 7; break;
                case "noon": hours = 12; break;
                case "night": hours = 20; break;
                case "midnight": hours = 0; break;
                default:
                    string[] parts = value.Split(':');
                    if (parts.Length != 2 || !int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m) || h < 0 || h > 23 || m < 0 || m > 59) {
                        reply(sender, "cmd.usage_time");
                        return;
                    }
                    hours = h + m / 60f;
                    break;
            }
            WorldNetwork.ServerSetTime(World.WorldTime.NextTimeAt(hours));
            Debug.Log(admin.record.Name + " set the time to " + World.WorldTime.Describe());
            GameTexts.SendToAll("cmd.time_set", admin.record.Name, World.WorldTime.DayNumber, World.WorldTime.Clock);
        }

        private static void dayLength(NetworkConnectionToClient sender, ConnectedPlayer admin, string value) {
            if (string.IsNullOrEmpty(value)) {
                reply(sender, "cmd.day_lasts", Math.Round(World.WorldTime.DayLength / 60.0, 1));
                return;
            }
            if (!float.TryParse(value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float minutes)
                    || minutes < 1 || minutes > 24 * 60) {
                reply(sender, "cmd.usage_daylength");
                return;
            }
            WorldNetwork.ServerSetDayLength(minutes * 60);
            Debug.Log(admin.record.Name + " made days last " + minutes.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " minutes");
            GameTexts.SendToAll("cmd.day_length_set", admin.record.Name, Math.Round((double)minutes, 1));
        }

        private static void teleport(NetworkConnectionToClient sender, ConnectedPlayer admin, string target) {
            NetworkConnectionToClient conn = findOnline(target, out ConnectedPlayer player);

            if (conn == null) {
                reply(sender, "cmd.nobody_online", target);
                return;
            }
            if (conn == sender) {
                reply(sender, "cmd.already_there");
                return;
            }
            if (conn.identity == null || sender.identity == null) {
                reply(sender, "cmd.still_joining");
                return;
            }
            sender.identity.GetComponent<PlayerNetwork>().ServerTeleport(conn.identity.transform.position);
            Debug.Log(admin.record.Name + " teleported to " + player.record.Name);
            reply(sender, "cmd.teleported", player.record.Name);
        }

        private static void role(NetworkConnectionToClient sender, ConnectedPlayer admin, string target, string newRole) {
            // online players use their record of the session, so the change applies right away
            NetworkConnectionToClient conn = findOnline(target, out ConnectedPlayer online);
            PlayerRecord player = online != null ? online.record : findRecord(target);

            if (player == null) {
                reply(sender, "cmd.no_player", target);
                return;
            }
            if (string.IsNullOrEmpty(newRole)) {
                reply(sender, "cmd.role_is", player.Name, player.Role);
                return;
            }

            newRole = newRole.ToLowerInvariant();
            if (newRole != PlayerRoles.User && newRole != PlayerRoles.Admin) {
                reply(sender, "cmd.unknown_role", newRole, PlayerRoles.User, PlayerRoles.Admin);
                return;
            }
            if (player.Id == admin.record.Id) {
                reply(sender, "cmd.own_role");
                return;
            }
            if (player.Role == newRole) {
                reply(sender, "cmd.role_already", player.Name, newRole);
                return;
            }

            BrickcraftNetworkManager.Instance.Database.SetRole(player, newRole);
            Debug.Log(admin.record.Name + " made " + player.Name + " (" + player.Id + ") " + newRole);
            reply(sender, "cmd.role_now", player.Name, newRole);

            if (conn != null) {
                reply(conn, "cmd.role_made_you", admin.record.Name, newRole);

                if (conn.identity != null) {
                    conn.identity.GetComponent<PlayerNetwork>().role = newRole; // the player list shows it
                }
            }
        }

        // ITEM[@COLOR] (a colour id or name, like plate_2x2@4 or plate_2x2@trans-clear), args: [count] [NICK|ID]
        private static void addItem(NetworkConnectionToClient sender, ConnectedPlayer admin, string itemText, string args) {
            string colorText = null;
            int at = itemText.IndexOf('@');
            if (at != -1) {
                colorText = itemText.Substring(at + 1);
                itemText = itemText.Substring(0, at);
            }
            string itemId = itemText.ToLowerInvariant();
            if (!Server.items.TryGetValue(itemId, out Item item)) {
                reply(sender, "cmd.no_item", itemText);
                return;
            }
            int color = item.color;
            if (colorText != null) {
                BrickColor brickColor = BrickColorPalette.Find(colorText);
                if (brickColor == null) {
                    reply(sender, "cmd.no_colour", colorText);
                    return;
                }
                if (!item.AllowsColor(brickColor.id)) {
                    reply(sender, "cmd.cant_be_colour", item, brickColor);
                    return;
                }
                color = brickColor.id;
            }
            // a colour that isn't the item's own is told (the "_colour" texts)
            BrickColor otherColor = color != item.color ? BrickColorPalette.Get(color) : null;
            string itemName = item.name + (otherColor != null ? " (" + otherColor.name + ")" : "");

            string[] rest = string.IsNullOrEmpty(args) ? new string[0] : args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int count = 1;
            string countText = rest.Length > 0 ? rest[0] : null;
            string target = rest.Length > 1 ? rest[1] : null;

            if (rest.Length == 1 && !int.TryParse(countText, out _)) {
                target = countText;
                countText = null;
            }
            if (countText != null && (!int.TryParse(countText, out count) || count < 1 || count > MaxAddedItems)) {
                reply(sender, "cmd.count_range", MaxAddedItems);
                return;
            }

            NetworkConnectionToClient conn = target == null ? sender : findOnline(target, out _);
            PlayerRecord player;
            bool added;

            if (conn != null) {
                ConnectedPlayer online = (ConnectedPlayer)conn.authenticationData;
                PlayerInventory inventory = conn.identity != null ? conn.identity.GetComponent<PlayerInventory>() : null;

                if (inventory == null) {
                    reply(sender, "cmd.player_still_joining", online.record.Name);
                    return;
                }
                player = online.record;
                added = inventory.ServerAdd(itemId, color, count);
            } else {
                // offline players get it in their saved inventory
                player = findRecord(target);
                if (player == null) {
                    reply(sender, "cmd.no_player", target);
                    return;
                }
                GameDatabase database = BrickcraftNetworkManager.Instance.Database;
                List<InventoryItem> items = database.LoadInventory(player.Id);
                added = Inventory.Add(items, itemId, color, count);

                if (added) {
                    database.SaveInventory(player.Id, items);
                }
            }

            if (!added) {
                if (otherColor != null) {
                    reply(sender, "cmd.no_room_colour", count, item, otherColor, player.Name);
                } else {
                    reply(sender, "cmd.no_room", count, item, player.Name);
                }
                return;
            }
            Debug.Log(admin.record.Name + " gave " + count + " x " + itemName + " (" + itemId + ") to " + player.Name + " (" + player.Id + ")");
            if (otherColor != null) {
                reply(sender, "cmd.gave_colour", count, item, otherColor, player.Name);
            } else {
                reply(sender, "cmd.gave", count, item, player.Name);
            }

            if (conn != null && conn != sender) {
                if (otherColor != null) {
                    reply(conn, "cmd.gave_you_colour", admin.record.Name, count, item, otherColor);
                } else {
                    reply(conn, "cmd.gave_you", admin.record.Name, count, item);
                }
            }
        }

        // empty if the admin can kick or ban the player, otherwise why not
        private static GameText refuse(ConnectedPlayer admin, PlayerRecord player, NetworkConnectionToClient conn) {
            if (player.Id == admin.record.Id) {
                return GameText.Of("cmd.not_yourself");
            }
            if (player.IsAdmin) {
                return GameText.Of("cmd.is_admin", player.Name);
            }
            if (conn is LocalConnectionToClient) {
                return GameText.Of("cmd.is_hosting", player.Name);
            }
            return default(GameText);
        }

        // the reason travels first, the connection closes once it had time to arrive
        private static void disconnect(NetworkConnectionToClient conn, GameText reason) {
            conn.Send(new DisconnectReasonMessage() { reason = reason });
            BrickcraftNetworkManager.Instance.StartCoroutine(disconnectLater(conn));
        }

        private static IEnumerator disconnectLater(NetworkConnectionToClient conn) {
            yield return new WaitForSecondsRealtime(DisconnectDelay);
            conn.Disconnect();
        }

        private static NetworkConnectionToClient findOnline(string target, out ConnectedPlayer player) {
            List<KeyValuePair<NetworkConnectionToClient, ConnectedPlayer>> online = onlinePlayers();

            foreach (KeyValuePair<NetworkConnectionToClient, ConnectedPlayer> entry in online) {
                if (string.Equals(entry.Value.record.Name, target, StringComparison.OrdinalIgnoreCase)) {
                    player = entry.Value;
                    return entry.Key;
                }
            }
            if (int.TryParse(target, out int id)) {
                foreach (KeyValuePair<NetworkConnectionToClient, ConnectedPlayer> entry in online) {
                    if (entry.Value.record.Id == id) {
                        player = entry.Value;
                        return entry.Key;
                    }
                }
            }
            player = null;
            return null;
        }

        private static PlayerRecord findRecord(string target) {
            GameDatabase database = BrickcraftNetworkManager.Instance.Database;
            PlayerRecord player = database.FindPlayer(target);

            if (player == null && int.TryParse(target, out int id)) {
                player = database.FindPlayer(id);
            }
            return player;
        }

        private static List<KeyValuePair<NetworkConnectionToClient, ConnectedPlayer>> onlinePlayers() {
            List<KeyValuePair<NetworkConnectionToClient, ConnectedPlayer>> online = new List<KeyValuePair<NetworkConnectionToClient, ConnectedPlayer>>();

            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn.authenticationData is ConnectedPlayer player && !player.hasLeft) {
                    online.Add(new KeyValuePair<NetworkConnectionToClient, ConnectedPlayer>(conn, player));
                }
            }
            return online;
        }

        private static string withReason(string reason) {
            return string.IsNullOrEmpty(reason) ? "" : ": " + reason;
        }

        // a line from the server, worded in that player's language
        private static void reply(NetworkConnectionToClient conn, string key, params object[] args) {
            GameTexts.Send(conn, key, args);
        }
    }
}
