using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
                name = "Sand 2x2"
            } },
            {9, new Item(){
                id = 9,
                type = Item.Type.Brick,
                brickModelId = 4186,
                materialName = "MediumNougat",
                name = "Dirt 48x48"
            } },
        };

        public const float studSize = 0.398f;
        public const float plateHeight = (0.478f / 3);
        public const float brickHeight = 0.478f;
        public const float brickWidth = 0.796f; // 2x2

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
            spawnBrick(Server.items[1], new Vector3(3.327f, 3, -4.196f), Quaternion.identity, true);
            spawnBrick(Server.items[1], new Vector3(1.468601f, 0, -4.383173f), Quaternion.identity, true);
            spawnBrick(Server.items[2], new Vector3(2.374763f, 0.372f, -3.981043f), Quaternion.identity, true);
            spawnBrick(Server.items[3], new Vector3(0.1108012f, 0.355f, -4.368471f), Quaternion.identity, true);
            spawnBrick(Server.items[4], new Vector3(-1.03f, 0.15f, -4.299f), Quaternion.identity, true);
            spawnBrick(Server.items[6], new Vector3(-2.72f, 0.15f, -4.15f), Quaternion.identity, true);

            generateCube(Server.items[9], new Vector3(3, 1, 5), new Vector3(-41f, 0f, -24f));

            // water
            generateCube(Server.items[7], new Vector3(10, 10, 10), new Vector3(16.411f, -4.824f, -3.732f));

            // sand
            generateCube(Server.items[8], new Vector3(2, 10, 10), new Vector3(14.819f, -4.824f, -3.732f));
            generateCube(Server.items[8], new Vector3(1, 11, 10), new Vector3(14.023f, -4.824f, -3.732f));
            generateCube(Server.items[8], new Vector3(13, 10, 2), new Vector3(14.023f, -4.824f, -5.323999f));
            generateCube(Server.items[8], new Vector3(2, 10, 12), new Vector3(24.371f, -4.824f, -5.323999f));
            generateCube(Server.items[8], new Vector3(15, 10, 2), new Vector3(14.023f, -4.824f, 4.228f));
            generateCube(Server.items[8], new Vector3(10, 1, 10), new Vector3(16.411f, -4.824f, -3.732f));

            // dirt
            //generateCube(Server.items[1], new Vector3(100, 11, 100), new Vector3(14.023f, -4.824f, 5.82f));

            spawnUnlimitedBlocks();
        }

        private void generateCube (Item item, Vector3 cubeDimensions, Vector3 startingPos) {
            float brickWidth = Server.studSize * (item.id == 9 ? 48 : 2);
            float brickHeight = Server.plateHeight * item.brickModel.heightInPlates;

            for (int x = 0; x < cubeDimensions.x; x++) {
                for (int y = 0; y < cubeDimensions.y; y++) {
                    for (int z = 0; z < cubeDimensions.z; z++) {
                        spawnBrick(item, new Vector3(startingPos.x + (x * brickWidth), startingPos.y + (y * brickHeight), startingPos.z + (z * brickWidth)), Quaternion.identity, true);
                    }
                }
            }
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
                brick = spawnBrick(item, pos, Quaternion.identity, true);
                brick.gameObject.layer = (int)Game.Layers.Default;
                boxCollider = brick.gameObject.GetComponent<BoxCollider>();
                boxCollider.isTrigger = true;
                boxCollider.size = colliderSize;
                blockAdder = brick.gameObject.AddComponent<BlockAdderTest>();
                blockAdder.item = item.id;
            }
        }

        public Brick spawnBrick(Item item, Vector3 position, Quaternion rotation, bool fromServer = false) {
            GameObject brickObj = Instantiate(brickPrefabs[item.brickModelId.ToString()], position, rotation);
            Material brickMaterial = item.material;

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

            Brick brick = new Brick();
            brick.id = System.Guid.NewGuid().ToString();
            brick.itemId = item.id;
            brick.gameObject = brickObj;

            bricks.Add(brick.id, brick);

            brickObj.name = brick.id;

            if (!fromServer) {
                SoundManager.Instance.play(SoundManager.EFFECT_TAPPING);
            }

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
                pivot = new Vector3(-(Server.studSize / 2), 0, -(Server.studSize / 2)),
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
                pivot = new Vector3(-(Server.studSize / 2), 0, 0),
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
            brick.pivot = new Vector3(-(Server.studSize / 2), 0, -(Server.studSize / 2));
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
            brick.pivot = new Vector3(0, 0, 0);
            brick.studs = new Dictionary<int, Dictionary<int, int[]>>() {
                {0, new Dictionary<int, int[]>() {
                        {0, new int[]{ } },
                    }
                }
            };
            brickModels.Add(brick.type, brick);

            brick = new BrickModel();
            brick.type = 3001;
            brick.heightInPlates = 3;
            brick.category = BrickModel.Category.Brick;
            brick.pivot = new Vector3(-(Server.studSize / 2) * 3, 0, -(Server.studSize / 2));
            brick.studs = new Dictionary<int, Dictionary<int, int[]>>() {
                {0, new Dictionary<int, int[]>() {
                        {0, new int[]{ } },
                    }
                }
            };
            brickModels.Add(brick.type, brick);

            brick = new BrickModel();
            brick.type = 4186;
            brick.heightInPlates = 1;
            brick.category = BrickModel.Category.Plate;
            brick.pivot = new Vector3(-(Server.studSize / 2) * 3, 0, -(Server.studSize / 2));
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