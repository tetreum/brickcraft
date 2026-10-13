using System;
using System.Collections.Generic;
using Brickcraft.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace Brickcraft
{
    /// <summary>
    /// The game's controls: each action has default keys (see Bindings), and players can remap them in the
    /// settings (their key is kept in PlayerPrefs, as an Input System control path, and replaces the defaults).
    /// They're read with the Input System: an InputAction for each button. While it has no devices (the editor
    /// before restarting with it on), the classic input is read instead, the keys directly and the movement with
    /// the Input Manager's axes.
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
        public const string Drop = "Drop";
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
            /// <summary>Its name, what the game asks for (see GameInput.GetButton).</summary>
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

        // a player's key for the button, a control path (see KeyPaths)
        private const string PathPrefix = "binding.path.";
        // the setting a remap changes (see EventManager.SettingChanged)
        private const string SettingPrefix = "binding.";

        // before Bindings: static fields are initialized in order, and creating Bindings fills it
        private static readonly Dictionary<string, Binding> bindingsByButton = new Dictionary<string, Binding>();

        /// <summary>Every action players can remap, in the order the settings show them.</summary>
        public static readonly Binding[] Bindings = createBindings();

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
                new Binding("Building", Drop, "Drop item (with Ctrl, the stack)", KeyCode.Q),
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

        // -------- actions --------

        // an action for each button, made the first time one is read
        private static Dictionary<string, InputAction> actions;

        /// <summary>The Input System has devices to read (it's on and the editor was restarted with it).</summary>
        public static bool UsesInputSystem {
            get { return Keyboard.current != null || Mouse.current != null; }
        }

        private static InputAction actionOf(string button) {
            if (actions == null) {
                actions = new Dictionary<string, InputAction>();
                foreach (Binding binding in Bindings) {
                    InputAction action = new InputAction(binding.button, InputActionType.Button);
                    actions[binding.button] = action;
                    bind(action, binding);
                    action.Enable();
                }
            }
            return actions[button];
        }

        // its keys: the player's, or its defaults
        private static void bind(InputAction action, Binding binding) {
            bool enabled = action.enabled;
            action.Disable();
            while (action.bindings.Count > 0) {
                action.ChangeBinding(0).Erase();
            }
            string player = playerPath(binding.button);
            if (player != null) {
                action.AddBinding(player);
            } else {
                foreach (KeyCode key in new[] { binding.defaultKey, binding.defaultAltKey }) {
                    string path = KeyPaths.PathOf(key);
                    if (path != null) {
                        action.AddBinding(path);
                    }
                }
            }
            if (enabled) {
                action.Enable();
            }
        }

        // -------- reading --------

        /// <summary>The button went down this frame.</summary>
        public static bool GetButtonDown(string button) {
            if (UsesInputSystem) {
                return actionOf(button).WasPressedThisFrame();
            }
            foreach (KeyCode key in GetKeys(button)) {
                if (Input.GetKeyDown(key)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The button is held.</summary>
        public static bool GetButton(string button) {
            if (UsesInputSystem) {
                return actionOf(button).IsPressed();
            }
            foreach (KeyCode key in GetKeys(button)) {
                if (Input.GetKey(key)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Movement from -1 to 1 on each axis (x: left/right, y: back/forward): the movement keys as they are. The classic
        /// input's smoothed Input Manager axes while the player didn't remap them and the Input System has no devices.
        /// </summary>
        public static Vector2 GetMovement() {
            bool remapped = GetPlayerKey(MoveForward).HasValue || GetPlayerKey(MoveBack).HasValue
                || GetPlayerKey(MoveLeft).HasValue || GetPlayerKey(MoveRight).HasValue;

            if (!remapped && !UsesInputSystem) {
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

        // -------- keys and the mouse --------
        // what isn't one of the remappable buttons: modifiers (Shift for any stud, Ctrl for the whole stack), Enter and
        // Escape in dialogs, the mouse's movement, pointer and wheel

        // the classic input's mouse axes are a tenth of the pixels moved (Input Manager's sensitivity)
        private const float MouseAxisScale = 0.1f;

        /// <summary>A key or mouse button (KeyCode.Mouse0...) is held.</summary>
        public static bool KeyHeld(KeyCode key) {
            if (UsesInputSystem) {
                ButtonControl control = controlOf(key);
                return control != null && control.isPressed;
            }
            return Input.GetKey(key);
        }

        /// <summary>A key or mouse button went down this frame.</summary>
        public static bool KeyDown(KeyCode key) {
            if (UsesInputSystem) {
                ButtonControl control = controlOf(key);
                return control != null && control.wasPressedThisFrame;
            }
            return Input.GetKeyDown(key);
        }

        /// <summary>A key or mouse button went up this frame.</summary>
        public static bool KeyUp(KeyCode key) {
            if (UsesInputSystem) {
                ButtonControl control = controlOf(key);
                return control != null && control.wasReleasedThisFrame;
            }
            return Input.GetKeyUp(key);
        }

        // the key's control on the keyboard or the mouse there is, null if there's none
        private static ButtonControl controlOf(KeyCode key) {
            string path = KeyPaths.PathOf(key);
            if (path == null) {
                return null;
            }
            // "<Keyboard>/q": its device and its control's name
            InputDevice device = path.StartsWith("<Mouse>") ? (InputDevice)Mouse.current : Keyboard.current;
            return device != null ? device.TryGetChildControl<ButtonControl>(path.Substring(path.IndexOf('/') + 1)) : null;
        }

        /// <summary>Either Shift is held.</summary>
        public static bool ShiftHeld {
            get { return KeyHeld(KeyCode.LeftShift) || KeyHeld(KeyCode.RightShift); }
        }

        /// <summary>Either Ctrl is held.</summary>
        public static bool CtrlHeld {
            get { return KeyHeld(KeyCode.LeftControl) || KeyHeld(KeyCode.RightControl); }
        }

        /// <summary>Enter (or the keypad's) went down this frame: a dialog's default button, sending a chat line.</summary>
        public static bool SubmitDown {
            get { return KeyDown(KeyCode.Return) || KeyDown(KeyCode.KeypadEnter); }
        }

        /// <summary>How much the mouse moved this frame, like the classic input's "Mouse X" and "Mouse Y" axes (looking around).</summary>
        public static Vector2 LookDelta {
            get {
                if (UsesInputSystem) {
                    return Mouse.current != null ? Mouse.current.delta.ReadValue() * MouseAxisScale : Vector2.zero;
                }
                return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            }
        }

        /// <summary>Where the pointer is on the screen, in pixels from its bottom left.</summary>
        public static Vector2 PointerPosition {
            get {
                if (UsesInputSystem) {
                    return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
                }
                return Input.mousePosition;
            }
        }

        /// <summary>How far the mouse wheel turned this frame, in notches (up positive).</summary>
        public static float ScrollDelta {
            get {
                if (UsesInputSystem) {
                    float scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0;
                    // notches; where it reports the platform's units instead (120 a notch on Windows), in notches too
                    return Mathf.Abs(scroll) > 2 ? scroll / 120 : scroll;
                }
                return Input.mouseScrollDelta.y;
            }
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
            KeyCode key = KeyPaths.KeyOf(playerPath(button));
            return key != KeyCode.None ? key : (KeyCode?)null;
        }

        private static string playerPath(string button) {
            string path = PlayerPrefs.GetString(PathPrefix + button, "");
            return path.Length > 0 ? path : null;
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
            string path = KeyPaths.PathOf(key);
            if (path == null) {
                return; // a key the Input System doesn't have
            }
            PlayerPrefs.SetString(PathPrefix + button, path);
            PlayerPrefs.Save();
            rebind(button);
            EventManager.SettingChanged.Raise(new SettingChangedEvent() { setting = SettingPrefix + button });
        }

        /// <summary>Back to the default keys of every button.</summary>
        public static void ResetAll() {
            foreach (Binding binding in Bindings) {
                PlayerPrefs.DeleteKey(PathPrefix + binding.button);
                rebind(binding.button);
            }
            PlayerPrefs.Save();
            EventManager.SettingChanged.Raise(new SettingChangedEvent() { setting = SettingPrefix + "*" });
        }

        private static void rebind(string button) {
            if (actions != null) {
                bind(actions[button], bindingsByButton[button]);
            }
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
    }
}
