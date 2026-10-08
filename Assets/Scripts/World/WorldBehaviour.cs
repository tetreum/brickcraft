using UnityEngine;
using System.Collections.Generic;
using System.Threading;
using System.Collections;
using UnityEngine.Rendering;
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

		[Tooltip("Chunks this far from the camera (in chunks) are drawn with studs, the rest with simple boxes")]
		public int DetailRadius = 4;

		// chunk the level of detail is centered on, the spawn until there's a camera
		private Vector2Int detailCenter = Vector2Int.zero;

		private const float UploadBudgetMs = 4f;
		private const float LoadingUploadBudgetMs = 40f;
		private readonly System.Diagnostics.Stopwatch uploadClock = new System.Diagnostics.Stopwatch();

		private const MeshUpdateFlags FastMeshUpdate = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;

		// local bounds of any slice mesh, with a block of margin for models bigger than a block
		private static readonly Bounds SliceBounds = new Bounds(
			new Vector3(7.5f * Server.brickWidth, 8.5f * Server.brickHeight, 7.5f * Server.brickWidth),
			new Vector3(18 * Server.brickWidth, 18 * Server.brickHeight, 18 * Server.brickWidth)
		);

        private int ChunksNum = 26 * 26;
        private const int ChunksInitializedPerFrame = 26;

        public static readonly int MapMinChunkX = -13;
        public static readonly int MapMaxChunkX = 12;

        public static readonly int MapMinCoords = MapMinChunkX * 16;
		public static readonly int MapMaxCoords = MapMaxChunkX * 16;

		// used on the main thread to rebuild slices after a block changes
		private ChunkRenderer sliceRenderer = new ChunkRenderer();

		public long? Seed { get; private set; }

		/// <summary>True once the whole world has been generated and its meshes built.</summary>
		public bool IsReady { get; private set; }

		public event System.Action OnReady;

		private void Awake() {
			Instance = this;
		}

		void Start () {
			Menu.Instance.showPanel("LoadingPanel");
		}

		/// <summary>
		/// Generates the world. Every player generates it from the server's seed, see Network.WorldNetwork.
		/// Calling it again does nothing, <see cref="OnReady"/> is raised once it's done.
		/// </summary>
		public void Generate(long seed)
		{
			if (Seed.HasValue) {
				if (Seed.Value != seed) {
					Debug.LogError("The world was already generated with another seed");
				}
				if (IsReady) {
					OnReady?.Invoke();
				}
				return;
			}
			Seed = seed;
			StartCoroutine(generate(seed));
		}

		// the main thread never waits for the generation, so the game (and its network connection) keeps running
		private IEnumerator generate(long seed)
		{
			WorldLoadProfiler.Start();

			// counts the whole generation, so the world isn't considered ready before it starts rendering
			PendingChunkRenders = ChunksNum;

			// a copy, so the texture array built from the block folders doesn't end up saved in the asset
			BlockMaterial = new Material((Material)Resources.Load ("Materials/Block_Triplanar", typeof(Material)));
			BlockMaterial.SetTexture("_MainTexture", BlockDatabase.TextureArray);

			ChunkMeshThreadEntry[] chunkEntries = new ChunkMeshThreadEntry[ChunksNum];

			ChunkGenManager chunkGenManager = new ChunkGenManager(MapMinChunkX, MapMaxChunkX + 1, 6, this, seed, chunkEntries);

			// its own thread: the generation waits for the thread pool, which it limits to its workers
			Thread generationThread = new Thread(() => chunkGenManager.Generate());
			generationThread.IsBackground = true;
			generationThread.Start();

			while (generationThread.IsAlive)
				yield return null;

			WorldLoadProfiler.Phase("terrain generated");

			// chunk game objects can only be created on the main thread
			for(int x = 0; x < ChunksNum; ++x)
			{
				chunkEntries[x].Init();
				chunkEntries[x].Chunk.IsDetailed = isNearCamera(chunkEntries[x].Chunk);
				ThreadPool.QueueUserWorkItem(new WaitCallback(chunkEntries[x].ThreadCallback));

				if (x % ChunksInitializedPerFrame == ChunksInitializedPerFrame - 1 && x < ChunksNum - 1)
					yield return null;
			}

			WorldLoadProfiler.Phase("chunk objects created");
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

			if (IsReady)
				updateLevelOfDetail();

			uploadSliceMeshes();
		}

		// re-meshes, on the thread pool, the chunks that got closer to or farther from the camera
		private void updateLevelOfDetail()
		{
			Camera cam = Camera.main;

			if (cam != null) {
				Vector3Int block = BrickGrid.CellToBlock(BrickGrid.WorldToCell(cam.transform.position));
				detailCenter = new Vector2Int(block.x >> 4, block.z >> 4);
			}

			foreach (Chunk chunk in ChunksMap) {
				if (chunk == null || chunk.IsRemeshing) {
					continue;
				}
				bool detailed = isNearCamera(chunk);

				if (chunk.IsDetailed != detailed) {
					chunk.IsDetailed = detailed;
					chunk.IsRemeshing = true;

					ThreadPool.QueueUserWorkItem(_ => {
						try {
							new ChunkRenderer().RenderChunk(chunk, detailed);
						} finally {
							chunk.IsRemeshing = false;
						}
					});
				}
			}
		}

		/// <summary>Height to stand on at the given position: right above its highest block.</summary>
		public float GetSurfaceHeight(Vector3 position)
		{
			Vector3Int block = BrickGrid.CellToBlock(BrickGrid.WorldToCell(position));

			for (int y = Chunk.NumSlices * Chunk.SliceHeight - 1; y >= 0; y--) {
				BlockType type = GetBlockType(block.x, y, block.z);

				if (type != BlockType.Air && type != BlockType.NULL) {
					return BrickGrid.CellToWorld(BrickGrid.BlockToCell(new Vector3Int(block.x, y + 1, block.z))).y;
				}
			}
			return position.y;
		}

		private bool isNearCamera(Chunk chunk)
		{
			return Mathf.Abs(chunk.X - detailCenter.x) <= DetailRadius && Mathf.Abs(chunk.Z - detailCenter.y) <= DetailRadius;
		}

		// uploads the slice meshes computed by the chunk threads, within a time budget per frame
		private void uploadSliceMeshes()
		{
			if (ChunkSlicesWorkingQueue.Count == 0)
			{
				lock(ChunkQueueLock)
				{
					Queue<ChunkSliceBuildEntry> temp = ChunkSlicesWorkingQueue;
					ChunkSlicesWorkingQueue = ChunkSlicesToBuild;
					ChunkSlicesToBuild = temp;
				}
			}

			// while loading nothing else is going on, but keep the frames short enough for the network
			float budgetMs = IsReady ? UploadBudgetMs : LoadingUploadBudgetMs;
			uploadClock.Restart();

			while (ChunkSlicesWorkingQueue.Count != 0 && uploadClock.Elapsed.TotalMilliseconds < budgetMs)
			{
				BuildChunkSliceMesh(ChunkSlicesWorkingQueue.Dequeue());
			}

			if (Seed.HasValue && !IsReady && ChunkSlicesWorkingQueue.Count == 0 && Volatile.Read(ref PendingChunkRenders) == 0) {
				bool queueEmpty;
				lock (ChunkQueueLock) {
					queueEmpty = ChunkSlicesToBuild.Count == 0;
				}
				if (queueEmpty) {
					IsReady = true;
					WorldLoadProfiler.Finish();
					OnReady?.Invoke();
				}
			}

			lock(SliceLock)
			{
				Queue<ChunkSlicesDeleteEntry> temp = SlicesToDeleteWorking;
				SlicesToDeleteWorking = SlicesToDelete;
				SlicesToDelete = temp;
			}

			SlicesToDeleteWorking.Clear();
		}

		// muertet
		public void BuildChunkSliceMesh(ChunkSliceBuildEntry chunkEntry)
		{
			// a block changed while a thread was meshing this slice, mesh it again with the current blocks
			if (chunkEntry.ChunkVersion != chunkEntry.ParentChunk.Version) {
				RebuildSlice(chunkEntry.ParentChunk, chunkEntry.SliceIndex);
				return;
			}

			long uploadStart = WorldLoadProfiler.Now();

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

			// the threads already computed everything, this is just a copy
			Mesh mesh = new Mesh();
			int vertexCount = chunkEntry.Vertices.Length;
			int indexCount = chunkEntry.Triangles.Length;

			mesh.SetVertexBufferParams(vertexCount, TerrainVertex.Layout);
			mesh.SetVertexBufferData(chunkEntry.Vertices, 0, 0, vertexCount, 0, FastMeshUpdate);
			mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
			mesh.SetIndexBufferData(chunkEntry.Triangles, 0, 0, indexCount, FastMeshUpdate);
			mesh.subMeshCount = 1;
			mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), FastMeshUpdate);
			mesh.bounds = SliceBounds;

			filter.sharedMesh = mesh;

			// generate a much simpler collider mesh
			long colliderStart = WorldLoadProfiler.Now();

			if (chunkEntry.ColliderTriangles.Length > 0) {
				mesh = new Mesh();
				mesh.indexFormat = IndexFormat.UInt32;
				mesh.SetVertices(chunkEntry.ColliderVertices);
				mesh.SetTriangles(chunkEntry.ColliderTriangles, 0);

				meshCollider.sharedMesh = mesh;
			} else {
				meshCollider.sharedMesh = null;
			}
			WorldLoadProfiler.AddColliderUpload(colliderStart);

			chunkEntry.ParentChunk.ClearDirtySlices();

			WorldLoadProfiler.AddSliceUpload(uploadStart, chunkEntry.Vertices.Length);
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

			// meshes of this chunk still being computed by a thread are outdated now
			chunk.Version++;

			chunk.GetOrCreateSliceObject(sliceIndex);
			BuildChunkSliceMesh(sliceRenderer.RenderSlice(chunk, sliceIndex, chunk.IsDetailed, chunk.Version));
		}

		public Chunk GetChunk(int x, int z)
		{
			return ChunksMap[ChunkIndexFromCoords(x, z)];
		}
	}
}
