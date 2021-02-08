using UnityEngine;

// Highly based on https://github.com/chraft/chunk-light-tester
namespace Brickcraft.World
{
	public class ColorBehaviour : MonoBehaviour {

		void Start () {
			MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
			renderer.material.color = Color.cyan;
		}
	}
}
