using System;
using Brickcraft.Combat;
using Brickcraft.World;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// A shot ammo item (an arrow, see Item.ammo) flying: it falls as it goes, hurts the first thing with health it
    /// hits (as its shooter would with its weapon: PvP, pets...) and sticks where it hits the world. Stuck, players
    /// walking onto it get it back, until it goes a minute later (or falls out when what held it is gone).
    ///
    /// Its flight is known from where and when it was shot and how fast: every peer works out where it is from
    /// that (no position is sent), the server checks what it hits on the way and tells where it stuck.
    /// Players see the shots of the chunks they have, like items on the ground (see ChunkInterestManagement).
    /// </summary>
    public class Projectile : NetworkBehaviour
    {
        public const string PrefabPath = "Projectile";

        /// <summary>How fast shots fall, meters per second each second.</summary>
        public const float Gravity = 12;
        /// <summary>How long its model is drawn, in meters.</summary>
        public const float ModelLength = 0.7f;
        private const float MaxFlightTime = 10;
        private const float StuckTime = 60;
        private const float PickupDistance = 1.3f;
        private const float PickableAfter = 0.5f;
        private const float FallLimit = -200;
        // its shooter can't hit itself right after shooting
        private const float ShooterSafeTime = 0.4f;
        // how thick it is, for grazing hits
        private const float Radius = 0.05f;
        // how deep the tip goes into what it sticks in
        private const float StickDepth = 0.15f;
        // it hits what drops land on: not water, nor players' own capsules (Ignore Raycast; they're checked apart)
        private const int SolidLayers = Physics.DefaultRaycastLayers & ~(1 << (int)Game.Layers.Water);
        private const float SupportCheckInterval = 1;

        /// <summary>What it is (its item, see Slugs) and its colour.</summary>
        [SyncVar] public string itemId;
        [SyncVar] public int color;
        // where (absolute, see FloatingOrigin) and when it was shot, and how fast
        [SyncVar] public double startX;
        [SyncVar] public double startY;
        [SyncVar] public double startZ;
        [SyncVar] public double startTime;
        [SyncVar] public Vector3 velocity;
        // where it stuck (absolute) and which way it pointed
        [SyncVar] public bool stuck;
        [SyncVar] public double stuckX;
        [SyncVar] public double stuckY;
        [SyncVar] public double stuckZ;
        [SyncVar] public Vector3 stuckDirection;

        // server: who shot it and how hard it hits
        private PlayerNetwork shooter;
        private int damage;
        private float knockback;
        private double stuckAt;
        private double nextSupportCheck;
        private float lastTime;

        private Transform model;

        /// <summary>Server: shoots one of that item from there (local position), that fast and that way.</summary>
        public static Projectile ServerShoot(PlayerNetwork shooter, string itemId, int color, Vector3 from, Vector3 velocity, int damage, float knockback) {
            GameObject go = Instantiate(Resources.Load<GameObject>(PrefabPath), from, Quaternion.LookRotation(velocity));
            Projectile shot = go.GetComponent<Projectile>();
            shot.itemId = itemId;
            shot.color = color;
            FloatingOrigin.ToAbsolute(from, out double x, out double y, out double z);
            shot.startX = x;
            shot.startY = y;
            shot.startZ = z;
            shot.startTime = NetworkTime.time;
            shot.velocity = velocity;
            shot.shooter = shooter;
            shot.damage = damage;
            shot.knockback = knockback;
            NetworkServer.Spawn(go);
            return shot;
        }

        /// <summary>Where it is that long after it was shot (local position), if it didn't hit anything.</summary>
        public Vector3 PositionAt(float time) {
            return FloatingOrigin.ToLocal(startX, startY, startZ) + velocity * time + Vector3.down * (Gravity / 2 * time * time);
        }

        private Vector3 velocityAt(float time) {
            return velocity + Vector3.down * Gravity * time;
        }

        private float flightTime {
            get { return Mathf.Max(0, (float)(NetworkTime.time - startTime)); }
        }

        public override void OnStartClient() {
            buildModel();
        }

        // the item's model without colliders, its longest side along where it flies (+Z)
        private void buildModel() {
            if (!Server.items.TryGetValue(itemId ?? "", out Item item) || item.brickModel == null) {
                return;
            }
            GameObject shown = Server.Instance.createBrickObject(item, color, Vector3.zero, Quaternion.identity);
            foreach (Collider collider in shown.GetComponentsInChildren<Collider>()) {
                Destroy(collider);
            }
            foreach (MonoBehaviour behaviour in shown.GetComponentsInChildren<MonoBehaviour>()) {
                behaviour.enabled = false;
            }
            foreach (Transform part in shown.GetComponentsInChildren<Transform>()) {
                part.gameObject.layer = (int)Game.Layers.IgnoreRaycast;
            }
            Bounds bounds = new Bounds(shown.transform.position, Vector3.zero);
            foreach (Renderer part in shown.GetComponentsInChildren<Renderer>()) {
                bounds.Encapsulate(part.bounds);
            }
            Vector3 size = bounds.size;
            float longest = Mathf.Max(size.x, size.y, size.z);
            Quaternion along = size.y >= longest ? Quaternion.Euler(90, 0, 0) : size.x >= longest ? Quaternion.Euler(0, -90, 0) : Quaternion.identity;

            GameObject pivot = new GameObject("Model");
            pivot.transform.SetParent(transform, false);
            pivot.transform.localScale = Vector3.one * (longest > 0 ? ModelLength / longest : 1);
            pivot.transform.localRotation = along;
            shown.transform.SetParent(pivot.transform, false);
            shown.transform.localPosition = -bounds.center; // centred on it
            model = pivot.transform;
        }

        private void Update() {
            if (isServer) {
                serverUpdate();
            } else {
                place();
            }
        }

        // clients: where it is now (from when it was shot, or where it stuck)
        private void place() {
            if (stuck) {
                transform.SetPositionAndRotation(FloatingOrigin.ToLocal(stuckX, stuckY, stuckZ), Quaternion.LookRotation(stuckDirection));
                return;
            }
            float time = flightTime;
            transform.SetPositionAndRotation(PositionAt(time), Quaternion.LookRotation(velocityAt(time)));
        }

        // -------- server --------

        private void serverUpdate() {
            if (stuck) {
                transform.SetPositionAndRotation(FloatingOrigin.ToLocal(stuckX, stuckY, stuckZ), Quaternion.LookRotation(stuckDirection));
                serverWhileStuck();
                return;
            }
            float time = flightTime;
            Vector3 from = PositionAt(lastTime);
            Vector3 to = PositionAt(time);
            lastTime = time;
            Vector3 path = to - from;
            float length = path.magnitude;
            if (length > 0.0001f && checkHits(from, path / length, length, time)) {
                return;
            }
            transform.SetPositionAndRotation(to, Quaternion.LookRotation(velocityAt(time)));
            if (time > MaxFlightTime || to.y < FallLimit) {
                NetworkServer.Destroy(gameObject);
            }
        }

        // whatever it hits going that way this frame: true if it stopped
        private bool checkHits(Vector3 from, Vector3 direction, float length, float time) {
            // the first thing in the way it doesn't fly past (what has health it can't hurt: its shooter, pets...)
            RaycastHit[] hits = Physics.RaycastAll(from, direction, length, SolidLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            bool hitWorld = false;
            RaycastHit hit = default(RaycastHit);
            Health victim = null;
            foreach (RaycastHit candidate in hits) {
                Health owner = candidate.collider.GetComponentInParent<Health>();
                if (owner != null && !canHit(owner, time)) {
                    continue;
                }
                hit = candidate;
                hitWorld = owner == null;
                victim = owner;
                break;
            }
            float reach = hitWorld || victim != null ? hit.distance : length;

            // the ray finds NPCs and other players' hitboxes, capsules the rest (the host's own player)
            float victimDistance = victim != null ? hit.distance : float.MaxValue;
            Health other = nearestHealth(from, direction, reach, time, out float otherDistance);
            if (other != null && otherDistance < victimDistance) {
                victim = other;
                victimDistance = otherDistance;
            }
            if (victim != null) {
                // what it didn't hurt (a raised shield...) it bounces off, falling to the ground
                if (!serverHurt(victim, direction)) {
                    DroppedItems.ServerDrop(itemId, color, 1, 0, from + direction * Mathf.Max(0, victimDistance - 0.3f), -direction * 2);
                }
                NetworkServer.Destroy(gameObject);
                return true;
            }
            if (hitWorld) {
                serverStick(hit.point, direction);
                return true;
            }
            return false;
        }

        // a shot can hurt it: alive, not its shooter right away, and what its shooter could hit (PvP, pets)
        private bool canHit(Health target, float time) {
            if (target.isDead || target.GetComponent<Projectile>() != null) {
                return false;
            }
            if (shooter == null) {
                return true;
            }
            if (target.gameObject == shooter.gameObject) {
                return time > ShooterSafeTime;
            }
            return shooter.ServerCanHit(target.netIdentity);
        }

        // the closest thing with health whose capsule the path goes through, within that far
        private Health nearestHealth(Vector3 from, Vector3 direction, float reach, float time, out float distance) {
            Health nearest = null;
            distance = float.MaxValue;
            Vector3 to = from + direction * reach;
            foreach (Health target in Health.All) {
                if (!capsuleOf(target, out Vector3 bottom, out Vector3 top, out float radius)) {
                    continue;
                }
                // far from the path: not worth the maths
                if ((Vector3.Lerp(bottom, top, 0.5f) - Vector3.Lerp(from, to, 0.5f)).sqrMagnitude > (reach + 3) * (reach + 3)) {
                    continue;
                }
                float along = closestOnPath(from, to, bottom, top, radius + Radius);
                if (along >= 0 && along * reach < distance && canHit(target, time)) {
                    nearest = target;
                    distance = along * reach;
                }
            }
            return nearest;
        }

        // the axis of its body (players' character controller, NPCs' capsule)
        private static bool capsuleOf(Health target, out Vector3 bottom, out Vector3 top, out float radius) {
            Vector3 center;
            float height;
            if (target.TryGetComponent(out CharacterController controller)) {
                center = controller.center;
                height = controller.height;
                radius = controller.radius;
            } else if (target.TryGetComponent(out CapsuleCollider capsule)) {
                center = capsule.center;
                height = capsule.height;
                radius = capsule.radius;
            } else {
                bottom = top = Vector3.zero;
                radius = 0;
                return false;
            }
            Vector3 middle = target.transform.TransformPoint(center);
            float half = Mathf.Max(0, height / 2 - radius);
            bottom = middle + Vector3.down * half;
            top = middle + Vector3.up * half;
            return true;
        }

        // the first point (0 to 1 along from-to) within that distance of the segment bottom-top, -1 if none; sampled,
        // a frame's path is short
        private static float closestOnPath(Vector3 from, Vector3 to, Vector3 bottom, Vector3 top, float within) {
            const int Steps = 8;
            for (int i = 0; i <= Steps; i++) {
                float along = i / (float)Steps;
                Vector3 point = Vector3.Lerp(from, to, along);
                Vector3 axis = top - bottom;
                float onAxis = axis.sqrMagnitude > 0 ? Mathf.Clamp01(Vector3.Dot(point - bottom, axis) / axis.sqrMagnitude) : 0;
                if ((bottom + axis * onAxis - point).sqrMagnitude <= within * within) {
                    return along;
                }
            }
            return -1;
        }

        // false if it couldn't be hurt
        private bool serverHurt(Health victim, Vector3 direction) {
            bool hurt = victim.ServerDamage(new DamageInfo() {
                amount = damage,
                kind = DamageInfo.Kind.Ranged,
                attacker = shooter != null ? shooter.gameObject : null,
                attackerName = shooter != null ? shooter.playerName : null,
                direction = direction,
            });
            if (!hurt || knockback <= 0) {
                return hurt;
            }
            Vector3 away = new Vector3(direction.x, 0, direction.z).normalized * knockback + Vector3.up * knockback / 2;
            if (victim.TryGetComponent(out PlayerNetwork player)) {
                player.ServerKnockback(away);
            } else if (victim.TryGetComponent(out Npcs.Npc npc)) {
                npc.ServerKnockback(away);
            }
            return true;
        }

        // into the world: it stays there, pointing the way it flew, its tip a bit in
        private void serverStick(Vector3 point, Vector3 direction) {
            Vector3 at = point - direction * (ModelLength / 2 - StickDepth);
            FloatingOrigin.ToAbsolute(at, out double x, out double y, out double z);
            stuckX = x;
            stuckY = y;
            stuckZ = z;
            stuckDirection = direction;
            stuck = true;
            stuckAt = NetworkTime.time;
            nextSupportCheck = NetworkTime.time + SupportCheckInterval;
            transform.SetPositionAndRotation(at, Quaternion.LookRotation(direction));
        }

        // stuck: picked up by players walking onto it, falls out when what held it is gone, goes after a while
        private void serverWhileStuck() {
            double now = NetworkTime.time;
            if (now - stuckAt > StuckTime) {
                NetworkServer.Destroy(gameObject);
                return;
            }
            if (now >= nextSupportCheck) {
                nextSupportCheck = now + SupportCheckInterval;
                Vector3 tip = transform.position + transform.forward * (ModelLength / 2 - StickDepth / 2);
                if (!Physics.CheckSphere(tip, Radius, SolidLayers, QueryTriggerInteraction.Ignore)) {
                    DroppedItems.ServerDrop(itemId, color, 1, 0, transform.position, Vector3.zero);
                    NetworkServer.Destroy(gameObject);
                    return;
                }
            }
            if (now - stuckAt < PickableAfter) {
                return;
            }
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn == null || conn.identity == null || conn.identity.GetComponent<Health>().isDead) {
                    continue;
                }
                CharacterController body = conn.identity.GetComponent<CharacterController>();
                Vector3 feet = conn.identity.transform.position + Vector3.down * (body != null ? body.height / 2 : 0.9f);
                Vector3 closest = new Vector3(feet.x, Mathf.Clamp(transform.position.y, feet.y, feet.y + 2), feet.z);
                if (Vector3.Distance(closest, transform.position) <= PickupDistance
                        && conn.identity.GetComponent<PlayerInventory>().ServerAdd(itemId, color, 1)) {
                    NetworkServer.Destroy(gameObject);
                    return;
                }
            }
        }
    }
}
