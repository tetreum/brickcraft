using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.HighDefinition;

/* Based on 
https://answers.unity.com/questions/1601619/access-or-remove-background-color-of-assetpreview.html
https://forum.unity.com/threads/fit-object-exactly-into-perspective-cameras-field-of-view-focus-the-object.496472/
*/
namespace Brickcraft
{
    public class IconGenerator : MonoBehaviour
    {
        private RenderTexture renderTexture;
        private Texture2D texture;
        private Rect rectReadPicture;
        private int iconSize = 100;
        private Quaternion cameraRotation = Quaternion.Euler(new Vector3(45f, 0, 0));
        private Color32 greenChroma = new Color32(0, 255, 0, 255);
        private Color32 blueChroma = new Color32(0, 0, 255, 255);
        private HDAdditionalCameraData cam;

        private void Awake() {
            cam = Camera.main.GetComponent<HDAdditionalCameraData>();
        }

        private void Start() {
            renderTexture = new RenderTexture(iconSize, iconSize, 24);
            RenderTexture.active = renderTexture;
            Camera.main.targetTexture = renderTexture;

            texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false);
            rectReadPicture = new Rect(0, 0, renderTexture.width, renderTexture.height);

            foreach (Item item in Server.items.Values) {
                if (item.type != Item.Type.Brick) {
                    continue;
                }
                generateIcon(item);
            }
        }

        private bool generateIcon(Item item, bool forceWrite = false) {
            string iconPath = "/Resources/Textures/Bricks/" + item.id + ".png";

            if (!forceWrite && System.IO.File.Exists(Application.dataPath + iconPath)) {
                return false;
            }

            string itemName = item.name.ToLower();

            if (itemName.Contains("water") || itemName.Contains("glass")) {
                cam.backgroundColorHDR = blueChroma;
            } else {
                cam.backgroundColorHDR = greenChroma;
            }

            GameObject obj = Server.Instance.spawnBrick(item, Vector3.zero, Quaternion.identity, true).gameObject;
            MeshRenderer objRenderer = obj.GetComponent<MeshRenderer>();
            Camera.main.transform.rotation = cameraRotation;

            Vector3 objectSizes = objRenderer.bounds.max - objRenderer.bounds.min;
            float objectSize = Mathf.Max(objectSizes.x, objectSizes.y, objectSizes.z);
            float cameraView = 2.0f * Mathf.Tan(0.5f * Mathf.Deg2Rad * Camera.main.fieldOfView); // Visible height 1 meter in front
            float distance = 1f * objectSize / cameraView; // Combined wanted distance from the object
            distance += 0.5f * objectSize; // Estimated offset from the center to the outside of the object
            Camera.main.transform.position = objRenderer.bounds.center - distance * Camera.main.transform.forward;

            // force render to make sure the new brick appears
            Camera.main.Render();

            iconPath = "Assets" + iconPath;
            texture.ReadPixels(rectReadPicture, 0, 0);
            Color32[] colors = texture.GetPixels32();
            int i = 0;
            Color32 transparent = colors[i];
            
            for (; i < colors.Length; i++) {
                if (colors[i].Equals(transparent)) {
                    colors[i] = new Color32();
                }
            }
            texture.SetPixels32(colors);
            
            byte[] bytes = texture.EncodeToPNG();
            System.IO.File.WriteAllBytes(iconPath, bytes);
            AssetDatabase.ImportAsset(iconPath);

            TextureImporter ti = (TextureImporter)TextureImporter.GetAtPath(iconPath);
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();

            DestroyImmediate(obj);
            return true;
        }
    }
}
