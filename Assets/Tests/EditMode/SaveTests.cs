using System;
using System.Collections.Generic;
using System.IO;
using Brickcraft.World;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>A chunk's changes come back from a save as they were (ChunkChangesSerializer).</summary>
    public class SaveTests
    {
        private ChunkChanges changes;
        private SavedBrick brick, door;
        private ushort placed, dug;

        [SetUp]
        public void SetUp() {
            changes = new ChunkChanges();
            placed = ChunkChanges.BlockIndex(3, 70, 15);
            dug = ChunkChanges.BlockIndex(0, 64, 0);
            changes.blocks[placed] = (byte)GameData.Block("stone");
            changes.blocks[dug] = (byte)BlockType.Air;
            changes.placers[placed] = new Placer() { playerId = 7, placedAt = 1760000000 };
            changes.colors[placed] = 4;

            brick = new SavedBrick() {
                id = Guid.NewGuid(), itemId = "chest", origin = new Vector3Int(-3, 150, 31), rotation = 3, color = 72,
                placer = new Placer() { playerId = 2, placedAt = 1760000001 }, state = 1,
            };
            door = new SavedBrick() { id = Guid.NewGuid(), itemId = "door", origin = brick.origin, color = 4, attachedTo = brick.id };
            changes.bricks[brick.id] = brick;
            changes.bricks[door.id] = door;
            changes.storage[brick.id] = new List<InventoryItem>() { new InventoryItem() { slot = 2, itemId = "pickaxe", color = 72, quantity = 1, health = 12 } };
            changes.npcs.Add(new SavedNpc() { type = "wolf", x = 1.5, y = 70, z = -2.25, yaw = 90, health = 8, owner = 2, ownerName = "Me", pack = Guid.NewGuid() });
            changes.drops.Add(new SavedDrop() { itemId = "stone", color = 72, quantity = 3, x = 4, y = 71, z = 5, lifetime = 120 });
        }

        [Test]
        public void BlockIndicesGoBothWays() {
            Assert.AreEqual(new Vector3Int(3, 70, 15), ChunkChanges.BlockFromIndex(placed));
            Assert.AreEqual(new Vector3Int(15, 255, 15), ChunkChanges.BlockFromIndex(ChunkChanges.BlockIndex(15, 255, 15)));
        }

        [Test]
        public void AChunksChangesComeBackAsTheyWere() {
            ChunkChanges loaded = ChunkChangesSerializer.Deserialize(ChunkChangesSerializer.Serialize(changes));

            CollectionAssert.AreEquivalent(changes.blocks, loaded.blocks);
            CollectionAssert.AreEquivalent(changes.placers, loaded.placers);
            CollectionAssert.AreEquivalent(changes.colors, loaded.colors);
            CollectionAssert.AreEquivalent(changes.bricks, loaded.bricks);

            Assert.AreEqual(1, loaded.storage.Count);
            CollectionAssert.AreEqual(changes.storage[brick.id], loaded.storage[brick.id]);
            CollectionAssert.AreEqual(changes.npcs, loaded.npcs);
            CollectionAssert.AreEqual(changes.drops, loaded.drops);
        }

        [Test]
        public void PagesForClientsLeaveOutWhatTheyDontGet() {
            List<byte[]> pages = ChunkChangesSerializer.SerializePages(changes);
            Assert.AreEqual(1, pages.Count);

            ChunkChanges loaded = ChunkChangesSerializer.Deserialize(pages[0]);
            Assert.AreEqual(2, loaded.bricks.Count);
            Assert.AreEqual(0, loaded.npcs.Count);
            Assert.AreEqual(0, loaded.storage.Count);
            Assert.AreEqual(0, loaded.drops.Count);
        }

        [Test]
        public void BigChunksAreSentInPages() {
            ChunkChanges big = new ChunkChanges();
            for (int i = 0; i < 1300; i++) {
                SavedBrick saved = new SavedBrick() { id = Guid.NewGuid(), itemId = "brick_2x2", origin = new Vector3Int(i % 32, i / 32, 0), color = 4 };
                big.bricks[saved.id] = saved;
            }
            List<byte[]> pages = ChunkChangesSerializer.SerializePages(big);
            Assert.Greater(pages.Count, 1);

            int bricks = 0;
            foreach (byte[] page in pages) {
                bricks += ChunkChangesSerializer.Deserialize(page).bricks.Count;
            }
            Assert.AreEqual(1300, bricks);
        }

        [Test]
        public void EmptyChangesAreEmpty() {
            Assert.IsTrue(new ChunkChanges().IsEmpty);
            Assert.IsTrue(ChunkChangesSerializer.Deserialize(ChunkChangesSerializer.Serialize(new ChunkChanges())).IsEmpty);
        }

        [Test]
        public void OtherVersionsAreRejected() {
            using (MemoryStream other = new MemoryStream()) {
                using (System.IO.Compression.DeflateStream deflate = new System.IO.Compression.DeflateStream(other, System.IO.Compression.CompressionMode.Compress)) {
                    deflate.WriteByte(ChunkChangesSerializer.Version + 1);
                }
                Assert.Throws<InvalidDataException>(() => ChunkChangesSerializer.Deserialize(other.ToArray()));
            }
        }
    }
}
