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
        /// <summary>The bed it sleeps in (a brick id), empty when awake, see PlayerSleep.</summary>
        [SyncVar(hook = nameof(onSleepingChanged))] public string sleepingIn = "";
        [SyncVar] public int pingMs;

        // what everybody sees it hold (see HeldItems): the item in its hand (its selected hotbar slot's) and
        // colour, what it wears in its left hand, and whether it's blocking with it (CmdBlock); empty for nothing
        [SyncVar(hook = nameof(onHeldChanged))] public string heldItem = "";
        [SyncVar(hook = nameof(onHeldColorChanged))] public int heldColor;
        [SyncVar(hook = nameof(onHeldChanged))] public string offhandItem = "";
        [SyncVar] public bool blocking;

        /// <summary>
        /// Air left, in tenths of a second (see MaxAir): with its eyes under water it runs out, and then it drowns
        /// (DrownDamage every DrownInterval); out of the water it comes back. Its owner sees it (EventManager.LocalPlayerAirChanged).
        /// </summary>
        [SyncVar(hook = nameof(onAirChanged))] public int air = MaxAir * 10;
        /// <summary>Seconds of air under water before drowning.</summary>
        public const int MaxAir = 15;
        // how fast air comes back out of the water (seconds of air a second), and drowning's damage and pace
        private const float AirRefill = 5;
        private const int DrownDamage = 2;
        private const double DrownInterval = 1;
        // server: the air left (air is it rounded), and when drowning hurts next
        private float airLeft = MaxAir;
        private double nextDrown;

        // server: the slot whose item is in its hand, see CmdSelectSlot
        private int selectedSlot = Inventory.FirstFastSlot;
        // hits from within this angle of where it looks are blocked by its shield (90: anything in front)
        private const float BlockAngle = 90;

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

        // NPCs are used from at most this far (a bit more than the player's reach, see Player)
        private const float MaxAttackReach = 6f;
        // hits go a bit farther than what's in hand reaches, and a bit faster than its attack speed: the client's
        // clicks and positions arrive late, and not evenly
        private const float AttackReachMargin = 1f;
        private const double AttackSpeedMargin = 0.9;
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
            health.ServerReduceDamage = reduceDamage;
            health.ServerDied += onServerDied;
            inventory.ServerChanged += refreshHeld;
            refreshHeld();
        }

        public override void OnStopServer() {
            inventory.ServerChanged -= refreshHeld;
        }

        // a hit it takes: blocked by its shield (which wears), or eased by what it wears
        [Server]
        private int reduceDamage(DamageInfo damage) {
            if (damage.kind == DamageInfo.Kind.Drowning) {
                return damage.amount;
            }
            if (blocking && damage.kind != DamageInfo.Kind.Script && comesFromFront(damage)) {
                inventory.ServerWear(Inventory.EquipmentSlot(Item.ArmorSlot.Offhand));
                return 0;
            }
            return inventory.ServerAbsorb(damage);
        }

        // whether the hit comes from in front of the player, where its shield covers: the way a shot flies, or
        // where its attacker is
        private bool comesFromFront(DamageInfo damage) {
            Vector3 toward;
            if (damage.direction != Vector3.zero) {
                toward = -damage.direction;
            } else if (damage.attacker != null) {
                toward = damage.attacker.transform.position - transform.position;
            } else {
                return false;
            }
            toward.y = 0;
            return toward.sqrMagnitude < 0.0001f || Vector3.Angle(transform.forward, toward) <= BlockAngle;
        }

        // what it holds, for everybody, after its inventory or selected slot changed; without a shield it can't block
        [Server]
        private void refreshHeld() {
            InventoryItem? held = inventory.ServerGetSlot(selectedSlot);
            InventoryItem? offhand = inventory.ServerGetSlot(Inventory.EquipmentSlot(Item.ArmorSlot.Offhand));
            heldItem = held.HasValue ? held.Value.itemId : "";
            heldColor = held.HasValue ? held.Value.color : 0;
            offhandItem = offhand.HasValue ? offhand.Value.itemId : "";
            if (blocking && !canBlock()) {
                blocking = false;
            }
        }

        // alive, awake, with a shield in its left hand
        [Server]
        private bool canBlock() {
            return !health.isDead && string.IsNullOrEmpty(sleepingIn)
                && Server.items.TryGetValue(offhandItem, out Item shield) && shield.IsShield;
        }

        private void onHeldChanged(string oldItem, string newItem) {
            if (heldItems != null) {
                heldItems.Refresh();
            }
        }

        private void onHeldColorChanged(int oldColor, int newColor) {
            onHeldChanged(null, null);
        }

        /// <summary>Client: the held items of this player, see HeldItems.</summary>
        public HeldItems heldItems { get; private set; }

        // mods can keep a player from being hurt
        [Server]
        private bool allowDamage(DamageInfo damage) {
            return ModScripts.ModEvent("onPlayerDamaged", PlayerHandle.For(connectionToClient), damage.amount, attackerHandle(damage));
        }

        [Server]
        private void onServerDied(DamageInfo damage) {
            blocking = false;
            ChatEventType type = damage.kind == DamageInfo.Kind.Drowning ? ChatEventType.Drowned : ChatEventType.Died;
            ChatEvents.Send(new ChatEventMessage() { type = type, player = playerName, by = damage.attackerName });
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
            if (isServer) {
                serverBreathe();
            }
        }

        // with its eyes under water its air runs out, then it drowns; out of it, air comes back
        [Server]
        private void serverBreathe() {
            bool under = !health.isDead && World.Water.IsAt(transform.position + Vector3.up * EyeHeight);
            airLeft = under ? Mathf.Max(0, airLeft - Time.deltaTime) : Mathf.Min(MaxAir, airLeft + AirRefill * Time.deltaTime);
            int shown = Mathf.CeilToInt(airLeft * 10);
            if (shown != air) {
                air = shown;
            }
            if (!under || airLeft > 0) {
                nextDrown = NetworkTime.time + DrownInterval;
                return;
            }
            if (NetworkTime.time >= nextDrown) {
                nextDrown = NetworkTime.time + DrownInterval;
                if (health.ServerDamage(new DamageInfo() { amount = DrownDamage, kind = DamageInfo.Kind.Drowning })) {
                    RpcDrowning();
                }
            }
        }

        // everybody hears it drown (itself over everything, the others from where it is), a clip at a time
        [ClientRpc]
        private void RpcDrowning() {
            if (Time.time < nextDrowningSound) {
                return;
            }
            nextDrowningSound = Time.time + DrowningSoundInterval;
            if (isLocalPlayer) {
                SoundManager.Instance.playOnce(SoundManager.EFFECT_DROWNING);
            } else {
                SoundManager.Instance.playAt(SoundManager.EFFECT_DROWNING, transform.position);
            }
        }

        // client: when its drowning can be heard again (its clip lasts about this)
        private float nextDrowningSound;
        private const float DrowningSoundInterval = 3.2f;

        private void onAirChanged(int oldAir, int newAir) {
            if (isLocalPlayer) {
                EventManager.LocalPlayerAirChanged.Raise(new LocalPlayerAirChangedEvent() { air = newAir / 10f, maxAir = MaxAir });
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
                heldItems = HeldItems.ForBody(this);
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
            heldItems = HeldItems.FirstPerson(this, GetComponentInChildren<Camera>());
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
        /// Hits something that has health (a player when PvP is on, see WorldNetwork.Pvp) with what's in the
        /// inventory slot: its damage, as often as its attack speed allows (see Item), within reach. Weapons that
        /// wear get worn by what they hurt. Ranged weapons shoot instead (CmdShoot).
        /// </summary>
        [Command]
        public void CmdAttack(NetworkIdentity target, int slot) {
            if (health.isDead || blocking || target == null || target == netIdentity || NetworkTime.time < nextAttackTime) {
                return;
            }
            InventoryItem? held = inventory.ServerGetSlot(slot);
            Item weapon = held.HasValue && Server.items.TryGetValue(held.Value.itemId, out Item item) ? item : null;
            float attackSpeed = weapon != null ? weapon.attackSpeed : Item.HandAttackSpeed;

            Health victim = target.GetComponent<Health>();
            PlayerNetwork victimPlayer = target.GetComponent<PlayerNetwork>();
            if (victim == null || victim.isDead || (weapon != null && weapon.IsRanged)
                || Vector3.Distance(transform.position, target.transform.position) > Item.HandReach + AttackReachMargin || !ServerCanHit(target)) {
                return;
            }
            nextAttackTime = NetworkTime.time + AttackSpeedMargin / attackSpeed;
            Npcs.NpcPerception.MakeNoise(transform.position, FightNoise, health, Npcs.Noise.Fight);
            RpcSwing();

            bool hurt = victim.ServerDamage(new DamageInfo() {
                amount = weapon != null ? weapon.damage : Item.HandDamage,
                kind = DamageInfo.Kind.Melee,
                attacker = gameObject,
                attackerName = playerName,
            });
            if (hurt) {
                inventory.ServerWear(slot);
            }

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

        // server: when it started drawing its ranged weapon (CmdDraw), -1 if it isn't
        private double drawStartedAt = -1;
        // the draw it says it did can be this much longer than what the server saw (commands arrive unevenly)
        private const double DrawLeeway = 0.15;

        /// <summary>It starts drawing its ranged weapon (holding the attack button): the longer, the stronger the shot (CmdShoot).</summary>
        [Command]
        public void CmdDraw() {
            drawStartedAt = NetworkTime.time;
            RpcDraw();
        }

        // the others hear it draw (its own client already does)
        [ClientRpc(includeOwner = false)]
        private void RpcDraw() {
            SoundManager.Instance.playAt(SoundManager.EFFECT_STRING_BOW, transform.position);
        }

        /// <summary>
        /// Shoots the ranged weapon in the inventory slot (a bow) that way (where its camera looks), drawn that much
        /// (0 to 1, at most as long as it was drawn since CmdDraw): one of its ammo flies off (see Projectile), the
        /// faster and the harder it hits the more drawn, as often as its attack speed allows. Each shot wears the weapon.
        /// </summary>
        [Command]
        public void CmdShoot(int slot, Vector3 direction, float power) {
            double drawn = drawStartedAt >= 0 ? NetworkTime.time - drawStartedAt + DrawLeeway : 0;
            drawStartedAt = -1;
            if (health.isDead || blocking || NetworkTime.time < nextAttackTime || direction.sqrMagnitude < 0.0001f) {
                return;
            }
            InventoryItem? held = inventory.ServerGetSlot(slot);
            if (!held.HasValue || !Server.items.TryGetValue(held.Value.itemId, out Item weapon) || !weapon.IsRanged) {
                return;
            }
            // the first stack of its ammo, whose colour the shot keeps
            int ammo = -1;
            for (int i = 0; i < inventory.items.Count && ammo == -1; i++) {
                if (inventory.items[i].itemId == weapon.ammo && !Inventory.IsEquipmentSlot(inventory.items[i].slot)) {
                    ammo = i;
                }
            }
            if (ammo == -1) {
                return; // nothing to shoot
            }
            InventoryItem shot = inventory.items[ammo];
            if (!inventory.ServerRemoveFromSlot(shot.slot, 1)) {
                return;
            }
            nextAttackTime = NetworkTime.time + AttackSpeedMargin / weapon.attackSpeed;
            direction.Normalize();
            power = Mathf.Clamp(Mathf.Min(power, (float)(drawn / weapon.drawTime)), Item.MinShotPower, 1);
            Vector3 eye = transform.position + Vector3.up * EyeHeight;
            Projectile.ServerShoot(this, shot.itemId, shot.color, eye + direction * ShotStart, direction * (weapon.ShotSpeed * power),
                Mathf.Max(1, Mathf.RoundToInt(weapon.damage * power)), ShotKnockback * power);
            Npcs.NpcPerception.MakeNoise(transform.position, FightNoise, health, Npcs.Noise.Fight);
            RpcSwing();
            inventory.ServerWear(slot);
        }

        // where shots leave from: its eyes (the camera's height), a bit ahead
        private const float EyeHeight = 0.74f;
        private const float ShotStart = 0.6f;
        private const float ShotKnockback = 3f;

        /// <summary>Server: whether this player can hurt that: players only with PvP, nobody their own pets (others' with PvP).</summary>
        [Server]
        public bool ServerCanHit(NetworkIdentity target) {
            return target != null && (target.GetComponent<PlayerNetwork>() == null || WorldNetwork.Pvp) && canHitPet(target);
        }

        // the others see it swing and hear it (its own client already did)
        [ClientRpc(includeOwner = false)]
        private void RpcSwing() {
            if (heldItems != null) {
                heldItems.Swing();
            }
            Server.items.TryGetValue(heldItem ?? "", out Item held);
            SoundManager.Instance.playAt(SoundManager.HitSound(held), transform.position);
        }

        /// <summary>The hotbar slot whose item is in its hand (what the others see it hold), -1 for none.</summary>
        [Command]
        public void CmdSelectSlot(int slot) {
            selectedSlot = slot;
            refreshHeld();
        }

        /// <summary>
        /// Raises its shield (the one worn in its left hand) or lowers it. Raised, hits from the front do nothing
        /// but wear the shield (it breaks once worn out), and it can't attack.
        /// </summary>
        [Command]
        public void CmdBlock(bool raise) {
            blocking = raise && canBlock();
        }

        // nobody hits their own pets, and others' only with PvP (pets are their owner's)
        private bool canHitPet(NetworkIdentity target) {
            if (!target.TryGetComponent(out Npcs.Npc npc) || npc.Brain == null || npc.Brain.Owner == 0) {
                return true;
            }
            ConnectedPlayer player = connectionToClient.authenticationData as ConnectedPlayer;
            return player != null && npc.Brain.Owner != player.record.Id && WorldNetwork.Pvp;
        }

        // how far noises carry, for NPCs (see Npcs.NpcPerception)
        private const float FightNoise = 10;
        private const float DigNoise = 8;
        private const float PlaceNoise = 6;
        private const float DoorNoise = 8;

        /// <summary>
        /// The use key on an NPC: its script's onInteract first; then holding what tames it, it becomes the
        /// player's pet; on one of their pets, its menu (TargetPetMenu).
        /// </summary>
        [Command]
        public void CmdInteractNpc(NetworkIdentity target, int slot) {
            if (health.isDead || target == null || !target.TryGetComponent(out Npcs.Npc npc) || npc.Brain == null || npc.Health.isDead
                    || Vector3.Distance(transform.position, target.transform.position) > MaxAttackReach) {
                return;
            }
            if (!ModScripts.NpcEvent(npc.type, "onInteract", script => new object[] { NpcHandle.For(npc), PlayerHandle.For(connectionToClient) })) {
                return;
            }
            ConnectedPlayer player = connectionToClient.authenticationData as ConnectedPlayer;
            if (player == null) {
                return;
            }
            if (npc.Brain.Owner == player.record.Id) {
                TargetPetMenu(npc.netId, npc.Info.name, npc.Brain.IsFollowing, (byte)npc.Brain.Attacks);
                return;
            }
            InventoryItem? held = inventory.ServerGetSlot(slot);
            if (npc.Brain.Owner == 0 && !string.IsNullOrEmpty(npc.Info.tameWith) && held.HasValue && held.Value.itemId == npc.Info.tameWith) {
                inventory.ServerRemoveFromSlot(slot, 1);
                npc.Brain.Tame(player.record.Id, player.record.Name);
                connectionToClient.Send(new ChatMessage() { sender = ChatCommands.ServerName, text = "The " + npc.Info.name + " is yours now. Use it again for its orders." });
            }
        }

        /// <summary>A pet's orders from its owner: follow (or stay where it is), and what to attack.</summary>
        [Command]
        public void CmdPetOrders(uint npcId, bool following, byte attacks) {
            ConnectedPlayer player = connectionToClient.authenticationData as ConnectedPlayer;
            if (player == null || !NetworkServer.spawned.TryGetValue(npcId, out NetworkIdentity identity) || !identity.TryGetComponent(out Npcs.Npc npc)
                    || npc.Brain == null || npc.Brain.Owner != player.record.Id || attacks > (byte)Npcs.NpcBrain.PetAttack.Everyone) {
                return;
            }
            npc.Brain.SetOrders(following, (Npcs.NpcBrain.PetAttack)attacks);
        }

        [TargetRpc]
        private void TargetPetMenu(uint npcId, string name, bool following, byte attacks) {
            EventManager.PetMenuRequested.Raise(new PetMenuRequestedEvent() { npcId = npcId, name = name, following = following, attacks = attacks });
        }

        /// <summary>A dead player comes back beside the bed it last slept in, or at the world's spawn, with all its health and air (and its inventory).</summary>
        [Command]
        public void CmdRespawn() {
            if (!health.isDead) {
                return;
            }
            ServerTeleport(PlayerSleep.SpawnPoint(this, out Vector3 bed) ? bed : BrickcraftNetworkManager.Instance.ServerSpawnPosition(out _));
            health.ServerRevive();
            airLeft = MaxAir;
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
            Npcs.NpcPerception.MakeNoise(placement.WorldBounds.center, PlaceNoise, health, Npcs.Noise.Place);
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
            Npcs.NpcPerception.MakeNoise(brick.placement.WorldBounds.center, DigNoise, health, Npcs.Noise.Dig);
            // what was attached to it comes along
            foreach (Brick removed in WorldNetwork.ServerRemoveBrick(brick)) {
                inventory.ServerAdd(removed.itemId, removed.color, 1);
            }
            wearDigTool();
            ModScripts.ItemEvent(brick.itemId, "onBroken", handle, player);
        }

        /// <summary>A click on a storage brick (or its lid): looks inside, or hears why not (see BrickStorage).</summary>
        [Command]
        public void CmdOpenStorage(string brickId) {
            if (health.isDead || !Server.bricks.TryGetValue(brickId, out Brick clicked)) {
                return;
            }
            Brick storage = BrickStorage.StorageOf(clicked);
            if (storage == null || !isInReach(storage.placement.WorldBounds.center)) {
                return;
            }
            if (!BrickStorage.CanOpen(connectionToClient, storage)) {
                connectionToClient.Send(new ChatMessage() { sender = ChatCommands.ServerName, text = "It's locked: only its owner can open it" });
                return;
            }
            if (BrickStorage.IsInUse(storage, connectionToClient)) {
                connectionToClient.Send(new ChatMessage() { sender = ChatCommands.ServerName, text = "Someone else is looking inside" });
                return;
            }
            // a mod can keep it closed
            if (ModScripts.HasItemEvent(storage.itemId, "onStorageOpened")
                    && !ModScripts.ItemEvent(storage.itemId, "onStorageOpened", BrickHandle.ForBrick(storage, WorldNetwork.ServerPlacerOf(storage)), PlayerHandle.For(connectionToClient))) {
                return;
            }
            BrickStorage.ServerOpen(connectionToClient, storage);
        }

        [Command]
        public void CmdCloseStorage(string brickId) {
            BrickStorage.ServerClose(connectionToClient, brickId, false);
        }

        /// <summary>It picked up something from the ground (see DroppedItems).</summary>
        [Server]
        public void ServerPickedUp() {
            if (connectionToClient != null) {
                TargetPickedUp();
            }
        }

        [TargetRpc]
        private void TargetPickedUp() {
            SoundManager.Instance.play(SoundManager.EFFECT_TAPPING);
        }

        /// <summary>A click on a bed: sleeps in it (see PlayerSleep), or hears why not.</summary>
        [Command]
        public void CmdSleep(string brickId) {
            if (health.isDead || !string.IsNullOrEmpty(sleepingIn) || !Server.bricks.TryGetValue(brickId, out Brick bed)
                    || !isInReach(bed.placement.WorldBounds.center)) {
                return;
            }
            string problem = PlayerSleep.WhyNot(this, bed);
            if (problem != null) {
                connectionToClient.Send(new ChatMessage() { sender = ChatCommands.ServerName, text = problem });
                return;
            }
            PlayerSleep.Sleep(this, bed);
        }

        [Command]
        public void CmdStopSleeping() {
            PlayerSleep.Wake(this);
        }

        /// <summary>Puts the player lying in a bed: feet on the point, head the other way from where it faces.</summary>
        [Server]
        public void ServerLieDown(Vector3 feet, Quaternion rotation) {
            // the player's position is the middle of its body, lying it's above the mattress halfway to its head
            Vector3 middle = feet - rotation * Vector3.forward * 0.9f + Vector3.up * 0.15f;
            FloatingOrigin.ToAbsolute(middle, out double x, out double y, out double z);
            TargetLieDown(x, y, z, rotation.eulerAngles.y);
        }

        [TargetRpc]
        private void TargetLieDown(double x, double y, double z, float yaw) {
            transform.SetPositionAndRotation(FloatingOrigin.ToLocal(x, y, z), Quaternion.Euler(0, yaw, 0));
            Physics.SyncTransforms();
            showSleeping();
        }

        // the local player sleeping can't move and looks up (behind the sleep panel); others see it lying
        private void onSleepingChanged(string oldBed, string newBed) {
            showSleeping();
        }

        private void showSleeping() {
            bool asleep = !string.IsNullOrEmpty(sleepingIn);
            if (isLocalPlayer) {
                Player player = GetComponent<Player>();
                if (asleep) {
                    player.freeze(Player.FreezeReason.Sleeping);
                    Camera view = GetComponentInChildren<Camera>();
                    if (view != null) {
                        view.transform.localRotation = Quaternion.Euler(-80, 0, 0);
                    }
                } else {
                    player.unFreeze(Player.FreezeReason.Sleeping);
                }
                return;
            }
            Transform body = transform.Find("Body");
            if (body != null) {
                // on its back: its head goes the other way from where it faces
                body.localRotation = asleep ? Quaternion.Euler(-90, 0, 0) : Quaternion.identity;
            }
        }

        /// <summary>A click on a brick with a door (see BrickDoor): opens or closes it.</summary>
        [Command]
        public void CmdToggleDoor(string brickId) {
            if (health.isDead || !Server.bricks.TryGetValue(brickId, out Brick brick) || !isInReach(brick.placement.WorldBounds.center)
                || brick.model.prefab.GetComponentInChildren<BrickDoor>(true) == null) {
                return;
            }
            WorldNetwork.ServerSetBrickState(brick, brick.state == BrickDoor.Open ? BrickDoor.Closed : BrickDoor.Open);
            Npcs.NpcPerception.MakeNoise(brick.placement.WorldBounds.center, DoorNoise, health, Npcs.Noise.Door);
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
                Npcs.NpcPerception.MakeNoise(blockCenter, DigNoise, health, Npcs.Noise.Dig);
                wearDigTool();
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

        /// <summary>An empty bucket in the slot picks up the liquid of that block (its block goes, the bucket is full).</summary>
        [Command]
        public void CmdFillBucket(int slot, Vector3Int block) {
            InventoryItem? held = inventory.ServerGetSlot(slot);
            if (health.isDead || WorldBehaviour.Instance == null || !held.HasValue || !Server.items.TryGetValue(held.Value.itemId, out Item item)
                    || item.bucket == null || item.bucket.filled == null || !Server.items.TryGetValue(item.bucket.filled, out Item full)
                    || !Server.items.TryGetValue(full.bucket.liquid, out Item liquid)) {
                return;
            }
            // its sources only (still water too), not what flows from them
            if (!Water.IsSourceOf(WorldBehaviour.Instance.GetBlockType(block), liquid.blockType.Value) || !isInReach(blockCenter(block))) {
                return;
            }
            if (WorldNetwork.ServerSetBlock(block, BlockType.Air, null)) {
                swapBucket(slot, full);
            }
        }

        /// <summary>A full bucket in the slot pours its liquid in that block, if it's empty (the bucket is empty then).</summary>
        [Command]
        public void CmdPourBucket(int slot, Vector3Int block) {
            InventoryItem? held = inventory.ServerGetSlot(slot);
            if (health.isDead || WorldBehaviour.Instance == null || !held.HasValue || !Server.items.TryGetValue(held.Value.itemId, out Item item)
                    || item.bucket == null || item.bucket.liquid == null || !Server.items.TryGetValue(item.bucket.liquid, out Item liquid)
                    || !Server.items.TryGetValue(item.bucket.empty, out Item empty)) {
                return;
            }
            BlockType there = WorldBehaviour.Instance.GetBlockType(block);
            // where it can go: air, or what it covers (flowing water too: it's a source there now)
            if ((there != BlockType.Air && !BlockDatabase.Get(there).isReplaceable) || Water.IsSourceOf(there, liquid.blockType.Value) || !isInReach(blockCenter(block))) {
                return;
            }
            // nothing else in it: bricks, players
            BrickPlacement placement = new BrickPlacement(liquid.brickModel, BrickGrid.BlockToCell(block), 0);
            if (!BrickGrid.IsFree(placement) || BrickPlacer.overlapsPlayer(placement)) {
                return;
            }
            ConnectedPlayer player = connectionToClient.authenticationData as ConnectedPlayer;
            if (WorldNetwork.ServerSetBlock(block, liquid.blockType.Value, Placer.Now(player != null ? player.record.Id : 0))) {
                swapBucket(slot, empty);
            }
        }

        // one bucket of the slot becomes the other (in the slot if it was the last one); dropped if it doesn't fit
        [Server]
        private void swapBucket(int slot, Item into) {
            InventoryItem? held = inventory.ServerGetSlot(slot);
            bool last = held.HasValue && held.Value.quantity == 1;
            inventory.ServerRemoveFromSlot(slot, 1);
            if (!inventory.ServerAdd(into.id, into.color, 1, 0, last ? slot : 0)) {
                Vector3 forward = transform.forward;
                DroppedItems.ServerDrop(into.id, into.color, 1, 0, transform.position + forward * 0.6f + Vector3.up * 0.5f, forward * 2);
            }
        }

        private static Vector3 blockCenter(Vector3Int block) {
            return BrickGrid.CellToWorld(BrickGrid.BlockToCell(block), new Vector3(1, 1.5f, 1));
        }

        // what's in hand dug something: a tool that digs (a pickaxe) wears
        [Server]
        private void wearDigTool() {
            InventoryItem? held = inventory.ServerGetSlot(selectedSlot);
            if (held.HasValue && Server.items.TryGetValue(held.Value.itemId, out Item tool) && tool.Digs) {
                inventory.ServerWear(selectedSlot);
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
