using UnityEngine;

// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	public class BlockHighliter {
	
		private GameObject primitiveCube;
	
		private Vector3 position;
	
		private readonly Vector3 pivotOffset = new Vector3(0.5f, 0.5f, 0.5f);
	
		public BlockHighliter(float x, float y, float z)
		{
			position = new Vector3(x,y,z);
			primitiveCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Vector3 cubePosition = position + pivotOffset;
			primitiveCube.transform.position = cubePosition;
			primitiveCube.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);
		
			MeshRenderer renderer = primitiveCube.GetComponent<MeshRenderer>();
			renderer.material = (Material)Resources.Load ("Materials/LightSelector", typeof(Material));
		}
	
		public void SetPosition(float x, float y, float z)
		{
			Vector3 newPosition = new Vector3(x,y,z);
			position = newPosition;
			newPosition += pivotOffset;
			primitiveCube.transform.position = newPosition;
		}
	}
}
