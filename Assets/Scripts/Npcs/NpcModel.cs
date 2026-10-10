using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// The root of an NPC's model (Assets/Resources/NpcModels/[model].prefab, made by Brickcraft > Import NPC
    /// model): its Animator has a state per animation, played by name (see NpcInfo.animations).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class NpcModel : MonoBehaviour
    {
        /// <summary>Where it can be hit, in the model's space: its "hitbox" node, or else all its meshes.</summary>
        public Bounds hitbox;
        /// <summary>Its animations (states of its Animator), and how long each one lasts in seconds.</summary>
        public string[] animations;
        public float[] durations;

        public float DurationOf(string animation) {
            int index = System.Array.IndexOf(animations, animation);
            return index >= 0 ? durations[index] : 0;
        }
    }
}
