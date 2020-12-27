using Brickcraft;
using UnityEngine;

public class BlockAdderTest : MonoBehaviour
{
    public int item;

    private void OnTriggerEnter(Collider other) {
        Player.Instance.addItem(new UserItem() {
            id = item,
            quantity = 100,
        });
    }
}
