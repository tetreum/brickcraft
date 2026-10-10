using System;
using System.Collections.Generic;
using Brickcraft.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// List of the players in the game, in the player panel. Shown while the "PlayerList" input
    /// (Tab if the project doesn't define it) is held.
    ///
    /// Admins get buttons to teleport to a player, kick or ban it (they send the chat commands, see
    /// Network.ChatCommands): right-click frees the mouse to click them while the list is shown.
    /// Kicking and banning ask first, with an optional reason.
    /// </summary>
    public class PlayerListPanel : MonoBehaviour
    {
        public GameObject window;
        public Text count;
        /// <summary>A row, copied for each player.</summary>
        public PlayerListRow rowTemplate;
        /// <summary>Its height follows the rows, up to maxShownRows (then they scroll).</summary>
        public LayoutElement scroll;
        /// <summary>How to use the buttons, only for admins.</summary>
        public GameObject adminHint;
        /// <summary>Asks before kicking or banning (the Prefabs/UI/Popup prefab).</summary>
        public Popup popupPrefab;
        public int maxShownRows = 8;
        public Color banColor = new Color(0.78f, 0.22f, 0.18f);
        public Color banBorderColor = new Color(0.9f, 0.45f, 0.41f);

        private const float RefreshInterval = 0.5f;
        private float nextRefresh;
        private readonly List<PlayerListRow> rows = new List<PlayerListRow>();
        private bool isMouseFree;
        private Popup popup;

        private void Awake() {
            window.SetActive(false);
            rowTemplate.gameObject.SetActive(false);
        }

        private void OnDisable() {
            hide();
            if (popup != null && popup.gameObject.activeSelf) {
                popup.Close(-1);
            }
        }

        private void Update() {
            bool show = Player.Instance != null && !GameInput.IsTyping && isHeld();

            if (show != window.activeSelf) {
                if (show) {
                    window.SetActive(true);
                    nextRefresh = 0;
                } else {
                    hide();
                }
            }
            if (!show) {
                return;
            }
            if (isAdmin() && !isMouseFree && Input.GetMouseButtonDown(1)) {
                freeMouse();
            }
            if (Time.unscaledTime >= nextRefresh) {
                nextRefresh = Time.unscaledTime + RefreshInterval;
                refresh();
            }
        }

        private static bool isHeld() {
            return GameInput.GetButton(GameInput.PlayerList);
        }

        private static bool isAdmin() {
            return Player.Instance != null && Player.Instance.network.role == PlayerRoles.Admin;
        }

        // the player stands still meanwhile, or clicking would also place and break bricks
        private void freeMouse() {
            isMouseFree = true;
            Game.unlockMouse();
            Player.Instance.freeze(Player.FreezeReason.PlayerList);
        }

        private void hide() {
            window.SetActive(false);
            if (!isMouseFree) {
                return;
            }
            isMouseFree = false;
            if (Player.Instance != null) {
                Player.Instance.unFreeze(Player.FreezeReason.PlayerList);
            }
            // a confirmation took over the mouse
            if (!DialogMode.IsOpen) {
                Game.lockMouse();
            }
        }

        private void refresh() {
            List<PlayerNetwork> players = new List<PlayerNetwork>(FindObjectsOfType<PlayerNetwork>());
            players.Sort((a, b) => string.Compare(a.playerName, b.playerName, StringComparison.OrdinalIgnoreCase));
            bool admin = isAdmin();

            while (rows.Count < players.Count) {
                rows.Add(createRow());
            }
            for (int i = 0; i < rows.Count; i++) {
                bool used = i < players.Count;
                rows[i].gameObject.SetActive(used);
                if (used) {
                    rows[i].Show(players[i], admin);
                }
            }

            count.text = players.Count + " online";
            adminHint.SetActive(admin);

            VerticalLayoutGroup layout = rowTemplate.transform.parent.GetComponent<VerticalLayoutGroup>();
            float rowHeight = rowTemplate.GetComponent<LayoutElement>().preferredHeight;
            int shown = Mathf.Clamp(players.Count, 1, maxShownRows);
            scroll.preferredHeight = shown * rowHeight + (shown - 1) * layout.spacing;
        }

        private PlayerListRow createRow() {
            PlayerListRow row = Instantiate(rowTemplate, rowTemplate.transform.parent);
            row.name = "Row";
            row.TeleportClicked += teleport;
            row.KickClicked += name => confirm(name, false);
            row.BanClicked += name => confirm(name, true);
            return row;
        }

        private static void teleport(string playerName) {
            command("/tp " + playerName);
        }

        private void confirm(string playerName, bool ban) {
            if (popup == null) {
                popup = Instantiate(popupPrefab, GetComponentInParent<Canvas>().rootCanvas.transform, false);
                popup.name = "PlayerListPopup";
            }
            hide(); // the popup takes the mouse from here
            popup.transform.SetAsLastSibling();
            popup.primaryColor = ban ? banColor : popupPrefab.primaryColor;
            popup.primaryBorderColor = ban ? banBorderColor : popupPrefab.primaryBorderColor;

            string title = (ban ? "Ban " : "Kick ") + playerName + "?";
            string text = ban
                ? playerName + " and their machine won't be able to join again, until /unban " + playerName + "."
                : playerName + " will leave the game, they can join again.";
            DialogMode.Enter();
            popup.Show(title, text, new[] { ban ? "Ban" : "Kick", "Cancel" }, true, "", "Reason (optional)", (button, reason) => {
                DialogMode.Exit();
                if (button == 0) {
                    reason = reason.Trim();
                    command((ban ? "/ban " : "/kick ") + playerName + (reason.Length > 0 ? " " + reason : ""));
                }
            });
        }

        // the server checks and answers in the chat
        private static void command(string text) {
            if (Player.Instance != null) {
                Player.Instance.network.CmdChat(text);
            }
        }
    }
}
