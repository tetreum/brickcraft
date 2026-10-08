using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Brickcraft.UI;
using Brickcraft.Bricks;
using Brickcraft.World;

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
        }

        public bool isFrozen {
            get {
                return (freezeReason != null);
            }
        }
        public FreezeReason? freezeReason;

        [HideInInspector]
        public int inventorySlots = 36;

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
        private List<UserItem> inventory = new List<UserItem>();

        // what the player is looking at: either a placed brick or a world block
        private Brick lookedBrick;
        private Vector3Int? lookedBlock;

        // what the player is digging
        private bool isDigging;
        private Brick diggedBrick;
        private Vector3Int? diggedBlock;
        private float digTime;
        private float digHardness;

        private float _duration = 0.5f;
        private float _timer = 0f;

        private void Awake() {
            Instance = this;
            firstPersonController = GetComponent<FirstPersonController>();
            triggerDetector = GetComponentInChildren<TriggerDetector>();
            brickPlacer = gameObject.AddComponent<BrickPlacer>();
        }

        private void Start() {
            PlayerPanel.Instance.reload();

            // temporal for testing
            addItem(new UserItem() {
                id = 1,
                quantity = 100,
            });
        }

        private void Update() {
            frontRaycast();

            if (isFrozen) {
                brickPlacer.hide();
                stopDigging();
            } else {
                brickPlacer.tick(hasHit, latestHit, PlayerPanel.Instance.selectedItem);

                if (Input.GetKey(KeyCode.Mouse0) && (lookedBrick != null || lookedBlock.HasValue)) {
                    dig();
                } else {
                    stopDigging();
                }
            }
            if (Input.GetKeyDown(KeyCode.I)) {
                Menu.Instance.togglePanel("InventoryPanel");
            }
            if (Input.GetKeyDown(KeyCode.Tab)) {
                PlayerPanel.Instance.switchSelectedItem();
            }
            if (Input.GetKeyDown(KeyCode.Escape)) {
                Menu.Instance.togglePanel("ESCPanel");
            }
            if (Input.GetKeyDown(KeyCode.Alpha1)) {
                PlayerPanel.Instance.selectFastSlot(0);
            } else if (Input.GetKeyDown(KeyCode.Alpha2)) {
                PlayerPanel.Instance.selectFastSlot(1);
            } else if (Input.GetKeyDown(KeyCode.Alpha3)) {
                PlayerPanel.Instance.selectFastSlot(2);
            } else if (Input.GetKeyDown(KeyCode.Alpha4)) {
                PlayerPanel.Instance.selectFastSlot(3);
            } else if (Input.GetKeyDown(KeyCode.Alpha5)) {
                PlayerPanel.Instance.selectFastSlot(4);
            } else if (Input.GetKeyDown(KeyCode.Alpha6)) {
                PlayerPanel.Instance.selectFastSlot(5);
            } else if (Input.GetKeyDown(KeyCode.Alpha7)) {
                PlayerPanel.Instance.selectFastSlot(6);
            } else if (Input.GetKeyDown(KeyCode.Alpha8)) {
                PlayerPanel.Instance.selectFastSlot(7);
            } else if (Input.GetKeyDown(KeyCode.Alpha9)) {
                PlayerPanel.Instance.selectFastSlot(8);
            }
        }

        public void freeze(FreezeReason reason) {
            if (isFrozen) {
                return;
            }
            freezeReason = reason;
            firstPersonController.enabled = false;
        }

        public void unFreeze(FreezeReason reason) {
            unFreeze();
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

            Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));
            hasHit = Physics.Raycast(ray, out latestHit, rayLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            if (!hasHit) {
                return;
            }

            lookedBrick = Server.findBrick(latestHit.collider);

            if (lookedBrick == null && WorldBehaviour.Instance != null && latestHit.collider.name.StartsWith("ChunkSlice")) {
                // step slightly inside the face we hit to find which block it belongs to
                Vector3Int cell = BrickGrid.WorldToCell(latestHit.point - latestHit.normal * 0.01f);
                lookedBlock = BrickGrid.CellToBlock(cell);
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
                SoundManager.Instance.play(SoundManager.EFFECT_DIG);
            }
        }

        void startDigging () {
            stopDigging();

            if (lookedBrick != null) {
                digHardness = lookedBrick.model.hardness;
                diggedBrick = lookedBrick;
            } else {
                BlockType blockType = WorldBehaviour.Instance.GetBlockType(lookedBlock.Value);

                if (!Blocks.IsBreakable(blockType)) {
                    return;
                }
                digHardness = Blocks.GetHardness(blockType);
                diggedBlock = lookedBlock;
            }

            isDigging = true;
            digTime = 0f;
            _timer = 0f;
            Game.breakAnimation.showAt(latestHit.point, Quaternion.FromToRotation(Vector3.back, latestHit.normal));
        }

        void finishDigging () {
            if (diggedBrick != null) {
                addItem(new UserItem() {
                    id = diggedBrick.itemId,
                    quantity = 1
                });
                Server.Instance.removeBrick(diggedBrick);
            } else {
                BlockType blockType = WorldBehaviour.Instance.GetBlockType(diggedBlock.Value);

                if (WorldBehaviour.Instance.SetBlockType(diggedBlock.Value, BlockType.Air)) {
                    Item item = Server.getItemForBlock(blockType);

                    if (item != null) {
                        addItem(new UserItem() {
                            id = item.id,
                            quantity = 1
                        });
                    }
                }
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

        public void addItem (UserItem userItem) {
            List<int> takenSlots = new List<int>();

            foreach (UserItem item in inventory) {
                takenSlots.Add(item.slot);
                if (item.id == userItem.id && item.health == userItem.health) {
                    item.quantity += userItem.quantity;
                    PlayerPanel.Instance.reload();
                    return;
                }
            }
            if (inventory.Count >= inventorySlots) {
                return;
            }
            if (userItem.slot == 0) { // not set
                int[] allSlots = Enumerable.Range(1, inventorySlots).ToArray();
                int[] availableSlots = allSlots.Except(takenSlots).ToArray();
                bool insertedInFastInventory = false;

                // whenever its possible insert new items in fast inventory
                foreach (var slot in availableSlots) {
                    if (slot > 27) {
                        userItem.slot = slot;
                        insertedInFastInventory = true;
                        break;
                    }
                }
                if (!insertedInFastInventory) {
                    userItem.slot = availableSlots[0];
                }
            }
        
            inventory.Add(userItem);
            InventoryPanel.Instance.reload();

            if (userItem.slot > 27) {
                PlayerPanel.Instance.reload();
            }
        }
        public void removeItem (UserItem userItem) {
            foreach (UserItem item in inventory) {
                if (item.id == userItem.id && item.health == userItem.health) {
                    item.quantity -= userItem.quantity;

                    if (item.quantity < 1) {
                        inventory.Remove(item);
                    }

                    InventoryPanel.Instance.reload();
                    if (item.slot > 27) {
                        PlayerPanel.Instance.reload();
                    }
                    return;
                }
            }
        }
        public void switchInventorySlots (int slot1, int slot2) {
            UserItem item1 = null;
            UserItem item2 = null;

            foreach (UserItem item in inventory) {
                if (item.slot == slot1) {
                    item1 = item;
                } else if (item.slot == slot2) {
                    item2 = item;
                }
            }

            if (item1 != null) {
                item1.slot = slot2;
            }
            if (item2 != null) {
                item2.slot = slot1;
            }

            InventoryPanel.Instance.reload();
            PlayerPanel.Instance.reload();
        }

        public Dictionary<int, UserItem> getInventoryBySlot () {
            Dictionary<int, UserItem> items = new Dictionary<int, UserItem>();

            foreach (var item in inventory) {
                items.Add(item.slot, item);
            }
            return items;
        }

        public bool isInventorySlotAvailable (int slotId) {
            Dictionary<int, UserItem> items = getInventoryBySlot();

            return !items.ContainsKey(slotId);
        }

        public List<UserItem> getInventory () {
            return inventory;
        }
    }
}
