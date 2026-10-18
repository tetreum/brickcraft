using System;
using System.Collections;
using System.Collections.Generic;
using Brickcraft.Bricks.LDraw;
using UnityEngine;
using UnityEngine.Rendering;

namespace Brickcraft.Bricks.Instructions
{
    /// <summary>
    /// Renders a model's building steps (see LDrawSteps), a frame each: each model (its submodels first) is built in a
    /// scene of its own, every part where it goes in its colour with its LDraw edges, and seen from a fixed angle
    /// without perspective, at the same zoom for all its steps; each step shows what's done so far, its new parts
    /// outlined, and lists what it adds (each part and colour once, with how many, and its icon). Each model is also
    /// rendered finished, without outlines (a submodel's picture where it's used; the whole model's, the cover). The
    /// studio's lights and capture are the icons' (see ModelIconRenderer).
    /// </summary>
    public class StepRenderer
    {
        public const int Width = 1024;
        public const int Height = 768;
        public const int IconPixels = 128;
        // rendered bigger and scaled down, for smooth edges
        private const int Supersampling = 2;
        // seen from the front left, above
        private static readonly Quaternion View = Quaternion.Euler(30, 45, 0);
        // the room around the model, of its size
        private const float Margin = 0.06f;
        private static readonly Color32 OutlineColor = new Color32(255, 196, 0, 255);
        private const int OutlinePixels = 3;

        private readonly LDrawSteps steps;
        private readonly Material edgeMaterial, newMaterial, oldMaterial;
        // what's been made: parts' edges and icons (by part and colour)
        private readonly Dictionary<string, Mesh> edgeMeshes = new Dictionary<string, Mesh>();
        private readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

        /// <summary>The steps rendered so far, every model's in order.</summary>
        public readonly List<InstructionStep> rendered = new List<InstructionStep>();
        /// <summary>Each model rendered finished, without outlines.</summary>
        public readonly Dictionary<LDrawSteps.Model, Texture2D> finished = new Dictionary<LDrawSteps.Model, Texture2D>();

        public StepRenderer(LDrawSteps steps) {
            this.steps = steps;
            edgeMaterial = Resources.Load<Material>("Materials/InstructionEdge");
            newMaterial = Resources.Load<Material>("Materials/InstructionMaskNew");
            oldMaterial = Resources.Load<Material>("Materials/InstructionMaskOld");
        }

        /// <summary>The cover: the whole model, finished.</summary>
        public Texture2D Cover {
            get { return finished.TryGetValue(steps.Main, out Texture2D cover) ? cover : null; }
        }

        /// <summary>Renders every step, a frame each; each one done is told.</summary>
        public IEnumerator Render(Action<InstructionStep> done = null) {
            foreach (LDrawSteps.Model model in steps.models) {
                IEnumerator rendering = renderModel(model, done);
                while (rendering.MoveNext()) {
                    yield return rendering.Current;
                }
            }
        }

        /// <summary>Lets go of what it made (its meshes, and with forgetImages its images).</summary>
        public void Dispose(bool forgetImages) {
            foreach (Mesh mesh in edgeMeshes.Values) {
                UnityEngine.Object.Destroy(mesh);
            }
            edgeMeshes.Clear();
            if (forgetImages) {
                foreach (InstructionStep step in rendered) {
                    UnityEngine.Object.Destroy(step.image);
                }
                foreach (Texture2D image in finished.Values) {
                    UnityEngine.Object.Destroy(image);
                }
                foreach (Texture2D icon in icons.Values) {
                    UnityEngine.Object.Destroy(icon);
                }
                rendered.Clear();
                finished.Clear();
                icons.Clear();
            }
        }

