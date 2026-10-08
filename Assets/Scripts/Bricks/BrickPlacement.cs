using System.Collections.Generic;
using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// Where a brick sits on the <see cref="BrickGrid"/>: the cell of its minimum corner plus
    /// a rotation in quarter turns around the Y axis.
    /// </summary>
    public struct BrickPlacement
    {
        public BrickModel model;
        public Vector3Int origin;
        public int rotation;

        public BrickPlacement(BrickModel model, Vector3Int origin, int rotation = 0) {
            this.model = model;
            this.origin = origin;
            this.rotation = BrickGrid.Mod(rotation, 4);
        }

        /// <summary>Size in cells once rotated.</summary>
        public Vector3Int Size {
            get {
                return SizeFor(model, rotation);
            }
        }

        public static Vector3Int SizeFor(BrickModel model, int rotation) {
            return rotation % 2 == 0
                ? new Vector3Int(model.width, model.heightInPlates, model.depth)
                : new Vector3Int(model.depth, model.heightInPlates, model.width);
        }

        public IEnumerable<Vector3Int> Cells {
            get {
                Vector3Int size = Size;
                for (int x = 0; x < size.x; x++) {
                    for (int y = 0; y < size.y; y++) {
                        for (int z = 0; z < size.z; z++) {
                            yield return origin + new Vector3Int(x, y, z);
                        }
                    }
                }
            }
        }

        public Quaternion Rotation {
            get {
                return Quaternion.Euler(0, 90 * rotation, 0);
            }
        }

        /// <summary>World position of the brick pivot (center of its footprint, at its bottom).</summary>
        public Vector3 Position {
            get {
                Vector3Int size = Size;
                return BrickGrid.CellToWorld(origin, new Vector3(size.x / 2f, 0, size.z / 2f));
            }
        }

        public Bounds WorldBounds {
            get {
                Vector3 min = BrickGrid.CellToWorld(origin);
                Vector3 max = BrickGrid.CellToWorld(origin + Size);
                Bounds bounds = new Bounds();
                bounds.SetMinMax(min, max);
                return bounds;
            }
        }

        /// <summary>True when the brick fills exactly one world block.</summary>
        public bool MatchesWorldBlock {
            get {
                return Size == new Vector3Int(BrickGrid.StudsPerBlock, BrickGrid.PlatesPerBlock, BrickGrid.StudsPerBlock)
                    && BrickGrid.IsBlockAligned(origin);
            }
        }
    }
}
