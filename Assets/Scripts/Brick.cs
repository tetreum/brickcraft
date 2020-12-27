using UnityEngine;

namespace Brickcraft
{
    public class Brick {
        public string id;
        public int itemId;
        public GameObject gameObject;

        public Item item {
            get {
                return Server.items[itemId];
            }
        }

        public BrickModel model {
            get {
                return Server.brickModels[item.brickModelId];
            }
        }
    }
}
