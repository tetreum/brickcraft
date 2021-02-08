// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	public class BlockEntry
	{
		public int X;
		public int Y;
		public int Z;
	
		public Chunk Chunk;
	
		public BlockEntry(int x, int y, int z, Chunk chunk)
		{
			X = x;
			Y = y;
			Z = z;
			Chunk = chunk;
		}
	}
}
