using UnityEngine;
using System;
using System.Threading;

// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	public class ChunkGenManager{
	
		private int fromChunkX;
		private int toChunkX;
		private int threadsNum;
		private ChunkMeshThreadEntry[] chunkEntries;
		private WorldBehaviour worldManager;
		private long worldSeed;
		private ChunkGeneratorPool GenPool;
	
		public ChunkGenManager(int fromX, int toX, int threads, WorldBehaviour world, long seed, ChunkMeshThreadEntry[] entries)
		{
			fromChunkX = fromX;
			toChunkX = toX;
			worldManager = world;
			chunkEntries = entries;
			threadsNum = threads;
			worldSeed = seed;
		}
	
		public void Generate()
		{
			ManualResetEvent[] genWait = new ManualResetEvent[threadsNum];
			ThreadPool.SetMaxThreads(threadsNum,threadsNum);
		
			int totalLoops = (Math.Abs(fromChunkX) + Math.Abs(toChunkX));
			int basePartition = totalLoops / threadsNum;
			int remainder = totalLoops - (basePartition * threadsNum);
		
			int currStartX, currEndX, oldEndX; 
			currStartX = currEndX = oldEndX = fromChunkX;
		
			int extraWork;
		
			GenPool = new ChunkGeneratorPool(threadsNum, worldSeed);
		
			for(int i = 0; i < threadsNum; ++i)
			{
				extraWork = remainder > 0 ? 1 : 0;
				--remainder;
			
				currStartX = oldEndX;
				currEndX += basePartition + extraWork;
				oldEndX = currEndX;
			
				genWait[i] = new ManualResetEvent(false);

				ChunkGenThreadEntry chunkGenEntry = new ChunkGenThreadEntry(currStartX, currEndX, GenPool, (chunkGen, x) => {
					int xComponent = (x + Math.Abs(fromChunkX)) * (toChunkX - fromChunkX);
					for(int z = fromChunkX; z < toChunkX; ++z)
					{
						ushort index = WorldBehaviour.ChunkIndexFromCoords(x, z);
						Chunk chunk = new Chunk(x,z, worldManager, (z + x) % 2 == 0 ? Color.black : Color.gray);
						chunkGen.GenerateChunk(chunk, x, z);
				
						int entryIndex = xComponent + (z + Math.Abs(fromChunkX));
						chunkEntries[entryIndex] = new ChunkMeshThreadEntry(chunk);
						WorldBehaviour.ChunksMap[index] = chunk;
					}
				});
			
				ThreadPool.QueueUserWorkItem(new WaitCallback(chunkGenEntry.ThreadCallback), genWait[i]);
			}
		
			WaitHandle.WaitAll(genWait);
		}
	}
}
