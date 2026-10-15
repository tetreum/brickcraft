using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// What's written on a sign (a brick of an item tagged "sign", see Item.IsSign; its text Brick.text): shown on its
    /// model's front (the biggest flat face of its mesh looking towards -Z, a little in front of it: a panel's flat side,
    /// not the framed one; placing a sign turns it towards the player, see BrickPlacer),
    /// centred, as big as it fits, dark on light
    /// signs and light on dark ones; only near enough to read it. Who places a sign writes it, then only they (and
    /// admins) can change it (see Network.PlayerNetwork.CmdSetSignText).
    /// </summary>
    public class SignText : MonoBehaviour
    {
        /// <summary>The most lines and characters a sign holds.</summary>
        public const int MaxLines = 6;
        public const int MaxLength = 120;
        /// <summary>How far away it's shown, metres.</summary>
        public const float ShowDistance = 32;

        // how much of the front's width and height the text takes, and how far in front of it, metres
        private const float Margin = 0.9f;
        private const float InFront = 0.01f;
        // the canvas' units in a metre: a font size in pixels a few centimetres tall
        private const float PixelsPerMetre = 200;
        private const float CheckInterval = 0.5f;

        private Canvas canvas;
        private Text label;
        private float nextCheck;

        /// <summary>The server asks this player to write a sign (the brick's id, what it says now): its editor opens.</summary>
        public static event System.Action<string, string> EditorRequested;

        public static void RequestEditor(string brickId, string text) {
            EditorRequested?.Invoke(brickId, text ?? "");
        }

        /// <summary>Whether that player (admin or not) can write on a sign placed by that one (0: no player placed it).</summary>
        public static bool CanWrite(int playerId, bool isAdmin, int placedBy) {
            return isAdmin || placedBy == 0 || placedBy == playerId;
        }

        /// <summary>A text as a sign takes it: its lines (at most MaxLines) trimmed, no control characters, at most MaxLength.</summary>
        public static string Clean(string text) {
            if (string.IsNullOrEmpty(text)) {
                return "";
            }
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder cleaned = new StringBuilder();
            for (int i = 0; i < lines.Length && i < MaxLines; i++) {
                StringBuilder line = new StringBuilder();
                foreach (char c in lines[i]) {
                    if (!char.IsControl(c)) {
                        line.Append(c);
                    }
                }
                if (i > 0) {
                    cleaned.Append('\n');
                }
                cleaned.Append(line.ToString().Trim());
            }
            string result = cleaned.ToString().TrimEnd('\n');
            return result.Length > MaxLength ? result.Substring(0, MaxLength).TrimEnd('\n') : result;
        }

        /// <summary>Writes a brick's text on its model (a sign's), nothing shown for none.</summary>
        public static void Show(Brick brick) {
            if (brick.gameObject == null) {
                return;
            }
            SignText sign = brick.gameObject.GetComponent<SignText>();
            if (string.IsNullOrEmpty(brick.text)) {
                if (sign != null) {
                    sign.label.text = "";
                    sign.canvas.enabled = false;
                }
                return;
            }
            if (sign == null) {
                sign = brick.gameObject.AddComponent<SignText>();
                sign.build(brick);
            }
            sign.label.text = brick.text;
            sign.label.color = textColorFor(brick.color);
            sign.nextCheck = 0;
        }

        // dark on light colours, light on dark ones
        private static Color textColorFor(int colorId) {
            BrickColor color = BrickColorPalette.Get(colorId);
            if (color == null) {
                return Color.black;
            }
            Color c = color.Color32;
            float light = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            return light > 0.5f ? new Color(0.08f, 0.08f, 0.08f) : new Color(0.95f, 0.95f, 0.95f);
        }

        /// <summary>
        /// Where a model is written on, in its own space: the biggest flat face of its mesh looking towards -Z (its
        /// width and height, its depth where it is); the front of its box if it has none.
        /// </summary>
        public static Bounds FrontOf(BrickModel model) {
            Bounds box = model != null ? BrickJoints.MeshBounds(model) : new Bounds(Vector3.up, Vector3.one);
            MeshFilter filter = model != null && model.prefab != null ? model.prefab.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) {
                return new Bounds(new Vector3(box.center.x, box.center.y, box.min.z), new Vector3(box.size.x, box.size.y, 0));
            }
            // the faces looking towards -Z, by their depth (to a millimetre): the one with the most area
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            System.Collections.Generic.Dictionary<int, float> areas = new System.Collections.Generic.Dictionary<int, float>();
            System.Collections.Generic.Dictionary<int, Bounds> extents = new System.Collections.Generic.Dictionary<int, Bounds>();
            for (int i = 0; i + 2 < triangles.Length; i += 3) {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                float area = normal.magnitude / 2;
                if (area <= 0 || Mathf.Abs(normal.z) < 0.99f * normal.magnitude) {
                    continue; // not flat across Z
                }
                // which way it looks: its normals' way (away from the box's middle without them)
                float looks = normals.Length == vertices.Length
                    ? normals[triangles[i]].z + normals[triangles[i + 1]].z + normals[triangles[i + 2]].z
                    : (a.z + b.z + c.z) / 3 - box.center.z;
                if (looks >= 0) {
                    continue;
                }
                int depth = Mathf.RoundToInt(a.z * 1000);
                areas.TryGetValue(depth, out float total);
                areas[depth] = total + area;
                Bounds face = extents.TryGetValue(depth, out Bounds known) ? known : new Bounds(a, Vector3.zero);
                face.Encapsulate(a);
                face.Encapsulate(b);
                face.Encapsulate(c);
                extents[depth] = face;
            }
            int best = 0;
            float most = 0;
            foreach (System.Collections.Generic.KeyValuePair<int, float> each in areas) {
                if (each.Value > most) {
                    most = each.Value;
                    best = each.Key;
                }
            }
            return most > 0 ? extents[best] : new Bounds(new Vector3(box.center.x, box.center.y, box.min.z), new Vector3(box.size.x, box.size.y, 0));
        }

        // a canvas on the front of its model
        private void build(Brick brick) {
            Bounds front = FrontOf(brick.model);
            GameObject holder = new GameObject("SignText", typeof(RectTransform), typeof(Canvas));
            holder.layer = (int)Game.Layers.IgnoreRaycast;
            holder.transform.SetParent(transform, false);
            // looking towards -Z, as a canvas does: read from in front of it
            holder.transform.localPosition = new Vector3(front.center.x, front.center.y, front.min.z - InFront);
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one / PixelsPerMetre;
            RectTransform rect = (RectTransform)holder.transform;
            rect.sizeDelta = new Vector2(front.size.x, front.size.y) * Margin * PixelsPerMetre;
            canvas = holder.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            GameObject text = new GameObject("Text", typeof(RectTransform), typeof(Text));
            text.transform.SetParent(holder.transform, false);
            RectTransform textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            label = text.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.supportRichText = false;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = 64;
            label.raycastTarget = false;
        }

        // shown only near enough to read it
        private void Update() {
            if (canvas == null || Time.time < nextCheck) {
                return;
            }
            nextCheck = Time.time + CheckInterval;
            Camera eye = Camera.main;
            canvas.enabled = !string.IsNullOrEmpty(label.text) && eye != null
                && (eye.transform.position - transform.position).sqrMagnitude < ShowDistance * ShowDistance;
        }
    }
}
