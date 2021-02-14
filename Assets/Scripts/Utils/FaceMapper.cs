using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/**
 * Gets the triangles, vertices & etc of each model's face
 * Place a plane right before each face and name it like the face position (Top, Bottom, etc..)
 * That object position will be used as reference to see if a vertice is before or after it, so it's part of that face.
*/
namespace Brickcraft.Utils { 
    public class FaceMapper
    {
        private Mesh mesh;
        private int[] triangles;
        private Vector3[] vertices;
        private Dictionary<Transform, Vector3> coordMap;

        public FaceMapper (Transform obj) {
            mesh = obj.GetComponent<MeshFilter>().sharedMesh;

            vertices = mesh.vertices;
            triangles = mesh.triangles;

            coordMap = new Dictionary<Transform, Vector3>() {
                {obj.Find("Top"), new Vector3(0, 1, 0)},
                {obj.Find("Bottom"), new Vector3(0, -1, 0)},
                {obj.Find("Right"), new Vector3(0, 0, -1)},
                {obj.Find("Left"), new Vector3(0, 0, 1)},
                {obj.Find("Front"), new Vector3(1, 0, 0)},
                {obj.Find("Back"), new Vector3(-1, 0, 0)},
            };
        }

        public Dictionary<string, FaceMap> getMapping () {
            Dictionary<string, FaceMap> map = new Dictionary<string, FaceMap>();

            foreach (KeyValuePair<Transform, Vector3> entry in coordMap) {
                map.Add(entry.Key.name.ToLower(), getFaceData(entry.Key.localPosition, entry.Value));
            }
            return map;
        }

        FaceMap getFaceData (Vector3 delimiter, Vector3 direction) {
            List<int> triangleList = new List<int>();
            List<Vector3> verticeList = new List<Vector3>();
            FaceMap faceMap = new FaceMap();

            for (int i = 0; i < triangles.Length; i += 3) { // each loop is a full triangle check (3 coords)

                if (isOnFace(vertices[triangles[i]], delimiter, direction) &&
                    isOnFace(vertices[triangles[i + 1]], delimiter, direction) &&
                    isOnFace(vertices[triangles[i + 2]], delimiter, direction)
                    ) {
                    triangleList.Add(triangles[i]);
                    triangleList.Add(triangles[i + 1]);
                    triangleList.Add(triangles[i + 2]);

                    verticeList.Add(vertices[triangles[i]]);
                    verticeList.Add(vertices[triangles[i + 1]]);
                    verticeList.Add(vertices[triangles[i + 2]]);
                }
            }

            // avoid duplicated vertices
            faceMap.vertices = verticeList.Distinct().ToArray();
            faceMap.triangles = new int[triangleList.Count];

            // remap triangles with the now distinct vertice list
            for (int i = 0; i < triangleList.Count; i++) {
                faceMap.triangles[i] = Array.IndexOf(faceMap.vertices, vertices[triangleList[i]]);
            }

            return faceMap;
        }

        bool isOnFace(Vector3 vertice, Vector3 delimiter, Vector3 direction) {
            if (direction.x != 0) {
                if (direction.x > 0 && vertice.x > delimiter.x) {
                    return true;
                } else if (direction.x < 0 && vertice.x < delimiter.x) {
                    return true;
                }
            } else if (direction.y != 0) {
                if (direction.y > 0 && vertice.y > delimiter.y) {
                    return true;
                } else if (direction.y < 0 && vertice.y < delimiter.y) {
                    return true;
                }
            } else if (direction.z != 0) {
                if (direction.z > 0 && vertice.z > delimiter.z) {
                    return true;
                } else if (direction.z < 0 && vertice.z < delimiter.z) {
                    return true;
                }
            }
            return false;
        }
    }
}