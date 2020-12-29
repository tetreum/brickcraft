using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Brickcraft.Bricks { 
    public class BreakAnimation
    {
        float[] frames = new float[] {
            1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f
        };

        private GameObject prefab;
        public DecalProjector decalProjector;

        private int currentFrame = 0;

        public void advance () {
            decalProjector.uvBias = new Vector2(frames[currentFrame], 0);
            currentFrame++;
        }

        public void showAt (Vector3 position, Quaternion rotation) {
            if (prefab == null) {
                prefab = Game.Instantiate(Game.Instance.breakAnimationPrefab, Vector3.zero, Quaternion.identity);
                decalProjector = prefab.GetComponent<DecalProjector>();
            }
            prefab.transform.position = position;
            prefab.transform.rotation = rotation;
            reset();
            prefab.SetActive(true);
        }

        public void hide () {
            prefab.SetActive(false);
        }

        public void reset () {
            currentFrame = 0;
            advance();
            currentFrame = 0;
        }
    }
}