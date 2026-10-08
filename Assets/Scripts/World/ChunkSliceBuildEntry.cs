using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	/// <summary>
	/// Mesh data of a chunk slice, fully computed by the meshing threads so the main thread
	/// only has to copy it into a Mesh.
	/// </summary>
	public class ChunkSliceBuildEntry {
		public TerrainVertex[] Vertices;
		public int[] Triangles;
		public Chunk ParentChunk;
		public int SliceIndex;
		public int ChunkVersion;

		public Vector3[] ColliderVertices;
		public int[] ColliderTriangles;
	}

	/// <summary>One interleaved vertex of the terrain mesh, uploaded as is.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct TerrainVertex
	{
		public Vector3 position;
		public Vector3 normal;
		public Color32 color;
		public Vector2 uv;

		public static readonly VertexAttributeDescriptor[] Layout = {
			new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
			new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
			new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
			new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
		};
	}
}
