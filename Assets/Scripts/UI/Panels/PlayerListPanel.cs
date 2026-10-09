using System;
using System.Collections.Generic;
using System.Text;
using Brickcraft.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// List of the players in the game, in the player panel. Shown while the "PlayerList" input
    /// (Tab if the project doesn't define it) is held.
    /// </summary>
    public class PlayerListPanel : MonoBehaviour
    {
        public GameObject window;
        public Text title;
        public Text names;
        public Text roles;
        public Text pings;

        private const float RefreshInterval = 0.5f;
        private float nextRefresh;

        private void Awake() {
            window.SetActive(false);

            // names are chosen by players, they can't style the list
            names.supportRichText = false;
        }

        private void Update() {
            bool show = Player.Instance != null && !GameInput.IsTyping && isHeld();

            if (show != window.activeSelf) {
                window.SetActive(show);
                nextRefresh = 0;
            }
            if (show && Time.unscaledTime >= nextRefresh) {
                nextRefresh = Time.unscaledTime + RefreshInterval;
                refresh();
            }
        }

        private static bool isHeld() {
            return GameInput.GetButton(GameInput.PlayerList);
        }

        private void refresh() {
            List<PlayerNetwork> players = new List<PlayerNetwork>(FindObjectsOfType<PlayerNetwork>());
            players.Sort((a, b) => string.Compare(a.playerName, b.playerName, StringComparison.OrdinalIgnoreCase));

            StringBuilder nameColumn = new StringBuilder();
            StringBuilder roleColumn = new StringBuilder();
            StringBuilder pingColumn = new StringBuilder();

            foreach (PlayerNetwork player in players) {
                nameColumn.Append(player.playerName).Append(player.isLocalPlayer ? " (you)" : "").Append('\n');
                roleColumn.Append(player.role == PlayerRoles.Admin ? "Admin" : "").Append('\n');
                pingColumn.Append(player.pingMs).Append(" ms\n");
            }

            title.text = "Players (" + players.Count + ")";
            names.text = nameColumn.ToString();
            roles.text = roleColumn.ToString();
            pings.text = pingColumn.ToString();
        }
    }
}
