using UnityEngine;

namespace Brickcraft
{
    public class StudInfo
    {
        public Vector3 center;
        public Vector2Int gridPosition = new Vector2Int();
        public Vector2Int gridDimensions = new Vector2Int();

        public override string ToString() {
            return string.Format("C: {0} | P: {1} | G: {2}", center, gridPosition, gridDimensions);
        }
    }
}
