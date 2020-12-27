using UnityEngine;

public class Game : MonoBehaviour
{
    public static Game Instance;

    public Shader transparentShader;
    public enum Layers
    {
        IgnoreRaycast = 2
    }

    private void Awake() {
        Instance = this;
    }
}
