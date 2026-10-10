using System;
using Brickcraft.Events;
using Mirror;
using UnityEngine;

namespace Brickcraft.Combat
{
    /// <summary>What hurt something, see Health.ServerDamage.</summary>
    public struct DamageInfo
    {
        public enum Kind : byte
        {
            /// <summary>A hit: a player's click, an NPC's attack.</summary>
            Melee = 1,
            /// <summary>A mod's script.</summary>
            Script = 2,
        }

        public int amount;
        public Kind kind;
        /// <summary>Who did it, null for nobody (a script).</summary>
        public GameObject attacker;
        /// <summary>Its name, for the chat and the death screen.</summary>
        public string attackerName;
    }

    /// <summary>
    /// Hit points of something that can be hurt and die: players, and NPCs. The server changes them
    /// (ServerDamage, ServerHeal, ServerRevive), everybody sees them. Right after a hit it can't be hurt
    /// for a moment, so a group doesn't take it down in a frame.
    ///
    /// On the server, ServerDamaged and ServerDied tell what happened; on the local player's client,
    /// EventManager.LocalPlayerHealthChanged, LocalPlayerDied and LocalPlayerRespawned.
    /// </summary>
    public class Health : NetworkBehaviour
    {
        public const float DefaultInvulnerableTime = 0.5f;

        [SyncVar(hook = nameof(onHealthChanged))] public int health = 20;
        [SyncVar(hook = nameof(onMaxHealthChanged))] public int maxHealth = 20;
        /// <summary>Who killed it, set before isDead (null for nobody).</summary>
        [SyncVar] public string killedBy;
        [SyncVar(hook = nameof(onDeadChanged))] public bool isDead;

        /// <summary>Seconds it can't be hurt after a hit.</summary>
        public float invulnerableTime = DefaultInvulnerableTime;

        /// <summary>Server: asked before every hit, false keeps it from happening (a mod's onPlayerDamaged...).</summary>
        public Func<DamageInfo, bool> ServerAllowDamage;
        /// <summary>Server: it was hurt (and is still alive, or just died).</summary>
        public event Action<DamageInfo> ServerDamaged;
        /// <summary>Server: it died of that hit.</summary>
        public event Action<DamageInfo> ServerDied;
        /// <summary>Everywhere: it died (true) or came back (false).</summary>
        public event Action<bool> DeadChanged;
        /// <summary>Everywhere: its health went from one value to another (lower: it was hurt).</summary>
        public event Action<int, int> HealthChanged;

        private double invulnerableUntil;

        /// <summary>Hurts it, false if it couldn't be (dead, just hit, or something stopped it).</summary>
        [Server]
        public bool ServerDamage(DamageInfo damage) {
            if (isDead || damage.amount <= 0 || NetworkTime.time < invulnerableUntil) {
                return false;
            }
            if (ServerAllowDamage != null && !ServerAllowDamage(damage)) {
                return false;
            }
            invulnerableUntil = NetworkTime.time + invulnerableTime;
            health = Mathf.Max(0, health - damage.amount);
            ServerDamaged?.Invoke(damage);

            if (health == 0) {
                killedBy = damage.attackerName;
                isDead = true;
                ServerDied?.Invoke(damage);
            }
            return true;
        }

        [Server]
        public void ServerHeal(int amount) {
            if (!isDead && amount > 0) {
                health = Mathf.Min(maxHealth, health + amount);
            }
        }

        /// <summary>Back to life, with all its health.</summary>
        [Server]
        public void ServerRevive() {
            health = maxHealth;
            killedBy = null;
            isDead = false;
        }

        private void onHealthChanged(int oldValue, int newValue) {
            HealthChanged?.Invoke(oldValue, newValue);
            announceToLocalPlayer();
        }

        private void onMaxHealthChanged(int oldValue, int newValue) {
            announceToLocalPlayer();
        }

        private void announceToLocalPlayer() {
            if (isLocalPlayer) {
                EventManager.LocalPlayerHealthChanged.Raise(new LocalPlayerHealthChangedEvent() { health = health, maxHealth = maxHealth });
            }
        }

        private void onDeadChanged(bool wasDead, bool dead) {
            DeadChanged?.Invoke(dead);
            if (!isLocalPlayer) {
                return;
            }
            if (dead) {
                EventManager.LocalPlayerDied.Raise(new LocalPlayerDiedEvent() { killedBy = killedBy });
            } else {
                EventManager.LocalPlayerRespawned.Raise(new LocalPlayerRespawnedEvent());
            }
        }

        public override void OnStartLocalPlayer() {
            announceToLocalPlayer();
        }
    }
}
