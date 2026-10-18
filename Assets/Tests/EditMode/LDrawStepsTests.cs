using System.Collections.Generic;
using System.IO;
using Brickcraft.Bricks.LDraw;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>The building steps of LDraw models (LDrawSteps): a file's own, or made layer by layer.</summary>
    public class LDrawStepsTests
    {
        [SetUp]
        public void SetUp() {
            GameData.Load();
        }

        // a part line, unturned (LDU, Y down: a brick 2x4's origin is its top, 24 above its bottom)
        private static string part(string id, float x, float y, float z, int color = 4) {
            return "1 " + color + " " + x + " " + y + " " + z + " 1 0 0 0 1 0 0 0 1 " + id + ".dat";
        }

        // the bottom of each part of a step, in steps' order
        private static List<float> bottoms(LDrawSteps.Model model) {
            List<float> all = new List<float>();
            foreach (LDrawSteps.Step step in model.steps) {
                foreach (int i in step.parts) {
                    Bounds placed = LDrawModelBuilder.Encapsulate(null, LDrawSteps.PartBounds(model.parts[i].part).Value, model.parts[i].Matrix).Value;
                    all.Add(placed.max.y);
                }
            }
            return all;
        }

        private static void AssertEveryPartOnce(LDrawSteps.Model model) {
            List<int> seen = new List<int>();
            foreach (LDrawSteps.Step step in model.steps) {
                seen.AddRange(step.parts);
            }
            seen.Sort();
            Assert.AreEqual(model.parts.Count, seen.Count, "every part in a step");
            for (int i = 0; i < seen.Count; i++) {
                Assert.AreEqual(i, seen[i], "each once");
            }
        }

        [Test]
        public void WithoutStepsTheyGoLayerByLayerFromTheBottom() {
            // a wall of 12 bricks 2x4 two rows high, its top row first in the file, and a plate on it
            List<string> lines = new List<string>() { "0 wall" };
            lines.Add(part("3020", 0, -56, 0));
            for (int x = 0; x < 6; x++) {
                lines.Add(part("3001", x * 80, -24, 0));
            }
            for (int x = 0; x < 6; x++) {
                lines.Add(part("3001", x * 80, 0, 0));
            }
            LDrawSteps steps = LDrawSteps.Read(lines.ToArray(), maxParts: 4);
            LDrawSteps.Model wall = steps.Main;

            Assert.IsTrue(wall.madeSteps);
            AssertEveryPartOnce(wall);
            List<float> heights = bottoms(wall);
            for (int i = 1; i < heights.Count; i++) {
                Assert.LessOrEqual(heights[i], heights[i - 1] + 0.01f, "nothing before what it stands on (Y is down)");
            }
            foreach (LDrawSteps.Step step in wall.steps) {
                Assert.LessOrEqual(step.parts.Count, 4, "at most so many a step");
            }
            // a row of 6 touching bricks is cut in pieces along it: 4 and 2, twice, then the plate
            Assert.AreEqual(5, wall.steps.Count);
            Assert.AreEqual(new[] { 7, 8, 9, 10 }, wall.steps[0].parts.ToArray(), "the bottom row's first four, along it");
            Assert.AreEqual(new[] { 0 }, wall.steps[4].parts.ToArray(), "the plate last");
        }

        [Test]
        public void AFilesOwnStepsAreKept() {
            string[] lines = {
                "0 two steps",
                part("3001", 0, 0, 0),
                part("3001", 40, 0, 0),
                "0 STEP",
                part("3001", 0, -24, 0),
                "0 ROTSTEP 30 45 0 ABS",
            };
            LDrawSteps.Model model = LDrawSteps.Read(lines).Main;
            Assert.IsFalse(model.madeSteps);
            Assert.AreEqual(2, model.steps.Count);
            Assert.AreEqual(new[] { 0, 1 }, model.steps[0].parts.ToArray());
            Assert.IsNull(model.steps[0].rotation);
            Assert.AreEqual(new[] { 2 }, model.steps[1].parts.ToArray());
            Assert.AreEqual(new Vector3(30, 45, 0), model.steps[1].rotation, "seen turned so");
        }

        [Test]
        public void WhatHangsInASlotComesAfterItsLayer() {
            // a chest and its lid (an attachment), the lid first in the file
            Item chest = Server.items["chest"], lid = Server.items["chest_lid"];
            Assume.That(lid.brickModel.IsAttachment, "the lid hangs in the chest's slot");
            string[] lines = { "0 chest", part(lid.LDrawPart, 0, -24, 10), part(chest.LDrawPart, 0, 0, 0), part("3001", 80, 0, 0) };
            LDrawSteps.Model model = LDrawSteps.Read(lines).Main;
            AssertEveryPartOnce(model);
            int lidStep = model.steps.FindIndex(s => s.parts.Contains(0)), chestStep = model.steps.FindIndex(s => s.parts.Contains(1));
            Assert.Greater(lidStep, chestStep, "the lid once the chest is there");
        }

        [Test]
        public void SubmodelsHaveStepsOfTheirOwnBeforeWhatUsesThem() {
            // the export tutorial's house: its bed is a submodel
            string[] lines = File.ReadAllLines(Path.Combine(Application.streamingAssetsPath, "Tutorials", "house.ldr"));
            LDrawSteps steps = LDrawSteps.Read(lines);
            Assert.AreEqual(2, steps.models.Count, "the bed, then the house");
            Assert.IsTrue(steps.models[0].isSubmodel);
            Assert.AreEqual("bed.ldr", steps.models[0].name);
            Assert.IsFalse(steps.Main.isSubmodel);
            AssertEveryPartOnce(steps.models[0]);
            AssertEveryPartOnce(steps.Main);
            Assert.AreEqual(1, steps.Main.parts.FindAll(p => steps.SubmodelOf(p) == steps.models[0]).Count, "the house uses it once, whole");
        }

        [Test]
        public void ExportedModelsHaveTheirSteps() {
            // a wall written like the parts lister does: its steps are in the file, as they'd be made
            string file = Path.Combine(Path.GetTempPath(), "brickcraft-steps-" + System.Guid.NewGuid() + ".ldr");
            try {
                List<LDrawPlacement> placements = new List<LDrawPlacement>();
                for (int x = 0; x < 3; x++) {
                    for (int row = 0; row < 3; row++) {
                        placements.Add(new LDrawPlacement() {
                            part = "3001", color = 4, position = new Vector3(x * 80, -row * 24, 0),
                            row0 = Vector3.right, row1 = Vector3.up, row2 = Vector3.forward,
                        });
                    }
                }
                LDrawWriter.Write(file, "wall", placements);
                string[] lines = File.ReadAllLines(file);
                Assert.Contains("0 STEP", lines);
                LDrawSteps.Model read = LDrawSteps.Read(lines).Main;
                Assert.IsFalse(read.madeSteps, "the file's own");
                AssertEveryPartOnce(read);
                Assert.AreEqual(3, read.steps.Count, "a row a step");
                List<float> heights = bottoms(read);
                for (int i = 1; i < heights.Count; i++) {
                    Assert.LessOrEqual(heights[i], heights[i - 1] + 0.01f, "bottom up");
                }
            } finally {
                File.Delete(file);
            }
        }
    }
}
