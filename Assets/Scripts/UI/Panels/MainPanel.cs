using Brickcraft.Network;
using UnityEngine;
using UnityEngine.UI;

public class MainPanel : MonoBehaviour
{
    public InputField addressInput;

    private static bool hasReadCommandLine;

    // "-host" or "-join <address>" start a multiplayer game right away, handy to test with several instances
    private void Start () {
        if (hasReadCommandLine) {
            return;
        }
        hasReadCommandLine = true;

        string[] args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "-host") {
                host();
            } else if (args[i] == "-join" && i + 1 < args.Length) {
                addressInput.text = args[i + 1];
                join();
            }
        }
    }

    // a server only this player can join
    public void play () {
        Menu.Instance.showPanel("LoadingPanel");
        BrickcraftNetworkManager.GetOrCreate().StartSingleplayer();
    }

    public void playTest() {
        Menu.Instance.showPanel("LoadingPanel");
        BrickcraftNetworkManager.GetOrCreate().StartSingleplayer(BrickcraftNetworkManager.TestScene);
    }

    public void host () {
        Menu.Instance.showPanel("LoadingPanel");
        BrickcraftNetworkManager.GetOrCreate().StartMultiplayerHost();
    }

    public void join () {
        string address = addressInput != null && !string.IsNullOrWhiteSpace(addressInput.text) ? addressInput.text.Trim() : "localhost";

        Menu.Instance.showPanel("LoadingPanel");
        BrickcraftNetworkManager.GetOrCreate().Join(address);
    }

    public void exit() {
        Application.Quit();
    }
}
