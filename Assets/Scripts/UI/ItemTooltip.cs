using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The name of the item under the pointer in an inventory, storage or crafting slot (see InventorySlot), with
    /// what weapons and armor do and how worn they are. It follows the pointer; made the first time it's needed.
    /// </summary>
    public class ItemTooltip : MonoBehaviour
    {
        private static ItemTooltip instance;
        private static readonly Vector2 Offset = new Vector2(18, -18);

        private Text title;
        private Text details;
        private RectTransform rect;
        private Canvas canvas;

        /// <summary>Shows that item's tooltip (health: how worn its stack is, see InventoryItem.health).</summary>
        public static void Show(Item item, int health) {
            if (item == null) {
                Hide();
                return;
            }
            if (instance == null) {
                if (Menu.Instance == null) {
                    return;
                }
                instance = create(Menu.Instance.transform);
            }
            instance.title.text = item.name;
            string about = describe(item, health);
            instance.details.text = about;
            instance.details.gameObject.SetActive(about.Length > 0);
            instance.transform.SetAsLastSibling();
            instance.gameObject.SetActive(true);
            instance.follow();
        }

        public static void Hide() {
            if (instance != null) {
                instance.gameObject.SetActive(false);
            }
        }

        // weapons' and armor's stats, empty for other items
        private static string describe(Item item, int health) {
            List<string> lines = new List<string>();
            if (item.type == Item.Type.Weapon) {
                lines.Add(item.IsRanged && Server.items.TryGetValue(item.ammo, out Item ammo)
                    ? "Damage " + item.damage + ", shoots " + ammo.name
                    : "Damage " + item.damage + ", " + item.attackSpeed.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " hits a second");
            }
            if (item.type == Item.Type.Armor) {
                lines.Add("Armor " + item.armor);
            }
            if (item.Wears) {
                lines.Add("Durability " + Mathf.Max(0, item.durability - health) + " / " + item.durability);
            }
            return string.Join("\n", lines);
        }

        private void LateUpdate() {
            follow();
        }

        // beside the pointer, kept on screen
        private void follow() {
            RectTransform parent = (RectTransform)transform.parent;
            Camera eye = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 pointer = Input.mousePosition;
            Vector2 size = rect.rect.size * (canvas != null ? canvas.scaleFactor : 1);
            Vector2 at = pointer + Offset;
            if (at.x + size.x > Screen.width) {
                at.x = pointer.x - Offset.x - size.x;
            }
            if (at.y - size.y < 0) {
                at.y = pointer.y - Offset.y + size.y;
            }
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, at, eye, out Vector2 local)) {
                rect.localPosition = local;
            }
        }

        private static ItemTooltip create(Transform root) {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject panel = new GameObject("ItemTooltip", typeof(RectTransform), typeof(Image), typeof(Outline),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(root, false);
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0, 1); // its top left at the pointer

            Image background = panel.GetComponent<Image>();
            background.color = new Color(0.07f, 0.1f, 0.14f, 0.95f);
            background.raycastTarget = false; // it never takes the pointer from the slots
            panel.GetComponent<Outline>().effectColor = new Color(0.22f, 0.29f, 0.38f);
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 7);
            layout.spacing = 2;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ItemTooltip tooltip = panel.AddComponent<ItemTooltip>();
            tooltip.rect = rect;
            tooltip.canvas = root.GetComponentInParent<Canvas>();
            tooltip.title = label(panel.transform, "Title", font, 18, FontStyle.Bold, Color.white);
            tooltip.details = label(panel.transform, "Details", font, 14, FontStyle.Normal, new Color(0.7f, 0.76f, 0.84f));
            panel.SetActive(false);
            return tooltip;
        }

        private static Text label(Transform parent, string name, Font font, int size, FontStyle style, Color color) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.raycastTarget = false;
            text.supportRichText = false; // names come from mods
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
