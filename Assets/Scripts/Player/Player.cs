using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Brickcraft.Bricks;
using Brickcraft.World;
using Brickcraft.Network;
using Brickcraft.Events;
using Brickcraft.Combat;

namespace Brickcraft
{
    public class Player : MonoBehaviour
    {
        public static Player Instance;

        public enum FreezeReason
        {
            Driving = 1,
            Dialog = 2,
            ESCMenu = 3,
            ViewingInventory = 4,
            Looting = 5,
            LoadingWorld = 6,
            Chatting = 7,
            Settings = 8,
            PlayerList = 9,
            Dead = 10,
            Sleeping = 11,
        }

        /// <summary>It's sleeping in a bed (see Network.PlayerSleep): it can't move nor open its inventory, but it can chat.</summary>
        public bool IsSleeping {
            get { return network != null && !string.IsNullOrEmpty(network.sleepingIn); }
        }

        /// <summary>It can open the chat: when nothing froze it, or it's only sleeping.</summary>
        public bool CanChat {
            get { return !isFrozen || freezeReason == FreezeReason.Sleeping; }
        }

        public bool isFrozen {
            get {
                return (freezeReason != null);
            }
        }
        public FreezeReason? freezeReason;

        [HideInInspector]
        public int inventorySlots = Brickcraft.Inventory.SlotCount;

        [HideInInspector]
        RaycastHit latestHit;

        [HideInInspector]
        public bool isOnWater = false;

        [HideInInspector]
        public FirstPersonController firstPersonController;
        [HideInInspector]
        public TriggerDetector triggerDetector;

        private float rayLength = 5f;
        private bool hasHit;
        private BrickPlacer brickPlacer;
        private PlayerInventory inventory;

        // what the player is looking at: either a placed brick or a world block, or something with health (another player...)
        private Brick lookedBrick;
        private Vector3Int? lookedBlock;
        private Health lookedHealth;

        // clicking something with health hits it, as often as what's in hand allows (the server checks it too)
        private float nextAttack;

        // the shield is raised (the place button held, see updateBlocking), and what the server was told
        private bool isBlocking;
        // the selected slot the server was told about (what the others see in its hand)
        private int sentSlot = int.MinValue;

        /// <summary>It holds its shield up: it can't attack meanwhile.</summary>
        public bool IsBlocking {
            get { return isBlocking; }
        }
        private Health health;

        // what the player is digging
        private bool isDigging;
        private Brick diggedBrick;
        private Vector3Int? diggedBlock;
        private float digTime;
        private float digHardness;

        private float _duration = 0.5f;
        private float _timer = 0f;

        // a door is opened or closed with a click (pressed for less than this), a bed slept in; holding it breaks them
        private const float ClickTime = 0.25f;
        private Brick clickedDoor;
        private float clickStart;
        private bool wasDigHeld;

        [HideInInspector]
        public PlayerNetwork network;

        /// <summary>The first inventory slot of the fast inventory (the hotbar), slots 1 to 9 select from there.</summary>
        public const int FirstFastSlot = 28;
        public const int FastSlotCount = 9;

        /// <summary>The inventory slot whose item is in hand, -1 for none. Changes raise EventManager.SelectedSlotChanged.</summary>
        public int SelectedSlot { get; private set; } = FirstFastSlot;

        /// <summary>The item in hand, null for none.</summary>
        public UserItem SelectedItem {
            get {
                getInventoryBySlot().TryGetValue(SelectedSlot, out UserItem item);
                return item;
            }
        }

        private void Awake() {
            firstPersonController = GetComponent<FirstPersonController>();
            triggerDetector = GetComponentInChildren<TriggerDetector>();
            network = GetComponent<PlayerNetwork>();
            inventory = GetComponent<PlayerInventory>();
            health = GetComponent<Health>();
        }

        // every player in the game has this component, but only the local one is controlled from here
        public void onStartLocalPlayer() {
            Instance = this;
            brickPlacer = gameObject.AddComponent<BrickPlacer>();
            health.DeadChanged += onDeadChanged;
            EventManager.LocalPlayerStarted.Raise(new LocalPlayerStartedEvent());
        }

        private void OnDestroy() {
            if (Instance == this) {
                health.DeadChanged -= onDeadChanged;
                Instance = null;
            }
        }

