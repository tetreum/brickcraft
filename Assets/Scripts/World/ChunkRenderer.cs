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
		private Dictionary<byte, List<Vector3>> meshes = new Dictionary<byte, List<Vector3>>();
		private Dictionary<Vector3, bool> added = new Dictionary<Vector3, bool>();

		// collider mesh
		private List<Vector3> colliderVertices = new List<Vector3>();
		private List<int> colliderTriangles = new List<int>();

		public void RenderChunk(Chunk chunk)
		{
			WorldBehaviour world = chunk.World;

			/*	Stopwatch watch = new Stopwatch();
				watch.Start();
			*/

			int minSliceIndex = chunk.MinSliceIndex;
			int lowestY = chunk.LowestY;
				
			Chunk frontChunk = world.GetChunk(chunk.X, chunk.Z - 1);
			Chunk backChunk = world.GetChunk(chunk.X, chunk.Z + 1);
			Chunk leftChunk = world.GetChunk(chunk.X - 1, chunk.Z);
			Chunk rightChunk = world.GetChunk(chunk.X + 1, chunk.Z);
			
			if(frontChunk != null && frontChunk.MinSliceIndex < minSliceIndex)
			{
				minSliceIndex = frontChunk.MinSliceIndex;
				lowestY = frontChunk.LowestY;
			}
			
			if(backChunk != null && backChunk.MinSliceIndex < minSliceIndex)
			{
				minSliceIndex = backChunk.MinSliceIndex;
				lowestY = backChunk.LowestY;
			}
			
			if(leftChunk != null && leftChunk.MinSliceIndex < minSliceIndex)
			{
				minSliceIndex = leftChunk.MinSliceIndex;
				lowestY = leftChunk.MinSliceIndex;
			}
			
			if(rightChunk != null && rightChunk.MinSliceIndex < minSliceIndex)
			{
				minSliceIndex = rightChunk.MinSliceIndex;
				lowestY = rightChunk.MinSliceIndex;
			}
			
			
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
				
				float epsilon = 0.00f;
				
				/*watch.Reset();
				watch.Start();*/
				
				int minHeight = chunk.MinSliceIndex == chunkSlice.Index ? (chunk.LowestY & Chunk.SliceHeightLimit) : 0;
				
				for (int x = 0; x < 16; x++)
				{
					for (int z = 0; z < 16; z++)
					{
						for (int y = Chunk.SliceHeight - 1; y >= 0 && y >= minHeight; --y)
						{					
							byte block = chunkSlice[x, y, z];
							int light = 0;
							byte top;
							
							if(block == 0)
								continue;
							
							if(y + 1 > Chunk.SliceHeightLimit)
							{
								if(i + 1 > Chunk.MaxSliceIndex)
									top = 0;
								else
								{
									ChunkSlice topSlice = chunk.Slices[i + 1];
									top = topSlice[x, (y + 1) & Chunk.SliceHeightLimit, z];
									light = topSlice.GetSkylight(x, (y + 1) & Chunk.SliceHeightLimit, z);
								}
							}
							else
							{
								top = chunkSlice[x, y + 1, z];
								light = chunkSlice.GetSkylight(x, y + 1, z);
							}
		
							// we are checking the top face of the block, so see if the top is exposed
							if (top == 0)
							{
								if (WorldBehaviour.mode == 2) {
									spawnBrick(block, new Vector3(x, y, z));
								} else if (WorldBehaviour.mode == 3) {
									addBrickFace("top", block, x, y, z);
								} else {
									int vertexIndex = vertices.Count;
									vertices.Add(new Vector3(x, y + 1, z));
									vertices.Add(new Vector3(x, y + 1, z + 1));
									vertices.Add(new Vector3(x + 1, y + 1, z + 1));
									vertices.Add(new Vector3(x + 1, y + 1, z));

									triangles.Add(vertexIndex);
									triangles.Add(vertexIndex+1);
									triangles.Add(vertexIndex+2);

									triangles.Add(vertexIndex+2);
									triangles.Add(vertexIndex+3);
									triangles.Add(vertexIndex);
								
							
									float attenuation = (light / 15.0f);
									Color withLight = new Color(topColor.r * attenuation, topColor.g * attenuation, topColor.b * attenuation, 1);
									colors.Add(withLight);
									colors.Add(withLight);
									colors.Add(withLight);
									colors.Add(withLight);
							
									Rect coords = BlockUVs.GetUVFromTypeAndFace((BlockType)block, BlockFace.Top);
								
									float yMax = coords.y + coords.height - epsilon;
									float xMax = coords.x + coords.width - epsilon;
									float xMin = coords.x + epsilon;
									float yMin = coords.y + epsilon;
								
									uvs.Add(new Vector2(xMin, yMax));
									uvs.Add(new Vector2(xMin, yMin));
									uvs.Add(new Vector2(xMax, yMin));							
									uvs.Add(new Vector2(xMax, yMax));
									
								}
							}
						
							int front;
							if(z - 1 < 0)
							{
								int worldX = (chunk.X << 4) + x;
								int worldZ = (chunk.Z << 4) - 1;
								int worldY = (chunkSlice.Index * ChunkSlice.SizeY) + y;
								
								front = (byte)world.GetBlockType(worldX, worldY, worldZ);
							}
							else
								front = chunkSlice[x, y, z - 1];
						
							if (front == 0)
							{
								if (WorldBehaviour.mode == 2) {
									spawnBrick(block, new Vector3(x, y, z));
								} else if (WorldBehaviour.mode == 3) {
									addBrickFace("front", block, x, y, z);
								} else {
									int vertexIndex = vertices.Count;
									vertices.Add(new Vector3(x, y, z));
									vertices.Add(new Vector3(x, y + 1, z));
									vertices.Add(new Vector3(x + 1, y + 1, z));
									vertices.Add(new Vector3(x + 1, y, z));
	
									triangles.Add(vertexIndex);
									triangles.Add(vertexIndex+1);
									triangles.Add(vertexIndex+2);

									triangles.Add(vertexIndex+2);
									triangles.Add(vertexIndex+3);
									triangles.Add(vertexIndex);
									
									colors.Add(firstSideColor);
									colors.Add(firstSideColor);
									colors.Add(firstSideColor);
									colors.Add(firstSideColor);
								
									Rect coords = BlockUVs.GetUVFromTypeAndFace((BlockType)block, BlockFace.Side);
								
									float yMax = coords.y + coords.height - epsilon;
									float xMax = coords.x + coords.width - epsilon;
									float xMin = coords.x + epsilon;
									float yMin = coords.y + epsilon;
								
									uvs.Add(new Vector2(xMin, yMin));
									uvs.Add(new Vector2(xMin, yMax));
									uvs.Add(new Vector2(xMax, yMax));							
									uvs.Add(new Vector2(xMax, yMin));
									
								}
							}
						
							int right;
							
							if(x + 1 > 15)
							{	
								int worldX = (chunk.X << 4) + 16;
								int worldZ = (chunk.Z << 4) + z;
								int worldY = (chunkSlice.Index * ChunkSlice.SizeY) + y;
								
								right = (byte)world.GetBlockType(worldX, worldY, worldZ);
							}
							else
								right = chunkSlice[x + 1, y, z];
		
							if (right == 0)
							{
								if (WorldBehaviour.mode == 2) {
									spawnBrick(block, new Vector3(x, y, z));
								} else if (WorldBehaviour.mode == 3) {
									addBrickFace("right", block, x, y, z);
								} else {
									int vertexIndex = vertices.Count;
									vertices.Add(new Vector3(x + 1, y, z));
									vertices.Add(new Vector3(x + 1, y + 1, z));
									vertices.Add(new Vector3(x + 1, y + 1, z + 1));
									vertices.Add(new Vector3(x + 1, y, z + 1));

									triangles.Add(vertexIndex);
									triangles.Add(vertexIndex+1);
									triangles.Add(vertexIndex+2);

									triangles.Add(vertexIndex+2);
									triangles.Add(vertexIndex+3);
									triangles.Add(vertexIndex);
							
									colors.Add(secondSideColor);
									colors.Add(secondSideColor);
									colors.Add(secondSideColor);
									colors.Add(secondSideColor);
								
									Rect coords = BlockUVs.GetUVFromTypeAndFace((BlockType)block, BlockFace.Side);
								
									float yMax = coords.y + coords.height - epsilon;
									float xMax = coords.x + coords.width - epsilon;
									float xMin = coords.x + epsilon;
									float yMin = coords.y + epsilon;
								
									uvs.Add(new Vector2(xMin, yMin));
									uvs.Add(new Vector2(xMin, yMax));
									uvs.Add(new Vector2(xMax, yMax));							
									uvs.Add(new Vector2(xMax, yMin));
									
								}
							}
						
							int back;
							
							if(z + 1 > 15)
							{
								int worldX = (chunk.X << 4) + x;
								int worldZ = (chunk.Z << 4) + 16;
								int worldY = (chunkSlice.Index * ChunkSlice.SizeY) + y;
								
								back = (byte)world.GetBlockType(worldX, worldY, worldZ);
							}
							else
								back = chunkSlice[x, y, z + 1];
						
							if (back == 0)
							{
								if (WorldBehaviour.mode == 2) {
									spawnBrick(block, new Vector3(x, y, z));
								} else if (WorldBehaviour.mode == 3) {
									addBrickFace("back", block, x, y, z);
								} else {
									int vertexIndex = vertices.Count;
									vertices.Add(new Vector3(x + 1, y, z + 1));
									vertices.Add(new Vector3(x + 1, y + 1, z + 1));
									vertices.Add(new Vector3(x, y + 1, z + 1));
									vertices.Add(new Vector3(x, y, z + 1));

									triangles.Add(vertexIndex);
									triangles.Add(vertexIndex+1);
									triangles.Add(vertexIndex+2);

									triangles.Add(vertexIndex+2);
									triangles.Add(vertexIndex+3);
									triangles.Add(vertexIndex);
							
									colors.Add(firstSideColor);
									colors.Add(firstSideColor);
									colors.Add(firstSideColor);
									colors.Add(firstSideColor);
								
									Rect coords = BlockUVs.GetUVFromTypeAndFace((BlockType)block, BlockFace.Side);
								
									float yMax = coords.y + coords.height - epsilon;
									float xMax = coords.x + coords.width - epsilon;
									float xMin = coords.x + epsilon;
									float yMin = coords.y + epsilon;
								
									uvs.Add(new Vector2(xMin, yMin));
									uvs.Add(new Vector2(xMin, yMax));
									uvs.Add(new Vector2(xMax, yMax));							
									uvs.Add(new Vector2(xMax, yMin));
									
								}
							}
						
							int left;
							
							if(x - 1 < 0)
							{
								int worldX = (chunk.X << 4) - 1;
								int worldZ = (chunk.Z << 4) + z;
								int worldY = (chunkSlice.Index * ChunkSlice.SizeY) + y;
								
								left = (byte)world.GetBlockType(worldX, worldY, worldZ);
							}
							else
								left = chunkSlice[x - 1, y, z];
						
							if (left == 0)
							{
								if (WorldBehaviour.mode == 2) {
									spawnBrick(block, new Vector3(x, y, z));
								} else if (WorldBehaviour.mode == 3) {
									addBrickFace("left", block, x, y, z);
								} else {
									int vertexIndex = vertices.Count;
									vertices.Add(new Vector3(x, y, z + 1));
									vertices.Add(new Vector3(x, y + 1, z + 1));
									vertices.Add(new Vector3(x, y + 1, z));
									vertices.Add(new Vector3(x, y, z));

									triangles.Add(vertexIndex);
									triangles.Add(vertexIndex+1);
									triangles.Add(vertexIndex+2);

									triangles.Add(vertexIndex+2);
									triangles.Add(vertexIndex+3);
									triangles.Add(vertexIndex);
							
									colors.Add(secondSideColor);
									colors.Add(secondSideColor);
									colors.Add(secondSideColor);
									colors.Add(secondSideColor);
								
									Rect coords = BlockUVs.GetUVFromTypeAndFace((BlockType)block, BlockFace.Side);
								
									float yMax = coords.y + coords.height - epsilon;
									float xMax = coords.x + coords.width - epsilon;
									float xMin = coords.x + epsilon;
									float yMin = coords.y + epsilon;
								
									uvs.Add(new Vector2(xMin, yMin));
									uvs.Add(new Vector2(xMin, yMax));
									uvs.Add(new Vector2(xMax, yMax));							
									uvs.Add(new Vector2(xMax, yMin));
									
								}
							}
						
							byte bottom;
							
							if(y - 1 < 0)
								bottom = 1;
							else
								bottom = chunkSlice[x, y - 1, z];
						
							if (bottom == 0)
							{
								if (WorldBehaviour.mode == 2) {
									spawnBrick(block, new Vector3(x, y, z));
								} else if (WorldBehaviour.mode == 3) {
									addBrickFace("bottom", block, x, y, z);
								} else {
									int vertexIndex = vertices.Count;
									vertices.Add(new Vector3(x, y, z + 1));
									vertices.Add(new Vector3(x, y, z));
									vertices.Add(new Vector3(x + 1, y, z));
									vertices.Add(new Vector3(x + 1, y, z + 1));

									triangles.Add(vertexIndex);
									triangles.Add(vertexIndex+1);
									triangles.Add(vertexIndex+2);

									triangles.Add(vertexIndex+2);
									triangles.Add(vertexIndex+3);
									triangles.Add(vertexIndex);
							
									colors.Add(bottomColor);
									colors.Add(bottomColor);
									colors.Add(bottomColor);
									colors.Add(bottomColor);
								
									Rect coords = BlockUVs.GetUVFromTypeAndFace((BlockType)block, BlockFace.Bottom);
									float yMax = coords.y + coords.height - epsilon;
									float xMax = coords.x + coords.width - epsilon;
									float xMin = coords.x + epsilon;
									float yMin = coords.y + epsilon;
								
									uvs.Add(new Vector2(xMin, yMin));
									uvs.Add(new Vector2(xMin, yMax));
									uvs.Add(new Vector2(xMax, yMax));
									uvs.Add(new Vector2(xMax, yMin));
									
								}
							}
						}
					}
				}
				/*watch.Stop();
				long elapsedPreMesh = watch.ElapsedMilliseconds;
				watch.Start();*/


				ChunkSliceBuildEntry chunkEntry = new ChunkSliceBuildEntry();
				chunkEntry.Vertices = vertices.ToArray();
				chunkEntry.Triangles = triangles.ToArray();
				chunkEntry.Colors = colors.ToArray();
				chunkEntry.Uvs = uvs.ToArray();
				chunkEntry.ParentChunk = chunk;
				chunkEntry.SliceIndex = i;

				chunkEntry.ColliderVertices = colliderVertices.ToArray();
				chunkEntry.ColliderTriangles = colliderTriangles.ToArray();

				if (WorldBehaviour.mode == 2) {
					Dictionary<byte, Vector3[]> meshList = new Dictionary<byte, Vector3[]>();
					foreach (byte block in meshes.Keys) {
						meshList.Add(block, meshes[block].ToArray());
					}

					chunkEntry.meshes = meshList;
				}

				vertices.Clear();
				triangles.Clear();
				colors.Clear();
				uvs.Clear();
				meshes.Clear();
				added.Clear();

				colliderVertices.Clear();
				colliderTriangles.Clear();

				lock (WorldBehaviour.ChunkQueueLock)
					WorldBehaviour.ChunkSlicesToBuild.Enqueue(chunkEntry);
				
				//watch.Stop();
				//elapsedPostSet = watch.ElapsedMilliseconds;
				
				/*UnityEngine.Debug.Log("Elapsed Mesh Prepare " + elapsedPreMesh + "ms");
				UnityEngine.Debug.Log("Elapsed Mesh Build " + (elapsedPostMesh - elapsedPreMesh) + "ms");
				UnityEngine.Debug.Log("Elapsed Mesh Set " + (elapsedPostSet - elapsedPostMesh) + "ms");
				UnityEngine.Debug.Log("Total Elapsed " + (elapsedPostSet));*/
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
			//watch.Stop();
			//UnityEngine.Debug.Log ("Total Elapsed " + ((double)watch.ElapsedTicks / (double)Stopwatch.Frequency) + "ms");
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

				if (face == "top" || face == "bottom") {
					uvs.Add(new Vector2(xt.x, zt.x));
				} else {
					uvs.Add(new Vector2(xt.y, zt.y));
				}
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

		void spawnBrick (byte block, Vector3 pos) {
			
			if (added.ContainsKey(pos)) {
				return;
			}
			if (!meshes.ContainsKey(block)) {
				meshes.Add(block, new List<Vector3>());
			}

			meshes[block].Add(pos);
		}
	}
}
