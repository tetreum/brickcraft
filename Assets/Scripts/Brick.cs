using UnityEngine;
using Brickcraft.Bricks;

namespace Brickcraft
{
    public class Brick {
        public string id;
        public string itemId;
        public GameObject gameObject;
        public BrickPlacement placement;

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
