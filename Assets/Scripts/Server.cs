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
        public static Dictionary<int, BrickModel> brickModels = new Dictionary<int, BrickModel>();
        public static Dictionary<string, GameObject> brickPrefabs = new Dictionary<string, GameObject>();
        // every item has its folder, see ItemDatabase. Ids are slugs, see Slugs
        public static Dictionary<string, Item> items = new Dictionary<string, Item>();

        public const float studSize = 0.398f;
        public const float plateHeight = (0.478f / 3);
        public const float brickHeight = plateHeight * 3;
        public const float brickWidth = studSize * 2; // 2x2, the size of a world block

        public GameObject[] prefabs;

        void Awake() {
            Instance = this;

            // static state survives scene changes, start every game clean
            bricks.Clear();
            BrickGrid.Clear();
            brickModels.Clear();
            brickPrefabs.Clear();

            setupBrickModels();
            processPrefabs();
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

        void processPrefabs() {
            foreach (var prefab in prefabs) {
                brickPrefabs.Add(prefab.name, prefab);
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
                if (item.type != Item.Type.Brick || item.brickModel.width > 4) {
                    continue;
                }
                cell.x -= 5;
                brick = spawnBrick(item, item.color, new BrickPlacement(item.brickModel, cell));
                brick.gameObject.layer = (int)Game.Layers.Default;
                boxCollider = brick.gameObject.GetComponent<BoxCollider>();
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
            GameObject prefab = brickPrefabs[item.brickModelId.ToString()];
            GameObject brickObj = Instantiate(prefab, position, rotation * prefab.transform.rotation);
            Material brickMaterial = item.MaterialFor(color);

            // legacy stud colliders, placement is computed from the brick grid now
            foreach (Transform child in brickObj.transform) {
                if (child.name.StartsWith("GridStud")) {
                    Destroy(child.gameObject);
                }
            }

            MeshRenderer meshRenderer = brickObj.GetComponent<MeshRenderer>();

            if (brickMaterial != null) {
                meshRenderer.material = brickMaterial;
            }
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
        public Brick spawnBrick(Item item, int color, BrickPlacement placement, string id = null) {
            color = item.ValidColor(color);
            GameObject brickObj = createBrickObject(item, color, placement.Position, placement.Rotation);

            Brick brick = new Brick();
            brick.id = id ?? System.Guid.NewGuid().ToString();
            brick.itemId = item.id;
            brick.color = color;
            brick.gameObject = brickObj;
            brick.placement = placement;

            bricks.Add(brick.id, brick);
            BrickGrid.Register(brick);

            brickObj.name = brick.id;

            return brick;
        }

        public void removeBrick(Brick brick) {
            bricks.Remove(brick.id);
            BrickGrid.Unregister(brick);
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

        private void setupBrickModels() {
            addBrickModel(3003, BrickModel.Category.Brick, 2, 2, 3);
            addBrickModel(22885, BrickModel.Category.Brick, 2, 1, 5);
            addBrickModel(3022, BrickModel.Category.Plate, 2, 2, 1);
            addBrickModel(3024, BrickModel.Category.Plate, 1, 1, 1);
            addBrickModel(3001, BrickModel.Category.Brick, 4, 2, 3);
            addBrickModel(4186, BrickModel.Category.Plate, 48, 48, 1);
        }

        private void addBrickModel(int type, BrickModel.Category category, int width, int depth, int heightInPlates) {
            brickModels.Add(type, new BrickModel() {
                type = type,
                category = category,
                width = width,
                depth = depth,
                heightInPlates = heightInPlates,
            });
        }
    }
}
