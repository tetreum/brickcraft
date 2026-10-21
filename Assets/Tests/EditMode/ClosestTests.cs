using Brickcraft.Bricks;
using Brickcraft.World;
using Brickcraft.World.CustomGenerator;
using Brickcraft.World.Towns;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>The /closest lookup: what it knows, and that what it finds really is there (see World.Closest).</summary>
    public class ClosestTests
    {
        [SetUp]
        public void SetUp() {
            GameData.Load();
        }

        [Test]
        public void ItKnowsWhatItCanLookFor() {
            Assert.AreEqual("town", Closest.KindOf("Town"));
            Assert.AreEqual("town", Closest.KindOf(" village "));
            Assert.AreEqual("mountains", Closest.KindOf("mountain"));
            Assert.IsNull(Closest.KindOf("dragon"));
        }

        [Test]
        public void WhatItFindsIsWhereItSays() {
            CustomChunkGenerator gen = new CustomChunkGenerator();
            gen.Init(Network.WorldNetwork.Seed);
            Vector3Int from = new Vector3Int(0, 64, 0);

            for (int rx = 0; rx < 3; rx++) {
                for (int rz = 0; rz < 3; rz++) {
                    from = new Vector3Int(rx * 300, 64, rz * 300);
                    foreach (string kind in new[] { "river", "desert", "snow", "mountains" }) {
                        bool found = Closest.Find(kind, from, out Vector3Int at);
                        if (!found) {
                            continue; // none within range of this point
                        }
                        switch (kind) {
                            case "river":
                            case "lake":
                                Assert.IsTrue(gen.IsRiverAt(at.x, at.z), "a river at " + at);
                                break;
                            case "desert":
                                Assert.AreEqual(CustomChunkGenerator.BIOME_TYPE.DESERT, gen.BiomeAt(at.x, at.z), "a desert at " + at);
                                break;
                            case "snow":
                                Assert.AreEqual(CustomChunkGenerator.BIOME_TYPE.SNOW, gen.BiomeAt(at.x, at.z), "snow at " + at);
                                break;
                            case "mountains":
                                Assert.AreEqual(CustomChunkGenerator.BIOME_TYPE.MOUNTAINS, gen.BiomeAt(at.x, at.z), "mountains at " + at);
                                break;
                        }
                    }
                }
            }
        }

        [Test]
        public void ATownIsFoundAtATown() {
            bool found = Closest.Find("town", new Vector3Int(0, 64, 0), out Vector3Int at);
            Assume.That(found, "a town within range of the origin");

            int region = TownPlanner.SpanChunks * 16;
            int regionX = BrickGrid.FloorDiv(at.x, region);
            int regionZ = BrickGrid.FloorDiv(at.z, region);
            Assert.IsNotNull(TownPlanner.Plan(Network.WorldNetwork.Seed, TownBuildings.All, regionX, regionZ), "a town at " + at);
        }
    }
}