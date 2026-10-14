using System.Collections;
using System.Collections.Generic;
using Brickcraft.Mods;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    public struct AuthRequestMessage : NetworkMessage
    {
        /// <summary>GameVersion.Current of the client. Keep it the first field, so any version can read it.</summary>
        public string version;
        public string name;
        public string machineId;
        /// <summary>The mods the player has installed, the server checks it has the world's.</summary>
        public ModEntry[] mods;
        /// <summary>The server's password as typed, hashed (see ServerConfig.HashPassword), empty if none.</summary>
        public string passwordHash;
    }

    public struct AuthResponseMessage : NetworkMessage
    {
        public bool accepted;
        public string message;
        /// <summary>Accepted: the mods to play with, the world's.</summary>
        public ModEntry[] mods;
        /// <summary>Accepted: the server's name (empty in singleplayer) and how many can play at once.</summary>
        public string serverName;
        public int maxPlayers;
    }

    /// <summary>A player that passed authentication, stored in the connection's authenticationData.</summary>
    public class ConnectedPlayer
    {
        public PlayerRecord record;
        public PlayerSessionRecord session;
        public bool hasLeft;
        /// <summary>Its player spawned, the others were told it joined.</summary>
        public bool hasJoined;
        /// <summary>The others were already told why it's leaving (kicked, banned).</summary>
        public bool leaveAnnounced;
    }

    /// <summary>
    /// Identifies players by their machine id (see <see cref="PlayerIdentity"/>): a machine plays as the same
    /// player whatever its name, joining with another name renames it. A name belongs to the machine that has it,
    /// no other can take it.
    /// Players also need the game's version and the world's mods (see ModDatabase.CheckPlayerMods),
    /// which they load once accepted, the server's password if it has one, and room in it (see
    /// ServerConfig: neither applies to the host's own player).
    /// </summary>
    public class BrickcraftAuthenticator : NetworkAuthenticator
    {
        /// <summary>Why the server rejected us, shown in the menu.</summary>
        public static string LastRejection { get; private set; }

        /// <summary>What to send as the server's password when joining (typed in the menu, never saved).</summary>
        public static string Password = "";

        /// <summary>The name of the server we're on, empty in singleplayer (or before it accepted us).</summary>
        public static string ServerName { get; private set; } = "";
        /// <summary>How many can play on it at once, 0 until it accepted us.</summary>
        public static int MaxPlayers { get; private set; }

        // -------- server --------

        public override void OnStartServer() {
            NetworkServer.RegisterHandler<AuthRequestMessage>(onAuthRequest, false);
        }

        public override void OnStopServer() {
            NetworkServer.UnregisterHandler<AuthRequestMessage>();
        }

        private void onAuthRequest(NetworkConnectionToClient conn, AuthRequestMessage request) {
            // a server that couldn't start is about to stop, see BrickcraftNetworkManager.failStart
            if (conn.isAuthenticated || conn.authenticationData != null || BrickcraftNetworkManager.Instance.HasFailedToStart) {
                return;
            }

            GameDatabase database = BrickcraftNetworkManager.Instance.Database;
            string error = request.version != GameVersion.Current
                ? "The server runs version " + GameVersion.Current + ", you have " + (string.IsNullOrEmpty(request.version) ? "an unknown version" : request.version)
                : ModDatabase.CheckPlayerMods(request.mods) ?? PlayerIdentity.ValidateName(request.name);
            PlayerRecord player = null;

            ServerConfig config = ServerConfig.Current;
            bool isHost = conn is LocalConnectionToClient;
            if (error == null && !isHost && !config.CheckPassword(request.passwordHash)) {
                error = string.IsNullOrEmpty(request.passwordHash) ? "This server needs a password" : "Wrong password";
            }
            if (error == null && !isHost && onlineCount() >= config.maxPlayers) {
                error = "The server is full (" + config.maxPlayers + " players)";
            }

            if (error == null) {
                string machineIdHash = PlayerIdentity.HashMachineId(request.machineId);
                JoinRules.Decision decision = JoinRules.Decide(request.name, machineIdHash, database.FindPlayer(request.name),
                    database.FindPlayerByMachine(machineIdHash), database.IsMachineBanned(machineIdHash), isOnline);
                error = decision.error;
                player = decision.player;
                if (error == null && player == null) {
                    player = database.CreatePlayer(request.name, machineIdHash);
                } else if (error == null && decision.rename) {
                    if (decision.moveAside != null) {
                        database.RenamePlayer(decision.moveAside, PlayerIdentity.FreeName(decision.moveAside.Name + "_" + decision.moveAside.Id, database));
                    }
                    Debug.Log(player.Name + " is now " + request.name);
                    database.RenamePlayer(player, request.name);
                }
            }

            if (error != null) {
                conn.Send(new AuthResponseMessage() { accepted = false, message = error });
                conn.isAuthenticated = false;
                StartCoroutine(rejectLater(conn));
                return;
            }

            // whoever joins a save without admins first (the host, starting singleplayer or a server) runs it
            if (!database.HasAdmin()) {
                database.SetRole(player, PlayerRoles.Admin);
                Debug.Log(player.Name + " is the admin of this save");
            }

            conn.authenticationData = new ConnectedPlayer() {
                record = player,
                session = database.StartSession(player, conn.address),
            };

            conn.Send(new AuthResponseMessage() {
                accepted = true,
                mods = ModDatabase.ActiveEntries(),
                serverName = BrickcraftNetworkManager.Instance.AllowsRemotePlayers ? config.name : "",
                maxPlayers = BrickcraftNetworkManager.Instance.AllowsRemotePlayers ? config.maxPlayers : 1,
            });
            ServerAccept(conn);
        }

        private static int onlineCount() {
            int count = 0;
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn.authenticationData is ConnectedPlayer connected && !connected.hasLeft) {
                    count++;
                }
            }
            return count;
        }

        private static bool isOnline(int playerId) {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn.authenticationData is ConnectedPlayer connected && !connected.hasLeft && connected.record.Id == playerId) {
                    return true;
                }
            }
            return false;
        }

        // give the response time to reach the client before disconnecting it
        private IEnumerator rejectLater(NetworkConnectionToClient conn) {
            yield return new WaitForSeconds(1f);
            ServerReject(conn);
        }

        // -------- client --------

        public override void OnStartClient() {
            LastRejection = null;
            ServerName = "";
            MaxPlayers = 0;
            NetworkClient.RegisterHandler<AuthResponseMessage>(onAuthResponse, false);
        }

        public override void OnStopClient() {
            NetworkClient.UnregisterHandler<AuthResponseMessage>();
        }

        public override void OnClientAuthenticate() {
            LastRejection = null;
            NetworkClient.Send(new AuthRequestMessage() {
                version = GameVersion.Current,
                name = PlayerIdentity.Name,
                machineId = PlayerIdentity.MachineId,
                mods = ModDatabase.InstalledEntries(),
                passwordHash = ServerConfig.HashPassword(Password),
            });
        }

        private void onAuthResponse(AuthResponseMessage response) {
            if (response.accepted) {
                ServerName = response.serverName ?? "";
                MaxPlayers = response.maxPlayers;
                // the world's mods (the host's client already has them, it shares the server's items)
                if (!NetworkServer.active) {
                    List<string> ids = new List<string>();
                    foreach (ModEntry mod in response.mods ?? new ModEntry[0]) {
                        ids.Add(mod.id);
                    }
                    ModDatabase.Activate(ids);
                }
                ClientAccept();
            } else {
                LastRejection = response.message;
                Debug.LogWarning("The server rejected us: " + response.message);
                ClientReject();
            }
        }
    }
}
