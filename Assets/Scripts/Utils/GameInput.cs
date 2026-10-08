using System;
using System.Collections.Generic;
using UnityEngine;

namespace Brickcraft
{
    /// <summary>
    /// Buttons of the Input Manager (Edit → Project Settings → Input Manager), so players and
    /// modders can rebind them. A button the project doesn't define falls back to its default key.
    /// </summary>
    public static class GameInput
    {
        public const string Dig = "Dig";
        public const string Place = "Place";
        public const string Rotate = "Rotate";
        public const string FreePlacement = "FreePlacement";
        public const string Sprint = "Sprint";
        public const string Menu = "Menu";
        public const string Inventory = "Inventory";
        public const string Chat = "Chat";
        public const string PlayerList = "PlayerList";
        public const string SlotPrefix = "Slot"; // Slot1 to Slot9

        // buttons the project doesn't define, so asking for them doesn't throw every frame
        private static readonly HashSet<string> missing = new HashSet<string>();

        /// <summary>The button went down this frame.</summary>
        public static bool GetButtonDown(string button, KeyCode fallback, KeyCode altFallback = KeyCode.None) {
            if (!missing.Contains(button)) {
                try {
                    return Input.GetButtonDown(button);
                } catch (ArgumentException) {
                    warnMissing(button);
                }
            }
            return Input.GetKeyDown(fallback) || (altFallback != KeyCode.None && Input.GetKeyDown(altFallback));
        }

        /// <summary>The button is held.</summary>
        public static bool GetButton(string button, KeyCode fallback, KeyCode altFallback = KeyCode.None) {
            if (!missing.Contains(button)) {
                try {
                    return Input.GetButton(button);
                } catch (ArgumentException) {
                    warnMissing(button);
                }
            }
            return Input.GetKey(fallback) || (altFallback != KeyCode.None && Input.GetKey(altFallback));
        }

        /// <summary>The fast inventory slot (0 to 8) whose button went down this frame, -1 if none.</summary>
        public static int GetSlotDown() {
            for (int i = 0; i < 9; i++) {
                if (GetButtonDown(SlotPrefix + (i + 1), KeyCode.Alpha1 + i)) {
                    return i;
                }
            }
            return -1;
        }

        private static void warnMissing(string button) {
            missing.Add(button);
            Debug.LogWarning("The Input Manager has no \"" + button + "\" button, using its default key");
        }
    }
}
