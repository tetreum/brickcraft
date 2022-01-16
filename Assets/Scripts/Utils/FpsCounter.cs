using TMPro;
using UnityEngine;

public class FpsCounter : MonoBehaviour
{
    public TMP_Text fps;
    public int avgFrameRate;

    private void Update()
    {
        float current = 0;
        current = (int) (1f / Time.unscaledDeltaTime);
        avgFrameRate = (int) current;
        fps.text = avgFrameRate + " FPS";
    }
}