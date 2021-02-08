using Brickcraft.World.CustomGenerator;
using System.Collections.Generic;

namespace Brickcraft.World {
	public class ChunkGeneratorPool {
	
		private Queue<CustomChunkGenerator> pool = new Queue<CustomChunkGenerator>();
		private object queueLock = new object();
	
		public ChunkGeneratorPool(int maxGenerators, long seed)
		{
			for(int i = 0; i < maxGenerators; ++i)
			{
				CustomChunkGenerator gen = new CustomChunkGenerator();
				gen.Init(seed);
				pool.Enqueue(gen);	
			}
		}
	
		public CustomChunkGenerator GetGenerator()
		{
			lock(queueLock)
			{
				if(pool.Count == 0)
				return null;			
		
				return pool.Dequeue();
			}
		}
	
		public void ReleaseGenerator(CustomChunkGenerator gen)
		{
			lock(queueLock)
			{
				pool.Enqueue(gen);
			}
		}
	}
}
