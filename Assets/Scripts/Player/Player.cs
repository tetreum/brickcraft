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
            /// <summary>Looking at its creation with the parts lister (see PartsListerView).</summary>
            PartsLister = 12,
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

        /// <summary>In water to the waist or more: it swims (see FirstPersonController).</summary>
        public bool isOnWater {
            get { return firstPersonController != null && firstPersonController.IsSwimming; }
        }

        [HideInInspector]
        public FirstPersonController firstPersonController;

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

        // when it started drawing its ranged weapon (holding the dig button), -1 if it isn't
        private float drawStart = -1;

        /// <summary>How drawn its ranged weapon is, 0 (not drawing) to 1 (fully), see Item.drawTime.</summary>
        public float DrawPower {
            get {
                Item weapon = SelectedItem != null ? SelectedItem.item : null;
                return drawStart < 0 || weapon == null || !weapon.IsRanged ? 0 : Mathf.Clamp01((Time.time - drawStart) / weapon.drawTime);
            }
        }

        /// <summary>It holds its shield up: it can't attack meanwhile.</summary>
        public bool IsBlocking {
            get { return isBlocking; }
        }
        private Health health;

        // right click with nothing in hand zooms in a little (see updateZoom)
        private readonly CameraZoom zoom = new CameraZoom();
        // the camera's view without the zoom, and what the zoom made it: other code (sprinting's kick) sets it too
        private float unzoomedFov = -1;
        private float zoomedFov = -1;

        // what the player is digging
        private bool isDigging;
        private Brick diggedBrick;
        private Vector3Int? diggedBlock;
        private float digTime;
        private float digHardness;
        // what digging it sounds like (see BlockDefinition.digSound)
        private string digSound = SoundManager.EFFECT_DIG;

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
            network = GetComponent<PlayerNetwork>();
            inventory = GetComponent<PlayerInventory>();
            health = GetComponent<Health>();
            partsLister = GetComponent<PartsLister>();
        }

        private PartsLister partsLister;

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
            // before what the place button does this frame (drinking the last potion empties the hand at once alone)
            couldZoom = canZoom();

            if (isFrozen) {
                brickPlacer.hide();
                stopDigging();
                clickedDoor = null;
                wasDigHeld = false;
                cancelDraw();
            } else {
                // with a ranged weapon (and its ammo) the dig button draws it instead; with a color picker it paints
                bool isDrawing = updateDrawing();
                bool isPainting = SelectedItem != null && SelectedItem.item.IsColorPicker;
                bool isDigHeld = !isDrawing && !isPainting && GameInput.GetButton(GameInput.Dig);
                bool isClickingDoor = clickDoors(isDigHeld);

                // the server checks it can be hit (PvP for players...)
                if (!isDrawing && !isPainting && GameInput.GetButtonDown(GameInput.Dig) && Time.time >= nextAttack) {
                    attack();
                }

                // the brick looked at takes the color picker's colour (the server checks it can)
                if (isPainting && !GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Dig) && lookedBrick != null) {
                    network.CmdPaintBrick(SelectedSlot, lookedBrick.id);
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

                // a parts lister is used on a brick (or block) of the player's: the server checks it is
                if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Place) && SelectedItem != null && SelectedItem.item.IsPartsLister) {
                    if (lookedBrick != null) {
                        partsLister.CmdStartOnBrick(SelectedSlot, lookedBrick.id);
                    } else if (lookedBlock.HasValue) {
                        partsLister.CmdStartOnBlock(SelectedSlot, lookedBlock.Value);
                    }
                }

                // a model importer opens its window, unless its ghost is out (it places it, see ModelImporterView)
                if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Place) && SelectedItem != null && SelectedItem.item.IsModelImporter
                        && !ModelImporterView.IsOpen && ModelImporterView.ClosedFrame != Time.frameCount) {
                    ModelImporterView.RequestWindow(SelectedSlot);
                }

                // a color picker opens its window, to pick its colour
                if (isPainting && !GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Place)) {
                    ColorPickerTool.RequestWindow(SelectedSlot);
                }

                // food is used up, if the player is hurt (the server checks)
                if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Place) && SelectedItem != null && SelectedItem.item.Heals
                        && health.health < health.maxHealth) {
                    network.CmdConsume(SelectedSlot);
                }

                // buckets: an empty one picks up the liquid looked at, a full one pours it against what's looked at
                if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Place) && SelectedItem != null && SelectedItem.item.bucket != null) {
                    useBucket(SelectedItem.item.bucket);
                }

                // throwing what's in hand: one, or the whole stack with Ctrl
                if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Drop) && SelectedItem != null) {
                    bool whole = GameInput.CtrlHeld;
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
                clickedDoor = lookedBrick != null && isUsable(lookedBrick) ? lookedBrick : null;
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

        // a click uses it: a door, a bed, a chest
        private static bool isUsable(Brick brick) {
            return brick.gameObject != null && (brick.gameObject.GetComponentInChildren<BrickDoor>() != null || isBed(brick) || BrickStorage.StorageOf(brick) != null);
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

        // a ranged weapon (a bow) with its ammo in hand: holding the dig button draws it, letting go shoots where
        // it looks, as strong as it was drawn (see DrawPower). False (and no drawing) for anything else.
        bool updateDrawing () {
            Item weapon = SelectedItem != null ? SelectedItem.item : null;
            if (weapon == null || !weapon.IsRanged || isBlocking
                    || Brickcraft.Inventory.Count(inventory.items, weapon.ammo, Brickcraft.Inventory.AnyColor, 0) == 0) {
                cancelDraw();
                return false;
            }
            if (drawStart < 0) {
                if (GameInput.GetButtonDown(GameInput.Dig) && Time.time >= nextAttack) {
                    drawStart = Time.time;
                    network.CmdDraw();
                    SoundManager.Instance.startHeld(SoundManager.EFFECT_STRING_BOW);
                }
            } else if (!GameInput.GetButton(GameInput.Dig)) {
                float power = DrawPower;
                cancelDraw();
                SoundManager.Instance.playOnce(SoundManager.EFFECT_SHOOT_BOW);
                nextAttack = Time.time + 1 / weapon.attackSpeed;
                if (network.heldItems != null) {
                    network.heldItems.Swing();
                }
                network.CmdShoot(SelectedSlot, Camera.main.transform.forward, power);
            }
            return true;
        }

        // no longer drawing (shot, or stopped): the string stops creaking
        void cancelDraw () {
            if (drawStart >= 0) {
                drawStart = -1;
                SoundManager.Instance.stopHeld();
            }
        }

        // a hit with what's in hand (see Item's attack stats) at what's looked at. Weapons swing even at nothing.
        void attack () {
            if (isBlocking) {
                return;
            }
            Item weapon = SelectedItem != null ? SelectedItem.item : null;
            Health target = lookedHealth;
            if (target == null && (weapon == null || weapon.type != Item.Type.Weapon)) {
                return;
            }
            nextAttack = Time.time + 1 / (weapon != null ? weapon.attackSpeed : Item.HandAttackSpeed);
            if (network.heldItems != null && weapon != null && weapon.type == Item.Type.Weapon) {
                network.heldItems.Swing();
            }
            // a weapon swishes at anything, a hand only punches something
            if (weapon != null && weapon.type == Item.Type.Weapon || target != null) {
                SoundManager.Instance.playOnce(SoundManager.HitSound(weapon));
            }
            if (target != null) {
                network.CmdAttack(target.netIdentity, SelectedSlot);
            }
        }

        private void LateUpdate() {
            if (Instance == this) {
                updateZoom();
            }
        }

        // the server checks the block, that it's in reach, and swaps the bucket (see PlayerNetwork.CmdFillBucket)
        void useBucket (BucketInfo bucket) {
            if (bucket.filled != null) {
                Vector3Int? liquid = liquidLookedAt(bucket);
                if (liquid.HasValue) {
                    network.CmdFillBucket(SelectedSlot, liquid.Value);
                }
            } else if (bucket.liquid != null && hasHit) {
                // the block in front of the face looked at
                Vector3Int cell = BrickGrid.WorldToCell(latestHit.point + latestHit.normal * 0.01f);
                network.CmdPourBucket(SelectedSlot, BrickGrid.CellToBlock(cell));
            }
        }

        // liquid blocks have no collider: the first one along the crosshair, before what it hits
        Vector3Int? liquidLookedAt (BucketInfo bucket) {
            if (WorldBehaviour.Instance == null || !Server.items.TryGetValue(bucket.filled, out Item full)
                    || !Server.items.TryGetValue(full.bucket.liquid ?? "", out Item liquid) || !liquid.blockType.HasValue) {
                return null;
            }
            Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));
            float distance = hasHit ? latestHit.distance : rayLength;
            for (float along = 0; along <= distance; along += 0.05f) {
                Vector3Int block = BrickGrid.CellToBlock(BrickGrid.WorldToCell(ray.GetPoint(along)));
                if (Water.IsSourceOf(WorldBehaviour.Instance.GetBlockType(block), liquid.blockType.Value)) {
                    return block;
                }
            }
            return null;
        }

        // the place button held with nothing in hand (nor a shield to raise, nor anything to use looked at) zooms in,
        // after the camera's view is set; decided when it goes down (see CameraZoom)
        private void updateZoom() {
            Camera eye = Camera.main;
            if (eye == null) {
                return;
            }
            if (unzoomedFov < 0 || eye.fieldOfView != zoomedFov) {
                unzoomedFov = eye.fieldOfView;
            }
            // a press zooms if it could when it went down, before the button did anything
            bool pressed = GameInput.GetButtonDown(GameInput.Place);
            zoomedFov = unzoomedFov * zoom.Update(pressed, GameInput.GetButton(GameInput.Place), pressed ? couldZoom : canZoom(), Time.deltaTime);
            eye.fieldOfView = zoomedFov;
        }

        // whether the place button would zoom now: nothing in hand, nothing to use looked at, playing
        private bool couldZoom;

        private bool canZoom() {
            return !isFrozen && !GameInput.IsTyping && !isBlocking && SelectedItem == null && (lookedBrick == null || !isUsable(lookedBrick));
        }

        // the place button held with a shield worn raises it, unless what's in hand is a brick (the button places it)
        void updateBlocking () {
            bool block = false;
            if (!isFrozen && !GameInput.IsTyping && GameInput.GetButton(GameInput.Place)) {
                UserItem held = SelectedItem;
                getInventoryBySlot().TryGetValue(Brickcraft.Inventory.EquipmentSlot(Item.ArmorSlot.Offhand), out UserItem offhand);
                block = offhand != null && offhand.item.IsShield && (held == null || (held.item.type != Item.Type.Brick && held.item.type != Item.Type.Tool));
            }
            if (block != isBlocking) {
                isBlocking = block;
                network.CmdBlock(block);
            }
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
                SoundManager.Instance.play(digSound);
            }
        }

        void startDigging () {
            stopDigging();

            if (lookedBrick != null) {
                digHardness = lookedBrick.model.hardness;
                diggedBrick = lookedBrick;
                // bricks that are also a block (2x2s) sound like it
                Item item = lookedBrick.item;
                digSound = item != null && item.blockType.HasValue ? BlockDatabase.Get(item.blockType.Value).digSound : SoundManager.EFFECT_DIG;
            } else {
                BlockDefinition block = BlockDatabase.Get(WorldBehaviour.Instance.GetBlockType(lookedBlock.Value));

                if (!block.isBreakable) {
                    return;
                }
                digHardness = block.hardness;
                diggedBlock = lookedBlock;
                digSound = block.digSound;
            }
            // a tool that digs (a pickaxe) is faster than a hand; the server wears it (see PlayerNetwork.CmdDigBlock)
            UserItem tool = SelectedItem;
            if (tool != null && tool.item.Digs) {
                digHardness /= tool.item.digSpeed;
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
                return diggedBrick.WorldBounds;
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
