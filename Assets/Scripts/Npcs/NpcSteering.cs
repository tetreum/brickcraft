using System.Collections.Generic;
using Brickcraft.Combat;
using Mirror;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// Keeps NPCs from walking into each other and into players, on the server: every so often each NPC
    /// that moves gets a velocity close to the one it wants (towards where it's going) that doesn't run
    /// into anyone soon. NPCs share the dodging (each moves half out of the other's way, reciprocal velocity
    /// obstacles, sampled), players are dodged without counting on them. Worked out for all NPCs at once on
    /// worker threads (Burst), the answer used the next frames (Npc.move).
    /// </summary>
    public static class NpcSteering
    {
        private const float Interval = 0.1f;
        /// <summary>Who's further than this isn't looked at.</summary>
        private const float NeighbourRange = 6;

        private struct Agent
        {
            public float2 position;
            public float2 velocity;
            public float2 preferred;
            public float radius;
            public float maxSpeed;
            public bool steers;      // an NPC: it gets a velocity
            public bool reciprocal;  // an NPC: it dodges too
        }

        [BurstCompile]
        private struct AvoidanceJob : IJobParallelFor
        {
            [Unity.Collections.ReadOnly] public NativeArray<Agent> agents;
            [WriteOnly] public NativeArray<float2> velocities;
            public float neighbourRange;

            private const int Directions = 24;
            // how much a collision soon weighs against going another way
            private const float CollisionWeight = 2.5f;

            public void Execute(int index) {
                Agent self = agents[index];
                if (!self.steers) {
                    velocities[index] = self.velocity;
                    return;
                }
                float2 best = self.preferred;
                float bestPenalty = float.MaxValue;
                // what it wants, standing still, and all around at full and half speed
                for (int c = -2; c < Directions * 2; c++) {
                    float2 candidate;
                    if (c == -2) {
                        candidate = self.preferred;
                    } else if (c == -1) {
                        candidate = float2.zero;
                    } else {
                        float angle = (c % Directions) * (2 * math.PI / Directions);
                        float speed = c < Directions ? self.maxSpeed : self.maxSpeed * 0.5f;
                        candidate = new float2(math.cos(angle), math.sin(angle)) * speed;
                    }
                    float soonest = float.MaxValue;
                    for (int j = 0; j < agents.Length; j++) {
                        if (j == index) {
                            continue;
                        }
                        Agent other = agents[j];
                        float2 offset = other.position - self.position;
                        if (math.lengthsq(offset) > neighbourRange * neighbourRange) {
                            continue;
                        }
                        // the other dodges half when it's an NPC
                        float2 relative = other.reciprocal ? 2 * candidate - self.velocity - other.velocity : candidate - other.velocity;
                        soonest = math.min(soonest, timeToCollision(offset, relative, self.radius + other.radius + 0.05f));
                    }
                    float penalty = CollisionWeight / math.max(soonest, 0.01f) + math.distance(candidate, self.preferred);
                    if (penalty < bestPenalty) {
                        bestPenalty = penalty;
                        best = candidate;
                    }
                }
                velocities[index] = best;
            }

            // when something at offset, with that velocity relative to it, comes within radius (0: it already is)
            private static float timeToCollision(float2 offset, float2 relative, float radius) {
                float c = math.lengthsq(offset) - radius * radius;
                if (c < 0) {
                    // already touching: only getting further away is fine
                    return math.dot(offset, relative) > 0 ? 0 : float.MaxValue;
                }
                float a = math.lengthsq(relative);
                float b = math.dot(offset, relative);
                float discriminant = b * b - a * c;
                if (a < 1e-6f || b <= 0 || discriminant < 0) {
                    return float.MaxValue; // never
                }
                return (b - math.sqrt(discriminant)) / a;
            }
        }

        private static readonly List<Npc> steered = new List<Npc>();
        private static NativeArray<Agent> agents;
        private static NativeArray<float2> velocities;
        private static JobHandle handle;
        private static bool isRunning;
        private static float nextRun;
        private static readonly Dictionary<NetworkConnectionToClient, Vector3> playerPositions = new Dictionary<NetworkConnectionToClient, Vector3>();
        private static float lastRun;

        public static void Update() {
            if (isRunning) {
                finish();
            }
            if (Time.time >= nextRun) {
                nextRun = Time.time + Interval;
                start();
            }
        }

        public static void Clear() {
            nextRun = 0; // Time.time starts over in a new play mode
            handle.Complete();
            dispose();
            steered.Clear();
            playerPositions.Clear();
        }

        private static void start() {
            List<Agent> list = new List<Agent>();
            steered.Clear();
            foreach (Npc npc in NpcSystem.All) {
                if (npc.Info == null || npc.Health.isDead || !NpcSystem.IsActive(npc.transform.position)) {
                    continue;
                }
                steered.Add(npc);
                list.Add(new Agent() {
                    position = flat(npc.transform.position),
                    velocity = flat(npc.Velocity),
                    preferred = flat(npc.PreferredVelocity),
                    radius = npc.Radius,
                    maxSpeed = npc.Info.speed,
                    steers = true,
                    reciprocal = true,
                });
            }
            if (steered.Count == 0) {
                return;
            }
            // players: in the way, moving as they did since the last time
            float elapsed = Mathf.Max(Time.time - lastRun, 0.01f);
            lastRun = Time.time;
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn.identity == null || !conn.identity.TryGetComponent(out Health player) || player.isDead) {
                    continue;
                }
                Vector3 position = conn.identity.transform.position;
                Vector3 velocity = playerPositions.TryGetValue(conn, out Vector3 last) ? (position - last) / elapsed : Vector3.zero;
                playerPositions[conn] = position;
                list.Add(new Agent() { position = flat(position), velocity = flat(velocity), radius = 0.35f });
            }

            agents = new NativeArray<Agent>(list.Count, Allocator.TempJob);
            for (int i = 0; i < list.Count; i++) {
                agents[i] = list[i];
            }
            velocities = new NativeArray<float2>(list.Count, Allocator.TempJob);
            handle = new AvoidanceJob() { agents = agents, velocities = velocities, neighbourRange = NeighbourRange }.Schedule(list.Count, 8);
            JobHandle.ScheduleBatchedJobs();
            isRunning = true;
        }

        private static void finish() {
            handle.Complete();
            for (int i = 0; i < steered.Count; i++) {
                if (steered[i] != null) {
                    float2 v = velocities[i];
                    steered[i].SetSteering(new Vector3(v.x, 0, v.y));
                }
            }
            dispose();
        }

        private static void dispose() {
            if (agents.IsCreated) {
                agents.Dispose();
            }
            if (velocities.IsCreated) {
                velocities.Dispose();
            }
            isRunning = false;
        }

        private static float2 flat(Vector3 v) {
            return new float2(v.x, v.z);
        }
    }
}
