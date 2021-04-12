using UnityEngine;
using System;
using System.Collections.Generic;

// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	public class Chunk
	{
		public static readonly int SliceHeight = 16;
	
		public static readonly int NumSlices = 256 / SliceHeight;
		public static readonly int MaxSliceIndex = NumSlices - 1;
		public static readonly int SliceHeightLimit = SliceHeight - 1;
		public ChunkSlice[] Slices = new ChunkSlice[NumSlices];
		public GameObject ChunkObject;
		public WorldBehaviour World;
		public int X;
		public int Z;
		public Color ChunkColor;
		public int MinSliceIndex;
		public int LowestY;
	
		public bool ProcessingLight{
			get;
			private set;
		}
	
		public GameObject[] ChunkSliceObjects = new GameObject[256 / Chunk.SliceHeight];
	
		public byte [,] HeightMap;
	
		public Chunk(int chunkX, int chunkZ, WorldBehaviour world, Color color)
		{
			ChunkColor = color;
		
			X = chunkX;
			Z = chunkZ;
			World = world;
		
			for(int i = 0; i < NumSlices; ++i)
				Slices[i] = new ChunkSlice(i, this);
		}
	
		public void InitGameObject()
		{
			ChunkObject = new GameObject(String.Format("X {0} Z {1}", X, Z));
		
			ChunkObject.transform.position = new Vector3(X * 16, 0, Z * 16);
		}
	
		public void InitRenderableSlices()
		{
			for(int i = 0; i < NumSlices; ++i)
			{
				ChunkSlice slice = Slices[i]; 
				if(!slice.IsEmpty)
				{
					slice.ClearDirtyLight();
					GameObject newObject = new GameObject("ChunkSlice#" + i);
					ChunkSliceObjects[i] = newObject;			
					MeshRenderer meshRenderer = newObject.AddComponent(typeof(MeshRenderer)) as MeshRenderer;
					meshRenderer.material = WorldBehaviour.BlockMaterial;
					meshRenderer.material.mainTexture = WorldBehaviour.AtlasTexture;
					meshRenderer.sharedMaterial = WorldBehaviour.BlockMaterial;
					newObject.AddComponent(typeof(MeshFilter));
					newObject.AddComponent<MeshCollider>();

					Vector3 pos = new Vector3(ChunkObject.transform.position.x, ChunkObject.transform.position.y + (i * Chunk.SliceHeight), ChunkObject.transform.position.z);
					if (WorldBehaviour.mode == 3) {
						pos.x = pos.x * Server.brickWidth;
						pos.y = pos.y * Server.brickHeight;
						pos.z = pos.z * Server.brickWidth;
					}
					newObject.transform.position = pos;
					newObject.transform.parent = ChunkObject.transform;

					slice.renderer = meshRenderer;
					slice.CreateBoundsBox(newObject.transform);
				}
			}
		}
	
		private int Depth;
		private const int MaxRecursionDepth = 100;
		public Queue<BlockEntry> BlockToRecalculate = new Queue<BlockEntry>();
	
		public byte ChooseAndReturnNewLight(byte x, byte y, byte z, byte[] skylights, byte[] heights)
		{
			byte light = skylights[0];
			if(x < 15)
			{
				skylights[1] = GetSkylight(x+1, y, z);
				heights[1] = HeightMap[x+1,z];
			}
			else if((X << 4) + 16 <= WorldBehaviour.MapMaxCoords)
			{
				Chunk chunk = World.GetChunk(X + 1, Z);
				skylights[1] = chunk.GetSkylight(0,y,z);
				heights[1] = chunk.HeightMap[0,z];
			}
		
			if(light < (skylights[1] - 1))
				light = (byte)(skylights[1] - 1);
		
			if(x > 0)
			{
				skylights[2] = GetSkylight(x-1, y, z);
				heights[2] = HeightMap[x-1,z];
			}
			else if((X << 4) - 1 >= WorldBehaviour.MapMinCoords)
			{
				Chunk chunk = World.GetChunk(X - 1, Z);
				skylights[2] = chunk.GetSkylight(15,y,z);
				heights[2] = chunk.HeightMap[15,z];
			}
		
			if(light < (skylights[2] - 1))
				light = (byte)(skylights[2] - 1);
		
			if(z < 15)
			{
				skylights[3] = GetSkylight(x, y, z+1);
				heights[3] = HeightMap[x,z+1];
			}
			else if((Z << 4) + 16 <= WorldBehaviour.MapMaxCoords)
			{
				Chunk chunk = World.GetChunk(X, Z + 1);
				skylights[3] = chunk.GetSkylight(x,y,0);
				heights[3] = chunk.HeightMap[x,0];
			}
		
			if(light < (skylights[3] - 1))
				light = (byte)(skylights[3] - 1);
		
			if(z > 0)
			{
				skylights[4] = GetSkylight(x, y, z-1);
				heights[4] = HeightMap[x,z-1];
			}
			else if((Z << 4) - 1 >= WorldBehaviour.MapMinCoords)
			{
				Chunk chunk = World.GetChunk(X, Z - 1);
				skylights[4] = chunk.GetSkylight(x,y,15);
				heights[4] = chunk.HeightMap[x,15];
			}
		
			if(light < (skylights[4] - 1))
				light = (byte)(skylights[4] - 1);
		
			if(y < 255)
				skylights[5] = GetSkylight(x,y+1,z);
			else
				skylights[5] = 15;
		
			// We prefer vertical light because it doesn't diminish it's power
			if(light < skylights[5])
				light = skylights[5];
		
			if(y > 0)
				skylights[6] = GetSkylight(x,y-1,z);
			else
				skylights[6] = 6;
		
			if(light < skylights[6])
				light = skylights[6];
		
			return light;
		}
		public byte GetSkylight(int x, int y, int z)
		{
			ChunkSlice slice = Slices[y / Chunk.SliceHeight];
			if(slice == null)
				return 0;
		
			return slice.GetSkylight(x, y & SliceHeightLimit, z);
		}
	
		public void SetSkylight(int x, int y, int z, byte newLight)
		{
			ChunkSlice slice = Slices[y / Chunk.SliceHeight];
			if(slice == null)
				return;
		
			slice.SetSkylight(x, y & SliceHeightLimit, z, newLight);
		}
	
		public BlockType GetBlockType(int x, int y, int z)
		{
			ChunkSlice slice = Slices[y / Chunk.SliceHeight];
		
			if(slice == null)
				return 0;
			return (BlockType)slice[x, y & Chunk.SliceHeightLimit, z];
		}
	
		public void SetType(int x, int y, int z, BlockType type, bool unused)
		{
			ChunkSlice slice = Slices[y / Chunk.SliceHeight];
			slice[x & 0xF, y & Chunk.SliceHeightLimit, z & 0xF] = (byte)type;
		}
	
		public void SetData(int x, int y, int z, byte data, bool unused)
		{
		
		}
	
		public void RecalculateHeight()
		{
			LowestY = 255;
			HeightMap = new byte[16, 16];
			for (int x = 0; x < 16; x++)
			{
				for (int z = 0; z < 16; z++)
					RecalculateHeight(x, z);
			}
		
			MinSliceIndex = (LowestY / Chunk.SliceHeight) - 1;
		}

		public void RecalculateHeight(int x, int z)
		{
			int height;
			BlockType blockType;
			for (height = 127; height > 0 && (GetBlockType(x, height - 1, z) == 0 || (blockType = GetBlockType(x, height - 1, z)) == BlockType.Leaves || blockType == BlockType.Water || blockType == BlockType.Still_Water); height--) ;
			HeightMap[x, z] = (byte)height;

			if (height < LowestY)
				LowestY = height;
		}
	
		public void ClearDirtySlices()
		{
			for(int i = 0; i < NumSlices; ++i)
			{
				ChunkSlice slice = Slices[i];
				if(slice != null && !slice.IsEmpty)
					slice.ClearDirtyLight();
			}
		}
		public static int CorrectBlockCoordinate(int axis) {
			return axis >= 0 ? axis : (axis + Chunk.SliceHeight);
		}
	}
}
