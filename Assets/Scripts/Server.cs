using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Brickcraft.Bricks;
using Brickcraft.World;

namespace Brickcraft
{
    public class Server : MonoBehaviour
    {
        public static Server Instance;
        public static Dictionary<string, Brick> bricks = new Dictionary<string, Brick>();
        public static Dictionary<int, BrickModel> brickModels = new Dictionary<int, BrickModel>();
        public static Dictionary<string, GameObject> brickPrefabs = new Dictionary<string, GameObject>();
        public static Dictionary<int, Item> items = new Dictionary<int, Item>() {
            {1, new Item(){
                id = 1,
                type = Item.Type.Brick,
                brickModelId = 3003,
                materialName = "MediumNougat",
                blockType = BlockType.Dirt,
                name = "Dirt 2x2"
            } },
            {2, new Item(){
                id = 2,
                type = Item.Type.Brick,
                brickModelId = 3022,
                materialName = "BrightYellow",
                name = "A brick"
            } },
            {3, new Item(){
                id = 3,
                type = Item.Type.Brick,
                brickModelId = 3024,
                materialName = "BrightGreen",
                name = "A brick"
            } },
            {4, new Item(){
                id = 4,
                type = Item.Type.Brick,
                brickModelId = 22885,
                materialName = "BrightGreen",
                name = "A brick"
            } },
            {5, new Item(){
                id = 5,
                type = Item.Type.Brick,
                brickModelId = 3003,
                materialName = "TransparentBlue",
                name = "Glass 2x2"
            } },
            {6, new Item(){
                id = 6,
                type = Item.Type.Brick,
                brickModelId = 3001,
                materialName = "MediumNougat",
                name = "Dirt 2x4"
            } },
            {7, new Item(){
                id = 7,
                type = Item.Type.Brick,
                brickModelId = 3003,
                materialName = "Water",
                layer = (int)Game.Layers.Water,
                name = "Water 2x2"
            } },
            {8, new Item(){
                id = 8,
                type = Item.Type.Brick,
                brickModelId = 3003,
                materialName = "BrickYellow",
                blockType = BlockType.Sand,
                name = "Sand 2x2"
            } },
            {9, new Item(){
                id = 9,
                type = Item.Type.Brick,
                brickModelId = 4186,
                materialName = "MediumNougat",
                name = "Dirt 48x48"
            } },
            {10, new Item(){
                id = 10,
                type = Item.Type.Brick,
                brickModelId = 3003,
                blockType = BlockType.Stone,
                name = "Stone 2x2"
            } },
            {11, new Item(){
                id = 11,
                type = Item.Type.Brick,
                brickModelId = 3003,
                materialName = "MediumNougat",
                blockType = BlockType.Wood,
                name = "Wood 2x2"
            } },
            {12, new Item(){
                id = 12,
                type = Item.Type.Brick,
                brickModelId = 3003,
                materialName = "BrightGreen",
                blockType = BlockType.Leaves,
                name = "Leaves 2x2"
            } },
        };

        public const float studSize = 0.398f;
        public const float plateHeight = (0.478f / 3);
        public const float brickHeight = plateHeight * 3;
        public const float brickWidth = studSize * 2; // 2x2, the size of a world block

        public GameObject[] prefabs;
        public GameObject playerPrefab;

        void Awake() {
            Instance = this;

            setupBrickModels();
            processPrefabs();
        }

        private void Start() {
            if (SceneManager.GetActiveScene().name == "Test") {
                setupTest();
            }
        }

        public void spawnPlayer (Vector3 pos, Quaternion rot) {
            Instantiate(playerPrefab, pos, rot);
        }

        void processPrefabs() {
            foreach (var prefab in prefabs) {
                brickPrefabs.Add(prefab.name, prefab);
            }
        }

