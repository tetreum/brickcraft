using System;
using System.Collections.Generic;
using Brickcraft.Combat;
using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// NPCs together, on the server.
    ///
    /// Attack slots: the NPCs attacking someone spread around them, each at its own place of a ring (the one
    /// closest to where it comes from), instead of all going for the same spot. Only a few hit at once (they
    /// hold the tokens); the others wait on a ring further out until one is free.
    ///
    /// Packs: NPCs spawned together (or put together by a script) follow their leader in a wedge when they
    /// have nothing to do, and when one of them goes for someone the others near it are told to.
    /// </summary>
    public static class NpcGroups
    {
        /// <summary>NPCs hitting someone at once; the others wait.</summary>
        public const int MaxAttackers = 3;
        private const float WaitingDistance = 2.5f;
        private const float AlertRange = 20;
        private const float FormationSpacing = 1.8f;

        private class Slots
        {
            public readonly List<Npc> attackers = new List<Npc>();      // they hold a token
            public readonly Dictionary<Npc, int> places = new Dictionary<Npc, int>();
            public readonly Dictionary<Npc, int> waiting = new Dictionary<Npc, int>();
        }

        private static readonly Dictionary<Health, Slots> slots = new Dictionary<Health, Slots>();

        /// <summary>A group of NPCs, its first living one leading.</summary>
        public class Pack
        {
            /// <summary>Who it is in saves (members are saved with their chunks, and come back together).</summary>
            public Guid id = Guid.NewGuid();
            public readonly List<Npc> members = new List<Npc>();
            /// <summary>Which way the wedge points: where the leader last walked (it doesn't swing while it turns on the spot).</summary>
            public Vector3 heading = Vector3.forward;

            public Npc Leader {
                get {
                    foreach (Npc member in members) {
                        if (member != null && !member.Health.isDead) {
                            return member;
                        }
                    }
                    return null;
                }
            }
        }

        private static readonly Dictionary<Npc, Pack> packs = new Dictionary<Npc, Pack>();

        public static void Clear() {
            slots.Clear();
            packs.Clear();
        }

        // -------- attack slots --------

        /// <summary>
        /// Where the NPC goes to attack its target: its place on the ring around them; mayAttack false while it
        /// waits for its turn (its place is further out then).
        /// </summary>
        public static Vector3 AttackSlot(Npc npc, Health target, out bool mayAttack) {
            if (!slots.TryGetValue(target, out Slots table)) {
                table = new Slots();
                slots[target] = table;
            }
            table.attackers.RemoveAll(a => a == null || a.Health.isDead || a.OrderTarget != target);
            mayAttack = table.attackers.Contains(npc);
            if (!mayAttack && table.attackers.Count < MaxAttackers) {
                table.attackers.Add(npc);
                table.waiting.Remove(npc);
                mayAttack = true;
            }

            // well inside its reach, so getting near its place is close enough to hit
            float ring = targetRadius(target) + npc.Radius + npc.Info.attack.range * 0.5f;
            Dictionary<Npc, int> places = mayAttack ? table.places : table.waiting;
            if (!mayAttack) {
                ring += WaitingDistance;
                table.places.Remove(npc);
            }
            int count = Mathf.Clamp(Mathf.FloorToInt(2 * Mathf.PI * ring / (npc.Radius * 2.2f)), 3, 12);
            if (!places.TryGetValue(npc, out int place) || place >= count) {
                place = freePlace(places, npc, target, count);
                places[npc] = place;
            }
            float angle = place * 2 * Mathf.PI / count;
            return target.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * ring;
        }

        // the free place closest to the NPC's bearing from the target
        private static int freePlace(Dictionary<Npc, int> places, Npc npc, Health target, int count) {
            HashSet<int> taken = new HashSet<int>();
            foreach (KeyValuePair<Npc, int> other in places) {
                if (other.Key != npc && other.Key != null) {
                    taken.Add(other.Value);
                }
            }
            Vector3 from = npc.transform.position - target.transform.position;
            float bearing = Mathf.Atan2(from.z, from.x);
            int best = 0;
            float bestDifference = float.MaxValue;
            for (int i = 0; i < count; i++) {
                if (taken.Contains(i)) {
                    continue;
                }
                float difference = Mathf.Abs(Mathf.DeltaAngle(bearing * Mathf.Rad2Deg, i * 360f / count));
                if (difference < bestDifference) {
                    bestDifference = difference;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>It isn't attacking that target anymore: its token and place are free.</summary>
        public static void ReleaseSlot(Npc npc, Health target) {
            if (target == null || !slots.TryGetValue(target, out Slots table)) {
                return;
            }
            table.attackers.Remove(npc);
            table.places.Remove(npc);
            table.waiting.Remove(npc);
            if (table.attackers.Count == 0 && table.waiting.Count == 0) {
                slots.Remove(target);
            }
        }

        private static float targetRadius(Health target) {
            if (target.TryGetComponent(out Npc npc)) {
                return npc.Radius;
            }
            return target.TryGetComponent(out CharacterController controller) ? controller.radius : 0.35f;
        }

        // -------- packs --------

        /// <summary>Puts NPCs in the same pack (with the first one's pack, if it has one).</summary>
        public static void Join(Npc npc, Npc packmate) {
            Leave(npc);
            if (!packs.TryGetValue(packmate, out Pack pack)) {
                pack = new Pack();
                pack.members.Add(packmate);
                packs[packmate] = pack;
            }
            pack.members.Add(npc);
            packs[npc] = pack;
        }

        /// <summary>Puts it in the pack with that id (back from a save), a new one if none of its members is around.</summary>
        public static void Join(Npc npc, Guid packId) {
            Leave(npc);
            Pack pack = null;
            foreach (Pack existing in packs.Values) {
                if (existing.id == packId) {
                    pack = existing;
                    break;
                }
            }
            if (pack == null) {
                pack = new Pack() { id = packId };
            }
            pack.members.Add(npc);
            packs[npc] = pack;
        }

        public static void Leave(Npc npc) {
            if (packs.TryGetValue(npc, out Pack pack)) {
                pack.members.Remove(npc);
                packs.Remove(npc);
            }
        }

        public static Pack PackOf(Npc npc) {
            return packs.TryGetValue(npc, out Pack pack) ? pack : null;
        }

        /// <summary>Where a pack member that isn't leading goes: its place in a wedge behind the leader.</summary>
        public static Vector3 FormationPlace(Npc npc, Pack pack) {
            Npc leader = pack.Leader;
            int index = 0;
            foreach (Npc member in pack.members) {
                if (member == leader || member == null || member.Health.isDead) {
                    continue;
                }
                index++;
                if (member == npc) {
                    break;
                }
            }
            if (leader.Velocity.sqrMagnitude > 0.25f) {
                pack.heading = leader.Velocity.normalized;
            }
            int row = (index + 1) / 2;
            float side = index % 2 == 1 ? -1 : 1;
            Vector3 offset = new Vector3(side * FormationSpacing * row, 0, -FormationSpacing * row);
            return leader.transform.position + Quaternion.LookRotation(pack.heading) * offset;
        }

        /// <summary>A member goes for someone: the others near it are told to.</summary>
        public static void Alert(Npc npc, Health target) {
            Pack pack = PackOf(npc);
            if (pack == null) {
                return;
            }
            foreach (Npc member in pack.members) {
                if (member != npc && member != null && member.Brain != null && !member.Health.isDead
                        && Vector3.Distance(member.transform.position, npc.transform.position) <= AlertRange) {
                    member.Brain.Alert(target);
                }
            }
        }

        /// <summary>It's gone: out of its pack and of every slot.</summary>
        public static void Forget(Npc npc) {
            Leave(npc);
            List<Health> empty = new List<Health>();
            foreach (KeyValuePair<Health, Slots> table in slots) {
                table.Value.attackers.Remove(npc);
                table.Value.places.Remove(npc);
                table.Value.waiting.Remove(npc);
                if (table.Value.attackers.Count == 0 && table.Value.waiting.Count == 0) {
                    empty.Add(table.Key);
                }
            }
            foreach (Health target in empty) {
                slots.Remove(target);
            }
        }
    }
}
