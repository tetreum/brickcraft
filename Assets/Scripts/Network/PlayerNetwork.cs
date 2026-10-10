using Brickcraft.Bricks;
using Brickcraft.Events;
using Brickcraft.Scripting;
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

        /// <summary>What other players see of this one, see PlayerCharacter.</summary>
        public GameObject characterPrefab;

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

            // the animated body, the local player doesn't see its own
            if (characterPrefab != null) {
                GameObject body = Instantiate(characterPrefab, transform, false);
                body.name = "Body";
            }
        }

        // -------- client to server --------

        /// <summary>Places the brick held in the inventory slot.</summary>
        [Command]
        public void CmdPlaceBrick(int slot, Vector3Int origin, byte rotation) {
            InventoryItem? held = inventory.ServerGetSlot(slot);
            if (!held.HasValue || !Server.items.TryGetValue(held.Value.itemId, out Item item) || item.type != Item.Type.Brick) {
                return;
            }
            BrickPlacement placement = new BrickPlacement(item.brickModel, origin, rotation);

            if (!isInReach(placement.WorldBounds.center) || !BrickGrid.IsFree(placement) || BrickPlacer.overlapsPlayer(placement)) {
                return;
            }

            // the save remembers who placed it and when
            ConnectedPlayer player = connectionToClient.authenticationData as ConnectedPlayer;
            Placer placer = Placer.Now(player != null ? player.record.Id : 0);

            int color = item.ValidColor(held.Value.color);

            // a 2x2 brick that fits exactly in a world block becomes part of the world, in its colour
            // (blocks in their item's default colour are drawn with its textures)
            Vector3Int block = BrickGrid.CellToBlock(placement.origin);
            int blockColor = color == item.color ? BrickColor.None : color;
            bool placedAsWorldBlock = item.blockType.HasValue
                && placement.MatchesWorldBlock
                && WorldNetwork.ServerSetBlock(block, item.blockType.Value, placer, blockColor);

            BrickHandle placed = placedAsWorldBlock
                ? BrickHandle.ForBlock(block, item.blockType.Value, blockColor, placer)
                : BrickHandle.ForBrick(WorldNetwork.ServerPlaceBrick(item, color, placement, placer), placer);

            inventory.ServerRemoveFromSlot(slot, 1);
            TargetBrickPlaced();
            ModScripts.ItemEvent(item.id, "onPlaced", placed, PlayerHandle.For(connectionToClient));
        }

        [Command]
        public void CmdRemoveBrick(string brickId) {
            if (!Server.bricks.TryGetValue(brickId, out Brick brick) || !isInReach(brick.placement.WorldBounds.center)) {
                return;
            }
            // its script can make it resist
            BrickHandle handle = BrickHandle.ForBrick(brick, WorldNetwork.ServerPlacerOf(brick));
            PlayerHandle player = PlayerHandle.For(connectionToClient);
            if (!ModScripts.ItemEvent(brick.itemId, "onHit", handle, player)) {
                return;
            }
            WorldNetwork.ServerRemoveBrick(brick);
            inventory.ServerAdd(brick.itemId, brick.color, 1);
            ModScripts.ItemEvent(brick.itemId, "onBroken", handle, player);
        }

        /// <summary>The use key on a brick (see ModScripts, onInteract).</summary>
        [Command]
        public void CmdInteractBrick(string brickId) {
            if (Server.bricks.TryGetValue(brickId, out Brick brick) && isInReach(brick.placement.WorldBounds.center)
                && ModScripts.HasItemEvent(brick.itemId, "onInteract")) {
                ModScripts.ItemEvent(brick.itemId, "onInteract", BrickHandle.ForBrick(brick, WorldNetwork.ServerPlacerOf(brick)), PlayerHandle.For(connectionToClient));
            }
        }

        /// <summary>The use key on a world block (see ModScripts, onInteract).</summary>
        [Command]
        public void CmdInteractBlock(Vector3Int block) {
            if (WorldBehaviour.Instance == null) {
                return;
            }
            BlockType type = WorldBehaviour.Instance.GetBlockType(block);
            string itemId = BlockDatabase.Get(type).itemId;
            Vector3 center = BrickGrid.CellToWorld(BrickGrid.BlockToCell(block), new Vector3(1, 1.5f, 1));

            if (isInReach(center) && ModScripts.HasItemEvent(itemId, "onInteract")) {
                BrickHandle handle = BrickHandle.ForBlock(block, type, WorldBehaviour.Instance.GetBlockColor(block), WorldNetwork.ServerPlacerOf(block));
                ModScripts.ItemEvent(itemId, "onInteract", handle, PlayerHandle.For(connectionToClient));
            }
        }

        [Command]
        public void CmdDigBlock(Vector3Int block) {
            if (WorldBehaviour.Instance == null) {
                return;
            }
            BlockType blockType = WorldBehaviour.Instance.GetBlockType(block);
            int blockColor = WorldBehaviour.Instance.GetBlockColor(block);
            Vector3 blockCenter = BrickGrid.CellToWorld(BrickGrid.BlockToCell(block), new Vector3(1, 1.5f, 1));

            if (!BlockDatabase.Get(blockType).isBreakable || !isInReach(blockCenter)) {
                return;
            }
            // its script can make it resist
            string blockItem = BlockDatabase.Get(blockType).itemId;
            BrickHandle handle = BrickHandle.ForBlock(block, blockType, blockColor, WorldNetwork.ServerPlacerOf(block));
            PlayerHandle player = PlayerHandle.For(connectionToClient);
            if (!ModScripts.ItemEvent(blockItem, "onHit", handle, player)) {
                return;
            }
            if (WorldNetwork.ServerSetBlock(block, BlockType.Air, null)) {
                // a block placed with a colour gives back its brick in that colour
                Item item = blockColor != BrickColor.None
                    ? (Server.items.TryGetValue(BlockDatabase.Get(blockType).itemId ?? "", out Item colored) ? colored : null)
                    : Server.getItemForBlock(blockType);

                if (item != null) {
                    inventory.ServerAdd(item.id, blockColor != BrickColor.None ? blockColor : item.color, 1);
                }
                ModScripts.ItemEvent(blockItem, "onBroken", handle, player);
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
                ChatCommands.Run(connectionToClient, text);
                return;
            }
            // mods can keep a message from being sent
            if (!ModScripts.ModEvent("onChat", PlayerHandle.For(connectionToClient), text)) {
                return;
            }

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

        /// <summary>
        /// Moves the player there. Players move themselves (their transform is client authoritative), so
        /// the server tells its client, in absolute coordinates as each side has its own floating origin.
        /// </summary>
        [Server]
        public void ServerTeleport(Vector3 localPosition) {
            FloatingOrigin.ToAbsolute(localPosition, out double x, out double y, out double z);
            TargetTeleport(x, y, z);
        }

        // -------- server to the player's client --------

        [TargetRpc]
        private void TargetBrickPlaced() {
            SoundManager.Instance.play(SoundManager.EFFECT_TAPPING);
        }

        [TargetRpc]
        private void TargetTeleport(double x, double y, double z) {
            transform.position = FloatingOrigin.ToLocal(x, y, z);
            // the character controller keeps its own position, it would move the player back otherwise
            Physics.SyncTransforms();
        }
    }
}
