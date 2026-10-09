using Brickcraft.Bricks;
using Brickcraft.Events;
using Brickcraft.World;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// Network side of a player. Players ask the server to change the world through commands,
    /// the server checks them, applies them through <see cref="WorldNetwork"/> and updates the
    /// player's <see cref="PlayerInventory"/>.
    ///
    /// Only the local player runs the first person controls; the others are just a body
    /// moved by their NetworkTransform.
    /// </summary>
    [RequireComponent(typeof(Player), typeof(PlayerInventory))]
    public class PlayerNetwork : NetworkBehaviour
    {
        // how far from the player the server accepts changes, a bit more than the player's reach
        private const float MaxReach = 8f;

        [SyncVar]
        public string playerName;

        // shown in the player list; changes raise EventManager.PlayerRoleChanged
        [SyncVar(hook = nameof(onRoleChanged))] public string role;
        [SyncVar] public int pingMs;

        private const float PingUpdateInterval = 2f;
        private float nextPingUpdate;

        // Absolute spawn point, set by the server. Mirror's spawn message carries the server's local
        // position, which means nothing with another floating origin, and owners of client authoritative
        // objects don't get their initial transform state, so the local player places itself from these.
        [SyncVar] public double spawnX;
        [SyncVar] public double spawnY;
        [SyncVar] public double spawnZ;
        [SyncVar] public float spawnYaw;

        private PlayerInventory inventory;

        private void Awake() {
            inventory = GetComponent<PlayerInventory>();
        }

        private void Update() {
            // the server measures every connection's round trip time
            if (isServer && connectionToClient != null && Time.unscaledTime >= nextPingUpdate) {
                nextPingUpdate = Time.unscaledTime + PingUpdateInterval;
                pingMs = (int)(connectionToClient.rtt * 1000);
            }
        }

        private void onRoleChanged(string oldRole, string newRole) {
            EventManager.PlayerRoleChanged.Raise(new PlayerRoleChangedEvent() {
                playerName = playerName,
                oldRole = oldRole,
                newRole = newRole,
                isLocalPlayer = isLocalPlayer,
            });
        }

        public override void OnStartClient() {
            if (!isLocalPlayer) {
                setupRemotePlayer();
            }
        }

        public override void OnStartLocalPlayer() {
            // before the first position is sent, and before mouse look reads the rotation in Start
            transform.SetPositionAndRotation(FloatingOrigin.ToLocal(spawnX, spawnY, spawnZ), Quaternion.Euler(0, spawnYaw, 0));

            // the character controller keeps its own position, it would move the player back otherwise
            Physics.SyncTransforms();

            GetComponent<Player>().onStartLocalPlayer();
        }

        [Server]
        public void ServerSetSpawn(Vector3 localPosition, float yaw) {
            // sync vars become properties once weaved, they can't be out parameters
            FloatingOrigin.ToAbsolute(localPosition, out double x, out double y, out double z);
            spawnX = x;
            spawnY = y;
            spawnZ = z;
            spawnYaw = yaw;
        }

        private void setupRemotePlayer() {
            GetComponent<Player>().enabled = false;
            GetComponent<FirstPersonController>().enabled = false;

            foreach (Camera cam in GetComponentsInChildren<Camera>(true)) {
                cam.enabled = false;
            }
            foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true)) {
                listener.enabled = false;
            }
            // only the local player swims
            foreach (TriggerDetector detector in GetComponentsInChildren<TriggerDetector>(true)) {
                detector.gameObject.SetActive(false);
            }

            // placeholder body, the local player doesn't see its own
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
        }

        // -------- client to server --------

        [Command]
        public void CmdPlaceBrick(int itemId, int health, Vector3Int origin, byte rotation) {
            if (!Server.items.TryGetValue(itemId, out Item item) || item.type != Item.Type.Brick || !inventory.ServerHas(itemId, 1, health)) {
                return;
            }
            BrickPlacement placement = new BrickPlacement(item.brickModel, origin, rotation);

            if (!isInReach(placement.WorldBounds.center) || !BrickGrid.IsFree(placement) || BrickPlacer.overlapsPlayer(placement)) {
                return;
            }

            // a 2x2 brick that fits exactly in a world block becomes part of the world
            bool placedAsWorldBlock = item.blockType.HasValue
                && placement.MatchesWorldBlock
                && WorldNetwork.ServerSetBlock(BrickGrid.CellToBlock(placement.origin), item.blockType.Value);

            if (!placedAsWorldBlock) {
                WorldNetwork.ServerPlaceBrick(item, placement);
            }
            inventory.ServerRemove(itemId, 1, health);
            TargetBrickPlaced();
        }

        [Command]
        public void CmdRemoveBrick(string brickId) {
            if (!Server.bricks.TryGetValue(brickId, out Brick brick) || !isInReach(brick.placement.WorldBounds.center)) {
                return;
            }
            WorldNetwork.ServerRemoveBrick(brick);
            inventory.ServerAdd(brick.itemId, 1);
        }

        [Command]
        public void CmdDigBlock(Vector3Int block) {
            if (WorldBehaviour.Instance == null) {
                return;
            }
            BlockType blockType = WorldBehaviour.Instance.GetBlockType(block);
            Vector3 blockCenter = BrickGrid.CellToWorld(BrickGrid.BlockToCell(block), new Vector3(1, 1.5f, 1));

            if (!BlockDatabase.Get(blockType).isBreakable || !isInReach(blockCenter)) {
                return;
            }
            if (WorldNetwork.ServerSetBlock(block, BlockType.Air)) {
                Item item = Server.getItemForBlock(blockType);

                if (item != null) {
                    inventory.ServerAdd(item.id, 1);
                }
            }
        }

        // a message every half a second at most
        private const double ChatInterval = 0.5;
        private double nextChatTime;

        [Command]
        public void CmdChat(string text) {
            text = cleanChatText(text);

            if (text.Length == 0 || NetworkTime.time < nextChatTime) {
                return;
            }
            nextChatTime = NetworkTime.time + ChatInterval;

            if (ChatCommands.IsCommand(text)) {
                Debug.Log("[Command] " + playerName + ": " + text);
                ChatCommands.Run(connectionToClient, text);
                return;
            }

            Debug.Log("[Chat] " + playerName + ": " + text);
            NetworkServer.SendToReady(new ChatMessage() { sender = playerName, text = text });
        }

        // one line of printable text, no longer than the chat allows
        private static string cleanChatText(string text) {
            if (text == null) {
                return "";
            }
            System.Text.StringBuilder clean = new System.Text.StringBuilder(text.Length);

            foreach (char c in text) {
                clean.Append(char.IsControl(c) ? ' ' : c);
            }
            string result = clean.ToString().Trim();

            return result.Length > ChatMessage.MaxLength ? result.Substring(0, ChatMessage.MaxLength) : result;
        }

        [Server]
        private bool isInReach(Vector3 position) {
            return Vector3.Distance(transform.position, position) <= MaxReach;
        }

        // -------- server to the player's client --------

        [TargetRpc]
        private void TargetBrickPlaced() {
            SoundManager.Instance.play(SoundManager.EFFECT_TAPPING);
        }
    }
}
