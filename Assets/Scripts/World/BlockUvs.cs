using UnityEngine;

// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	public class BlockUVs
	{
		public static Rect GetUVFromTypeAndFace(BlockType type, BlockFace face)
		{
			Rect result;
			switch(type)
			{
				case BlockType.Grass:
				{
					switch(face)
					{
						case BlockFace.Top:
						{
							result = new Rect(16, 48, 16, 16);
							break;
						}
						case BlockFace.Side:
						{
							result = new Rect(32, 48, 16, 16);
							break;
						}
						case BlockFace.Bottom:
						{
							result = new Rect(16, 48, 16, 16);
							break;
						}
						default:
						{
							result = new Rect(0, 0, 16, 16);
							break;
						}
					}
					break;
				}
				case BlockType.Stone:
				{
					switch(face)
					{
						case BlockFace.Top:
						case BlockFace.Side:
						case BlockFace.Bottom:
						{
							result = new Rect(0, 48, 16, 16);
							break;
						}
						default:
						{
							result = new Rect(0, 0, 16, 16);
							break;
						}
					}
					break;
				}
				case BlockType.Wood:
				{
					switch(face)
					{
						case BlockFace.Top:
						{
							result = new Rect(16, 32, 16, 16);
							break;
						}
						case BlockFace.Side:
						{
							result = new Rect(0, 32, 16, 16);
							break;
						}
						case BlockFace.Bottom:
						{
							result = new Rect(16, 32, 16, 16);
							break;
						}
						default:
						{
							result = new Rect(0, 0, 16, 16);
							break;
						}
					}
					break;
				}
				case BlockType.Leaves:
				{
					switch(face)
					{
						case BlockFace.Top:
						{
							result = new Rect(32, 32, 16, 16);
							break;
						}
						case BlockFace.Side:
						{
							result = new Rect(32, 32, 16, 16);
							break;
						}
						case BlockFace.Bottom:
						{
							result = new Rect(32, 32, 16, 16);
							break;
						}
						default:
						{
							result = new Rect(0, 0, 16, 16);
							break;
						}
					}
					break;
				}
				case BlockType.Sand:
				{
					switch(face)
					{
						case BlockFace.Top:
						case BlockFace.Side:
						case BlockFace.Bottom:
						{
							result = new Rect(48, 32, 16, 16);
							break;
						}
						default:
						{
							result = new Rect(0, 0, 16, 16);
							break;
						}
					}
					break;
				}
				case BlockType.Dirt:
				{
					switch(face)
					{
						case BlockFace.Top:
						case BlockFace.Side:
						case BlockFace.Bottom:
						{
							result = new Rect(48, 48, 16, 16);
							break;
						}
						default:
						{
							result = new Rect(0, 0, 16, 16);
							break;
						}
					}
					break;
				}
				case BlockType.Snow:
				{
					switch(face)
					{
						case BlockFace.Top:
						case BlockFace.Bottom:
						{
							result = new Rect(0, 16, 16, 16);
							break;
						}
						case BlockFace.Side:
						{
							result = new Rect(16, 16, 16, 16);
							break;
						}
					
						default:
						{
							result = new Rect(0, 0, 16, 16);
							break;
						}
					}
					break;
				}
				case BlockType.Water:
				case BlockType.Still_Water:
				{
					switch(face)
					{
						case BlockFace.Top:
						case BlockFace.Side:
						case BlockFace.Bottom:
						{
							result = new Rect(32, 16, 16, 16);
							break;
						}
						default:
						{
							result = new Rect(0, 0, 16, 16);
							break;
						}
					}
					break;
				}
				case BlockType.Ice:
				{
					switch(face)
					{
						case BlockFace.Top:
						case BlockFace.Side:
						case BlockFace.Bottom:
						{
							result = new Rect(48, 16, 16, 16);
							break;
						}
						default:
						{
							result = new Rect(0, 0, 16, 16);
							break;
						}
					}
					break;
				}
				default:
				{
					result = new Rect(0, 0, 16, 16);
					break;
				}
			
			}
		
			return (result = new Rect(result.x / 64.0f, result.y / 64.0f, result.width / 64.0f, result.height / 64.0f));
		}
	}
}
