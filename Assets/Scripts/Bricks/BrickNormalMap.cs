using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// A brick model's own normal map (its bevelled edges, from Mecabricks models). Brick colours are
    /// shared materials (see BrickColorPalette), so it goes on the renderer as a property: the palette
    /// materials have normal mapping on, with a flat normal map that this replaces.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class BrickNormalMap : MonoBehaviour
    {
        private static readonly int NormalMapProperty = Shader.PropertyToID("_NormalMap");

        public Texture2D normalMap;

        private void Awake() {
            Apply();
        }

        public void Apply() {
            if (normalMap == null) {
                return;
            }
            Renderer meshRenderer = GetComponent<Renderer>();
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(properties);
            properties.SetTexture(NormalMapProperty, normalMap);
            meshRenderer.SetPropertyBlock(properties);
        }
    }
}
