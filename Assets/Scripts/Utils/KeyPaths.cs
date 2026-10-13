using System.Collections.Generic;
using UnityEngine;

namespace Brickcraft
{
    /// <summary>
    /// The classic input's keys (KeyCode) as the Input System's control paths ("&lt;Keyboard&gt;/w", "&lt;Mouse&gt;/rightButton")
    /// and back: GameInput's defaults and the settings' keys are KeyCodes, its actions read paths.
    /// </summary>
    public static class KeyPaths
    {
        private static readonly Dictionary<KeyCode, string> paths = createPaths();
        private static readonly Dictionary<string, KeyCode> keys = createKeys();

        /// <summary>The control path of a key, null if the Input System has none (joystick buttons...).</summary>
        public static string PathOf(KeyCode key) {
            return paths.TryGetValue(key, out string path) ? path : null;
        }

        /// <summary>The key of a control path, None if it isn't one of PathOf's.</summary>
        public static KeyCode KeyOf(string path) {
            return path != null && keys.TryGetValue(path, out KeyCode key) ? key : KeyCode.None;
        }

        private static Dictionary<KeyCode, string> createPaths() {
            Dictionary<KeyCode, string> map = new Dictionary<KeyCode, string>();
            for (KeyCode key = KeyCode.A; key <= KeyCode.Z; key++) {
                map[key] = "<Keyboard>/" + (char)('a' + (key - KeyCode.A));
            }
            for (int digit = 0; digit <= 9; digit++) {
                map[KeyCode.Alpha0 + digit] = "<Keyboard>/" + digit;
                map[KeyCode.Keypad0 + digit] = "<Keyboard>/numpad" + digit;
            }
            for (int f = 1; f <= 15; f++) {
                map[KeyCode.F1 + f - 1] = "<Keyboard>/f" + f;
            }
            Add(map, KeyCode.Space, "<Keyboard>/space");
            Add(map, KeyCode.Return, "<Keyboard>/enter");
            Add(map, KeyCode.KeypadEnter, "<Keyboard>/numpadEnter");
            Add(map, KeyCode.Escape, "<Keyboard>/escape");
            Add(map, KeyCode.Tab, "<Keyboard>/tab");
            Add(map, KeyCode.Backspace, "<Keyboard>/backspace");
            Add(map, KeyCode.Delete, "<Keyboard>/delete");
            Add(map, KeyCode.Insert, "<Keyboard>/insert");
            Add(map, KeyCode.Home, "<Keyboard>/home");
            Add(map, KeyCode.End, "<Keyboard>/end");
            Add(map, KeyCode.PageUp, "<Keyboard>/pageUp");
            Add(map, KeyCode.PageDown, "<Keyboard>/pageDown");
            Add(map, KeyCode.UpArrow, "<Keyboard>/upArrow");
            Add(map, KeyCode.DownArrow, "<Keyboard>/downArrow");
            Add(map, KeyCode.LeftArrow, "<Keyboard>/leftArrow");
            Add(map, KeyCode.RightArrow, "<Keyboard>/rightArrow");
            Add(map, KeyCode.LeftShift, "<Keyboard>/leftShift");
            Add(map, KeyCode.RightShift, "<Keyboard>/rightShift");
            Add(map, KeyCode.LeftControl, "<Keyboard>/leftCtrl");
            Add(map, KeyCode.RightControl, "<Keyboard>/rightCtrl");
            Add(map, KeyCode.LeftAlt, "<Keyboard>/leftAlt");
            Add(map, KeyCode.RightAlt, "<Keyboard>/rightAlt");
            Add(map, KeyCode.LeftCommand, "<Keyboard>/leftMeta");
            Add(map, KeyCode.RightCommand, "<Keyboard>/rightMeta");
            Add(map, KeyCode.CapsLock, "<Keyboard>/capsLock");
            Add(map, KeyCode.Numlock, "<Keyboard>/numLock");
            Add(map, KeyCode.ScrollLock, "<Keyboard>/scrollLock");
            Add(map, KeyCode.Pause, "<Keyboard>/pause");
            Add(map, KeyCode.Print, "<Keyboard>/printScreen");
            Add(map, KeyCode.Minus, "<Keyboard>/minus");
            Add(map, KeyCode.Equals, "<Keyboard>/equals");
            Add(map, KeyCode.LeftBracket, "<Keyboard>/leftBracket");
            Add(map, KeyCode.RightBracket, "<Keyboard>/rightBracket");
            Add(map, KeyCode.Semicolon, "<Keyboard>/semicolon");
            Add(map, KeyCode.Quote, "<Keyboard>/quote");
            Add(map, KeyCode.BackQuote, "<Keyboard>/backquote");
            Add(map, KeyCode.Comma, "<Keyboard>/comma");
            Add(map, KeyCode.Period, "<Keyboard>/period");
            Add(map, KeyCode.Slash, "<Keyboard>/slash");
            Add(map, KeyCode.Backslash, "<Keyboard>/backslash");
            Add(map, KeyCode.KeypadPlus, "<Keyboard>/numpadPlus");
            Add(map, KeyCode.KeypadMinus, "<Keyboard>/numpadMinus");
            Add(map, KeyCode.KeypadMultiply, "<Keyboard>/numpadMultiply");
            Add(map, KeyCode.KeypadDivide, "<Keyboard>/numpadDivide");
            Add(map, KeyCode.KeypadPeriod, "<Keyboard>/numpadPeriod");
            Add(map, KeyCode.KeypadEquals, "<Keyboard>/numpadEquals");
            Add(map, KeyCode.Mouse0, "<Mouse>/leftButton");
            Add(map, KeyCode.Mouse1, "<Mouse>/rightButton");
            Add(map, KeyCode.Mouse2, "<Mouse>/middleButton");
            Add(map, KeyCode.Mouse3, "<Mouse>/backButton");
            Add(map, KeyCode.Mouse4, "<Mouse>/forwardButton");
            return map;
        }

        private static void Add(Dictionary<KeyCode, string> map, KeyCode key, string path) {
            map[key] = path;
        }

        private static Dictionary<string, KeyCode> createKeys() {
            Dictionary<string, KeyCode> map = new Dictionary<string, KeyCode>();
            foreach (KeyValuePair<KeyCode, string> entry in paths) {
                map[entry.Value] = entry.Key;
            }
            return map;
        }
    }
}
