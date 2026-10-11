using System.Collections.Generic;
using Brickcraft.Combat;
using Brickcraft.Network;
using Brickcraft.Scripting;
using Mirror;
using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// What an NPC thinks, on the server, a few times a second: what it perceives (NpcPerception: who it sees,
    /// within its sight range and field of view and not hidden; what it hears; who hurts it), who it
    /// remembers and where it last knew them to be, and what it does about it, given to its body as orders
    /// (see Npc).
    ///
    /// Wild NPCs go for whoever matches one of their aggro rules (NpcInfo.aggro, or their behaviour's), the
    /// nearest of them; one they lose from sight they look for where it last was, and after their memory
    /// runs out they give up (onTargetLost). One they can't get to they leave alone for a while. Passive
    /// ones run from who hurts them; with nobody to go for they wander, or follow their pack's leader (see
    /// NpcGroups: a pack goes for whoever one of them goes for).
    ///
    /// Pets (tamed with NpcInfo.tameWith) follow their owner (or stay), catching up when left behind, and
    /// attack what their attack mode says: those who hurt their owner or whom their owner hits, hostile NPCs
    /// too, or players too (with PvP). Those following leave when their owner does and come back with them
    /// (see NpcSystem); those staying stay, standing still while their owner is away.
    ///
    /// Mods' NPCs can have a script (npcs/[npc]/script.lua, see Scripting.ModScripts): onThink(npc, senses)
    /// returning false keeps this brain out of it (the script gives the orders); onSpawn, onDamaged,
    /// onDeath, onTargetLost and onInteract tell it what happens.
    /// </summary>
    public class NpcBrain
    {
        public enum PetAttack : byte
        {
            Nothing = 0,
            /// <summary>Who hurts its owner, and whom its owner hits.</summary>
            Defend = 1,
            /// <summary>Also hostile NPCs it sees.</summary>
            Hostiles = 2,
            /// <summary>Also players it sees (with PvP).</summary>
            Everyone = 3,
        }

        // how long it runs from who hurt it, gives up on who it can't reach, and how far a pet strays
        private const float FleeTime = 5;
        private const float FleeDistance = 10;
        private const float GiveUpTime = 10;
        private const float PetFollowDistance = 2.5f;
        private const float PetCatchUpDistance = 24;
        private const float PetGuardDistance = 16;
        // seen this recently: it's chased where it is, not where it was last known to be
        private const float FreshSight = 0.6f;

        private readonly Npc npc;

        private class Memory
        {
            public Vector3 position;  // where it last knew it was
            public double knownAt;    // when it last saw, heard or was hit by it
            public bool isTarget;     // it matched an aggro rule
        }

        private readonly Dictionary<Health, Memory> memories = new Dictionary<Health, Memory>();
        private Health target;
        private Health lastAttacker;
        private double lastAttackedAt;
        private double previousThink;
        private double lastNoise;
        private Health ignored;
        private double ignoredUntil;
        private Vector3 stayPosition;

        /// <summary>Its owner's players.db id if it's a pet, 0 otherwise.</summary>
        public int Owner { get; private set; }
        public bool IsFollowing { get; private set; } = true;
        public PetAttack Attacks { get; private set; } = PetAttack.Defend;

        /// <summary>What it perceived the last time it thought (what scripts get as senses).</summary>
        public readonly List<Health> Seen = new List<Health>();
        public readonly List<Noise> Heard = new List<Noise>();
        /// <summary>Who hurt it since it last thought, null if nobody.</summary>
        public Health AttackedBy { get; private set; }

        public Health Target {
            get { return target; }
        }

        public NpcBrain(Npc npc) {
            this.npc = npc;
        }

        private NpcInfo info {
            get { return npc.Info; }
        }

        public void Start() {
            npc.Health.ServerDamaged += onDamaged;
            npc.Health.ServerDied += onDied;
            npc.Health.ServerAllowDamage = allowDamage;
            npc.OrderWander();
            scriptEvent("onSpawn", script => new object[] { NpcHandle.For(npc) });
        }

        // -------- thinking --------

        public void Think(double now) {
            perceive(now);
            if (ModScripts.HasNpcEvent(npc.type, "onThink")
                    && !ModScripts.NpcEvent(npc.type, "onThink", script => new object[] { NpcHandle.For(npc), LuaSenses.Table(script, this) })) {
                return; // the script decided
            }
            if (Owner != 0) {
                thinkPet(now);
            } else {
                thinkWild(now);
            }
            previousThink = now;
        }

        // who it sees and hears now, and who it remembers
        private void perceive(double now) {
            Seen.Clear();
            Heard.Clear();
            AttackedBy = lastAttackedAt > previousThink ? lastAttacker : null;
            float half = info.fieldOfView / 2;
            float feel = feelRange();
            foreach (Health candidate in candidates()) {
                Vector3 to = candidate.transform.position - npc.transform.position;
                float distance = to.magnitude;
                if (distance <= feel) {
                    remember(candidate, now); // that close, seen or not (a "distance" rule)
                }
                if (distance > info.sightRange) {
                    continue;
                }
                to.y = 0;
                if (Vector3.Angle(npc.transform.forward, to) <= half && NpcPerception.CanSee(npc, candidate)) {
                    Seen.Add(candidate);
                    remember(candidate, now);
                }
            }
            foreach (Noise noise in NpcPerception.Noises) {
                if (noise.time <= lastNoise || noise.source == npc.Health) {
                    continue;
                }
                if (Vector3.Distance(noise.position, npc.transform.position) <= Mathf.Min(noise.radius, info.hearingRange)) {
                    Heard.Add(noise);
                    if (noise.source != null) {
                        remember(noise.source, now).position = noise.position;
                    }
                }
            }
            foreach (Noise noise in NpcPerception.Noises) {
                lastNoise = System.Math.Max(lastNoise, noise.time);
            }
            if (AttackedBy != null) {
                remember(AttackedBy, now);
            }
            // forgotten: not known of for longer than it remembers
            List<Health> forgotten = null;
            foreach (KeyValuePair<Health, Memory> memory in memories) {
                if (memory.Key == null || memory.Key.isDead || now - memory.Value.knownAt > info.memory) {
                    (forgotten = forgotten ?? new List<Health>()).Add(memory.Key);
                }
            }
            if (forgotten != null) {
                foreach (Health health in forgotten) {
                    memories.Remove(health);
                    if (health == target) {
                        loseTarget();
                    }
                }
            }
        }

        // the furthest its "distance" rules reach
        private float feelRange() {
            float range = 0;
            foreach (AggroRule rule in info.aggro ?? DefaultAggro(info)) {
                if (rule.sense == AggroRule.Distance) {
                    range = Mathf.Max(range, rule.range);
                }
            }
            return range;
        }

        private Memory remember(Health health, double now) {
            if (!memories.TryGetValue(health, out Memory memory)) {
                memory = new Memory();
                memories[health] = memory;
            }
            memory.position = health.transform.position;
            memory.knownAt = now;
            return memory;
        }

        // players and NPCs alive around (not itself, nor its owner)
        private IEnumerable<Health> candidates() {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                Health player = conn.identity != null ? conn.identity.GetComponent<Health>() : null;
                if (player != null && !player.isDead && !isOwner(player)) {
                    yield return player;
                }
            }
            foreach (Npc other in NpcSystem.All) {
                if (other != npc && !other.Health.isDead && !(Owner != 0 && other.Brain != null && other.Brain.Owner == Owner)) {
                    yield return other.Health;
                }
            }
        }

        private bool isOwner(Health player) {
            return Owner != 0 && player.connectionToClient != null
                && player.connectionToClient.authenticationData is ConnectedPlayer owner && owner.record.Id == Owner;
        }

        private static bool isPlayer(Health health) {
            return health.GetComponent<PlayerNetwork>() != null;
        }

        // -------- wild --------

        private void thinkWild(double now) {
            if (now >= ignoredUntil) {
                ignored = null;
            }
            markTargets(now);
            Health previous = target;
            chooseTarget(now);
            if (target != null && target != previous) {
                NpcGroups.Alert(npc, target);
            }

            if (target != null) {
                chase(now);
                return;
            }
            if (info.behaviour == NpcInfo.Passive && lastAttacker != null && now - lastAttackedAt < FleeTime) {
                Vector3 away = npc.transform.position - lastAttacker.transform.position;
                away.y = 0;
                npc.OrderMoveTo(npc.transform.position + away.normalized * FleeDistance, 1);
                return;
            }
            NpcGroups.Pack pack = NpcGroups.PackOf(npc);
            Npc leader = pack != null ? pack.Leader : null;
            if (leader != null && leader != npc) {
                npc.OrderMoveTo(NpcGroups.FormationPlace(npc, pack), 0.8f);
                return;
            }
            npc.OrderWander();
        }

        /// <summary>A packmate goes for someone: it does too, if it would go for them.</summary>
        public void Alert(Health someone) {
            if (Owner == 0 && someone != null && !someone.isDead && isTargetable(someone)) {
                remember(someone, NetworkTime.time).isTarget = true;
                npc.ThinkSoon();
            }
        }

        // who matches one of its aggro rules becomes something to go for
        private void markTargets(double now) {
            AggroRule[] rules = info.aggro ?? DefaultAggro(info);
            foreach (KeyValuePair<Health, Memory> memory in memories) {
                Health candidate = memory.Key;
                if (memory.Value.isTarget || !isTargetable(candidate)) {
                    continue;
                }
                float distance = Vector3.Distance(candidate.transform.position, npc.transform.position);
                foreach (AggroRule rule in rules) {
                    if (matches(rule, candidate, distance)) {
                        memory.Value.isTarget = true;
                        break;
                    }
                }
            }
        }

        private bool matches(AggroRule rule, Health candidate, float distance) {
            // peaceful worlds: players are only gone for when they hit first
            if (isPlayer(candidate) && WorldNetwork.Difficulty == Difficulty.Peaceful && rule.sense != AggroRule.Attacked) {
                return false;
            }
            switch (rule.sense) {
                case AggroRule.Sight:
                    if (distance > rule.range || !Seen.Contains(candidate)) {
                        return false;
                    }
                    Vector3 to = candidate.transform.position - npc.transform.position;
                    to.y = 0;
                    return Vector3.Angle(npc.transform.forward, to) <= (rule.fov > 0 ? rule.fov : info.fieldOfView) / 2;
                case AggroRule.Hearing:
                    foreach (Noise noise in Heard) {
                        if (noise.source == candidate && Vector3.Distance(noise.position, npc.transform.position) <= rule.range) {
                            return true;
                        }
                    }
                    return false;
                case AggroRule.Distance:
                    return distance <= rule.range;
                case AggroRule.Attacked:
                    return candidate == lastAttacker;
                default:
                    return false;
            }
        }

        /// <summary>A behaviour's aggro rules: passive none, neutral who hurts it, aggressive also who it sees, hears or bumps into.</summary>
        public static AggroRule[] DefaultAggro(NpcInfo info) {
            switch (info.behaviour) {
                case NpcInfo.Passive:
                    return new AggroRule[0];
                case NpcInfo.Aggressive:
                    return new[] {
                        new AggroRule() { sense = AggroRule.Sight, range = info.sightRange },
                        new AggroRule() { sense = AggroRule.Hearing, range = info.hearingRange * 0.75f },
                        new AggroRule() { sense = AggroRule.Distance, range = 3 },
                        new AggroRule() { sense = AggroRule.Attacked },
                    };
                default:
                    return new[] { new AggroRule() { sense = AggroRule.Attacked } };
            }
        }

        // what it goes for: players, NPCs or both (NpcInfo.targets); anything that hurt it
        private bool isTargetable(Health candidate) {
            if (candidate == ignored) {
                return false;
            }
            if (candidate == lastAttacker) {
                return true;
            }
            bool player = isPlayer(candidate);
            return info.targets == "all" || (player ? info.targets != "npcs" : info.targets != "players");
        }

        // the one it's after, while it's still a target; otherwise the nearest target it knows of
        private void chooseTarget(double now) {
            if (target != null && memories.TryGetValue(target, out Memory current) && current.isTarget && target != ignored) {
                return;
            }
            Health nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (KeyValuePair<Health, Memory> memory in memories) {
                if (!memory.Value.isTarget || memory.Key == ignored) {
                    continue;
                }
                float distance = Vector3.Distance(memory.Value.position, npc.transform.position);
                if (distance < nearestDistance) {
                    nearest = memory.Key;
                    nearestDistance = distance;
                }
            }
            target = nearest;
        }

        // after it where it is if it's in sight (or just hit it, or is right there), otherwise where it was
        private void chase(double now) {
            Memory memory = memories[target];
            bool fresh = now - memory.knownAt <= FreshSight || npc.IsInReach(target);
            if (fresh) {
                npc.OrderAttack(target);
            } else {
                npc.OrderMoveTo(memory.position, 1);
                if (npc.HasArrived) {
                    // not there anymore: lost
                    memories.Remove(target);
                    loseTarget();
                    return;
                }
            }
            if (npc.IsUnreachable) {
                ignored = target;
                ignoredUntil = now + GiveUpTime;
                memory.isTarget = false;
                loseTarget();
            }
        }

        private void loseTarget() {
            Health lost = target;
            target = null;
            if (lost != null) {
                scriptEvent("onTargetLost", script => new object[] { NpcHandle.For(npc), LuaHandles.For(lost) });
            }
        }

        // -------- pets --------

        /// <summary>It becomes the player's pet: it follows them and defends them.</summary>
        [Server]
        public void Tame(int ownerId, string ownerName) {
            Owner = ownerId;
            npc.ownerName = ownerName;
            IsFollowing = true;
            Attacks = PetAttack.Defend;
            memories.Clear();
            target = null;
            lastAttacker = null;
            ignored = null;
        }

        /// <summary>What its owner tells it: follow (or stay where it is), and what to attack.</summary>
        [Server]
        public void SetOrders(bool following, PetAttack attacks) {
            IsFollowing = following;
            Attacks = attacks;
            stayPosition = npc.transform.position;
            target = null;
        }

        private void thinkPet(double now) {
            Health owner = NpcSystem.PlayerHealth(Owner);
            if (owner == null) {
                npc.OrderIdle();
                return;
            }
            if (IsFollowing && Npc.horizontalDistance(owner.transform.position, npc.transform.position) > PetCatchUpDistance
                    || Mathf.Abs(owner.transform.position.y - npc.transform.position.y) > 10) {
                catchUp(owner);
            }
            if (now >= ignoredUntil) {
                ignored = null;
            }
            Health threat = petThreat(owner, now);
            if (threat != null) {
                target = threat;
                npc.OrderAttack(threat);
                if (npc.IsUnreachable) {
                    ignored = threat;
                    ignoredUntil = now + GiveUpTime;
                    target = null;
                }
                return;
            }
            target = null;
            if (IsFollowing) {
                npc.OrderFollow(owner, PetFollowDistance);
            } else {
                npc.OrderMoveTo(stayPosition, 0.5f);
            }
        }

        // what its attack mode tells it to go for, near its owner; the closest
        private Health petThreat(Health owner, double now) {
            if (Attacks == PetAttack.Nothing) {
                return null;
            }
            List<Health> threats = new List<Health>();
            foreach (Health attacker in NpcSystem.RecentAttackersOf(owner)) {
                threats.Add(attacker);
            }
            foreach (Health victim in NpcSystem.RecentTargetsOf(owner)) {
                threats.Add(victim);
            }
            if (lastAttacker != null && now - lastAttackedAt < GiveUpTime) {
                threats.Add(lastAttacker);
            }
            if (Attacks >= PetAttack.Hostiles) {
                foreach (Health seen in Seen) {
                    if (seen.TryGetComponent(out Npc other) && other.Brain != null && other.Brain.isHostile) {
                        threats.Add(seen);
                    }
                }
            }
            if (Attacks >= PetAttack.Everyone) {
                foreach (Health seen in Seen) {
                    if (isPlayer(seen)) {
                        threats.Add(seen);
                    }
                }
            }

            Health closest = null;
            float closestDistance = float.MaxValue;
            foreach (Health threat in threats) {
                if (threat == null || threat.isDead || threat == ignored || threat == npc.Health || isOwner(threat)
                        || (isPlayer(threat) && !WorldNetwork.Pvp)
                        || (threat.TryGetComponent(out Npc other) && other.Brain != null && other.Brain.Owner == Owner)
                        || Vector3.Distance(threat.transform.position, owner.transform.position) > PetGuardDistance) {
                    continue;
                }
                float distance = Vector3.Distance(threat.transform.position, npc.transform.position);
                if (distance < closestDistance) {
                    closest = threat;
                    closestDistance = distance;
                }
            }
            return closest;
        }

        // wild and out to hurt players: aggressive, or after a player right now
        private bool isHostile {
            get { return Owner == 0 && (info.behaviour == NpcInfo.Aggressive || (target != null && isPlayer(target))); }
        }

        // right behind its owner, on the ground there (if there's ground)
        private void catchUp(Health owner) {
            // not into the void (an owner falling, or flying over nothing), nor into them or a wall
            if (NpcSystem.SpotNear(owner.transform, npc.Radius, npc.Height, out Vector3 spot)) {
                npc.ServerTeleport(spot);
            }
        }

        // -------- what happens to it --------

        private void onDamaged(DamageInfo damage) {
            Health attacker = damage.attacker != null ? damage.attacker.GetComponent<Health>() : null;
            if (attacker == null) {
                return;
            }
            lastAttacker = attacker;
            lastAttackedAt = NetworkTime.time;
            if (attacker == ignored) {
                ignored = null; // it'll try again
            }
            npc.ThinkSoon(); // turns on them right away
        }

        // a script can keep it from being hurt
        private bool allowDamage(DamageInfo damage) {
            Health attacker = damage.attacker != null ? damage.attacker.GetComponent<Health>() : null;
            return ModScripts.NpcEvent(npc.type, "onDamaged", script => new object[] { NpcHandle.For(npc), damage.amount, LuaHandles.For(attacker) });
        }

        private void onDied(DamageInfo damage) {
            Health killer = damage.attacker != null ? damage.attacker.GetComponent<Health>() : null;
            scriptEvent("onDeath", script => new object[] { NpcHandle.For(npc), LuaHandles.For(killer) });
        }

        private void scriptEvent(string handler, System.Func<MoonSharp.Interpreter.Script, object[]> args) {
            if (ModScripts.HasNpcEvent(npc.type, handler)) {
                ModScripts.NpcEvent(npc.type, handler, args);
            }
        }
    }
}
