using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerPanel : MonoBehaviour
{
    public static PlayerPanel Instance;

    public InventorySlot[] fastInventorySlots;

    void Awake()
    {
        Instance = this;
    }

    private void OnEnable() {
        reload();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab)) {
            switchSelectedItem();
        }
    }

    public void reload() {
        if (Player.Instance == null) {
            return;
        }
        Dictionary<int, UserItem> inventory = Player.Instance.getInventoryBySlot();
        InventorySlot slot;
        int slotId;

        for (int i = 0; i < fastInventorySlots.Length; i++) {
            slot = fastInventorySlots[i];
            slotId = int.Parse(slot.originalParent == null ? slot.transform.parent.name : slot.originalParent.name);

            if (!inventory.ContainsKey(slotId)) {
                slot.setVisible(false);
                continue;
            }
            slot.setVisible(true);
            slot.GetComponent<RawImage>().texture = inventory[slotId].item.icon;
            slot.quantity.text = inventory[slotId].quantity.ToString();
        }
    }

    void switchSelectedItem () {

    }
}
