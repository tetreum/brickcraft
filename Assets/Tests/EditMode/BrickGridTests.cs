using System.Collections.Generic;
using Brickcraft.Bricks;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>BrickGrid: cells of a stud by a plate, three plates and two studs a block, bricks taking their cells.</summary>
    public class BrickGridTests
    {
        private static readonly BrickModel Brick2x4 = new BrickModel() { id = "test 2x4", width = 2, depth = 4, heightInPlates = 3 };

        [SetUp]
        public void SetUp() {
            BrickGrid.Clear();
        }

        [TearDown]
        public void TearDown() {
            BrickGrid.Clear();
        }

        [Test]
        public void CellsAndBlocksConvertEvenBelowZero() {
            Assert.AreEqual(new Vector3Int(0, 0, 0), BrickGrid.CellToBlock(new Vector3Int(1, 2, 1)));
            Assert.AreEqual(new Vector3Int(-1, -1, -1), BrickGrid.CellToBlock(new Vector3Int(-1, -1, -2)));
            Assert.AreEqual(new Vector3Int(-2, 3, 4), BrickGrid.BlockToCell(new Vector3Int(-1, 1, 2)));

            Assert.IsTrue(BrickGrid.IsBlockAligned(new Vector3Int(-2, -3, 4)));
            Assert.IsFalse(BrickGrid.IsBlockAligned(new Vector3Int(-1, 0, 0)));
            Assert.IsFalse(BrickGrid.IsBlockAligned(new Vector3Int(0, 1, 0)));
        }

        [Test]
        public void FloorDivAndModRoundDown() {
            Assert.AreEqual(-1, BrickGrid.FloorDiv(-1, 2));
            Assert.AreEqual(-2, BrickGrid.FloorDiv(-3, 2));
            Assert.AreEqual(1, BrickGrid.Mod(-3, 2));
            Assert.AreEqual(2, BrickGrid.Mod(-1, 3));
        }

        [Test]
        public void ACellsCentreIsInIt() {
            foreach (Vector3Int cell in new[] { Vector3Int.zero, new Vector3Int(-5, 7, 33), new Vector3Int(100, 0, -64) }) {
                Assert.AreEqual(cell, BrickGrid.WorldToCell(BrickGrid.CellToWorld(cell, Vector3.one * 0.5f)));
            }
        }

        [Test]
        public void RotatingSwapsWidthAndDepth() {
            Assert.AreEqual(new Vector3Int(2, 3, 4), new BrickPlacement(Brick2x4, Vector3Int.zero, 0).Size);
            Assert.AreEqual(new Vector3Int(4, 3, 2), new BrickPlacement(Brick2x4, Vector3Int.zero, 1).Size);
            Assert.AreEqual(new Vector3Int(4, 3, 2), new BrickPlacement(Brick2x4, Vector3Int.zero, -1).Size);
            Assert.AreEqual(3, new BrickPlacement(Brick2x4, Vector3Int.zero, -1).rotation);
        }

        [Test]
        public void ABrickTakesItsCells() {
            BrickPlacement placement = new BrickPlacement(Brick2x4, new Vector3Int(-1, 0, 30), 1);
            Assert.AreEqual(4 * 3 * 2, new List<Vector3Int>(placement.Cells).Count);

            Brick brick = new Brick() { id = "a", placement = placement };
            BrickGrid.Register(brick);

            Assert.AreSame(brick, BrickGrid.GetBrickAt(new Vector3Int(2, 2, 31)));
            Assert.IsNull(BrickGrid.GetBrickAt(new Vector3Int(3, 0, 30)));
            Assert.IsFalse(BrickGrid.IsFree(new BrickPlacement(Brick2x4, new Vector3Int(2, 2, 31))));
            Assert.IsTrue(BrickGrid.IsFree(new BrickPlacement(Brick2x4, new Vector3Int(3, 0, 30))));

            // across chunks: x -1 is chunk -1, z 31 to 32 crosses into chunk 1
            Assert.Contains(brick, new List<Brick>(BrickGrid.BricksInChunk(new Vector2Int(-1, 0))));
            Assert.Contains(brick, new List<Brick>(BrickGrid.BricksInChunk(new Vector2Int(0, 0))));
            Assert.AreEqual(0, BrickGrid.BricksInChunk(new Vector2Int(0, 1)).Count, "z 30 and 31 only");

            BrickGrid.Unregister(brick);
            Assert.IsNull(BrickGrid.GetBrickAt(new Vector3Int(2, 2, 31)));
            Assert.AreEqual(0, BrickGrid.BricksInChunk(new Vector2Int(0, 0)).Count);
        }
    }
}
