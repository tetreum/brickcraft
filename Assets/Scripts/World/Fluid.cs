using System.Collections.Generic;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// A fluid that flows (water), like Minecraft's: its source block (the "flow" of its block, see FlowInfo), a
    /// block for each level it spreads at, 1 to 7, lower and lower, and one for what flows down. Other blocks can be
    /// sources of it too ("of", like still_water, what the world generator fills lakes with). They're blocks of their
    /// own (named [source]_level_[n] and [source]_falling), so saves and the network handle them like any block;
    /// they have no item. FluidSimulator makes them flow.
    /// </summary>
    public class Fluid
    {
        /// <summary>The level of falling blocks: they spread around like a source where they land.</summary>
        public const int Falling = 8;
        public const int MaxLevel = 7;

        // the shapes of a flowing level: flat, sloped down toward one side, or a corner down toward two
        public const int FlatShape = 0;
        private const int SlopeShapes = 1; // + side - Front: Front, Back, Left, Right
        private const int CornerShapes = 5; // FrontRight, FrontLeft, BackLeft, BackRight
        public const int ShapeCount = 9;

        /// <summary>LDraw's parts the slopes are made of: a 2x2 45 slope (down toward -Z), and its double convex corner (down toward -Z and +X).</summary>
        public const string SlopePart = "3039";
        public const string CornerPart = "3045";

        /// <summary>Its main source block, what buckets pour.</summary>
        public BlockDefinition source;
        public FlowInfo flow;
        /// <summary>By level: 0 its source, 1 to MaxLevel flowing.</summary>
        public readonly BlockDefinition[] levels = new BlockDefinition[MaxLevel + 1];
        public BlockDefinition falling;

        /// <summary>How far it spreads, at most MaxLevel.</summary>
        public int Reach {
            get { return Mathf.Clamp(flow.reach, 1, MaxLevel); }
        }

        /// <summary>The block of that level (Falling too).</summary>
        public BlockDefinition At(int level) {
            return level == Falling ? falling : levels[Mathf.Clamp(level, 0, MaxLevel)];
        }

        /// <summary>How high it is in its block, 0 to 1: sources and falling fill it, flowing levels less each one (Minecraft's).</summary>
        public static float HeightOf(int level) {
            return level == 0 || level == Falling ? 1f : (MaxLevel + 1 - level) / (float)(MaxLevel + 2);
        }

        /// <summary>
        /// The shape of a flowing level, by which of its sides are lower (air or lower fluid next to it, see
        /// ChunkRenderer): one of them a slope down toward it, two side by side a corner, otherwise flat.
        /// </summary>
        public static int ShapeFor(bool front, bool back, bool left, bool right) {
            int lower = (front ? 1 : 0) + (back ? 1 : 0) + (left ? 1 : 0) + (right ? 1 : 0);
            if (lower == 1) {
                return SlopeShapes + (front ? 0 : back ? 1 : left ? 2 : 3);
            }
            if (lower == 2) {
                if (front && right) return CornerShapes;
                if (front && left) return CornerShapes + 1;
                if (back && left) return CornerShapes + 2;
                if (back && right) return CornerShapes + 3;
            }
            return FlatShape;
        }

        // -------- shapes --------

        private static readonly Dictionary<int, BlockShape[]> shapesByLevel = new Dictionary<int, BlockShape[]>();
        private static Vector3[] flatVertices, slopeVertices, cornerVertices;
        private static int[] flatTriangles, slopeTriangles, cornerTriangles;

        /// <summary>Forgets the shapes (the default block model may have changed).</summary>
        public static void ClearShapes() {
            shapesByLevel.Clear();
            flatVertices = slopeVertices = cornerVertices = null;
        }

        /// <summary>The shapes of a flowing level (see ShapeFor), made the first time (main thread: LDraw's parts are read).</summary>
        public static BlockShape[] ShapesOf(int level, Vector3[] blockVertices, int[] blockTriangles) {
            if (shapesByLevel.TryGetValue(level, out BlockShape[] shapes)) {
                return shapes;
            }
            if (flatVertices == null) {
                flatVertices = blockVertices;
                flatTriangles = blockTriangles;
                loadPart(SlopePart, out slopeVertices, out slopeTriangles);
                loadPart(CornerPart, out cornerVertices, out cornerTriangles);
            }
            float height = HeightOf(level);
            shapes = new BlockShape[ShapeCount];
            shapes[FlatShape] = shape(flatVertices, flatTriangles, height, 0);
            // the slope is down toward the front (-Z): turned for the others; the corner toward the front and right
            float[] slopeTurns = { 0, 180, 90, -90 };
            float[] cornerTurns = { 0, 90, 180, -90 };
            for (int i = 0; i < 4; i++) {
                shapes[SlopeShapes + i] = slopeVertices != null ? shape(slopeVertices, slopeTriangles, height, slopeTurns[i]) : shapes[FlatShape];
                shapes[CornerShapes + i] = cornerVertices != null ? shape(cornerVertices, cornerTriangles, height, cornerTurns[i]) : shapes[FlatShape];
            }
            shapesByLevel[level] = shapes;
            return shapes;
        }

        // squashed to that part of a block's height, turned around its middle
        private static BlockShape shape(Vector3[] vertices, int[] triangles, float height, float turn) {
            Quaternion rotation = Quaternion.Euler(0, turn, 0);
            Vector3[] placed = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) {
                Vector3 v = vertices[i];
                placed[i] = rotation * new Vector3(v.x, v.y * height, v.z);
            }
            return new BlockShape(placed, triangles, Server.brickHeight * height);
        }

        // an LDraw part, its pivot in the middle of its footprint on its bottom, like block models
        private static void loadPart(string part, out Vector3[] vertices, out int[] triangles) {
            vertices = null;
            triangles = null;
            Mesh mesh = Bricks.LDraw.LDrawModelBuilder.Build(part, out _);
            if (mesh == null || mesh.vertexCount == 0) {
                Debug.LogWarning("The LDraw library has no " + part + ": flowing fluids are flat");
                return;
            }
            Bounds bounds = mesh.bounds;
            Vector3 pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) {
                vertices[i] -= pivot;
            }
            triangles = mesh.triangles;
            if (Application.isPlaying) {
                Object.Destroy(mesh);
            } else {
                Object.DestroyImmediate(mesh); // the editor's tools load the blocks too
            }
        }
    }
}
