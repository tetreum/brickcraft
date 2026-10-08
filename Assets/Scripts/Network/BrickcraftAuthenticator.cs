using System.Collections;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    public struct AuthRequestMessage : NetworkMessage
    {
        public string name;
        public string machineId;
    }

    public struct AuthResponseMessage : NetworkMessage
    {
        public bool accepted;
        public string message;
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
    /// Identifies players by name + machine id (see <see cref="PlayerIdentity"/>). A new name is
    /// registered to the machine that first uses it; afterwards only that machine can play with it.
    /// </summary>
    public class BrickcraftAuthenticator : NetworkAuthenticator
    {
        /// <summary>Why the server rejected us, shown in the menu.</summary>
        public static string LastRejection { get; private set; }

        // -------- server --------

        public override void OnStartServer() {
            NetworkServer.RegisterHandler<AuthRequestMessage>(onAuthRequest, false);
        }

        public override void OnStopServer() {
            NetworkServer.UnregisterHandler<AuthRequestMessage>();
        }

        private void onAuthRequest(NetworkConnectionToClient conn, AuthRequestMessage request) {
            if (conn.isAuthenticated || conn.authenticationData != null) {
                return;
            }

            GameDatabase database = BrickcraftNetworkManager.Instance.Database;
            string error = PlayerIdentity.ValidateName(request.name);
            PlayerRecord player = null;

            if (error == null) {
                string machineIdHash = PlayerIdentity.HashMachineId(request.machineId);
                player = database.FindPlayer(request.name);

                // admins are never locked out by the ban of someone using the same machine
                bool isAdmin = player != null && player.IsAdmin && player.TokenHash == machineIdHash;

                if ((player != null && player.Banned) || (!isAdmin && database.IsMachineBanned(machineIdHash))) {
                    error = "You are banned from this server";
                    if (player != null && !string.IsNullOrEmpty(player.BanReason)) {
                        error += ": " + player.BanReason;
                    }
                } else if (player == null) {
                    player = database.CreatePlayer(request.name, machineIdHash);
                } else if (player.TokenHash != machineIdHash) {
                    error = "The name " + player.Name + " belongs to someone else";
                } else if (isOnline(player.Id)) {
                    error = player.Name + " is already playing";
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
            Debug.Log(player.Name + " joined (player " + player.Id + ", " + player.Role + ", " + player.TimesJoined + " times)");

            conn.Send(new AuthResponseMessage() { accepted = true });
            ServerAccept(conn);
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
            NetworkClient.RegisterHandler<AuthResponseMessage>(onAuthResponse, false);
        }

        public override void OnStopClient() {
            NetworkClient.UnregisterHandler<AuthResponseMessage>();
        }

        public override void OnClientAuthenticate() {
            LastRejection = null;
            NetworkClient.Send(new AuthRequestMessage() {
                name = PlayerIdentity.Name,
                machineId = PlayerIdentity.MachineId,
            });
        }

        private void onAuthResponse(AuthResponseMessage response) {
            if (response.accepted) {
                ClientAccept();
            } else {
                LastRejection = response.message;
                Debug.LogWarning("The server rejected us: " + response.message);
                ClientReject();
            }
        }
    }
}
