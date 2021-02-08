using System.Threading;
using System;
using Brickcraft.World.CustomGenerator;

// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	public class ChunkGenThreadEntry {
	
		private int fromChunkX;
		private int toChunkX;
		private ChunkGeneratorPool genPool;
		private Action<CustomChunkGenerator,int> threadBody;
	
		public ChunkGenThreadEntry(int fromX, int toX, ChunkGeneratorPool pool, Action<CustomChunkGenerator,int> action)
		{
			threadBody = action;
			fromChunkX = fromX;
			toChunkX = toX;
			genPool = pool;
		}
	
		public void ThreadCallback(object context)
		{
			CustomChunkGenerator chunkGen = genPool.GetGenerator();
		
			for(int x = fromChunkX; x < toChunkX; ++x)
				threadBody.Invoke(chunkGen, x);
		
			genPool.ReleaseGenerator(chunkGen);
		
			ManualResetEvent finishEvent = (ManualResetEvent)context;
			finishEvent.Set();
		}
	}
}
