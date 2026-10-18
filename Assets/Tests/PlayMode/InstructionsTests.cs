using System.Collections;
using System.Collections.Generic;
using System.IO;
using Brickcraft.Bricks.LDraw;
using Brickcraft.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Brickcraft.Tests
{
    /// <summary>Building instructions (UI.InstructionBook): a model's pages, made from its steps, and shown in game.</summary>
    [Timeout(180000)]
    public class InstructionsTests
    {
        private TestGame game;
        private string folder;

        [UnitySetUp]
        public IEnumerator SetUp() {
            game = new TestGame();
            yield return game.Start();
            folder = Path.Combine(Path.GetTempPath(), "brickcraft-instructions-" + System.Guid.NewGuid());
            Directory.CreateDirectory(folder);
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            if (Directory.Exists(folder)) {
                Directory.Delete(folder, true);
            }
            yield return game.Delete();
        }

        // a part line, unturned (LDU, Y down)
        private static string part(string id, float x, float y, float z, int color) {
            return "1 " + color + " " + x + " " + y + " " + z + " 1 0 0 0 1 0 0 0 1 " + id + ".dat";
        }

        // how much of a page isn't white
        private static float inked(Texture2D page) {
            Color32[] pixels = page.GetPixels32();
            int ink = 0;
            foreach (Color32 p in pixels) {
                if (p.r < 230 || p.g < 230 || p.b < 230) {
                    ink++;
                }
            }
            return ink / (float)pixels.Length;
        }

        [UnityTest]
        public IEnumerator AModelsInstructionsHaveACoverAPageAStepAndItsParts() {
            // a wall two rows high: 4 red bricks 2x4, then 4 blue ones
            List<string> lines = new List<string>() { "0 wall", "0 Name: wall.ldr" };
            for (int x = 0; x < 4; x++) {
                lines.Add(part("3001", x * 80, 0, 0, 4));
                lines.Add(part("3001", x * 80, -24, 0, 1));
            }
            string model = Path.Combine(folder, "wall.ldr");
            File.WriteAllLines(model, lines);
            int steps = LDrawSteps.Read(lines.ToArray()).Main.steps.Count;
            Assert.AreEqual(2, steps, "a row a step");

            string made = null;
            float lastProgress = 0;
            yield return InstructionBook.Make(model, "Wall", progress => lastProgress = progress, done => made = done);
            Assert.AreEqual(InstructionBook.FolderOf(model), made, "made, next to the model");
            Assert.AreEqual(1, lastProgress, 0.001f, "all the way");

            List<string> pages = InstructionBook.Pages(made);
            Assert.AreEqual(1 + steps + 1, pages.Count, "the cover, a page a step, the parts list");
            foreach (string path in pages) {
                Texture2D page = new Texture2D(2, 2);
                Assert.IsTrue(page.LoadImage(File.ReadAllBytes(path)), path);
                Assert.AreEqual(InstructionPage.Width, page.width);
                Assert.AreEqual(InstructionPage.Height, page.height);
                Assert.Greater(inked(page), 0.005f, Path.GetFileName(path) + " shows something");
                Object.Destroy(page);
            }

            // shown in game, from its first page
            InstructionsPanel.Open("Wall", made);
            Assert.IsTrue(InstructionsPanel.IsOpen);
            InstructionsPanel shown = Object.FindObjectOfType<InstructionsPanel>();
            StringAssert.Contains(pages.Count.ToString(), shown.subtitle.text, "which page of how many");
            Assert.IsFalse(shown.previousButton.interactable, "the first");
            shown.nextButton.onClick.Invoke();
            Assert.IsTrue(shown.previousButton.interactable, "the second");
            shown.Close();
            Assert.IsFalse(InstructionsPanel.IsOpen);
        }
    }
}
