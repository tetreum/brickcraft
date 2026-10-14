using System.IO;
using Brickcraft.Bricks;
using Brickcraft.World;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>Fluid heights and shapes, TriangleRays against checking every triangle, and reading OBJ files.</summary>
    public class GeometryTests
    {
        [Test]
        public void FluidsGetLowerEachLevel() {
            Assert.AreEqual(1f, Fluid.HeightOf(0));
            Assert.AreEqual(1f, Fluid.HeightOf(Fluid.Falling));
            for (int level = 1; level <= Fluid.MaxLevel; level++) {
                Assert.Less(Fluid.HeightOf(level), Fluid.HeightOf(level - 1), "level " + level);
                Assert.Greater(Fluid.HeightOf(level), 0);
            }
        }

        [Test]
        public void FluidShapesSlopeTowardLowerSides() {
            Assert.AreEqual(Fluid.FlatShape, Fluid.ShapeFor(false, false, false, false));
            Assert.AreEqual(Fluid.FlatShape, Fluid.ShapeFor(true, true, false, false), "opposite sides");
            Assert.AreEqual(Fluid.FlatShape, Fluid.ShapeFor(true, true, true, false));

            int[] shapes = {
                Fluid.ShapeFor(true, false, false, false), Fluid.ShapeFor(false, true, false, false),
                Fluid.ShapeFor(false, false, true, false), Fluid.ShapeFor(false, false, false, true),
                Fluid.ShapeFor(true, false, false, true), Fluid.ShapeFor(true, false, true, false),
                Fluid.ShapeFor(false, true, true, false), Fluid.ShapeFor(false, true, false, true),
            };
            CollectionAssert.AllItemsAreUnique(shapes);
            foreach (int shape in shapes) {
                Assert.That(shape, Is.InRange(1, Fluid.ShapeCount - 1));
            }
        }

        [Test]
        public void AFluidsSlopeIsAllOnItsTop() {
            // a slope down toward the front, across the whole block: from the top of the back to the bottom of the front
            float w = Server.brickWidth / 2, h = Server.brickHeight;
            Vector3[] vertices = { new Vector3(-w, 0, -w), new Vector3(w, 0, -w), new Vector3(w, h, w), new Vector3(-w, h, w) };
            int[] triangles = { 0, 2, 1, 0, 3, 2 };
            Assert.Greater(Vector3.Cross(vertices[2] - vertices[0], vertices[1] - vertices[0]).y, 0, "it faces up");

            BlockShape surface = new BlockShape(vertices, triangles, h, true);
            Assert.AreEqual(6, surface.GetSide(BlockSide.Top).triangles.Length, "both triangles on the top: drawn while there's air above");

            // as a block's model, its triangles go to the sides nearest them: a slope there would be split
            BlockShape model = new BlockShape(vertices, triangles, h);
            Assert.Less(model.GetSide(BlockSide.Top).triangles.Length, 6);
        }

        [Test]
        public void TriangleRaysHitWhatEveryTriangleWould() {
            System.Random random = new System.Random(1234);
            const int Count = 300;
            Vector3[] a = new Vector3[Count], b = new Vector3[Count], c = new Vector3[Count];
            for (int i = 0; i < Count; i++) {
                Vector3 centre = point(random, 6);
                a[i] = centre + point(random, 1.5f);
                b[i] = centre + point(random, 1.5f);
                c[i] = centre + point(random, 1.5f);
            }
            TriangleRays rays = new TriangleRays(a, b, c);

            int hitCount = 0;
            for (int r = 0; r < 2000; r++) {
                Vector3 origin = point(random, 8);
                Vector3 direction = point(random, 1).normalized;
                float distance = (float)random.NextDouble() * 15;

                bool expected = false;
                for (int i = 0; i < Count && !expected; i++) {
                    expected = hits(origin, direction, a[i], b[i], c[i], distance);
                }
                Assert.AreEqual(expected, rays.Hits(origin, direction, distance), "ray " + r);
                hitCount += expected ? 1 : 0;
            }
            Assert.Greater(hitCount, 200, "enough rays hit something to mean anything");
        }

        [Test]
        public void NoTrianglesNoHits() {
            Assert.IsFalse(new TriangleRays(new Vector3[0], new Vector3[0], new Vector3[0]).Hits(Vector3.zero, Vector3.up, 100));
        }

        private static Vector3 point(System.Random random, float size) {
            return new Vector3((float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1)) * size;
        }

        // Möller-Trumbore, either side, in doubles: what TriangleRays should find
        private static bool hits(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, float distance) {
            Vector3 edge1 = b - a, edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            double determinant = Vector3.Dot(edge1, p);
            if (System.Math.Abs(determinant) < 1e-9) {
                return false;
            }
            Vector3 s = origin - a;
            double u = Vector3.Dot(s, p) / determinant;
            if (u < 0 || u > 1) {
                return false;
            }
            Vector3 q = Vector3.Cross(s, edge1);
            double v = Vector3.Dot(direction, q) / determinant;
            if (v < 0 || u + v > 1) {
                return false;
            }
            double t = Vector3.Dot(edge2, q) / determinant;
            return t >= 0 && t <= distance;
        }

        [Test]
        public void ObjFilesAreMirroredAndKeepTheirWinding() {
            string path = Path.Combine(Path.GetTempPath(), "brickcraft-test-" + System.Guid.NewGuid() + ".obj");
            File.WriteAllText(path, "# a quad facing +z\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nvn 0 0 1\nf 1//1 2//1 3//1 4//1\n");
            try {
                ObjLoader.Load(path, out Vector3[] vertices, out int[] triangles);

                Assert.AreEqual(new Vector3(-1, 0, 0), vertices[1], "x mirrored");
                Assert.AreEqual(6, triangles.Length, "a fan of two triangles");
                Vector3 normal = Vector3.Cross(vertices[triangles[1]] - vertices[triangles[0]], vertices[triangles[2]] - vertices[triangles[0]]);
                // Unity's front faces are clockwise, their cross product points out of the front
                Assert.Greater(normal.z, 0, "still facing +z");
            } finally {
                File.Delete(path);
            }
        }
    }
}
