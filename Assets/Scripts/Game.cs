using Brickcraft.Bricks;
using UnityEngine;

namespace Brickcraft
{
    public class Game : MonoBehaviour
    {
        public static Game Instance;
        public static BreakAnimation breakAnimation;

        public Material transparentMaterial;
        public GameObject breakAnimationPrefab;

        public Material[] brickMaterials;

        public enum Layers
        {
            Default = 0,
            IgnoreRaycast = 2,
            Water = 4
        }

        private void Awake() {
            Instance = this;

            breakAnimation = new BreakAnimation();
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
