using System.IO;
using Brickcraft.World;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brickcraft.Network
{
    /// <summary>
    /// Every game is a networked game. Singleplayer is a host that doesn't accept other players:
    /// it doesn't listen for connections at all, and rejects any remote connection just in case.
    ///
    /// The manager lives in Resources/NetworkManager so menus and scenes can create it on demand.
    /// </summary>
    public class BrickcraftNetworkManager : NetworkManager
    {
        public const string PrefabPath = "NetworkManager";
        public const string WorldScene = "WorldGenerationTest";
        public const string TestScene = "Test";

        public static BrickcraftNetworkManager Instance {
            get {
                return (BrickcraftNetworkManager)singleton;
            }
        }

        [Tooltip("Where players spawn when the scene has no NetworkStartPosition")]
        public Vector3 defaultSpawnPosition = new Vector3(0, 160, 0);

        // the player's pivot is at the middle of its body
        private const float SpawnHeightAboveGround = 1.5f;

        [Tooltip("World seed, 0 picks a random one")]
        public long seed = 13284938921;

        /// <summary>False in singleplayer: the server doesn't let other players join.</summary>
        public bool AllowsRemotePlayers { get; private set; }

        public static BrickcraftNetworkManager GetOrCreate() {
            if (singleton == null) {
                Instantiate(Resources.Load<GameObject>(PrefabPath));
            }
            return Instance;
        }

        public void StartSingleplayer(string scene = WorldScene) {
            AllowsRemotePlayers = false;
            NetworkServer.listen = false;
            onlineScene = scene;
            StartHost();
        }

        public void StartMultiplayerHost(string scene = WorldScene) {
            AllowsRemotePlayers = true;
            NetworkServer.listen = true;
            onlineScene = scene;
            StartHost();
        }

        public void Join(string address) {
            networkAddress = address;
            StartClient();
        }

        /// <summary>Leaves the game and goes back to the main menu.</summary>
        public void Leave() {
            if (NetworkServer.active && NetworkClient.isConnected) {
                StopHost();
            } else if (NetworkClient.isConnected) {
                StopClient();
            } else if (NetworkServer.active) {
                StopServer();
            }
        }

        // -------- server --------

        public override void OnStartServer() {
            base.OnStartServer();
            WorldNetwork.StartServer(seed != 0 ? seed : Random.Range(1, int.MaxValue));
        }

        public override void OnServerConnect(NetworkConnectionToClient conn) {
            if (!AllowsRemotePlayers && !(conn is LocalConnectionToClient)) {
                Debug.LogWarning("Rejected " + conn.address + ": this is a singleplayer game");
                conn.Disconnect();
                return;
            }
            base.OnServerConnect(conn);
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn) {
            Transform start = GetStartPosition();
            Vector3 position = start != null ? start.position : defaultSpawnPosition;
            Quaternion rotation = start != null ? start.rotation : Quaternion.identity;

            // drop players just above the ground instead of from the sky
            if (start == null && WorldBehaviour.Instance != null && WorldBehaviour.Instance.IsReady) {
                position.y = WorldBehaviour.Instance.GetSurfaceHeight(position) + SpawnHeightAboveGround;
            }

            GameObject player = Instantiate(playerPrefab, position, rotation);
            player.name = playerPrefab.name + " [connId=" + conn.connectionId + "]";
            NetworkServer.AddPlayerForConnection(conn, player);
        }

        // -------- client --------

        public override void OnStartClient() {
            base.OnStartClient();
            WorldNetwork.StartClient();
        }

        // players are added once the client has the world, see WorldNetwork
        public override void OnClientConnect() {
            requestWorldIfInGame();
        }

        public override void OnClientSceneChanged() {
            requestWorldIfInGame();
        }

        // a failed join leaves us in the menu, show it again
        public override void OnClientDisconnect() {
            base.OnClientDisconnect();

            if (isInMenu() && Menu.Instance != null) {
                Debug.LogWarning("Couldn't join " + networkAddress);
                Menu.Instance.showPanel("MainPanel");
            }
        }

        private void requestWorldIfInGame() {
            if (NetworkClient.isConnected && !NetworkClient.isLoadingScene && !isInMenu()) {
                WorldNetwork.RequestWorld();
            }
        }

        private bool isInMenu() {
            Scene scene = SceneManager.GetActiveScene();

            return scene.path == offlineScene || scene.name == Path.GetFileNameWithoutExtension(offlineScene);
        }
    }
}
