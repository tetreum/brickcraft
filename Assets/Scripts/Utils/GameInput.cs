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
        /// <summary>Building technic: the part held goes on the other way round, or by another of its connectors (see Bricks.BrickJoints).</summary>
        public const string Flip = "Flip";
        public const string NextConnector = "NextConnector";
        /// <summary>Building technic: the part held slides along the connection, half a stud at a time.</summary>
        public const string SlideBack = "SlideBack";
        public const string SlideForward = "SlideForward";
        /// <summary>Building: the brick held goes by one of its corners at the crosshair instead of centred on it, the next corner each press.</summary>
        public const string Corner = "Corner";
        public const string Inventory = "Inventory";
        public const string Chat = "Chat";
        public const string PlayerList = "PlayerList";
        public const string Menu = "Menu";
        public const string SlotPrefix = "Slot"; // Slot1 to Slot9
        /// <summary>In a vehicle: up and down (a plane's nose, see Vehicles.WireSource.UpDown).</summary>
        public const string VehicleUp = "VehicleUp";
        public const string VehicleDown = "VehicleDown";

        public sealed class Binding
        {
            /// <summary>Its name, what the game asks for (see GameInput.GetButton).</summary>
            public string button;
            /// <summary>Its key in the Controls table (see Label).</summary>
            public string labelKey;
            /// <summary>The number in its label, like a hotbar slot's (0 if none).</summary>
            public int labelNumber;
            /// <summary>To group them in the settings: its id, "movement"... (see CategoryName).</summary>
            public string category;
            public KeyCode defaultKey;
            public KeyCode defaultAltKey;

            public Binding(string category, string button, string labelKey, KeyCode defaultKey, KeyCode defaultAltKey = KeyCode.None, int labelNumber = 0) {
                this.category = category;
                this.button = button;
                this.labelKey = labelKey;
                this.labelNumber = labelNumber;
                this.defaultKey = defaultKey;
                this.defaultAltKey = defaultAltKey;
            }

            /// <summary>What the action is called, in the player's language.</summary>
            public string Label {
                get { return labelNumber > 0 ? Loc.Get(Loc.Controls, labelKey, labelNumber) : Loc.Get(Loc.Controls, labelKey); }
            }

            /// <summary>Its category's name, in the player's language.</summary>
            public string CategoryName {
                get { return Loc.Get(Loc.Controls, "category." + category); }
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
                new Binding("movement", MoveForward, "controls.move_forward", KeyCode.W, KeyCode.UpArrow),
                new Binding("movement", MoveBack, "controls.move_back", KeyCode.S, KeyCode.DownArrow),
                new Binding("movement", MoveLeft, "controls.move_left", KeyCode.A, KeyCode.LeftArrow),
                new Binding("movement", MoveRight, "controls.move_right", KeyCode.D, KeyCode.RightArrow),
                new Binding("movement", Jump, "controls.jump", KeyCode.Space),
                new Binding("movement", Sprint, "controls.sprint", KeyCode.LeftShift),
                new Binding("building", Dig, "controls.dig", KeyCode.Mouse0),
                new Binding("building", Place, "controls.place", KeyCode.Mouse1),
                new Binding("building", Rotate, "controls.rotate", KeyCode.R),
                new Binding("building", Interact, "controls.interact", KeyCode.E),
                new Binding("building", Drop, "controls.drop", KeyCode.Q),
                new Binding("building", FreePlacement, "controls.free_placement", KeyCode.LeftShift, KeyCode.RightShift),
                new Binding("building", Flip, "controls.flip", KeyCode.T),
                new Binding("building", NextConnector, "controls.next_connector", KeyCode.G),
                new Binding("building", SlideBack, "controls.slide_back", KeyCode.Z),
                new Binding("building", SlideForward, "controls.slide_forward", KeyCode.X),
                new Binding("building", Corner, "controls.corner", KeyCode.Mouse2),
                new Binding("interface", Inventory, "controls.inventory", KeyCode.I),
                new Binding("interface", Chat, "controls.chat", KeyCode.Return, KeyCode.KeypadEnter),
                new Binding("interface", PlayerList, "controls.player_list", KeyCode.Tab),
                new Binding("interface", Menu, "controls.menu", KeyCode.Escape),
                new Binding("vehicle", VehicleUp, "controls.vehicle_up", KeyCode.R),
                new Binding("vehicle", VehicleDown, "controls.vehicle_down", KeyCode.F),
            };
            for (int i = 1; i <= 9; i++) {
                bindings.Add(new Binding("hotbar", SlotPrefix + i, "controls.hotbar_slot", KeyCode.Alpha0 + i, KeyCode.None, i));
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

        /// <summary>A key as players know it, in their language: "Left Click", "Left Shift", "1"...</summary>
        public static string KeyName(KeyCode key) {
            string named = keyNameKey(key);
            if (named != null) {
                return Loc.Get(Loc.Controls, named);
            }
            string name = key.ToString();
            if (name.StartsWith("Alpha")) {
                return name.Substring(5);
            }
            if (name.StartsWith("Mouse")) {
                return Loc.Get(Loc.Controls, "key.mouse", name.Substring(5));
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

        // the Controls table's key of the keys whose name isn't just their KeyCode's, null for the rest
        private static string keyNameKey(KeyCode key) {
            switch (key) {
                case KeyCode.Mouse0: return "key.left_click";
                case KeyCode.Mouse1: return "key.right_click";
                case KeyCode.Mouse2: return "key.middle_click";
                case KeyCode.Return: return "key.enter";
                case KeyCode.KeypadEnter: return "key.keypad_enter";
                case KeyCode.Escape: return "key.esc";
                case KeyCode.UpArrow: return "key.up";
                case KeyCode.DownArrow: return "key.down";
                case KeyCode.LeftArrow: return "key.left";
                case KeyCode.RightArrow: return "key.right";
                case KeyCode.LeftShift: return "key.left_shift";
                case KeyCode.RightShift: return "key.right_shift";
                case KeyCode.LeftControl: return "key.left_ctrl";
                case KeyCode.RightControl: return "key.right_ctrl";
                case KeyCode.LeftAlt: return "key.left_alt";
                case KeyCode.RightAlt: return "key.right_alt";
                case KeyCode.Space: return "key.space";
                case KeyCode.Backspace: return "key.backspace";
                case KeyCode.Delete: return "key.delete";
                case KeyCode.CapsLock: return "key.caps_lock";
            }
            return null;
        }
    }
}
