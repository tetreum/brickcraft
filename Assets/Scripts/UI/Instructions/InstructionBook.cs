using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Brickcraft.Bricks;
using Brickcraft.Bricks.Instructions;
using Brickcraft.Bricks.LDraw;
using UnityEngine;

namespace Brickcraft.UI
{
    /// <summary>
    /// The building instructions of an LDraw model as images (see InstructionPage, InstructionPage.Width x Height): the
    /// cover, a page a step (its submodels' first, see LDrawSteps) and the parts list, written as page_001.png... in an
    /// "instructions" folder next to the model. Its steps are rendered by StepRenderer, its pages laid out by the
    /// InstructionPage prefab in a scene of their own, both a frame at a time.
    /// </summary>
    public static class InstructionBook
    {
        public const string FolderName = "instructions";
        public const string PagePrefix = "page_";
        /// <summary>How many parts a page of the parts list has (its grid: 8 across, 4 down).</summary>
        public const int PartsPerListPage = 32;
        private const string PagePrefab = "UI/InstructionPage";

        /// <summary>Where a model's instructions go: next to it.</summary>
        public static string FolderOf(string modelPath) {
            return Path.Combine(Path.GetDirectoryName(modelPath), FolderName);
        }

        /// <summary>A folder's pages, in order (none if it has none).</summary>
        public static List<string> Pages(string folder) {
            List<string> pages = new List<string>();
            if (Directory.Exists(folder)) {
                pages.AddRange(Directory.GetFiles(folder, PagePrefix + "*.png"));
                pages.Sort(StringComparer.OrdinalIgnoreCase);
            }
            return pages;
        }

        /// <summary>
        /// Makes a model's instructions (its .ldr, named so), a frame at a time; tells how far it got (0 to 1) and, once
        /// done, its folder (null if it couldn't).
        /// </summary>
        public static IEnumerator Make(string modelPath, string title, Action<float> progress, Action<string> done) {
            string[] lines;
            try {
                lines = File.ReadAllLines(modelPath);
            } catch (Exception e) {
                Debug.LogWarning("Can't read " + modelPath + ": " + e.Message);
                done?.Invoke(null);
                yield break;
            }
            LDrawSteps steps = LDrawSteps.Read(lines);
            int stepCount = 0;
            foreach (LDrawSteps.Model model in steps.models) {
                stepCount += model.steps.Count;
            }
            StepRenderer renderer = new StepRenderer(steps);
            // the steps take most of the time
            yield return renderer.Render(step => progress?.Invoke(0.8f * step.number / Mathf.Max(1, stepCount)));

            string folder = FolderOf(modelPath);
            Directory.CreateDirectory(folder);
            foreach (string old in Pages(folder)) {
                File.Delete(old);
            }

            // every part, each with how many (submodels' parts too), for the cover and the parts list
            List<StepPart> all = count(LDrawContraption.Parse(lines), renderer);
            int total = 0;
            foreach (StepPart part in all) {
                total += part.count;
            }

            Studio studio = new Studio("Instruction pages", InstructionPage.Width, InstructionPage.Height);
            GameObject pageObject = null;
            try {
                pageObject = UnityEngine.Object.Instantiate(Resources.Load<GameObject>(PagePrefab));
                studio.Add(pageObject);
                Canvas canvas = pageObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = studio.camera;
                canvas.planeDistance = 1;
                InstructionPage page = pageObject.GetComponent<InstructionPage>();

                int number = 0;
                int pageCount = 1 + renderer.rendered.Count + Mathf.Max(1, Mathf.CeilToInt(all.Count / (float)PartsPerListPage));
                page.ShowCover(title, renderer.Cover, Loc.Get(Loc.UI, "instructions.info", total, renderer.rendered.Count));
                write(folder, ++number, capture(studio));
                progress?.Invoke(0.8f + 0.2f * number / pageCount);
                yield return null;
                foreach (InstructionStep step in renderer.rendered) {
                    page.ShowStep(step, step.model.isSubmodel ? nameOf(step.model) : null);
                    write(folder, ++number, capture(studio));
                    progress?.Invoke(0.8f + 0.2f * number / pageCount);
                    yield return null;
                }
                for (int start = 0; start < Mathf.Max(1, all.Count); start += PartsPerListPage) {
                    page.ShowParts(Loc.Get(Loc.UI, "instructions.parts"), all.GetRange(start, Mathf.Min(PartsPerListPage, all.Count - start)));
                    write(folder, ++number, capture(studio));
                    progress?.Invoke(0.8f + 0.2f * number / pageCount);
                    yield return null;
                }
            } finally {
                if (pageObject != null) {
                    UnityEngine.Object.DestroyImmediate(pageObject);
                }
                studio.Dispose();
                renderer.Dispose(true);
            }
            done?.Invoke(folder);
        }

        // a submodel's name: its item's (the bed), else its file's
        private static string nameOf(LDrawSteps.Model model) {
            string key = LDrawSteps.Key(model.name);
            return Server.items.TryGetValue(key, out Item item) ? item.name : key;
        }

        // each part and colour once, how many, its icon; the most first
        private static List<StepPart> count(List<LDrawContraption.Part> parts, StepRenderer renderer) {
            Dictionary<string, StepPart> byKey = new Dictionary<string, StepPart>();
            List<StepPart> all = new List<StepPart>();
            foreach (LDrawContraption.Part part in parts) {
                string key = part.part + "/" + part.color;
                if (byKey.TryGetValue(key, out StepPart known)) {
                    known.count++;
                    continue;
                }
                StepPart listed = new StepPart() { part = part.part, color = part.color, count = 1, icon = renderer.IconOf(part.part, part.color) };
                byKey[key] = listed;
                all.Add(listed);
            }
            all.Sort((a, b) => b.count != a.count ? b.count.CompareTo(a.count) : string.CompareOrdinal(a.part, b.part));
            return all;
        }

        // the page as laid out, on white
        private static Texture2D capture(Studio studio) {
            Canvas.ForceUpdateCanvases();
            Color32[] pixels = studio.Capture(Color.white);
            Texture2D image = new Texture2D(studio.Width, studio.Height, TextureFormat.RGB24, false);
            image.SetPixels32(pixels);
            image.Apply();
            return image;
        }

        private static void write(string folder, int number, Texture2D page) {
            File.WriteAllBytes(Path.Combine(folder, PagePrefix + number.ToString("000") + ".png"), page.EncodeToPNG());
            UnityEngine.Object.Destroy(page);
        }
    }
}
