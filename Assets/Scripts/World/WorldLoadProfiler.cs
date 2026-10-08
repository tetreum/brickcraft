using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Brickcraft.World
{
    /// <summary>
    /// Measures how long loading the area around the spawn takes, and logs it once players can walk on it.
    /// Thread safe, the chunk threads add their own timings.
    /// </summary>
    public static class WorldLoadProfiler
    {
        private static readonly Stopwatch clock = new Stopwatch();
        private static readonly StringBuilder phases = new StringBuilder();
        private static long lastPhaseTicks;

        // summed across threads, in stopwatch ticks
        private static long chunkMeshingTicks;
        private static long sliceUploadTicks;
        private static long colliderUploadTicks;
        private static int slicesUploaded;
        private static long verticesUploaded;

        public static void Start() {
            UnityEngine.Debug.Log("Generating the world, " + UnityEngine.Time.realtimeSinceStartup.ToString("0.0") + " s after the game started");
            phases.Clear();
            chunkMeshingTicks = 0;
            sliceUploadTicks = 0;
            colliderUploadTicks = 0;
            slicesUploaded = 0;
            verticesUploaded = 0;
            lastPhaseTicks = 0;
            clock.Restart();
        }

        /// <summary>Marks the end of a step, measured since the previous one.</summary>
        public static void Phase(string name) {
            long now = clock.ElapsedTicks;
            phases.AppendFormat("  {0}: {1:0} ms\n", name, toMs(now - lastPhaseTicks));
            lastPhaseTicks = now;
        }

        public static long Now() {
            return Stopwatch.GetTimestamp();
        }

        public static void AddChunkMeshing(long startTimestamp) {
            Interlocked.Add(ref chunkMeshingTicks, Stopwatch.GetTimestamp() - startTimestamp);
        }

        public static void AddSliceUpload(long startTimestamp, int vertices) {
            Interlocked.Add(ref sliceUploadTicks, Stopwatch.GetTimestamp() - startTimestamp);
            Interlocked.Increment(ref slicesUploaded);
            Interlocked.Add(ref verticesUploaded, vertices);
        }

        public static void AddColliderUpload(long startTimestamp) {
            Interlocked.Add(ref colliderUploadTicks, Stopwatch.GetTimestamp() - startTimestamp);
        }

        public static void Finish() {
            Phase("spawn area ready");
            UnityEngine.Debug.Log(
                "World loaded in " + toMs(clock.ElapsedTicks).ToString("0") + " ms\n" + phases +
                "  meshing CPU time (all threads): " + toMs(chunkMeshingTicks).ToString("0") + " ms\n" +
                "  mesh upload on the main thread: " + toMs(sliceUploadTicks).ToString("0") + " ms for " +
                slicesUploaded + " slices, " + verticesUploaded + " vertices (colliders: " + toMs(colliderUploadTicks).ToString("0") + " ms)"
            );
            clock.Stop();
        }

        private static double toMs(long ticks) {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
