using System.Collections.Generic;
using Brickcraft.Bricks;
using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>
    /// The cells a vehicle's bricks fill, in its own space (see Vehicle): like the BrickGrid, studs sideways and plates
    /// up, but moving and turning with the vehicle. Bricks touching by a face are connected; a vehicle is one group of them.
    /// </summary>
    public class VehicleGrid
    {
        private static readonly Vector3Int[] Neighbours = {
            Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        private readonly Dictionary<Vector3Int, Brick> cells = new Dictionary<Vector3Int, Brick>();
        private readonly HashSet<Brick> bricks = new HashSet<Brick>();

        public int Count {
            get { return bricks.Count; }
        }

        public IEnumerable<Brick> Bricks {
            get { return bricks; }
        }

        /// <summary>A brick went in or out.</summary>
        public event System.Action Changed;

        public void Register(Brick brick) {
            bricks.Add(brick);
            Changed?.Invoke();
            foreach (Vector3Int cell in brick.placement.Cells) {
                cells[cell] = brick;
            }
        }

        public void Unregister(Brick brick) {
            if (!bricks.Remove(brick)) {
                return;
            }
            Changed?.Invoke();
            foreach (Vector3Int cell in brick.placement.Cells) {
                if (cells.TryGetValue(cell, out Brick there) && there == brick) {
                    cells.Remove(cell);
                }
            }
        }

        public Brick GetBrickAt(Vector3Int cell) {
            return cells.TryGetValue(cell, out Brick brick) ? brick : null;
        }

        public bool IsFree(BrickPlacement placement) {
            foreach (Vector3Int cell in placement.Cells) {
                if (cells.ContainsKey(cell)) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The bricks a placement would touch by a face.</summary>
        public HashSet<Brick> Touching(BrickPlacement placement) {
            HashSet<Brick> touched = new HashSet<Brick>();
            foreach (Vector3Int cell in placement.Cells) {
                foreach (Vector3Int step in Neighbours) {
                    Brick brick = GetBrickAt(cell + step);
                    if (brick != null) {
                        touched.Add(brick);
                    }
                }
            }
            return touched;
        }

        /// <summary>Its bricks in groups that touch each other, the biggest group first.</summary>
        public List<List<Brick>> Components() {
            List<List<Brick>> groups = new List<List<Brick>>();
            HashSet<Brick> seen = new HashSet<Brick>();
            foreach (Brick start in bricks) {
                if (!seen.Add(start)) {
                    continue;
                }
                List<Brick> group = new List<Brick>();
                Queue<Brick> queue = new Queue<Brick>();
                queue.Enqueue(start);
                while (queue.Count > 0) {
                    Brick brick = queue.Dequeue();
                    group.Add(brick);
                    foreach (Vector3Int cell in brick.placement.Cells) {
                        foreach (Vector3Int step in Neighbours) {
                            Brick next = GetBrickAt(cell + step);
                            if (next != null && seen.Add(next)) {
                                queue.Enqueue(next);
                            }
                        }
                    }
                }
                groups.Add(group);
            }
            groups.Sort((a, b) => b.Count.CompareTo(a.Count));
            return groups;
        }

        // -------- another grid's cells in this one's: grids that line up (a whole number of cells apart, turned by quarter turns) --------

        /// <summary>A point in cells turned by quarter turns around the vertical, like Quaternion.Euler(0, 90 * turns, 0).</summary>
        public static Vector3Int Turn(Vector3Int point, int turns) {
            turns = BrickGrid.Mod(turns, 4);
            for (int i = 0; i < turns; i++) {
                point = new Vector3Int(point.z, point.y, -point.x);
            }
            return point;
        }

        /// <summary>
        /// A brick's placement in another grid whose cells are this one's turned by turns and moved by offset (cells): its box
        /// turned like that, its rotation too.
        /// </summary>
        public static BrickPlacement Map(BrickPlacement placement, int turns, Vector3Int offset) {
            Vector3Int a = Turn(placement.origin, turns), b = Turn(placement.origin + placement.Size, turns);
            Vector3Int min = Vector3Int.Min(a, b);
            return new BrickPlacement(placement.model, min + offset, placement.rotation + turns);
        }

        /// <summary>
        /// How another vehicle's cells are this one's, if they line up: its rotation a whole number of quarter turns around
        /// this one's vertical, and its origin a whole number of cells away. False if they don't.
        /// </summary>
        public static bool LinesUp(Transform to, Transform from, out int turns, out Vector3Int offset) {
            turns = 0;
            offset = Vector3Int.zero;
            Quaternion relative = Quaternion.Inverse(to.rotation) * from.rotation;
            Vector3 euler = relative.eulerAngles;
            if (Mathf.Abs(Mathf.DeltaAngle(euler.x, 0)) > 2 || Mathf.Abs(Mathf.DeltaAngle(euler.z, 0)) > 2) {
                return false;
            }
            float quarter = euler.y / 90f;
            if (Mathf.Abs(quarter - Mathf.Round(quarter)) > 2f / 90) {
                return false;
            }
            turns = BrickGrid.Mod(Mathf.RoundToInt(quarter), 4);
            Vector3 local = to.InverseTransformPoint(from.position);
            Vector3 cells = new Vector3(local.x / BrickGrid.CellSize.x, local.y / BrickGrid.CellSize.y, local.z / BrickGrid.CellSize.z);
            Vector3Int whole = Vector3Int.RoundToInt(cells);
            if ((cells - whole).magnitude > 0.05f) {
                return false;
            }
            offset = whole;
            return true;
        }
    }
}
