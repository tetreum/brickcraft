using Brickcraft.Network;
using Mirror;
using UnityEngine;

// test scene brick that gives 100 items to the players walking into it
public class BlockAdderTest : MonoBehaviour
{
    public int item;

    // only the server can give items, and it sees every player
    private void OnTriggerEnter(Collider other) {
        if (!NetworkServer.active) {
            return;
        }
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();

        if (inventory != null) {
            inventory.ServerAdd(item, 100);
        }
    }
}