        // the dead don't move or act until they respawn, whatever else froze them
        private void onDeadChanged(bool dead) {
            if (dead) {
                unFreeze();
                freeze(FreezeReason.Dead);
            } else {
                unFreeze(FreezeReason.Dead);
            }
        }

        private void Update() {
            // remote players are disabled once spawned, but on the host they can tick once before that
            if (Instance != this) {
                return;
            }
            waitForGround();
            frontRaycast();
            if (SelectedSlot != sentSlot) {
                sentSlot = SelectedSlot;
                network.CmdSelectSlot(SelectedSlot);
            }
            updateBlocking();

            if (isFrozen) {
                brickPlacer.hide();
                stopDigging();
                clickedDoor = null;
                wasDigHeld = false;
            } else {
                bool isDigHeld = GameInput.GetButton(GameInput.Dig);
                bool isClickingDoor = clickDoors(isDigHeld);

                // the server checks it can be hit (PvP for players...)
                if (GameInput.GetButtonDown(GameInput.Dig) && Time.time >= nextAttack) {
                    attack();
                }

                // the placing preview would hide what's being dug
                if (isDigHeld) {
                    brickPlacer.hide();
                } else {
                    brickPlacer.tick(hasHit, latestHit, SelectedItem);
                }

                if (isDigHeld && !isClickingDoor && (lookedBrick != null || lookedBlock.HasValue)) {
                    dig();
                } else {
                    stopDigging();
                }

                // throwing what's in hand: one, or the whole stack with Ctrl
                if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Drop) && SelectedItem != null) {
                    bool whole = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    inventory.CmdDrop(SelectedSlot, whole ? SelectedItem.quantity : 1);
                }

