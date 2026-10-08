using UnityEngine;
using Chraft.Utilities.Misc;
	
namespace Brickcraft.World
{
	public class ChunkSlice
	{
		public int Index;
		public static readonly int SizeX = 16;
		public static readonly int SizeY = Chunk.SliceHeight;
		public static readonly int SizeZ = 16;
		
		public static readonly short TotalBlockNumber = (short)(SizeX * SizeY * SizeZ);
		
		private byte[] _Types = new byte[SizeX * SizeY * SizeZ];
		private NibbleArray _Skylight = new NibbleArray(TotalBlockNumber / 2);
		
		private short solidBlocks;
		
		private bool dirtyLight;
		
		public Chunk ParentChunk;

		public static ChunkRenderer chunkRenderer = new ChunkRenderer();
		public MeshRenderer renderer;
		public Bounds bounds;

		public bool IsEmpty {
			get {return solidBlocks == 0; }
		}
		
		public ChunkSlice(int index, Chunk parent)
		{
			Index = index;
			solidBlocks = 0;
			ParentChunk = parent;
		}
		
		public BlockType GetBlockType(int x, int y, int z)
		{
			return (BlockType)_Types[y << 8 | z << 4 | x];
		}
		
		public byte this[int x, int y, int z]
		{
			get{
				if(x > 15 || x < 0 || y > Chunk.SliceHeightLimit || y < 0 || z > 15 || z < 0)
					return 0;
				
				return _Types[y << 8 | z << 4 | x];
			}
			set{
				byte oldType = _Types[y << 8 | z << 4 | x];
				if(oldType != value)
				{
					_Types[y << 8 | z << 4 | x] = value;
					if(value != 0)
						++solidBlocks;
					else
						--solidBlocks;
				}
			
			}
		}
		
		public bool IsDirtyLight()
		{
			return dirtyLight;
		}
		
		public bool IsProcessingLight()
		{
			return ParentChunk.ProcessingLight;
		}
		
		public void SetDirtyLight()
		{
			dirtyLight = true;
		}
		
		public void ClearDirtyLight()
		{
			dirtyLight = false;
		}
		
		public void SetSkylight(int bx, int by, int bz, byte light)
		{
			_Skylight.setNibble(by << 8 | bz << 4 | bx, light);
		}
		
		public byte GetSkylight(int bx, int by, int bz)
		{
			return (byte)_Skylight.getNibble(by << 8 | bz << 4 | bx);
		}
		// bounds relative to the slice, it moves when the floating origin does
		public void CreateBoundsBox(Transform transform) {
			Vector3 size = new Vector3(16 * Server.brickWidth, Chunk.SliceHeight * Server.brickHeight, 16 * Server.brickWidth);
			Vector3 center = new Vector3(size.x / 2 - Server.brickWidth / 2, size.y / 2, size.z / 2 - Server.brickWidth / 2);

			bounds = new Bounds(center, size + Vector3.one * Server.brickWidth);
		}

		public void FrustrumCulling() {
			if (!Camera.main) {
				return;
			}
			Bounds worldBounds = new Bounds(bounds.center + renderer.transform.position, bounds.size);
			var visible = Vector3.Distance(worldBounds.center, Camera.main.transform.position) <= (10 * Chunk.SliceHeight);

            if (visible) {
				Plane[] planes = GeometryUtility.CalculateFrustumPlanes(Camera.main);
				visible = GeometryUtility.TestPlanesAABB(planes, worldBounds);
			}

			renderer.enabled = visible;
		}
	}
}
