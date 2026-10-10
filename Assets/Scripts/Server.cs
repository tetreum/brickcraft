using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Brickcraft.Bricks;
using Brickcraft.World;
using Brickcraft.Network;
using Mirror;

namespace Brickcraft
{
    public class Server : MonoBehaviour
    {
        public static Server Instance;
        public static Dictionary<string, Brick> bricks = new Dictionary<string, Brick>();
        // every item has its folder, see ItemDatabase. Ids are slugs, see Slugs
        public static Dictionary<string, Item> items = new Dictionary<string, Item>();

        public const float studSize = 0.398f;
        public const float plateHeight = (0.478f / 3);
        public const float brickHeight = plateHeight * 3;
        public const float brickWidth = studSize * 2; // 2x2, the size of a world block


        void Awake() {
            Instance = this;

            // static state survives scene changes, start every game clean
            bricks.Clear();
            BrickGrid.Clear();

            checkBrickModels();
        }

        private void Start() {
            // game scene played straight from the editor: make it a singleplayer game
            if (!NetworkServer.active && !NetworkClient.active) {
                BrickcraftNetworkManager.GetOrCreate().StartSingleplayer(SceneManager.GetActiveScene().name);
            }

            if (NetworkServer.active && SceneManager.GetActiveScene().name == BrickcraftNetworkManager.TestScene) {
                setupTest();
            }
        }

        void setupTest() {
            // a few loose bricks
            spawnBrick(items["dirt"], items["dirt"].color, new BrickPlacement(items["dirt"].brickModel, new Vector3Int(8, 18, -12)));
            spawnBrick(items["dirt"], items["dirt"].color, new BrickPlacement(items["dirt"].brickModel, new Vector3Int(4, 0, -12)));
            spawnBrick(items["plate_2x2_yellow"], items["plate_2x2_yellow"].color, new BrickPlacement(items["plate_2x2_yellow"].brickModel, new Vector3Int(6, 0, -10)));
            spawnBrick(items["plate_1x1_green"], items["plate_1x1_green"].color, new BrickPlacement(items["plate_1x1_green"].brickModel, new Vector3Int(0, 0, -11)));
            spawnBrick(items["brick_1x2_side_studs_green"], items["brick_1x2_side_studs_green"].color, new BrickPlacement(items["brick_1x2_side_studs_green"].brickModel, new Vector3Int(-3, 0, -11)));
            spawnBrick(items["dirt_2x4"], items["dirt_2x4"].color, new BrickPlacement(items["dirt_2x4"].brickModel, new Vector3Int(-8, 0, -11)));

            fillWithBricks(items["dirt_48x48"], new Vector3Int(-150, 0, -100), new Vector3Int(3, 1, 5));

            // a pool of water surrounded by sand
            fillWithBricks(items["sand"], new Vector3Int(40, -30, -12), new Vector3Int(12, 1, 12));
            fillWithBricks(items["water_2x2"], new Vector3Int(42, -27, -10), new Vector3Int(10, 10, 10));
            fillWithBricks(items["sand"], new Vector3Int(40, -27, -12), new Vector3Int(1, 10, 12));
            fillWithBricks(items["sand"], new Vector3Int(62, -27, -12), new Vector3Int(1, 10, 12));
            fillWithBricks(items["sand"], new Vector3Int(42, -27, -12), new Vector3Int(10, 10, 1));
            fillWithBricks(items["sand"], new Vector3Int(42, -27, 10), new Vector3Int(10, 10, 1));

            spawnUnlimitedBlocks();
        }

        // fills a box with copies of the item's brick, starting at the given cell
        private void fillWithBricks (Item item, Vector3Int originCell, Vector3Int brickCount) {
            Vector3Int size = BrickPlacement.SizeFor(item.brickModel, 0);

            for (int x = 0; x < brickCount.x; x++) {
                for (int y = 0; y < brickCount.y; y++) {
                    for (int z = 0; z < brickCount.z; z++) {
                        Vector3Int cell = originCell + Vector3Int.Scale(new Vector3Int(x, y, z), size);
                        spawnBrick(item, item.color, new BrickPlacement(item.brickModel, cell));
                    }
                }
            }
        }

        // spawn a brick that gives user 100 bricks of that type.
        // For testing.
        void spawnUnlimitedBlocks () {
            Vector3 colliderSize = new Vector3(1.5f, 1.5f, 1.5f);
            Vector3Int cell = new Vector3Int(12, 3, 14);
            Brick brick;
            BoxCollider boxCollider;
            BlockAdderTest blockAdder;

            foreach (Item item in items.Values) {
                if (item.type != Item.Type.Brick || item.brickModel.width > 4 || item.brickModel.IsAttachment) {
                    continue;
                }
                cell.x -= 5;
                brick = spawnBrick(item, item.color, new BrickPlacement(item.brickModel, cell));
                brick.gameObject.layer = (int)Game.Layers.Default;
                boxCollider = brick.gameObject.GetComponent<BoxCollider>();
                if (boxCollider == null) {
                    continue; // models with a mesh collider
                }
                boxCollider.isTrigger = true;
                boxCollider.size = colliderSize;
                blockAdder = brick.gameObject.AddComponent<BlockAdderTest>();
                blockAdder.item = item.id;
            }
        }

