using System;
using System.Collections.Generic;
using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>A colour bricks can have, one of Rebrickable's colour list.</summary>
    [Serializable]
    public class BrickColor
    {
        /// <summary>No colour: items that aren't plastic bricks, and world blocks drawn with their own texture.</summary>
        public const int None = -1;

        /// <summary>Rebrickable's colour id.</summary>
        public int id;
        public string name;
        /// <summary>RRGGBB, without #.</summary>
        public string hex;
        public bool isTransparent;
        public Material material;

        public Color32 Color32 {
            get {
                Color color;
                ColorUtility.TryParseHtmlString("#" + hex, out color);
                return color;
            }
        }
    }

    /// <summary>
    /// Every colour bricks can have and its material (Assets/Materials/BrickColors/Palette/[id]_[Name].mat),
    /// by colour id. The list is this asset, Resources/BrickColorPalette: a colour is added there.
    ///
    /// Loaded on the main thread by <see cref="Load"/> (ItemDatabase does it before the first scene),
    /// lookups are then safe from any thread.
    /// </summary>
    public class BrickColorPalette : ScriptableObject
    {
        public const string ResourceName = "BrickColorPalette";

        public List<BrickColor> colors = new List<BrickColor>();

        private static Dictionary<int, BrickColor> byId = new Dictionary<int, BrickColor>();

        /// <summary>Every colour, by id.</summary>
        public static IEnumerable<BrickColor> All {
            get { return byId.Values; }
        }

        public static void Load() {
            BrickColorPalette palette = Resources.Load<BrickColorPalette>(ResourceName);
            Dictionary<int, BrickColor> loaded = new Dictionary<int, BrickColor>();

            if (palette == null) {
                Debug.LogError("Missing Resources/" + ResourceName + ", bricks have no colours");
            } else {
                foreach (BrickColor color in palette.colors) {
                    loaded[color.id] = color;
                }
            }
            byId = loaded;
        }

        /// <summary>The colour with that Rebrickable id, null if there's none.</summary>
        public static BrickColor Get(int id) {
            BrickColor found;
            return byId.TryGetValue(id, out found) ? found : null;
        }

        /// <summary>By id or name, ignoring case, spaces and dashes ("4", "red", "trans-clear", "TransClear").</summary>
        public static BrickColor Find(string idOrName) {
            int id;
            if (int.TryParse(idOrName, out id)) {
                return Get(id);
            }
            string key = nameKey(idOrName);
            foreach (BrickColor color in byId.Values) {
                if (nameKey(color.name) == key) {
                    return color;
                }
            }
            return null;
        }

        private static string nameKey(string name) {
            System.Text.StringBuilder key = new System.Text.StringBuilder();
            foreach (char c in name) {
                if (char.IsLetterOrDigit(c)) {
                    key.Append(char.ToLowerInvariant(c));
                }
            }
            return key.ToString();
        }
    }
}
