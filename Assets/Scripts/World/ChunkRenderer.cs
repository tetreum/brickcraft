using Brickcraft.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace Brickcraft.World
{
	public class ChunkRenderer
	{
		private static Color firstSideColor = new Color(0.9f, 0.9f, 0.9f, 1.0f);
		private static Color secondSideColor = new Color(0.8f, 0.8f, 0.8f, 1.0f);
		private static Color topColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		private static Color bottomColor = new Color(0.7f, 0.7f, 0.7f, 1.0f);

		private List<int> indexesToDelete = new List<int>();
		private List<Vector3> vertices = new List<Vector3>();
		private	List<int> triangles = new List<int>();
		private	List<Color> colors = new List<Color>();
		private	List<Vector2> uvs = new List<Vector2>();

		// collider mesh
		private List<Vector3> colliderVertices = new List<Vector3>();
		private List<int> colliderTriangles = new List<int>();

		public void RenderChunk(Chunk chunk)
		{
			WorldBehaviour world = chunk.World;

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

				ChunkSliceBuildEntry chunkEntry = RenderSlice(chunk, i, minHeight);

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
		public ChunkSliceBuildEntry RenderSlice(Chunk chunk, int sliceIndex, int minHeight = 0)
		{
			ChunkSlice chunkSlice = chunk.Slices[sliceIndex];

			for (int x = 0; x < 16; x++)
			{
				for (int z = 0; z < 16; z++)
				{
					for (int y = Chunk.SliceHeight - 1; y >= 0 && y >= minHeight; --y)
					{
						byte block = chunkSlice[x, y, z];

						if(block == 0)
							continue;

						// only faces that are exposed to air are rendered
						if (GetNeighbour(chunk, sliceIndex, x, y + 1, z) == 0)
							addBrickFace("top", block, x, y, z);

						if (GetNeighbour(chunk, sliceIndex, x, y, z - 1) == 0)
							addBrickFace("front", block, x, y, z);

						if (GetNeighbour(chunk, sliceIndex, x + 1, y, z) == 0)
							addBrickFace("right", block, x, y, z);

						if (GetNeighbour(chunk, sliceIndex, x, y, z + 1) == 0)
							addBrickFace("back", block, x, y, z);

						if (GetNeighbour(chunk, sliceIndex, x - 1, y, z) == 0)
							addBrickFace("left", block, x, y, z);

						if (GetNeighbour(chunk, sliceIndex, x, y - 1, z) == 0)
							addBrickFace("bottom", block, x, y, z);
					}
				}
			}

			ChunkSliceBuildEntry chunkEntry = new ChunkSliceBuildEntry();
			chunkEntry.Vertices = vertices.ToArray();
			chunkEntry.Triangles = triangles.ToArray();
			chunkEntry.Colors = colors.ToArray();
			chunkEntry.Uvs = uvs.ToArray();
			chunkEntry.ParentChunk = chunk;
			chunkEntry.SliceIndex = sliceIndex;

			chunkEntry.ColliderVertices = colliderVertices.ToArray();
			chunkEntry.ColliderTriangles = colliderTriangles.ToArray();

			vertices.Clear();
			triangles.Clear();
			colors.Clear();
			uvs.Clear();

			colliderVertices.Clear();
			colliderTriangles.Clear();

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

		void addBrickFace (string face, byte block, int x, int y, int z) {
			int vertexIndex = vertices.Count;
			FaceMap faceMap = WorldBehaviour.meshMap[face];
			Color color;

			switch (face) {
				case "top":
					color = topColor;
					break;
				case "bottom":
					color = bottomColor;
					break;
				case "front":
				case "back":
					color = firstSideColor;
					break;
				case "left":
				case "right":
					color = secondSideColor;
					break;
				default:
					throw new System.Exception("wrong face name: " + face);
			}

			// temporal | testing
			Vector2 xt = new Vector2(0, 0);
			Vector2 zt = new Vector2(0, 0);

			foreach (Vector3 vertice in faceMap.vertices) {
				if (vertice.x > xt.x) {
					xt.x = vertice.x;
				} else if (vertice.x < xt.y) {
					xt.y = vertice.x;
				}
				if (vertice.z > zt.x) {
					zt.x = vertice.z;
				} else if (vertice.x < zt.y) {
					zt.y = vertice.z;
				}
			}

			foreach (Vector3 vertice in faceMap.vertices) {
				Vector3 pos = new Vector3(
					vertice.x + (x * Server.brickWidth),
					vertice.y + (y * Server.brickHeight),
					vertice.z + (z * Server.brickWidth)
				);
				vertices.Add(pos);
				colors.Add(color);

				// temporal | testing | dunno what im doing here
				if (face == "top" || face == "bottom") {
					Rect coords = BlockUVs.GetUVFromTypeAndFace((BlockType)block, face == "top" ? BlockFace.Top : BlockFace.Bottom);

					float yMax = (coords.y + coords.height) - 0.125f;
					float xMax = (coords.x + coords.width) - 0.125f;
					float xMin = coords.x + 0.125f;
					float yMin = coords.y + 0.125f;

					uvs.Add(new Vector2(vertice.x < xt.x ? xMin : xMax, vertice.z < zt.x ? yMin : yMax));
				}
			}
			if (face != "top" && face != "bottom") {
				Rect coords = BlockUVs.GetUVFromTypeAndFace((BlockType)block, BlockFace.Side);

				float yMax = (coords.y + coords.height) - 0.125f;
				float xMax = (coords.x + coords.width) - 0.125f;
				float xMin = coords.x + 0.125f;
				float yMin = coords.y + 0.125f;

				uvs.Add(new Vector2(xMax, yMax));
				uvs.Add(new Vector2(xMin, yMax));
				uvs.Add(new Vector2(xMin, yMin));
				uvs.Add(new Vector2(xMax, yMin));
			}
			foreach (int index in faceMap.triangles) {
				triangles.Add(index + vertexIndex);
			}

			// Mesh collider, it is way more simple than the normal mesh, as it hasn't the studs
			vertexIndex = colliderVertices.Count;
			FaceMap colliderFaceMap = WorldBehaviour.colliderMeshMap[face];
			foreach (Vector3 vertice in colliderFaceMap.vertices) {
				Vector3 pos = new Vector3(
					vertice.x + (x * Server.brickWidth),
					vertice.y + (y * Server.brickHeight),
					vertice.z + (z * Server.brickWidth)
				);
				colliderVertices.Add(pos);
			}
			foreach (int index in colliderFaceMap.triangles) {
				colliderTriangles.Add(index + vertexIndex);
			}
		}
	}
}
