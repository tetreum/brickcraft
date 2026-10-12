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
			public bool translucent;
			// texture layer of the block's colour, -1 to use the block's own textures
			public int colorLayer;
			// drawn merged with its neighbours' (see mergeFaces): only its collider is added on its own
			public bool merged;
		}

		// a side of a box block in a far chunk, to be merged with the ones beside it
		private struct MergeableSide
		{
			public int u, v; // where on its plane, in blocks
			public int code; // what it looks like: its texture layer and translucency, see codeOf
		}

		// sides of blocks merged into one rectangle
		private struct Quad
		{
			public BlockSide side;
			public Vector3 a, b, c, d;
			public int layer;
			public bool translucent;
		}

		private List<VisibleSide> visibleSides = new List<VisibleSide>();
		// by side and plane (side * 16 + its coordinate across the side)
		private readonly List<MergeableSide>[] mergeable = new List<MergeableSide>[BlockShape.SideCount * 16];
		private readonly List<Quad> quads = new List<Quad>();
		private readonly int[] mask = new int[16 * 16];

		/// <summary>Meshes the chunk's slices on a thread: with studs if detailed, and colliders if asked (see Chunk.WithColliders).</summary>
		public void RenderChunk(Chunk chunk, bool detailed, bool colliders)
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

				// nothing below the lowest surface around can be seen
				if(i < minSliceIndex)
					break;

				if(chunkSlice.IsEmpty)
					continue;

				int minHeight = chunk.MinSliceIndex == chunkSlice.Index ? (chunk.LowestY & Chunk.SliceHeightLimit) : 0;

				ChunkSliceBuildEntry chunkEntry = RenderSlice(chunk, i, detailed, colliders, version, minHeight);
				chunkEntry.CountsAsPending = true;
				Interlocked.Increment(ref chunk.PendingSliceUploads);

				lock (WorldBehaviour.ChunkQueueLock)
					WorldBehaviour.ChunkSlicesToBuild.Enqueue(chunkEntry);
			}
		}

		/// <summary>
		/// Builds the mesh data of a single slice. Used both by the initial world rendering
		/// and to refresh a slice after one of its blocks changed.
		/// </summary>
		public ChunkSliceBuildEntry RenderSlice(Chunk chunk, int sliceIndex, bool detailed, bool colliders, int version, int minHeight = 0)
		{
			ChunkSlice chunkSlice = chunk.Slices[sliceIndex];
			int vertexCount = 0;
			int triangleCount = 0;
			int translucentTriangleCount = 0;
			int colliderVertexCount = 0;
			int colliderTriangleCount = 0;

			// first find what has to be drawn, so the mesh arrays can be allocated at their final size
			visibleSides.Clear();
			quads.Clear();
			for (int i = 0; i < mergeable.Length; i++) {
				if (mergeable[i] == null)
					mergeable[i] = new List<MergeableSide>();
				mergeable[i].Clear();
			}

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
						int color = chunk.GetColor(x, sliceIndex * Chunk.SliceHeight + y, z);
						int colorLayer = color == Bricks.BrickColor.None ? -1 : BlockDatabase.ColorLayer(color);
						bool translucent = definition.isTranslucent || (colorLayer >= 0 && BlockDatabase.IsTransparentColor(color));
						Vector3 offset = new Vector3(x * Server.brickWidth, y * Server.brickHeight, z * Server.brickWidth);

						// only sides that can be seen are rendered: the ones touching air, and for
						// opaque blocks also the ones seen through translucent blocks (under water)
						for (int side = 0; side < BlockShape.SideCount; side++)
						{
							Vector3Int direction = BlockShape.SideDirections[side];
							byte neighbour = GetNeighbour(chunk, sliceIndex, x + direction.x, y + direction.y, z + direction.z);

							if (neighbour != 0 && (translucent || !isTranslucent(chunk, sliceIndex, x + direction.x, y + direction.y, z + direction.z, neighbour)))
								continue;

							BlockShape shape = detailed ? definition.shape : definition.colliderShape;
							// far away, flat sides of boxes are merged with the ones beside them that look the same
							bool merged = !detailed && shape.IsBox;
							visibleSides.Add(new VisibleSide() { shape = shape, definition = definition, side = (BlockSide)side, offset = offset, translucent = translucent, colorLayer = colorLayer, merged = merged });

							if (merged) {
								int layer = colorLayer >= 0 ? colorLayer : definition.GetTextureLayer((BlockSide)side);
								addMergeable((BlockSide)side, x, y, z, codeOf(layer, translucent));
							} else {
								FaceMap faceMap = shape.GetSide((BlockSide)side);
								vertexCount += faceMap.vertices.Length;
								triangleCount += faceMap.triangles.Length;
								if (translucent)
									translucentTriangleCount += faceMap.triangles.Length;
							}

							// liquids are drawn but don't collide: things go into them (see Water)
							if (!colliders || definition.isFluid)
								continue;
							FaceMap colliderFaceMap = definition.colliderShape.GetSide((BlockSide)side);
							colliderVertexCount += colliderFaceMap.vertices.Length;
							colliderTriangleCount += colliderFaceMap.triangles.Length;
						}
					}
				}
			}

			mergeFaces();
			foreach (Quad quad in quads) {
				vertexCount += 4;
				triangleCount += 6;
				if (quad.translucent)
					translucentTriangleCount += 6;
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
			int colliderVertex = 0;
			int colliderTriangle = 0;

			// opaque sides first and translucent ones after, they're drawn with different materials
			chunkEntry.TranslucentStart = triangleCount - translucentTriangleCount;
			int opaqueTriangle = 0;
			int translucentTriangle = chunkEntry.TranslucentStart;

			foreach (VisibleSide visible in visibleSides)
			{
				if (visible.translucent)
					addBlockSide(chunkEntry, visible, colliders, ref vertex, ref translucentTriangle, ref colliderVertex, ref colliderTriangle);
				else
					addBlockSide(chunkEntry, visible, colliders, ref vertex, ref opaqueTriangle, ref colliderVertex, ref colliderTriangle);
			}
			foreach (Quad quad in quads)
			{
				if (quad.translucent)
					addQuad(chunkEntry, quad, ref vertex, ref translucentTriangle);
				else
					addQuad(chunkEntry, quad, ref vertex, ref opaqueTriangle);
			}

			return chunkEntry;
		}

		// -------- merged sides (far chunks) --------

		private static int codeOf(int layer, bool translucent)
		{
			return 1 + layer * 2 + (translucent ? 1 : 0); // 0: none
		}

		// on the side's plane: u and v along it, plane across it
		private void addMergeable(BlockSide side, int x, int y, int z, int code)
		{
			int plane, u, v;
			switch (side) {
				case BlockSide.Top: case BlockSide.Bottom: plane = y; u = x; v = z; break;
				case BlockSide.Front: case BlockSide.Back: plane = z; u = x; v = y; break;
				default: plane = x; u = z; v = y; break;
			}
			mergeable[(int)side * 16 + plane].Add(new MergeableSide() { u = u, v = v, code = code });
		}

		// greedy meshing: on each plane, the biggest rectangles of sides that look the same
		private void mergeFaces()
		{
			for (int side = 0; side < BlockShape.SideCount; side++) {
				for (int plane = 0; plane < 16; plane++) {
					List<MergeableSide> sides = mergeable[side * 16 + plane];
					if (sides.Count == 0)
						continue;
					System.Array.Clear(mask, 0, mask.Length);
					foreach (MergeableSide s in sides)
						mask[s.v * 16 + s.u] = s.code;

					for (int v = 0; v < 16; v++) {
						for (int u = 0; u < 16; ) {
							int code = mask[v * 16 + u];
							if (code == 0) {
								u++;
								continue;
							}
							int width = 1;
							while (u + width < 16 && mask[v * 16 + u + width] == code)
								width++;
							int height = 1;
							while (v + height < 16 && sameRow(v + height, u, width, code))
								height++;
							for (int dv = 0; dv < height; dv++)
								for (int du = 0; du < width; du++)
									mask[(v + dv) * 16 + u + du] = 0;
							quads.Add(quadOf((BlockSide)side, plane, u, v, width, height, code));
							u += width;
						}
					}
				}
			}
		}

		private bool sameRow(int v, int u, int width, int code)
		{
			for (int du = 0; du < width; du++) {
				if (mask[v * 16 + u + du] != code)
					return false;
			}
			return true;
		}

		private static Quad quadOf(BlockSide side, int plane, int u, int v, int width, int height, int code)
		{
			Quad quad = new Quad() {
				side = side,
				a = cornerOf(side, plane, u, v),
				b = cornerOf(side, plane, u + width, v),
				c = cornerOf(side, plane, u + width, v + height),
				d = cornerOf(side, plane, u, v + height),
				layer = (code - 1) / 2,
				translucent = ((code - 1) & 1) == 1,
			};
			// clockwise seen from where it faces
			if (Vector3.Dot(Vector3.Cross(quad.b - quad.a, quad.c - quad.a), BlockShape.SideDirections[(int)side]) < 0) {
				Vector3 swap = quad.b;
				quad.b = quad.d;
				quad.d = swap;
			}
			return quad;
		}

		// a corner of the blocks' grid on a side's plane (blocks are centred on x and z, on their bottom)
		private static Vector3 cornerOf(BlockSide side, int plane, int u, int v)
		{
			float width = Server.brickWidth, height = Server.brickHeight, half = width / 2;
			switch (side) {
				case BlockSide.Top: return new Vector3(u * width - half, (plane + 1) * height, v * width - half);
				case BlockSide.Bottom: return new Vector3(u * width - half, plane * height, v * width - half);
				case BlockSide.Front: return new Vector3(u * width - half, v * height, plane * width - half);
				case BlockSide.Back: return new Vector3(u * width - half, v * height, plane * width + half);
				case BlockSide.Left: return new Vector3(plane * width - half, v * height, u * width - half);
				default: return new Vector3(plane * width + half, v * height, u * width - half);
			}
		}

		void addQuad(ChunkSliceBuildEntry entry, Quad quad, ref int vertex, ref int triangle)
		{
			Color32 color = sideColors[(int)quad.side];
			Vector2 uv = TerrainTextures.LayerToUV(quad.layer);
			Vector3 normal = BlockShape.SideDirections[(int)quad.side];
			TerrainVertex[] vertices = entry.Vertices;
			int first = vertex;
			Vector3[] corners = { quad.a, quad.b, quad.c, quad.d };
			foreach (Vector3 corner in corners) {
				vertices[vertex].position = corner;
				vertices[vertex].normal = normal;
				vertices[vertex].color = color;
				vertices[vertex].uv = uv;
				vertex++;
			}
			int[] triangles = entry.Triangles;
			triangles[triangle++] = first;
			triangles[triangle++] = first + 1;
			triangles[triangle++] = first + 2;
			triangles[triangle++] = first;
			triangles[triangle++] = first + 2;
			triangles[triangle++] = first + 3;
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

		// drawn see-through: by its kind (water) or its colour (trans bricks)
		bool isTranslucent(Chunk chunk, int sliceIndex, int x, int y, int z, byte block)
		{
			if (BlockDatabase.Get(block).isTranslucent)
				return true;

			int worldY = sliceIndex * Chunk.SliceHeight + y;
			if (worldY < 0 || worldY >= Chunk.NumSlices * Chunk.SliceHeight)
				return false;

			int color = x < 0 || x > 15 || z < 0 || z > 15
				? chunk.World.GetBlockColor((chunk.X << 4) + x, worldY, (chunk.Z << 4) + z)
				: chunk.GetColor(x, worldY, z);

			return color != Bricks.BrickColor.None && BlockDatabase.IsTransparentColor(color);
		}

		void addBlockSide (ChunkSliceBuildEntry entry, VisibleSide visible, bool colliders, ref int vertex, ref int triangle, ref int colliderVertex, ref int colliderTriangle) {
			if (!visible.merged)
				addRenderedSide(entry, visible, ref vertex, ref triangle);

			// the collider is usually way simpler than the rendered mesh, as it hasn't the studs; liquids have none
			if (!colliders || visible.definition.isFluid) {
				return;
			}
			FaceMap colliderFaceMap = visible.definition.colliderShape.GetSide(visible.side);
			Vector3[] colliderSideVertices = colliderFaceMap.vertices;
			int[] colliderSideTriangles = colliderFaceMap.triangles;
			int firstVertex = colliderVertex;

			for (int i = 0; i < colliderSideVertices.Length; i++, colliderVertex++) {
				entry.ColliderVertices[colliderVertex] = colliderSideVertices[i] + visible.offset;
			}
			for (int i = 0; i < colliderSideTriangles.Length; i++, colliderTriangle++) {
				entry.ColliderTriangles[colliderTriangle] = colliderSideTriangles[i] + firstVertex;
			}
		}

		void addRenderedSide (ChunkSliceBuildEntry entry, VisibleSide visible, ref int vertex, ref int triangle) {
			Color32 color = sideColors[(int)visible.side];
			Vector2 uv = TerrainTextures.LayerToUV(visible.colorLayer >= 0 ? visible.colorLayer : visible.definition.GetTextureLayer(visible.side));

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
		}
	}
}
