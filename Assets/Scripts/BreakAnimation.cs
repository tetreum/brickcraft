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

        // progress goes from 0 (just started) to 1 (broken)
        public void setProgress (float progress) {
            currentFrame = Mathf.Clamp(Mathf.FloorToInt(progress * frames.Length), 0, frames.Length - 1);
            decalProjector.uvBias = new Vector2(frames[currentFrame], 0);
        }

        // how far in front of the face the projection starts, so it reaches the top of the studs
        private const float OutsideDepth = Server.plateHeight;
        // and how far behind it goes, so the face is covered even if it's a bit uneven
        private const float InsideDepth = 0.02f;
        // surfaces turned further than this from the projector (stud sides, back faces) don't get cracks
        private const float StartAngleFade = 60f;
        private const float EndAngleFade = 85f;

        /// <summary>Covers the face of <paramref name="target"/> (world bounds of the brick or block) that <paramref name="normal"/> points out of.</summary>
        public void showAt (Bounds target, Vector3 normal) {
            if (prefab == null) {
                prefab = Game.Instantiate(Game.Instance.breakAnimationPrefab, Vector3.zero, Quaternion.identity);
                decalProjector = prefab.GetComponent<DecalProjector>();
                decalProjector.startAngleFade = StartAngleFade;
                decalProjector.endAngleFade = EndAngleFade;
            }

            Vector3 axis = dominantAxis(normal);
            // project into the face; side faces keep the image upright
            Vector3 up = axis.y != 0 ? Vector3.forward : Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(-axis, up);
            Vector3 right = rotation * Vector3.right;

            Vector3 faceCenter = target.center + Vector3.Scale(axis, target.extents);
            float depth = OutsideDepth + InsideDepth;

            prefab.transform.SetPositionAndRotation(faceCenter + axis * OutsideDepth, rotation);
            decalProjector.size = new Vector3(
                Mathf.Abs(Vector3.Dot(target.size, right)),
                Mathf.Abs(Vector3.Dot(target.size, up)),
                depth);
            decalProjector.pivot = new Vector3(0, 0, depth / 2f);

            reset();
            prefab.SetActive(true);
        }

        private static Vector3 dominantAxis (Vector3 v) {
            Vector3 abs = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

            if (abs.y >= abs.x && abs.y >= abs.z) {
                return new Vector3(0, Mathf.Sign(v.y), 0);
            }
            if (abs.x >= abs.z) {
                return new Vector3(Mathf.Sign(v.x), 0, 0);
            }
            return new Vector3(0, 0, Mathf.Sign(v.z));
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