using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// The light of items that give it (a torch, see Item.light): a point light at the top of their model, where it's
    /// placed and in the hand (see Server.createBrickObject and HeldItems). No shadows: there can be many.
    /// </summary>
    public static class ItemLight
    {
        public const string Name = "ItemLight";
        // warm, like a flame
        private static readonly Color DefaultColor = new Color(1f, 0.72f, 0.4f);

        /// <summary>Gives the model its item's light, if it gives one.</summary>
        public static void Attach(GameObject model, Item item) {
            if (item == null || item.light == null || model == null) {
                return;
            }
            Bounds bounds = boundsOf(model);
            GameObject lightObject = new GameObject(Name);
            lightObject.transform.SetParent(model.transform, false);
            // a bit over its top, where its flame would be
            lightObject.transform.position = new Vector3(bounds.center.x, bounds.max.y + 0.05f, bounds.center.z);
            lightObject.layer = model.layer;

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            float[] rgb = item.light.color;
            light.color = rgb != null ? new Color(rgb[0], rgb[1], rgb[2]) : DefaultColor;
            light.range = item.light.range;
            HDAdditionalLightData data = lightObject.AddComponent<HDAdditionalLightData>();
            data.SetIntensity(item.light.intensity, LightUnit.Lumen);
            data.SetRange(item.light.range);
            data.EnableShadows(false);
        }

        private static Bounds boundsOf(GameObject model) {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) {
                return new Bounds(model.transform.position, Vector3.zero);
            }
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) {
                bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }
    }
}
