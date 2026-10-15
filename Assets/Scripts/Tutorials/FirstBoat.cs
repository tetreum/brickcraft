using Brickcraft.World;
using UnityEngine;
using static Brickcraft.Tutorials.TutorialChecks;

namespace Brickcraft.Tutorials
{
    /// <summary>
    /// Build your first boat, on a coast (see TerrainKind.Coast): a seat placed on the water starts it, bricks around
    /// its base make a raft, a propeller behind it pushes it; then sit and sail.
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
                new TutorialStep() { textKey = "tutorial.first_boat.raft", isDone = () => Count(Raft) >= RaftBricks && IsEven() },
                new TutorialStep() { textKey = "tutorial.first_boat.propeller", isDone = () => Parts(Propeller).Exists(PushesFromBehind) },
                new TutorialStep() { textKey = "tutorial.first_boat.sit", isDone = IsSeated },
                new TutorialStep() {
                    textKey = "tutorial.first_boat.sail",
                    begin = () => sailStart = Where(),
                    isDone = () => IsSeated() && Vector3.Distance(Where(), sailStart) >= SailDistance,
                    overTime = true,
                },
            },
        };

        // the raft's middle under the seat's (lopsided, it rolls over)
        private static bool IsEven() {
            Brick seat = Parts(Seat).Find(b => true);
            if (seat == null) {
                return false;
            }
            Transform vehicle = seat.vehicle.transform;
            Vector3 middle = Vector3.zero;
            System.Collections.Generic.List<Brick> raft = Parts(Raft);
            foreach (Brick brick in raft) {
                middle += Vehicles.VehicleShape.MeshBounds(brick, vehicle).center / raft.Count;
            }
            Vector3 off = middle - Vehicles.VehicleShape.MeshBounds(seat, vehicle).center;
            off.y = 0;
            return off.magnitude < EvenOff;
        }

        // how far the raft's middle may be from the seat's, metres (a stud)
        private const float EvenOff = 0.45f;

        // how far off the seat's middle a propeller may be, metres (half a stud): off it, it pushes the boat round
        private const float MiddleOff = 0.1f;

        // right behind the seat, pushing the way it faces
        private static bool PushesFromBehind(Brick propeller) {
            foreach (Brick seat in Parts(Seat)) {
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
    }
}
