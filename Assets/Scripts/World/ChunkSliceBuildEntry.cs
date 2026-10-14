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
		/// <summary>The opaque sides first, then the translucent ones (from TranslucentStart).</summary>
		public int[] Triangles;
		public int TranslucentStart;
		public Chunk ParentChunk;
		public int SliceIndex;
		public int ChunkVersion;

		// queued by a chunk render job, counted in the chunk's PendingSliceUploads
		public bool CountsAsPending;

		public Vector3[] ColliderVertices;
		public int[] ColliderTriangles;
	}

	/// <summary>
	/// One interleaved vertex of the terrain mesh, uploaded as is (24 bytes): its normal in bytes, its uv (which
	/// only says the texture layer, see TerrainTextures.LayerToUV) in half floats, exact for its values.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct TerrainVertex
	{
		public Vector3 position;
		private sbyte normalX, normalY, normalZ, normalW;
		public Color32 color;
		private Unity.Mathematics.half u, v;

		public Vector3 normal {
			set {
				normalX = toSNorm8(value.x);
				normalY = toSNorm8(value.y);
				normalZ = toSNorm8(value.z);
				normalW = 0;
			}
		}

		public Vector2 uv {
			set {
				u = new Unity.Mathematics.half(value.x);
				v = new Unity.Mathematics.half(value.y);
			}
		}

		private static sbyte toSNorm8(float value) {
			return (sbyte)Mathf.RoundToInt(Mathf.Clamp(value, -1f, 1f) * 127f);
		}

		public static readonly VertexAttributeDescriptor[] Layout = {
			new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
			new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
			new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
			new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2),
		};
	}
}
