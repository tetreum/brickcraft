using UnityEngine;

public class CameraController : MonoBehaviour
{

    private static float movementSpeed = 1.0f;

    private void Start() {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update() {
        if (!enabled) {
            return;
        }
        movementSpeed = Mathf.Max(movementSpeed += Brickcraft.GameInput.ScrollDelta * 0.1f, 0.0f);
        Vector2 move = Brickcraft.GameInput.GetMovement();
        if (move.y != 0) {
            transform.Translate(Vector3.forward * movementSpeed * move.y);
        }
        if (move.x != 0) {
            transform.Translate(Vector3.right * movementSpeed * move.x);
        }
        Vector2 look = Brickcraft.GameInput.LookDelta;
        transform.eulerAngles += new Vector3(-look.y, look.x, 0);
    }
}