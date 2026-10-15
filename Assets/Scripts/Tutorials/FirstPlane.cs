using Brickcraft.World.CustomGenerator;
using UnityEngine;
using static Brickcraft.Tutorials.TutorialChecks;

namespace Brickcraft.Tutorials
{
    /// <summary>
    /// Build your first plane: a car's chassis the technic way (a wheel on the ground, an axle, a technic brick 1x8,
    /// another axle, three more wheels, a brick 1x8 on top), a wing 16x16 on it, a propeller near its middle and a
    /// seat facing it; then take off. Its wheels are small (the wing goes over them) and rubber. A finished one stands
    /// behind where the player starts.
    /// </summary>
    public static class FirstPlane
    {
        public const string Id = "first_plane";
        public const string Wheel = "part_32193", Axle = "part_3706", Beam = "part_3702", Top = "part_3008", Wing = "part_42609",
            Propeller = "part_41530", Seat = "seat";
        /// <summary>How high it flies to be done, metres over where it started.</summary>
        public const float FlyHeight = 5;

        private static float groundHeight;

        public static readonly Tutorial Tutorial = new Tutorial() {
            id = Id,
            titleKey = "tutorial.first_plane.title",
            descriptionKey = "tutorial.first_plane.description",
            items = new[] {
                new TutorialItem(Wheel, 4),
                new TutorialItem(Axle, 2),
                new TutorialItem(Beam, 1),
                new TutorialItem(Top, 1),
                new TutorialItem(Wing, 1),
                new TutorialItem(Propeller, 1),
                new TutorialItem(Seat, 1),
            },
            steps = new[] {
                new TutorialStep() { textKey = "tutorial.first_plane.wheel", isDone = () => Count(Wheel) >= 1 },
                new TutorialStep() { textKey = "tutorial.first_plane.axle", isDone = () => Count(Axle) >= 1 },
                new TutorialStep() { textKey = "tutorial.first_plane.beam", isDone = () => Parts(Beam).Exists(b => HeldBy(b, Axle) && IsUpright(b)) },
                new TutorialStep() { textKey = "tutorial.first_plane.second_axle", isDone = () => Parts(Axle).Exists(a => HeldBy(a, Beam)) },
                new TutorialStep() { textKey = "tutorial.first_plane.wheels", isDone = () => Count(Wheel) >= 4 },
                new TutorialStep() { textKey = "tutorial.first_plane.top", isDone = () => Count(Top) >= 1 },
                new TutorialStep() { textKey = "tutorial.first_plane.wing", isDone = () => Count(Wing) >= 1 },
                new TutorialStep() { textKey = "tutorial.first_plane.propeller", isDone = () => Parts(Propeller).Exists(IsGoodPropeller) },
                new TutorialStep() { textKey = "tutorial.first_plane.seat", isDone = () => Parts(Seat).Exists(s => FacesAcross(s, Axle)) },
                new TutorialStep() { textKey = "tutorial.first_plane.sit", isDone = IsSeated },
                new TutorialStep() {
                    textKey = "tutorial.first_plane.fly",
                    begin = () => groundHeight = Where().y,
                    isDone = () => IsSeated() && Where().y - groundHeight >= FlyHeight,
                    overTime = true,
                },
            },
            buildDemo = BuildDemo,
        };

        /// <summary>Pushing the way the plane goes, in the middle of its width (off it, it'd push it round).</summary>
        public static bool IsGoodPropeller(Brick propeller) {
            Vehicles.Vehicle plane = propeller.vehicle;
            Brick beam = Parts(plane, Beam).Find(b => true);
            if (beam == null) {
                return false;
            }
            Vector3 ahead = AcrossAxles(plane, Axle);
            Vector3 off = Vehicles.VehicleShape.MeshBounds(propeller, plane.transform).center - Vehicles.VehicleShape.MeshBounds(beam, plane.transform).center;
            return PushesAlong(propeller, ahead) && Mathf.Abs(Vector3.Dot(off, Vector3.Cross(Vector3.up, ahead))) < MiddleOff;
        }

        // how far off the middle a propeller may be, metres (half a stud)
        private const float MiddleOff = 0.25f;

        /// <summary>A finished plane behind where the player starts.</summary>
        public static Vehicles.Vehicle BuildDemo(Vector3 start) {
            Brick top = TutorialDemos.Chassis(TutorialDemos.CellBeside(start, new Vector3Int(0, CustomChunkGenerator.FlatGrassHeight + 1, -10)), Wheel, Axle, Beam, Top);
            if (top == null) {
                return null;
            }
            BrickModel topModel = Server.items[Top].brickModel, wingModel = Server.items[Wing].brickModel;
            Vector3Int topSize = Bricks.BrickPlacement.SizeFor(topModel, 0);
            Brick wing = TutorialDemos.OnStuds(top, Wing, TutorialDemos.Around(topSize.x / 2 - wingModel.width / 2, topSize.z / 2 - wingModel.depth / 2, topModel.heightInPlates, 6));
            if (wing == null) {
                return top.vehicle;
            }
            int on = wingModel.heightInPlates, size = wingModel.width;
            TutorialDemos.OnStuds(wing, Propeller, TutorialDemos.Around(size / 2 - 3, size / 2 - 1, on, 6), IsGoodPropeller);
            TutorialDemos.OnStuds(wing, Seat, TutorialDemos.Around(size / 2 - 1, size / 2 - 1, on, 4), s => FacesAcross(s, Axle));
            return top.vehicle;
        }
    }
}
