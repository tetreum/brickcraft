using System;
using System.Collections.Generic;
using Brickcraft.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brickcraft
{
    /// <summary>
    /// The game's controls. Each action is a button of the Input Manager (Edit → Project Settings →
    /// Input Manager), so modders can rebind them, with a default key if the project doesn't define it.
    /// Players can remap them in the settings: their key is kept in PlayerPrefs and replaces the
    /// Input Manager's for that action.
    /// </summary>
    public static class GameInput
    {
        public const string MoveForward = "MoveForward";
        public const string MoveBack = "MoveBack";
        public const string MoveLeft = "MoveLeft";
        public const string MoveRight = "MoveRight";
        public const string Jump = "Jump";
        public const string Sprint = "Sprint";
        public const string Dig = "Dig";
        public const string Place = "Place";
        public const string Rotate = "Rotate";
        public const string Interact = "Interact";
        public const string FreePlacement = "FreePlacement";
        public const string Inventory = "Inventory";
        public const string Chat = "Chat";
        public const string PlayerList = "PlayerList";
        public const string Menu = "Menu";
        public const string SlotPrefix = "Slot"; // Slot1 to Slot9

        // Input Manager axes the movement uses while the player didn't remap it
        private const string HorizontalAxis = "Horizontal";
        private const string VerticalAxis = "Vertical";

        public sealed class Binding
        {
            /// <summary>Its Input Manager button.</summary>
            public string button;
            public string label;
            /// <summary>To group them in the settings.</summary>
            public string category;
            public KeyCode defaultKey;
            public KeyCode defaultAltKey;

            public Binding(string category, string button, string label, KeyCode defaultKey, KeyCode defaultAltKey = KeyCode.None) {
                this.category = category;
                this.button = button;
                this.label = label;
                this.defaultKey = defaultKey;
                this.defaultAltKey = defaultAltKey;
            }
        }

        private const string BindingPrefix = "binding.";

        // before Bindings: static fields are initialized in order, and creating Bindings fills it
        private static readonly Dictionary<string, Binding> bindingsByButton = new Dictionary<string, Binding>();

        /// <summary>Every action players can remap, in the order the settings show them.</summary>
        public static readonly Binding[] Bindings = createBindings();

        // buttons the project doesn't define, so asking for them doesn't throw every frame
        private static readonly HashSet<string> missing = new HashSet<string>();

        private static bool isTyping;
        private static int stoppedTypingFrame = -1;

        private static Binding[] createBindings() {
            List<Binding> bindings = new List<Binding>() {
                new Binding("Movement", MoveForward, "Move forward", KeyCode.W, KeyCode.UpArrow),
                new Binding("Movement", MoveBack, "Move back", KeyCode.S, KeyCode.DownArrow),
                new Binding("Movement", MoveLeft, "Move left", KeyCode.A, KeyCode.LeftArrow),
                new Binding("Movement", MoveRight, "Move right", KeyCode.D, KeyCode.RightArrow),
                new Binding("Movement", Jump, "Jump", KeyCode.Space),
                new Binding("Movement", Sprint, "Sprint", KeyCode.LeftShift),
                new Binding("Building", Dig, "Dig or remove", KeyCode.Mouse0),
                new Binding("Building", Place, "Place brick", KeyCode.Mouse1),
                new Binding("Building", Rotate, "Rotate brick", KeyCode.R),
                new Binding("Building", Interact, "Use brick", KeyCode.E),
                new Binding("Building", FreePlacement, "Place on any stud (hold)", KeyCode.LeftShift, KeyCode.RightShift),
                new Binding("Interface", Inventory, "Inventory", KeyCode.I),
                new Binding("Interface", Chat, "Chat", KeyCode.Return, KeyCode.KeypadEnter),
                new Binding("Interface", PlayerList, "Player list (hold)", KeyCode.Tab),
                new Binding("Interface", Menu, "Menu", KeyCode.Escape),
            };
            for (int i = 1; i <= 9; i++) {
                bindings.Add(new Binding("Hotbar", SlotPrefix + i, "Hotbar slot " + i, KeyCode.Alpha0 + i));
            }
            foreach (Binding binding in bindings) {
                bindingsByButton[binding.button] = binding;
            }
            return bindings.ToArray();
        }

        // -------- typing --------

        /// <summary>
        /// Keys are being typed as text (in the chat, a search field...), so they aren't shortcuts.
        /// Also true the frame typing stopped, so the key that stopped it (Escape) does nothing else.
        /// </summary>
        public static bool IsTyping {
            get { return isTyping || stoppedTypingFrame == Time.frameCount || isInputFieldFocused(); }
        }

        /// <summary>For the UI: it starts or stops using the keyboard (for text, or to pick a key).</summary>
        public static void SetTyping(bool typing) {
            if (isTyping && !typing) {
                stoppedTypingFrame = Time.frameCount;
            }
            isTyping = typing;
        }

        private static bool isInputFieldFocused() {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            InputField field = selected != null ? selected.GetComponent<InputField>() : null;
            return field != null && field.isFocused;
        }

        // -------- reading --------

        /// <summary>The button went down this frame.</summary>
        public static bool GetButtonDown(string button) {
            KeyCode? key = GetPlayerKey(button);
            if (key.HasValue) {
                return Input.GetKeyDown(key.Value);
            }
            if (!missing.Contains(button)) {
                try {
                    return Input.GetButtonDown(button);
                } catch (ArgumentException) {
                    warnMissing(button);
                }
            }
            Binding binding = bindingsByButton[button];
            return Input.GetKeyDown(binding.defaultKey) || (binding.defaultAltKey != KeyCode.None && Input.GetKeyDown(binding.defaultAltKey));
        }

        /// <summary>The button is held.</summary>
        public static bool GetButton(string button) {
            KeyCode? key = GetPlayerKey(button);
            if (key.HasValue) {
                return Input.GetKey(key.Value);
            }
            if (!missing.Contains(button)) {
                try {
                    return Input.GetButton(button);
                } catch (ArgumentException) {
                    warnMissing(button);
                }
            }
            Binding binding = bindingsByButton[button];
            return Input.GetKey(binding.defaultKey) || (binding.defaultAltKey != KeyCode.None && Input.GetKey(binding.defaultAltKey));
        }

        /// <summary>
        /// Movement from -1 to 1 on each axis (x: left/right, y: back/forward). The Input Manager's
        /// smoothed axes while the player didn't remap the movement, the keys as they are otherwise.
        /// </summary>
        public static Vector2 GetMovement() {
            bool remapped = GetPlayerKey(MoveForward).HasValue || GetPlayerKey(MoveBack).HasValue
                || GetPlayerKey(MoveLeft).HasValue || GetPlayerKey(MoveRight).HasValue;

            if (!remapped) {
                try {
                    return new Vector2(Input.GetAxis(HorizontalAxis), Input.GetAxis(VerticalAxis));
                } catch (ArgumentException) {
                    // no axes in the Input Manager, use the keys
                }
            }
            return new Vector2(
                (GetButton(MoveRight) ? 1 : 0) - (GetButton(MoveLeft) ? 1 : 0),
                (GetButton(MoveForward) ? 1 : 0) - (GetButton(MoveBack) ? 1 : 0));
        }

        /// <summary>The fast inventory slot (0 to 8) whose button went down this frame, -1 if none.</summary>
        public static int GetSlotDown() {
            for (int i = 0; i < 9; i++) {
                if (GetButtonDown(SlotPrefix + (i + 1))) {
                    return i;
                }
            }
            return -1;
        }

        // -------- remapping --------

        /// <summary>The key the player chose for the button, null if it uses the default.</summary>
        public static KeyCode? GetPlayerKey(string button) {
            string key = BindingPrefix + button;
            return PlayerPrefs.HasKey(key) ? (KeyCode)PlayerPrefs.GetInt(key) : (KeyCode?)null;
        }

        /// <summary>The keys the button uses now: the player's, or its defaults.</summary>
        public static KeyCode[] GetKeys(string button) {
            KeyCode? key = GetPlayerKey(button);
            if (key.HasValue) {
                return new[] { key.Value };
            }
            Binding binding = bindingsByButton[button];
            return binding.defaultAltKey == KeyCode.None ? new[] { binding.defaultKey } : new[] { binding.defaultKey, binding.defaultAltKey };
        }

        public static void Remap(string button, KeyCode key) {
            PlayerPrefs.SetInt(BindingPrefix + button, (int)key);
            PlayerPrefs.Save();
            EventManager.SettingChanged.Raise(new SettingChangedEvent() { setting = BindingPrefix + button });
        }

        /// <summary>Back to the default keys of every button.</summary>
        public static void ResetAll() {
            foreach (Binding binding in Bindings) {
                PlayerPrefs.DeleteKey(BindingPrefix + binding.button);
            }
            PlayerPrefs.Save();
            EventManager.SettingChanged.Raise(new SettingChangedEvent() { setting = BindingPrefix + "*" });
        }

        /// <summary>A key as players know it: "Left Click", "Left Shift", "1"...</summary>
        public static string KeyName(KeyCode key) {
            switch (key) {
                case KeyCode.Mouse0: return "Left Click";
                case KeyCode.Mouse1: return "Right Click";
                case KeyCode.Mouse2: return "Middle Click";
                case KeyCode.Return: return "Enter";
                case KeyCode.KeypadEnter: return "Keypad Enter";
                case KeyCode.Escape: return "Esc";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
            }
            string name = key.ToString();
            if (name.StartsWith("Alpha")) {
                return name.Substring(5);
            }
            if (name.StartsWith("Mouse")) {
                return "Mouse " + name.Substring(5);
            }
            // "LeftShift" -> "Left Shift"
            System.Text.StringBuilder spaced = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++) {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) {
                    spaced.Append(' ');
                }
                spaced.Append(name[i]);
            }
            return spaced.ToString();
        }

        private static void warnMissing(string button) {
            missing.Add(button);
            Debug.LogWarning("The Input Manager has no \"" + button + "\" button, using its default key");
        }
    }
}
