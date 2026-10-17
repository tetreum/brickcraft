using System.Collections.Generic;
using System.IO;
using Brickcraft.Bricks;
using Brickcraft.Bricks.LDraw;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>LDraw models read as bricks (LDrawImport): what the parts lister writes comes back where it was.</summary>
    public class LDrawImportTests
    {
        private string file;

        [SetUp]
        public void SetUp() {
            GameData.Load();
            file = Path.Combine(Path.GetTempPath(), "brickcraft-import-" + System.Guid.NewGuid() + ".ldr");
        }

        [TearDown]
        public void TearDown() {
            File.Delete(file);
        }

        private static Item item(string id) {
            return Server.items[id];
        }

        // an .ldr of those bricks, written like the parts lister does (see PartsList.Placements)
        private string[] write(params (string item, int color, Vector3Int origin, int rotation)[] bricks) {
            List<LDrawPlacement> placements = new List<LDrawPlacement>();
            foreach (var brick in bricks) {
                Item it = item(brick.item);
                BrickPlacement placement = new BrickPlacement(it.brickModel, brick.origin, brick.rotation);
                placements.Add(LDrawWriter.Place(it.LDrawPart, brick.color, placement.Position, placement.Rotation,
                    LDrawWriter.OriginOf(it.brickModel, it.LDrawPart, it.ldrawShape), BrickGrid.CellToWorld(Vector3Int.zero)));
            }
            LDrawWriter.Write(file, "test", placements);
            return File.ReadAllLines(file);
        }

        [Test]
        public void WhatThePartsListerWritesComesBack() {
            int red = BrickColorPalette.FromLDraw(4).id, blue = BrickColorPalette.FromLDraw(1).id;
            LDrawImport model = LDrawImport.Read(write(
                ("part_3001", red, new Vector3Int(2, 0, 0), 0),
                ("part_3001", blue, new Vector3Int(10, 3, 2), 1),
                ("part_3020", red, new Vector3Int(2, 3, 0), 3),
                ("part_3003", blue, new Vector3Int(6, 0, 8), 2)));

            Assert.AreEqual(0, model.Skipped);
            Assert.AreEqual(4, model.bricks.Count);
            // its box from its lowest corner: (2, 0, 0) is (0, 0, 0)
            AssertBrick(model.bricks[0], "part_3001", red, new Vector3Int(0, 0, 0), 0);
            AssertBrick(model.bricks[1], "part_3001", blue, new Vector3Int(8, 3, 2), 1);
            AssertBrick(model.bricks[2], "part_3020", red, new Vector3Int(0, 3, 0), 3);
            AssertBrick(model.bricks[3], "part_3003", blue, new Vector3Int(4, 0, 8), 2);
            Assert.AreEqual(new Vector3Int(10, 6, 10), model.Size);
        }

        [Test]
        public void ItemsMadeOfPartsComeBackWhole() {
            // a brick, and a bed (made of the parts of its model.ldr) turned a quarter, written like the parts lister does:
            // the bed as a submodel of its own (see PartsList.Placements)
            Item brick = item("part_3001"), bed = item("bed");
            Assume.That(bed.ldr, "the bed is made of parts");
            int red = BrickColorPalette.FromLDraw(4).id;
            Vector3 corner = BrickGrid.CellToWorld(Vector3Int.zero);
            BrickPlacement brickAt = new BrickPlacement(brick.brickModel, new Vector3Int(0, 0, 0), 0);
            BrickPlacement bedAt = new BrickPlacement(bed.brickModel, new Vector3Int(6, 0, 2), 1);
            LDrawPlacement bedPlaced = LDrawWriter.Place(bed.id, bed.color, bedAt.Position, bedAt.Rotation, Vector3.zero, corner);
            bedPlaced.submodel = true;
            LDrawWriter.Write(file, "test", new List<LDrawPlacement>() {
                LDrawWriter.Place(brick.LDrawPart, red, brickAt.Position, brickAt.Rotation, LDrawWriter.OriginOf(brick.brickModel, brick.LDrawPart, brick.ldrawShape), corner),
                bedPlaced,
            });
            string[] lines = File.ReadAllLines(file);

            // LDraw tools see a model and the bed's submodel, with all its parts
            Assert.Contains("0 FILE " + LDrawWriter.SubmodelFile(bed.id), lines);
            Assert.AreEqual(1 + LDrawContraption.PartsOf(bed).Count, LDrawContraption.Parse(lines).Count, "the brick and the bed's parts");

            // and it comes back a bed, where it was
            LDrawImport model = LDrawImport.Read(lines);
            Assert.AreEqual(0, model.Skipped);
            Assert.AreEqual(2, model.bricks.Count);
            AssertBrick(model.bricks[0], "part_3001", red, new Vector3Int(0, 0, 0), 0);
            AssertBrick(model.bricks[1], "bed", bed.color, new Vector3Int(6, 0, 2), 1);
        }

        private static void AssertBrick(ImportedBrick brick, string itemId, int color, Vector3Int origin, int rotation) {
            Assert.AreEqual(itemId, brick.itemId);
            Assert.AreEqual(color, brick.color, itemId + "'s colour");
            Assert.AreEqual(origin, brick.origin, itemId + "'s place");
            Assert.AreEqual(rotation, brick.rotation, itemId + "'s rotation");
        }

        [Test]
        public void AttachmentsGoInTheirBricksSlot() {
            // a chest turned a quarter, and its lid in its slot, written like the parts lister does (where the lid hangs)
            Item chest = item("chest"), lid = item("chest_lid");
            BrickPlacement placement = new BrickPlacement(chest.brickModel, new Vector3Int(4, 0, 2), 1);
            BrickSlot slot = LDrawImport.SlotOf(chest.brickModel);
            Assume.That(slot != null && slot.fits == lid.brickModel.attachesTo, "the chest's lid goes in its slot");
            Vector3 corner = BrickGrid.CellToWorld(Vector3Int.zero);
            Vector3 hinge = corner + LDrawImport.SlotPoint(placement, slot);
            Quaternion hanging = placement.Rotation * slot.point.rotation * lid.brickModel.prefab.transform.rotation;
            List<LDrawPlacement> placements = new List<LDrawPlacement>() {
                LDrawWriter.Place(chest.LDrawPart, 4, placement.Position, placement.Rotation, LDrawWriter.OriginOf(chest.brickModel, chest.LDrawPart, chest.ldrawShape), corner),
                LDrawWriter.Place(lid.LDrawPart, 4, hinge, hanging, LDrawWriter.OriginOf(lid.brickModel, lid.LDrawPart, lid.ldrawShape), corner),
            };
            LDrawWriter.Write(file, "test", placements);
            LDrawImport model = LDrawImport.Read(File.ReadAllLines(file));

            Assert.AreEqual(0, model.Skipped);
            Assert.AreEqual(2, model.bricks.Count);
            Assert.AreEqual("chest", model.bricks[0].itemId);
            Assert.AreEqual(1, model.bricks[0].rotation);
            Assert.AreEqual("chest_lid", model.bricks[1].itemId);
            Assert.AreEqual(1, model.bricks[1].attachedTo, "in the chest");
            Assert.IsTrue(model.Placed(Vector3Int.one, 2)[1].IsAttachment, "still in it, turned");
        }

        [Test]
        public void TurningTheModelTurnsEveryCell() {
            LDrawImport model = LDrawImport.Read(write(
                ("part_3001", 4, new Vector3Int(0, 0, 0), 0),
                ("part_3004", 4, new Vector3Int(4, 0, 6), 1),
                ("part_3020", 4, new Vector3Int(0, 3, 3), 0)));
            HashSet<Vector3Int> unturned = cells(model.Placed(Vector3Int.zero, 0));
            Vector3Int corner = new Vector3Int(100, 20, -50);

            for (int turns = 0; turns < 4; turns++) {
                HashSet<Vector3Int> expected = new HashSet<Vector3Int>();
                foreach (Vector3Int cell in unturned) {
                    // a quarter turn: (x, z) to (z, width - 1 - x), as many times
                    Vector3Int c = cell;
                    int width = model.Size.x, depth = model.Size.z;
                    for (int turn = 0; turn < turns; turn++) {
                        c = new Vector3Int(c.z, c.y, width - 1 - c.x);
                        int swap = width;
                        width = depth;
                        depth = swap;
                    }
                    expected.Add(c + corner);
                }
                CollectionAssert.AreEquivalent(expected, cells(model.Placed(corner, turns)), turns + " turns");
            }
        }

        private static HashSet<Vector3Int> cells(List<ImportedBrick> bricks) {
            HashSet<Vector3Int> cells = new HashSet<Vector3Int>();
            foreach (ImportedBrick brick in bricks) {
                foreach (Vector3Int cell in new BrickPlacement(Server.items[brick.itemId].brickModel, brick.origin, brick.rotation).Cells) {
                    Assert.IsTrue(cells.Add(cell), "two bricks in " + cell);
                }
            }
            return cells;
        }

        [Test]
        public void PartsItCantPlaceAreCounted() {
            string[] lines = {
                "1 4 0 -24 0 1 0 0 0 1 0 0 0 1 3001.dat",          // a 2x4 brick
                "1 4 0 -24 0 1 0 0 0 1 0 0 0 1 3001.dat",          // the same, again
                "1 4 200 -24 0 0.7071 0 0.7071 0 1 0 -0.7071 0 0.7071 3001.dat", // turned 45 degrees
                "1 4 400 -24 0 1 0 0 0 1 0 0 0 1 notapart.dat",    // not a part the game has
                "1 4 600 -24 0 1 0 0 0 0 -1 0 1 0 3001.dat",       // lying on its side
                "1 4 800 -24 0 -1 0 0 0 1 0 0 0 1 3001.dat",       // mirrored: symmetric, the same brick
                "1 4 1000 -8 10 1 0 0 0 1 0 0 0 1 3023.dat",       // an old number: LDraw has 3023a and 3023b now
                "1 4 1200 -26 0 1 0 0 0 1 0 0 0 1 3001.dat",       // a quarter plate up: in the nearest plate
            };
            LDrawImport model = LDrawImport.Read(lines);

            Assert.AreEqual(4, model.bricks.Count);
            Assert.AreEqual(0, model.bricks[1].rotation, "mirrored, not turned");
            Assert.AreEqual("part_3023a", model.bricks[2].itemId);
            Assert.AreEqual(model.bricks[0].origin.y, model.bricks[3].origin.y, "snapped to the plate");
            Assert.AreEqual(1, model.overlapping);
            Assert.AreEqual(2, model.offGrid);
            Assert.AreEqual(1, model.unknown);
            CollectionAssert.AreEqual(new[] { "notapart" }, model.unknownParts);
        }

        [Test]
        public void AMirroredCornerBrickHasItsNotchMirrored() {
            BrickModel corner = BrickModels.Get("2357");
            LDrawImport plain = LDrawImport.Read(new[] { "1 4 10 -24 10 1 0 0 0 1 0 0 0 1 2357.dat" });
            LDrawImport mirrored = LDrawImport.Read(new[] { "1 4 10 -24 10 -1 0 0 0 1 0 0 0 1 2357.dat" });

            // the notch's column, from the box's middle, in half studs
            Vector2Int notch(LDrawImport model) {
                BrickPlacement placement = new BrickPlacement(corner, model.bricks[0].origin, model.bricks[0].rotation);
                for (int x = 0; x < 2; x++) {
                    for (int z = 0; z < 2; z++) {
                        if (!placement.FillsColumn(x, z)) {
                            return new Vector2Int(2 * x - 1, 2 * z - 1);
                        }
                    }
                }
                Assert.Fail("no notch");
                return default;
            }
            Vector2Int plainNotch = notch(plain);
            Assert.AreEqual(new Vector2Int(-plainNotch.x, plainNotch.y), notch(mirrored), "mirrored left to right");
        }

        [Test]
        public void SubmodelsAreTakenApart() {
            string[] lines = {
                "0 FILE main.ldr",
                "1 16 0 0 0 1 0 0 0 1 0 0 0 1 wall.ldr",
                "1 16 0 -24 0 1 0 0 0 1 0 0 0 1 wall.ldr",
                "0 FILE wall.ldr",
                "1 4 0 0 0 1 0 0 0 1 0 0 0 1 3001.dat",
                "1 1 80 0 0 1 0 0 0 1 0 0 0 1 3001.dat",
            };
            LDrawImport model = LDrawImport.Read(lines);

            Assert.AreEqual(4, model.bricks.Count);
            Assert.AreEqual(0, model.Skipped);
            Assert.AreEqual(new Vector3Int(8, 6, 2), model.Size, "two walls of two bricks, one on the other");
        }
    }
}
