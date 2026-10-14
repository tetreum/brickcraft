using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System.Runtime.InteropServices;
using Brickcraft;
using Brickcraft.Events;
using Brickcraft.UI;

public class Menu : MonoBehaviour {

	public List<GameObject> Menus;


	public static Menu Instance;

    [DllImport("__Internal")]
    private static extern void showScreenshot(byte[] array, int byteLength, string fileName);

    void Awake() {
		Instance = this;
		
        //DontDestroyOnLoad(transform.gameObject);
        //DontDestroyOnLoad(GameObject.Find("EventSystem"));
		
        SceneManager.sceneLoaded += OnLevelFinishedLoading;
		EventManager.LocalPlayerStarted.Subscribe(onLocalPlayerStarted);
		EventManager.WorldLoadingStarted.Subscribe(onWorldLoadingStarted);

		InventoryPanel.Instance = getPanel("InventoryPanel").GetComponent<InventoryPanel>();
		Brickcraft.Network.ClientStorage.Changed += onStorageChanged;
		PartsListerView.Opened += PartsListerPanel.Show;
		PartsListerView.Closed += PartsListerPanel.Hide;
		ModelImporterView.WindowRequested += ModelImporterPanel.Open;
		ColorPickerTool.WindowRequested += ColorPickerPanel.Open;
		ModelImporterView.Opened += ModelImporterPanel.ShowHint;
		ModelImporterView.Closed += ModelImporterPanel.HideHint;
    }

	// a storage brick the server opened for us: its panel, over the others
	private void onStorageChanged() {
		if (Brickcraft.Network.ClientStorage.IsOpen && !getPanel(StoragePanel.PanelName).activeSelf) {
			showPanel(StoragePanel.PanelName, false);
		}
	}
	private void OnDestroy() {
		SceneManager.sceneLoaded -= OnLevelFinishedLoading;
		EventManager.LocalPlayerStarted.Unsubscribe(onLocalPlayerStarted);
		EventManager.WorldLoadingStarted.Unsubscribe(onWorldLoadingStarted);
		Brickcraft.Network.ClientStorage.Changed -= onStorageChanged;
		PartsListerView.Opened -= PartsListerPanel.Show;
		PartsListerView.Closed -= PartsListerPanel.Hide;
		ModelImporterView.WindowRequested -= ModelImporterPanel.Open;
		ColorPickerTool.WindowRequested -= ColorPickerPanel.Open;
		ModelImporterView.Opened -= ModelImporterPanel.ShowHint;
		ModelImporterView.Closed -= ModelImporterPanel.HideHint;
	}

	private void onLocalPlayerStarted(LocalPlayerStartedEvent e) {
		showPanel("PlayerPanel");
	}

	private void onWorldLoadingStarted(WorldLoadingStartedEvent e) {
		showPanel("LoadingPanel");
	}

	// the panels' shortcuts, while playing
	private void Update() {
		if (Player.Instance == null || GameInput.IsTyping || SettingsPanel.IsOpen || SettingsPanel.WasClosedThisFrame) {
			return;
		}
		// looking with the parts lister: Escape cancels it, the inventory stays closed
		if (PartsListerView.IsOpen) {
			if (GameInput.GetButtonDown(GameInput.Menu)) {
				PartsListerView.Current.Cancel();
			}
			return;
		}
		// placing an imported model: Escape gives up
		if (ModelImporterView.IsOpen && GameInput.GetButtonDown(GameInput.Menu)) {
			ModelImporterView.Current.Close();
			return;
		}
		// a storage panel open: the inventory key and Escape close it
		GameObject storage = getPanel(StoragePanel.PanelName);
		if (storage.activeSelf) {
			if (GameInput.GetButtonDown(GameInput.Inventory) || GameInput.GetButtonDown(GameInput.Menu)) {
				storage.SetActive(false);
			}
			return;
		}
		// sleeping, the inventory stays closed (the chat and the player list work)
		if (GameInput.GetButtonDown(GameInput.Inventory) && !Player.Instance.IsSleeping) {
			togglePanel("InventoryPanel");
		}
		if (GameInput.GetButtonDown(GameInput.Menu)) {
			togglePanel("ESCPanel");
		}
	}

	public void OnLevelFinishedLoading(Scene scene, LoadSceneMode mode) {
        switch (scene.name) {
			case "Main":
				Game.unlockMouse();
				showPanel("MainPanel");
				break;
			case "IconGenerator":
				hideAllPanels();
				Game.unlockMouse();
				break;
			default:
                showPanel("LoadingPanel");
				break;
		}
		/*
        if (Chat.Instance != null) {
            Chat.Instance.deleteAllMessages();
        } else {
            getPanel("Chat").GetComponent<Chat>().deleteAllMessages();
        }*/
	}

	public GameObject getPanel (string name) {
		foreach (GameObject panel in Menus) {
			if (panel.name == name) {
				return panel;
			}
		}
		throw new UnityException ("UI Panel "+ name +" not found");
	}

	public void togglePanel (string name) {
		GameObject panel = getPanel (name);

		panel.SetActive (!panel.activeSelf);
	}
	
	public GameObject showPanel (string name, bool hidePanels = true) {
		if (hidePanels) {
			hideAllPanels ();
		}

		GameObject panel = this.getPanel (name);
		panel.SetActive (true);

		return panel;
	}

	public void hidePanel (string name) {
		foreach (GameObject panel in Menus) {
			if (panel.name == name) {
				panel.SetActive(false);
			}
		}
	}
	
	public void hideAllPanels() {
		foreach (GameObject panel in Menus) {
			panel.SetActive(false);
		}
	}

	/*
	 * We place this here since SmartphonePanel gets disabled when taking a screenshot
	 */
	public void takeScreenshot () {
		StartCoroutine(takeScreen());
	}

	IEnumerator takeScreen () {
        // hide some panels before taking the screenshot
        Menu.Instance.hidePanel("SmartphonePanel");
        Menu.Instance.hidePanel("PlayerPanel");

        yield return new WaitForEndOfFrame();

        int width = Screen.width;
        int height = Screen.height;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);

        // Read screen contents into the texture
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        // Encode texture into PNG
        byte[] bytes = tex.EncodeToPNG();
        Destroy(tex);

        string name = "Cubity_" + System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ".png";

#if !UNITY_EDITOR
        showScreenshot(bytes, bytes.Length, name);
#endif
		
		Menu.Instance.showPanel("SmartphonePanel", false);
		Menu.Instance.showPanel("PlayerPanel", false);
	}
}