        private IEnumerator renderModel(LDrawSteps.Model model, Action<InstructionStep> done) {
            Studio studio = new Studio("Instructions " + model.name, Width * Supersampling, Height * Supersampling);
            GameObject root = null;
            try {
                root = new GameObject("Model");
                studio.Add(root);

                // every part, hidden until its step
                List<GameObject>[] parts = new List<GameObject>[model.parts.Count];
                for (int i = 0; i < parts.Length; i++) {
                    parts[i] = new List<GameObject>();
                    build(model.parts[i], Matrix4x4.identity, LDrawModelBuilder.MainColor, root.transform, parts[i], 0);
                }
                studio.Add(root);
                frame(studio.camera, root);
                foreach (List<GameObject> part in parts) {
                    foreach (GameObject leaf in part) {
                        leaf.SetActive(false);
                    }
                }

                for (int s = 0; s < model.steps.Count; s++) {
                    LDrawSteps.Step step = model.steps[s];
                    List<GameObject> added = new List<GameObject>();
                    foreach (int i in step.parts) {
                        foreach (GameObject leaf in parts[i]) {
                            leaf.SetActive(true);
                            added.Add(leaf);
                        }
                    }
                    InstructionStep instruction = new InstructionStep() {
                        number = rendered.Count + 1, model = model, index = s,
                        image = capture(studio, root, added),
                    };
                    // its parts' icons are rendered where the model is: it's out of their way meanwhile
                    root.SetActive(false);
                    list(model, step, instruction);
                    root.SetActive(true);
                    rendered.Add(instruction);
                    done?.Invoke(instruction);
                    yield return null;
                }
                finished[model] = capture(studio, root, null);
            } finally {
                // gone at once: the scene goes later, and the next model's camera would see it meanwhile
                if (root != null) {
                    UnityEngine.Object.DestroyImmediate(root);
                }
                studio.Dispose();
            }
        }

        // -------- the model --------

