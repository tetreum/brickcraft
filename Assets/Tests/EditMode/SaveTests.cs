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
        private SavedBrick brick, door, axle, onStuds;
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
            axle = new SavedBrick() {
                id = Guid.NewGuid(), itemId = "brick_2x2", origin = brick.origin, color = 0, attachedTo = brick.id,
                joint = new Bricks.BrickJoint() { holderConnector = 2, connector = 1, slide = -3, spin = 3, flip = true },
            };
            changes.bricks[axle.id] = axle;
            onStuds = new SavedBrick() {
                id = Guid.NewGuid(), itemId = "brick_2x2", origin = brick.origin, color = 4, attachedTo = axle.id,
                joint = Bricks.BrickJoint.OnStuds(new Vector3Int(1, 3, -2), 2),
            };
            changes.bricks[onStuds.id] = onStuds;
            changes.storage[brick.id] = new List<InventoryItem>() { new InventoryItem() { slot = 2, itemId = "pickaxe", color = 72, quantity = 1, health = 12 } };
            changes.npcs.Add(new SavedNpc() { type = "wolf", x = 1.5, y = 70, z = -2.25, yaw = 90, health = 8, owner = 2, ownerName = "Me", pack = Guid.NewGuid() });
            changes.drops.Add(new SavedDrop() { itemId = "stone", color = 72, quantity = 3, x = 4, y = 71, z = 5, lifetime = 120 });
            changes.vehicles.Add(new SavedVehicle() {
                id = Guid.NewGuid(), x = 1e7 + 0.5, y = 68.25, z = -3, rotation = Quaternion.Euler(0, 90, 5),
                parts = new List<SavedVehiclePart>() {
                    new SavedVehiclePart() { id = Guid.NewGuid(), itemId = "brick_2x2", color = 4, origin = Vector3Int.zero, rotation = 1 },
                    new SavedVehiclePart() { id = Guid.NewGuid(), itemId = "seat", color = 72, origin = new Vector3Int(-2, 3, 1), state = 2,
                        holder = Guid.NewGuid(), joint = new Bricks.BrickJoint() { holderConnector = 1, connector = 0, slide = 2, spin = 1 } },
                    new SavedVehiclePart() { id = Guid.NewGuid(), itemId = "brick_2x2", color = 1, origin = Vector3Int.zero,
                        holder = Guid.NewGuid(), joint = Bricks.BrickJoint.OnStuds(new Vector3Int(0, 3, 5), 1) },
                    new SavedVehiclePart() { id = Guid.NewGuid(), itemId = "no_such_item", origin = new Vector3Int(0, 6, 0) },
                },
                wires = new List<SavedWire>() {
                    new SavedWire() { seat = Guid.NewGuid(), part = Guid.NewGuid(), action = 1, source = 2, key = (int)KeyCode.W, invert = true },
                    new SavedWire() { seat = Guid.NewGuid(), part = Guid.NewGuid() },
                },
            });
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

            Assert.AreEqual(axle.joint, loaded.bricks[axle.id].joint, "a connected brick keeps how it's held");
            Assert.AreEqual(onStuds.joint, loaded.bricks[onStuds.id].joint, "and one on a connected brick's studs, where it is on them");
            Assert.IsFalse(loaded.bricks[brick.id].joint.HasValue);
            Assert.AreEqual(1, loaded.storage.Count);
            CollectionAssert.AreEqual(changes.storage[brick.id], loaded.storage[brick.id]);
            CollectionAssert.AreEqual(changes.npcs, loaded.npcs);
            CollectionAssert.AreEqual(changes.drops, loaded.drops);

            Assert.AreEqual(1, loaded.vehicles.Count);
            SavedVehicle vehicle = changes.vehicles[0], back = loaded.vehicles[0];
            Assert.AreEqual(vehicle.id, back.id);
            Assert.AreEqual(new[] { vehicle.x, vehicle.y, vehicle.z }, new[] { back.x, back.y, back.z });
            Assert.AreEqual(vehicle.rotation, back.rotation);
            CollectionAssert.AreEqual(vehicle.parts.GetRange(0, 3), back.parts, "the parts of items that aren't there are lost");
            CollectionAssert.AreEqual(vehicle.wires, back.wires);
        }

        [Test]
        public void PagesForClientsLeaveOutWhatTheyDontGet() {
            List<byte[]> pages = ChunkChangesSerializer.SerializePages(changes);
            Assert.AreEqual(1, pages.Count);

            ChunkChanges loaded = ChunkChangesSerializer.Deserialize(pages[0]);
            Assert.AreEqual(4, loaded.bricks.Count);
            Assert.AreEqual(axle.joint, loaded.bricks[axle.id].joint, "players get how connected bricks are held");
            Assert.AreEqual(0, loaded.npcs.Count);
            Assert.AreEqual(0, loaded.storage.Count);
            Assert.AreEqual(0, loaded.drops.Count);
            Assert.AreEqual(0, loaded.vehicles.Count);
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
        public void OldWorldsAreUpgradedNeitherFlatNorTutorials() {
            string folder = Path.Combine(Path.GetTempPath(), "brickcraft-test-" + Guid.NewGuid().ToString("N"));
            try {
                WorldStorage.Create(folder, "Old", 5, Difficulty.Normal, true, terrain: TerrainKind.Flat, tutorial: "x");
                // as format 15 wrote it: without its last fields (a bool, and a string of one character)
                string path = Path.Combine(folder, WorldStorage.HeaderFile);
                byte[] header = File.ReadAllBytes(path);
                byte[] old = new byte[header.Length - 3];
                Array.Copy(header, old, old.Length);
                old[4] = 15;
                old[5] = 0;
                File.WriteAllBytes(path, old);

                new Brickcraft.World.Migrations.Migration15To16().Migrate(folder);
                WorldStorage info = WorldStorage.ReadInfo(folder);
                Assert.AreEqual(WorldStorage.FormatVersion, info.Format);
                Assert.AreEqual("Old", info.Name, "the rest as it was");
                Assert.AreEqual(TerrainKind.Generated, info.Terrain);
                Assert.AreEqual("", info.Tutorial);
            } finally {
                Directory.Delete(folder, true);
            }
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
