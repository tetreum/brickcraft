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

        // icon of items defined in a block folder, loaded from iconFile (see BlockDatabase)
        public string iconFile;
        public Texture2D iconTexture;

        public Texture2D icon {
            get {
                if (iconFile != null) {
                    return iconTexture;
                }
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