        void setupTest() {
            // a few loose bricks
            spawnBrick(items[1], new BrickPlacement(items[1].brickModel, new Vector3Int(8, 18, -12)), true);
            spawnBrick(items[1], new BrickPlacement(items[1].brickModel, new Vector3Int(4, 0, -12)), true);
            spawnBrick(items[2], new BrickPlacement(items[2].brickModel, new Vector3Int(6, 0, -10)), true);
            spawnBrick(items[3], new BrickPlacement(items[3].brickModel, new Vector3Int(0, 0, -11)), true);
            spawnBrick(items[4], new BrickPlacement(items[4].brickModel, new Vector3Int(-3, 0, -11)), true);
            spawnBrick(items[6], new BrickPlacement(items[6].brickModel, new Vector3Int(-8, 0, -11)), true);

            fillWithBricks(items[9], new Vector3Int(-150, 0, -100), new Vector3Int(3, 1, 5));

            // a pool of water surrounded by sand
            fillWithBricks(items[8], new Vector3Int(40, -30, -12), new Vector3Int(12, 1, 12));
            fillWithBricks(items[7], new Vector3Int(42, -27, -10), new Vector3Int(10, 10, 10));
            fillWithBricks(items[8], new Vector3Int(40, -27, -12), new Vector3Int(1, 10, 12));
            fillWithBricks(items[8], new Vector3Int(62, -27, -12), new Vector3Int(1, 10, 12));
            fillWithBricks(items[8], new Vector3Int(42, -27, -12), new Vector3Int(10, 10, 1));
            fillWithBricks(items[8], new Vector3Int(42, -27, 10), new Vector3Int(10, 10, 1));

            spawnUnlimitedBlocks();
        }

        // fills a box with copies of the item's brick, starting at the given cell
        private void fillWithBricks (Item item, Vector3Int originCell, Vector3Int brickCount) {
            Vector3Int size = BrickPlacement.SizeFor(item.brickModel, 0);

            for (int x = 0; x < brickCount.x; x++) {
                for (int y = 0; y < brickCount.y; y++) {
                    for (int z = 0; z < brickCount.z; z++) {
                        Vector3Int cell = originCell + Vector3Int.Scale(new Vector3Int(x, y, z), size);
                        spawnBrick(item, new BrickPlacement(item.brickModel, cell), true);
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
                brick = spawnBrick(item, new BrickPlacement(item.brickModel, cell), true);
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
        public GameObject createBrickObject(Item item, Vector3 position, Quaternion rotation) {
            GameObject prefab = brickPrefabs[item.brickModelId.ToString()];
            GameObject brickObj = Instantiate(prefab, position, rotation * prefab.transform.rotation);
            Material brickMaterial = item.material;

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

        public Brick spawnBrick(Item item, BrickPlacement placement, bool fromServer = false) {
            GameObject brickObj = createBrickObject(item, placement.Position, placement.Rotation);

            Brick brick = new Brick();
            brick.id = System.Guid.NewGuid().ToString();
            brick.itemId = item.id;
            brick.gameObject = brickObj;
            brick.placement = placement;

            bricks.Add(brick.id, brick);
            BrickGrid.Register(brick);

            brickObj.name = brick.id;

            if (!fromServer) {
                SoundManager.Instance.play(SoundManager.EFFECT_TAPPING);
            }

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
            switch (blockType) {
                case BlockType.Grass:
                case BlockType.Dirt:
                    return items[1];
                case BlockType.Sand:
                    return items[8];
                case BlockType.Wood:
                case BlockType.Wood_Planks:
                    return items[11];
                case BlockType.Leaves:
                    return items[12];
                case BlockType.Stone:
                case BlockType.Cobblestone:
                case BlockType.Gravel:
                case BlockType.Coal_Ore:
                case BlockType.Iron_Ore:
                case BlockType.Gold_Ore:
                case BlockType.Diamond_Ore:
                case BlockType.Lapis_Lazuli_Ore:
                case BlockType.Redstone_Ore:
                case BlockType.Redstone_Ore_Glowing:
                    return items[10];
                default:
                    return null;
            }
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
