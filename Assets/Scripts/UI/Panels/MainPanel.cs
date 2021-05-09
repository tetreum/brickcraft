using UnityEngine;
using UnityEngine.SceneManagement;

public class MainPanel : MonoBehaviour
{
    public void play () {
        Menu.Instance.showPanel("LoadingPanel");
        SceneManager.LoadScene("WorldGenerationTest");
    }

    public void playTest() {
        SceneManager.LoadScene("Test");
    }

    public void exit() {
        Application.Quit();
    }
}
