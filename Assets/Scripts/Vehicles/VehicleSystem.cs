using System;
using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.World;
using Mirror;
using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>
    /// Vehicles on the server (see Vehicle): a wheel placed on the ground starts one, bricks placed on its bricks join it.
    /// A brick removed that leaves its bricks in separate groups (see VehicleGrid.Components) splits it: the biggest group
    /// stays, the others become vehicles of their own where they are. A brick placed that also touches another vehicle
    /// lined up with this one (a whole number of cells away, quarter turns apart: like the halves of a split one) merges
    /// them: the other's bricks join this one.
    ///
    /// They're saved with the chunk they're in, like items on the ground (see NpcSystem, which saves and loads both).
    ///
    /// Players sit on their seats (PlayerNetwork.seatedIn): the first one in drives it (see Vehicle.ServerSetDriver),
    /// then the next one in when they get up. Getting up puts them beside it; so does their seat going.
    /// </summary>
    public static class VehicleSystem
    {
        /// <summary>A vehicle started by a part (a wheel) placed on the world's grid, there.</summary>
        public static Vehicle ServerCreate(Item item, int color, BrickPlacement placed) {
            Vehicle vehicle = create(BrickGrid.CellToWorld(placed.origin), Quaternion.identity);
            vehicle.ServerAdd(item, color, new BrickPlacement(item.brickModel, Vector3Int.zero, placed.rotation));
            NetworkServer.Spawn(vehicle.gameObject);
            return vehicle;
        }

        /// <summary>A brick placed on a vehicle, in its cells; the vehicles it also touches join it.</summary>
        public static Brick ServerPlace(Vehicle vehicle, Item item, int color, BrickPlacement placement) {
            Brick brick = vehicle.ServerAdd(item, color, placement);
            if (brick != null) {
                mergeTouching(vehicle, placement);
            }
            return brick;
        }

        /// <summary>Removes a vehicle's brick; what it held together comes apart. Returns the bricks removed (it).</summary>
        public static List<Brick> ServerRemove(Brick brick) {
            Vehicle vehicle = brick.vehicle;
            foreach (PlayerNetwork player in players()) {
                if (player.seatedIn == brick.id) {
                    ServerStandUp(player, vehicle.Body.velocity, vehicle.Body.angularVelocity);
                }
            }
            vehicle.ServerRemove(brick);
            if (vehicle.grid.Count == 0) {
                NetworkServer.Destroy(vehicle.gameObject);
            } else {
                vehicle.ServerCleanWires();
                split(vehicle);
            }
            return new List<Brick>() { brick };
        }

        /// <summary>
        /// Why the player can't sit on that brick now: it isn't a vehicle's seat, someone's on it; empty if it can.
        /// </summary>
        public static GameText WhyNotSit(PlayerNetwork player, Brick seat) {
            if (seat.vehicle == null || seat.item.VehicleRole != VehicleRole.Seat) {
                return GameText.Of("vehicle.not_seat");
            }
            foreach (PlayerNetwork other in players()) {
                if (other != player && other.seatedIn == seat.id) {
                    return GameText.Of("vehicle.seat_taken");
                }
            }
            return new GameText();
        }

        /// <summary>
        /// Sits the player on a vehicle's seat (see WhyNotSit); if nobody drives it, it does. False if a mod's
        /// onVehicleEnter said no.
        /// </summary>
        public static bool ServerSit(PlayerNetwork player, Brick seat) {
            if (!Scripting.ModScripts.ModEvent("onVehicleEnter", Scripting.PlayerHandle.For(player.connectionToClient), Scripting.VehicleHandle.For(seat.vehicle))) {
                return false;
            }
            player.seatedIn = seat.id;
            refreshDriver(seat.vehicle);
            GameTexts.Send(player.connectionToClient, "vehicle.reposition_hint");
            return true;
        }

        /// <summary>
        /// The vehicle the player sits in back on its wheels (see Vehicle.Reposition), by whoever moves it: its driver's
        /// client, or the server. False if the player isn't in one.
        /// </summary>
        public static bool ServerReposition(PlayerNetwork player) {
            if (string.IsNullOrEmpty(player.seatedIn) || !Server.bricks.TryGetValue(player.seatedIn, out Brick seat) || seat.vehicle == null) {
                return false;
            }
            ServerReposition(seat.vehicle);
            return true;
        }

        /// <summary>The vehicle back on its wheels, by whoever moves it.</summary>
        public static void ServerReposition(Vehicle vehicle) {
            PlayerNetwork driver = vehicle.Driver;
            if (driver != null && driver.connectionToClient != null) {
                vehicle.TargetReposition(driver.connectionToClient);
            } else {
                vehicle.Reposition();
            }
        }

        /// <summary>
        /// The player gets up from its seat (its client puts it beside the vehicle, see PlayerSeat); the vehicle goes on as
        /// it was moving, if it drove it.
        /// </summary>
        public static void ServerStandUp(PlayerNetwork player, Vector3 velocity, Vector3 angularVelocity) {
            if (string.IsNullOrEmpty(player.seatedIn)) {
                return;
            }
            Server.bricks.TryGetValue(player.seatedIn, out Brick seat);
            player.seatedIn = "";
            if (seat == null || seat.vehicle == null) {
                return;
            }
            Vehicle vehicle = seat.vehicle;
            if (vehicle.driverId == player.netId) {
                vehicle.ServerSetDriver(null, velocity, angularVelocity);
            }
            refreshDriver(vehicle);
            Scripting.ModScripts.ModEvent("onVehicleLeave", Scripting.PlayerHandle.For(player.connectionToClient), Scripting.VehicleHandle.For(vehicle));
        }

        // the vehicle's driver is in one of its seats, else the first player in one (else nobody)
        private static void refreshDriver(Vehicle vehicle) {
            if (vehicle == null) {
                return;
            }
            PlayerNetwork driver = vehicle.Driver;
            if (driver != null && vehicle.SeatOf(driver) != null) {
                return;
            }
            PlayerNetwork next = null;
            foreach (PlayerNetwork player in players()) {
                if (vehicle.SeatOf(player) != null) {
                    next = player;
                    break;
                }
            }
            if (next != null || vehicle.driverId != 0) {
                vehicle.ServerSetDriver(next, vehicle.Body.velocity, vehicle.Body.angularVelocity);
            }
        }

        private static IEnumerable<PlayerNetwork> players() {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                PlayerNetwork player = conn.identity != null ? conn.identity.GetComponent<PlayerNetwork>() : null;
                if (player != null) {
                    yield return player;
                }
            }
        }

        /// <summary>Paints a vehicle's brick (a colour its item can have).</summary>
        public static void ServerSetColor(Brick brick, int color) {
            brick.vehicle.ServerSetColor(brick, color);
        }

        /// <summary>
        /// Whether a placement on a vehicle would go into something that isn't the vehicle (the terrain, the world's
        /// bricks, players, other vehicles).
        /// </summary>
        public static bool OverlapsOthers(Vehicle vehicle, BrickPlacement placement) {
            Bounds local = placement.LocalBounds;
            Transform tr = vehicle.transform;
            Vector3 halfSize = local.extents - Vector3.one * 0.02f; // touching is fine
            foreach (Collider collider in Physics.OverlapBox(tr.TransformPoint(local.center), halfSize, tr.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore)) {
                if (collider.GetComponentInParent<Vehicle>() != vehicle) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The vehicles in a chunk, to save with it; with remove, they go (the chunk is unloaded).</summary>
        public static List<SavedVehicle> SavedIn(Vector2Int coords, bool remove) {
            List<SavedVehicle> saved = new List<SavedVehicle>();
            foreach (Vehicle vehicle in new List<Vehicle>(Vehicle.All)) {
                if (vehicle == null || !vehicle.isServer || vehicle.grid.Count == 0 || WorldBehaviour.ChunkAt(vehicle.transform.position) != coords) {
                    continue;
                }
                saved.Add(toSaved(vehicle));
                if (remove) {
                    NetworkServer.Destroy(vehicle.gameObject);
                }
            }
            return saved;
        }

        /// <summary>Puts back the vehicles saved in a chunk.</summary>
        public static void Restore(IEnumerable<SavedVehicle> saved) {
            foreach (SavedVehicle s in saved) {
                Vehicle vehicle = create(FloatingOrigin.ToLocal(s.x, s.y, s.z), s.rotation);
                vehicle.vehicleId = s.id.ToString();
                foreach (SavedVehiclePart part in s.parts) {
                    if (Server.items.TryGetValue(part.itemId, out Item item) && item.brickModel != null) {
                        vehicle.ServerAdd(item, part.color, new BrickPlacement(item.brickModel, part.origin, part.rotation), part.id.ToString(), part.state);
                    }
                }
                if (vehicle.grid.Count == 0) {
                    UnityEngine.Object.Destroy(vehicle.gameObject);
                    continue;
                }
                if (s.wires != null) {
                    foreach (SavedWire wire in s.wires) {
                        VehicleWire restored = new VehicleWire() {
                            seat = wire.seat.ToString(), part = wire.part.ToString(),
                            action = (WireAction)wire.action, source = (WireSource)wire.source, key = wire.key, invert = wire.invert,
                        };
                        if (vehicle.wires.Count < VehicleWires.MaxPerVehicle && VehicleWires.IsValid(vehicle, restored)) {
                            vehicle.wires.Add(restored);
                        }
                    }
                }
                NetworkServer.Spawn(vehicle.gameObject);
            }
        }

        private static SavedVehicle toSaved(Vehicle vehicle) {
            FloatingOrigin.ToAbsolute(vehicle.transform.position, out double x, out double y, out double z);
            SavedVehicle saved = new SavedVehicle() {
                id = guidOf(vehicle.vehicleId),
                x = x, y = y, z = z,
                rotation = vehicle.transform.rotation,
                parts = new List<SavedVehiclePart>(),
                wires = new List<SavedWire>(),
            };
            foreach (VehicleWire wire in vehicle.wires) {
                saved.wires.Add(new SavedWire() {
                    seat = guidOf(wire.seat), part = guidOf(wire.part),
                    action = (byte)wire.action, source = (byte)wire.source, key = wire.key, invert = wire.invert,
                });
            }
            foreach (Brick brick in vehicle.grid.Bricks) {
                saved.parts.Add(new SavedVehiclePart() {
                    id = guidOf(brick.id),
                    itemId = brick.itemId,
                    color = brick.color,
                    origin = brick.placement.origin,
                    rotation = (byte)brick.placement.rotation,
                    state = brick.state,
                });
            }
            return saved;
        }

        private static Guid guidOf(string id) {
            return Guid.TryParse(id, out Guid guid) ? guid : Guid.NewGuid();
        }

        // a vehicle not spawned yet: its parts go in first, so the spawn message has them (a host applies that message a
        // frame later, over what changed since; see Vehicle.OnStartClient)
        private static Vehicle create(Vector3 position, Quaternion rotation) {
            GameObject go = UnityEngine.Object.Instantiate(Resources.Load<GameObject>(Vehicle.PrefabPath), position, rotation);
            go.name = "Vehicle";
            Vehicle vehicle = go.GetComponent<Vehicle>();
            vehicle.vehicleId = Guid.NewGuid().ToString();
            return vehicle;
        }

        // the groups that aren't connected to the biggest one any more become vehicles of their own, where they are
        private static void split(Vehicle vehicle) {
            List<List<Brick>> groups = vehicle.grid.Components();
            for (int i = 1; i < groups.Count; i++) {
                Vehicle piece = create(vehicle.transform.position, vehicle.transform.rotation);
                move(groups[i], vehicle, piece, 0, Vector3Int.zero);
                NetworkServer.Spawn(piece.gameObject);
                refreshDriver(piece);
            }
            refreshDriver(vehicle);
        }

        // other vehicles lined up with this one that the placement touches join it
        private static void mergeTouching(Vehicle vehicle, BrickPlacement placement) {
            foreach (Vehicle other in new List<Vehicle>(Vehicle.All)) {
                if (other == vehicle || other.grid.Count == 0
                        || !VehicleGrid.LinesUp(vehicle.transform, other.transform, out int turns, out Vector3Int offset)) {
                    continue;
                }
                // its cells in this one's
                VehicleGrid mapped = new VehicleGrid();
                List<Brick> theirs = new List<Brick>(other.grid.Bricks);
                foreach (Brick brick in theirs) {
                    mapped.Register(new Brick() { id = brick.id, placement = VehicleGrid.Map(brick.placement, turns, offset) });
                }
                if (mapped.Touching(placement).Count == 0) {
                    continue;
                }
                // they mustn't go into this one's bricks
                bool fits = true;
                foreach (Brick brick in mapped.Bricks) {
                    fits &= vehicle.grid.IsFree(brick.placement);
                }
                if (!fits) {
                    continue;
                }
                move(theirs, other, vehicle, turns, offset);
                NetworkServer.Destroy(other.gameObject);
            }
            refreshDriver(vehicle);
        }

        // bricks from one vehicle to another (keeping their ids), their placements turned and moved into its cells; the
        // wires between them go too, the ones to bricks left behind are cut
        private static void move(List<Brick> bricks, Vehicle from, Vehicle to, int turns, Vector3Int offset) {
            HashSet<string> moving = new HashSet<string>();
            foreach (Brick brick in bricks) {
                moving.Add(brick.id);
            }
            List<VehicleWire> wires = new List<VehicleWire>();
            foreach (VehicleWire wire in from.wires) {
                if (moving.Contains(wire.seat) && moving.Contains(wire.part)) {
                    wires.Add(wire);
                }
            }
            foreach (Brick brick in bricks) {
                Item item = brick.item;
                int color = brick.color, state = brick.state;
                string id = brick.id;
                BrickPlacement placement = VehicleGrid.Map(brick.placement, turns, offset);
                from.ServerRemove(brick);
                to.ServerAdd(item, color, placement, id, state);
            }
            from.ServerCleanWires();
            foreach (VehicleWire wire in wires) {
                if (to.wires.Count < VehicleWires.MaxPerVehicle) {
                    to.wires.Add(wire);
                }
            }
        }
    }
}
