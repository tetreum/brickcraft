using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Mirror;
using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>A brick of a vehicle as it travels: where it is in the vehicle's cells (see VehicleGrid).</summary>
    public struct VehiclePart
    {
        public string id;
        public string itemId;
        public int color;
        public Vector3Int origin;
        public byte rotation;
        public int state;
    }

    /// <summary>
    /// Something built of bricks that isn't part of the world: a car, a boat, a plane. It starts with a wheel placed on the
    /// ground, and bricks placed on its bricks are its own (see VehicleSystem). They're placed like the world's, studs
    /// and plates, but on its own grid (VehicleGrid), so it can move and turn. Its parts are synced with it: every peer
    /// makes their bricks under it (Server.spawnBrick with the vehicle).
    ///
    /// It's driven from a seat (see VehicleSystem.ServerSit): the driver's client simulates it then (it owns it, and
    /// sends where it is), the others follow. Left, the server simulates it until it comes to rest (a plane falls...),
    /// then it stays where it is, solid to build on. See VehicleDrive for how it moves.
    /// </summary>
    public class Vehicle : NetworkBehaviour
    {
        public const string PrefabPath = "Vehicle";

        /// <summary>Every vehicle of this peer.</summary>
        public static readonly HashSet<Vehicle> All = new HashSet<Vehicle>();

        /// <summary>Identifies it in saves.</summary>
        [SyncVar] public string vehicleId;

        public readonly SyncList<VehiclePart> parts = new SyncList<VehiclePart>();

        /// <summary>Its seats' wires to its parts (see VehicleWire, WiringTool).</summary>
        public readonly SyncList<VehicleWire> wires = new SyncList<VehicleWire>();

        public readonly VehicleGrid grid = new VehicleGrid();

        /// <summary>The player driving it (its netId), 0 if nobody is.</summary>
        [SyncVar(hook = nameof(onDriverChanged))] public uint driverId;

        public Rigidbody Body { get; private set; }

        /// <summary>It moves by physics on this peer: its driver's, or the server's while it comes to rest.</summary>
        public bool IsSimulated { get; private set; }

        public VehicleDrive Drive { get; private set; }

        // with nobody driving it the server lets it come to rest, for at most this long
        private const float MaxSettleTime = 60;
        private const float RestTime = 1;
        private bool settling;
        private float settleTime, restTime;
        private bool physicsDirty = true;
        private NetworkTransformBase netTransform;

        private void Awake() {
            Body = GetComponent<Rigidbody>();
            netTransform = GetComponent<NetworkTransformBase>();
            Drive = new VehicleDrive(this);
            grid.Changed += () => physicsDirty = true;
            wires.OnChange += (op, index, old) => physicsDirty = true;
            All.Add(this);
        }

        public override void OnStartServer() {
            updateSimulation();
        }

        public override void OnStartAuthority() {
            updateSimulation();
        }

        public override void OnStopAuthority() {
            updateSimulation();
        }

        private void onDriverChanged(uint oldDriver, uint newDriver) {
            updateSimulation();
        }

        /// <summary>The player driving it, null if nobody is.</summary>
        public PlayerNetwork Driver {
            get {
                if (driverId == 0) {
                    return null;
                }
                NetworkIdentity identity;
                bool found = isServer ? NetworkServer.spawned.TryGetValue(driverId, out identity) : NetworkClient.spawned.TryGetValue(driverId, out identity);
                return found && identity != null ? identity.GetComponent<PlayerNetwork>() : null;
            }
        }

        /// <summary>Someone sat at its wheel (or nobody, with how it was moving: it goes on like that).</summary>
        [Server]
        public void ServerSetDriver(PlayerNetwork driver, Vector3 velocity, Vector3 angularVelocity) {
            if (driver != null) {
                if (connectionToClient != driver.connectionToClient) {
                    if (connectionToClient != null) {
                        netIdentity.RemoveClientAuthority();
                    }
                    netIdentity.AssignClientAuthority(driver.connectionToClient);
                }
                settling = false;
                driverId = driver.netId;
            } else {
                if (connectionToClient != null) {
                    netIdentity.RemoveClientAuthority();
                }
                driverId = 0;
                settling = true;
                settleTime = restTime = 0;
            }
            updateSimulation();
            if (driver == null && IsSimulated) {
                Body.velocity = Vector3.ClampMagnitude(velocity, VehicleDrive.MaxSpeed * 2);
                Body.angularVelocity = Vector3.ClampMagnitude(angularVelocity, 10);
            }
        }

        // who moves it: its driver sends where it is, or the server does
        private void updateSimulation() {
            bool driven = driverId != 0;
            SyncDirection direction = driven ? SyncDirection.ClientToServer : SyncDirection.ServerToClient;
            if (netTransform != null && netTransform.syncDirection != direction) {
                netTransform.syncDirection = direction;
                netTransform.ResetState();
            }
            IsSimulated = driven ? isOwned : isServer && settling;
            Body.isKinematic = !IsSimulated;
            // followed, it's moved by the network, not its body
            Body.interpolation = IsSimulated ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            Drive.SetSimulated(IsSimulated);
            physicsDirty = true;
        }

        private void FixedUpdate() {
            if (physicsDirty) {
                physicsDirty = false;
                PlayerNetwork driver = Driver;
                Brick seat = driver != null ? SeatOf(driver) : null;
                Drive.Rebuild(seat != null ? VehicleShape.Seat(seat, transform).forward : Vector3.forward,
                    seat != null ? WiresOf(seat.id) : new List<VehicleWire>());
            }
            if (!IsSimulated) {
                return;
            }
            if (driverId != 0) {
                Drive.Apply(VehicleControls.Read());
                return;
            }
            Drive.Apply(VehicleInput.Parked);
            settleTime += Time.fixedDeltaTime;
            bool still = Body.velocity.sqrMagnitude < 0.04f && Body.angularVelocity.sqrMagnitude < 0.04f;
            restTime = still ? restTime + Time.fixedDeltaTime : 0;
            if (restTime >= RestTime || settleTime >= MaxSettleTime) {
                settling = false;
                updateSimulation();
            }
        }

        /// <summary>The wires from that seat.</summary>
        public List<VehicleWire> WiresOf(string seatId) {
            List<VehicleWire> from = new List<VehicleWire>();
            foreach (VehicleWire wire in wires) {
                if (wire.seat == seatId) {
                    from.Add(wire);
                }
            }
            return from;
        }

        /// <summary>The part is wired to that seat.</summary>
        public bool IsWired(string seatId, string partId) {
            foreach (VehicleWire wire in wires) {
                if (wire.seat == seatId && wire.part == partId) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The seat's wires are these now (the ones that aren't valid left out, see VehicleWires.IsValid).</summary>
        [Server]
        public void ServerSetWires(string seatId, IEnumerable<VehicleWire> seatWires) {
            for (int i = wires.Count - 1; i >= 0; i--) {
                if (wires[i].seat == seatId) {
                    wires.RemoveAt(i);
                }
            }
            foreach (VehicleWire wire in seatWires) {
                if (wire.seat == seatId && wires.Count < VehicleWires.MaxPerVehicle && VehicleWires.IsValid(this, wire)) {
                    wires.Add(wire);
                }
            }
            physicsDirty = true;
        }

        /// <summary>Wires the part to the seat, as parts are by default (see VehicleWires.Defaults); or unwires it if it was.</summary>
        [Server]
        public void ServerToggleWire(Brick seat, Brick part) {
            List<VehicleWire> seatWires = WiresOf(seat.id);
            if (IsWired(seat.id, part.id)) {
                seatWires.RemoveAll(wire => wire.part == part.id);
            } else {
                seatWires.AddRange(VehicleWires.Defaults(this, seat, part));
            }
            ServerSetWires(seat.id, seatWires);
        }

        /// <summary>Wires go with their ends: the ones whose seat or part isn't one of its bricks any more.</summary>
        [Server]
        public void ServerCleanWires() {
            for (int i = wires.Count - 1; i >= 0; i--) {
                if (!VehicleWires.IsValid(this, wires[i])) {
                    wires.RemoveAt(i);
                }
            }
        }

        /// <summary>The seat of this vehicle the player is in, null if it isn't in one.</summary>
        public Brick SeatOf(PlayerNetwork player) {
            return !string.IsNullOrEmpty(player.seatedIn) && Server.bricks.TryGetValue(player.seatedIn, out Brick seat) && seat.vehicle == this ? seat : null;
        }

        private void OnDestroy() {
            All.Remove(this);
            // its bricks go with it (none on the server once moved to another vehicle)
            if (Server.Instance != null) {
                foreach (Brick brick in new List<Brick>(grid.Bricks)) {
                    Server.Instance.removeBrick(brick);
                }
            }
        }

        // clients make the bricks the server sends (the host has them already)
        public override void OnStartClient() {
            if (isServer) {
                repair();
                return;
            }
            updateSimulation();
            foreach (VehiclePart part in parts) {
                spawn(part);
            }
            parts.OnAdd += index => spawn(parts[index]);
            parts.OnRemove += (index, old) => remove(old.id);
            parts.OnSet += (index, old) => changed(old, parts[index]);
            parts.OnClear += () => {
                foreach (Brick brick in new List<Brick>(grid.Bricks)) {
                    Server.Instance.removeBrick(brick);
                }
            };
        }

        // a host applies its spawn message a frame after it was made, over its parts: they're its bricks again
        [Server]
        private void repair() {
            bool same = parts.Count == grid.Count;
            foreach (VehiclePart part in parts) {
                same &= BrickOf(part) != null;
            }
            if (same) {
                return;
            }
            parts.Clear();
            foreach (Brick brick in grid.Bricks) {
                parts.Add(new VehiclePart() {
                    id = brick.id,
                    itemId = brick.itemId,
                    color = brick.color,
                    origin = brick.placement.origin,
                    rotation = (byte)brick.placement.rotation,
                    state = brick.state,
                });
            }
        }

        public Brick BrickOf(VehiclePart part) {
            return Server.bricks.TryGetValue(part.id, out Brick brick) && brick.vehicle == this ? brick : null;
        }

        private Brick spawn(VehiclePart part) {
            if (!Server.items.TryGetValue(part.itemId, out Item item)) {
                Debug.LogWarning("A vehicle's brick is of the unknown item " + part.itemId);
                return null;
            }
            // moving here from another vehicle (split or merged): its message may come first
            if (Server.bricks.TryGetValue(part.id, out Brick moving)) {
                Server.Instance.removeBrick(moving);
            }
            BrickPlacement placement = new BrickPlacement(item.brickModel, part.origin, part.rotation);
            return Server.Instance.spawnBrick(item, part.color, placement, part.id, part.state, null, this);
        }

        private void remove(string id) {
            if (Server.bricks.TryGetValue(id, out Brick brick) && brick.vehicle == this) {
                Server.Instance.removeBrick(brick);
            }
        }

        private void changed(VehiclePart old, VehiclePart now) {
            Brick brick = BrickOf(now);
            if (brick == null) {
                return;
            }
            if (old.color != now.color) {
                Server.Instance.setBrickColor(brick, now.color);
            }
            if (old.state != now.state) {
                Server.Instance.setBrickState(brick, now.state);
            }
        }

        // -------- the server changes it (see VehicleSystem), its parts tell the clients --------

        [Server]
        public Brick ServerAdd(Item item, int color, BrickPlacement placement, string id = null, int state = 0) {
            VehiclePart part = new VehiclePart() {
                id = id ?? System.Guid.NewGuid().ToString(),
                itemId = item.id,
                color = item.ValidColor(color),
                origin = placement.origin,
                rotation = (byte)placement.rotation,
                state = state,
            };
            Brick brick = spawn(part);
            if (brick != null) {
                parts.Add(part);
            }
            return brick;
        }

        [Server]
        public void ServerRemove(Brick brick) {
            int index = indexOf(brick.id);
            if (index >= 0) {
                parts.RemoveAt(index);
            }
            Server.Instance.removeBrick(brick);
        }

        [Server]
        public void ServerSetColor(Brick brick, int color) {
            int index = indexOf(brick.id);
            if (index < 0) {
                return;
            }
            Server.Instance.setBrickColor(brick, color);
            VehiclePart part = parts[index];
            part.color = brick.color;
            parts[index] = part;
        }

        private int indexOf(string id) {
            for (int i = 0; i < parts.Count; i++) {
                if (parts[i].id == id) {
                    return i;
                }
            }
            return -1;
        }
    }
}
