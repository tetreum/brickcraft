using System.Collections;
using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Vehicles;
using Brickcraft.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Brickcraft.Tests
{
    /// <summary>Vehicles in a game: started on the world, built on their own cells, split and merged (VehicleSystem).</summary>
    [Timeout(120000)]
    public class VehicleTests
    {
        private TestGame game;
        private Item brick2x2;

        [UnitySetUp]
        public IEnumerator SetUp() {
            game = new TestGame();
            yield return game.Start();
            brick2x2 = Server.items["brick_2x2"];
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            yield return game.Delete();
        }

        // a cell high above the player, in the air whatever the terrain
        private static Vector3Int high() {
            Vector3Int block = TestGame.PlayerBlock;
            return BrickGrid.BlockToCell(new Vector3Int(block.x, Mathf.Min(block.y + 20, Chunk.NumSlices * Chunk.SliceHeight - 10), block.z));
        }

        private static void near(Vector3 expected, Vector3 actual, string what) {
            Assert.Less(Vector3.Distance(expected, actual), 0.001f, what + ": " + actual + " instead of " + expected);
        }

        private BrickPlacement at(Vector3Int origin) {
            return new BrickPlacement(brick2x2.brickModel, origin);
        }

        [UnityTest]
        public IEnumerator AVehicleIsBuiltOnItsOwnCells() {
            Vector3Int cell = high();
            Vehicle vehicle = VehicleSystem.ServerCreate(brick2x2, 4, at(cell));
            yield return null;

            Assert.AreEqual(1, vehicle.grid.Count);
            Brick first = vehicle.grid.GetBrickAt(Vector3Int.zero);
            Assert.IsNotNull(first, "its first part is its cell 0");
            Assert.AreSame(vehicle, first.vehicle);
            Assert.AreSame(vehicle.transform, first.gameObject.transform.parent, "under it, moving with it");
            Assert.IsNull(BrickGrid.GetBrickAt(cell), "not on the world's grid");
            near(BrickGrid.CellToWorld(cell), vehicle.transform.position, "where it was placed");

            Brick top = VehicleSystem.ServerPlace(vehicle, brick2x2, 1, at(new Vector3Int(0, 3, 0)));
            Assert.AreEqual(2, vehicle.grid.Count);
            Assert.AreEqual(2, vehicle.parts.Count, "its parts, for the clients");
            near(vehicle.transform.TransformPoint(top.placement.LocalPosition), top.gameObject.transform.position, "on top of the first");

            // it moves and turns, its bricks with it
            vehicle.transform.SetPositionAndRotation(vehicle.transform.position + Vector3.up * 2, Quaternion.Euler(0, 30, 10));
            near(vehicle.transform.TransformPoint(top.placement.LocalPosition), top.gameObject.transform.position, "moved along");
        }

        // a small wheel of LDraw's
        private static Item wheel() {
            Item best = null;
            foreach (Item item in Server.items.Values) {
                if (item.VehicleRole == VehicleRole.Wheel && item.brickModel != null && item.brickModel.width <= 2 && item.brickModel.depth <= 4
                        && (best == null || string.CompareOrdinal(item.id, best.id) < 0)) {
                    best = item;
                }
            }
            return best;
        }

        [UnityTest]
        public IEnumerator APlayerStartsOneWithAWheelAndBuildsOnIt() {
            Item wheelItem = wheel();
            Assume.That(wheelItem, Is.Not.Null, "a small wheel in the catalogue");
            Player player = Player.Instance;
            Network.PlayerInventory inventory = player.GetComponent<Network.PlayerInventory>();
            int slot = Player.FirstFastSlot + 4;
            yield return TestGame.Wait(1); // on the ground

            // in front of the player, in the air
            Transform eye = Camera.main.transform;
            Vector3Int cell = BrickGrid.WorldToCell(eye.position + eye.forward * 2.5f);
            Assert.IsTrue(inventory.ServerAdd(wheelItem.id, wheelItem.color, 1, 0, slot));
            int before = Vehicle.All.Count;
            player.network.CmdCreateVehicle(slot, cell, 0);
            yield return null;
            Assert.AreEqual(before + 1, Vehicle.All.Count, "the wheel started a vehicle");
            Assert.IsFalse(inventory.ServerGetSlot(slot).HasValue, "the wheel was used");
            Vehicle vehicle = null;
            foreach (Vehicle v in Vehicle.All) {
                if (v.grid.Count == 1 && v.grid.GetBrickAt(Vector3Int.zero) != null && v.grid.GetBrickAt(Vector3Int.zero).itemId == wheelItem.id) {
                    vehicle = v;
                }
            }
            Assert.IsNotNull(vehicle);

            // a brick on top of it
            Assert.IsTrue(inventory.ServerAdd("brick_2x2", brick2x2.color, 1, 0, slot));
            Vector3Int top = new Vector3Int(0, wheelItem.brickModel.heightInPlates, 0);
            player.network.CmdPlaceVehicleBrick(slot, vehicle.netIdentity, top, 0);
            yield return null;
            Assert.AreEqual(2, vehicle.grid.Count, "on the vehicle");
            Assert.IsNotNull(vehicle.grid.GetBrickAt(top));
            Assert.IsFalse(inventory.ServerGetSlot(slot).HasValue, "the brick was used");

            // floating in the air: a brick that touches nothing of it isn't placed
            Assert.IsTrue(inventory.ServerAdd("brick_2x2", brick2x2.color, 1, 0, slot));
            player.network.CmdPlaceVehicleBrick(slot, vehicle.netIdentity, top + new Vector3Int(0, 10, 0), 0);
            yield return null;
            Assert.AreEqual(2, vehicle.grid.Count, "not placed apart from it");
        }

        [UnityTest]
        public IEnumerator TakingTheMiddleOutSplitsItAndPuttingItBackMergesThem() {
            Vector3Int cell = high();
            Vehicle vehicle = VehicleSystem.ServerCreate(brick2x2, 4, at(cell));
            Brick middle = VehicleSystem.ServerPlace(vehicle, brick2x2, 4, at(new Vector3Int(2, 0, 0)));
            Brick end = VehicleSystem.ServerPlace(vehicle, brick2x2, 4, at(new Vector3Int(4, 0, 0)));
            yield return null;
            int before = Vehicle.All.Count;

            VehicleSystem.ServerRemove(middle);
            yield return null;
            Assert.AreEqual(before + 1, Vehicle.All.Count, "two vehicles now");
            Assert.AreEqual(1, vehicle.grid.Count);
            Vehicle piece = Server.bricks[end.id].vehicle;
            Assert.AreNotSame(vehicle, piece, "the end is a vehicle of its own");
            Assert.AreEqual(1, piece.grid.Count);
            Brick moved = Server.bricks[end.id];
            near(vehicle.transform.TransformPoint(at(new Vector3Int(4, 0, 0)).LocalPosition), moved.gameObject.transform.position, "where it was");

            // the middle back: one vehicle again
            VehicleSystem.ServerPlace(vehicle, brick2x2, 4, at(new Vector3Int(2, 0, 0)));
            yield return null;
            Assert.AreEqual(before, Vehicle.All.Count, "merged");
            Assert.AreEqual(3, vehicle.grid.Count);
            Assert.AreSame(vehicle, Server.bricks[end.id].vehicle, "the end is this one's again, the same brick");
            Assert.IsNotNull(vehicle.grid.GetBrickAt(new Vector3Int(4, 0, 0)), "the end in its cells");
            Assert.AreEqual(3, vehicle.parts.Count);
        }

        [UnityTest]
        public IEnumerator AVehicleIsSavedWhereItWas() {
            Vehicle vehicle = VehicleSystem.ServerCreate(brick2x2, 4, at(high()));
            Brick top = VehicleSystem.ServerPlace(vehicle, brick2x2, 1, at(new Vector3Int(1, 3, 0)));
            // moved and turned (its body too: it's interpolated)
            vehicle.transform.SetPositionAndRotation(vehicle.transform.position + new Vector3(0.3f, 0.5f, 0), Quaternion.Euler(0, 30, 0));
            vehicle.Body.position = vehicle.transform.position;
            vehicle.Body.rotation = vehicle.transform.rotation;
            string id = vehicle.vehicleId, topId = top.id;
            Vector3 position = FloatingOrigin.ToAbsolute(vehicle.transform.position);
            // saved with its chunk once it's loaded (like what's on the ground)
            yield return TestGame.WaitUntil(() => Npcs.NpcSystem.IsLoaded(vehicle.transform.position), 30, "its chunk loaded");

            yield return game.Stop();
            Assert.AreEqual(0, Vehicle.All.Count, "gone with the game");
            yield return game.Start();
            Vehicle back = null;
            yield return TestGame.WaitUntil(() => {
                foreach (Vehicle v in Vehicle.All) {
                    if (v.vehicleId == id) {
                        back = v;
                    }
                }
                return back != null;
            }, 10, "the vehicle coming back");

            near(position, FloatingOrigin.ToAbsolute(back.transform.position), "where it was");
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 30, 0), back.transform.rotation), 0.01f, "turned as it was");
            Assert.AreEqual(2, back.grid.Count);
            Brick saved = back.grid.GetBrickAt(new Vector3Int(1, 3, 0));
            Assert.IsNotNull(saved, "its bricks in their cells");
            Assert.AreEqual(topId, saved.id, "the same brick");
            Assert.AreEqual(1, saved.color);
            Assert.AreEqual(2, back.parts.Count, "its parts, for the clients");
        }
    }
}
