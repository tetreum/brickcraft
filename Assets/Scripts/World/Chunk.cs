using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading;

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

		// level of detail: near the camera blocks are drawn with their studs, far away with a simple box
		public bool IsDetailed;
		public volatile bool IsRemeshing;

		// bumped by every block change, so meshes computed from older data are thrown away
		public int Version;

		public enum ChunkState { Generating, Generated, Unloaded }

		/// <summary>Set by the main thread; block data can only be read once Generated.</summary>
		public volatile ChunkState State = ChunkState.Generating;

		// meshing: a render job runs, then its slices are uploaded one by one
		public bool HasMesh;
		public volatile bool IsMeshJobDone;
		public int PendingSliceUploads;

		/// <summary>Set once the first mesh is fully uploaded, stays while it's only being remeshed.</summary>
		public bool HasColliders;

		/// <summary>The current mesh job and all its slices are uploaded.</summary>
		public bool IsMeshReady {
			get { return HasMesh && IsMeshJobDone && Volatile.Read(ref PendingSliceUploads) == 0; }
		}
	
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
			if (ChunkObject != null)
				return;

			ChunkObject = new GameObject(String.Format("X {0} Z {1}", X, Z));
			ChunkObject.transform.position = new Vector3(X * 16, 0, Z * 16);
		}

		/// <summary>Destroys the chunk's game objects and meshes, its block data stays.</summary>
		public void DestroyMesh()
		{
			for (int i = 0; i < NumSlices; ++i)
			{
				GameObject sliceObject = ChunkSliceObjects[i];

				if (sliceObject == null)
					continue;

				UnityEngine.Object.Destroy(sliceObject.GetComponent<MeshFilter>().sharedMesh);
				UnityEngine.Object.Destroy(sliceObject.GetComponent<MeshCollider>().sharedMesh);
				UnityEngine.Object.Destroy(sliceObject);
				ChunkSliceObjects[i] = null;
				Slices[i].renderer = null;
			}
			if (ChunkObject != null)
				UnityEngine.Object.Destroy(ChunkObject);

			ChunkObject = null;
			HasMesh = false;
			HasColliders = false;
			IsMeshJobDone = false;
		}
	
		public void InitRenderableSlices()
		{
			for(int i = 0; i < NumSlices; ++i)
			{
				ChunkSlice slice = Slices[i]; 
				if(!slice.IsEmpty)
				{
					slice.ClearDirtyLight();
					GetOrCreateSliceObject(i);
				}
			}
		}

		public GameObject GetOrCreateSliceObject(int i)
		{
			if (ChunkSliceObjects[i] != null)
				return ChunkSliceObjects[i];

			ChunkSlice slice = Slices[i];
			GameObject newObject = new GameObject("ChunkSlice#" + i);
			ChunkSliceObjects[i] = newObject;
			MeshRenderer meshRenderer = newObject.AddComponent<MeshRenderer>();
			meshRenderer.sharedMaterial = WorldBehaviour.BlockMaterial;
			newObject.AddComponent<MeshFilter>();
			newObject.AddComponent<MeshCollider>();

			newObject.transform.position = new Vector3(
				X * 16 * Server.brickWidth,
				i * Chunk.SliceHeight * Server.brickHeight,
				Z * 16 * Server.brickWidth
			);
			newObject.transform.parent = ChunkObject.transform;

			slice.renderer = meshRenderer;
			slice.CreateBoundsBox(newObject.transform);

			return newObject;
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
			for (height = 127; height > 0 && BlockDatabase.Get(GetBlockType(x, height - 1, z)).isTransparent; height--) ;
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
