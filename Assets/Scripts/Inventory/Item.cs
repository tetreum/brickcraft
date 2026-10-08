using UnityEngine;
using Brickcraft.World;

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
        public int layer;
        public Type type;
        public string name;

        // World block this item turns into when placed exactly over a world block (2x2 bricks only)
        public BlockType? blockType;

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