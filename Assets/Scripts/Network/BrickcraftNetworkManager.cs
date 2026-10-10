using System.Collections;
using System.Collections.Generic;
using System.IO;
using Brickcraft.Bricks;
using Brickcraft.Mods;
using Brickcraft.Scripting;
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

        [Tooltip("Where players spawn when the scene has no NetworkStartPosition, in absolute coordinates")]
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
        public StarterItem[] starterItems = { new StarterItem() { itemId = "dirt", quantity = 100 } };

        [System.Serializable]
        public struct StarterItem
        {
            public string itemId;
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

        /// <summary>Plays a saved world (see World.SavedWorlds) alone.</summary>
        public void PlayWorld(string worldSaveName) {
            saveName = worldSaveName;
            StartSingleplayer();
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
            startFailure = null;
            long newSeed = seed != 0 ? seed : Random.Range(1, int.MaxValue);
            // only the generated world is saved, the test scene starts from scratch every time
            bool isSaved = isWorldScene(onlineScene);

            // a save of an older format is upgraded before anything opens it (see WorldStorage.FormatVersion)
            WorldStorage storage;
            try {
                if (isSaved) {
                    WorldStorage.UpgradeIfNeeded(SaveFolder);
                }
                Database = new GameDatabase(SaveFolder);

                // the world's mods, before its scene loads and uses the items
                WorldStorage info = isSaved ? WorldStorage.ReadInfo(SaveFolder) : null;
                ModDatabase.Activate(info != null ? info.ModIds : new List<string>());
                ModScripts.Start();

                storage = isSaved ? WorldStorage.OpenOrCreate(SaveFolder, newSeed, saveName, Difficulty.Normal) : null;
            } catch (System.Exception e) {
                failStart("Couldn't open the world " + saveName + ": " + e.Message, e);
                return;
            }
            WorldNetwork.StartServer(storage, newSeed, defaultSpawnPosition);

            if (storage != null) {
                StartCoroutine(autosave());
            }
        }

        // why the server couldn't start, told to the host's player once back in the menu
        private static string startFailure;

        /// <summary>The server couldn't start: nobody can join it.</summary>
        public bool HasFailedToStart {
            get { return startFailure != null; }
        }

        // stops the server the next frame (Mirror is still starting it) and goes back to the menu
        private void failStart(string message, System.Exception e) {
            startFailure = message;
            Debug.LogError(message + "\n" + e);
            StartCoroutine(stopAfterFailedStart());
        }

        private IEnumerator stopAfterFailedStart() {
            yield return null;
            Leave();
        }

        // the host's player objects are destroyed before the server stops, so save them first
        public override void OnStopHost() {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                endSession(conn, false);
            }
            base.OnStopHost();
        }

        public override void OnStopServer() {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                endSession(conn, false);
            }
            ModScripts.Stop();
            WorldNetwork.StopServer();
            if (Database != null) {
                Database.Dispose();
                Database = null;
            }
            base.OnStopServer();
        }

        // writes the changed chunks on a background thread every now and then
        private IEnumerator autosave() {
            WaitForSecondsRealtime wait = new WaitForSecondsRealtime(autosaveInterval);

            while (NetworkServer.active && WorldNetwork.Storage != null) {
                yield return wait;

                // without it the world is still saved when regions unload and when the server stops
                if (WorldNetwork.Storage != null && GameSettings.AutoSave) {
                    WorldNetwork.Storage.SaveInBackground();
                }
                // so a crash doesn't send players back much
                foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                    savePosition(conn);
                }
            }
        }

        private static bool isWorldScene(string scene) {
            return Path.GetFileNameWithoutExtension(scene) == WorldScene;
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn) {
            endSession(conn, true);
            WorldNetwork.ServerDisconnect(conn);
            base.OnServerDisconnect(conn);
        }

        // announce is false when the whole server stops, nobody would be left to read it
        private void endSession(NetworkConnectionToClient conn, bool announce) {
            if (conn.authenticationData is ConnectedPlayer player && !player.hasLeft) {
                savePosition(conn);
                player.hasLeft = true;
                Database.EndSession(player.record, player.session);

                if (announce && player.hasJoined && !player.leaveAnnounced) {
                    ChatEvents.Send(new ChatEventMessage() { type = ChatEventType.Left, player = player.record.Name }, conn);
                }
                if (player.hasJoined) {
                    ModScripts.ModEvent("onPlayerLeft", PlayerHandle.For(conn));
                }
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
            ConnectedPlayer connected = (ConnectedPlayer)conn.authenticationData;
            Transform start = GetStartPosition();
            Vector3 position = start != null ? start.position : FloatingOrigin.ToLocal(defaultSpawnPosition);
            Quaternion rotation = start != null ? start.rotation : Quaternion.identity;

            if (HasSavedPosition(connected.record)) {
                // back where it left
                PlayerRecord record = connected.record;
                position = FloatingOrigin.ToLocal(record.LastX.Value, record.LastY.Value, record.LastZ.Value);
                rotation = Quaternion.Euler(0, record.LastYaw ?? 0, 0);
            } else if (start == null && WorldBehaviour.Instance != null && WorldBehaviour.Instance.IsGenerated(WorldBehaviour.ChunkAt(position))) {
                // drop players just above the ground instead of from the sky
                position.y = WorldBehaviour.Instance.GetSurfaceHeight(position) + SpawnHeightAboveGround;
            }

            GameObject player = Instantiate(playerPrefab, position, rotation);
            player.name = playerPrefab.name + " [" + connected.record.Name + "]";

            PlayerNetwork network = player.GetComponent<PlayerNetwork>();
            network.playerName = connected.record.Name;
            network.role = connected.record.Role;
            network.ServerSetSpawn(position, rotation.eulerAngles.y);
            loadInventory(player.GetComponent<PlayerInventory>(), connected.record);

            NetworkServer.AddPlayerForConnection(conn, player);

            connected.hasJoined = true;
            ChatEvents.Send(new ChatEventMessage() { type = ChatEventType.Joined, player = connected.record.Name });
            ModScripts.ModEvent("onPlayerJoined", PlayerHandle.For(conn));
        }

        /// <summary>
        /// Players come back where they left, but only in the generated world: the test scene shares
        /// the players database but is another place.
        /// </summary>
        public static bool HasSavedPosition(PlayerRecord record) {
            return record.HasLastPosition && WorldBehaviour.Instance != null;
        }

        // absolute position and facing direction, saved as doubles so they're exact however far
        private void savePosition(NetworkConnectionToClient conn) {
            if (WorldBehaviour.Instance == null || conn.identity == null || !(conn.authenticationData is ConnectedPlayer player) || player.hasLeft) {
                return;
            }
            Transform transform = conn.identity.transform;
            FloatingOrigin.ToAbsolute(transform.position, out double x, out double y, out double z);
            Database.SavePosition(player.record, x, y, z, transform.eulerAngles.y);
        }

        private void loadInventory(PlayerInventory inventory, PlayerRecord record) {
            inventory.ServerLoad(record.Id, Database.LoadInventory(record.Id));

            if (record.TimesJoined == 1 && inventory.items.Count == 0) {
                foreach (StarterItem starter in starterItems) {
                    inventory.ServerAdd(starter.itemId, Server.items.TryGetValue(starter.itemId, out Item item) ? item.color : BrickColor.None, starter.quantity);
                }
            }
        }

        public override void Update() {
            base.Update();

            if (NetworkServer.active) {
                WorldNetwork.ServerUpdate();
                LuaApi.Update();
            }
            if (NetworkClient.active) {
                WorldNetwork.ClientUpdate();
            }
        }

        // -------- client --------

        public override void OnStartClient() {
            base.OnStartClient();
            WorldNetwork.StartClient();

            Events.EventManager.ClientStarted.Raise(new Events.ClientStartedEvent());
            NetworkClient.RegisterHandler<ChatMessage>(message => Events.EventManager.ChatLineReceived.Raise(new Events.ChatLineReceivedEvent() {
                sender = message.sender,
                text = message.text,
            }));
            NetworkClient.RegisterHandler<ChatEventMessage>(message => {
                string text = ChatEvents.Describe(message);

                if (text != null) {
                    Events.EventManager.ChatLineReceived.Raise(new Events.ChatLineReceivedEvent() { text = text });
                }
            });

            disconnectReason = null;
            NetworkClient.RegisterHandler<DisconnectReasonMessage>(message => disconnectReason = message.reason);
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

        // why the server disconnected us (kicked, banned), if it said
        private static string disconnectReason;

        // tells the player why it's back in the menu
        public override void OnClientDisconnect() {
            base.OnClientDisconnect();

            string message = startFailure ?? BrickcraftAuthenticator.LastRejection ?? disconnectReason;
            startFailure = null;

            if (message == null && isInMenu()) {
                message = "Couldn't join " + networkAddress; // a failed join leaves us in the menu
            }
            if (message == null) {
                return;
            }
            Debug.LogWarning(message);
            Events.EventManager.Disconnected.Raise(new Events.DisconnectedEvent() { message = message, isInMenu = isInMenu() });
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
