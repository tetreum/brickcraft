using System;
using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Vehicles;
using UnityEngine;

namespace Brickcraft.Tutorials
{
    /// <summary>
    /// Builds a tutorial's finished vehicle beside where the player starts (see Tutorial.buildDemo), to look at while
    /// building its own: the same parts put together as the steps say, by trying the ways they go (like a player would)
    /// and keeping the first one that's right. It's marked so the steps don't count it (see TutorialChecks.MarkDemo).
    /// </summary>
    public static class TutorialDemos
    {
        /// <summary>A vehicle started with that part on the world's grid there, marked as a demo.</summary>
        public static Vehicle Start(string itemId, Vector3Int cell, int rotation = 0) {
            Item item = Server.items[itemId];
            Vehicle vehicle = VehicleSystem.ServerCreate(item, item.color, new BrickPlacement(item.brickModel, cell, rotation));
            TutorialChecks.MarkDemo(vehicle);
            return vehicle;
        }

        /// <summary>Gone now (it's destroyed at the end of the frame): out of the way of the next try.</summary>
        public static void TakeBack(Brick brick) {
            GameObject removed = brick.gameObject;
            VehicleSystem.ServerRemove(brick);
            if (removed != null) {
                removed.SetActive(false);
            }
        }

        /// <summary>The part held by a brick the first of those ways it can be (and is good), null if none.</summary>
        public static Brick Connect(Brick holder, string itemId, IEnumerable<BrickJoint> joints, Func<Brick, bool> good = null) {
            Item part = Server.items[itemId];
            foreach (BrickJoint joint in joints) {
                if (BrickJoints.WhyNot(holder, part.brickModel, joint) != null) {
                    continue;
                }
                Brick brick = VehicleSystem.ServerConnect(part, part.color, holder, joint);
                if (brick == null || good == null || good(brick)) {
                    return brick;
                }
                TakeBack(brick);
            }
            return null;
        }

        /// <summary>The part on a brick's studs (its grid), at the first of those cells, turned the first way it goes (and is good).</summary>
        public static Brick OnStuds(Brick holder, string itemId, IEnumerable<Vector3Int> cells, Func<Brick, bool> good = null) {
            List<BrickJoint> joints = new List<BrickJoint>();
            foreach (Vector3Int cell in cells) {
                for (int rotation = 0; rotation < 4; rotation++) {
                    joints.Add(BrickJoint.OnStuds(cell, rotation));
                }
            }
            return Connect(holder, itemId, joints, good);
        }

        /// <summary>The part on a vehicle's cells, at the first of those placements that's free and touches it (and is good).</summary>
        public static Brick Place(Vehicle vehicle, string itemId, IEnumerable<BrickPlacement> placements, Func<Brick, bool> good = null) {
            Item part = Server.items[itemId];
            foreach (BrickPlacement placement in placements) {
                if (!vehicle.grid.IsFree(placement) || vehicle.grid.Touching(placement).Count == 0 || VehicleSystem.OverlapsOthers(vehicle, placement)) {
                    continue;
                }
                Brick brick = VehicleSystem.ServerPlace(vehicle, part, part.color, placement);
                if (brick == null || good == null || good(brick)) {
                    return brick;
                }
                TakeBack(brick);
            }
            return null;
        }

        /// <summary>Cells of a row (at that height), around a middle first, out to that far.</summary>
        public static IEnumerable<Vector3Int> Around(int x, int z, int y, int reach) {
            for (int distance = 0; distance <= reach; distance++) {
                for (int dx = -distance; dx <= distance; dx++) {
                    for (int dz = -distance; dz <= distance; dz++) {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) == distance) {
                            yield return new Vector3Int(x + dx, y, z + dz);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// A car's chassis the technic way, at that cell of the world's grid: a wheel, an axle in its hub, a technic
        /// brick on the axle by an end hole (studs up), another axle through its other end hole, a wheel on each free
        /// axle end, a brick on top. Returns the brick on top, null if a part didn't go.
        /// </summary>
        public static Brick Chassis(Vector3Int cell, string wheelId, string axleId, string beamId, string topId) {
            BrickModel wheel = Server.items[wheelId].brickModel, axle = Server.items[axleId].brickModel, beam = Server.items[beamId].brickModel;
            int hub = Array.FindIndex(wheel.connectors, c => !c.male && c.HasAxleShape);
            if (hub < 0) {
                return null;
            }
            Vehicle vehicle = Start(wheelId, cell);
            Brick first = new List<Brick>(vehicle.grid.Bricks)[0];

            int along = BrickJoints.MaxSlide(wheel.connectors[hub], axle.connectors[0]);
            Brick rod = Connect(first, axleId, new[] {
                new BrickJoint() { holderConnector = hub, connector = 0, slide = along },
                new BrickJoint() { holderConnector = hub, connector = 0, slide = -along },
            });
            if (rod == null) {
                return null;
            }

            // its end holes: the round ones furthest apart
            int firstHole = -1, lastHole = -1;
            for (int i = 0; i < beam.connectors.Length; i++) {
                Connector c = beam.connectors[i];
                if (c.male || c.HasAxleShape) {
                    continue;
                }
                if (firstHole < 0 || c.Middle.x < beam.connectors[firstHole].Middle.x) { firstHole = i; }
                if (lastHole < 0 || c.Middle.x > beam.connectors[lastHole].Middle.x) { lastHole = i; }
            }
            List<BrickJoint> onAxle = new List<BrickJoint>();
            int slides = BrickJoints.MaxSlide(axle.connectors[0], beam.connectors[firstHole]);
            for (int slide = 0; slide <= slides; slide++) {
                foreach (int way in new[] { slide, -slide }) {
                    for (int spin = 0; spin < 4; spin++) {
                        foreach (bool flip in new[] { false, true }) {
                            onAxle.Add(new BrickJoint() { holderConnector = 0, connector = firstHole, slide = way, spin = spin, flip = flip });
                        }
                    }
                }
            }
            Brick technic = Connect(rod, beamId, onAxle, TutorialChecks.IsUpright);
            if (technic == null) {
                return null;
            }
            Brick rod2 = Connect(technic, axleId, new[] { new BrickJoint() { holderConnector = lastHole, connector = 0 } });
            if (rod2 == null) {
                return null;
            }
            foreach (Brick each in new[] { rod, rod2 }) {
                int max = BrickJoints.MaxSlide(each.model.connectors[0], wheel.connectors[hub]);
                foreach (int slide in new[] { max, -max }) {
                    Connect(each, wheelId, new[] { new BrickJoint() { holderConnector = 0, connector = hub, slide = slide, flip = slide < 0 } });
                }
            }
            int height = beam.heightInPlates;
            return Connect(technic, topId, new[] { BrickJoint.OnStuds(new Vector3Int(0, height, 0), 0), BrickJoint.OnStuds(new Vector3Int(0, height, 0), 1) });
        }

        /// <summary>The cell on the world's grid of a block's bottom, the block given relative to where the player started.</summary>
        public static Vector3Int CellBeside(Vector3 start, Vector3Int blocks) {
            Vector3Int block = BrickGrid.CellToBlock(BrickGrid.WorldToCell(start));
            return BrickGrid.BlockToCell(new Vector3Int(block.x + blocks.x, blocks.y, block.z + blocks.z));
        }
    }
}