        // a part of a model where it goes (matrix: where its model is; colour: the one 16 is), each of its own parts
        // added to leaves: a submodel's, or the part itself
        private void build(LDrawContraption.Part part, Matrix4x4 matrix, int color, Transform root, List<GameObject> leaves, int depth) {
            Matrix4x4 placed = matrix * part.Matrix;
            int partColor = part.color == LDrawModelBuilder.MainColor ? color : part.color;
            LDrawSteps.Model submodel = steps.SubmodelOf(part);
            if (submodel != null) {
                if (depth < 16) {
                    foreach (LDrawContraption.Part inner in submodel.parts) {
                        build(inner, placed, partColor, root, leaves, depth + 1);
                    }
                }
                return;
            }
            GameObject leaf = new GameObject(part.part);
            leaf.transform.SetParent(root, false);
            place(leaf.transform, placed);
            GameObject body = makeBody(part.part, partColor);
            if (body != null) {
                body.name = "Body";
                body.transform.SetParent(leaf.transform, false);
                body.SetActive(true);
            }
            Mesh edges = edgesOf(part.part);
            if (edges != null) {
                GameObject lines = new GameObject("Edges", typeof(MeshFilter), typeof(MeshRenderer));
                lines.transform.SetParent(leaf.transform, false);
                lines.GetComponent<MeshFilter>().sharedMesh = edges;
                MeshRenderer renderer = lines.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = edgeMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            leaves.Add(leaf);
        }

        // where an LDraw matrix puts a part's origin: LDU with Y down to the game's units with Y up (mirrored: X turned over)
        private static void place(Transform transform, Matrix4x4 ldraw) {
            Matrix4x4 flip = Matrix4x4.Scale(new Vector3(1, -1, 1));
            Matrix4x4 game = flip * ldraw * flip;
            Vector3 position = (Vector3)game.GetColumn(3) * LDrawModelBuilder.Scale;
            game.SetColumn(3, new Vector4(0, 0, 0, 1));
            bool mirrored = game.determinant < 0;
            if (mirrored) {
                game = game * Matrix4x4.Scale(new Vector3(-1, 1, 1));
            }
            transform.localPosition = position;
            transform.localRotation = game.rotation;
            transform.localScale = mirrored ? new Vector3(-1, 1, 1) : Vector3.one;
        }

        // a part's game model in that LDraw colour, its LDraw origin where its transform is; null if the game has none
        private GameObject makeBody(string part, int color) {
            Item item = LDrawSteps.ItemOf(part);
            BrickModel model = item != null && item.brickModel != null ? item.brickModel : BrickModels.Get(part);
            if (model == null || model.prefab == null) {
                return null;
            }
            GameObject holder = new GameObject(part);
            holder.SetActive(false);
            GameObject body = UnityEngine.Object.Instantiate(model.prefab, holder.transform, false);
            body.SetActive(true);
            // its LDraw origin (LDU, Y up from its pivot) at the holder's
            Vector3 origin = LDrawWriter.OriginOf(model, item != null ? item.LDrawPart : part, item != null ? item.ldrawShape : null) * LDrawModelBuilder.Scale;
            body.transform.localRotation = model.prefab.transform.localRotation;
            body.transform.localPosition = -(body.transform.localRotation * origin);
            // just its look: no colliders or behaviours (doors, slots) in a picture
            foreach (Collider collider in body.GetComponentsInChildren<Collider>(true)) {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            foreach (MonoBehaviour behaviour in body.GetComponentsInChildren<MonoBehaviour>(true)) {
                if (!(behaviour is BrickModelParts)) {
                    UnityEngine.Object.DestroyImmediate(behaviour);
                }
            }
            BrickModels.ApplyColor(body, materialFor(item, color));
            return holder;
        }

        private static Material materialFor(Item item, int ldrawColor) {
            BrickColor color = BrickColorPalette.FromLDraw(ldrawColor);
            if (item != null) {
                return item.MaterialFor(color != null ? color.id : item.color);
            }
            return color != null ? color.material : null;
        }

        private Mesh edgesOf(string part) {
            if (!edgeMeshes.TryGetValue(part, out Mesh mesh)) {
                mesh = LDrawModelBuilder.BuildEdges(part);
                edgeMeshes[part] = mesh;
            }
            return mesh;
        }

        // the camera fitting the whole model (all its parts shown), seen from the view's angle without perspective
        private static void frame(Camera camera, GameObject root) {
            Bounds bounds = new Bounds(root.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>()) {
                if (!any) {
                    bounds = renderer.bounds;
                    any = true;
                } else {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            camera.orthographic = true;
            camera.transform.rotation = View;
            float depth = bounds.size.magnitude + 1;
            camera.transform.position = bounds.center - camera.transform.forward * depth;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = depth * 2 + 1;
            // its box's corners seen from the camera: their extent across and up
            Vector2 half = Vector2.zero;
            for (int corner = 0; corner < 8; corner++) {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                Vector3 seen = camera.transform.InverseTransformPoint(point);
                half = Vector2.Max(half, new Vector2(Mathf.Abs(seen.x), Mathf.Abs(seen.y)));
            }
            float aspect = (float)Width / Height;
            camera.orthographicSize = Mathf.Max(half.y, half.x / aspect, 0.01f) * (1 + Margin);
        }

        // -------- pictures --------

        // the model as it is, transparent around it; the parts added (if any) outlined
        private Texture2D capture(Studio studio, GameObject root, List<GameObject> added) {
            Color32[] overBlack = studio.Capture(Color.black);
            Color32[] overWhite = studio.Capture(Color.white);
            Texture2D image = ModelIconRenderer.Combine(overBlack, overWhite, Width, Height, Supersampling);
            if (added != null && added.Count > 0) {
                outline(image, mask(studio, root, added));
            }
            return image;
        }

        // where the parts added show (behind the rest, they don't): them white, the rest black, no edges
        private bool[] mask(Studio studio, GameObject root, List<GameObject> added) {
            HashSet<Renderer> isNew = new HashSet<Renderer>();
            foreach (GameObject leaf in added) {
                isNew.UnionWith(leaf.GetComponentsInChildren<Renderer>());
            }
            Dictionary<Renderer, Material[]> saved = new Dictionary<Renderer, Material[]>();
            List<Renderer> hidden = new List<Renderer>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>()) {
                if (renderer.gameObject.name == "Edges") {
                    if (renderer.enabled) {
                        renderer.enabled = false;
                        hidden.Add(renderer);
                    }
                    continue;
                }
                saved[renderer] = renderer.sharedMaterials;
                Material[] flat = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < flat.Length; i++) {
                    flat[i] = isNew.Contains(renderer) ? newMaterial : oldMaterial;
                }
                renderer.sharedMaterials = flat;
            }
            Color32[] pixels;
            try {
                pixels = studio.Capture(Color.black);
            } finally {
                foreach (KeyValuePair<Renderer, Material[]> was in saved) {
                    was.Key.sharedMaterials = was.Value;
                }
                foreach (Renderer renderer in hidden) {
                    renderer.enabled = true;
                }
            }
            // a pixel is new if any of its square is
            bool[] shows = new bool[Width * Height];
            int fullWidth = Width * Supersampling;
            for (int y = 0; y < Height * Supersampling; y++) {
                for (int x = 0; x < fullWidth; x++) {
                    if (pixels[y * fullWidth + x].r > 127) {
                        shows[(y / Supersampling) * Width + x / Supersampling] = true;
                    }
                }
            }
            return shows;
        }

        // a band around the parts added (where they show), over the picture
        private static void outline(Texture2D image, bool[] shows) {
            bool[] around = grow(shows, Width, Height, OutlinePixels);
            Color32[] pixels = image.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) {
                if (around[i] && !shows[i]) {
                    pixels[i] = OutlineColor;
                }
            }
            image.SetPixels32(pixels);
            image.Apply();
        }

        // what's within so many pixels (across and up) of what's set
        private static bool[] grow(bool[] set, int width, int height, int by) {
            bool[] across = new bool[set.Length];
            for (int y = 0; y < height; y++) {
                int last = -by - 1;
                for (int x = 0; x < width; x++) {
                    if (set[y * width + x]) {
                        last = x;
                    }
                    across[y * width + x] = x - last <= by;
                }
                last = width + by + 1;
                for (int x = width - 1; x >= 0; x--) {
                    if (set[y * width + x]) {
                        last = x;
                    }
                    across[y * width + x] |= last - x <= by;
                }
            }
            bool[] grown = new bool[set.Length];
            for (int x = 0; x < width; x++) {
                int last = -by - 1;
                for (int y = 0; y < height; y++) {
                    if (across[y * width + x]) {
                        last = y;
                    }
                    grown[y * width + x] = y - last <= by;
                }
                last = height + by + 1;
                for (int y = height - 1; y >= 0; y--) {
                    if (across[y * width + x]) {
                        last = y;
                    }
                    grown[y * width + x] |= last - y <= by;
                }
            }
            return grown;
        }

        // -------- a step's parts --------

        // what a step adds, each part and colour once (its count and icon); a submodel, its finished picture
        private void list(LDrawSteps.Model model, LDrawSteps.Step step, InstructionStep instruction) {
            Dictionary<string, StepPart> byKey = new Dictionary<string, StepPart>();
            foreach (int i in step.parts) {
                LDrawContraption.Part part = model.parts[i];
                LDrawSteps.Model submodel = steps.SubmodelOf(part);
                string key = part.part + "/" + part.color;
                if (byKey.TryGetValue(key, out StepPart known)) {
                    known.count++;
                    continue;
                }
                StepPart listed = new StepPart() { part = part.part, color = part.color, count = 1, isSubmodel = submodel != null };
                listed.icon = submodel != null ? (finished.TryGetValue(submodel, out Texture2D whole) ? whole : null) : IconOf(part.part, part.color);
                byKey[key] = listed;
                instruction.parts.Add(listed);
            }
        }

        /// <summary>A part's icon in that LDraw colour (made once), null if the game has no model of it.</summary>
        public Texture2D IconOf(string part, int color) {
            string key = part + "/" + color;
            if (icons.TryGetValue(key, out Texture2D icon)) {
                return icon;
            }
            GameObject body = makeBody(part, color);
            if (body != null) {
                try {
                    icon = ModelIconRenderer.Render(body, IconPixels);
                } finally {
                    UnityEngine.Object.DestroyImmediate(body);
                }
            }
            icons[key] = icon;
            return icon;
        }
    }
}
