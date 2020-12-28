using System.Collections.Generic;
using UnityEngine;

namespace Brickcraft
{
    public class BrickModel
    {
        public enum Category {
            Brick = 1,
            Plate = 2
        };
        public int type;
        public int heightInPlates;
        public Category category;
        public Vector3 pivot = Vector3.zero; // the point from where we will rotate/place it
        public float hardness = 4; //seconds with bare hands
        public Dictionary<int, Dictionary<int, int[]>> studs = new Dictionary<int, Dictionary<int, int[]>>();
    }
}