        /// <summary>
        /// Creates the GameObject of an item's brick without registering it in the world.
        /// </summary>
        public GameObject createBrickObject(Item item, int color, Vector3 position, Quaternion rotation) {
            GameObject prefab = item.brickModel.prefab;
            GameObject brickObj = Instantiate(prefab, position, rotation * prefab.transform.rotation);
            Material brickMaterial = item.MaterialFor(color);

            // legacy stud colliders, placement is computed from the brick grid now
            foreach (Transform child in brickObj.transform) {
                if (child.name.StartsWith("GridStud")) {
                    Destroy(child.gameObject);
                }
            }

            MeshRenderer meshRenderer = brickObj.GetComponent<MeshRenderer>();
            BrickModels.ApplyColor(brickObj, brickMaterial);
            if (item.layer > 0) {
                brickObj.layer = item.layer;
                foreach (Transform tr in brickObj.transform) {
                    tr.gameObject.layer = item.layer;
                }
            }
            if ((Game.Layers)item.layer == Game.Layers.Water) {
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return brickObj;
        }

        /// <summary>
        /// Adds a brick to this game instance. In a networked game use Network.WorldNetwork instead,
        /// which calls this on the server and every client.
        /// </summary>
        public Brick spawnBrick(Item item, int color, BrickPlacement placement, string id = null, int state = 0, string attachedTo = null) {
            color = item.ValidColor(color);
            GameObject brickObj;
            if (attachedTo != null) {
                // in its brick's slot, moving with it
                if (!bricks.TryGetValue(attachedTo, out Brick holder) || slotOf(holder) == null) {
                    Debug.LogError("The brick " + attachedTo + " that " + item.id + " is attached to isn't there, or has no slot");
                    return null;
                }
                Transform point = slotOf(holder).point;
                brickObj = createBrickObject(item, color, point.position, point.rotation);
                brickObj.transform.SetParent(holder.gameObject.transform, true);
                placement = holder.placement;
            } else {
                brickObj = createBrickObject(item, color, placement.Position, placement.Rotation);
            }

            Brick brick = new Brick();
            brick.id = id ?? System.Guid.NewGuid().ToString();
            brick.itemId = item.id;
            brick.color = color;
            brick.gameObject = brickObj;
            brick.placement = placement;
            brick.state = state;
            brick.attachedTo = attachedTo;

            bricks.Add(brick.id, brick);
            if (attachedTo == null) {
                BrickGrid.Register(brick);
            }

            brickObj.name = brick.id;
            if (state != 0) {
                showState(brick, false);
            }

            return brick;
        }

        /// <summary>Moves a brick of this game instance, false (and it stays) if the cells there aren't free.</summary>
        public bool moveBrick(Brick brick, BrickPlacement to) {
            BrickGrid.Unregister(brick);
            if (!BrickGrid.IsFree(to)) {
                BrickGrid.Register(brick);
                return false;
            }
            // the prefab's own rotation stays under the placement's
            Quaternion prefabRotation = Quaternion.Inverse(brick.placement.Rotation) * brick.gameObject.transform.rotation;
            brick.placement = to;
            brick.gameObject.transform.SetPositionAndRotation(to.Position, to.Rotation * prefabRotation);
            BrickGrid.Register(brick);
            return true;
        }

        /// <summary>Changes a brick's state in this game instance, its model shows it (see IBrickState).</summary>
        public void setBrickState(Brick brick, int state) {
            brick.state = state;
            showState(brick, true);
        }

        private static void showState(Brick brick, bool animate) {
            foreach (IBrickState shown in brick.gameObject.GetComponentsInChildren<IBrickState>()) {
                shown.ShowState(brick.state, animate);
            }
        }

        /// <summary>The slot of a brick (see BrickSlot), null if its model has none.</summary>
        public static BrickSlot slotOf(Brick brick) {
            return brick.gameObject != null ? brick.gameObject.GetComponent<BrickSlot>() : null;
        }

        /// <summary>What's attached to a brick (in its slot, see BrickSlot), null if nothing is.</summary>
        public static Brick attachmentOf(Brick brick) {
            foreach (Brick other in bricks.Values) {
                if (other.attachedTo == brick.id) {
                    return other;
                }
            }
            return null;
        }

        /// <summary>Removes a brick from this game instance, and what's attached to it.</summary>
        public void removeBrick(Brick brick) {
            Brick attached = attachmentOf(brick);
            if (attached != null) {
                removeBrick(attached);
            }
            if (!bricks.Remove(brick.id)) {
                return;
            }
            if (brick.attachedTo == null) {
                BrickGrid.Unregister(brick);
            }
            Destroy(brick.gameObject);
        }

        public static Brick findBrick(Collider collider) {
            for (Transform tr = collider.transform; tr != null; tr = tr.parent) {
                if (bricks.TryGetValue(tr.name, out Brick brick)) {
                    return brick;
                }
            }
            return null;
        }

        // the item a player gets after digging a world block, if any
        public static Item getItemForBlock(BlockType blockType) {
            string itemId = BlockDatabase.Get(blockType).dropItemId;

            return itemId != null && items.TryGetValue(itemId, out Item item) ? item : null;
        }

        // every brick item needs its model's prefab
        private void checkBrickModels() {
            foreach (Item item in items.Values) {
                if (item.type == Item.Type.Brick && !BrickModels.Exists(item.brickModelId)) {
                    Debug.LogError("The item " + item.id + " is made of the brick model " + item.brickModelId
                        + ", which doesn't exist (see BrickModels)");
                }
            }
        }
    }
}
