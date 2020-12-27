using System.Collections.Generic;
using UnityEngine;

public class Server : MonoBehaviour
{
    public static Server Instance;
    public static Dictionary<string, Brick> bricks = new Dictionary<string, Brick>();
    public static Dictionary<int, BrickModel> brickModels = new Dictionary<int, BrickModel>();
    public static Dictionary<string, GameObject> brickPrefabs = new Dictionary<string, GameObject>();
    public static Dictionary<int, Item> items = new Dictionary<int, Item>() {
        {3003, new Item(){
            id = 3003,
            type = Item.Type.Brick,
            name = "A brick"
        } },
        {3022, new Item(){
            id = 3022,
            type = Item.Type.Brick,
            name = "A brick"
        } },
        {3024, new Item(){
            id = 3024,
            type = Item.Type.Brick,
            name = "A brick"
        } },
        {22885, new Item(){
            id = 22885,
            type = Item.Type.Brick,
            name = "A brick"
        } },
    };

    public const float studSize = 0.398f;
    public const float plateHeight = (0.478f / 3);

    public GameObject[] prefabs;

    void Awake() {
        Instance = this;
        setupBrickModels();

        setupTest();
        processPrefabs();
    }

    void processPrefabs() {
        foreach (var prefab in prefabs) {
            brickPrefabs.Add(prefab.name, prefab);
        }
    }

    void setupTest () {
        Brick brick = new Brick();
        brick.id = "3003";
        brick.type = 3003;
        brick.gameObject = GameObject.Find(brick.id);

        bricks.Add(brick.id, brick);

        brick = new Brick();
        brick.id = "3022";
        brick.type = 3022;
        brick.gameObject = GameObject.Find(brick.id);

        bricks.Add(brick.id, brick);

        brick = new Brick();
        brick.id = "3024";
        brick.type = 3024;
        brick.gameObject = GameObject.Find(brick.id);

        bricks.Add(brick.id, brick);

        brick = new Brick();
        brick.id = "22885";
        brick.type = 22885;
        brick.gameObject = GameObject.Find(brick.id);

        bricks.Add(brick.id, brick);
    }

    public void spawnBrick (int model, Vector3 position, Quaternion rotation) {
        GameObject brickObj = Instantiate(brickPrefabs[model.ToString()], position, rotation);

        Brick brick = new Brick();
        brick.id = System.Guid.NewGuid().ToString();
        brick.type = model;
        brick.gameObject = brickObj;

        bricks.Add(brick.id, brick);

        brickObj.name = brick.id;
        SoundManager.Instance.play(SoundManager.EFFECT_TAPPING);
    }

    public void removeBrick(Brick brick) {
        bricks.Remove(brick.id);
        Destroy(brick.gameObject);
    }

    private void setupBrickModels() {
        BrickModel brick;


        brick = new BrickModel();
        brick.type = 3003;
        brick.heightInPlates = 3;
        brick.category = BrickModel.Category.Brick;
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
        brick.type = 22885;
        brick.heightInPlates = 6;
        brick.category = BrickModel.Category.Brick;
        brick.studs = new Dictionary<int, Dictionary<int, int[]>>() {
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
