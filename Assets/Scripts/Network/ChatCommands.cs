using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>Server to client: why the server is about to disconnect it.</summary>
    public struct DisconnectReasonMessage : NetworkMessage
    {
        public string reason;
    }

    /// <summary>
    /// Chat messages starting with "/" are commands, run on the server:
    ///   /players                    online players and their ids
    ///   /kick NICK|ID [reason]      disconnects an online player
    ///   /ban NICK|ID [reason]       bans a player (and its machine), online or not
    ///   /unban NICK|ID
    ///   /role NICK|ID [role]        shows or changes a player's role (user, admin)
    /// NICK|ID is a player name (any case) or, if no name matches, a player id.
    /// Only admins can use them.
    /// </summary>
    public static class ChatCommands
    {
        public const string ServerName = "Server";

        private const float DisconnectDelay = 0.5f;

        public static bool IsCommand(string text) {
            return text.StartsWith("/");
        }

        public static void Run(NetworkConnectionToClient sender, string text) {
            ConnectedPlayer admin = sender.authenticationData as ConnectedPlayer;
            string[] parts = text.Substring(1).Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
            string target = parts.Length > 1 ? parts[1] : null;
            string reason = parts.Length > 2 ? parts[2].Trim() : null;

            if (admin == null || !admin.record.IsAdmin) {
                reply(sender, "Only admins can use commands");
                return;
            }

            switch (command) {
                case "players":
                    listPlayers(sender);
                    break;
                case "kick":
                    if (target == null) {
                        reply(sender, "Usage: /kick NICK|ID [reason]");
                    } else {
                        kick(sender, admin, target, reason);
                    }
                    break;
                case "ban":
                    if (target == null) {
                        reply(sender, "Usage: /ban NICK|ID [reason]");
                    } else {
                        ban(sender, admin, target, reason);
                    }
                    break;
                case "role":
                    if (target == null) {
                        reply(sender, "Usage: /role NICK|ID [" + PlayerRoles.User + "|" + PlayerRoles.Admin + "]");
                    } else {
                        role(sender, admin, target, reason);
                    }
                    break;
                case "unban":
                    if (target == null) {
                        reply(sender, "Usage: /unban NICK|ID");
                    } else {
                        unban(sender, target);
                    }
                    break;
                default:
                    reply(sender, "Unknown command. Commands: /players, /kick NICK|ID [reason], /ban NICK|ID [reason], /unban NICK|ID, /role NICK|ID [role]");
                    break;
            }
        }

        private static void listPlayers(NetworkConnectionToClient sender) {
            StringBuilder list = new StringBuilder("Online:");

            foreach (KeyValuePair<NetworkConnectionToClient, ConnectedPlayer> online in onlinePlayers()) {
                list.Append(' ').Append(online.Value.record.Name).Append(" (").Append(online.Value.record.Id).Append(')');
            }
            reply(sender, list.ToString());
        }

        private static void kick(NetworkConnectionToClient sender, ConnectedPlayer admin, string target, string reason) {
            NetworkConnectionToClient conn = findOnline(target, out ConnectedPlayer player);

            if (conn == null) {
                reply(sender, "Nobody called " + target + " is online");
                return;
            }
            string refusal = refuse(admin, player.record, conn);
            if (refusal != null) {
                reply(sender, refusal);
                return;
            }

            player.leaveAnnounced = true;
            ChatEvents.Send(new ChatEventMessage() { type = ChatEventType.Kicked, player = player.record.Name, by = admin.record.Name, reason = reason });
            disconnect(conn, "You were kicked by " + admin.record.Name + withReason(reason));
        }

        private static void ban(NetworkConnectionToClient sender, ConnectedPlayer admin, string target, string reason) {
            NetworkConnectionToClient conn = findOnline(target, out ConnectedPlayer online);
            PlayerRecord player = online != null ? online.record : findRecord(target);

            if (player == null) {
                reply(sender, "There's no player called " + target);
                return;
            }
            string refusal = refuse(admin, player, conn);
            if (refusal != null) {
                reply(sender, refusal);
                return;
            }
            if (player.TokenHash == admin.record.TokenHash) {
                reply(sender, player.Name + " plays from your machine, banning it would ban you too");
                return;
            }
            if (player.Banned) {
                reply(sender, player.Name + " is already banned");
                return;
            }

            BrickcraftNetworkManager.Instance.Database.SetBanned(player, true, reason);
            if (online != null) {
                online.leaveAnnounced = true;
            }
            ChatEvents.Send(new ChatEventMessage() { type = ChatEventType.Banned, player = player.Name, by = admin.record.Name, reason = reason });
            Debug.Log(player.Name + " (" + player.Id + ") was banned by " + admin.record.Name + withReason(reason));

            if (conn != null) {
                disconnect(conn, "You were banned by " + admin.record.Name + withReason(reason));
            }
        }

        private static void unban(NetworkConnectionToClient sender, string target) {
            PlayerRecord player = findRecord(target);

            if (player == null || !player.Banned) {
                reply(sender, player == null ? "There's no player called " + target : player.Name + " isn't banned");
                return;
            }
            BrickcraftNetworkManager.Instance.Database.SetBanned(player, false, null);
            reply(sender, player.Name + " can join again");
        }

        private static void role(NetworkConnectionToClient sender, ConnectedPlayer admin, string target, string newRole) {
            // online players use their record of the session, so the change applies right away
            NetworkConnectionToClient conn = findOnline(target, out ConnectedPlayer online);
            PlayerRecord player = online != null ? online.record : findRecord(target);

            if (player == null) {
                reply(sender, "There's no player called " + target);
                return;
            }
            if (string.IsNullOrEmpty(newRole)) {
                reply(sender, player.Name + " is " + player.Role);
                return;
            }

            newRole = newRole.ToLowerInvariant();
            if (newRole != PlayerRoles.User && newRole != PlayerRoles.Admin) {
                reply(sender, "Unknown role " + newRole + ", roles are " + PlayerRoles.User + " and " + PlayerRoles.Admin);
                return;
            }
            if (player.Id == admin.record.Id) {
                reply(sender, "You can't change your own role");
                return;
            }
            if (player.Role == newRole) {
                reply(sender, player.Name + " is already " + newRole);
                return;
            }

            BrickcraftNetworkManager.Instance.Database.SetRole(player, newRole);
            Debug.Log(admin.record.Name + " made " + player.Name + " (" + player.Id + ") " + newRole);
            reply(sender, player.Name + " is now " + newRole);

            if (conn != null) {
                reply(conn, admin.record.Name + " made you " + newRole);

                if (conn.identity != null) {
                    conn.identity.GetComponent<PlayerNetwork>().role = newRole; // the player list shows it
                }
            }
        }

        // null if the admin can kick or ban the player, otherwise why not
        private static string refuse(ConnectedPlayer admin, PlayerRecord player, NetworkConnectionToClient conn) {
            if (player.Id == admin.record.Id) {
                return "You can't do that to yourself";
            }
            if (player.IsAdmin) {
                return player.Name + " is an admin";
            }
            if (conn is LocalConnectionToClient) {
                return player.Name + " is hosting the game";
            }
            return null;
        }

        // the reason travels first, the connection closes once it had time to arrive
        private static void disconnect(NetworkConnectionToClient conn, string reason) {
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

        private static void reply(NetworkConnectionToClient conn, string text) {
            conn.Send(new ChatMessage() { sender = ServerName, text = text });
        }
    }
}
