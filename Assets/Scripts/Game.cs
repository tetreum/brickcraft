using UnityEngine;

namespace Brickcraft
{
    public class Game : MonoBehaviour
    {
        public static Game Instance;

        public Shader transparentShader;
        public Texture2D redTexture; // used to display invalid/colliding Brick previews

        public Material[] brickMaterials;

        public enum Layers
        {
            IgnoreRaycast = 2
        }

        private void Awake() {
            Instance = this;
        }

        private void Start() {
            lockMouse();
        }

        private void OnDestroy() {
            unlockMouse(); // unity editor is buggy and keeps mouse locked even after stopping the game...
        }

        public Material getBrickMaterial (string name) {
            foreach (Material material in brickMaterials) {
                if (material.name == name) {
                    return material;
                }
            }
            return null;
        }

        public static void lockMouse () {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public static void unlockMouse () {
            Cursor.lockState = CursorLockMode.None;
        }
    }
}
