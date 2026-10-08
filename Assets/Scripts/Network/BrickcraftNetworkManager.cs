using System.Collections;
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

        [Tooltip("Name of the save, stored in [persistentDataPath]/saves/[name]/")]
        public string saveName = "world";

        [Tooltip("Seconds between saves of the world changes")]
        public float autosaveInterval = 30f;

        [Tooltip("Items new players start with")]
        public StarterItem[] starterItems = { new StarterItem() { itemId = 1, quantity = 100 } };

        [System.Serializable]
        public struct StarterItem
        {
            public int itemId;
            public int quantity;
        }

        /// <summary>False in singleplayer: the server doesn't let other players join.</summary>
        public bool AllowsRemotePlayers { get; private set; }

        /// <summary>Server only: players, sessions and inventories.</summary>
        public GameDatabase Database { get; private set; }

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

        /// <summary>Folder of the current save: the world files and the players database.</summary>
        public string SaveFolder {
            get { return Path.Combine(Application.persistentDataPath, "saves", saveName); }
        }

        public override void OnStartServer() {
            base.OnStartServer();
            Database = new GameDatabase(SaveFolder);
            Debug.Log("Server save: " + SaveFolder);

            long newSeed = seed != 0 ? seed : Random.Range(1, int.MaxValue);

            // only the generated world is saved, the test scene starts from scratch every time
            WorldStorage storage = isWorldScene(onlineScene) ? WorldStorage.OpenOrCreate(SaveFolder, newSeed) : null;
            WorldNetwork.StartServer(storage, newSeed, defaultSpawnPosition);

            if (storage != null) {
                StartCoroutine(autosave());
            }
        }

        public override void OnStopServer() {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                endSession(conn);
            }
            WorldNetwork.StopServer();
            Database.Dispose();
            Database = null;
            base.OnStopServer();
        }

        // writes the changed chunks on a background thread every now and then
        private IEnumerator autosave() {
            WaitForSecondsRealtime wait = new WaitForSecondsRealtime(autosaveInterval);

            while (NetworkServer.active && WorldNetwork.Storage != null) {
                yield return wait;

                if (WorldNetwork.Storage != null) {
                    WorldNetwork.Storage.SaveInBackground();
                }
            }
        }

        private static bool isWorldScene(string scene) {
            return Path.GetFileNameWithoutExtension(scene) == WorldScene;
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn) {
            endSession(conn);
            WorldNetwork.ServerDisconnect(conn);
            base.OnServerDisconnect(conn);
        }

        private void endSession(NetworkConnectionToClient conn) {
            if (conn.authenticationData is ConnectedPlayer player && !player.hasLeft) {
                player.hasLeft = true;
                Database.EndSession(player.record, player.session);
                Debug.Log(player.record.Name + " left");
            }
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
            if (start == null && WorldBehaviour.Instance != null && WorldBehaviour.Instance.IsGenerated(WorldBehaviour.ChunkAt(position))) {
                position.y = WorldBehaviour.Instance.GetSurfaceHeight(position) + SpawnHeightAboveGround;
            }

            ConnectedPlayer connected = (ConnectedPlayer)conn.authenticationData;
            GameObject player = Instantiate(playerPrefab, position, rotation);
            player.name = playerPrefab.name + " [" + connected.record.Name + "]";

            player.GetComponent<PlayerNetwork>().playerName = connected.record.Name;
            loadInventory(player.GetComponent<PlayerInventory>(), connected.record);

            NetworkServer.AddPlayerForConnection(conn, player);
        }

        private void loadInventory(PlayerInventory inventory, PlayerRecord record) {
            inventory.ServerLoad(record.Id, Database.LoadInventory(record.Id));

            if (record.TimesJoined == 1 && inventory.items.Count == 0) {
                foreach (StarterItem starter in starterItems) {
                    inventory.ServerAdd(starter.itemId, starter.quantity);
                }
            }
        }

        public override void Update() {
            base.Update();

            if (NetworkServer.active) {
                WorldNetwork.ServerUpdate();
            }
            if (NetworkClient.active) {
                WorldNetwork.ClientUpdate();
            }
        }

        // -------- client --------

        public override void OnStartClient() {
            base.OnStartClient();
            WorldNetwork.StartClient();
        }

        public override void OnStopClient() {
            WorldNetwork.StopClient();
            base.OnStopClient();
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
                string message = BrickcraftAuthenticator.LastRejection ?? "Couldn't join " + networkAddress;

                Debug.LogWarning(message);
                Menu.Instance.showPanel("MainPanel").GetComponent<MainPanel>().showMessage(message);
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
