using Brickcraft.Network;
using UnityEngine;
using UnityEngine.UI;

public class MainPanel : MonoBehaviour
{
    public InputField addressInput;
    public InputField nameInput;
    public Text messageText;
    public Text versionText;

    private static bool hasReadCommandLine;

    /// <summary>Message to show next time the menu opens, like why the server disconnected us.</summary>
    public static string PendingMessage;

    // a disconnection can happen while this panel doesn't exist (in game), so it listens from the start
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void subscribe () {
        Brickcraft.Events.EventManager.Disconnected.Subscribe(onDisconnected);
    }

    private static void onDisconnected (Brickcraft.Events.DisconnectedEvent e) {
        if (e.isInMenu && Menu.Instance != null) {
            Menu.Instance.showPanel("MainPanel").GetComponent<MainPanel>().showMessage(e.message);
        } else {
            PendingMessage = e.message; // shown once the menu loads
        }
    }

    private void OnEnable () {
        if (versionText != null) {
            versionText.text = "v" + Brickcraft.GameVersion.Current;
        }
        if (nameInput != null) {
            nameInput.text = PlayerIdentity.Name;
        }
        if (PendingMessage != null) {
            showMessage(PendingMessage);
            PendingMessage = null;
        }
    }

    public void showMessage (string message) {
        if (messageText != null) {
            messageText.text = message;
        }
    }

    // "-host" or "-join <address>" start a multiplayer game right away, handy to test with several instances
    // "-test" starts the test scene, which has no button
    // "-name <name>" picks the player name
    private void Start () {
        if (hasReadCommandLine) {
            return;
        }
        hasReadCommandLine = true;

        string[] args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++) {
            if (args[i] == "-name") {
                nameInput.text = args[i + 1];
            }
        }
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "-host") {
                host();
            } else if (args[i] == "-test") {
                playTest();
            } else if (args[i] == "-join" && i + 1 < args.Length) {
                addressInput.text = args[i + 1];
                join();
            }
        }
    }

    // saves the typed name, false if it isn't valid
    private bool applyName () {
        string name = nameInput != null ? nameInput.text.Trim() : PlayerIdentity.Name;
        string error = PlayerIdentity.ValidateName(name);

        if (error != null) {
            showMessage(error);
            return false;
        }
        PlayerIdentity.Name = name;
        showMessage("");
        return true;
    }

    // a server only this player can join
    public void play () {
        if (!applyName()) {
            return;
        }
        Menu.Instance.showPanel("LoadingPanel");
        BrickcraftNetworkManager.GetOrCreate().StartSingleplayer();
    }

    public void playTest() {
        if (!applyName()) {
            return;
        }
        Menu.Instance.showPanel("LoadingPanel");
        BrickcraftNetworkManager.GetOrCreate().StartSingleplayer(BrickcraftNetworkManager.TestScene);
    }

    public void host () {
        if (!applyName()) {
            return;
        }
        Menu.Instance.showPanel("LoadingPanel");
        BrickcraftNetworkManager.GetOrCreate().StartMultiplayerHost();
    }

    public void join () {
        if (!applyName()) {
            return;
        }
        string address = addressInput != null && !string.IsNullOrWhiteSpace(addressInput.text) ? addressInput.text.Trim() : "localhost";

        Menu.Instance.showPanel("LoadingPanel");
        BrickcraftNetworkManager.GetOrCreate().Join(address);
    }

    public void openSettings () {
        Brickcraft.UI.SettingsPanel.Open();
    }

    public void exit() {
        Application.Quit();
    }
}
