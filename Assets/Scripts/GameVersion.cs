using UnityEngine;

namespace Brickcraft
{
    /// <summary>
    /// The version of the game: Player Settings → Version (Application.version). Clients can only
    /// join servers running the same version, and saves remember which versions created and last
    /// saved them, so bump it on every release.
    /// </summary>
    public static class GameVersion
    {
        // Application.version only works on the main thread, saves are written on others
        public static string Current { get; private set; } = "unknown";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void init() {
            Current = Application.version;
        }

#if UNITY_EDITOR
        // edit mode tools save worlds too
        [UnityEditor.InitializeOnLoadMethod]
        private static void initEditor() {
            Current = Application.version;
        }
#endif
    }
}
