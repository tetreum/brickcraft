using Brickcraft.World;
using Brickcraft.World.CustomGenerator;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>The /closest lookup from the seed: what it knows, and that the terrain it finds is there (see World.Closest).</summary>
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

            for (int rx = 0; rx < 3; rx++) {
                for (int rz = 0; rz < 3; rz++) {
                    Vector3Int from = new Vector3Int(rx * 300, 64, rz * 300);
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
    }
}