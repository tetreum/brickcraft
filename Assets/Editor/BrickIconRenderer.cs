using System.Collections.Generic;
using System.IO;
using Brickcraft;
using Brickcraft.Bricks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Renders brick items' icons (StreamingAssets/Items/[item]/icon.png) in the editor, without playing:
/// the brick's prefab in its item's colour, seen from the front and above like the other icons, in a
/// preview scene of its own (nothing of the open scene shows), rendered like the game's (see ModelIconRenderer).
/// </summary>
public static class BrickIconRenderer
{
    public const int IconSize = ModelIconRenderer.IconSize;
    private const string PalettePath = "Assets/Resources/" + BrickColorPalette.ResourceName + ".asset";

    /// <summary>Icons for the brick items that have none. Items with a world block are left out: their icon is their block's texture.</summary>
    [MenuItem("Brickcraft/Generate missing item icons")]
    public static void GenerateMissing() {
        loadModels();
        int made = 0;
        foreach (string folder in itemFolders()) {
            if (!File.Exists(Path.Combine(folder, ItemDatabase.IconFile)) && !hasBlock(folder) && generate(folder, null)) {
                made++;
            }
        }
        AssetDatabase.Refresh();
        Debug.Log("Generated " + made + " item icons");
    }

    private static bool hasBlock(string folder) {
        try {
            return JObject.Parse(File.ReadAllText(Path.Combine(folder, ItemDatabase.InfoFile)))["block"] != null;
        } catch (System.Exception) {
            return false;
        }
    }

    /// <summary>Renders the icon of every item whose brick is that model (overwriting theirs). Returns how many.</summary>
    public static int GenerateForModel(string brickModel) {
        loadModels();
        int made = 0;
        foreach (string folder in itemFolders()) {
            if (generate(folder, brickModel)) {
                made++;
            }
        }
        return made;
    }

    private static IEnumerable<string> itemFolders() {
        string items = Path.Combine(Application.streamingAssetsPath, ItemDatabase.Folder);
        foreach (string folder in Directory.GetDirectories(items)) {
            yield return folder;
        }
        // mods' items too
        string mods = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Mods"));
        if (Directory.Exists(mods)) {
            foreach (string mod in Directory.GetDirectories(mods)) {
                string modItems = Path.Combine(mod, "items");
                if (Directory.Exists(modItems)) {
                    foreach (string folder in Directory.GetDirectories(modItems)) {
                        yield return folder;
                    }
                }
            }
        }
    }

    // the game's models and the installed mods' ones, as the game finds them (see BrickModels)
    private static void loadModels() {
        BrickColorPalette.Load();
        BrickModels.Reload(Brickcraft.Mods.ModDatabase.Installed()); // files may have changed since
    }

    // the mod of an item folder (Mods/[mod]/items/[item]), null for the game's
    private static Brickcraft.Mods.ModInfo modOf(string folder) {
        string items = Path.GetDirectoryName(folder);
        if (Path.GetFileName(items) != "items") {
            return null;
        }
        return Brickcraft.Mods.ModDatabase.Find(Path.GetFileName(Path.GetDirectoryName(items)));
    }

    // the icon of the item in the folder, if it has a model (that one, if given) with a prefab
    private static bool generate(string folder, string onlyModel) {
        string infoFile = Path.Combine(folder, ItemDatabase.InfoFile);
        if (!File.Exists(infoFile)) {
            return false;
        }
        JObject info;
        try {
            info = JObject.Parse(File.ReadAllText(infoFile));
        } catch (System.Exception) {
            return false;
        }
        // weapons and the like are long and thin: tilted, they fill the icon better
        string type = (string)info["type"] ?? "brick";
        Quaternion tilt = type == "weapon" || type == "material" ? Quaternion.Euler(0, 0, -45) : Quaternion.identity;
        string model = BrickModels.Resolve((string)info["brickModel"] ?? "3003", modOf(folder));
        if (onlyModel != null && model != onlyModel) {
            return false;
        }
        BrickModel brickModel = BrickModels.Get(model);
        if (brickModel == null || brickModel.prefab == null) {
            return false;
        }
        GameObject prefab = brickModel.prefab;
        Material material = colorMaterial((int?)info["color"]);
        Texture2D icon = Render(prefab, material, tilt);
        File.WriteAllBytes(Path.Combine(folder, ItemDatabase.IconFile), icon.EncodeToPNG());
        Object.DestroyImmediate(icon);
        return true;
    }

    private static Material colorMaterial(int? color) {
        if (!color.HasValue) {
            return null; // the prefab's own
        }
        BrickColorPalette palette = AssetDatabase.LoadAssetAtPath<BrickColorPalette>(PalettePath);
        if (palette == null) {
            return null;
        }
        foreach (BrickColor brickColor in palette.colors) {
            if (brickColor.id == color.Value) {
                return brickColor.material;
            }
        }
        return null;
    }

    /// <summary>An icon of the prefab (with that material, or its own if null), transparent around it.</summary>
    public static Texture2D Render(GameObject prefab, Material material, Quaternion tilt = default(Quaternion)) {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try {
            // the game's custom models are prefab assets, mods' are built from their files or come from AssetBundles (see BrickModels)
            GameObject brick;
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(prefab))) { // bundles' prefabs are assets without a path
                brick = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            } else {
                brick = Object.Instantiate(prefab);
                brick.hideFlags = HideFlags.None;
                brick.SetActive(true);
                SceneManager.MoveGameObjectToScene(brick, scene);
            }
            brick.transform.position = Vector3.zero;
            if (tilt != default(Quaternion)) {
                brick.transform.rotation = tilt * brick.transform.rotation;
            }
            BrickModels.ApplyColor(brick, material);
            // what the game applies at runtime (Awake doesn't run in the editor)
            foreach (BrickNormalMap normalMap in brick.GetComponentsInChildren<BrickNormalMap>()) {
                normalMap.Apply();
            }
            return ModelIconRenderer.RenderInScene(scene, brick);
        } finally {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
