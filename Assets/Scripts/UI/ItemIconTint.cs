using Brickcraft.Bricks;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// Shows an item's icon in the colour of its stack: icons are rendered in the item's colour, and a shader
    /// (Resources/Shader/ItemIconTint) gives the pixels of that colour the stack's, keeping their shading. It's an
    /// approximation: what has a colour of its own close to the item's takes it too, and transparency doesn't show.
    /// Each icon gets its material the first time it's tinted, destroyed with it.
    /// </summary>
    public class ItemIconTint : MonoBehaviour
    {
        private const string ShaderResource = "Shader/ItemIconTint";
        private static Shader shader;
        private static readonly int FromId = Shader.PropertyToID("_From");
        private static readonly int ToId = Shader.PropertyToID("_To");

        private Material material;

        /// <summary>Tints the icon to the colour of the item's stack, or shows it as it is (item null, or in its own colour).</summary>
        public static void Set(Graphic icon, Item item, int color) {
            BrickColor rendered = item != null && item.IsColorable && color != item.IconColor ? BrickColorPalette.Get(item.IconColor) : null;
            BrickColor shown = rendered != null ? BrickColorPalette.Get(color) : null;
            ItemIconTint tint = icon.GetComponent<ItemIconTint>();

            if (shown == null) {
                if (tint != null) {
                    icon.material = null;
                }
                return;
            }
            if (tint == null) {
                if (shader == null) {
                    shader = Resources.Load<Shader>(ShaderResource);
                    if (shader == null) {
                        Debug.LogWarning("There's no " + ShaderResource + " shader in Resources: icons keep their colour");
                        return;
                    }
                }
                tint = icon.gameObject.AddComponent<ItemIconTint>();
                tint.material = new Material(shader) { name = "ItemIconTint" };
            }
            tint.material.SetColor(FromId, rendered.Color32);
            tint.material.SetColor(ToId, shown.Color32);
            icon.material = tint.material;
            // a material's colours changed: the UI draws it again
            icon.SetMaterialDirty();
        }

        private void OnDestroy() {
            if (material != null) {
                Destroy(material);
            }
        }
    }
}
