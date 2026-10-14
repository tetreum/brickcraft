using Brickcraft.Combat;
using Brickcraft.Network;
using Brickcraft.Npcs.Navigation;
using Mirror;
using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>What an NPC is doing, as its brain (or a script) ordered.</summary>
    public enum NpcOrderKind : byte
    {
        Idle = 0,
        Wander = 1,
        /// <summary>To a place, within a range of it.</summary>
        MoveTo = 2,
        /// <summary>After someone, staying within a range of them.</summary>
        Follow = 3,
        /// <summary>After someone, hitting them when they're in reach.</summary>
        Attack = 4,
    }

    /// <summary>
    /// A creature of the world, of a kind of NpcDatabase (its type). The server runs it (in active chunks, see
    /// NpcSystem); everybody sees its model (see NpcModel) play its animations: idle or walk all the time,
    /// attack and death when they happen. Its capsule is what it bumps into things with and what players hit.
    ///
    /// This is its body: it does what it's ordered (Order: idle, wander, go somewhere, follow or attack
    /// someone), and its brain (NpcBrain: senses, targets, pets, scripts) decides the orders. It goes along
    /// paths of the navigation grid (see Navigation.NavGrid), asked for again as who it goes after moves;
    /// where there's no grid it walks straight. Moving, it steps up what's low enough and keeps out of fluids.
    /// When it doesn't get anywhere (something the grid doesn't know of in the way: an NPC, a player) it asks for
    /// a new path or goes sideways for a moment; when the best path can't take it closer, it can't get there
    /// (IsUnreachable) and stops. On its way it dodges other NPCs and players (NpcSteering), and attacking it
    /// takes its place around its target, waiting its turn if too many are at it already (NpcGroups).
    /// </summary>
    [RequireComponent(typeof(Health), typeof(CapsuleCollider))]
    public class Npc : NetworkBehaviour
    {
        public const string Idle = "idle";
        public const string Walk = "walk";
        public const string Attack = "attack";
        public const string Death = "death";
        public const string Sleep = "sleep";

        /// <summary>Its kind, see NpcDatabase.</summary>
        [SyncVar(hook = nameof(onTypeChanged))] public string type;
        /// <summary>What it plays when nothing else is: idle or walk.</summary>
        [SyncVar(hook = nameof(onLoopChanged))] public string loop = Idle;
        /// <summary>Its owner's name if it's a pet, empty otherwise.</summary>
        [SyncVar] public string ownerName = "";
        /// <summary>It's lying down (asleep): its model lies on its back, unless it has a sleep animation.</summary>
        [SyncVar(hook = nameof(onLyingChanged))] public bool isLying;

        /// <summary>Server: where it lives, an absolute cell (where it appeared, unless set): it sleeps in a bed near it, stays or wanders around it (see NpcInfo.schedule).</summary>
        public Vector3Int Home { get; set; }
        /// <summary>Server: it appeared by itself (NpcSpawner): it may go away (see SpawnInfo.atDay).</summary>
        public bool IsNatural { get; set; }
        /// <summary>Server: the bed it sleeps in (a brick id), null if none, see NpcBeds.</summary>
        public string BedId { get; set; }

        public NpcInfo Info { get; private set; }
        public Health Health { get; private set; }
        /// <summary>Server: what decides its orders.</summary>
        public NpcBrain Brain { get; private set; }

        private const float ThinkInterval = 0.25f;
        private const float Gravity = 20;
        private const float TurnSpeed = 540;
        private const float DeathLinger = 3;
        private const float KnockbackDrag = 12;
        // a player's radius, for how close it has to be to hit one
        private const float TargetRadius = 0.35f;
        private const float FallLimit = -200;
        private const float FlashTime = 0.15f;
        // how far down it looks for a fluid it would drop into
        private const float DropCheck = 8;
        // what it walks on and bumps into: not water, nor what's on Ignore Raycast (players' own capsules)
        private const int SolidLayers = Physics.DefaultRaycastLayers & ~(1 << (int)Game.Layers.Water);

        private CapsuleCollider body;
        private NpcModel model;

        // server: its order
        private NpcOrderKind order;
        private Health orderTarget;
        private Vector3 orderPosition;
        private float orderRange;
        private Vector3Int wanderHome;      // wandering: around there, within wanderRange (anywhere if 0)
        private float wanderRange;

        private Vector3 heading;      // where it walks, a horizontal unit vector or zero
        private Vector3 steering;     // the velocity NpcSteering found, used while it's fresh
        private float steeringAt = -1;
        private const float SteeringFreshness = 0.3f;
        private bool slotFailed;      // its place around its target can't be reached: it goes straight for them
        private Vector3 facing;       // where it looks
        private float verticalSpeed;
        private Vector3 knockback;
        private double nextThink;
        private double nextAttack;
        private double attackLandsAt = -1;
        private Health attackTarget;
        private double nextWander;
        private bool bumped;          // walked into something since it last thought

        // not getting anywhere: checked every ProgressInterval
        private const float ProgressInterval = 1.5f;
        private const float MinProgress = 0.3f;
        private const int MaxStrikes = 3;
        // a new path when the destination moved this far from where the last one went, or this often
        private const float RepathDistance = 1.5f;
        private const float RepathInterval = 2;
        private const float WaypointReach = 0.25f;
        private double progressCheckAt;
        private Vector3 progressPosition;
        private Vector3 progressDestination;
        // the closest it got to the destination since it last moved: detours that only seem to get closer don't count
        private float bestDistance;
        private int strikes;
        private NavAgent agent;
        private NavPath path;
        private int pathIndex;
        private bool isPathPending;
        private double nextRepath;
        private Vector3 pathGoal;
        private double detourUntil;
        private Vector3 detourHeading;

        // client
        private float oneShotUntil = -1;
        private float flashUntil;
        private bool isFlashing;
        private MaterialPropertyBlock flashProperties;

        private void Awake() {
            Health = GetComponent<Health>();
            body = GetComponent<CapsuleCollider>();
        }

        public override void OnStartServer() {
            Home = Bricks.BrickGrid.WorldToCell(transform.position + Vector3.up * 0.05f);
            setup();
            Health.ServerDied += onServerDied;
            facing = transform.forward;
            Brain = new NpcBrain(this);
            NpcSystem.Register(this);
            Brain.Start();
        }

        public override void OnStopServer() {
            NpcSystem.Unregister(this);
            NpcGroups.Forget(this);
            NpcBeds.Release(this);
        }

        public override void OnStartClient() {
            setup();
            Health.HealthChanged += onHealthChanged;
            Health.DeadChanged += onDeadChanged;
            body.enabled = !Health.isDead;
            if (Health.isDead) {
                play(Death);
            }
        }

        public override void OnStopClient() {
            Health.HealthChanged -= onHealthChanged;
            Health.DeadChanged -= onDeadChanged;
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

            Bounds hitbox = model.hitbox;
            body.center = hitbox.center * Info.scale;
            body.height = hitbox.size.y * Info.scale;
            body.radius = Mathf.Min(Mathf.Max(hitbox.size.x, hitbox.size.z) * Info.scale / 2, body.height / 2);
            agent = NavGrid.AgentFor(Info, body.radius, body.height);
            play(loop);
            showLying();
        }

        /// <summary>Its capsule's radius.</summary>
        public float Radius {
            get { return body.radius; }
        }

        public float Height {
            get { return body.height; }
        }

        /// <summary>Where it looks from.</summary>
        public Vector3 EyePosition {
            get { return transform.position + Vector3.up * (body.height * 0.85f); }
        }

        public bool IsPet {
            get { return Brain != null ? Brain.Owner != 0 : ownerName.Length > 0; }
        }

        /// <summary>Server: how fast it moved last, sideways.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Server: how fast it wants to move, sideways (where it heads at its speed).</summary>
        public Vector3 PreferredVelocity {
            get { return heading * Info.speed; }
        }

        /// <summary>Server: a velocity that dodges who's around, see NpcSteering.</summary>
        public void SetSteering(Vector3 velocity) {
            steering = velocity;
            steeringAt = Time.time;
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

        // -------- server: orders --------

        public NpcOrderKind Order {
            get { return order; }
        }

        public Health OrderTarget {
            get { return orderTarget; }
        }

        /// <summary>It can't get to where it was ordered: it stopped. Cleared by the next order.</summary>
        public bool IsUnreachable { get; private set; }

        /// <summary>It got to where it was ordered to move (MoveTo), or it's within range of who it follows.</summary>
        public bool HasArrived { get; private set; }

        [Server]
        public void OrderIdle() {
            setOrder(NpcOrderKind.Idle, null, Vector3.zero, 0);
        }

        [Server]
        public void OrderWander() {
            wanderRange = 0;
            setOrder(NpcOrderKind.Wander, null, Vector3.zero, 0);
        }

        /// <summary>Wanders, but not further than range meters from that cell.</summary>
        [Server]
        public void OrderWanderNear(Vector3Int cell, float range) {
            wanderHome = cell;
            wanderRange = range;
            setOrder(NpcOrderKind.Wander, null, Vector3.zero, range);
        }

        /// <summary>Lies down there (asleep), still until it gets an order other than standing.</summary>
        [Server]
        public void LieDown(Vector3 position, Quaternion rotation) {
            OrderIdle();
            heading = Vector3.zero;
            knockback = Vector3.zero;
            verticalSpeed = 0;
            transform.SetPositionAndRotation(position, rotation);
            facing = rotation * Vector3.forward;
            isLying = true;
            loop = Sleep; // it doesn't move while lying, nothing else changes it
        }

        /// <summary>Gets up (it was lying), stepping off what it lay on.</summary>
        [Server]
        public void GetUp() {
            if (!isLying) {
                return;
            }
            isLying = false;
            loop = Idle;
            Transform here = transform;
            body.enabled = false;
            if (NpcSystem.SpotNear(here, body.radius, body.height, out Vector3 spot)) {
                ServerTeleport(spot);
            }
            body.enabled = true;
        }

        /// <summary>To a local position, close enough within range meters.</summary>
        [Server]
        public void OrderMoveTo(Vector3 position, float range = 0.5f) {
            setOrder(NpcOrderKind.MoveTo, null, position, range);
        }

        [Server]
        public void OrderFollow(Health target, float range) {
            setOrder(NpcOrderKind.Follow, target, Vector3.zero, range);
        }

        [Server]
        public void OrderAttack(Health target) {
            setOrder(NpcOrderKind.Attack, target, Vector3.zero, 0);
        }

        // the same order again keeps how it's going about it; lying, anything but standing gets it up
        private void setOrder(NpcOrderKind kind, Health target, Vector3 position, float range) {
            if (isLying && kind != NpcOrderKind.Idle) {
                GetUp();
            }
            bool same = kind == order && target == orderTarget && range == orderRange
                && (kind != NpcOrderKind.MoveTo || horizontalDistance(position, orderPosition) < RepathDistance);
            if (order == NpcOrderKind.Attack && (kind != NpcOrderKind.Attack || target != orderTarget)) {
                NpcGroups.ReleaseSlot(this, orderTarget);
                slotFailed = false;
            }
            order = kind;
            orderTarget = target;
            orderPosition = position;
            orderRange = range;
            if (!same) {
                resetProgress(NetworkTime.time);
                IsUnreachable = false;
                HasArrived = false;
            }
        }

        /// <summary>Moves it there right away (a pet catching up with its owner).</summary>
        [Server]
        public void ServerTeleport(Vector3 position) {
            transform.position = position;
            verticalSpeed = 0;
            knockback = Vector3.zero;
            resetProgress(NetworkTime.time);
        }

        // -------- server: every frame --------

        private void serverUpdate() {
            if (Health.isDead || !NpcSystem.IsActive(transform.position)) {
                return;
            }
            double now = NetworkTime.time;
            if (attackLandsAt >= 0 && now >= attackLandsAt) {
                attackLandsAt = -1;
                landAttack();
            }
            if (now >= nextThink) {
                nextThink = now + ThinkInterval;
                Brain.Think(now);
                if (!isLying) {
                    act(now);
                }
            }
            if (!isLying) {
                move(Time.deltaTime);
            }
            if (NavGrid.DebugDraw && path != null) {
                for (int i = 1; i < path.Count; i++) {
                    Debug.DrawLine(path.PointAt(i - 1) + Vector3.up * 0.1f, path.PointAt(i) + Vector3.up * 0.1f, path.result == NavResult.Found ? Color.yellow : Color.red);
                }
            }
        }

        /// <summary>Thinks again next frame (it was just hurt...).</summary>
        public void ThinkSoon() {
            nextThink = 0;
        }

        // carries its order out
        private void act(double now) {
            if (orderTarget != null && orderTarget.isDead) {
                orderTarget = null;
            }
            switch (order) {
                case NpcOrderKind.Wander:
                    wander(now);
                    break;
                case NpcOrderKind.MoveTo:
                    goTo(now, orderPosition, orderRange);
                    break;
                case NpcOrderKind.Follow:
                    if (orderTarget == null) {
                        stand();
                    } else {
                        goTo(now, orderTarget.transform.position, orderRange);
                    }
                    break;
                case NpcOrderKind.Attack:
                    if (orderTarget == null) {
                        stand();
                    } else {
                        attack(now);
                    }
                    break;
                default:
                    stand();
                    break;
            }
        }

        private void stand() {
            heading = Vector3.zero;
        }

        // from its place around the target, when it's its turn; otherwise to its place, facing them
        private void attack(double now) {
            Vector3 slot = NpcGroups.AttackSlot(this, orderTarget, out bool mayAttack);
            Vector3 toTarget = orderTarget.transform.position - transform.position;
            toTarget.y = 0;
            if (mayAttack && IsInReach(orderTarget)) {
                facing = toTarget;
                heading = Vector3.zero;
                resetProgress(now);
                tryAttack(now);
                return;
            }
            if (slotFailed) {
                goTo(now, orderTarget.transform.position, Info.attack.range + body.radius);
            } else {
                goTo(now, slot, 0.25f);
                if (IsUnreachable) {
                    // its place is somewhere it can't go (in a wall...): straight for them then
                    slotFailed = true;
                    IsUnreachable = false;
                    resetProgress(now);
                }
            }
            if (toTarget.magnitude < 5 && heading == Vector3.zero) {
                facing = toTarget; // waiting its turn, it watches them
            }
        }

        // along a path to a (maybe moving) destination, stopping within range of it
        private void goTo(double now, Vector3 destination, float range) {
            Vector3 to = destination - transform.position;
            to.y = 0;
            if (IsUnreachable) {
                heading = Vector3.zero;
                facing = to;
                return;
            }
            if (to.magnitude <= range && Mathf.Abs(destination.y - transform.position.y) <= body.height) {
                heading = Vector3.zero;
                HasArrived = true;
                resetProgress(now);
                return;
            }
            HasArrived = false;
            if (now < detourUntil) {
                heading = detourHeading;
                facing = heading;
                return;
            }
            if (!isPathPending && (path == null || now >= nextRepath || horizontalDistance(destination, pathGoal) > RepathDistance)) {
                requestPath(now, destination, range);
            }
            if (now >= progressCheckAt) {
                checkProgress(now, destination, to);
                if (IsUnreachable || now < detourUntil) {
                    return; // stopped, or going around
                }
            }
            followPath(to);
        }

        private void requestPath(double now, Vector3 destination, float range) {
            isPathPending = true;
            nextRepath = now + RepathInterval;
            pathGoal = destination;
            NavGrid.RequestPath(agent, transform.position, destination, range, result => {
                isPathPending = false;
                if (this == null || Health.isDead) {
                    return;
                }
                path = result.result == NavResult.Failed ? null : result;
                pathIndex = 1;
            });
        }

        // towards the next waypoint; straight at the destination without a path; still at the end of one that
        // can't get closer
        private void followPath(Vector3 toDestination) {
            facing = toDestination;
            if (path == null) {
                heading = toDestination.normalized;
                return;
            }
            while (pathIndex < path.Count && horizontalDistance(transform.position, path.PointAt(pathIndex)) < WaypointReach) {
                pathIndex++;
            }
            if (pathIndex < path.Count) {
                Vector3 to = path.PointAt(pathIndex) - transform.position;
                to.y = 0;
                heading = to.normalized;
                facing = to;
            } else {
                heading = path.result == NavResult.Partial ? Vector3.zero : toDestination.normalized;
            }
        }

        private bool isFollowingPath {
            get { return path != null && pathIndex < path.Count; }
        }

        // from its center to the center of who it can hit
        private float reach {
            get { return body.radius + Info.attack.range + TargetRadius; }
        }

        /// <summary>Close enough to hit them: sideways, and not too far above or below (on a cliff...).</summary>
        public bool IsInReach(Health victim) {
            Vector3 to = victim.transform.position - transform.position;
            float up = to.y;
            to.y = 0;
            return to.magnitude <= reach && Mathf.Abs(up) <= body.height;
        }

        private void resetProgress(double now) {
            progressCheckAt = now + ProgressInterval;
            progressPosition = transform.position;
            progressDestination = orderTarget != null ? orderTarget.transform.position : orderPosition;
            bestDistance = horizontalDistance(transform.position, progressDestination);
            strikes = 0;
            detourUntil = 0;
            path = null;
        }

        // stuck: following a path, it hardly moved (something the grid doesn't know of is in the way); at the end
        // of a path that can't get closer, it can't get there; without a path, it got no closer than it ever did
        private void checkProgress(double now, Vector3 destination, Vector3 to) {
            float moved = horizontalDistance(transform.position, progressPosition);
            float distance = to.magnitude;
            if (horizontalDistance(destination, progressDestination) >= 1) {
                bestDistance = float.MaxValue; // it went somewhere else: a fresh start
                strikes = 0;
            }
            bool blocked = moved < Info.speed * ProgressInterval * 0.25f;
            bool stuck = isFollowingPath ? blocked
                : path != null ? path.result == NavResult.Partial
                : blocked || distance >= bestDistance - MinProgress;

            progressCheckAt = now + ProgressInterval;
            progressPosition = transform.position;
            progressDestination = destination;
            bestDistance = Mathf.Min(bestDistance, distance);
            if (!stuck) {
                strikes = 0;
                return;
            }
            strikes++;
            bool tried = strikes <= MaxStrikes;
            if (tried && isFollowingPath && strikes % 2 == 1) {
                nextRepath = 0; // a new path, around what's in the way now
            } else if (tried && (path == null || isFollowingPath)) {
                tried = startDetour(now, to);
            } else if (tried) {
                nextRepath = 0; // the destination may be somewhere else by then
            }
            if (!tried) {
                IsUnreachable = true;
                strikes = 0;
                path = null;
                heading = Vector3.zero;
            }
        }

        // the free direction closest to the destination's, trying both sides further and further round
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
            Vector3 towardHome = HomeOffset(wanderHome);
            if (wanderRange > 0 && towardHome.magnitude > wanderRange) {
                // too far from home: back towards it
                heading = towardHome.normalized;
                facing = heading;
            } else if (Random.value < 0.5f) {
                Vector2 direction = Random.insideUnitCircle.normalized;
                heading = new Vector3(direction.x, 0, direction.y);
                facing = heading;
            } else {
                heading = Vector3.zero;
            }
        }

        // it can go that way for that distance (checked with its capsule off)
        private bool canWalk(Vector3 direction, float distance) {
            body.enabled = false;
            bool free = isWalkable(transform.position, direction * distance);
            body.enabled = true;
            return free;
        }

        // nothing in the way, the world is there, and no fluid under where it'd be (swimmers come later)
        private bool isWalkable(Vector3 position, Vector3 step) {
            if (isBlocked(position, step) || !NpcSystem.IsLoaded(position + step)) {
                return false;
            }
            if (isFluidAt(position + Vector3.up * 0.1f)) {
                return true; // already in it: it can walk out
            }
            Vector3 from = position + step + Vector3.up * (Info.stepHeight + 0.05f);
            // water bricks are on the Water layer, which it otherwise walks through
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit below, DropCheck, SolidLayers | (1 << (int)Game.Layers.Water), QueryTriggerInteraction.Ignore)) {
                return true;
            }
            // the terrain's fluids have no collider (players swim in them): the ray goes through a lake to its
            // bottom, so where its feet would be is checked too
            return !isFluid(below) && !isFluidAt(below.point + Vector3.up * 0.05f);
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

        public static float horizontalDistance(Vector3 a, Vector3 b) {
            a.y = 0;
            b.y = 0;
            return Vector3.Distance(a, b);
        }

        // -------- server: attacking --------

        private void tryAttack(double now) {
            if (now < nextAttack) {
                return;
            }
            nextAttack = now + Info.attack.cooldown;
            attackLandsAt = now + Info.attack.hitTime;
            attackTarget = orderTarget;
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
            NpcPerception.MakeNoise(transform.position, 8, Health, Noise.Fight);
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

        // players are hurt more in hard worlds, and not at all in peaceful ones; pets only hurt players with PvP
        private int damageFor(Health victim) {
            if (victim.GetComponent<PlayerNetwork>() == null) {
                return Info.attack.damage;
            }
            if (IsPet && !WorldNetwork.Pvp) {
                return 0;
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

        // killed by a player, its drops fall around it (see DroppedItems); it stays a moment to fall
        private void onServerDied(DamageInfo damage) {
            heading = Vector3.zero;
            body.enabled = false;
            RpcPlay(Death);
            PlayerInventory killer = damage.attacker != null ? damage.attacker.GetComponent<PlayerInventory>() : null;
            if (killer != null && !IsPet) {
                Vector3 where = transform.position + Vector3.up * (body.height / 2);
                foreach (DropInfo drop in Info.drops) {
                    if (Random.value <= drop.chance && Server.items.TryGetValue(drop.item ?? "", out Item item)) {
                        Vector2 away = Random.insideUnitCircle * 1.5f;
                        DroppedItems.ServerDrop(item.id, item.color, drop.count, 0, where, new Vector3(away.x, 3, away.y));
                    }
                }
            }
            Invoke(nameof(Despawn), DeathLinger);
        }

        /// <summary>Takes it out of the world (nobody gets its drops).</summary>
        [Server]
        public void Despawn() {
            NetworkServer.Destroy(gameObject);
        }

        // -------- server: moving --------

        public Vector3 HomePosition {
            get { return Bricks.BrickGrid.CellToWorld(Home, new Vector3(0.5f, 0, 0.5f)); }
        }

        /// <summary>From it to that cell, sideways.</summary>
        public Vector3 HomeOffset(Vector3Int cell) {
            Vector3 offset = Bricks.BrickGrid.CellToWorld(cell, new Vector3(0.5f, 0, 0.5f)) - transform.position;
            offset.y = 0;
            return offset;
        }

        // walks where it heads, stepping up what's low enough and falling down what isn't under it
        private void move(float deltaTime) {
            Vector3 position = transform.position;
            // dodging who's around while it goes somewhere (standing, it holds its place)
            Vector3 walk = heading * Info.speed;
            if (heading != Vector3.zero && Time.time - steeringAt < SteeringFreshness) {
                walk = Vector3.ClampMagnitude(steering, Info.speed);
            }
            // flowing water pushes it along (see Water.CurrentAt)
            Vector3 current = World.Water.CurrentAt(position + Vector3.up * 0.1f);
            current.y = 0;
            Vector3 step = (walk + knockback + current) * deltaTime;
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, KnockbackDrag * deltaTime);

            body.enabled = false; // its own capsule isn't in the way
            if (step.sqrMagnitude > 0 && !isWalkable(position, step)) {
                step = Vector3.zero;
                knockback = Vector3.zero;
                bumped = true;
            }
            Vector3 next = position + step;
            Velocity = deltaTime > 0 ? new Vector3(step.x, 0, step.z) / deltaTime : Vector3.zero;

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

            string wanted = isLying ? Sleep : Velocity.sqrMagnitude > 0.01f ? Walk : Idle;
            if (loop != wanted) {
                loop = wanted;
            }
            if (next.y < FallLimit) {
                Despawn(); // fell out of the world
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

        // lying without a sleep animation: on its back, its head away from where it faces (the bed's
        // SleepPoint faces its feet), its back on what it lies on
        private void onLyingChanged(bool wasLying, bool lying) {
            showLying();
        }

        private void showLying() {
            if (model == null) {
                return;
            }
            bool lieDown = isLying && !model.Has(NpcDatabase.AnimationOf(Info, Sleep));
            model.transform.localRotation = lieDown ? Quaternion.Euler(-90, 0, 0) : Quaternion.identity;
            model.transform.localPosition = lieDown ? Vector3.up * (-model.hitbox.min.z * Info.scale) : Vector3.zero;
        }

        private void onLoopChanged(string oldLoop, string newLoop) {
            if (oneShotUntil < 0) {
                play(newLoop);
            }
        }

        // loops (idle, walk) play until told otherwise; the others once, then the loop again (death stays)
        private void play(string animation) {
            if (model == null) {
                return;
            }
            string state = NpcDatabase.AnimationOf(Info, animation);
            if (!model.Has(state) && animation == Sleep) {
                state = NpcDatabase.AnimationOf(Info, Idle); // lying down, idle
            }
            if (!model.Has(state)) {
                return;
            }
            model.Play(state, 0.1f);
            bool isLoop = animation == Idle || animation == Walk || animation == Sleep;
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
