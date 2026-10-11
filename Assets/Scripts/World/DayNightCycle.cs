using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Brickcraft.World
{
    /// <summary>
    /// Days and nights, on every peer, from the game's time (WorldTime): the scene's sun (its directional
    /// light named "Sun") goes round, rising at 6 and setting at 18, and the moon (a copy of it, drawn in the
    /// sky with Resources/World/moon_disc) goes opposite. The physically based sky makes dawns, dusks and
    /// nights out of where the sun is; at night its stars (its space emission texture) turn with them.
    /// Added to the world by WorldBehaviour.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        /// <summary>Degrees around Y of the sun's path: where it rises.</summary>
        public float azimuth = 35;
        /// <summary>Lux of the full moon.</summary>
        public float moonIntensity = 6;
        public Color moonColor = new Color(0.72f, 0.8f, 1f);
        /// <summary>How big the moon looks, in degrees (the real one is half a degree).</summary>
        public float moonSize = 4;
        /// <summary>
        /// How bright its face is drawn: the sky draws it as bright as its light, which washes it out
        /// into a white blob; dimmer, its seas and craters show.
        /// </summary>
        public float moonFaceBrightness = 0.03f;

        private Light sun;
        private HDAdditionalLightData sunData;
        private float sunIntensity;
        private Light moon;
        private HDAdditionalLightData moonData;
        private PhysicallyBasedSky sky;

        private void Start() {
            sun = findSun();
            if (sun == null) {
                Debug.LogWarning("No directional light named Sun: no days and nights");
                enabled = false;
                return;
            }
            sunData = sun.GetComponent<HDAdditionalLightData>();
            sunIntensity = sunData.intensity;

            // the moon: the sun's settings (its sky, its units), dimmer, bluish, no shadows, with its face
            GameObject moonObject = Instantiate(sun.gameObject, sun.transform.parent);
            moonObject.name = "Moon";
            moon = moonObject.GetComponent<Light>();
            moonData = moonObject.GetComponent<HDAdditionalLightData>();
            moon.shadows = LightShadows.None;
            moonData.color = moonColor;
            moonData.angularDiameter = moonSize;
            moonData.surfaceTexture = Resources.Load<Texture2D>("World/moon_disc");
            moonData.surfaceTint = new Color(moonFaceBrightness, moonFaceBrightness, moonFaceBrightness);
            moonData.flareSize = 0;
            moonData.intensity = moonIntensity;

            Volume volume = findSkyVolume();
            if (volume != null) {
                volume.profile.TryGet(out sky); // its own copy, changed below
            }
        }

        private static Light findSun() {
            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional) {
                return RenderSettings.sun;
            }
            foreach (Light light in FindObjectsOfType<Light>()) {
                if (light.type == LightType.Directional && light.name == "Sun") {
                    return light;
                }
            }
            return null;
        }

        private static Volume findSkyVolume() {
            foreach (Volume volume in FindObjectsOfType<Volume>()) {
                if (volume.isGlobal && volume.sharedProfile != null && volume.sharedProfile.Has<PhysicallyBasedSky>()) {
                    return volume;
                }
            }
            return null;
        }

        private void Update() {
            // 0 at sunrise, 90 at noon, 180 at sunset
            float angle = (WorldTime.TimeOfDay - 0.25f) * 360f;
            Quaternion path = Quaternion.Euler(0, azimuth, 0);
            sun.transform.rotation = path * Quaternion.Euler(angle, 0, 0);
            moon.transform.rotation = path * Quaternion.Euler(angle + 180, 0, 0);

            // how high each is: they light the world while they're up, fading at the horizon
            float sunHeight = Mathf.Sin(angle * Mathf.Deg2Rad);
            float sunLight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.1f, 0.08f, sunHeight));
            float moonLight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.05f, 0.12f, -sunHeight));
            sun.enabled = sunLight > 0;
            sunData.intensity = sunIntensity * sunLight;
            moon.enabled = moonLight > 0;
            moonData.intensity = moonIntensity * moonLight;

            if (sky != null) {
                // the stars turn with the sky
                sky.spaceRotation.overrideState = true;
                sky.spaceRotation.value = new Vector3(angle, azimuth, 0);
            }
        }

        private void OnDestroy() {
            if (moon != null) {
                Destroy(moon.gameObject);
            }
        }
    }
}
