using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Brickcraft.Bricks.LDraw
{
    /// <summary>
    /// The catalogue's glass goes in its frames: "Glass for Window 1x6x5" is an attachment (see BrickAttachment)
    /// of the frames named like it, up to their size ("Window 1x6x5", "Window 1x6x5 with..."), which get a slot
    /// for it (see BrickSlot) where their LDraw origin is: LDraw draws glass to fit its frame at the same origin.
    /// Panes and shutters aren't frames; glass without frames is placed like any brick.
    /// </summary>
    public static class LDrawGlass
    {
        private const string GlassPrefix = "Glass for ";
        private const string FitsPrefix = "glass:";
        // a size like 1x6x5 (or 6x14x7.333)
        private static readonly Regex Size = new Regex(@"\b\d+(\.\d+)?x\d+(\.\d+)?(x\d+(\.\d+)?)?\b", RegexOptions.IgnoreCase);
        private static readonly Regex NotFrame = new Regex(@"\b(pane|panes|shutter)\b", RegexOptions.IgnoreCase);

        // what the catalogue's glass and frames fit, read the first time
        private static HashSet<string> glassKeys;
        private static HashSet<string> frameKeys;

        /// <summary>What a glass part of the catalogue fits in (see BrickAttachment.fits), null if it isn't one or has no frame.</summary>
        public static string GlassFits(LDrawPart part) {
            string key = glassKeyOf(part);
            return key != null && keys().frames.Contains(key) ? FitsPrefix + key : null;
        }

        /// <summary>The glass a frame of the catalogue takes (see BrickSlot.fits), null if it takes none.</summary>
        public static string FrameFits(LDrawPart part) {
            string key = frameKeyOf(part);
            return key != null && keys().glass.Contains(key) ? FitsPrefix + key : null;
        }

        /// <summary>Forgets what it read of the catalogue (a new one).</summary>
        public static void ClearCache() {
            glassKeys = null;
            frameKeys = null;
        }

        private static (HashSet<string> glass, HashSet<string> frames) keys() {
            if (glassKeys == null) {
                glassKeys = new HashSet<string>();
                frameKeys = new HashSet<string>();
                foreach (LDrawPart part in LDrawLibrary.Catalog) {
                    string glass = glassKeyOf(part);
                    if (glass != null) {
                        glassKeys.Add(glass);
                    }
                    string frame = frameKeyOf(part);
                    if (frame != null) {
                        frameKeys.Add(frame);
                    }
                }
            }
            return (glassKeys, frameKeys);
        }

        private static string glassKeyOf(LDrawPart part) {
            if (part == null || part.name == null || !part.name.StartsWith(GlassPrefix, System.StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            return keyOf(part.name.Substring(GlassPrefix.Length));
        }

        private static string frameKeyOf(LDrawPart part) {
            if (part == null || part.name == null || part.name.StartsWith(GlassPrefix, System.StringComparison.OrdinalIgnoreCase)
                    || NotFrame.IsMatch(part.name)) {
                return null;
            }
            return keyOf(part.name);
        }

        // the name up to its size, lowercase: "window 1x6x5"
        private static string keyOf(string name) {
            Match size = Size.Match(name);
            if (!size.Success) {
                return null;
            }
            return Regex.Replace(name.Substring(0, size.Index + size.Length).Trim().ToLowerInvariant(), @"\s+", " ");
        }
    }
}
