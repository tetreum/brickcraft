using Brickcraft.Combat;
using Brickcraft.Network;
using Mirror;
using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// A creature of the world, of a kind of NpcDatabase (its type). The server runs it (in active chunks, see
    /// NpcSystem): it picks a target, walks, attacks and dies; everybody sees its model (see NpcModel) play its
    /// animations: idle or walk all the time, attack and death when they happen.
    ///
    /// For now it walks straight to where it goes, stepping up low obstacles (the navigation grid comes next),
    /// and keeps out of fluids (water, lava): it doesn't step onto them or down into them.
    /// When it doesn't get anywhere chasing (a wall, a cliff) it tries going around it, sideways for a moment;
    /// after a few tries it gives up on that target for a while, unless it's hurt by it again.
    /// Its capsule is what it bumps into things with and what players hit (see Combat.Health).
    /// </summary>
    [RequireComponent(typeof(Health), typeof(CapsuleCollider))]
    public class Npc : NetworkBehaviour
    {
        public const string Idle = "idle";
        public const string Walk = "walk";
        public const string Attack = "attack";
        public const string Death = "death";

        /// <summary>Its kind, see NpcDatabase.</summary>
        [SyncVar(hook = nameof(onTypeChanged))] public string type;
        /// <summary>What it plays when nothing else is: idle or walk.</summary>
        [SyncVar(hook = nameof(onLoopChanged))] public string loop = Idle;

        public NpcInfo Info { get; private set; }

        private const float ThinkInterval = 0.25f;
        private const float Gravity = 20;
        private const float TurnSpeed = 540;
        // how long the dead stay, and how long it remembers who hurt it
        private const float DeathLinger = 3;
        private const float ForgetTime = 15;
        private const float KnockbackDrag = 12;
        // a player's radius, for how close it has to be to hit one
        private const float TargetRadius = 0.35f;
        private const float FallLimit = -200;
        private const float FlashTime = 0.15f;
        // how far down it looks for a fluid it would drop into
        private const float DropCheck = 8;
        // what it walks on and bumps into: not water, nor what's on Ignore Raycast (players' own capsules)
        private const int SolidLayers = Physics.DefaultRaycastLayers & ~(1 << (int)Game.Layers.Water);

        private Health health;
        private CapsuleCollider body;
        private NpcModel model;
        private Animator animator;

        // server
        private Health target;
        private Health lastAttacker;
        private double lastAttackedAt;
        private Vector3 heading;      // where it walks, a horizontal unit vector or zero
        private Vector3 facing;       // where it looks
        private float verticalSpeed;
        private Vector3 knockback;
        private double nextThink;
        private double nextAttack;
        private double attackLandsAt = -1;
        private Health attackTarget;
        private double nextWander;
        private bool bumped;          // walked into something since it last thought

        // not getting anywhere: checked every ProgressInterval, then it goes sideways for a moment (a detour)
        private const float ProgressInterval = 1.5f;
        private const float MinProgress = 0.3f;
        private const int MaxDetours = 3;
        private const float GiveUpTime = 10;
        private double progressCheckAt;
        private Vector3 progressPosition;
        private Vector3 progressTargetPosition;
        // the closest it got to the target since the target last moved: detours that only seem to get closer don't count
        private float bestDistance;
        private int detours;
        private double detourUntil;
        private Vector3 detourHeading;
        private Health ignored;
        private double ignoredUntil;

        // client
        private float oneShotUntil = -1;
        private float flashUntil;
        private bool isFlashing;
        private MaterialPropertyBlock flashProperties;

        private void Awake() {
            health = GetComponent<Health>();
            body = GetComponent<CapsuleCollider>();
        }

        public override void OnStartServer() {
            setup();
            health.ServerDamaged += onServerDamaged;
            health.ServerDied += onServerDied;
            NpcSystem.Register(this);
            facing = transform.forward;
        }

        public override void OnStopServer() {
            NpcSystem.Unregister(this);
        }

        public override void OnStartClient() {
            setup();
            health.HealthChanged += onHealthChanged;
            health.DeadChanged += onDeadChanged;
            body.enabled = !health.isDead;
            if (health.isDead) {
                play(Death);
            }
        }

        public override void OnStopClient() {
            health.HealthChanged -= onHealthChanged;
            health.DeadChanged -= onDeadChanged;
        }

        private void onTypeChanged(string oldType, string newType) {
            setup();
        }

        // its model, and a capsule around its hitbox
        private void setup() {
            if (model != null || string.IsNullOrEmpty(type)) {
                return;
            }
            Info = NpcDatabase.Get(type);
            if (Info == null) {
                Debug.LogError("Unknown NPC " + type);
                return;
            }
            GameObject instance = Instantiate(NpcDatabase.ModelOf(Info), transform, false);
            instance.name = "Model";
            instance.transform.localScale = Vector3.one * Info.scale;
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>()) {
                Destroy(collider);
            }
            model = instance.GetComponent<NpcModel>();
            animator = instance.GetComponent<Animator>();

            Bounds hitbox = model.hitbox;
            body.center = hitbox.center * Info.scale;
            body.height = hitbox.size.y * Info.scale;
            body.radius = Mathf.Min(Mathf.Max(hitbox.size.x, hitbox.size.z) * Info.scale / 2, body.height / 2);
            play(loop);
        }

        /// <summary>Its capsule's radius.</summary>
        public float Radius {
            get { return body.radius; }
        }

        private void Update() {
            if (Info == null) {
                return;
            }
            if (isServer) {
                serverUpdate();
            }
            if (isClient) {
                clientUpdate();
            }
        }

        // -------- server --------

        private void serverUpdate() {
            if (health.isDead || !NpcSystem.IsActive(transform.position)) {
                return;
            }
            double now = NetworkTime.time;
            if (attackLandsAt >= 0 && now >= attackLandsAt) {
                attackLandsAt = -1;
                landAttack();
            }
            if (now >= nextThink) {
                nextThink = now + ThinkInterval;
                think(now);
            }
            move(Time.deltaTime);
        }

        private void think(double now) {
            Health previous = target;
            target = chooseTarget(now);
            if (target != previous) {
                resetProgress(now);
            }
            if (target == null) {
                wander(now);
                return;
            }
            Vector3 to = target.transform.position - transform.position;
            to.y = 0;
            if (isInReach(target)) {
                facing = to;
                heading = Vector3.zero;
                resetProgress(now);
                tryAttack(now);
                return;
            }
            if (now < detourUntil) {
                heading = detourHeading;
                facing = heading;
                return;
            }
            if (now >= progressCheckAt) {
                checkProgress(now, to);
                if (target == null || now < detourUntil) {
                    return; // gave up, or going around
                }
            }
            heading = to.normalized;
            facing = to;
        }

        // from its center to the center of who it can hit
        private float reach {
            get { return body.radius + Info.attack.range + TargetRadius; }
        }

        // close enough sideways, and not too far above or below (on a cliff...)
        private bool isInReach(Health victim) {
            Vector3 to = victim.transform.position - transform.position;
            float up = to.y;
            to.y = 0;
            return to.magnitude <= reach && Mathf.Abs(up) <= body.height;
        }

        private void resetProgress(double now) {
            progressCheckAt = now + ProgressInterval;
            progressPosition = transform.position;
            progressTargetPosition = target != null ? target.transform.position : Vector3.zero;
            bestDistance = target != null ? horizontalDistance(transform.position, target.transform.position) : 0;
            detours = 0;
            detourUntil = 0;
        }

        // stuck: it hardly moved, or it got no closer than it ever did (while the target stood still)
        private void checkProgress(double now, Vector3 to) {
            float moved = horizontalDistance(transform.position, progressPosition);
            float distance = to.magnitude;
            if (horizontalDistance(target.transform.position, progressTargetPosition) >= 1) {
                bestDistance = float.MaxValue; // it went somewhere else: a fresh start
                detours = 0;
            }
            bool closer = distance < bestDistance - MinProgress;
            bool stuck = moved < Info.speed * ProgressInterval * 0.25f || !closer;

            progressCheckAt = now + ProgressInterval;
            progressPosition = transform.position;
            progressTargetPosition = target.transform.position;
            bestDistance = Mathf.Min(bestDistance, distance);
            if (!stuck) {
                detours = 0;
                return;
            }
            if (++detours > MaxDetours || !startDetour(now, to)) {
                // can't get there: somebody else, or wandering, for a while
                ignored = target;
                ignoredUntil = now + GiveUpTime;
                target = null;
                detours = 0;
                heading = Vector3.zero;
            }
        }

        // the free direction closest to the target's, trying both sides further and further round
        private bool startDetour(double now, Vector3 to) {
            float side = Random.value < 0.5f ? 1 : -1;
            foreach (float angle in new[] { 45f, 90f, 135f }) {
                for (int s = 0; s < 2; s++, side = -side) {
                    Vector3 direction = Quaternion.Euler(0, angle * side, 0) * to.normalized;
                    if (canWalk(direction, 1)) {
                        detourHeading = direction;
                        detourUntil = now + Random.Range(1.5f, 2.5f);
                        heading = direction;
                        facing = direction;
                        return true;
                    }
                }
            }
            return false;
        }

        // it can go that way for that distance (checked with its capsule off)
        private bool canWalk(Vector3 direction, float distance) {
            body.enabled = false;
            bool free = isWalkable(transform.position, direction * distance);
            body.enabled = true;
            return free;
        }

        // nothing in the way, the world is there, and no fluid under where it'd be (swimmers come with the navigation grid)
        private bool isWalkable(Vector3 position, Vector3 step) {
            if (isBlocked(position, step) || !NpcSystem.IsLoaded(position + step)) {
                return false;
            }
            if (isFluidAt(position + Vector3.up * 0.1f)) {
                return true; // already in it: it can walk out
            }
            Vector3 from = position + step + Vector3.up * (Info.stepHeight + 0.05f);
            // water bricks are on the Water layer, which it otherwise walks through
            return !(Physics.Raycast(from, Vector3.down, out RaycastHit below, DropCheck, SolidLayers | (1 << (int)Game.Layers.Water), QueryTriggerInteraction.Ignore)
                && isFluid(below));
        }

        // what a ray hit is a fluid: a fluid block of the terrain, or a water (or other fluid) brick
        private static bool isFluid(RaycastHit hit) {
            Brick brick = Server.findBrick(hit.collider);
            if (brick != null) {
                return isFluid(brick.item);
            }
            return isFluidAt(hit.point - hit.normal * 0.01f);
        }

        private static bool isFluid(Item item) {
            return item.layer == (int)Game.Layers.Water || (item.blockType.HasValue && World.BlockDatabase.Get(item.blockType.Value).isFluid);
        }

        // the terrain block or brick at a point is a fluid
        private static bool isFluidAt(Vector3 point) {
            Vector3Int cell = Bricks.BrickGrid.WorldToCell(point);
            Brick brick = Bricks.BrickGrid.GetBrickAt(cell);
            if (brick != null) {
                return isFluid(brick.item);
            }
            World.WorldBehaviour world = World.WorldBehaviour.Instance;
            return world != null && World.BlockDatabase.Get(world.GetBlockType(Bricks.BrickGrid.CellToBlock(cell))).isFluid;
        }

        private static float horizontalDistance(Vector3 a, Vector3 b) {
            a.y = 0;
            b.y = 0;
            return Vector3.Distance(a, b);
        }

        // who hurt it (until it forgets or loses them), or for aggressive ones the nearest player in sight
        private Health chooseTarget(double now) {
            if (now >= ignoredUntil) {
                ignored = null;
            }
            if (canTarget(lastAttacker, Info.sightRange * 1.5f) && now - lastAttackedAt < ForgetTime) {
                return lastAttacker;
            }
            lastAttacker = null;
            if (Info.behaviour != "aggressive" || WorldNetwork.Difficulty == Difficulty.Peaceful) {
                return null;
            }
            Health nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                Health player = conn.identity != null ? conn.identity.GetComponent<Health>() : null;
                if (player != ignored && canTarget(player, Info.sightRange)) {
                    float distance = Vector3.Distance(transform.position, player.transform.position);
                    if (distance < nearestDistance) {
                        nearest = player;
                        nearestDistance = distance;
                    }
                }
            }
            return nearest;
        }

        private bool canTarget(Health candidate, float range) {
            return candidate != null && !candidate.isDead && candidate != health
                && Vector3.Distance(transform.position, candidate.transform.position) <= range;
        }

        // now and then it walks somewhere for a while, or stands
        private void wander(double now) {
            if (bumped) {
                nextWander = 0; // walked into a wall: somewhere else
            }
            bumped = false;
            if (now < nextWander) {
                return;
            }
            nextWander = now + Random.Range(2f, 6f);
            if (Random.value < 0.5f) {
                Vector2 direction = Random.insideUnitCircle.normalized;
                heading = new Vector3(direction.x, 0, direction.y);
                facing = heading;
            } else {
                heading = Vector3.zero;
            }
        }

        private void tryAttack(double now) {
            if (now < nextAttack) {
                return;
            }
            nextAttack = now + Info.attack.cooldown;
            attackLandsAt = now + Info.attack.hitTime;
            attackTarget = target;
            RpcPlay(Attack);
        }

        // the hit lands if who it swung at is still within reach
        private void landAttack() {
            if (attackTarget == null || attackTarget.isDead) {
                return;
            }
            Vector3 away = attackTarget.transform.position - transform.position;
            away.y = 0;
            if (away.magnitude > reach * 1.25f || Mathf.Abs(attackTarget.transform.position.y - transform.position.y) > body.height) {
                return;
            }
            int damage = damageFor(attackTarget);
            bool hurt = damage > 0 && attackTarget.ServerDamage(new DamageInfo() {
                amount = damage,
                kind = DamageInfo.Kind.Melee,
                attacker = gameObject,
                attackerName = Info.name,
            });
            if (hurt) {
                Vector3 push = away.normalized * Info.attack.knockback + Vector3.up * 3;
                PlayerNetwork player = attackTarget.GetComponent<PlayerNetwork>();
                if (player != null) {
                    player.ServerKnockback(push);
                } else if (attackTarget.TryGetComponent(out Npc npc)) {
                    npc.ServerKnockback(push);
                }
            }
        }

        // players are hurt more in hard worlds, and not at all in peaceful ones
        private int damageFor(Health victim) {
            if (victim.GetComponent<PlayerNetwork>() == null) {
                return Info.attack.damage;
            }
            switch (WorldNetwork.Difficulty) {
                case Difficulty.Peaceful:
                    return 0;
                case Difficulty.Hard:
                    return Mathf.CeilToInt(Info.attack.damage * 1.5f);
                default:
                    return Info.attack.damage;
            }
        }

        /// <summary>Pushes it (a hit): away for a moment, and up.</summary>
        [Server]
        public void ServerKnockback(Vector3 velocity) {
            knockback = new Vector3(velocity.x, 0, velocity.z);
            verticalSpeed = Mathf.Max(verticalSpeed, velocity.y);
        }

        private void onServerDamaged(DamageInfo damage) {
            Health attacker = damage.attacker != null ? damage.attacker.GetComponent<Health>() : null;
            if (attacker != null) {
                lastAttacker = attacker;
                lastAttackedAt = NetworkTime.time;
                if (attacker == ignored) {
                    ignored = null; // it'll try again
                }
                nextThink = 0; // turns on them right away
            }
        }

        // whoever killed it gets its drops; it stays a moment to fall
        private void onServerDied(DamageInfo damage) {
            heading = Vector3.zero;
            body.enabled = false;
            RpcPlay(Death);
            PlayerInventory killer = damage.attacker != null ? damage.attacker.GetComponent<PlayerInventory>() : null;
            if (killer != null) {
                foreach (DropInfo drop in Info.drops) {
                    if (Random.value <= drop.chance && Server.items.TryGetValue(drop.item ?? "", out Item item)) {
                        killer.ServerAdd(item.id, item.color, drop.count);
                    }
                }
            }
            Invoke(nameof(despawn), DeathLinger);
        }

        private void despawn() {
            NetworkServer.Destroy(gameObject);
        }

        // walks where it heads, stepping up what's low enough and falling down what isn't under it
        private void move(float deltaTime) {
            Vector3 position = transform.position;
            Vector3 step = (heading * Info.speed + knockback) * deltaTime;
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, KnockbackDrag * deltaTime);

            body.enabled = false; // its own capsule isn't in the way
            if (step.sqrMagnitude > 0 && !isWalkable(position, step)) {
                step = Vector3.zero;
                knockback = Vector3.zero;
                bumped = true;
            }
            Vector3 next = position + step;

            verticalSpeed -= Gravity * deltaTime;
            float rise = verticalSpeed * deltaTime;
            float probe = Info.stepHeight + 0.05f;
            if (verticalSpeed <= 0 && Physics.Raycast(next + Vector3.up * probe, Vector3.down, out RaycastHit ground,
                    probe - rise + 0.05f, SolidLayers, QueryTriggerInteraction.Ignore) && ground.point.y >= next.y + rise) {
                next.y = ground.point.y; // on the ground (or stepping up onto it)
                verticalSpeed = 0;
            } else {
                next.y += rise;
            }
            body.enabled = true;

            transform.position = next;
            if (facing.sqrMagnitude > 0.001f) {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), TurnSpeed * deltaTime);
            }

            string wanted = heading.sqrMagnitude > 0 ? Walk : Idle;
            if (loop != wanted) {
                loop = wanted;
            }
            if (next.y < FallLimit) {
                despawn(); // fell out of the world
            }
        }

        // something in the way above its step height (a wall, another NPC, a player)
        private bool isBlocked(Vector3 position, Vector3 step) {
            float radius = body.radius * 0.9f;
            Vector3 bottom = position + Vector3.up * (Info.stepHeight + radius);
            Vector3 top = position + Vector3.up * Mathf.Max(Info.stepHeight + radius, body.center.y * 2 - radius);
            return Physics.CapsuleCast(bottom, top, radius, step.normalized, step.magnitude + 0.05f, SolidLayers, QueryTriggerInteraction.Ignore);
        }

        // -------- everybody --------

        [ClientRpc]
        private void RpcPlay(string animation) {
            play(animation);
        }

        private void onLoopChanged(string oldLoop, string newLoop) {
            if (oneShotUntil < 0) {
                play(newLoop);
            }
        }

        // loops (idle, walk) play until told otherwise; the others once, then the loop again (death stays)
        private void play(string animation) {
            if (animator == null) {
                return;
            }
            string state = NpcDatabase.AnimationOf(Info, animation);
            if (System.Array.IndexOf(model.animations, state) < 0) {
                return;
            }
            animator.CrossFadeInFixedTime(state, 0.1f, 0, 0);
            bool isLoop = animation == Idle || animation == Walk;
            oneShotUntil = isLoop ? -1 : animation == Death ? float.MaxValue : Time.time + model.DurationOf(state);
        }

        private void clientUpdate() {
            if (oneShotUntil >= 0 && Time.time >= oneShotUntil) {
                oneShotUntil = -1;
                play(loop);
            }
            bool flash = Time.time < flashUntil;
            if (flash != isFlashing) {
                isFlashing = flash;
                showFlash(flash);
            }
        }

        private void onHealthChanged(int oldHealth, int newHealth) {
            if (newHealth < oldHealth) {
                flashUntil = Time.time + FlashTime;
            }
        }

        private void onDeadChanged(bool dead) {
            body.enabled = !dead;
        }

        // hurt: tinted red for a moment
        private void showFlash(bool flash) {
            if (model == null) {
                return;
            }
            flashProperties = flashProperties ?? new MaterialPropertyBlock();
            foreach (Renderer part in model.GetComponentsInChildren<Renderer>()) {
                part.GetPropertyBlock(flashProperties);
                flashProperties.SetColor("_BaseColor", flash ? new Color(1, 0.35f, 0.35f) : Color.white);
                part.SetPropertyBlock(flashProperties);
            }
        }
    }
}