                // what happens is up to the brick's mod, see ModScripts; NPCs are tamed or given orders
                if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Interact)) {
                    if (lookedHealth != null && lookedHealth.GetComponent<Npcs.Npc>() != null) {
                        network.CmdInteractNpc(lookedHealth.netIdentity, SelectedSlot);
                    } else if (lookedBrick != null) {
                        network.CmdInteractBrick(lookedBrick.id);
                    } else if (lookedBlock.HasValue) {
                        network.CmdInteractBlock(lookedBlock.Value);
                    }
                }
            }

            // keys typed as text aren't shortcuts; the UI handles its own (inventory, menu...)
            if (GameInput.IsTyping) {
                return;
            }
            int slot = GameInput.GetSlotDown();
            if (slot != -1) {
                selectFastSlot(slot);
            }
        }

        /// <summary>Selects the given slot of the fast inventory (0 to 8), or none if it's already selected.</summary>
        public void selectFastSlot(int index) {
            int slot = FirstFastSlot + index;
            SelectedSlot = slot == SelectedSlot ? -1 : slot;
            EventManager.SelectedSlotChanged.Raise(new SelectedSlotChangedEvent() { slot = SelectedSlot });
        }

        /// <summary>
        /// Selects the next (step 1) or previous (step -1) slot of the fast inventory, wrapping around.
        /// With none selected, it starts from the first or last one.
        /// </summary>
        public void scrollFastSlot(int step) {
            int index = SelectedSlot - FirstFastSlot;

            if (index < 0 || index >= FastSlotCount) {
                index = step > 0 ? 0 : FastSlotCount - 1;
            } else {
                index = ((index + step) % FastSlotCount + FastSlotCount) % FastSlotCount;
            }
            SelectedSlot = FirstFastSlot + index;
            EventManager.SelectedSlotChanged.Raise(new SelectedSlotChangedEvent() { slot = SelectedSlot });
        }

        // a click on a door opens or closes it, on a bed sleeps in it, on a chest looks inside; true while it may still be one (digging waits for it)
        private bool clickDoors(bool isDigHeld) {
            bool isPressed = isDigHeld && !wasDigHeld;
            bool isReleased = !isDigHeld && wasDigHeld;
            wasDigHeld = isDigHeld;

            if (isPressed) {
                clickedDoor = lookedBrick != null && (lookedBrick.gameObject.GetComponentInChildren<BrickDoor>() != null || isBed(lookedBrick)
                    || BrickStorage.StorageOf(lookedBrick) != null) ? lookedBrick : null;
                clickStart = Time.time;
            }
            if (clickedDoor == null) {
                return false;
            }
            bool isQuick = Time.time - clickStart < ClickTime;
            if (isReleased) {
                if (isQuick && lookedBrick == clickedDoor) {
                    if (isBed(clickedDoor)) {
                        network.CmdSleep(clickedDoor.id);
                    } else if (BrickStorage.StorageOf(clickedDoor) != null) {
                        network.CmdOpenStorage(clickedDoor.id);
                    } else {
                        network.CmdToggleDoor(clickedDoor.id);
                    }
                }
                clickedDoor = null;
                return false;
            }
            return isQuick;
        }

        private static bool isBed(Brick brick) {
            return Server.items.TryGetValue(brick.itemId, out Item item) && item.tags.Contains(Item.BedTag);
        }

        public void freeze(FreezeReason reason) {
            if (isFrozen) {
                return;
            }
            freezeReason = reason;
            firstPersonController.enabled = false;
        }

        public void unFreeze(FreezeReason reason) {
            if (freezeReason == reason) {
                unFreeze();
            }
        }

        // the world streams in around the player; if it walks faster than that, keep it from falling through
        private void waitForGround() {
            if (WorldBehaviour.Instance == null) {
                return;
            }
            bool isGroundLoaded = WorldBehaviour.Instance.IsWalkable(WorldBehaviour.ChunkAt(transform.position));

            if (!isGroundLoaded && !isFrozen) {
                freeze(FreezeReason.LoadingWorld);
            } else if (isGroundLoaded && freezeReason == FreezeReason.LoadingWorld) {
                unFreeze(FreezeReason.LoadingWorld);
            }
        }

        public void unFreeze() {
            freezeReason = null;
            try {
                firstPersonController.enabled = true;
            } catch { }
        }

        void frontRaycast () {
            lookedBrick = null;
            lookedBlock = null;
            lookedHealth = null;

            Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));
            hasHit = Physics.Raycast(ray, out latestHit, rayLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            if (!hasHit) {
                return;
            }

            lookedBrick = Server.findBrick(latestHit.collider);

            Health hit = latestHit.collider.GetComponentInParent<Health>();
            if (hit != null && hit != health && !hit.isDead) {
                lookedHealth = hit;
            }

            if (lookedBrick == null && WorldBehaviour.Instance != null && latestHit.collider.name.StartsWith("ChunkSlice")) {
                // step slightly inside the face we hit to find which block it belongs to
                Vector3Int cell = BrickGrid.WorldToCell(latestHit.point - latestHit.normal * 0.01f);
                lookedBlock = BrickGrid.CellToBlock(cell);
            }
        }

        // a hit with what's in hand (see Item's attack stats): what's looked at, or with a ranged weapon (a bow,
        // with its ammo) whatever with health is in its sight. Weapons swing even at nothing.
        void attack () {
            if (isBlocking) {
                return;
            }
            Item weapon = SelectedItem != null ? SelectedItem.item : null;
            Health target = lookedHealth;
            if (weapon != null && weapon.IsRanged) {
                if (Brickcraft.Inventory.Count(inventory.items, weapon.ammo, Brickcraft.Inventory.AnyColor, 0) == 0) {
                    return;
                }
                target = target ?? rangedTarget(weapon.reach);
            }
            if (target == null && (weapon == null || weapon.type != Item.Type.Weapon)) {
                return;
            }
            nextAttack = Time.time + 1 / (weapon != null ? weapon.attackSpeed : Item.HandAttackSpeed);
            if (network.heldItems != null && weapon != null && weapon.type == Item.Type.Weapon) {
                network.heldItems.Swing();
            }
            if (target != null) {
                network.CmdAttack(target.netIdentity, SelectedSlot);
            }
        }

        // the place button held with a shield worn raises it, unless what's in hand is a brick (the button places it)
        void updateBlocking () {
            bool block = false;
            if (!isFrozen && !GameInput.IsTyping && GameInput.GetButton(GameInput.Place)) {
                UserItem held = SelectedItem;
                getInventoryBySlot().TryGetValue(Brickcraft.Inventory.EquipmentSlot(Item.ArmorSlot.Offhand), out UserItem offhand);
                block = offhand != null && offhand.item.IsShield && (held == null || held.item.type != Item.Type.Brick);
            }
            if (block != isBlocking) {
                isBlocking = block;
                network.CmdBlock(block);
            }
        }

        // what has health in the middle of the screen, up to that far (nothing if something else is in the way)
        Health rangedTarget (float range) {
            Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));
            if (!Physics.Raycast(ray, out RaycastHit hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) {
                return null;
            }
            Health found = hit.collider.GetComponentInParent<Health>();
            return found != null && found != health && !found.isDead ? found : null;
        }

        void dig () {
            bool isSameTarget = isDigging && (diggedBrick != null
                ? diggedBrick == lookedBrick
                : lookedBrick == null && diggedBlock == lookedBlock);

            if (!isSameTarget) {
                startDigging();

                if (!isDigging) {
                    return;
                }
            }

            digTime += Time.deltaTime;

            if (digTime >= digHardness) {
                finishDigging();
                return;
            }

            Game.breakAnimation.setProgress(digTime / digHardness);

            _timer += Time.deltaTime;
            if (_timer >= _duration) {
                _timer = 0f;
                SoundManager.Instance.play(SoundManager.EFFECT_DIG);
            }
        }

        void startDigging () {
            stopDigging();

            if (lookedBrick != null) {
                digHardness = lookedBrick.model.hardness;
                diggedBrick = lookedBrick;
            } else {
                BlockDefinition block = BlockDatabase.Get(WorldBehaviour.Instance.GetBlockType(lookedBlock.Value));

                if (!block.isBreakable) {
                    return;
                }
                digHardness = block.hardness;
                diggedBlock = lookedBlock;
            }

            isDigging = true;
            digTime = 0f;
            _timer = 0f;
            Game.breakAnimation.showAt(diggedBounds(), latestHit.normal);
        }

        // what the cracks cover: the whole brick or block, not just the face under the crosshair
        Bounds diggedBounds () {
            if (diggedBrick != null && diggedBrick.attachedTo != null) {
                Bounds attached = new Bounds(diggedBrick.gameObject.transform.position, Vector3.zero);
                foreach (Renderer part in diggedBrick.gameObject.GetComponentsInChildren<Renderer>()) {
                    attached.Encapsulate(part.bounds);
                }
                return attached;
            }
            if (diggedBrick != null) {
                return diggedBrick.placement.WorldBounds;
            }
            Vector3Int cell = BrickGrid.BlockToCell(diggedBlock.Value);
            Bounds bounds = new Bounds();
            bounds.SetMinMax(
                BrickGrid.CellToWorld(cell),
                BrickGrid.CellToWorld(cell + new Vector3Int(BrickGrid.StudsPerBlock, BrickGrid.PlatesPerBlock, BrickGrid.StudsPerBlock)));
            return bounds;
        }

        // the server removes it and gives us the item
        void finishDigging () {
            if (diggedBrick != null) {
                network.CmdRemoveBrick(diggedBrick.id);
            } else {
                network.CmdDigBlock(diggedBlock.Value);
            }
            SoundManager.Instance.play(SoundManager.EFFECT_REMOVE_BLOCK);
            stopDigging();
        }

        void stopDigging () {
            if (!isDigging) {
                return;
            }
            isDigging = false;
            diggedBrick = null;
            diggedBlock = null;
            Game.breakAnimation.hide();
        }

        // the server owns the inventory (see Network.PlayerInventory), this just asks it to move a stack
        /// <summary>Moves the stack in a slot to another: merged if it's the same item (as much as fits), swapped otherwise.</summary>
        public void moveInventoryStack (int fromSlot, int toSlot) {
            inventory.CmdMoveStack(fromSlot, toSlot);
        }

        /// <summary>Asks the server to move one unit of the stack in a slot to another.</summary>
        public void moveOneInventoryItem (int fromSlot, int toSlot) {
            inventory.CmdMoveOne(fromSlot, toSlot);
        }

        public Dictionary<int, UserItem> getInventoryBySlot () {
            Dictionary<int, UserItem> items = new Dictionary<int, UserItem>();

            // not Add: a slot never holds two stacks, but the list can be caught in the middle of a swap
            foreach (var item in getInventory()) {
                items[item.slot] = item;
            }
            return items;
        }

        public bool isInventorySlotAvailable (int slotId) {
            Dictionary<int, UserItem> items = getInventoryBySlot();

            return !items.ContainsKey(slotId);
        }

        /// <summary>The items the server synced to us.</summary>
        public List<UserItem> getInventory () {
            List<UserItem> items = new List<UserItem>(inventory.items.Count);

            foreach (InventoryItem item in inventory.items) {
                items.Add(new UserItem() {
                    id = item.itemId,
                    color = item.color,
                    quantity = item.quantity,
                    health = item.health,
                    slot = item.slot,
                });
            }
            return items;
        }
    }
}
