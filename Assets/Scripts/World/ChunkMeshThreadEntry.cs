// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	public class ChunkMeshThreadEntry {

		private Chunk chunkToBeRendered;
	
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
			ChunkRenderer chunkRenderer = new ChunkRenderer();
			chunkRenderer.RenderChunk(chunkToBeRendered);
		}
	}
}
