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
/// preview scene of its own (nothing of the open scene shows). Each icon is rendered twice, over black
/// and over white: how much the background shows through gives its transparency, so edges are smooth
/// and transparent bricks see-through.
/// </summary>
public static class BrickIconRenderer
{
    public const int IconSize = 100;
    // rendered bigger and scaled down, for smooth edges
    private const int Supersampling = 4;
    private static readonly Quaternion CameraRotation = Quaternion.Euler(45, 0, 0);
    private const string PalettePath = "Assets/Resources/" + BrickColorPalette.ResourceName + ".asset";
    // lighting of the icons, in HDRP's physical units
    private const float SunLux = 3f;
    private const float FillLux = 1f;
    private const float Exposure = 0f;

    /// <summary>Icons for the brick items that have none. Items with a world block are left out: their icon is their block's texture.</summary>
    [MenuItem("Brickcraft/Generate missing item icons")]
    public static void GenerateMissing() {
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
    public static int GenerateForModel(int brickModel) {
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

    // the icon of the item in the folder, if it's a brick (of that model, if given) with a prefab
    private static bool generate(string folder, int? onlyModel) {
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
        if ((string)info["type"] != null && (string)info["type"] != "brick") {
            return false;
        }
        int model = (int?)info["brickModel"] ?? 3003;
        if (onlyModel.HasValue && model != onlyModel.Value) {
            return false;
        }
        GameObject prefab = findPrefab(model);
        if (prefab == null) {
            return false;
        }
        Material material = colorMaterial((int?)info["color"]);
        Texture2D icon = Render(prefab, material);
        File.WriteAllBytes(Path.Combine(folder, ItemDatabase.IconFile), icon.EncodeToPNG());
        Object.DestroyImmediate(icon);
        return true;
    }

    private static GameObject findPrefab(int model) {
        string path = "Assets/Models/Bricks/" + model + "/" + model + ".prefab";
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
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
    public static Texture2D Render(GameObject prefab, Material material) {
        Scene scene = EditorSceneManager.NewPreviewScene();
        int size = IconSize * Supersampling;
        RenderTexture target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        try {
            GameObject brick = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            brick.transform.position = Vector3.zero;
            foreach (MeshRenderer meshRenderer in brick.GetComponentsInChildren<MeshRenderer>()) {
                if (material != null) {
                    meshRenderer.sharedMaterial = material;
                }
            }
            // what the game applies at runtime (Awake doesn't run in the editor)
            foreach (BrickNormalMap normalMap in brick.GetComponentsInChildren<BrickNormalMap>()) {
                normalMap.Apply();
            }
            Bounds bounds = brick.GetComponentInChildren<MeshRenderer>().bounds;

            GameObject sun = new GameObject("Sun", typeof(Light));
            SceneManager.MoveGameObjectToScene(sun, scene);
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            Light light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            HDAdditionalLightData lightData = sun.AddComponent<HDAdditionalLightData>();
            lightData.SetIntensity(SunLux, LightUnit.Lux);

            // there's no sky in the preview scene: a weaker light from the other side keeps shadows from being black
            GameObject fill = new GameObject("Fill", typeof(Light));
            SceneManager.MoveGameObjectToScene(fill, scene);
            fill.transform.rotation = Quaternion.Euler(20, 150, 0);
            fill.GetComponent<Light>().type = LightType.Directional;
            fill.AddComponent<HDAdditionalLightData>().SetIntensity(FillLux, LightUnit.Lux);

            // a fixed exposure and some ambient light, whatever the project's volumes say
            GameObject volumeObject = new GameObject("Volume", typeof(Volume));
            SceneManager.MoveGameObjectToScene(volumeObject, scene);
            Volume volume = volumeObject.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000;
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Exposure exposure = profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(Exposure);
            // no sky: the project's (daylight) would light it far more than these lights
            VisualEnvironment environment = profile.Add<VisualEnvironment>(true);
            environment.skyType.Override(0);
            volume.sharedProfile = profile;

            GameObject cameraObject = new GameObject("IconCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.targetTexture = target;
            camera.fieldOfView = 30;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100;
            HDAdditionalCameraData cameraData = cameraObject.AddComponent<HDAdditionalCameraData>();
            cameraData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            cameraData.volumeLayerMask = ~0;
            // no post-processing: bloom and the like would tint the background, which has to stay black or white
            cameraData.customRenderingSettings = true;
            cameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
            cameraData.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, false);

            // framed like IconGenerator: from the front, looking down
            camera.transform.rotation = CameraRotation;
            float objectSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float view = 2f * Mathf.Tan(0.5f * Mathf.Deg2Rad * camera.fieldOfView);
            float distance = objectSize / view + 0.5f * objectSize;
            camera.transform.position = bounds.center - distance * camera.transform.forward;

            Color32[] overBlack = capture(camera, cameraData, target, Color.black);
            Color32[] overWhite = capture(camera, cameraData, target, Color.white);

            Object.DestroyImmediate(profile);
            return combine(overBlack, overWhite, size);
        } finally {
            EditorSceneManager.ClosePreviewScene(scene);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    private static Color32[] capture(Camera camera, HDAdditionalCameraData cameraData, RenderTexture target, Color background) {
        cameraData.backgroundColorHDR = background;
        camera.Render();
        // HDRP renders the frame after: a second time so this one has the background
        camera.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D read = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        RenderTexture.active = previous;
        Color32[] pixels = read.GetPixels32();
        Object.DestroyImmediate(read);
        return pixels;
    }

    // alpha is how little the background changes a pixel; its colour, the one over black undone of the black
    private static Texture2D combine(Color32[] overBlack, Color32[] overWhite, int size) {
        Color[] full = new Color[overBlack.Length];
        for (int i = 0; i < full.Length; i++) {
            Color black = overBlack[i], white = overWhite[i];
            float alpha = 1 - Mathf.Max(white.r - black.r, white.g - black.g, white.b - black.b);
            alpha = Mathf.Clamp01(alpha);
            full[i] = alpha > 0.001f ? new Color(black.r / alpha, black.g / alpha, black.b / alpha, alpha) : Color.clear;
        }

        // scaled down: each icon pixel is the average of its square, weighted by alpha
        Texture2D icon = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[IconSize * IconSize];
        for (int y = 0; y < IconSize; y++) {
            for (int x = 0; x < IconSize; x++) {
                Color sum = Color.clear;
                float alphaSum = 0;
                for (int sy = 0; sy < Supersampling; sy++) {
                    for (int sx = 0; sx < Supersampling; sx++) {
                        Color c = full[(y * Supersampling + sy) * size + x * Supersampling + sx];
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, 0);
                        alphaSum += c.a;
                    }
                }
                float alpha = alphaSum / (Supersampling * Supersampling);
                pixels[y * IconSize + x] = alphaSum > 0 ? new Color(sum.r / alphaSum, sum.g / alphaSum, sum.b / alphaSum, alpha) : Color.clear;
            }
        }
        icon.SetPixels(pixels);
        icon.Apply();
        return icon;
    }
}
