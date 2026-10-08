using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// Minimal Wavefront OBJ reader for block models: only vertex positions and faces are used,
    /// polygons are triangulated as fans. Like Unity's importer, X is mirrored to go from OBJ's
    /// right handed coordinates to Unity's left handed ones.
    /// </summary>
    public static class ObjLoader
    {
        public static void Load(string path, out Vector3[] vertices, out int[] triangles) {
            List<Vector3> vertexList = new List<Vector3>();
            List<int> triangleList = new List<int>();
            List<int> polygon = new List<int>();
            int lineNumber = 0;

            foreach (string rawLine in File.ReadLines(path)) {
                lineNumber++;
                string line = rawLine.Trim();

                if (line.StartsWith("v ")) {
                    string[] parts = split(line);
                    vertexList.Add(new Vector3(
                        -parseFloat(parts[1], path, lineNumber),
                        parseFloat(parts[2], path, lineNumber),
                        parseFloat(parts[3], path, lineNumber)
                    ));
                } else if (line.StartsWith("f ")) {
                    string[] parts = split(line);
                    polygon.Clear();

                    for (int i = 1; i < parts.Length; i++) {
                        // "v", "v/vt", "v//vn" or "v/vt/vn", negative indices count from the end
                        int index = int.Parse(parts[i].Split('/')[0], CultureInfo.InvariantCulture);
                        polygon.Add(index > 0 ? index - 1 : vertexList.Count + index);
                    }
                    for (int i = 1; i < polygon.Count - 1; i++) {
                        // mirroring X flips the winding, so reverse it back
                        triangleList.Add(polygon[0]);
                        triangleList.Add(polygon[i + 1]);
                        triangleList.Add(polygon[i]);
                    }
                }
            }

            vertices = vertexList.ToArray();
            triangles = triangleList.ToArray();

            foreach (int index in triangles) {
                if (index < 0 || index >= vertices.Length) {
                    throw new InvalidDataException("Face references a vertex that doesn't exist in " + path);
                }
            }
        }

        private static string[] split(string line) {
            return line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
        }

        private static float parseFloat(string value, string path, int lineNumber) {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)) {
                throw new InvalidDataException("Invalid number \"" + value + "\" at " + path + ":" + lineNumber);
            }
            return result;
        }
    }
}
