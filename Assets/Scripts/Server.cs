using System.Collections.Generic;
using UnityEngine;

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
        };

        public const float studSize = 0.398f;
        public const float plateHeight = (0.478f / 3);

        public GameObject[] prefabs;

        void Awake() {
            Instance = this;

            setupBrickModels();
            processPrefabs();
        }

        private void Start() {
            setupTest();
        }

        void processPrefabs() {
            foreach (var prefab in prefabs) {
                brickPrefabs.Add(prefab.name, prefab);
            }
        }

        void setupTest() {
            spawnBrick(Server.items[1], new Vector3(3.327f, 0, -4.196f), Quaternion.identity);
            spawnBrick(Server.items[1], new Vector3(1.468601f, 0, -4.383173f), Quaternion.identity);
            spawnBrick(Server.items[2], new Vector3(2.374763f, 0.372f, -3.981043f), Quaternion.identity);
            spawnBrick(Server.items[3], new Vector3(0.1108012f, 0.355f, -4.368471f), Quaternion.identity);
            spawnBrick(Server.items[4], new Vector3(-1.03f, 0.15f, -4.299f), Quaternion.identity);

            float brickWidth = Server.studSize * 2;
            int rectangleSize = 10;
            for (int i = 0; i < rectangleSize; i++) {
                for (int y = 0; y < rectangleSize; y++) {
                    spawnBrick(Server.items[1], new Vector3(5.615f + (i * brickWidth), 0f, -3.732f + (y * brickWidth)), Quaternion.identity);
                }
            }

            spawnUnlimitedBlocks();
        }
        // spawn a brick that gives user 100 bricks of that type.
        // For testing.
        void spawnUnlimitedBlocks () {
            Vector3 colliderSize = new Vector3(1.5f, 1.5f, 1.5f);
            Vector3 pos = new Vector3(4.574519f, 0.5f, 5.509473f);
            Brick brick;
            BoxCollider boxCollider;
            BlockAdderTest blockAdder;

            foreach (Item item in items.Values) {
                if (item.type != Item.Type.Brick) {
                    continue;
                }
                pos.x -= 2;
                brick = spawnBrick(item, pos, Quaternion.identity);
                boxCollider = brick.gameObject.GetComponent<BoxCollider>();
                boxCollider.isTrigger = true;
                boxCollider.size = colliderSize;
                blockAdder = brick.gameObject.AddComponent<BlockAdderTest>();
                blockAdder.item = item.id;
            }
        }

        public Brick spawnBrick(Item item, Vector3 position, Quaternion rotation) {
            GameObject brickObj = Instantiate(brickPrefabs[item.brickModelId.ToString()], position, rotation);
            Material brickMaterial = item.material;

            if (brickMaterial != null) {
                brickObj.GetComponent<MeshRenderer>().material = brickMaterial;
            }

            Brick brick = new Brick();
            brick.id = System.Guid.NewGuid().ToString();
            brick.itemId = item.id;
            brick.gameObject = brickObj;

            bricks.Add(brick.id, brick);

            brickObj.name = brick.id;
            SoundManager.Instance.play(SoundManager.EFFECT_TAPPING);

            return brick;
        }

        public void removeBrick(Brick brick) {
            bricks.Remove(brick.id);
            Destroy(brick.gameObject);
        }

        private void setupBrickModels() {
            BrickModel brick;

            brick = new BrickModel() {
                type = 3003,
                heightInPlates = 3,
                category = BrickModel.Category.Brick,
                studs = new Dictionary<int, Dictionary<int, int[]>>() {
                    {0, new Dictionary<int, int[]>() {
                            {0, new int[]{1, 2} },
                            {1, new int[]{0, 3} },
                            {2, new int[]{0, 3} },
                            {3, new int[]{1, 2} },
                        }
                    }
                }
            };
            
            brickModels.Add(brick.type, brick);

            brick = new BrickModel() {
                type = 22885,
                heightInPlates = 6,
                category = BrickModel.Category.Brick,
                studs = new Dictionary<int, Dictionary<int, int[]>>() {
                    {0, new Dictionary<int, int[]>() {
                            {0, new int[]{1, 2} },
                            {1, new int[]{0, 3} },
                            {2, new int[]{0, 3} },
                            {3, new int[]{1, 2} },
                        }
                    },
                    {1, new Dictionary<int, int[]>() {
                            {0, new int[]{1} },
                            {1, new int[]{0} },
                        }
                    },
                }
            };
            brickModels.Add(brick.type, brick);

            brick = new BrickModel();
            brick.type = 3022;
            brick.heightInPlates = 1;
            brick.category = BrickModel.Category.Plate;
            brick.studs = new Dictionary<int, Dictionary<int, int[]>>() {
                {0, new Dictionary<int, int[]>() {
                    {0, new int[]{1, 2} },
                    {1, new int[]{0, 3} },
                    {2, new int[]{0, 3} },
                    {3, new int[]{1, 2} },
                }
             }
            };
            brickModels.Add(brick.type, brick);

            brick = new BrickModel();
            brick.type = 3024;
            brick.heightInPlates = 1;
            brick.category = BrickModel.Category.Plate;
            brick.studs = new Dictionary<int, Dictionary<int, int[]>>() {
                {0, new Dictionary<int, int[]>() {
                        {0, new int[]{ } },
                    }
                }
            };
            brickModels.Add(brick.type, brick);
        }
    }
}