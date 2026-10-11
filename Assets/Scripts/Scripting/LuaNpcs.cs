using Brickcraft.Bricks;
using Brickcraft.Combat;
using Brickcraft.Network;
using Brickcraft.Npcs;
using Mirror;
using MoonSharp.Interpreter;
using UnityEngine;

namespace Brickcraft.Scripting
{
    /// <summary>An NPC as scripts see it (see docs/lua/npcs.md).</summary>
    [MoonSharpUserData]
    public class NpcHandle
    {
        /// <summary>Its network id, the same for as long as it exists.</summary>
        public uint id;
        /// <summary>Its kind, like "golem" or "my_mod:wolf".</summary>
        public string type;
        public string name;

        [MoonSharpHidden] public Npc npc;

        [MoonSharpHidden]
        public static NpcHandle For(Npc npc) {
            return npc != null ? new NpcHandle() { id = npc.netId, type = npc.type, name = npc.Info != null ? npc.Info.name : npc.type, npc = npc } : null;
        }

        /// <summary>It's still in the world and alive.</summary>
        public bool exists {
            get { return npc != null && !npc.Health.isDead; }
        }

        /// <summary>The cell its feet are in, nil once it's gone.</summary>
        public LuaVector position {
            get { return npc != null ? new LuaVector(BrickGrid.WorldToCell(npc.transform.position + Vector3.up * 0.05f)) : null; }
        }

        public DynValue health {
            get { return npc != null ? DynValue.NewNumber(npc.Health.health) : DynValue.Nil; }
        }

        public DynValue maxHealth {
            get { return npc != null ? DynValue.NewNumber(npc.Health.maxHealth) : DynValue.Nil; }
        }

        /// <summary>Who it's after (a player or an NPC), nil if nobody.</summary>
        public object target {
            get { return npc != null && npc.Brain != null ? LuaHandles.For(npc.Brain.Target ?? npc.OrderTarget) : null; }
        }

        /// <summary>Its owner if it's a pet (a player, nil while they're away), nil if it's wild.</summary>
        public PlayerHandle owner {
            get {
                Health player = npc != null && npc.Brain != null ? NpcSystem.PlayerHealth(npc.Brain.Owner) : null;
                return player != null ? PlayerHandle.For(player.connectionToClient) : null;
            }
        }

        // -------- orders (in onThink returning false, they're all it does) --------

        /// <summary>Goes after a player or an NPC and hits it.</summary>
        public void attack(DynValue who) {
            npcOrDie().OrderAttack(LuaHandles.HealthOf(who, "attack"));
        }

        /// <summary>Walks to a position (stops within 1 cell... a meter of it).</summary>
        public void moveTo(DynValue to) {
            npcOrDie().OrderMoveTo(BrickGrid.CellToWorld(LuaVector.Read(to, "moveTo's position"), new Vector3(0.5f, 0, 0.5f)), 1);
        }

        /// <summary>Walks after a player or an NPC, staying within distance meters (2 by default).</summary>
        public void follow(DynValue who, float distance = 2) {
            npcOrDie().OrderFollow(LuaHandles.HealthOf(who, "follow"), Mathf.Max(0.5f, distance));
        }

        public void wander() {
            npcOrDie().OrderWander();
        }

        public void stop() {
            npcOrDie().OrderIdle();
        }

        // -------- the rest --------

        /// <summary>Hurts it (by nobody). False if it couldn't be.</summary>
        public bool damage(int amount) {
            if (amount < 1) {
                throw new ScriptRuntimeException("damage's amount should be at least 1");
            }
            return npcOrDie().Health.ServerDamage(new DamageInfo() { amount = amount, kind = DamageInfo.Kind.Script });
        }

        public void heal(int amount) {
            if (amount < 1) {
                throw new ScriptRuntimeException("heal's amount should be at least 1");
            }
            npcOrDie().Health.ServerHeal(amount);
        }

        /// <summary>Makes it a player's pet.</summary>
        public void tame(PlayerHandle player) {
            ConnectedPlayer connected = player != null && player.connection != null ? player.connection.authenticationData as ConnectedPlayer : null;
            if (connected == null) {
                throw new ScriptRuntimeException("tame needs a player that's in the game");
            }
            npcOrDie().Brain.Tame(connected.record.Id, connected.record.Name);
        }

        /// <summary>Takes it out of the world (nobody gets its drops). False if it was gone already.</summary>
        public bool remove() {
            if (npc == null) {
                return false;
            }
            npc.Despawn();
            return true;
        }

        private Npc npcOrDie() {
            if (npc == null) {
                throw new ScriptRuntimeException("the NPC " + name + " isn't there anymore");
            }
            return npc;
        }

        public override string ToString() {
            return "npc " + name;
        }
    }

    /// <summary>Players and NPCs (what has health) to and from scripts.</summary>
    public static class LuaHandles
    {
        /// <summary>A PlayerHandle or an NpcHandle, null for nothing.</summary>
        public static object For(Health health) {
            if (health == null) {
                return null;
            }
            if (health.TryGetComponent(out Npc npc)) {
                return NpcHandle.For(npc);
            }
            return health.connectionToClient != null ? PlayerHandle.For(health.connectionToClient) : null;
        }

        /// <summary>What a script passed as a player or an NPC.</summary>
        public static Health HealthOf(DynValue value, string what) {
            object handle = value.Type == DataType.UserData ? value.UserData.Object : null;
            if (handle is NpcHandle npc && npc.npc != null) {
                return npc.npc.Health;
            }
            if (handle is PlayerHandle player && player.connection != null && player.connection.identity != null) {
                return player.connection.identity.GetComponent<Health>();
            }
            throw new ScriptRuntimeException(what + " needs a player or an NPC that's still there");
        }
    }

    /// <summary>What an NPC perceived, as onThink gets it: senses.seen, senses.heard, senses.attackedBy.</summary>
    public static class LuaSenses
    {
        public static Table Table(Script script, NpcBrain brain) {
            Table senses = new Table(script);
            Table seen = new Table(script);
            foreach (Health health in brain.Seen) {
                object handle = LuaHandles.For(health);
                if (handle != null) {
                    seen.Append(DynValue.FromObject(script, handle));
                }
            }
            Table heard = new Table(script);
            foreach (Noise noise in brain.Heard) {
                Table entry = new Table(script);
                entry["kind"] = noise.kind;
                entry["position"] = DynValue.FromObject(script, new LuaVector(BrickGrid.WorldToCell(noise.position)));
                object source = LuaHandles.For(noise.source);
                if (source != null) {
                    entry["source"] = DynValue.FromObject(script, source);
                }
                heard.Append(DynValue.NewTable(entry));
            }
            senses["seen"] = DynValue.NewTable(seen);
            senses["heard"] = DynValue.NewTable(heard);
            object attackedBy = LuaHandles.For(brain.AttackedBy);
            if (attackedBy != null) {
                senses["attackedBy"] = DynValue.FromObject(script, attackedBy);
            }
            return senses;
        }
    }
}
