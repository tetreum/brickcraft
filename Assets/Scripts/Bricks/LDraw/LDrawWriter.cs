using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Brickcraft.Bricks.LDraw
{
    /// <summary>A part where it goes in an LDraw model: its file, colour and transform (LDU, LDraw's axes: Y down).</summary>
    public struct LDrawPlacement
    {
        /// <summary>Its LDraw part number (without ".dat").</summary>
        public string part;
        /// <summary>Its colour, the palette's id (see BrickColor.ldrawId), BrickColor.None for none.</summary>
        public int color;
        public Vector3 position;
        /// <summary>Its rotation matrix's rows.</summary>
        public Vector3 row0, row1, row2;
    }

    /// <summary>
    /// LDraw models of what players built (see Network.PartsList.Placements): where each part goes, in LDraw's
    /// units and axes, from where it is in the game, and the .ldr file (LeoCAD, Studio, LDView... open them).
    /// </summary>
    public static class LDrawWriter
    {
        // the game's space to LDraw's: Y flips, a stud is 20 LDU, a plate 8
        private static readonly Matrix4x4 Flip = Matrix4x4.Scale(new Vector3(1, -1, 1));
        private static readonly Vector3 ToLDU = new Vector3(20 / Server.studSize, 8 / Server.plateHeight, 20 / Server.studSize);

        /// <summary>
        /// Where a part goes in an LDraw model whose origin is the game's point origin: the model's LDraw origin
        /// (BrickModel.LDrawOrigin, in its own space, LDU) put where the brick is (position, rotation).
        /// </summary>
        public static LDrawPlacement Place(string part, int color, Vector3 position, Quaternion rotation, Vector3 ldrawOrigin, Vector3 origin) {
            Matrix4x4 r = Matrix4x4.Rotate(rotation);
            Matrix4x4 m = Flip * r * Flip; // the rotation seen with LDraw's axes
            // where it is by the grid's cells (a stud is 20 LDU, a plate 8: the game's plate isn't exactly 8/20 of its
            // stud), then its origin in its own units
            Vector3 at = Flip.MultiplyPoint3x4(Vector3.Scale(position - origin, ToLDU) + rotation * ldrawOrigin);
            return new LDrawPlacement() {
                part = part, color = color, position = at,
                row0 = m.GetRow(0), row1 = m.GetRow(1), row2 = m.GetRow(2),
            };
        }

        // LDraw parts' origins worked out so far (from their shape), by part number
        private static readonly System.Collections.Generic.Dictionary<string, Vector3?> origins = new System.Collections.Generic.Dictionary<string, Vector3?>();

        /// <summary>
        /// Where an LDraw part's origin is in a brick model of it (LDU, Y up, from its pivot): the model's own if it
        /// was made from the part; for other models of a part the library has (the game's OBJ and prefab ones), from
        /// the part's shape put at the bottom of the middle of the model's shape (a door's pivot is its hinge);
        /// otherwise the top of its middle (LDraw's bricks and plates have theirs there).
        /// </summary>
        public static Vector3 OriginOf(BrickModel model, string part, string shape = null) {
            if (model.ldrawOrigin.HasValue) {
                return model.LDrawOrigin;
            }
            // a part the library hasn't: placed like one shaped like it (see Item.ldrawShape)
            if (!LDrawLibrary.HasPart(part) && !string.IsNullOrEmpty(shape)) {
                part = shape;
            }
            if (!origins.TryGetValue(part, out Vector3? origin)) {
                origin = null;
                if (LDrawLibrary.HasPart(part)) {
                    Mesh mesh = LDrawModelBuilder.Build(part, out string[] names);
                    if (mesh != null && mesh.vertexCount > 0) {
                        Bounds bounds = mesh.bounds;
                        origin = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) / LDrawModelBuilder.Scale;
                    }
                    Object.DestroyImmediate(mesh); // outside play mode too (tests)
                }
                origins[part] = origin;
            }
            // bricks on the grid have their pivot there already; attachments' is their hinge
            Vector3 pivotToShape = model.IsAttachment ? bottomMiddleOf(model) / LDrawModelBuilder.Scale : Vector3.zero;
            return origin.HasValue ? pivotToShape + origin.Value : model.LDrawOrigin;
        }

        // the bottom of the middle of a model's shape, from its pivot (game units): every mesh of its prefab
        private static Vector3 bottomMiddleOf(BrickModel model) {
            if (model.prefab == null) {
                return Vector3.zero;
            }
            Transform root = model.prefab.transform;
            bool any = false;
            Bounds bounds = new Bounds();
            foreach (MeshFilter filter in model.prefab.GetComponentsInChildren<MeshFilter>(true)) {
                if (filter.sharedMesh == null) {
                    continue;
                }
                Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Bounds local = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++) {
                    Vector3 point = toRoot.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (!any) {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    } else {
                        bounds.Encapsulate(point);
                    }
                }
            }
            return any ? new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) : Vector3.zero;
        }

        /// <summary>Writes the model (an .ldr file) named so; returns how many parts had a colour LDraw doesn't have.</summary>
        public static int Write(string path, string name, IList<LDrawPlacement> placements) {
            StringBuilder ldr = new StringBuilder();
            ldr.Append("0 ").Append(clean(name)).Append('\n');
            ldr.Append("0 Name: ").Append(Path.GetFileName(path)).Append('\n');
            ldr.Append("0 Author: Brickcraft\n");
            ldr.Append("0 BFC CERTIFY CCW\n\n");
            int colorless = 0;
            foreach (LDrawPlacement placed in placements) {
                BrickColor color = BrickColorPalette.Get(placed.color);
                int code = color != null ? color.ldrawId : BrickColor.None;
                if (code == BrickColor.None) {
                    code = LDrawModelBuilder.MainColor;
                    if (placed.color != BrickColor.None) {
                        colorless++;
                    }
                }
                ldr.Append("1 ").Append(code);
                number(ldr, placed.position);
                number(ldr, placed.row0);
                number(ldr, placed.row1);
                number(ldr, placed.row2);
                ldr.Append(' ').Append(placed.part).Append(".dat\n");
            }
            File.WriteAllText(path, ldr.ToString(), new UTF8Encoding(false));
            return colorless;
        }

        // " x y z" to a hundredth (positions far in the world carry float errors)
        private static void number(StringBuilder ldr, Vector3 values) {
            for (int i = 0; i < 3; i++) {
                float rounded = Mathf.Round(values[i] * 100) / 100;
                if (Mathf.Abs(rounded) < 0.005f) {
                    rounded = 0;
                }
                ldr.Append(' ').Append(rounded.ToString("0.##", CultureInfo.InvariantCulture));
            }
        }

        private static string clean(string name) {
            return (name ?? "").Replace('\n', ' ').Replace('\r', ' ');
        }
    }
}
