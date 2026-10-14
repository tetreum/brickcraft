using System;
using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Brickcraft.Npcs.Navigation
{
    /// <summary>
    /// Where NPCs can walk, on the server: the brick grid's cells (studs sideways, plates up) seen as columns,
    /// each with its spans (see NavSpan): where there's solid ground with free cells above. Solid is the
    /// terrain's blocks (fluids and replaceable blocks aren't) and the cells bricks fill: their placement, or for
    /// bricks with other colliders (a door frame's opening, a door, open or closed) the cells their colliders
    /// touch. Positions are absolute cells, so the floating origin doesn't matter.
    ///
    /// Tiles are chunks (32 x 32 columns), built on worker threads (NavTileBuildJob) around NPCs, and built
    /// again when something changes there (blocks, bricks, doors). Paths (NavPathJob) are asked for with
    /// RequestPath and come back a frame or so later.
    /// </summary>
    public static class NavGrid
    {
        public const int TileCells = 32;
        /// <summary>The cells columns cover, up and down (the test scene has bricks below 0).</summary>
        public const int MinY = -768;
        public const int MaxY = 1024;
        public const int Height = MaxY - MinY;
        /// <summary>Tiles kept around each NPC.</summary>
        public const int TileRadius = 2;
        private const int MaxBuildsPerFrame = 2;
        private const int MaxNodes = 6000;
        // a door swings for a moment before its tile is built again
        private const float StateChangeDelay = 0.5f;

        /// <summary>Paths are drawn in the scene view (the /navdebug command).</summary>
        public static bool DebugDraw;

        private class Tile
        {
            public bool built;
            public bool building;
            public float dirtyAt = -1; // when it has to be built again, -1 if it doesn't
        }

        private class Build
        {
            public Vector2Int chunk;
            public NavTileBuildJob job;
            public JobHandle handle;
        }

        private class Request
        {
            public NavPathJob job;
            public JobHandle handle;
            public Action<NavPath> done;
        }

        private static NativeParallelMultiHashMap<int2, NavSpan> spans;
        private static readonly Dictionary<Vector2Int, Tile> tiles = new Dictionary<Vector2Int, Tile>();
        private static readonly List<Build> builds = new List<Build>();
        private static readonly List<Request> requests = new List<Request>();
        private static readonly List<Request> waiting = new List<Request>();
        // path jobs reading the spans: done before they change
        private static JobHandle readers;
        private static GameObject probe;
        private static BoxCollider probeBox;

        public static bool IsRunning {
            get { return spans.IsCreated; }
        }

        public static void Start() {
            Stop();
            spans = new NativeParallelMultiHashMap<int2, NavSpan>(1 << 16, Allocator.Persistent);
            probe = new GameObject("NavGrid probe") { hideFlags = HideFlags.HideAndDontSave };
            probe.layer = (int)Game.Layers.IgnoreRaycast;
            probe.transform.position = new Vector3(0, -100000, 0);
            probeBox = probe.AddComponent<BoxCollider>();
            probeBox.isTrigger = true;
            // the middle of a cell: thin parts at its edges (a door frame's posts) don't block it, a wall through it does
            probeBox.size = BrickGrid.CellSize * 0.4f;
        }

        public static void Stop() {
            readers.Complete();
            foreach (Build build in builds) {
                build.handle.Complete();
                dispose(build);
            }
            builds.Clear();
            foreach (Request request in requests) {
                request.handle.Complete();
                dispose(request);
            }
            foreach (Request request in waiting) {
                dispose(request);
            }
            requests.Clear();
            waiting.Clear();
            tiles.Clear();
            if (spans.IsCreated) {
                spans.Dispose();
            }
            if (probe != null) {
                UnityEngine.Object.Destroy(probe);
            }
        }

        /// <summary>What an NPC of that size needs, in cells.</summary>
        public static NavAgent AgentFor(NpcInfo info, float radius, float height) {
            return new NavAgent() {
                radius = Mathf.Max(0, Mathf.CeilToInt(radius / Server.studSize - 0.5f)),
                height = Mathf.CeilToInt(height / Server.plateHeight),
                step = Mathf.FloorToInt(info.stepHeight / Server.plateHeight + 0.01f),
                drop = Mathf.FloorToInt(info.maxDrop / Server.plateHeight),
                avoidFluids = true, // swimmers come later
            };
        }

        /// <summary>
        /// Asks for a path from a position to another (local positions); done gets it a frame or so later.
        /// goalRange: close enough, in meters sideways from the goal.
        /// </summary>
        public static void RequestPath(NavAgent agent, Vector3 from, Vector3 to, float goalRange, Action<NavPath> done) {
            if (!IsRunning) {
                done(new NavPath(NavResult.Failed, new List<Vector3Int>()));
                return;
            }
            Vector3Int start = BrickGrid.WorldToCell(from + Vector3.up * 0.05f);
            Vector3Int goal = BrickGrid.WorldToCell(to + Vector3.up * 0.05f);
            waiting.Add(new Request() {
                job = new NavPathJob() {
                    agent = agent,
                    start = new int3(start.x, start.y, start.z),
                    goal = new int3(goal.x, goal.y, goal.z),
                    goalRange = Mathf.Max(0, Mathf.FloorToInt(goalRange / Server.studSize)),
                    maxNodes = MaxNodes,
                    path = new NativeList<int3>(32, Allocator.Persistent),
                    result = new NativeReference<NavResult>(Allocator.Persistent),
                },
                done = done,
            });
        }

        /// <summary>Something changed in the cells between those positions: their tiles are built again.</summary>
        public static void MarkDirty(Bounds bounds, float delay = 0) {
            if (!IsRunning) {
                return;
            }
            Vector2Int min = chunkOf(BrickGrid.WorldToCell(bounds.min));
            Vector2Int max = chunkOf(BrickGrid.WorldToCell(bounds.max));
            for (int x = min.x; x <= max.x; x++) {
                for (int z = min.y; z <= max.y; z++) {
                    if (tiles.TryGetValue(new Vector2Int(x, z), out Tile tile)) {
                        float at = Time.time + delay;
                        tile.dirtyAt = tile.dirtyAt < 0 ? at : Mathf.Max(tile.dirtyAt, at);
                    }
                }
            }
        }

        /// <summary>A brick appeared, went away or changed: what it covers (all its colliders, a door swinging) is checked again.</summary>
        public static void MarkDirty(Brick brick, bool stateChanged = false) {
            if (!IsRunning) {
                return;
            }
            Bounds bounds = brick.WorldBounds;
            if (brick.gameObject != null) {
                foreach (Collider collider in brick.gameObject.GetComponentsInChildren<Collider>()) {
                    bounds.Encapsulate(collider.bounds);
                }
            }
            bounds.Expand(Server.brickWidth * 2); // an open door's reach, whichever way it turns
            MarkDirty(bounds, stateChanged ? StateChangeDelay : 0);
        }

        public static void MarkBlockDirty(Vector3Int block) {
            Vector3 corner = BrickGrid.CellToWorld(BrickGrid.BlockToCell(block));
            MarkDirty(new Bounds(corner + Vector3.one * 0.4f, Vector3.one * 2));
        }

        private static Vector2Int chunkOf(Vector3Int cell) {
            return new Vector2Int(BrickGrid.FloorDiv(cell.x, TileCells), BrickGrid.FloorDiv(cell.z, TileCells));
        }

        // -------- every frame --------

        public static void Update() {
            if (!IsRunning) {
                return;
            }
            finishBuilds();
            finishRequests();
            updateTiles();
            startRequests();
            if (DebugDraw) {
                drawAroundNpcs();
            }
        }

        private static void finishBuilds() {
            for (int i = builds.Count - 1; i >= 0; i--) {
                Build build = builds[i];
                if (!build.handle.IsCompleted) {
                    continue;
                }
                build.handle.Complete();
                readers.Complete();
                builds.RemoveAt(i);
                if (tiles.TryGetValue(build.chunk, out Tile tile)) {
                    removeSpans(build.chunk);
                    foreach (NavSpanEntry entry in build.job.spans) {
                        spans.Add(entry.column, entry.span);
                    }
                    tile.built = true;
                    tile.building = false;
                }
                dispose(build);
            }
        }

        private static void finishRequests() {
            for (int i = requests.Count - 1; i >= 0; i--) {
                Request request = requests[i];
                if (!request.handle.IsCompleted) {
                    continue;
                }
                request.handle.Complete();
                requests.RemoveAt(i);
                List<Vector3Int> points = new List<Vector3Int>(request.job.path.Length);
                foreach (int3 cell in request.job.path) {
                    points.Add(new Vector3Int(cell.x, cell.y, cell.z));
                }
                NavResult result = request.job.result.Value;
                dispose(request);
                request.done(new NavPath(result, points));
            }
        }

        private static void startRequests() {
            foreach (Request request in waiting) {
                request.job.spans = spans;
                request.handle = request.job.Schedule();
                readers = JobHandle.CombineDependencies(readers, request.handle);
                requests.Add(request);
            }
            waiting.Clear();
            JobHandle.ScheduleBatchedJobs();
        }

        // tiles around NPCs (only where the world is loaded); dirty ones built again
        private static void updateTiles() {
            HashSet<Vector2Int> wanted = new HashSet<Vector2Int>();
            foreach (Npc npc in NpcSystem.All) {
                Vector2Int center = chunkOf(BrickGrid.WorldToCell(npc.transform.position));
                for (int x = -TileRadius; x <= TileRadius; x++) {
                    for (int z = -TileRadius; z <= TileRadius; z++) {
                        Vector2Int chunk = center + new Vector2Int(x, z);
                        if (WorldBehaviour.Instance == null || WorldBehaviour.Instance.IsGenerated(chunk)) {
                            wanted.Add(chunk);
                        }
                    }
                }
            }
            List<Vector2Int> unwanted = new List<Vector2Int>();
            foreach (KeyValuePair<Vector2Int, Tile> tile in tiles) {
                if (!wanted.Contains(tile.Key) && !tile.Value.building) {
                    unwanted.Add(tile.Key);
                }
            }
            if (unwanted.Count > 0) {
                readers.Complete();
                foreach (Vector2Int chunk in unwanted) {
                    removeSpans(chunk);
                    tiles.Remove(chunk);
                }
            }
            foreach (Vector2Int chunk in wanted) {
                if (!tiles.ContainsKey(chunk)) {
                    tiles[chunk] = new Tile() { dirtyAt = 0 };
                }
            }

            int started = 0;
            foreach (KeyValuePair<Vector2Int, Tile> entry in tiles) {
                Tile tile = entry.Value;
                if (started >= MaxBuildsPerFrame) {
                    break;
                }
                if (!tile.building && tile.dirtyAt >= 0 && Time.time >= tile.dirtyAt) {
                    tile.dirtyAt = -1;
                    tile.building = true;
                    startBuild(entry.Key);
                    started++;
                }
            }
        }

        private static void removeSpans(Vector2Int chunk) {
            for (int x = 0; x < TileCells; x++) {
                for (int z = 0; z < TileCells; z++) {
                    spans.Remove(new int2(chunk.x * TileCells + x, chunk.y * TileCells + z));
                }
            }
        }

        // -------- building a tile --------

        private static void startBuild(Vector2Int chunk) {
            int2 origin = new int2(chunk.x * TileCells, chunk.y * TileCells);
            NavTileBuildJob job = new NavTileBuildJob() {
                blocks = terrainOf(chunk),
                brickCells = bricksIn(origin),
                origin = origin,
                cells = new NativeArray<byte>(TileCells * TileCells * Height, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                spans = new NativeList<NavSpanEntry>(TileCells * TileCells * 2, Allocator.Persistent),
            };
            builds.Add(new Build() { chunk = chunk, job = job, handle = job.Schedule() });
            JobHandle.ScheduleBatchedJobs();
        }

        // what every block of the chunk is
        private static NativeArray<byte> terrainOf(Vector2Int coords) {
            Chunk chunk = WorldBehaviour.Instance != null ? WorldBehaviour.Instance.GetChunk(coords.x, coords.y) : null;
            if (chunk == null) {
                return new NativeArray<byte>(0, Allocator.Persistent);
            }
            byte[] kinds = new byte[256];
            for (int i = 0; i < kinds.Length; i++) {
                BlockDefinition block = BlockDatabase.Get((byte)i);
                kinds[i] = i == (int)BlockType.Air || block.isUnknown ? NavTileBuildJob.Empty
                    : block.isFluid ? NavTileBuildJob.FluidCell
                    : block.isReplaceable ? NavTileBuildJob.Empty
                    : NavTileBuildJob.Solid;
            }
            NativeArray<byte> blocks = new NativeArray<byte>(16 * 256 * 16, Allocator.Persistent);
            for (int y = 0; y < 256; y++) {
                for (int z = 0; z < 16; z++) {
                    for (int x = 0; x < 16; x++) {
                        blocks[(y * 16 + z) * 16 + x] = kinds[(byte)chunk.GetBlockType(x, y, z)];
                    }
                }
            }
            return blocks;
        }

        // the cells the bricks over the tile fill
        private static NativeArray<int4> bricksIn(int2 origin) {
            List<int4> cells = new List<int4>();
            RectInt tile = new RectInt(origin.x, origin.y, TileCells, TileCells);
            // tiles are chunks: only the bricks in it (attachments' colliders are their holder's children, done with it)
            Vector2Int chunk = BrickGrid.ChunkOfCell(new Vector3Int(origin.x, 0, origin.y));
            foreach (Brick brick in BrickGrid.BricksInChunk(chunk)) {
                byte kind = isFluid(brick.item) ? NavTileBuildJob.FluidCell : NavTileBuildJob.Solid;
                if (isPlainBox(brick)) {
                    Vector3Int min = brick.placement.origin;
                    Vector3Int size = brick.placement.Size;
                    if (min.x >= tile.xMax || min.z >= tile.yMax || min.x + size.x <= tile.xMin || min.z + size.z <= tile.yMin) {
                        continue;
                    }
                    for (int x = Mathf.Max(min.x, tile.xMin); x < Mathf.Min(min.x + size.x, tile.xMax); x++) {
                        for (int z = Mathf.Max(min.z, tile.yMin); z < Mathf.Min(min.z + size.z, tile.yMax); z++) {
                            for (int y = min.y; y < min.y + size.y; y++) {
                                cells.Add(new int4(x - origin.x, y, z - origin.y, kind));
                            }
                        }
                    }
                } else {
                    voxelize(brick, tile, origin, kind, cells);
                }
            }
            NativeArray<int4> result = new NativeArray<int4>(cells.Count, Allocator.Persistent);
            for (int i = 0; i < cells.Count; i++) {
                result[i] = cells[i];
            }
            return result;
        }

        // one box collider filling its placement (most bricks)
        private static bool isPlainBox(Brick brick) {
            if (brick.gameObject == null) {
                return true;
            }
            Collider[] colliders = brick.gameObject.GetComponentsInChildren<Collider>();
            return colliders.Length == 1 && colliders[0] is BoxCollider && colliders[0].gameObject == brick.gameObject;
        }

        // the cells its colliders (and what's attached to it) touch
        private static void voxelize(Brick brick, RectInt tile, int2 origin, byte kind, List<int4> cells) {
            foreach (Collider collider in brick.gameObject.GetComponentsInChildren<Collider>()) {
                if (!collider.enabled || collider.isTrigger) {
                    continue;
                }
                Vector3Int min = BrickGrid.WorldToCell(collider.bounds.min);
                Vector3Int max = BrickGrid.WorldToCell(collider.bounds.max);
                Transform t = collider.transform;
                for (int x = Mathf.Max(min.x, tile.xMin); x <= Mathf.Min(max.x, tile.xMax - 1); x++) {
                    for (int z = Mathf.Max(min.z, tile.yMin); z <= Mathf.Min(max.z, tile.yMax - 1); z++) {
                        for (int y = min.y; y <= max.y; y++) {
                            Vector3 center = BrickGrid.CellToWorld(new Vector3Int(x, y, z), new Vector3(0.5f, 0.5f, 0.5f));
                            if (Physics.ComputePenetration(probeBox, center, Quaternion.identity, collider, t.position, t.rotation, out _, out _)) {
                                cells.Add(new int4(x - origin.x, y, z - origin.y, kind));
                            }
                        }
                    }
                }
            }
        }

        private static bool isFluid(Item item) {
            return item.layer == (int)Game.Layers.Water || (item.blockType.HasValue && BlockDatabase.Get(item.blockType.Value).isFluid);
        }

        private static void dispose(Build build) {
            build.job.blocks.Dispose();
            build.job.brickCells.Dispose();
            build.job.cells.Dispose();
            build.job.spans.Dispose();
        }

        private static void dispose(Request request) {
            request.job.path.Dispose();
            request.job.result.Dispose();
        }

        // -------- debugging --------

        // the floors around each NPC, in the scene view
        private static void drawAroundNpcs() {
            readers.Complete();
            foreach (Npc npc in NpcSystem.All) {
                Vector3Int center = BrickGrid.WorldToCell(npc.transform.position);
                for (int x = -12; x <= 12; x++) {
                    for (int z = -12; z <= 12; z++) {
                        int2 column = new int2(center.x + x, center.z + z);
                        if (!spans.TryGetFirstValue(column, out NavSpan span, out NativeParallelMultiHashMapIterator<int2> it)) {
                            continue;
                        }
                        do {
                            if (math.abs(span.floor - center.y) > 30) {
                                continue;
                            }
                            Vector3 a = BrickGrid.CellToWorld(new Vector3Int(column.x, span.floor, column.y), new Vector3(0.2f, 0.05f, 0.2f));
                            Vector3 b = BrickGrid.CellToWorld(new Vector3Int(column.x, span.floor, column.y), new Vector3(0.8f, 0.05f, 0.8f));
                            Debug.DrawLine(a, b, span.IsFluid ? Color.blue : Color.green);
                        } while (spans.TryGetNextValue(out span, ref it));
                    }
                }
            }
        }
    }

    /// <summary>
    /// A path NavGrid found: the floor cells to walk through (the first is where it started). Cells are absolute,
    /// so it stays right when the floating origin moves.
    /// </summary>
    public class NavPath
    {
        public readonly NavResult result;
        public readonly List<Vector3Int> cells;

        public NavPath(NavResult result, List<Vector3Int> cells) {
            this.result = result;
            this.cells = cells;
        }

        public int Count {
            get { return cells.Count; }
        }

        /// <summary>Where a point is now: the middle of its cell, on its floor.</summary>
        public Vector3 PointAt(int index) {
            return BrickGrid.CellToWorld(cells[index], new Vector3(0.5f, 0, 0.5f));
        }
    }
}
