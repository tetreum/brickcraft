using Brickcraft.Network;
using Brickcraft.World;
using Brickcraft.World.CustomGenerator;
using Mirror;
using UnityEngine;
using static Brickcraft.Tutorials.TutorialChecks;

namespace Brickcraft.Tutorials
{
    /// <summary>
    /// The basics: build a small house, brick by brick. A door frame on the ground and its door in it, the walls around
    /// a room, a window frame in a wall and its glass in it, the walls up to the door frame's top, a bed and a chest
    /// with its lid inside; then night falls, and the player sleeps in the bed until morning. A finished one stands
    /// behind where the player starts.
    /// </summary>
    public static class FirstHouse
    {
        public const string Id = "first_house";
        public const string Frame = "door_frame", Door = "door", Window = "part_3853", Glass = "part_3855b",
            LongBrick = "brick_1x6", ShortBrick = "part_3010", Bed = "bed", Chest = "chest", Lid = "chest_lid";
        /// <summary>Wall bricks it takes to start the walls, and to finish them.</summary>
        public const int FirstRows = 10, AllWalls = 28;
        /// <summary>The hour night falls at once the house is finished.</summary>
        public const float NightHour = 21;

        // it slept since night fell
        private static bool slept;

        public static readonly Tutorial Tutorial = new Tutorial() {
            id = Id,
            titleKey = "tutorial.first_house.title",
            descriptionKey = "tutorial.first_house.description",
            items = new[] {
                new TutorialItem(Frame, 1),
                new TutorialItem(Door, 1),
                new TutorialItem(LongBrick, 20),
                new TutorialItem(ShortBrick, 20),
                new TutorialItem(Window, 1),
                new TutorialItem(Glass, 1),
                new TutorialItem(Bed, 1),
                new TutorialItem(Chest, 1),
                new TutorialItem(Lid, 1),
            },
            steps = new[] {
                new TutorialStep() { textKey = "tutorial.first_house.frame", isDone = () => Placed(Frame).Count >= 1 },
                new TutorialStep() { textKey = "tutorial.first_house.door", isDone = () => IsIn(Door, Frame) },
                new TutorialStep() { textKey = "tutorial.first_house.walls", isDone = () => WallBricks() >= FirstRows },
                new TutorialStep() { textKey = "tutorial.first_house.window", isDone = () => Placed(Window).Count >= 1 },
                new TutorialStep() { textKey = "tutorial.first_house.glass", isDone = () => IsIn(Glass, Window) },
                new TutorialStep() { textKey = "tutorial.first_house.walls_up", isDone = () => WallBricks() >= AllWalls },
                new TutorialStep() { textKey = "tutorial.first_house.bed", isDone = () => Placed(Bed).Count >= 1 },
                new TutorialStep() { textKey = "tutorial.first_house.chest", isDone = () => Placed(Chest).Count >= 1 },
                new TutorialStep() { textKey = "tutorial.first_house.lid", isDone = () => IsIn(Lid, Chest) },
                new TutorialStep() {
                    textKey = "tutorial.first_house.sleep",
                    begin = NightFalls,
                    isDone = SleptTillMorning,
                    overTime = true,
                },
            },
            buildDemo = BuildDemo,
        };

        /// <summary>The player's wall bricks placed.</summary>
        public static int WallBricks() {
            return Placed(LongBrick).Count + Placed(ShortBrick).Count;
        }

        // the house finished: night, at once (the server's, this game being played alone)
        private static void NightFalls() {
            slept = false;
            if (NetworkServer.active && WorldTime.IsDay) {
                WorldNetwork.ServerSetTime(WorldTime.NextTimeAt(NightHour));
            }
        }

        // it lay in a bed, and the night went by (see PlayerSleep)
        private static bool SleptTillMorning() {
            PlayerNetwork network = Player.Instance != null ? Player.Instance.network : null;
            if (network != null && !string.IsNullOrEmpty(network.sleepingIn)) {
                slept = true;
            }
            return slept && WorldTime.IsDay;
        }

        /// <summary>
        /// A finished house behind where the player starts, its door towards it: 12 by 8 studs, walls 6 bricks high of
        /// 1x6 and 1x4 bricks, the door in the middle of the front, the window in the middle of the back a brick up, the
        /// bed along the back and the chest beside it.
        /// </summary>
        public static bool BuildDemo(Vector3 start) {
            return Build(TutorialDemos.CellBeside(start, new Vector3Int(-3, CustomChunkGenerator.FlatGrassHeight + 1, -16)), true);
        }

        /// <summary>The finished house with its corner at that cell of the world's grid, a demo's or not; false if a part didn't go.</summary>
        public static bool Build(Vector3Int o, bool demo) {
            const int Width = 12, Depth = 8, Rows = 6, Row = 3;
            for (int row = 0; row < Rows; row++) {
                int y = o.y + row * Row;
                // front (+Z: the door's 4 in the middle), back (the window's 4 in the middle, rows 1 to 3), sides between
                int front = o.z + Depth - 1;
                wall(new Vector3Int(o.x, y, front), 4, true, demo);
                wall(new Vector3Int(o.x + 8, y, front), 4, true, demo);
                if (row >= 1 && row <= 3) {
                    wall(new Vector3Int(o.x, y, o.z), 4, true, demo);
                    wall(new Vector3Int(o.x + 8, y, o.z), 4, true, demo);
                } else {
                    wall(new Vector3Int(o.x, y, o.z), Width, true, demo);
                }
                wall(new Vector3Int(o.x, y, o.z + 1), Depth - 2, false, demo);
                wall(new Vector3Int(o.x + Width - 1, y, o.z + 1), Depth - 2, false, demo);
            }
            Brick door = TutorialDemos.AttachWorld(Door, TutorialDemos.PlaceWorld(Frame, new Vector3Int(o.x + 4, o.y, o.z + Depth - 1), 2, demo), demo);
            Brick glass = TutorialDemos.AttachWorld(Glass, TutorialDemos.PlaceWorld(Window, new Vector3Int(o.x + 4, o.y + Row, o.z), 2, demo), demo);
            Brick bed = TutorialDemos.PlaceWorld(Bed, new Vector3Int(o.x + 1, o.y, o.z + 1), 1, demo);
            Brick lid = TutorialDemos.AttachWorld(Lid, TutorialDemos.PlaceWorld(Chest, new Vector3Int(o.x + 9, o.y, o.z + 3), 1, demo), demo);
            return door != null && glass != null && bed != null && lid != null;
        }

        // a stretch of wall that long, along X or Z, of 1x6 bricks and a 1x4 for what's left
        private static void wall(Vector3Int from, int length, bool alongX, bool demo) {
            int at = 0;
            while (at < length) {
                bool six = length - at >= 6;
                int size = six ? 6 : 4;
                Vector3Int cell = alongX ? from + new Vector3Int(at, 0, 0) : from + new Vector3Int(0, 0, at);
                TutorialDemos.PlaceWorld(six ? LongBrick : ShortBrick, cell, alongX ? 0 : 1, demo);
                at += size;
            }
        }
    }
}
