using System;
using System.Collections.Generic;
using System.Text;
using Brickcraft.Network;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The chat, in the player panel. The "Chat" input (Enter if the project doesn't define it)
    /// opens it, Enter sends the message and Escape closes it. Recent messages stay visible for
    /// a few seconds, all of them while typing.
    /// </summary>
    public class ChatPanel : MonoBehaviour
    {
        public const int MaxMessageLength = 200;

        private const int HistorySize = 50;
        private const int LinesShown = 10;
        private const float SecondsShown = 10f;

        public Text log;
        public InputField input;

        private struct Line
        {
            public float time;
            public string text;
        }

        // kept apart from the panel, messages can arrive before it exists
        private static readonly List<Line> history = new List<Line>();
        private static int closedFrame = -1;

        /// <summary>The player is typing, other keys shouldn't do anything.</summary>
        public static bool IsTyping { get; private set; }

        /// <summary>The chat closed this frame, so the key that closed it (Escape) shouldn't do anything else.</summary>
        public static bool WasClosedThisFrame {
            get { return closedFrame == Time.frameCount; }
        }

        public static void ClearHistory() {
            history.Clear();
        }

        /// <summary>Client handler of the messages the server relays.</summary>
        public static void OnChatMessage(ChatMessage message) {
            addLine(message.sender + ": " + message.text);
        }

        /// <summary>Client handler of the events the server announces, like players joining or leaving.</summary>
        public static void OnChatEvent(ChatEventMessage message) {
            string text = ChatEvents.Describe(message);

            if (text != null) {
                addLine(text);
            }
        }

        private static void addLine(string text) {
            history.Add(new Line() { time = Time.unscaledTime, text = text });

            if (history.Count > HistorySize) {
                history.RemoveAt(0);
            }
        }

        private void Awake() {
            input.characterLimit = MaxMessageLength;
            input.gameObject.SetActive(false);
            log.supportRichText = false; // so nobody can style their messages
        }

        private void OnDisable() {
            if (IsTyping) {
                close();
            }
        }

        private void Update() {
            if (!IsTyping) {
                if (isChatPressed() && Player.Instance != null && !Player.Instance.isFrozen) {
                    open();
                }
            } else if (GameInput.GetButtonDown(GameInput.Menu, KeyCode.Escape)) {
                close();
            } else if (isChatPressed() || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) {
                send();
                close();
            }

            refreshLog();
        }

        private static bool isChatPressed() {
            return GameInput.GetButtonDown(GameInput.Chat, KeyCode.Return, KeyCode.KeypadEnter);
        }

        private void open() {
            IsTyping = true;
            Player.Instance.freeze(Player.FreezeReason.Chatting);

            input.gameObject.SetActive(true);
            input.text = "";
            input.ActivateInputField();
        }

        private void close() {
            IsTyping = false;
            closedFrame = Time.frameCount;

            input.DeactivateInputField();
            input.gameObject.SetActive(false);
            if (EventSystem.current != null) {
                EventSystem.current.SetSelectedGameObject(null);
            }
            if (Player.Instance != null) {
                Player.Instance.unFreeze(Player.FreezeReason.Chatting);
            }
        }

        private void send() {
            string text = input.text.Trim();

            if (text.Length > 0 && Player.Instance != null) {
                Player.Instance.network.CmdChat(text);
            }
        }

        private void refreshLog() {
            StringBuilder text = new StringBuilder();
            int first = Math.Max(0, history.Count - LinesShown);

            for (int i = first; i < history.Count; i++) {
                if (IsTyping || Time.unscaledTime - history[i].time <= SecondsShown) {
                    if (text.Length > 0) {
                        text.Append('\n');
                    }
                    text.Append(history[i].text);
                }
            }
            log.text = text.ToString();
        }
    }
}
