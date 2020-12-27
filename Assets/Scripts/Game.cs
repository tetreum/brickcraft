using UnityEngine;

namespace Brickcraft
{
    public class Game : MonoBehaviour
    {
        public static Game Instance;

        public Shader transparentShader;
        public enum Layers
        {
            IgnoreRaycast = 2
        }

        private void Awake() {
            Instance = this;
        }
    }
}
