using UnityEngine;

namespace Brickcraft.UI
{
    /// <summary>
    /// Marks a grid of inventory slots (see InventorySlot) as showing a storage brick's slots (the storage panel's
    /// upper grid, see StoragePanel) instead of the player's inventory, so dragging between grids moves items
    /// between the two (Network.PlayerInventory.CmdStorageMove). Grids without one are the player's.
    /// </summary>
    public class SlotGroup : MonoBehaviour
    {
        public bool isStorage;

        /// <summary>Whether a slot cell (or anything in it) belongs to a storage grid.</summary>
        public static bool IsStorage(Transform cell) {
            SlotGroup group = cell != null ? cell.GetComponentInParent<SlotGroup>(true) : null;
            return group != null && group.isStorage;
        }
    }
}
