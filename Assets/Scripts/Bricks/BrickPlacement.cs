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

        /// <summary>The cells it fills: its box's, but the columns its model leaves free (see BrickModel.footprint).</summary>
        public IEnumerable<Vector3Int> Cells {
            get {
                Vector3Int size = Size;
                for (int x = 0; x < size.x; x++) {
                    for (int z = 0; z < size.z; z++) {
                        if (!FillsColumn(x, z)) {
                            continue;
                        }
                        for (int y = 0; y < size.y; y++) {
                            yield return origin + new Vector3Int(x, y, z);
                        }
                    }
                }
            }
        }

        /// <summary>Whether it fills that column of its box (turned, from its origin).</summary>
        public bool FillsColumn(int x, int z) {
            if (model.footprint == null) {
                return true;
            }
            Vector3Int size = Size;
            // the column's middle from the pivot, in half studs, turned back to the model's own axes: a quarter turn
            // takes (x, z) to (z, -x), so back is (-z, x)
            int px = 2 * x + 1 - size.x, pz = 2 * z + 1 - size.z;
            for (int turn = 0; turn < rotation; turn++) {
                int swap = px;
                px = -pz;
                pz = swap;
            }
            return model.Fills((px + model.width - 1) / 2, (pz + model.depth - 1) / 2);
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

        /// <summary>
        /// Position of the pivot in a vehicle (see Vehicles.Vehicle), whose cells start at its own origin: like Position
        /// without the world's floating origin.
        /// </summary>
        public Vector3 LocalPosition {
            get {
                Vector3Int size = Size;
                return Vector3.Scale(origin + new Vector3(size.x / 2f, 0, size.z / 2f), BrickGrid.CellSize);
            }
        }

        /// <summary>Its box in a vehicle's own space (see LocalPosition).</summary>
        public Bounds LocalBounds {
            get {
                Bounds bounds = new Bounds();
                bounds.SetMinMax(Vector3.Scale(origin, BrickGrid.CellSize), Vector3.Scale(origin + Size, BrickGrid.CellSize));
                return bounds;
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
