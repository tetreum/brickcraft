using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// The colour of each part (submesh) of a model whose parts aren't all the brick's colour, like a
    /// transparent window in a coloured frame (see BrickModels.ModelInfo.parts). BrickColor.None is a part
    /// in the brick's colour, other values a palette colour it always has.
    /// </summary>
    public class BrickModelParts : MonoBehaviour
    {
        public int[] colors;
    }
}
