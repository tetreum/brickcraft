using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.World;
using Brickcraft.World.CustomGenerator;
using UnityEngine;
using static Brickcraft.Tutorials.TutorialChecks;

namespace Brickcraft.Tutorials
{
    /// <summary>
    /// Build your first boat, on a coast (see TerrainKind.Coast): a seat placed on the water starts it, bricks around
    /// its base make a raft, a propeller behind it pushes it; then sit and sail. A finished one floats off the shore,
    /// beside where the player starts.
    /// </summary>
    public static class FirstBoat
    {
        public const string Id = "first_boat";
        public const string Seat = "seat", Raft = "part_3001", Propeller = "part_92842";
        /// <summary>Bricks of the raft it takes, of those it gets.</summary>
        public const int RaftBricks = 4;
        /// <summary>How far it sails to be done, metres.</summary>
        public const float SailDistance = 20;

        private static Vector3 sailStart;

        public static readonly Tutorial Tutorial = new Tutorial() {
            id = Id,
            titleKey = "tutorial.first_boat.title",
            descriptionKey = "tutorial.first_boat.description",
            terrain = TerrainKind.Coast,
            items = new[] {
                new TutorialItem(Seat, 1),
                new TutorialItem(Raft, 6),
                new TutorialItem(Propeller, 1),
            },
            steps = new[] {
                new TutorialStep() { textKey = "tutorial.first_boat.seat", isDone = () => Count(Seat) >= 1 },
                new TutorialStep() { textKey = "tutorial.first_boat.raft", isDone = () => Parts(Seat).Exists(IsOnEvenRaft) },
                new TutorialStep() { textKey = "tutorial.first_boat.propeller", isDone = () => Parts(Propeller).Exists(PushesFromBehind) },
                new TutorialStep() { textKey = "tutorial.first_boat.sit", isDone = IsSeated },
                new TutorialStep() {
                    textKey = "tutorial.first_boat.sail",
                    begin = () => sailStart = Where(),
                    isDone = () => IsSeated() && Vector3.Distance(Where(), sailStart) >= SailDistance,
                    overTime = true,
                },
            },
            buildDemo = start => BuildDemo(start) != null,
        };

        /// <summary>On a raft of enough bricks whose middle is under it (lopsided, it rolls over).</summary>
        public static bool IsOnEvenRaft(Brick seat) {
            List<Brick> raft = Parts(seat.vehicle, Raft);
            if (raft.Count < RaftBricks) {
                return false;
            }
            Transform vehicle = seat.vehicle.transform;
            Vector3 middle = Vector3.zero;
            foreach (Brick brick in raft) {
                middle += Vehicles.VehicleShape.MeshBounds(brick, vehicle).center / raft.Count;
            }
            Vector3 off = middle - Vehicles.VehicleShape.MeshBounds(seat, vehicle).center;
            off.y = 0;
            return off.magnitude < EvenOff;
        }

        // how far the raft's middle may be from the seat's, metres (a stud)
        private const float EvenOff = 0.45f;

        // how far off the seat's middle a propeller may be, metres (a quarter of a stud): off it, it pushes the boat round
        private const float MiddleOff = 0.1f;

        /// <summary>Right behind a seat of its boat, in the middle, pushing the way it faces.</summary>
        public static bool PushesFromBehind(Brick propeller) {
            foreach (Brick seat in Parts(propeller.vehicle, Seat)) {
                Vector3 facing = Facing(seat);
                Transform vehicle = seat.vehicle.transform;
                Vector3 behind = Vehicles.VehicleShape.MeshBounds(propeller, vehicle).center - Vehicles.VehicleShape.MeshBounds(seat, vehicle).center;
                float aside = Vector3.Dot(behind, Vector3.Cross(Vector3.up, facing).normalized);
                if (PushesAlong(propeller, facing) && Vector3.Dot(behind, facing) < 0 && Mathf.Abs(aside) < MiddleOff) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>A finished boat afloat beside where the player starts, past the shallows, facing out to sea.</summary>
        public static Vehicles.Vehicle BuildDemo(Vector3 start) {
            Vector3Int block = BrickGrid.CellToBlock(BrickGrid.WorldToCell(start));
            Vector3Int cell = BrickGrid.BlockToCell(new Vector3Int(block.x - 8, CustomChunkGenerator.FlatGrassHeight + 1,
                CustomChunkGenerator.CoastSeaZ + CustomChunkGenerator.CoastShallows + CustomChunkGenerator.CoastShelf + 2));
            Brick seat = null;
            for (int rotation = 0; rotation < 4 && seat == null; rotation++) {
                Vehicles.Vehicle boat = TutorialDemos.Start(Seat, cell, rotation);
                seat = new List<Brick>(boat.grid.Bricks)[0];
                if (boat.transform.TransformDirection(Facing(seat)).z < 0.9f) {
                    TutorialDemos.TakeBack(seat);
                    seat = null;
                }
            }
            if (seat == null) {
                return null;
            }

            // a brick against each side of its base, then the propeller behind it
            BrickModel raft = Server.items[Raft].brickModel, propeller = Server.items[Propeller].brickModel;
            Vector3Int o = seat.placement.origin, seatSize = seat.placement.Size;
            for (int side = 0; side < 4; side++) {
                List<BrickPlacement> against = new List<BrickPlacement>();
                for (int rotation = 0; rotation < 2; rotation++) {
                    Vector3Int s = BrickPlacement.SizeFor(raft, rotation);
                    int middleX = o.x + (seatSize.x - s.x) / 2, middleZ = o.z + (seatSize.z - s.z) / 2;
                    Vector3Int at = side == 0 ? new Vector3Int(o.x - s.x, o.y, middleZ) : side == 1 ? new Vector3Int(o.x + seatSize.x, o.y, middleZ)
                        : side == 2 ? new Vector3Int(middleX, o.y, o.z - s.z) : new Vector3Int(middleX, o.y, o.z + seatSize.z);
                    against.Add(new BrickPlacement(raft, at, rotation));
                }
                TutorialDemos.Place(seat.vehicle, Raft, against);
            }
            List<BrickPlacement> behind = new List<BrickPlacement>();
            foreach (Vector3Int at in TutorialDemos.Around(o.x, o.z, o.y, 8)) {
                for (int rotation = 0; rotation < 4; rotation++) {
                    behind.Add(new BrickPlacement(propeller, at, rotation));
                }
            }
            TutorialDemos.Place(seat.vehicle, Propeller, behind, PushesFromBehind);
            return seat.vehicle;
        }
    }
}
