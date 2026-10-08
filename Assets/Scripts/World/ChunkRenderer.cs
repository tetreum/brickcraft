using Brickcraft.Utils;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Brickcraft.World
{
	public class ChunkRenderer
	{
		private static Color32 firstSideColor = new Color(0.9f, 0.9f, 0.9f, 1.0f);
		private static Color32 secondSideColor = new Color(0.8f, 0.8f, 0.8f, 1.0f);
		private static Color32 topColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		private static Color32 bottomColor = new Color(0.7f, 0.7f, 0.7f, 1.0f);

		// indexed by BlockSide
		private static readonly Color32[] sideColors = {
			topColor, bottomColor, firstSideColor, firstSideColor, secondSideColor, secondSideColor
		};

		// sides of the blocks that are exposed to air, gathered before building the mesh arrays
		private struct VisibleSide
		{
			public BlockShape shape;
			public BlockDefinition definition;
			public BlockSide side;
			public Vector3 offset;
		}

		private List<int> indexesToDelete = new List<int>();
		private List<VisibleSide> visibleSides = new List<VisibleSide>();

		public void RenderChunk(Chunk chunk, bool detailed)
		{
			WorldBehaviour world = chunk.World;
			int version = Volatile.Read(ref chunk.Version);

			int minSliceIndex = chunk.MinSliceIndex;

			Chunk frontChunk = world.GetChunk(chunk.X, chunk.Z - 1);
			Chunk backChunk = world.GetChunk(chunk.X, chunk.Z + 1);
			Chunk leftChunk = world.GetChunk(chunk.X - 1, chunk.Z);
			Chunk rightChunk = world.GetChunk(chunk.X + 1, chunk.Z);

			if(frontChunk != null && frontChunk.MinSliceIndex < minSliceIndex)
				minSliceIndex = frontChunk.MinSliceIndex;

			if(backChunk != null && backChunk.MinSliceIndex < minSliceIndex)
				minSliceIndex = backChunk.MinSliceIndex;

			if(leftChunk != null && leftChunk.MinSliceIndex < minSliceIndex)
				minSliceIndex = leftChunk.MinSliceIndex;

			if(rightChunk != null && rightChunk.MinSliceIndex < minSliceIndex)
				minSliceIndex = rightChunk.MinSliceIndex;

			for(int i = Chunk.NumSlices - 1; i >= 0; --i)
			{
				ChunkSlice chunkSlice = chunk.Slices[i];

				if(i < minSliceIndex)
				{
					for(int index = i; index >= 0; --index)
						indexesToDelete.Add(index);
					break;
				}

				if(chunkSlice.IsEmpty)
				{
					indexesToDelete.Add(i);
					continue;
				}

				int minHeight = chunk.MinSliceIndex == chunkSlice.Index ? (chunk.LowestY & Chunk.SliceHeightLimit) : 0;

				ChunkSliceBuildEntry chunkEntry = RenderSlice(chunk, i, detailed, version, minHeight);

				lock (WorldBehaviour.ChunkQueueLock)
					WorldBehaviour.ChunkSlicesToBuild.Enqueue(chunkEntry);
			}
			if(indexesToDelete.Count > 0)
			{
				lock(WorldBehaviour.SliceLock)
				{
					ChunkSlicesDeleteEntry chunkSliceEntry = new ChunkSlicesDeleteEntry();
					chunkSliceEntry.indexesToDelete = indexesToDelete.ToArray();
					chunkSliceEntry.parentChunk = chunk;

					WorldBehaviour.SlicesToDelete.Enqueue(chunkSliceEntry);
				}
			}
		}

		/// <summary>
		/// Builds the mesh data of a single slice. Used both by the initial world rendering
		/// and to refresh a slice after one of its blocks changed.
		/// </summary>
		public ChunkSliceBuildEntry RenderSlice(Chunk chunk, int sliceIndex, bool detailed, int version, int minHeight = 0)
		{
			ChunkSlice chunkSlice = chunk.Slices[sliceIndex];
			int vertexCount = 0;
			int triangleCount = 0;
			int colliderVertexCount = 0;
			int colliderTriangleCount = 0;

			// first find what has to be drawn, so the mesh arrays can be allocated at their final size
			visibleSides.Clear();

			for (int x = 0; x < 16; x++)
			{
				for (int z = 0; z < 16; z++)
				{
					for (int y = Chunk.SliceHeight - 1; y >= 0 && y >= minHeight; --y)
					{
						byte block = chunkSlice[x, y, z];

						if(block == 0)
							continue;

						BlockDefinition definition = BlockDatabase.Get(block);
						Vector3 offset = new Vector3(x * Server.brickWidth, y * Server.brickHeight, z * Server.brickWidth);

						// only sides that are exposed to air are rendered
						for (int side = 0; side < BlockShape.SideCount; side++)
						{
							Vector3Int direction = BlockShape.SideDirections[side];

							if (GetNeighbour(chunk, sliceIndex, x + direction.x, y + direction.y, z + direction.z) != 0)
								continue;

							BlockShape shape = detailed ? definition.shape : definition.colliderShape;
							visibleSides.Add(new VisibleSide() { shape = shape, definition = definition, side = (BlockSide)side, offset = offset });

							FaceMap faceMap = shape.GetSide((BlockSide)side);
							vertexCount += faceMap.vertices.Length;
							triangleCount += faceMap.triangles.Length;

							FaceMap colliderFaceMap = definition.colliderShape.GetSide((BlockSide)side);
							colliderVertexCount += colliderFaceMap.vertices.Length;
							colliderTriangleCount += colliderFaceMap.triangles.Length;
						}
					}
				}
			}

			ChunkSliceBuildEntry chunkEntry = new ChunkSliceBuildEntry();
			chunkEntry.ParentChunk = chunk;
			chunkEntry.SliceIndex = sliceIndex;
			chunkEntry.ChunkVersion = version;
			chunkEntry.Vertices = new TerrainVertex[vertexCount];
			chunkEntry.Triangles = new int[triangleCount];
			chunkEntry.ColliderVertices = new Vector3[colliderVertexCount];
			chunkEntry.ColliderTriangles = new int[colliderTriangleCount];

			int vertex = 0;
			int triangle = 0;
			int colliderVertex = 0;
			int colliderTriangle = 0;

			foreach (VisibleSide visible in visibleSides)
			{
				addBlockSide(chunkEntry, visible, ref vertex, ref triangle, ref colliderVertex, ref colliderTriangle);
			}

			return chunkEntry;
		}

		// Block type at slice local coords, which may fall into a neighbour slice or chunk
		byte GetNeighbour(Chunk chunk, int sliceIndex, int x, int y, int z)
		{
			if (y > Chunk.SliceHeightLimit)
			{
				if (sliceIndex + 1 > Chunk.MaxSliceIndex)
					return 0;
				return chunk.Slices[sliceIndex + 1][x & 0xF, y & Chunk.SliceHeightLimit, z & 0xF];
			}
			if (y < 0)
			{
				if (sliceIndex == 0)
					return 1; // nobody will ever see the bottom of the world
				return chunk.Slices[sliceIndex - 1][x & 0xF, y & Chunk.SliceHeightLimit, z & 0xF];
			}
			if (x < 0 || x > 15 || z < 0 || z > 15)
			{
				int worldX = (chunk.X << 4) + x;
				int worldZ = (chunk.Z << 4) + z;
				int worldY = (sliceIndex * ChunkSlice.SizeY) + y;

				return (byte)chunk.World.GetBlockType(worldX, worldY, worldZ);
			}
			return chunk.Slices[sliceIndex][x, y, z];
		}

		void addBlockSide (ChunkSliceBuildEntry entry, VisibleSide visible, ref int vertex, ref int triangle, ref int colliderVertex, ref int colliderTriangle) {
			Color32 color = sideColors[(int)visible.side];
			Vector2 uv = TerrainTextures.LayerToUV(visible.definition.GetTextureLayer(visible.side));

			FaceMap faceMap = visible.shape.GetSide(visible.side);
			Vector3[] sideVertices = faceMap.vertices;
			Vector3[] sideNormals = faceMap.normals;
			int[] sideTriangles = faceMap.triangles;
			TerrainVertex[] vertices = entry.Vertices;
			int[] triangles = entry.Triangles;
			int firstVertex = vertex;

			for (int i = 0; i < sideVertices.Length; i++, vertex++) {
				vertices[vertex].position = sideVertices[i] + visible.offset;
				vertices[vertex].normal = sideNormals[i];
				vertices[vertex].color = color;
				vertices[vertex].uv = uv;
			}
			for (int i = 0; i < sideTriangles.Length; i++, triangle++) {
				triangles[triangle] = sideTriangles[i] + firstVertex;
			}

			// the collider is usually way simpler than the rendered mesh, as it hasn't the studs
			FaceMap colliderFaceMap = visible.definition.colliderShape.GetSide(visible.side);
			Vector3[] colliderSideVertices = colliderFaceMap.vertices;
			int[] colliderSideTriangles = colliderFaceMap.triangles;
			firstVertex = colliderVertex;

			for (int i = 0; i < colliderSideVertices.Length; i++, colliderVertex++) {
				entry.ColliderVertices[colliderVertex] = colliderSideVertices[i] + visible.offset;
			}
			for (int i = 0; i < colliderSideTriangles.Length; i++, colliderTriangle++) {
				entry.ColliderTriangles[colliderTriangle] = colliderSideTriangles[i] + firstVertex;
			}
		}
	}
}
