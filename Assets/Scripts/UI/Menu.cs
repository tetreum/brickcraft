using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System.Runtime.InteropServices;

public class Menu : MonoBehaviour {

	public List<GameObject> Menus;

	public GameObject ItemTooltipPanel;

	public static Menu Instance;

    [DllImport("__Internal")]
    private static extern void showScreenshot(byte[] array, int byteLength, string fileName);

    void Awake() {
		Instance = this;
        DontDestroyOnLoad(transform.gameObject);
        DontDestroyOnLoad(GameObject.Find("EventSystem"));
        SceneManager.sceneLoaded += OnLevelFinishedLoading;
    }

    public void OnLevelFinishedLoading(Scene scene, LoadSceneMode mode) {
        switch (scene.name) {
			default:
                showPanel("PlayerPanel");
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

	/**
		Shows a tooltip near to item slot
		@param Vector3 pos Position to show the tooltip
		@param Item item Item which we want to show its information
		@return void
	 */
	public void showTooltip (Vector3 pos) {
		ItemTooltipPanel.SetActive (true);
		ItemTooltipPanel.GetComponent<RectTransform> ().SetAsLastSibling ();
		ItemTooltipPanel.transform.position = pos;
		/*
		ItemTooltipPanel.transform.Find("TitleLabel").GetComponent<Text>().text = item.name;
		ItemTooltipPanel.transform.Find("TextLabel").GetComponent<Text>().text = item.description;*/
	}
	
	/**
		hides the tooltip
		@return void
	*/
	public void hideTooltip () {
		ItemTooltipPanel.SetActive (false);
	}
}
