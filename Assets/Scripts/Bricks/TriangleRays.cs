using System;
using System.Collections.Generic;
using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// Whether rays hit any of a set of triangles (both their sides), through a bounding volume hierarchy. Plain
    /// C#, so it runs on any thread (CreationModel looks for hidden faces with it in the background); once built
    /// it's only read, so several threads can cast rays at once.
    /// </summary>
    public class TriangleRays
    {
        private const int LeafSize = 4;
        private const int MaxDepth = 64;

        private struct Node
        {
            public Vector3 min, max;
            // a leaf: its triangles, from first; otherwise its children are right after it and at second
            public int first, count, second;
        }

        private readonly Vector3[] a, b, c;
        private readonly int[] order;
        private readonly List<Node> nodes = new List<Node>();
        private Node[] tree;

        [ThreadStatic] private static int[] stack;

        /// <summary>The triangles, as their corners (each triangle is a[i], b[i], c[i]).</summary>
        public TriangleRays(Vector3[] a, Vector3[] b, Vector3[] c) {
            this.a = a;
            this.b = b;
            this.c = c;
            order = new int[a.Length];
            Vector3[] centers = new Vector3[a.Length];
            for (int i = 0; i < a.Length; i++) {
                order[i] = i;
                centers[i] = (a[i] + b[i] + c[i]) / 3;
            }
            if (a.Length > 0) {
                build(centers, 0, a.Length, 0);
            }
            tree = nodes.ToArray();
            nodes.Clear();
        }

        // the node of those triangles (order[start..end)), split on the longest side of their middles
        private int build(Vector3[] centers, int start, int end, int depth) {
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            Vector3 centersMin = Vector3.positiveInfinity, centersMax = Vector3.negativeInfinity;
            for (int i = start; i < end; i++) {
                int t = order[i];
                min = Vector3.Min(min, Vector3.Min(a[t], Vector3.Min(b[t], c[t])));
                max = Vector3.Max(max, Vector3.Max(a[t], Vector3.Max(b[t], c[t])));
                centersMin = Vector3.Min(centersMin, centers[t]);
                centersMax = Vector3.Max(centersMax, centers[t]);
            }
            int index = nodes.Count;
            nodes.Add(new Node() { min = min, max = max, first = start, count = end - start });
            if (end - start <= LeafSize || depth >= MaxDepth - 2) {
                return index;
            }
            Vector3 size = centersMax - centersMin;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            float split = (centersMin[axis] + centersMax[axis]) / 2;
            int middle = start;
            for (int i = start; i < end; i++) {
                if (centers[order[i]][axis] < split) {
                    int swap = order[i];
                    order[i] = order[middle];
                    order[middle] = swap;
                    middle++;
                }
            }
            // all on one side (the same middle): halves
            if (middle == start || middle == end) {
                middle = (start + end) / 2;
            }
            build(centers, start, middle, depth + 1);
            int second = build(centers, middle, end, depth + 1);
            Node node = nodes[index];
            node.count = 0;
            node.second = second;
            nodes[index] = node;
            return index;
        }

        /// <summary>Whether the ray hits a triangle closer than that distance (its direction normalized).</summary>
        public bool Hits(Vector3 origin, Vector3 direction, float distance) {
            if (tree.Length == 0) {
                return false;
            }
            if (stack == null) {
                stack = new int[MaxDepth * 2];
            }
            Vector3 inverse = new Vector3(1 / direction.x, 1 / direction.y, 1 / direction.z);
            int top = 0;
            stack[top++] = 0;
            while (top > 0) {
                int index = stack[--top];
                Node node = tree[index];
                if (!hitsBox(origin, inverse, node.min, node.max, distance)) {
                    continue;
                }
                if (node.count > 0) {
                    for (int i = node.first; i < node.first + node.count; i++) {
                        int t = order[i];
                        if (hitsTriangle(origin, direction, a[t], b[t], c[t], distance)) {
                            return true;
                        }
                    }
                    continue;
                }
                stack[top++] = node.second;
                stack[top++] = index + 1;
            }
            return false;
        }

        private static bool hitsBox(Vector3 origin, Vector3 inverse, Vector3 min, Vector3 max, float distance) {
            float t1 = (min.x - origin.x) * inverse.x, t2 = (max.x - origin.x) * inverse.x;
            float near = Math.Min(t1, t2), far = Math.Max(t1, t2);
            t1 = (min.y - origin.y) * inverse.y;
            t2 = (max.y - origin.y) * inverse.y;
            near = Math.Max(near, Math.Min(t1, t2));
            far = Math.Min(far, Math.Max(t1, t2));
            t1 = (min.z - origin.z) * inverse.z;
            t2 = (max.z - origin.z) * inverse.z;
            near = Math.Max(near, Math.Min(t1, t2));
            far = Math.Min(far, Math.Max(t1, t2));
            return far >= Math.Max(near, 0) && near <= distance;
        }

        // Möller-Trumbore, either side
        private static bool hitsTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, float distance) {
            const float Epsilon = 1e-7f;
            Vector3 edge1 = b - a, edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (determinant > -Epsilon && determinant < Epsilon) {
                return false;
            }
            float inverse = 1 / determinant;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inverse;
            if (u < 0 || u > 1) {
                return false;
            }
            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0 || u + v > 1) {
                return false;
            }
            float t = Vector3.Dot(edge2, q) * inverse;
            return t > 0 && t <= distance;
        }
    }
}
