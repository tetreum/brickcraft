using System.Collections.Generic;
using Brickcraft.Utils;
using UnityEngine;

namespace Brickcraft.World
{
    public enum BlockSide
    {
        Top,
        Bottom,
        Front, // -Z
        Back,  // +Z
        Left,  // -X
        Right, // +X
    }

    /// <summary>
    /// The geometry of a block, split by side so the chunk renderer only adds the sides
    /// that are exposed to air.
    ///
    /// Models use game units with the pivot at the center of the footprint, on the bottom face,
    /// so a block spans [-width/2, width/2] x [0, height] x [-width/2, width/2].
    /// Every triangle goes to the side it's closest to (studs go to the top). Triangles facing
    /// the inside of the block (the hollow underside of a brick) go to the bottom, which is
    /// the side they can be seen through.
    /// </summary>
    public class BlockShape
    {
        public const int SideCount = 6;

        public static readonly Vector3Int[] SideDirections = {
            Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1), Vector3Int.left, Vector3Int.right
        };

        // triangles facing this much against a side's outward direction are inside the block
        private const float InwardThreshold = -0.1f;

        private readonly FaceMap[] sides;

        public FaceMap GetSide(BlockSide side) {
            return sides[(int)side];
        }

        public BlockShape(Vector3[] vertices, int[] triangles) {
            List<int>[] sideTriangles = new List<int>[SideCount];
            for (int i = 0; i < SideCount; i++) {
                sideTriangles[i] = new List<int>();
            }

            for (int i = 0; i < triangles.Length; i += 3) {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                BlockSide side = classify((a + b + c) / 3f, Vector3.Cross(b - a, c - a).normalized);

                sideTriangles[(int)side].Add(triangles[i]);
                sideTriangles[(int)side].Add(triangles[i + 1]);
                sideTriangles[(int)side].Add(triangles[i + 2]);
            }

            Vector3[] normals = calculateNormals(vertices, triangles);

            sides = new FaceMap[SideCount];
            for (int i = 0; i < SideCount; i++) {
                sides[i] = extract(vertices, normals, sideTriangles[i]);
            }
        }

        public static BlockShape FromObj(string path) {
            ObjLoader.Load(path, out Vector3[] vertices, out int[] triangles);
            return new BlockShape(vertices, triangles);
        }

        private static BlockSide classify(Vector3 center, Vector3 normal) {
            float halfWidth = Server.brickWidth / 2;
            float height = Server.brickHeight;

            // distance to each side, relative to the block size so tall and flat sides compare fairly
            float[] distances = {
                (height - center.y) / height,
                center.y / height,
                (center.z + halfWidth) / Server.brickWidth,
                (halfWidth - center.z) / Server.brickWidth,
                (center.x + halfWidth) / Server.brickWidth,
                (halfWidth - center.x) / Server.brickWidth,
            };

            int closest = 0;
            for (int i = 1; i < SideCount; i++) {
                if (distances[i] < distances[closest]) {
                    closest = i;
                }
            }

            if (Vector3.Dot(normal, SideDirections[closest]) < InwardThreshold) {
                return BlockSide.Bottom;
            }
            return (BlockSide)closest;
        }

        // area weighted normals of the shared vertices, like Mesh.RecalculateNormals, computed once
        // here so the terrain meshes don't have to recalculate them
        private static Vector3[] calculateNormals(Vector3[] vertices, int[] triangles) {
            Vector3[] normals = new Vector3[vertices.Length];

            for (int i = 0; i < triangles.Length; i += 3) {
                Vector3 a = vertices[triangles[i]];
                Vector3 faceNormal = Vector3.Cross(vertices[triangles[i + 1]] - a, vertices[triangles[i + 2]] - a);

                normals[triangles[i]] += faceNormal;
                normals[triangles[i + 1]] += faceNormal;
                normals[triangles[i + 2]] += faceNormal;
            }
            for (int i = 0; i < normals.Length; i++) {
                normals[i] = normals[i].normalized;
            }
            return normals;
        }

        // copies the triangles of a side with their own compact vertex list
        private static FaceMap extract(Vector3[] vertices, Vector3[] normals, List<int> triangles) {
            Dictionary<int, int> remap = new Dictionary<int, int>();
            List<Vector3> sideVertices = new List<Vector3>();
            List<Vector3> sideNormals = new List<Vector3>();
            int[] sideTriangles = new int[triangles.Count];

            for (int i = 0; i < triangles.Count; i++) {
                if (!remap.TryGetValue(triangles[i], out int index)) {
                    index = sideVertices.Count;
                    remap.Add(triangles[i], index);
                    sideVertices.Add(vertices[triangles[i]]);
                    sideNormals.Add(normals[triangles[i]]);
                }
                sideTriangles[i] = index;
            }

            return new FaceMap() {
                vertices = sideVertices.ToArray(),
                normals = sideNormals.ToArray(),
                triangles = sideTriangles,
            };
        }
    }
}
