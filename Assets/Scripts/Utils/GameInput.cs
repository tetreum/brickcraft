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
    /// They're read with the Input System: an InputAction for each button, and the keyboard and mouse for the rest
    /// (modifiers, the mouse's movement, pointer and wheel).
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

        /// <summary>There's a keyboard or a mouse to read.</summary>
        public static bool HasKeyboardOrMouse {
            get { return Keyboard.current != null || Mouse.current != null; }
        }

        /// <summary>Its actions are made again the next time one is read: the Input System was reset (tests' InputTestFixture).</summary>
        public static void Reload() {
            if (actions != null) {
                foreach (InputAction action in actions.Values) {
                    action.Dispose();
                }
                actions = null;
            }
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
            return actionOf(button).WasPressedThisFrame();
        }

        /// <summary>The button is held.</summary>
        public static bool GetButton(string button) {
            return actionOf(button).IsPressed();
        }

        /// <summary>Movement from -1 to 1 on each axis (x: left/right, y: back/forward): the movement keys.</summary>
        public static Vector2 GetMovement() {
            return new Vector2(
                (GetButton(MoveRight) ? 1 : 0) - (GetButton(MoveLeft) ? 1 : 0),
                (GetButton(MoveForward) ? 1 : 0) - (GetButton(MoveBack) ? 1 : 0));
        }

        // -------- keys and the mouse --------
        // what isn't one of the remappable buttons: modifiers (Shift for any stud, Ctrl for the whole stack), Enter and
        // Escape in dialogs, the mouse's movement, pointer and wheel

        // the mouse's movement as the classic input's axes had it: a tenth of the pixels moved (the feel of looking around)
        private const float MouseAxisScale = 0.1f;

        /// <summary>A key or mouse button (KeyCode.Mouse0...) is held.</summary>
        public static bool KeyHeld(KeyCode key) {
            ButtonControl control = controlOf(key);
            return control != null && control.isPressed;
        }

        /// <summary>A key or mouse button went down this frame.</summary>
        public static bool KeyDown(KeyCode key) {
            ButtonControl control = controlOf(key);
            return control != null && control.wasPressedThisFrame;
        }

        /// <summary>A key or mouse button went up this frame.</summary>
        public static bool KeyUp(KeyCode key) {
            ButtonControl control = controlOf(key);
            return control != null && control.wasReleasedThisFrame;
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

        /// <summary>How much the mouse moved this frame, for looking around (see MouseAxisScale).</summary>
        public static Vector2 LookDelta {
            get { return Mouse.current != null ? Mouse.current.delta.ReadValue() * MouseAxisScale : Vector2.zero; }
        }

        /// <summary>Where the pointer is on the screen, in pixels from its bottom left.</summary>
        public static Vector2 PointerPosition {
            get { return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero; }
        }

        /// <summary>How far the mouse wheel turned this frame, in notches (up positive).</summary>
        public static float ScrollDelta {
            get {
                float scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0;
                // notches; where it reports the platform's units instead (120 a notch on Windows), in notches too
                return Mathf.Abs(scroll) > 2 ? scroll / 120 : scroll;
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

        private static string playerPath(string button) {
            string path = PlayerPrefs.GetString(PathPrefix + button, "");
            return path.Length > 0 ? path : null;
        }

        /// <summary>The controls the button uses now (control paths, see KeyPaths): the player's, or its defaults.</summary>
        public static string[] GetPaths(string button) {
            string player = playerPath(button);
            if (player != null) {
                return new[] { player };
            }
            Binding binding = bindingsByButton[button];
            List<string> paths = new List<string>();
            foreach (KeyCode key in new[] { binding.defaultKey, binding.defaultAltKey }) {
                string path = KeyPaths.PathOf(key);
                if (path != null) {
                    paths.Add(path);
                }
            }
            return paths.ToArray();
        }

        /// <summary>The button uses that control now (a key or mouse button, a control path), instead of its defaults.</summary>
        public static void Remap(string button, string path) {
            if (string.IsNullOrEmpty(path) || !(path.StartsWith("<Keyboard>/") || path.StartsWith("<Mouse>/"))) {
                return;
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

        /// <summary>
        /// Waits for the next key or mouse button pressed (the Input System's interactive rebinding: not the mouse's movement
        /// or wheel), and the button uses it; Escape cancels. Then calls done with its control path, null if cancelled.
        /// Dispose what it returns to stop waiting. Null (done isn't called) without a keyboard or mouse.
        /// </summary>
        public static IDisposable StartRebinding(string button, Action<string> done) {
            if (!HasKeyboardOrMouse) {
                return null;
            }
            return new Rebinding(button, actionOf(button), done);
        }

        // waiting for the key of a button: the Input System's rebinding of its action, its result kept by Remap
        private sealed class Rebinding : IDisposable
        {
            private readonly string button;
            private readonly InputAction action;
            private readonly Action<string> done;
            private readonly InputActionRebindingExtensions.RebindingOperation operation;
            private bool finished;

            public Rebinding(string button, InputAction action, Action<string> done) {
                this.button = button;
                this.action = action;
                this.done = done;
                action.Disable();
                operation = action.PerformInteractiveRebinding(0)
                    .WithControlsHavingToMatchPath("<Keyboard>")
                    .WithControlsHavingToMatchPath("<Mouse>")
                    .WithControlsExcluding("<Keyboard>/anyKey")
                    .WithControlsExcluding("<Mouse>/press")
                    .WithControlsExcluding("<Mouse>/position")
                    .WithControlsExcluding("<Mouse>/delta")
                    .WithControlsExcluding("<Mouse>/scroll")
                    .WithExpectedControlType("Button")
                    .WithCancelingThrough("<Keyboard>/escape")
                    .OnComplete(o => finish(o.action.bindings[0].overridePath, true))
                    .OnCancel(o => finish(null, true))
                    .Start();
            }

            // the action back as it was (Remap rebuilds it with the new key), and whoever waited told
            private void finish(string path, bool tell) {
                if (finished) {
                    return;
                }
                finished = true;
                operation.Dispose();
                action.RemoveAllBindingOverrides();
                action.Enable();
                if (path != null) {
                    Remap(button, path);
                }
                if (tell) {
                    done(path);
                }
            }

            public void Dispose() {
                finish(null, false);
            }
        }

        /// <summary>A control as players know it: "Left Click", "Left Shift", "1"... (the Input System's name for keys the game didn't know).</summary>
        public static string PathName(string path) {
            KeyCode key = KeyPaths.KeyOf(path);
            return key != KeyCode.None ? KeyName(key) : InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
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
