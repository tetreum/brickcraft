using Brickcraft.Bricks;
using Brickcraft.Combat;
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
    [RequireComponent(typeof(Player), typeof(PlayerInventory), typeof(Health))]
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
        private Health health;

        // a hit at most this often, from at most this far (a bit more than the player's reach, see Player)
        private const double AttackCooldown = 0.4;
        private const float MaxAttackReach = 6f;
        // how a hit pushes who it hits: away, and a little up
        private const float KnockbackSpeed = 6f;
        private const float KnockbackUp = 3f;
        private double nextAttackTime;

        private void Awake() {
            inventory = GetComponent<PlayerInventory>();
            health = GetComponent<Health>();
        }

        public override void OnStartServer() {
            health.ServerAllowDamage = allowDamage;
            health.ServerDied += onServerDied;
        }

        // mods can keep a player from being hurt
        [Server]
        private bool allowDamage(DamageInfo damage) {
            return ModScripts.ModEvent("onPlayerDamaged", PlayerHandle.For(connectionToClient), damage.amount, attackerHandle(damage));
        }

        [Server]
        private void onServerDied(DamageInfo damage) {
            ChatEvents.Send(new ChatEventMessage() { type = ChatEventType.Died, player = playerName, by = damage.attackerName });
            ModScripts.ModEvent("onPlayerDied", PlayerHandle.For(connectionToClient), attackerHandle(damage));
        }

        // the player that did it, null if it wasn't one
        private static PlayerHandle attackerHandle(DamageInfo damage) {
            NetworkIdentity attacker = damage.attacker != null ? damage.attacker.GetComponent<NetworkIdentity>() : null;
            return attacker != null && attacker.connectionToClient != null && attacker.GetComponent<PlayerNetwork>() != null
                ? PlayerHandle.For(attacker.connectionToClient)
                : null;
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

        public override void OnStopClient() {
            health.DeadChanged -= showBody;
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

            // what other players' crosshairs hit (the player object is on Ignore Raycast, see Player): its capsule
            CharacterController capsule = GetComponent<CharacterController>();
            GameObject hitbox = new GameObject("Hitbox");
            hitbox.layer = (int)Game.Layers.Default;
            hitbox.transform.SetParent(transform, false);
            CapsuleCollider collider = hitbox.AddComponent<CapsuleCollider>();
            collider.center = capsule.center;
            collider.height = capsule.height;
            collider.radius = capsule.radius;

            // dead players have neither
            showBody(health.isDead);
            health.DeadChanged += showBody;
        }

        private void showBody(bool dead) {
            foreach (string part in new[] { "Body", "Hitbox" }) {
                Transform child = transform.Find(part);
                if (child != null) {
                    child.gameObject.SetActive(!dead);
                }
            }
        }

        // -------- client to server --------

        /// <summary>
        /// Hits something that has health (a player when PvP is on, see WorldNetwork.Pvp) with what's
        /// in the inventory slot (its damage, see Item.damage).
        /// </summary>
        [Command]
        public void CmdAttack(NetworkIdentity target, int slot) {
            if (health.isDead || target == null || target == netIdentity || NetworkTime.time < nextAttackTime) {
                return;
            }
            Health victim = target.GetComponent<Health>();
            PlayerNetwork victimPlayer = target.GetComponent<PlayerNetwork>();
            if (victim == null || victim.isDead || Vector3.Distance(transform.position, target.transform.position) > MaxAttackReach
                || (victimPlayer != null && !WorldNetwork.Pvp)) {
                return;
            }
            nextAttackTime = NetworkTime.time + AttackCooldown;

            InventoryItem? held = inventory.ServerGetSlot(slot);
            int damage = held.HasValue && Server.items.TryGetValue(held.Value.itemId, out Item item) ? item.damage : Item.HandDamage;
            bool hurt = victim.ServerDamage(new DamageInfo() {
                amount = damage,
                kind = DamageInfo.Kind.Melee,
                attacker = gameObject,
                attackerName = playerName,
            });

            if (hurt) {
                Vector3 away = target.transform.position - transform.position;
                away.y = 0;
                Vector3 push = away.normalized * KnockbackSpeed + Vector3.up * KnockbackUp;
                if (victimPlayer != null) {
                    victimPlayer.ServerKnockback(push);
                } else if (target.TryGetComponent(out Npcs.Npc npc)) {
                    npc.ServerKnockback(push);
                }
            }
        }

        /// <summary>A dead player comes back at the spawn, with all its health (and its inventory).</summary>
        [Command]
        public void CmdRespawn() {
            if (!health.isDead) {
                return;
            }
            ServerTeleport(BrickcraftNetworkManager.Instance.ServerSpawnPosition(out _));
            health.ServerRevive();
        }

        /// <summary>Places the brick held in the inventory slot.</summary>
        [Command]
        public void CmdPlaceBrick(int slot, Vector3Int origin, byte rotation) {
            if (health.isDead) {
                return;
            }
            InventoryItem? held = inventory.ServerGetSlot(slot);
            if (!held.HasValue || !Server.items.TryGetValue(held.Value.itemId, out Item item) || item.type != Item.Type.Brick
                || item.brickModel.IsAttachment) {
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

        /// <summary>Puts the attachment held in the inventory slot (a door...) in a brick's slot, see BrickSlot.</summary>
        [Command]
        public void CmdAttachBrick(int slot, string holderId) {
            if (health.isDead) {
                return;
            }
            InventoryItem? held = inventory.ServerGetSlot(slot);
            if (!held.HasValue || !Server.items.TryGetValue(held.Value.itemId, out Item item) || item.type != Item.Type.Brick
                || !Server.bricks.TryGetValue(holderId, out Brick holder) || !CanAttach(item.brickModel, holder)
                || !isInReach(holder.placement.WorldBounds.center)) {
                return;
            }
            ConnectedPlayer player = connectionToClient.authenticationData as ConnectedPlayer;
            Placer placer = Placer.Now(player != null ? player.record.Id : 0);
            Brick brick = WorldNetwork.ServerAttachBrick(item, item.ValidColor(held.Value.color), holder, placer);

            inventory.ServerRemoveFromSlot(slot, 1);
            TargetBrickPlaced();
            ModScripts.ItemEvent(item.id, "onPlaced", BrickHandle.ForBrick(brick, placer), PlayerHandle.For(connectionToClient));
        }

        /// <summary>Whether bricks of the model can go in the brick's slot now: it fits, and it's free.</summary>
        public static bool CanAttach(BrickModel model, Brick holder) {
            BrickSlot slot = Server.slotOf(holder);
            return model.IsAttachment && holder.attachedTo == null && slot != null && slot.fits == model.attachesTo
                && Server.attachmentOf(holder) == null;
        }

        [Command]
        public void CmdRemoveBrick(string brickId) {
            if (health.isDead || !Server.bricks.TryGetValue(brickId, out Brick brick) || !isInReach(brick.placement.WorldBounds.center)) {
                return;
            }
            // its script can make it resist
            BrickHandle handle = BrickHandle.ForBrick(brick, WorldNetwork.ServerPlacerOf(brick));
            PlayerHandle player = PlayerHandle.For(connectionToClient);
            if (!ModScripts.ItemEvent(brick.itemId, "onHit", handle, player)) {
                return;
            }
            // what was attached to it comes along
            foreach (Brick removed in WorldNetwork.ServerRemoveBrick(brick)) {
                inventory.ServerAdd(removed.itemId, removed.color, 1);
            }
            ModScripts.ItemEvent(brick.itemId, "onBroken", handle, player);
        }

        /// <summary>A click on a brick with a door (see BrickDoor): opens or closes it.</summary>
        [Command]
        public void CmdToggleDoor(string brickId) {
            if (health.isDead || !Server.bricks.TryGetValue(brickId, out Brick brick) || !isInReach(brick.placement.WorldBounds.center)
                || brick.model.prefab.GetComponentInChildren<BrickDoor>(true) == null) {
                return;
            }
            WorldNetwork.ServerSetBrickState(brick, brick.state == BrickDoor.Open ? BrickDoor.Closed : BrickDoor.Open);
        }

        /// <summary>The use key on a brick (see ModScripts, onInteract).</summary>
        [Command]
        public void CmdInteractBrick(string brickId) {
            if (!health.isDead && Server.bricks.TryGetValue(brickId, out Brick brick) && isInReach(brick.placement.WorldBounds.center)
                && ModScripts.HasItemEvent(brick.itemId, "onInteract")) {
                ModScripts.ItemEvent(brick.itemId, "onInteract", BrickHandle.ForBrick(brick, WorldNetwork.ServerPlacerOf(brick)), PlayerHandle.For(connectionToClient));
            }
        }

        /// <summary>The use key on a world block (see ModScripts, onInteract).</summary>
        [Command]
        public void CmdInteractBlock(Vector3Int block) {
            if (health.isDead || WorldBehaviour.Instance == null) {
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
            if (health.isDead || WorldBehaviour.Instance == null) {
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

        /// <summary>Pushes the player (a hit): its client moves it.</summary>
        [Server]
        public void ServerKnockback(Vector3 velocity) {
            if (connectionToClient != null) {
                TargetKnockback(velocity);
            }
        }

        [TargetRpc]
        private void TargetKnockback(Vector3 velocity) {
            GetComponent<FirstPersonController>().Knockback(velocity);
        }

        [TargetRpc]
        private void TargetTeleport(double x, double y, double z) {
            transform.position = FloatingOrigin.ToLocal(x, y, z);
            // the character controller keeps its own position, it would move the player back otherwise
            Physics.SyncTransforms();
        }
    }
}
