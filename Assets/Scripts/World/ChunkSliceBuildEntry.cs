using System.Collections.Generic;
using UnityEngine;

// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	public class ChunkSliceBuildEntry {
		public Vector3[] Vertices;
		public int[] Triangles;
		public Color[] Colors;
		public Vector2[] Uvs;
		public Chunk ParentChunk;
		public int SliceIndex;
		public Dictionary<byte, Vector3[]> meshes;
	}
}
