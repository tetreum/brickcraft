using System.Threading;

// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	public class ChunkMeshThreadEntry {

		private Chunk chunkToBeRendered;

		public Chunk Chunk {
			get { return chunkToBeRendered; }
		}
	
		public ChunkMeshThreadEntry(Chunk chunk)
		{
			chunkToBeRendered = chunk;
		}
	
		public void Init()
		{
			chunkToBeRendered.InitGameObject();
			chunkToBeRendered.InitRenderableSlices();
		}
	
		public void ThreadCallback(object threadContext)
		{
			long start = WorldLoadProfiler.Now();
			try
			{
				ChunkRenderer chunkRenderer = new ChunkRenderer();
				chunkRenderer.RenderChunk(chunkToBeRendered, chunkToBeRendered.IsDetailed);
			}
			finally
			{
				WorldLoadProfiler.AddChunkMeshing(start);
				Interlocked.Decrement(ref WorldBehaviour.PendingChunkRenders);
			}
		}
	}
}
