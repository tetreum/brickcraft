using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Brickcraft.UI;

namespace Brickcraft
{
    public class Player : MonoBehaviour
    {
        public static Player Instance;

        [HideInInspector]
        public int inventorySlots = 36;

        [HideInInspector]
        RaycastHit latestHit;

        private float rayLength = 5f;
        private GameObject lookedBrick;
        private Brick diggedBrick;
        private DateTime? activityStartTime;
        private List<UserItem> inventory = new List<UserItem>();

        private float _duration = 0.5f;
        private float _timer = 0f;

        private void Awake() {
            Instance = this;
        }

        private void Start() {
            PlayerPanel.Instance.reload();
        }

        private void Update() {
            frontRaycast();
        
            if (Input.GetKey(KeyCode.Mouse0) && lookedBrick != null) {
                dig();
            } else if (Input.GetKeyUp(KeyCode.Mouse0) && diggedBrick != null) {
                stopDigging();
            }
            if (Input.GetKeyDown(KeyCode.I)) {
                Menu.Instance.togglePanel("InventoryPanel");
            }
            if (Input.GetKeyDown(KeyCode.Tab)) {
                PlayerPanel.Instance.switchSelectedItem();
            }
        }

        void frontRaycast () {
            lookedBrick = null;
            Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));

            // debug Ray
            //Debug.DrawRay(ray.origin, ray.direction * rayLength, Color.red);

            if (Physics.Raycast(ray, out latestHit, rayLength)) {
                if (latestHit.collider.name.StartsWith("GridStud")) {
                    if (BrickCollisionDetector.Instance != null) {
                        BrickCollisionDetector.Instance.lookingAtStud(latestHit);
                    }
                    lookedBrick = latestHit.transform.parent.gameObject;
                } else if (latestHit.transform.tag == "Block") {
                    lookedBrick = latestHit.transform.gameObject;
                }
            }
        }

        void dig () {
            if (activityStartTime == null) {
                diggedBrick = Server.bricks[lookedBrick.name];
                activityStartTime = DateTime.Now;

                // fix z-fighting
                Vector3 hitPoint = latestHit.point;
                hitPoint.z += lookedBrick.transform.position.z > 0 ? 0.001f : -0.001f;
                hitPoint.y += lookedBrick.transform.position.y > 0 ? -0.001f : 0.001f;
                hitPoint.x += lookedBrick.transform.position.x > 0 ? -0.001f : 0.001f;
                Game.breakAnimation.showAt(hitPoint, Quaternion.FromToRotation(Vector3.back, latestHit.normal));
            }
            DateTime currentTime = DateTime.Now;
            TimeSpan span = currentTime.Subtract((DateTime)activityStartTime);

            // has finished digging
            if (span.Seconds >= diggedBrick.model.hardness) {
                addItem(new UserItem() {
                    id = diggedBrick.itemId,
                    quantity = 1
                });
                Server.Instance.removeBrick(diggedBrick);
                SoundManager.Instance.play(SoundManager.EFFECT_REMOVE_BLOCK);
                stopDigging();
            } else {
                _timer += Time.deltaTime;
                if (_timer >= _duration) {
                    _timer = 0f;
                    SoundManager.Instance.play(SoundManager.EFFECT_DIG);
                    Game.breakAnimation.advance();
                }
            }
        }

        void stopDigging () {
            diggedBrick = null;
            activityStartTime = null;
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
            PlayerPanel.Instance.reload();
        }
        public void removeItem (UserItem userItem) {
            foreach (UserItem item in inventory) {
                if (item.id == userItem.id && item.health == userItem.health) {
                    item.quantity -= userItem.quantity;

                    if (item.quantity < 1) {
                        inventory.Remove(item);
                    }
                    PlayerPanel.Instance.reload();
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
    }
}
