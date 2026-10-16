using Brickcraft.World.CustomGenerator;
using UnityEngine;
using static Brickcraft.Tutorials.TutorialChecks;

namespace Brickcraft.Tutorials
{
    /// <summary>
    /// Build your first car, the technic way (see docs/vehicles.md): a wheel on the ground starts it, an axle in its
    /// hub, a technic brick 1x12 on the axle by an end hole, another axle through its other end hole, the other three
    /// wheels on the axles' ends, a brick 1x8 on top and a seat on it; then sit and drive. A finished one stands
    /// behind where the player starts.
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
                new TutorialStep() { textKey = "tutorial.first_car.seat", isDone = () => Parts(Seat).Exists(s => FacesAcross(s, Axle)) },
                new TutorialStep() { textKey = "tutorial.first_car.sit", isDone = IsSeated },
                new TutorialStep() {
                    textKey = "tutorial.first_car.drive",
                    begin = () => driveStart = Where(),
                    isDone = () => IsSeated() && Vector3.Distance(Where(), driveStart) >= DriveDistance,
                    overTime = true,
                },
            },
            buildDemo = start => BuildDemo(start) != null,
        };

        /// <summary>A finished car behind where the player starts.</summary>
        public static Vehicles.Vehicle BuildDemo(Vector3 start) {
            Brick top = TutorialDemos.Chassis(TutorialDemos.CellBeside(start, new Vector3Int(0, CustomChunkGenerator.FlatGrassHeight + 1, -10)), Wheel, Axle, Beam, Top);
            if (top == null) {
                return null;
            }
            int on = Server.items[Top].brickModel.heightInPlates;
            TutorialDemos.OnStuds(top, Seat, TutorialDemos.Around(3, 0, on, 4), s => FacesAcross(s, Axle));
            return top.vehicle;
        }
    }
}
