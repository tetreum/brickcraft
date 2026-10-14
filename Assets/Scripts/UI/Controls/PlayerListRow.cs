using System;
using Brickcraft.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// A player in the player list: name, an admin badge, ping and, for admins looking at it, buttons to
    /// teleport to the player, kick or ban it. Rows are reused as the list refreshes, the buttons act on
    /// whoever the row shows when clicked.
    /// </summary>
    public class PlayerListRow : MonoBehaviour
    {
        public Text playerName;
        public GameObject adminBadge;
        public Text ping;
        /// <summary>The buttons, only for admins.</summary>
        public GameObject actions;
        public Button teleportButton;
        public Button kickButton;
        public Button banButton;

        public Color goodPing = new Color(0.45f, 0.85f, 0.35f);
        public Color mediumPing = new Color(0.95f, 0.75f, 0.25f);
        public Color badPing = new Color(0.95f, 0.35f, 0.3f);

        private const int MediumPingMs = 100;
        private const int BadPingMs = 250;

        /// <summary>Who it shows.</summary>
        public string PlayerName { get; private set; }

        public event Action<string> TeleportClicked;
        public event Action<string> KickClicked;
        public event Action<string> BanClicked;

        private void Awake() {
            // names are chosen by players, they can't style the list
            playerName.supportRichText = false;
            teleportButton.onClick.AddListener(() => TeleportClicked?.Invoke(PlayerName));
            kickButton.onClick.AddListener(() => KickClicked?.Invoke(PlayerName));
            banButton.onClick.AddListener(() => BanClicked?.Invoke(PlayerName));
        }

        /// <param name="isAdminView">Whether the local player is an admin, who gets the buttons.</param>
        public void Show(PlayerNetwork player, bool isAdminView) {
            PlayerName = player.playerName;
            bool isAdmin = player.role == PlayerRoles.Admin;

            playerName.text = player.playerName + (player.isLocalPlayer ? " (you)" : "");
            adminBadge.SetActive(isAdmin);
            ping.text = player.pingMs + " ms";
            ping.color = player.pingMs >= BadPingMs ? badPing : player.pingMs >= MediumPingMs ? mediumPing : goodPing;

            // nobody acts on themselves, and admins can't be kicked or banned (the server checks it too);
            // the buttons' column keeps its width so pings stay aligned
            actions.SetActive(isAdminView);
            teleportButton.gameObject.SetActive(!player.isLocalPlayer);
            kickButton.gameObject.SetActive(!player.isLocalPlayer && !isAdmin);
            banButton.gameObject.SetActive(!player.isLocalPlayer && !isAdmin);
        }
    }
}
