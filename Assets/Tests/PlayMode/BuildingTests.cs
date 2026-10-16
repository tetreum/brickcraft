using System.Collections;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Brickcraft.Tests
{
    /// <summary>Where the brick in hand would go (Bricks.BrickPlacer's preview), aimed like a player does.</summary>
    [Timeout(120000)]
    public class BuildingTests
    {
        private TestGame game;

        [UnitySetUp]
        public IEnumerator SetUp() {
            game = new TestGame();
            yield return game.Start();
            yield return TestGame.StandStill();
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            yield return game.Delete();
        }

        // the preview, the brick in hand aimed at that point (from where the player is)
        private static IEnumerator aimAt(Vector3 point, System.Action<BrickPlacement?> preview) {
            Player player = Player.Instance;
            Transform eye = Camera.main.transform;
            Vector3 toward = (point - eye.position).normalized;
            player.transform.rotation = Quaternion.LookRotation(new Vector3(toward.x, 0, toward.z).normalized);
            eye.localRotation = Quaternion.Euler(-Mathf.Asin(toward.y) * Mathf.Rad2Deg, 0, 0);
            player.firstPersonController.ResetLook();
            yield return null;
            yield return null;
            BrickPlacer placer = Object.FindObjectOfType<BrickPlacer>();
            preview((BrickPlacement?)typeof(BrickPlacer).GetField("target", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(placer));
        }

        [UnityTest]
        public IEnumerator AWallsNextBrickGoesRightBesideTheLast() {
            // a floor in front of the player, a 1x4 on it an odd stud along
            BlockType stone = (BlockType)BlockDatabase.IdOf("stone");
            Transform eye = Camera.main.transform;
            Vector3Int floor = BrickGrid.CellToBlock(BrickGrid.WorldToCell(eye.position + Player.Instance.transform.forward * 3)) + Vector3Int.down * 3;
            TestGame.Floor(floor, 5, stone);
            Vector3Int cell = BrickGrid.BlockToCell(floor + Vector3Int.up) + new Vector3Int(1, 0, 0);
            Item four = Server.items["part_3010"];
            Brick last = WorldNetwork.ServerPlaceBrick(four, four.color, new BrickPlacement(four.brickModel, cell), Placer.Now(0));
            Assert.IsNotNull(last);
            Assert.AreEqual(1, BrickGrid.Mod(cell.x, BrickGrid.StudsPerBlock), "an odd stud along");
            yield return TestGame.WaitUntil(() => Physics.Raycast(last.gameObject.transform.position + Vector3.up, Vector3.down, 2), 10, "the floor and the brick solid");

            // the player west of it, on the floor, looking at its end
            Player.Instance.network.ServerTeleport(BrickGrid.CellToWorld(cell + new Vector3Int(-9, 0, 0), new Vector3(0.5f, 0, 0.5f)) + Vector3.up * 1.2f);
            yield return TestGame.StandStill();

            PlayerInventory inventory = Player.Instance.GetComponent<PlayerInventory>();
            Assert.IsTrue(inventory.ServerAdd("brick_1x6", Server.items["brick_1x6"].color, 4, 0, Player.FirstFastSlot + 6));
            Player.Instance.selectFastSlot(6);
            Item six = Server.items["brick_1x6"];

            // aimed at the floor where the 1x6's middle goes, right before the 1x4: there, not a stud off
            Vector3 middle = BrickGrid.CellToWorld(new Vector3Int(cell.x - 3, cell.y, cell.z), new Vector3(0, 0, 0.5f));
            BrickPlacement? onFloor = null;
            yield return aimAt(middle, p => onFloor = p);
            Assert.IsTrue(onFloor.HasValue, "a preview");
            Assume.That(onFloor.Value.Size.x == six.brickModel.width, "the 1x6 along X (not turned)");
            Assert.AreEqual(cell.x - 6, onFloor.Value.origin.x, "ending right before the 1x4");
            Assert.AreEqual(cell.y, onFloor.Value.origin.y, "on the floor");

            // aimed at the 1x4's end, half way up: level with it
            Vector3 end = BrickGrid.CellToWorld(cell, new Vector3(0, 1.5f, 0.5f));
            BrickPlacement? beside = null;
            yield return aimAt(end, p => beside = p);
            Assert.IsTrue(beside.HasValue, "a preview against it");
            Assert.AreEqual(cell.x - 6, beside.Value.origin.x, "right beside it");
            Assert.AreEqual(cell.y, beside.Value.origin.y, "level with it, not a plate up");
        }

        [UnityTest]
        public IEnumerator ABedOnTheGroundGoesRightBesideAWall() {
            // a floor in front of the player, a 1x4 on it an odd stud along; the player west of it
            BlockType stone = (BlockType)BlockDatabase.IdOf("stone");
            Transform eye = Camera.main.transform;
            Vector3Int floor = BrickGrid.CellToBlock(BrickGrid.WorldToCell(eye.position + Player.Instance.transform.forward * 3)) + Vector3Int.down * 3;
            TestGame.Floor(floor, 5, stone);
            Vector3Int cell = BrickGrid.BlockToCell(floor + Vector3Int.up) + new Vector3Int(1, 0, 0);
            Item four = Server.items["part_3010"];
            Brick wall = WorldNetwork.ServerPlaceBrick(four, four.color, new BrickPlacement(four.brickModel, cell), Placer.Now(0));
            Assert.IsNotNull(wall);
            yield return TestGame.WaitUntil(() => Physics.Raycast(wall.gameObject.transform.position + Vector3.up, Vector3.down, 2), 10, "the floor and the brick solid");
            Player.Instance.network.ServerTeleport(BrickGrid.CellToWorld(cell + new Vector3Int(-9, 0, 0), new Vector3(0.5f, 0, 0.5f)) + Vector3.up * 1.2f);
            yield return TestGame.StandStill();

            // a bed, even sized, aimed at the ground where it'd end right before the wall: there, not a stud off (the
            // terrain's blocks are 2 studs)
            PlayerInventory inventory = Player.Instance.GetComponent<PlayerInventory>();
            Assert.IsTrue(inventory.ServerAdd("bed", Server.items["bed"].color, 1, 0, Player.FirstFastSlot + 6));
            Player.Instance.selectFastSlot(6);
            BrickPlacement? bed = null;
            yield return aimAt(BrickGrid.CellToWorld(new Vector3Int(cell.x - 2, cell.y, cell.z), new Vector3(0, 0, 0.5f)), p => bed = p);
            Assert.IsTrue(bed.HasValue, "a preview");
            Vector3Int size = bed.Value.Size;
            Assume.That(size.x % BrickGrid.StudsPerBlock == 0, "even along the wall's way: " + size);
            yield return aimAt(BrickGrid.CellToWorld(new Vector3Int(cell.x, cell.y, cell.z), new Vector3(-size.x / 2f, 0, 0.5f)), p => bed = p);
            Assert.IsTrue(bed.HasValue, "a preview beside the wall");
            Assert.AreEqual(cell.x - size.x, bed.Value.origin.x, "ending right before the wall, an odd stud along");

            // the terrain's own bricks still go on its blocks, to be one
            Item block = null;
            foreach (Item item in Server.items.Values) {
                if (item.blockType.HasValue && item.brickModel != null) {
                    block = item;
                    break;
                }
            }
            Assume.That(block != null, "a brick that's a block");
            Assert.IsTrue(inventory.ServerAdd(block.id, block.color, 1, 0, Player.FirstFastSlot + 7));
            Player.Instance.selectFastSlot(7);
            BrickPlacement? onBlocks = null;
            yield return aimAt(BrickGrid.CellToWorld(new Vector3Int(cell.x - 4, cell.y, cell.z), new Vector3(0, 0, 0.5f)), p => onBlocks = p);
            Assert.IsTrue(onBlocks.HasValue, "a preview of the block");
            Assert.AreEqual(0, BrickGrid.Mod(onBlocks.Value.origin.x, BrickGrid.StudsPerBlock), "on a block");
            Assert.AreEqual(0, BrickGrid.Mod(onBlocks.Value.origin.z, BrickGrid.StudsPerBlock), "on a block");
        }

        [UnityTest]
        public IEnumerator GlassGoesInTheGroovesOfItsFrame() {
            // a window 1x4x3, whose LDraw origin is at its top (its glass's is at its middle): grooves 4 LDU either side
            // of it (the LDCad Shadow Library's, see LDrawGlass.Seats)
            Item frameItem = Server.items["part_3853"], glassItem = Server.items["part_3855b"];
            BrickSlot frameSlot = frameItem.brickModel.prefab.GetComponent<BrickSlot>();
            Assert.AreEqual(2, frameSlot.Seats, "a groove either side");
            Vector3Int cell = BrickGrid.WorldToCell(Camera.main.transform.position + Player.Instance.transform.forward * 4) + new Vector3Int(0, 6, 0);
            float[] depths = new float[2];
            for (int seat = 0; seat < 2; seat++) {
                Brick frame = WorldNetwork.ServerPlaceBrick(frameItem, frameItem.color, new BrickPlacement(frameItem.brickModel, cell + new Vector3Int(seat * 6, 0, 0), 1), Placer.Now(0));
                Assert.IsNotNull(frame);
                Brick glass = WorldNetwork.ServerAttachBrick(glassItem, glassItem.color, frame, Placer.Now(0), seat);
                Assert.IsNotNull(glass, "the glass goes in it");
                Assert.AreEqual(seat, glass.state, "the groove kept");
                yield return null;

                // the glass's middle is the frame's across it, its depth the groove's
                Bounds frameBounds = frame.gameObject.GetComponent<Renderer>().bounds, glassBounds = glass.gameObject.GetComponent<Renderer>().bounds;
                Vector3 off = frame.gameObject.transform.InverseTransformVector(glassBounds.center - frameBounds.center);
                Assert.Less(Mathf.Abs(off.x), 0.05f, "not to a side: " + off);
                Assert.Less(Mathf.Abs(off.y), 0.05f, "not up or down: " + off);
                frameBounds.Expand(0.02f);
                Assert.IsTrue(frameBounds.Contains(glassBounds.min) && frameBounds.Contains(glassBounds.max), "inside it");
                depths[seat] = off.z;
            }
            Vector3 origin = frameItem.brickModel.ldrawOrigin.Value;
            float groove = 4 * Bricks.LDraw.LDrawModelBuilder.Scale;
            Assert.AreEqual(origin.z - groove, depths[0], 0.01f, "the first groove");
            Assert.AreEqual(origin.z + groove, depths[1], 0.01f, "the other");
        }
    }
}
