using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// Which columns of its box a model's shape fills (see BrickModel.footprint): the corner brick 2357 fills 3 of its
    /// 2x2, so another part fits in its notch. A column is filled if any of the shape's triangles, seen from above,
    /// overlaps it (a little inside its sides: the notch's walls are on them); so a column with only a sliver of the
    /// shape, or a hole through it, still counts as filled, and nothing ends up inside a part.
    /// </summary>
    public static class BrickFootprint
    {
        // how far inside a column's sides the shape has to reach, in studs
        private const float Margin = 0.15f;

        /// <summary>
        /// The columns a mesh (in the game's units, its pivot in the middle of its footprint, see BrickModel) fills, x
        /// + z * width; null if it fills them all.
        /// </summary>
        public static bool[] Of(Mesh mesh, int width, int depth) {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            bool[] filled = new bool[width * depth];
            float stud = Server.studSize;
            Vector2 corner = new Vector2(-width * stud / 2, -depth * stud / 2);
            int left = width * depth;
            for (int t = 0; t < triangles.Length && left > 0; t += 3) {
                Vector2 a = flat(vertices[triangles[t]]), b = flat(vertices[triangles[t + 1]]), c = flat(vertices[triangles[t + 2]]);
                // only the columns under the triangle's bounds
                Vector2 min = Vector2.Min(a, Vector2.Min(b, c)) - corner, max = Vector2.Max(a, Vector2.Max(b, c)) - corner;
                int fromX = Mathf.Max(0, Mathf.FloorToInt(min.x / stud)), toX = Mathf.Min(width - 1, Mathf.FloorToInt(max.x / stud));
                int fromZ = Mathf.Max(0, Mathf.FloorToInt(min.y / stud)), toZ = Mathf.Min(depth - 1, Mathf.FloorToInt(max.y / stud));
                for (int x = fromX; x <= toX; x++) {
                    for (int z = fromZ; z <= toZ; z++) {
                        if (filled[x + z * width]) {
                            continue;
                        }
                        Vector2 low = corner + new Vector2(x + Margin, z + Margin) * stud;
                        Vector2 high = corner + new Vector2(x + 1 - Margin, z + 1 - Margin) * stud;
                        if (overlaps(a, b, c, low, high)) {
                            filled[x + z * width] = true;
                            left--;
                        }
                    }
                }
            }
            return left == 0 ? null : filled;
        }

        private static Vector2 flat(Vector3 point) {
            return new Vector2(point.x, point.z);
        }

        // a triangle (maybe flat to a segment, a wall seen from above) and a rectangle overlap: no axis separates them
        private static bool overlaps(Vector2 a, Vector2 b, Vector2 c, Vector2 low, Vector2 high) {
            if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) <= low.x || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) >= high.x
                    || Mathf.Max(a.y, Mathf.Max(b.y, c.y)) <= low.y || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) >= high.y) {
                return false;
            }
            return !separates(a, b, c, low, high, b - a) && !separates(a, b, c, low, high, c - b) && !separates(a, b, c, low, high, a - c);
        }

        // the line across that edge has the triangle on one side and the rectangle on the other
        private static bool separates(Vector2 a, Vector2 b, Vector2 c, Vector2 low, Vector2 high, Vector2 edge) {
            Vector2 normal = new Vector2(-edge.y, edge.x);
            if (normal.sqrMagnitude < 1e-12f) {
                return false;
            }
            float t0 = Vector2.Dot(a, normal), t1 = Vector2.Dot(b, normal), t2 = Vector2.Dot(c, normal);
            float triangleMin = Mathf.Min(t0, Mathf.Min(t1, t2)), triangleMax = Mathf.Max(t0, Mathf.Max(t1, t2));
            float r0 = Vector2.Dot(low, normal), r1 = Vector2.Dot(new Vector2(high.x, low.y), normal);
            float r2 = Vector2.Dot(high, normal), r3 = Vector2.Dot(new Vector2(low.x, high.y), normal);
            float rectangleMin = Mathf.Min(Mathf.Min(r0, r1), Mathf.Min(r2, r3)), rectangleMax = Mathf.Max(Mathf.Max(r0, r1), Mathf.Max(r2, r3));
            return triangleMax <= rectangleMin || triangleMin >= rectangleMax;
        }
    }
}
