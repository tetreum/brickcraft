using System;
using System.Collections.Generic;
using System.Text;
using Brickcraft.Events;
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

        /// <summary>The player is typing a message (GameInput.IsTyping tells the rest of the game).</summary>
        public static bool IsTyping { get; private set; }

        // lines are kept even while the panel doesn't exist, so it listens from the start
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void subscribe() {
            EventManager.ClientStarted.Subscribe(e => history.Clear());
            EventManager.ChatLineReceived.Subscribe(onChatLine);
        }

        private static void onChatLine(ChatLineReceivedEvent line) {
            // things that happened read as a sentence, messages say who wrote them
            addLine(line.IsEvent ? line.text : line.sender + ": " + line.text);
        }

        private static void addLine(string text) {
            history.Add(new Line() { time = Time.unscaledTime, text = text });

            if (history.Count > HistorySize) {
                history.RemoveAt(0);
            }
        }

        private void Awake() {
            input.characterLimit = Network.ChatMessage.MaxLength;
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
            GameInput.SetTyping(true);
            Player.Instance.freeze(Player.FreezeReason.Chatting);

            input.gameObject.SetActive(true);
            input.text = "";
            input.ActivateInputField();
        }

        private void close() {
            IsTyping = false;
            GameInput.SetTyping(false);

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
