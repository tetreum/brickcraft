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
            LuaUi.StartServer();
            // the players it takes, a few more connections so the next ones hear it's full
            ServerConfig config = ServerConfig.Load();
            maxConnections = config.maxPlayers + 4;
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
                failStart(Loc.Get(Loc.UI, "network.couldnt_open_the_world", saveName, e.Message), e);
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
            Npcs.NpcSystem.ServerSave(true);
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                endSession(conn, false);
            }
            base.OnStopHost();
        }

        public override void OnStopServer() {
            Npcs.NpcSystem.ServerSave(true);
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                endSession(conn, false);
            }
            // a tutorial's world is played once: it goes with the game (see SavedWorlds.CreateTutorial)
            string tutorialWorld = WorldNetwork.Storage != null && !string.IsNullOrEmpty(WorldNetwork.Storage.Tutorial) ? saveName : null;
            ModScripts.Stop();
            LuaUi.StopServer();
            WorldNetwork.StopServer();
            if (Database != null) {
                Database.Dispose();
                Database = null;
            }
            base.OnStopServer();
            if (tutorialWorld != null) {
                StartCoroutine(deleteWorld(tutorialWorld));
            }
        }

        // how long a world's files may take to be let go of once it's closed (the players database's)
        private const float DeleteTimeout = 5;

        // deletes a world as soon as its files are closed
        private IEnumerator deleteWorld(string world) {
            float end = Time.realtimeSinceStartup + DeleteTimeout;
            while (true) {
                try {
                    if (Directory.Exists(Path.Combine(SavedWorlds.Folder, world))) {
                        SavedWorlds.Delete(world);
                    }
                    yield break;
                } catch (IOException e) {
                    if (Time.realtimeSinceStartup > end) {
                        Debug.LogWarning("The tutorial's world " + world + " couldn't be deleted (" + e.Message + "): it goes the next time the game starts");
                        yield break;
                    }
                } catch (System.UnauthorizedAccessException e) {
                    if (Time.realtimeSinceStartup > end) {
                        Debug.LogWarning("The tutorial's world " + world + " couldn't be deleted (" + e.Message + "): it goes the next time the game starts");
                        yield break;
                    }
                }
                yield return new WaitForSecondsRealtime(0.2f);
            }
        }

        // writes the changed chunks on a background thread every now and then
        private IEnumerator autosave() {
            WaitForSecondsRealtime wait = new WaitForSecondsRealtime(autosaveInterval);

            while (NetworkServer.active && WorldNetwork.Storage != null) {
                yield return wait;

                if (WorldNetwork.Storage != null) {
                    Npcs.NpcSystem.ServerSave();
                    WorldNetwork.SaveTime();
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
                LuaUi.PlayerLeft(conn);
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

        /// <summary>Where new players appear, and dead ones come back (local position).</summary>
        public Vector3 ServerSpawnPosition(out Quaternion rotation) {
            Transform start = GetStartPosition();
            Vector3 position = start != null ? start.position : FloatingOrigin.ToLocal(defaultSpawnPosition);
            rotation = start != null ? start.rotation : Quaternion.identity;

            if (start == null && WorldBehaviour.Instance != null && WorldBehaviour.Instance.IsGenerated(WorldBehaviour.ChunkAt(position))) {
                // drop players just above the ground instead of from the sky, on dry ground where there is (not in water)
                position = dryGround(position);
                position.y = WorldBehaviour.Instance.GetSurfaceHeight(position) + SpawnHeightAboveGround;
            }
            return position;
        }

        // how far from the spawn a dry spot is looked for, in blocks (see dryGround)
        private const int SpawnSearchBlocks = 24;

        // that position if its ground is dry, or the nearest dry ground within SpawnSearchBlocks (players shouldn't
        // be dropped in water, where swimming makes it hard to get out: see FirstPersonController)
        private static Vector3 dryGround(Vector3 position) {
            WorldBehaviour world = WorldBehaviour.Instance;
            Vector3Int at = BrickGrid.CellToBlock(BrickGrid.WorldToCell(position));
            for (int radius = 0; radius <= SpawnSearchBlocks; radius++) {
                for (int x = -radius; x <= radius; x++) {
                    for (int z = -radius; z <= radius; z++) {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != radius) {
                            continue;
                        }
                        int bx = at.x + x, bz = at.z + z;
                        int surface = world.SurfaceBlockY(bx, bz);
                        if (surface <= 0) {
                            continue; // the world isn't there
                        }
                        BlockType under = world.GetBlockType(bx, surface - 1, bz);
                        if (under == BlockType.Water || under == BlockType.Still_Water || under == BlockType.Ice) {
                            continue; // in water: not there
                        }
                        return BrickGrid.CellToWorld(BrickGrid.BlockToCell(new Vector3Int(bx, surface, bz)), new Vector3(1, 0, 1));
                    }
                }
            }
            return position; // no dry ground near: the spot as it is
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn) {
            ConnectedPlayer connected = (ConnectedPlayer)conn.authenticationData;
            Vector3 position;
            Quaternion rotation;

            if (SavedPosition(connected.record, out position, out float yaw)) {
                rotation = Quaternion.Euler(0, yaw, 0);
            } else {
                position = ServerSpawnPosition(out rotation);
            }

            GameObject player = Instantiate(playerPrefab, position, rotation);
            player.name = playerPrefab.name + " [" + connected.record.Name + "]";

            PlayerNetwork network = player.GetComponent<PlayerNetwork>();
            network.playerName = connected.record.Name;
            network.role = connected.record.Role;
            network.ServerSetSpawn(position, rotation.eulerAngles.y);
            // with the health it left with (it left dead: back to life)
            Combat.Health health = player.GetComponent<Combat.Health>();
            int? saved = connected.record.Health;
            health.health = saved.HasValue && saved.Value > 0 ? Mathf.Min(saved.Value, health.maxHealth) : health.maxHealth;
            loadInventory(player.GetComponent<PlayerInventory>(), connected.record);

            NetworkServer.AddPlayerForConnection(conn, player);

            connected.hasJoined = true;
            ChatEvents.Send(new ChatEventMessage() { type = ChatEventType.Joined, player = connected.record.Name });
            ModScripts.ModEvent("onPlayerJoined", PlayerHandle.For(conn));
        }

        /// <summary>
        /// Where a joining player comes back (local position, and facing): where it left, or beside its bed if it left
        /// dead (like respawning, see PlayerNetwork.CmdRespawn); false for the world's spawn. Only in the generated
        /// world: the test scene shares the players database but is another place.
        /// </summary>
        public static bool SavedPosition(PlayerRecord record, out Vector3 position, out float yaw) {
            position = default(Vector3);
            yaw = 0;
            if (WorldBehaviour.Instance == null) {
                return false;
            }
            if (record.LeftDead) {
                if (!record.HasSpawnBed) {
                    return false;
                }
                position = FloatingOrigin.ToLocal(record.SpawnX.Value, record.SpawnY.Value, record.SpawnZ.Value);
                return true;
            }
            if (!record.HasLastPosition) {
                return false;
            }
            position = FloatingOrigin.ToLocal(record.LastX.Value, record.LastY.Value, record.LastZ.Value);
            yaw = record.LastYaw ?? 0;
            return true;
        }

        // absolute position and facing direction, saved as doubles so they're exact however far
        private void savePosition(NetworkConnectionToClient conn) {
            if (WorldBehaviour.Instance == null || conn.identity == null || !(conn.authenticationData is ConnectedPlayer player) || player.hasLeft) {
                return;
            }
            Transform transform = conn.identity.transform;
            FloatingOrigin.ToAbsolute(transform.position, out double x, out double y, out double z);
            Combat.Health health = conn.identity.GetComponent<Combat.Health>();
            Database.SavePosition(player.record, x, y, z, transform.eulerAngles.y, health.isDead ? 0 : health.health);
        }

        private void loadInventory(PlayerInventory inventory, PlayerRecord record) {
            inventory.ServerLoad(record.Id, Database.LoadInventory(record.Id));

            if (record.TimesJoined == 1 && inventory.items.Count == 0) {
                // a tutorial's world: what it needs instead, and its finished vehicle to look at
                Tutorials.Tutorial tutorial = WorldNetwork.Storage != null ? Tutorials.Tutorial.Find(WorldNetwork.Storage.Tutorial) : null;
                if (tutorial != null) {
                    tutorial.ServerGive(inventory);
                    tutorial.ServerBuildDemo(inventory.transform.position, record.Id);
                    return;
                }
                foreach (StarterItem starter in starterItems) {
                    inventory.ServerAdd(starter.itemId, Server.items.TryGetValue(starter.itemId, out Item item) ? item.color : BrickColor.None, starter.quantity);
                }
            }
        }

        public override void Update() {
            base.Update();

            if (NetworkServer.active) {
                WorldNetwork.ServerUpdate();
                Npcs.NpcSystem.ServerUpdate();
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
            ModUiClient.Start();

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
            NetworkClient.RegisterHandler<DisconnectReasonMessage>(message => disconnectReason = message.reason.Say());
            NetworkClient.RegisterHandler<GameTextMessage>(message => Events.EventManager.ChatLineReceived.Raise(new Events.ChatLineReceivedEvent() {
                sender = message.isEvent ? null : ChatCommands.ServerName,
                text = message.text.Say(),
            }));
        }

        public override void OnStopClient() {
            WorldNetwork.StopClient();
            ModUiClient.Stop();
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
                message = Loc.Get(Loc.UI, "network.couldnt_join", networkAddress); // a failed join leaves us in the menu
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
