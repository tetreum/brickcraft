using UnityEngine;
using System.Collections.Generic;
using System.Threading;
using Brickcraft.Utils;
using Brickcraft.Bricks;

// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	public class WorldBehaviour : MonoBehaviour {
		public static WorldBehaviour Instance;
		public static Chunk[] ChunksMap = new Chunk[ushort.MaxValue];

		public static Queue<ChunkSliceBuildEntry> ChunkSlicesToBuild = new Queue<ChunkSliceBuildEntry>();
		private static Queue<ChunkSliceBuildEntry> ChunkSlicesWorkingQueue = new Queue<ChunkSliceBuildEntry>();

		public static Queue<ChunkSlicesDeleteEntry> SlicesToDelete = new Queue<ChunkSlicesDeleteEntry>();
		private static Queue<ChunkSlicesDeleteEntry> SlicesToDeleteWorking = new Queue<ChunkSlicesDeleteEntry>();

		public static object ChunkQueueLock = new object();
		public static object SliceLock = new object();

		// chunks still being rendered by the thread pool
		public static int PendingChunkRenders;

		public static Material BlockMaterial;

		public Transform obj;
		public Transform brickColliderObj;

		private int accumulator;

        private int ChunksNum = 26 * 26;

        public static readonly int MapMinChunkX = -13;
        public static readonly int MapMaxChunkX = 12;

        public static readonly int MapMinCoords = MapMinChunkX * 16;
		public static readonly int MapMaxCoords = MapMaxChunkX * 16;

		public static Dictionary<string, FaceMap> meshMap;
		public static Dictionary<string, FaceMap> colliderMeshMap;

		// used on the main thread to rebuild slices after a block changes
		private ChunkRenderer sliceRenderer = new ChunkRenderer();

		/// <summary>True once the whole world has been generated and its meshes built.</summary>
		public bool IsReady { get; private set; }

		private void Awake() {
			Instance = this;
			meshMap = (new FaceMapper(obj)).getMapping();
			colliderMeshMap = (new FaceMapper(brickColliderObj)).getMapping();

			BrickGrid.AlignToWorldBlockMesh(obj.GetComponent<MeshFilter>().sharedMesh.bounds);
		}

		void Start () {
			Menu.Instance.showPanel("LoadingPanel");

			BlockMaterial = (Material)Resources.Load ("Materials/Block_Triplanar", typeof(Material));

			ChunkMeshThreadEntry[] chunkEntries = new ChunkMeshThreadEntry[ChunksNum];

			ChunkGenManager chunkGenManager = new ChunkGenManager(MapMinChunkX, MapMaxChunkX + 1, 6, this, 13284938921, chunkEntries);

			chunkGenManager.Generate();

			PendingChunkRenders = ChunksNum;

			for(int x = 0; x < ChunksNum; ++x)
			{
				chunkEntries[x].Init();
				ThreadPool.QueueUserWorkItem(new WaitCallback(chunkEntries[x].ThreadCallback));
			}
		}

		public static ushort ChunkIndexFromCoords(int x, int z)
		{
			return (ushort)((x + 127) << 8 | (z + 127));
		}

		void Update () {

			foreach (Chunk chunk in ChunksMap) {
				if (chunk == null) {
					continue;
				}
				foreach (ChunkSlice slice in chunk.Slices) {
					if (slice.IsEmpty || slice.renderer == null) {
						continue;
					}
					slice.FrustrumCulling();
				}
			}
		}

		void FixedUpdate()
		{
			++accumulator;

			if(accumulator == 2) // Each 100ms (1 FixedStep is 50ms)
			{
				lock(ChunkQueueLock)
				{
					Queue<ChunkSliceBuildEntry> temp = ChunkSlicesWorkingQueue;
					ChunkSlicesWorkingQueue = ChunkSlicesToBuild;
					ChunkSlicesToBuild = temp;
				}

				for(int i = 0; ChunkSlicesWorkingQueue.Count != 0 && i < 40; ++i)
				{
					ChunkSliceBuildEntry chunkEntry = ChunkSlicesWorkingQueue.Dequeue();
					BuildChunkSliceMesh(chunkEntry);
				}

				if (!IsReady && ChunkSlicesWorkingQueue.Count == 0 && Volatile.Read(ref PendingChunkRenders) == 0) {
					bool queueEmpty;
					lock (ChunkQueueLock) {
						queueEmpty = ChunkSlicesToBuild.Count == 0;
					}
					if (queueEmpty) {
						IsReady = true;
						Server.Instance.spawnPlayer(new Vector3(0, 160, 0), Quaternion.identity);
						Menu.Instance.showPanel("PlayerPanel");
					}
				}

				lock(SliceLock)
				{
					Queue<ChunkSlicesDeleteEntry> temp = SlicesToDeleteWorking;
					SlicesToDeleteWorking = SlicesToDelete;
					SlicesToDelete = temp;
				}

				SlicesToDeleteWorking.Clear();

				accumulator = 0;
			}
		}

		// muertet
		public void BuildChunkSliceMesh(ChunkSliceBuildEntry chunkEntry)
		{
			GameObject chunkSliceObject = chunkEntry.ParentChunk.ChunkSliceObjects[chunkEntry.SliceIndex];
			MeshFilter filter = chunkSliceObject.GetComponent<MeshFilter>();
			MeshCollider meshCollider = chunkSliceObject.GetComponent<MeshCollider>();

			// slices are rebuilt when their blocks change, don't leak the old meshes
			if (filter.sharedMesh != null) {
				Destroy(filter.sharedMesh);
			}
			if (meshCollider.sharedMesh != null) {
				Destroy(meshCollider.sharedMesh);
			}

			// Build the Mesh:
			Mesh mesh = new Mesh();

			if (chunkEntry.Vertices.Length > 65535) {
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			}

			mesh.vertices = chunkEntry.Vertices;
			mesh.triangles = chunkEntry.Triangles;
			mesh.uv = chunkEntry.Uvs;
			mesh.colors = chunkEntry.Colors;

			mesh.RecalculateNormals();
			mesh.RecalculateBounds();

			filter.sharedMesh = mesh;

			// generate a much simpler collider mesh
			if (chunkEntry.ColliderTriangles.Length > 0) {
				mesh = new Mesh();

				if (chunkEntry.ColliderVertices.Length > 65535) {
					mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				}
				mesh.vertices = chunkEntry.ColliderVertices;
				mesh.triangles = chunkEntry.ColliderTriangles;

				mesh.RecalculateNormals();
				mesh.RecalculateBounds();

				meshCollider.sharedMesh = mesh;
			} else {
				meshCollider.sharedMesh = null;
			}
			chunkEntry.ParentChunk.ClearDirtySlices();
		}

		public BlockType GetBlockType(int x, int y, int z)
		{
			if (y < 0 || y >= Chunk.NumSlices * Chunk.SliceHeight)
				return BlockType.NULL;

			int chunkX = x >> 4;
			int chunkZ = z >> 4;

			Chunk chunk = GetChunk(chunkX, chunkZ);

			if(chunk == null)
				return BlockType.NULL; // We return NULL so that is different from air and we don't build side faces of the blocks

			return chunk.GetBlockType(x & 0xF, y, z&0xF);
		}

		public BlockType GetBlockType(Vector3Int block)
		{
			return GetBlockType(block.x, block.y, block.z);
		}

		/// <summary>
		/// Changes a block of the generated world and rebuilds the affected slice meshes.
		/// </summary>
		public bool SetBlockType(Vector3Int block, BlockType type)
		{
			if (!IsReady || block.y < 0 || block.y >= Chunk.NumSlices * Chunk.SliceHeight)
				return false;

			Chunk chunk = GetChunk(block.x >> 4, block.z >> 4);

			if (chunk == null)
				return false;

			int x = block.x & 0xF;
			int z = block.z & 0xF;
			int sliceIndex = block.y / Chunk.SliceHeight;
			int y = block.y & Chunk.SliceHeightLimit;

			chunk.SetType(x, block.y, z, type, false);
			chunk.RecalculateHeight(x, z);

			RebuildSlice(chunk, sliceIndex);

			// faces of the neighbour blocks may have become visible (or hidden)
			if (y == 0)
				RebuildSlice(chunk, sliceIndex - 1);
			if (y == Chunk.SliceHeightLimit)
				RebuildSlice(chunk, sliceIndex + 1);
			if (x == 0)
				RebuildSlice(GetChunk(chunk.X - 1, chunk.Z), sliceIndex);
			if (x == 15)
				RebuildSlice(GetChunk(chunk.X + 1, chunk.Z), sliceIndex);
			if (z == 0)
				RebuildSlice(GetChunk(chunk.X, chunk.Z - 1), sliceIndex);
			if (z == 15)
				RebuildSlice(GetChunk(chunk.X, chunk.Z + 1), sliceIndex);

			return true;
		}

		private void RebuildSlice(Chunk chunk, int sliceIndex)
		{
			if (chunk == null || sliceIndex < 0 || sliceIndex > Chunk.MaxSliceIndex)
				return;

			if (chunk.Slices[sliceIndex].IsEmpty && chunk.ChunkSliceObjects[sliceIndex] == null)
				return;

			chunk.GetOrCreateSliceObject(sliceIndex);
			BuildChunkSliceMesh(sliceRenderer.RenderSlice(chunk, sliceIndex));
		}

		public Chunk GetChunk(int x, int z)
		{
			return ChunksMap[ChunkIndexFromCoords(x, z)];
		}
	}
}
