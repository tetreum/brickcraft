using UnityEngine;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine.Rendering;
using Brickcraft.Bricks;
using Brickcraft.World.CustomGenerator;

// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	/// <summary>
	/// The streamed, endless world. Chunks are loaded on demand (see Network.WorldNetwork, which
	/// decides what each side needs) and go through:
	///   1. generation from the seed, on the thread pool
	///   2. the changes players made to them applied, on the main thread
	///   3. meshing, once their 4 neighbours exist and they're within view of the local camera
	///   4. unloading, when nobody needs them anymore
	/// </summary>
	public class WorldBehaviour : MonoBehaviour {
		public static WorldBehaviour Instance;

		public static Queue<ChunkSliceBuildEntry> ChunkSlicesToBuild = new Queue<ChunkSliceBuildEntry>();
		private static Queue<ChunkSliceBuildEntry> ChunkSlicesWorkingQueue = new Queue<ChunkSliceBuildEntry>();
		public static object ChunkQueueLock = new object();

		public static Material BlockMaterial;

		[Tooltip("Chunks this far from the camera (in chunks) are drawn")]
		public int ViewDistance = 8;

		[Tooltip("Chunks this far from the camera (in chunks) are drawn with studs, the rest with simple boxes")]
		public int DetailRadius = 4;

		private const int MeshJobsStartedPerFrame = 16;
		private const float UploadBudgetMs = 4f;
		private const float LoadingUploadBudgetMs = 40f;
		private readonly System.Diagnostics.Stopwatch uploadClock = new System.Diagnostics.Stopwatch();

		private const MeshUpdateFlags FastMeshUpdate = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;

		// local bounds of any slice mesh, with a block of margin for models bigger than a block
		private static readonly Bounds SliceBounds = new Bounds(
			new Vector3(7.5f * Server.brickWidth, 8.5f * Server.brickHeight, 7.5f * Server.brickWidth),
			new Vector3(18 * Server.brickWidth, 18 * Server.brickHeight, 18 * Server.brickWidth)
		);

		private readonly ConcurrentDictionary<Vector2Int, Chunk> chunks = new ConcurrentDictionary<Vector2Int, Chunk>();
		private readonly ConcurrentQueue<Chunk> generatedChunks = new ConcurrentQueue<Chunk>();
		private readonly List<Chunk> meshCandidates = new List<Chunk>();

		// one generator per thread, they aren't thread safe
		private ThreadLocal<CustomChunkGenerator> generators;

		// changes players made, applied to chunks once generated
		private WorldChanges changes;

		// called on the generation thread before a chunk is finished, to load what it needs from disk
		private Action<Vector2Int> prepareChunk;

		// used on the main thread to rebuild slices after a block changes
		private ChunkRenderer sliceRenderer = new ChunkRenderer();

		public long? Seed { get; private set; }

		/// <summary>Where chunks are drawn around until the local player exists.</summary>
		public Vector2Int SpawnChunk { get; set; }

		/// <summary>True while the area around the spawn is loading, uploads get a bigger budget.</summary>
		public bool IsLoadingSpawn { get; set; }

		/// <summary>A chunk was generated and its saved changes applied (main thread).</summary>
		public event Action<Chunk> ChunkGenerated;

		/// <summary>A chunk was unloaded (main thread).</summary>
		public event Action<Vector2Int> ChunkUnloaded;

		public int LoadedChunkCount {
			get { return chunks.Count; }
		}

		public ICollection<Chunk> Chunks {
			get { return chunks.Values; }
		}

		private void Awake() {
			Instance = this;
			FloatingOrigin.Reset();
		}

		void Start () {
			Events.EventManager.WorldLoadingStarted.Raise(new Events.WorldLoadingStartedEvent());
		}

		/// <summary>Sets up the world, chunks are then loaded with <see cref="RequestChunk"/>.</summary>
		public void Initialize(long seed, WorldChanges worldChanges, Action<Vector2Int> prepare = null)
		{
			Seed = seed;
			changes = worldChanges;
			prepareChunk = prepare;
			generators = new ThreadLocal<CustomChunkGenerator>(() => {
				CustomChunkGenerator generator = new CustomChunkGenerator();
				generator.Init(seed);
				return generator;
			});

			// a copy, so the texture array built from the block folders doesn't end up saved in the asset
			BlockMaterial = new Material((Material)Resources.Load ("Materials/Block_Triplanar", typeof(Material)));
			BlockMaterial.SetTexture("_MainTexture", BlockDatabase.TextureArray);

			WorldLoadProfiler.Start();
		}

		// -------- loading and unloading --------

		/// <summary>Starts generating a chunk, unless it's already loaded (main thread).</summary>
		public void RequestChunk(Vector2Int coords)
		{
			if (chunks.ContainsKey(coords))
				return;

			Chunk chunk = new Chunk(coords.x, coords.y, this, Color.white);
			chunks[coords] = chunk;

			ThreadPool.QueueUserWorkItem(_ => generate(chunk));
		}

		/// <summary>Forgets a chunk: its meshes, objects and blocks (main thread).</summary>
		public void UnloadChunk(Vector2Int coords)
		{
			if (!chunks.TryRemove(coords, out Chunk chunk))
				return;

			bool wasGenerated = chunk.State == Chunk.ChunkState.Generated;
			chunk.State = Chunk.ChunkState.Unloaded;
			chunk.DestroyMesh();

			if (wasGenerated)
				ChunkUnloaded?.Invoke(coords);
		}

		public bool IsGenerated(Vector2Int coords)
		{
			return chunks.TryGetValue(coords, out Chunk chunk) && chunk.State == Chunk.ChunkState.Generated;
		}

		/// <summary>The chunk is drawn and has colliders, so it can be walked on.</summary>
		/// <summary>How far a chunk is from walkable, from 0 (not requested) to 1, for loading screens.</summary>
		public float GetLoadProgress(Vector2Int coords)
		{
			if (!chunks.TryGetValue(coords, out Chunk chunk) || chunk.State == Chunk.ChunkState.Unloaded)
				return 0f;
			if (chunk.State == Chunk.ChunkState.Generating)
				return 0.25f;
			if (chunk.HasColliders)
				return 1f;
			return chunk.IsMeshJobDone ? 0.85f : 0.6f;
		}

		public bool IsWalkable(Vector2Int coords)
		{
			return chunks.TryGetValue(coords, out Chunk chunk) && chunk.State == Chunk.ChunkState.Generated && chunk.HasColliders;
		}

		public static Vector2Int ChunkAt(Vector3 position)
		{
			Vector3Int block = BrickGrid.CellToBlock(BrickGrid.WorldToCell(position));
			return new Vector2Int(block.x >> 4, block.z >> 4);
		}

		private void generate(Chunk chunk)
		{
			try {
				if (chunk.State == Chunk.ChunkState.Unloaded)
					return;

				generators.Value.GenerateChunk(chunk, chunk.X, chunk.Z);

				if (prepareChunk != null && chunk.State != Chunk.ChunkState.Unloaded)
					prepareChunk(new Vector2Int(chunk.X, chunk.Z));
			} catch (Exception e) {
				Debug.LogError("Couldn't generate chunk " + chunk.X + "," + chunk.Z + ": " + e);
			} finally {
				generatedChunks.Enqueue(chunk);
			}
		}

		// generated chunks get the changes players made and become usable
		private void finishGeneratedChunks()
		{
			while (generatedChunks.TryDequeue(out Chunk chunk)) {
				if (chunk.State == Chunk.ChunkState.Unloaded)
					continue;

				if (changes != null) {
					changes.ApplyTo(chunk);
					chunk.RecalculateHeight();
				}
				chunk.State = Chunk.ChunkState.Generated;
				ChunkGenerated?.Invoke(chunk);
			}
		}

		// -------- meshing --------

		void Update () {
			finishGeneratedChunks();

			if (!Seed.HasValue)
				return;

			if (Player.Instance != null)
				FloatingOrigin.Recenter(Player.Instance.transform.position);

			Vector2Int center = Player.Instance != null ? ChunkAt(Player.Instance.transform.position) : SpawnChunk;

			foreach (Chunk chunk in chunks.Values) {
				if (!chunk.HasMesh)
					continue;

				foreach (ChunkSlice slice in chunk.Slices) {
					if (slice.IsEmpty || slice.renderer == null) {
						continue;
					}
					slice.FrustrumCulling();
				}

				if (!chunk.HasColliders && chunk.IsMeshReady)
					chunk.HasColliders = true;
			}

			updateMeshes(center);
			uploadSliceMeshes();
		}

		// meshes the nearest chunks in view, drops the meshes of the ones out of view, and switches levels of detail
		private void updateMeshes(Vector2Int center)
		{
			meshCandidates.Clear();

			foreach (Chunk chunk in chunks.Values) {
				if (chunk.State != Chunk.ChunkState.Generated)
					continue;

				int distance = Math.Max(Math.Abs(chunk.X - center.x), Math.Abs(chunk.Z - center.y));

				if (chunk.HasMesh) {
					if (distance > ViewDistance + 1) {
						chunk.DestroyMesh();
					} else if (chunk.IsMeshJobDone && !chunk.IsRemeshing && chunk.IsDetailed != (distance <= DetailRadius)) {
						remesh(chunk, distance <= DetailRadius);
					}
				} else if (distance <= ViewDistance && hasGeneratedNeighbours(chunk)) {
					meshCandidates.Add(chunk);
				}
			}

			if (meshCandidates.Count == 0)
				return;

			meshCandidates.Sort((a, b) =>
				Math.Max(Math.Abs(a.X - center.x), Math.Abs(a.Z - center.y)).CompareTo(Math.Max(Math.Abs(b.X - center.x), Math.Abs(b.Z - center.y))));

			for (int i = 0; i < meshCandidates.Count && i < MeshJobsStartedPerFrame; i++) {
				Chunk chunk = meshCandidates[i];
				bool detailed = Math.Max(Math.Abs(chunk.X - center.x), Math.Abs(chunk.Z - center.y)) <= DetailRadius;

				chunk.InitGameObject();
				chunk.InitRenderableSlices();
				chunk.HasMesh = true;
				chunk.IsDetailed = detailed;
				chunk.IsMeshJobDone = false;

				ThreadPool.QueueUserWorkItem(_ => {
					long start = WorldLoadProfiler.Now();
					try {
						new ChunkRenderer().RenderChunk(chunk, detailed);
					} finally {
						WorldLoadProfiler.AddChunkMeshing(start);
						chunk.IsMeshJobDone = true;
					}
				});
			}
		}

		// faces on chunk borders depend on the neighbours' blocks
		private bool hasGeneratedNeighbours(Chunk chunk)
		{
			return IsGenerated(new Vector2Int(chunk.X + 1, chunk.Z)) && IsGenerated(new Vector2Int(chunk.X - 1, chunk.Z))
				&& IsGenerated(new Vector2Int(chunk.X, chunk.Z + 1)) && IsGenerated(new Vector2Int(chunk.X, chunk.Z - 1));
		}

		private void remesh(Chunk chunk, bool detailed)
		{
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
			float budgetMs = IsLoadingSpawn ? LoadingUploadBudgetMs : UploadBudgetMs;
			uploadClock.Restart();

			while (ChunkSlicesWorkingQueue.Count != 0 && uploadClock.Elapsed.TotalMilliseconds < budgetMs)
			{
				ChunkSliceBuildEntry entry = ChunkSlicesWorkingQueue.Dequeue();
				BuildChunkSliceMesh(entry);

				if (entry.CountsAsPending)
					Interlocked.Decrement(ref entry.ParentChunk.PendingSliceUploads);
			}
		}

		// muertet
		public void BuildChunkSliceMesh(ChunkSliceBuildEntry chunkEntry)
		{
			Chunk chunk = chunkEntry.ParentChunk;
			GameObject chunkSliceObject = chunk.ChunkSliceObjects[chunkEntry.SliceIndex];

			// unloaded, or no longer in view, while the mesh was being computed
			if (chunk.State == Chunk.ChunkState.Unloaded || !chunk.HasMesh || chunkSliceObject == null)
				return;

			// a block changed while a thread was meshing this slice, mesh it again with the current blocks
			if (chunkEntry.ChunkVersion != chunk.Version) {
				RebuildSlice(chunk, chunkEntry.SliceIndex);
				return;
			}

			long uploadStart = WorldLoadProfiler.Now();

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

			chunk.ClearDirtySlices();

			WorldLoadProfiler.AddSliceUpload(uploadStart, chunkEntry.Vertices.Length);
		}

		// -------- blocks --------

		/// <summary>Block at world block coordinates, NULL where the world isn't loaded.</summary>
		public BlockType GetBlockType(int x, int y, int z)
		{
			if (y < 0 || y >= Chunk.NumSlices * Chunk.SliceHeight)
				return BlockType.NULL;

			Chunk chunk = GetChunk(x >> 4, z >> 4);

			if(chunk == null)
				return BlockType.NULL; // We return NULL so that is different from air and we don't build side faces of the blocks

			return chunk.GetBlockType(x & 0xF, y, z&0xF);
		}

		public BlockType GetBlockType(Vector3Int block)
		{
			return GetBlockType(block.x, block.y, block.z);
		}

		/// <summary>
		/// Changes a block of a loaded chunk and rebuilds the affected slice meshes.
		/// </summary>
		public bool SetBlockType(Vector3Int block, BlockType type)
		{
			if (block.y < 0 || block.y >= Chunk.NumSlices * Chunk.SliceHeight)
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

			// meshes of this chunk still being computed by a thread are outdated now
			chunk.Version++;

			// chunks out of view have no mesh to update
			if (!chunk.HasMesh)
				return;

			if (chunk.Slices[sliceIndex].IsEmpty && chunk.ChunkSliceObjects[sliceIndex] == null)
				return;

			chunk.GetOrCreateSliceObject(sliceIndex);
			BuildChunkSliceMesh(sliceRenderer.RenderSlice(chunk, sliceIndex, chunk.IsDetailed, chunk.Version));
		}

		/// <summary>A generated chunk, null if it isn't loaded (yet).</summary>
		public Chunk GetChunk(int x, int z)
		{
			return chunks.TryGetValue(new Vector2Int(x, z), out Chunk chunk) && chunk.State == Chunk.ChunkState.Generated ? chunk : null;
		}
	}
}
