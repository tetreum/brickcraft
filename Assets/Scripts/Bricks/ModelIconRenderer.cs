using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// Renders a brick's icon (IconSize square, transparent around it): the brick alone in a scene of its own,
    /// seen from the front and above, lit by lights of its own with a fixed exposure and no sky, whatever the
    /// world's time and volumes. It's rendered twice, over black and over white: how much the background shows
    /// through gives its transparency, so edges are smooth and transparent bricks see-through.
    /// The editor renders items' icons with it (BrickIconRenderer), the game creations' (see CreationModel); its
    /// studio (lights, volume, camera, capture) renders building instructions too (Instructions.StepRenderer).
    /// </summary>
    /// <summary>
    /// A scene of its own with the icons' light and a camera rendering to a texture of that size, which sees only the
    /// studio's layer (ModelIconRenderer.StudioLayer): what's put in it (on that layer) is captured over a background.
    /// Building instructions render their steps and pages in one (see Instructions.StepRenderer, UI.InstructionBook).
    /// </summary>
    public sealed class Studio : System.IDisposable
    {
        public readonly Scene scene;
        public readonly Camera camera;
        private readonly HDAdditionalCameraData cameraData;
        private readonly RenderTexture target;
        private readonly VolumeProfile profile;

        public Studio(string name, int width, int height) {
            scene = SceneManager.CreateScene(name + " " + System.Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.None));
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            ModelIconRenderer.SetUpStudio(scene, profile);
            camera = ModelIconRenderer.MakeCamera(scene, target, out cameraData);
        }

        public int Width {
            get { return target.width; }
        }

        public int Height {
            get { return target.height; }
        }

        /// <summary>Puts an object in it, on its layer, so its camera sees it.</summary>
        public void Add(GameObject thing) {
            if (thing.transform.parent == null) {
                SceneManager.MoveGameObjectToScene(thing, scene);
            }
            ModelIconRenderer.SetLayer(thing, ModelIconRenderer.StudioLayer);
        }

        /// <summary>What its camera sees over that background (the other scenes' lights off meanwhile).</summary>
        public Color32[] Capture(Color background) {
            List<Light> others = ModelIconRenderer.DimOtherScenes(scene);
            try {
                return ModelIconRenderer.Capture(camera, cameraData, target, background);
            } finally {
                ModelIconRenderer.Restore(others);
            }
        }

        /// <summary>Gone: its camera and texture at once, its scene a moment later (what's in it should be destroyed first).</summary>
        public void Dispose() {
            if (camera != null) {
                Object.DestroyImmediate(camera.gameObject);
            }
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(profile);
            if (scene.isLoaded) {
                SceneManager.UnloadSceneAsync(scene);
            }
        }
    }

    public static class ModelIconRenderer
    {
        public const int IconSize = 100;
        // rendered bigger and scaled down, for smooth edges
        private const int Supersampling = 4;
        private static readonly Quaternion CameraRotation = Quaternion.Euler(45, 0, 0);
        // lighting of the icons, in HDRP's physical units
        private const float SunLux = 3f;
        private const float FillLux = 1f;
        private const float Exposure = 0f;
        // the icon's volume is on a layer of its own: the world's volumes (its fog...) reach every scene's cameras
        private const int VolumeLayer = 31;
        /// <summary>
        /// The layer of what the studio's camera sees: a scene's camera sees the other scenes too (the world's terrain
        /// where the model is), so it only sees this layer.
        /// </summary>
        public const int StudioLayer = 30;

        /// <summary>
        /// Renders an icon of a model (with its own materials) at runtime, in a scene made for it and gone after,
        /// that many pixels square. The model is copied: it's left as it was.
        /// </summary>
        public static Texture2D Render(GameObject model, int iconSize = IconSize) {
            Scene scene = SceneManager.CreateScene("Icon " + model.GetInstanceID(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            GameObject copy = null;
            try {
                copy = Object.Instantiate(model);
                copy.SetActive(true);
                SceneManager.MoveGameObjectToScene(copy, scene);
                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                return RenderInScene(scene, copy, iconSize);
            } finally {
                // gone at once: the scene goes later, and the next render's camera would see it meanwhile
                if (copy != null) {
                    Object.DestroyImmediate(copy);
                }
                SceneManager.UnloadSceneAsync(scene);
            }
        }

        /// <summary>Renders an icon of a brick already alone in a scene (an editor preview scene, or one of Render's).</summary>
        public static Texture2D RenderInScene(Scene scene, GameObject brick, int iconSize = IconSize) {
            SetLayer(brick, StudioLayer);
            int size = iconSize * Supersampling;
            RenderTexture target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            try {
                Bounds bounds = new Bounds(brick.transform.position, Vector3.zero);
                bool any = false;
                foreach (Renderer part in brick.GetComponentsInChildren<Renderer>()) {
                    if (!any) {
                        bounds = part.bounds;
                        any = true;
                    } else {
                        bounds.Encapsulate(part.bounds);
                    }
                }

                SetUpStudio(scene, profile);
                Camera camera = MakeCamera(scene, target, out HDAdditionalCameraData cameraData);
                camera.fieldOfView = 30;

                // from the front, looking down, fitting its largest side
                camera.transform.rotation = CameraRotation;
                float objectSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                float view = 2f * Mathf.Tan(0.5f * Mathf.Deg2Rad * camera.fieldOfView);
                float distance = objectSize / view + 0.5f * objectSize;
                camera.transform.position = bounds.center - distance * camera.transform.forward;

                List<Light> others = DimOtherScenes(scene);
                Color32[] overBlack, overWhite;
                try {
                    overBlack = Capture(camera, cameraData, target, Color.black);
                    overWhite = Capture(camera, cameraData, target, Color.white);
                } finally {
                    Restore(others);
                }
                // it isn't drawn again (a scene of the game's goes away a frame later)
                Object.DestroyImmediate(camera.gameObject);
                return Combine(overBlack, overWhite, iconSize, iconSize, Supersampling);
            } finally {
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(profile);
            }
        }

        /// <summary>
        /// The icons' light in a scene: a sun, a weaker fill from the other side (there's no sky), and a volume of its
        /// own with a fixed exposure, no sky and no fog (whatever the project's volumes say), its settings in profile.
        /// </summary>
        public static void SetUpStudio(Scene scene, VolumeProfile profile) {
            GameObject sun = new GameObject("Sun", typeof(Light));
            SceneManager.MoveGameObjectToScene(sun, scene);
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            sun.GetComponent<Light>().type = LightType.Directional;
            sun.AddComponent<HDAdditionalLightData>().SetIntensity(SunLux, LightUnit.Lux);

            // there's no sky: a weaker light from the other side keeps shadows from being black
            GameObject fill = new GameObject("Fill", typeof(Light));
            SceneManager.MoveGameObjectToScene(fill, scene);
            fill.transform.rotation = Quaternion.Euler(20, 150, 0);
            fill.GetComponent<Light>().type = LightType.Directional;
            fill.AddComponent<HDAdditionalLightData>().SetIntensity(FillLux, LightUnit.Lux);

            // a fixed exposure and some ambient light, whatever the project's volumes say
            GameObject volumeObject = new GameObject("Volume", typeof(Volume));
            volumeObject.layer = VolumeLayer;
            SceneManager.MoveGameObjectToScene(volumeObject, scene);
            Volume volume = volumeObject.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000;
            UnityEngine.Rendering.HighDefinition.Exposure exposure = profile.Add<UnityEngine.Rendering.HighDefinition.Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(Exposure);
            // no sky: the project's (daylight) would light it far more than these lights
            VisualEnvironment environment = profile.Add<VisualEnvironment>(true);
            environment.skyType.Override(0);
            Fog fog = profile.Add<Fog>(true);
            fog.enabled.Override(false);
            volume.sharedProfile = profile;
        }

        /// <summary>A camera of a scene rendering to target, with the studio's volume, a plain background and no post-processing.</summary>
        public static Camera MakeCamera(Scene scene, RenderTexture target, out HDAdditionalCameraData cameraData) {
            GameObject cameraObject = new GameObject("IconCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.targetTexture = target;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000;
            cameraData = cameraObject.AddComponent<HDAdditionalCameraData>();
            cameraData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            cameraData.volumeLayerMask = 1 << VolumeLayer;
            camera.cullingMask = 1 << StudioLayer;
            // no post-processing: bloom and the like would tint the background, which has to stay black or white
            cameraData.customRenderingSettings = true;
            cameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
            cameraData.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, false);
            return camera;
        }

        /// <summary>An object and everything in it on that layer.</summary>
        public static void SetLayer(GameObject root, int layer) {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) {
                t.gameObject.layer = layer;
            }
        }

        /// <summary>The other scenes' lights (the world's sun) light every scene: off while it's rendered (see Restore).</summary>
        public static List<Light> DimOtherScenes(Scene scene) {
            List<Light> others = new List<Light>();
            foreach (Light light in Object.FindObjectsOfType<Light>()) {
                if (light.enabled && light.gameObject.scene != scene) {
                    light.enabled = false;
                    others.Add(light);
                }
            }
            return others;
        }

        public static void Restore(List<Light> lights) {
            foreach (Light light in lights) {
                if (light != null) {
                    light.enabled = true;
                }
            }
        }

        /// <summary>What the camera sees over that background, its target's pixels.</summary>
        public static Color32[] Capture(Camera camera, HDAdditionalCameraData cameraData, RenderTexture target, Color background) {
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

        /// <summary>
        /// A transparent image (width x height) of what was captured over black and over white, that many times bigger:
        /// alpha is how little the background changes a pixel; its colour, the one over black undone of the black.
        /// </summary>
        public static Texture2D Combine(Color32[] overBlack, Color32[] overWhite, int width, int height, int supersampling) {
            int fullWidth = width * supersampling;
            Color[] full = new Color[overBlack.Length];
            for (int i = 0; i < full.Length; i++) {
                Color black = overBlack[i], white = overWhite[i];
                float alpha = 1 - Mathf.Max(white.r - black.r, white.g - black.g, white.b - black.b);
                alpha = Mathf.Clamp01(alpha);
                full[i] = alpha > 0.001f ? new Color(black.r / alpha, black.g / alpha, black.b / alpha, alpha) : Color.clear;
            }

            // scaled down: each pixel is the average of its square, weighted by alpha
            Texture2D image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    Color sum = Color.clear;
                    float alphaSum = 0;
                    for (int sy = 0; sy < supersampling; sy++) {
                        for (int sx = 0; sx < supersampling; sx++) {
                            Color c = full[(y * supersampling + sy) * fullWidth + x * supersampling + sx];
                            sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, 0);
                            alphaSum += c.a;
                        }
                    }
                    float alpha = alphaSum / (supersampling * supersampling);
                    pixels[y * width + x] = alphaSum > 0 ? new Color(sum.r / alphaSum, sum.g / alphaSum, sum.b / alphaSum, alpha) : Color.clear;
                }
            }
            image.SetPixels(pixels);
            image.Apply();
            return image;
        }
    }
}
