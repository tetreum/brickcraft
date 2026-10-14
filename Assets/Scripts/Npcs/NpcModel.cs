using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// The root of an NPC's model: a prefab of Assets/Resources/NpcModels made by Brickcraft > Import NPC model
    /// (its Animator has a state per animation) or of a mod's AssetBundle, or built from a glTF model in the
    /// NPC's folder while the game runs (an Animation component with a clip per animation), see NpcDatabase
    /// and NpcModelBuilder. Animations are played by name (see NpcInfo.animations).
    /// </summary>
    public class NpcModel : MonoBehaviour
    {
        /// <summary>Where it can be hit, in the model's space: its "hitbox" node, or else all its meshes.</summary>
        public Bounds hitbox;
        /// <summary>Its animations, and how long each one lasts in seconds.</summary>
        public string[] animations;
        public float[] durations;

        private Animator animator;
        private Animation clips;
        private bool isSetUp;

        public float DurationOf(string animation) {
            int index = System.Array.IndexOf(animations, animation);
            return index >= 0 ? durations[index] : 0;
        }

        public bool Has(string animation) {
            return System.Array.IndexOf(animations, animation) >= 0;
        }

        /// <summary>Blends into the animation in fade seconds.</summary>
        public void Play(string animation, float fade) {
            if (!isSetUp) {
                isSetUp = true;
                animator = GetComponent<Animator>();
                clips = GetComponent<Animation>();
            }
            if (!Has(animation)) {
                return;
            }
            if (animator != null && animator.runtimeAnimatorController != null) {
                animator.CrossFadeInFixedTime(animation, fade, 0, 0);
            } else if (clips != null && clips[animation] != null) {
                // from its start, like the Animator's (an attack again)
                if (clips[animation].wrapMode != WrapMode.Loop) {
                    clips[animation].time = 0;
                }
                clips.CrossFade(animation, fade);
            }
        }
    }
}
