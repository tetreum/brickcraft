using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.Vehicles;
using Brickcraft.World;
using UnityEngine;

namespace Brickcraft.Tutorials
{
    /// <summary>
    /// Build your first car, the technic way (see docs/vehicles.md): a wheel on the ground starts it, an axle in its
    /// hub, a technic brick 1x12 on the axle by an end hole, another axle through its other end hole, the other three
    /// wheels on the axles' ends, a brick 1x8 on top and a seat on it; then sit and drive.
    /// </summary>
    public static class FirstCar
    {
        public const string Id = "first_car";
        public const string Wheel = "part_105645", Axle = "part_3707", Beam = "part_3895", Top = "part_3008", Seat = "seat";
        /// <summary>How far it's driven to be done, metres.</summary>
        public const float DriveDistance = 15;

        private static Vector3 driveStart;

        public static readonly Tutorial Tutorial = new Tutorial() {
            id = Id,
            titleKey = "tutorial.first_car.title",
            descriptionKey = "tutorial.first_car.description",
            items = new[] {
                new TutorialItem(Wheel, 4),
                new TutorialItem(Axle, 2),
                new TutorialItem(Beam, 1),
                new TutorialItem(Top, 1),
                new TutorialItem(Seat, 1),
            },
            steps = new[] {
                new TutorialStep() { textKey = "tutorial.first_car.wheel", isDone = () => Count(Wheel) >= 1 },
                new TutorialStep() { textKey = "tutorial.first_car.axle", isDone = () => Count(Axle) >= 1 },
                new TutorialStep() { textKey = "tutorial.first_car.beam", isDone = () => Parts(Beam).Exists(b => HeldBy(b, Axle) && IsUpright(b)) },
                new TutorialStep() { textKey = "tutorial.first_car.second_axle", isDone = () => Parts(Axle).Exists(a => HeldBy(a, Beam)) },
                new TutorialStep() { textKey = "tutorial.first_car.wheels", isDone = () => Count(Wheel) >= 4 },
                new TutorialStep() { textKey = "tutorial.first_car.top", isDone = () => Count(Top) >= 1 },
                new TutorialStep() { textKey = "tutorial.first_car.seat", isDone = () => Parts(Seat).Exists(FacesAlong) },
                new TutorialStep() { textKey = "tutorial.first_car.sit", isDone = IsSeated },
                new TutorialStep() {
                    textKey = "tutorial.first_car.drive",
                    begin = () => driveStart = Car() != null ? FloatingOrigin.ToAbsolute(Car().transform.position) : Vector3.zero,
                    isDone = () => IsSeated() && Vector3.Distance(FloatingOrigin.ToAbsolute(Car().transform.position), driveStart) >= DriveDistance,
                    overTime = true,
                },
            },
        };

        /// <summary>The car being built: the vehicle with the most bricks, null if there's none yet.</summary>
        public static Vehicle Car() {
            Vehicle car = null;
            foreach (Vehicle vehicle in Vehicle.All) {
                if (vehicle != null && (car == null || vehicle.grid.Count > car.grid.Count)) {
                    car = vehicle;
                }
            }
            return car;
        }

        /// <summary>The car's bricks of that item.</summary>
        public static List<Brick> Parts(string itemId) {
            List<Brick> parts = new List<Brick>();
            Vehicle car = Car();
            if (car != null) {
                foreach (Brick brick in car.grid.Bricks) {
                    if (brick.itemId == itemId) {
                        parts.Add(brick);
                    }
                }
            }
            return parts;
        }

        public static int Count(string itemId) {
            return Parts(itemId).Count;
        }

        // held by a brick of that item (an axle through it, see BrickJoints)
        private static bool HeldBy(Brick brick, string itemId) {
            return brick.joint.HasValue && brick.attachedTo != null && Server.bricks.TryGetValue(brick.attachedTo, out Brick holder) && holder.itemId == itemId;
        }

        // its studs up, on the car
        private static bool IsUpright(Brick brick) {
            BrickJoints.GridOf(brick, out _, out Quaternion turned);
            return Vector3.Dot(turned * Vector3.up, Vector3.up) > 0.9f;
        }

        // a seat facing the way the wheels roll: across the axles
        private static bool FacesAlong(Brick seat) {
            List<Brick> axles = Parts(Axle);
            if (axles.Count == 0 || seat.vehicle == null) {
                return false;
            }
            Brick axle = axles[0];
            Vector3 along = axle.jointRotation * axle.model.connectors[0].axis;
            Vector3 forward = VehicleShape.Seat(seat, seat.vehicle.transform).forward;
            return Mathf.Abs(Vector3.Dot(along.normalized, forward.normalized)) < 0.5f;
        }

        // the local player in the car's seat
        private static bool IsSeated() {
            PlayerNetwork network = Player.Instance != null ? Player.Instance.network : null;
            Vehicle car = Car();
            return network != null && car != null && !string.IsNullOrEmpty(network.seatedIn)
                && Server.bricks.TryGetValue(network.seatedIn, out Brick seat) && seat.vehicle == car;
        }
    }
}
