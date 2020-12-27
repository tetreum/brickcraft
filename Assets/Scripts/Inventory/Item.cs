using UnityEngine;

namespace Brickcraft
{
    public class Item
    {
        public enum Type
        {
            Brick = 1
        }
        public int id;
        public int brickModelId;
        public string materialName;
        public Type type;
        public string name;

        public Texture2D icon {
            get {
                return Resources.Load<Texture2D>("Textures/Bricks/" + id);
            }
        }
        public BrickModel brickModel {
            get {
                return Server.brickModels[brickModelId];
            }
        }
        public Material material {
            get {
                return Game.Instance.getBrickMaterial(materialName);
            }
        }
    }
}