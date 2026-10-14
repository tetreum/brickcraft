using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Brickcraft.Editor
{
    /// <summary>
    /// Puts back what the MCP bridge's test runs change. Starting a run, the bridge turns Enter Play Mode Options on,
    /// which Unity writes into EditorSettings.asset (the bridge only puts them back in memory), and saves the open
    /// scenes that are dirty (Main.unity always is: its layout rebuilds when it opens). So the settings file is kept
    /// as it is while no run goes on (a baseline, refreshed when it changes: the user's own changes are kept), and a
    /// scene the bridge saves is copied right before; once the run is over and play mode left, the copies go back
    /// (the scenes with none of them open, so Unity doesn't ask about a scene changed on disk, then reopened).
    /// Saves outside the bridge's runs are left alone. The copies wait in Library/TestRunCleanup.
    /// </summary>
    [InitializeOnLoad]
    public static class TestRunCleanup
    {
        private const string SettingsFile = "ProjectSettings/EditorSettings.asset";
        private static readonly string Folder = Path.Combine("Library", "TestRunCleanup");
        private static readonly string ListFile = Path.Combine(Folder, "files.txt");
        private static readonly string Baseline = Path.Combine("Library", "TestRunCleanup-EditorSettings.asset");
        private static System.DateTime baselineWritten;
        private static bool settingsKept; // this run's settings copy is made

        // the bridge's TestRunStatus.IsRunning (internal), null without the bridge
        private static readonly PropertyInfo bridgeRunning = findBridgeStatus();

        static TestRunCleanup() {
            EditorSceneManager.sceneSaving += (scene, path) => {
                if (isBridgeRunning()) {
                    keepCopy(path, path);
                }
            };
            EditorApplication.update += update;
        }

        // copies go back once the run is over and play mode left; till then (and after) the settings are watched
        private static void update() {
            bool running = isBridgeRunning();
            if (!running && File.Exists(ListFile) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling) {
                restore();
            }
            watchSettings(running);
        }

        private static PropertyInfo findBridgeStatus() {
            System.Type status = System.Type.GetType("MCPForUnity.Editor.Services.TestRunStatus, MCPForUnity.Editor");
            return status != null ? status.GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Static) : null;
        }

        private static bool isBridgeRunning() {
            return bridgeRunning != null && (bool)bridgeRunning.GetValue(null);
        }

        // the settings file: its baseline while no run goes on; once one does (the bridge changed them before its
        // run shows), the baseline is what goes back
        private static void watchSettings(bool running) {
            if (running) {
                if (!settingsKept) {
                    keepCopy(SettingsFile, Baseline);
                    settingsKept = true;
                }
                return;
            }
            settingsKept = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(ListFile) || !File.Exists(SettingsFile)) {
                return;
            }
            System.DateTime written = File.GetLastWriteTimeUtc(SettingsFile);
            if (written != baselineWritten || !File.Exists(Baseline)) {
                File.Copy(SettingsFile, Baseline, true);
                baselineWritten = written;
            }
        }

        // what goes back to that file after the run, once: later writes in the same run don't replace it
        private static void keepCopy(string path, string source) {
            if (string.IsNullOrEmpty(path) || !File.Exists(source)) {
                return;
            }
            List<string> files = copiedFiles();
            if (files.Contains(path)) {
                return;
            }
            Directory.CreateDirectory(Folder);
            File.Copy(source, copyOf(files.Count), true);
            files.Add(path);
            File.WriteAllLines(ListFile, files);
        }

        private static List<string> copiedFiles() {
            return File.Exists(ListFile) ? new List<string>(File.ReadAllLines(ListFile)) : new List<string>();
        }

        private static string copyOf(int index) {
            return Path.Combine(Folder, index + ".copy");
        }

        private static void restore() {
            List<string> files = copiedFiles();
            if (files.Count == 0) {
                return;
            }
            List<int> changed = new List<int>();
            bool sceneChanged = false;
            for (int i = 0; i < files.Count; i++) {
                if (!sameBytes(copyOf(i), files[i])) {
                    changed.Add(i);
                    sceneChanged |= files[i] != SettingsFile;
                }
            }

            // scenes go back with none of them open
            SceneSetup[] open = EditorSceneManager.GetSceneManagerSetup();
            if (sceneChanged) {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            foreach (int i in changed) {
                File.Copy(copyOf(i), files[i], true);
            }
            if (changed.Count > 0) {
                AssetDatabase.Refresh();
            }
            if (sceneChanged && open.Length > 0) {
                EditorSceneManager.RestoreSceneManagerSetup(open);
            }
            Directory.Delete(Folder, true);

            if (changed.Count > 0) {
                List<string> names = new List<string>();
                foreach (int i in changed) {
                    names.Add(files[i]);
                }
                Debug.Log("[TestRunCleanup] Put back what the test run changed: " + string.Join(", ", names));
            }
        }

        private static bool sameBytes(string a, string b) {
            if (!File.Exists(a) || !File.Exists(b)) {
                return false;
            }
            byte[] x = File.ReadAllBytes(a);
            byte[] y = File.ReadAllBytes(b);
            if (x.Length != y.Length) {
                return false;
            }
            for (int i = 0; i < x.Length; i++) {
                if (x[i] != y[i]) {
                    return false;
                }
            }
            return true;
        }
    }
}
