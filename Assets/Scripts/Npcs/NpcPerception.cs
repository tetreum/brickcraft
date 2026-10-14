using System.Collections.Generic;
using Brickcraft.Combat;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>A noise NPCs may hear: where, how far it carries, who made it (null for nobody) and what it was.</summary>
    public struct Noise
    {
        public Vector3 position;
        public float radius;
        public Health source;
        public string kind;
        public double time;

        public const string Steps = "steps";
        public const string Sprint = "sprint";
        public const string Dig = "dig";
        public const string Place = "place";
        public const string Fight = "fight";
        public const string Door = "door";
    }

    /// <summary>
    /// What NPCs perceive, on the server: noises (MakeNoise: players' footsteps, digging, placing, fights,
    /// doors), and whether one can see another (CanSee: lines of sight from its eyes to their middle, cast
    /// in one batch per frame on worker threads with RaycastCommand, the answer comes the next frame).
    /// </summary>
    public static class NpcPerception
    {
        // how long noises stay to be heard, and lines of sight are trusted
        private const float NoiseTime = 1;
        private const float SightTime = 0.6f;
        // walls, the ground, bricks, other NPCs (water doesn't hide)
        private const int SightLayers = Physics.DefaultRaycastLayers & ~(1 << (int)Game.Layers.Water);

        private static readonly List<Noise> noises = new List<Noise>();

        private struct Pair
        {
            public Npc npc;
            public Health target;
        }

        private struct Sight
        {
            public bool visible;
            public float time;
        }

        private static readonly Dictionary<Pair, Sight> sights = new Dictionary<Pair, Sight>();
        private static readonly List<Pair> queued = new List<Pair>();
        private static readonly HashSet<Pair> queuedSet = new HashSet<Pair>();
        private static List<Pair> casting = new List<Pair>();
        private static NativeArray<RaycastCommand> commands;
        private static NativeArray<RaycastHit> hits;
        private static JobHandle casts;
        private static bool isCasting;

        public static IReadOnlyList<Noise> Noises {
            get { return noises; }
        }

        /// <summary>A noise at a local position, heard as far as radius by NPCs that hear that far.</summary>
        public static void MakeNoise(Vector3 position, float radius, Health source, string kind) {
            noises.Add(new Noise() { position = position, radius = radius, source = source, kind = kind, time = Time.timeAsDouble });
        }

        /// <summary>
        /// Whether the NPC sees the target from its eyes, as found the last frames: asking keeps it checked.
        /// Range and field of view are up to the asker.
        /// </summary>
        public static bool CanSee(Npc npc, Health target) {
            Pair pair = new Pair() { npc = npc, target = target };
            if (queuedSet.Add(pair)) {
                queued.Add(pair);
            }
            return sights.TryGetValue(pair, out Sight sight) && sight.visible && Time.time - sight.time < SightTime;
        }

        /// <summary>The middle of something with health (where it's looked at and aimed at).</summary>
        public static Vector3 CenterOf(Health target) {
            if (target.TryGetComponent(out CharacterController controller)) {
                return target.transform.TransformPoint(controller.center);
            }
            if (target.TryGetComponent(out CapsuleCollider capsule)) {
                return target.transform.TransformPoint(capsule.center);
            }
            return target.transform.position + Vector3.up;
        }

        public static void Update() {
            finishCasts();
            noises.RemoveAll(n => Time.timeAsDouble - n.time > NoiseTime);
            startCasts();
        }

        public static void Clear() {
            casts.Complete();
            disposeCasts();
            noises.Clear();
            sights.Clear();
            queued.Clear();
            queuedSet.Clear();
            casting.Clear();
        }

        private static void finishCasts() {
            if (!isCasting) {
                return;
            }
            casts.Complete();
            float now = Time.time;
            for (int i = 0; i < casting.Count; i++) {
                Pair pair = casting[i];
                if (pair.npc == null || pair.target == null) {
                    continue;
                }
                RaycastHit hit = hits[i];
                // nothing in the way, or what's in the way is the target itself
                bool visible = hit.colliderInstanceID == 0 || (hit.collider != null && hit.collider.GetComponentInParent<Health>() == pair.target);
                sights[pair] = new Sight() { visible = visible, time = now };
            }
            disposeCasts();
            // forget pairs nobody asked about for a while (dead NPCs, players gone)
            List<Pair> stale = null;
            foreach (KeyValuePair<Pair, Sight> sight in sights) {
                if (now - sight.Value.time > SightTime * 4 || sight.Key.npc == null || sight.Key.target == null) {
                    (stale = stale ?? new List<Pair>()).Add(sight.Key);
                }
            }
            if (stale != null) {
                foreach (Pair pair in stale) {
                    sights.Remove(pair);
                }
            }
        }

        private static void startCasts() {
            if (queued.Count == 0) {
                return;
            }
            commands = new NativeArray<RaycastCommand>(queued.Count, Allocator.TempJob);
            hits = new NativeArray<RaycastHit>(queued.Count, Allocator.TempJob);
            QueryParameters parameters = new QueryParameters(SightLayers, false, QueryTriggerInteraction.Ignore, false);
            for (int i = 0; i < queued.Count; i++) {
                Pair pair = queued[i];
                Vector3 from = pair.npc != null ? pair.npc.EyePosition : Vector3.zero;
                Vector3 to = pair.target != null ? CenterOf(pair.target) : from;
                Vector3 direction = to - from;
                float distance = direction.magnitude;
                commands[i] = new RaycastCommand(from, distance > 0 ? direction / distance : Vector3.forward, parameters, distance);
            }
            casting = new List<Pair>(queued);
            queued.Clear();
            queuedSet.Clear();
            casts = RaycastCommand.ScheduleBatch(commands, hits, 16);
            JobHandle.ScheduleBatchedJobs();
            isCasting = true;
        }

        private static void disposeCasts() {
            if (commands.IsCreated) {
                commands.Dispose();
            }
            if (hits.IsCreated) {
                hits.Dispose();
            }
            isCasting = false;
        }
    }
}
