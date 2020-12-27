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
        public Type type;
        public string name;

        public Texture2D icon {
            get {
                return Resources.Load<Texture2D>("Textures/Bricks/" + id);
            }
        }
    }
}