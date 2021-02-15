using UnityEngine;

public class CameraController : MonoBehaviour
{

    private static float movementSpeed = 1.0f;

    private void Start() {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update() {
        movementSpeed = Mathf.Max(movementSpeed += Input.GetAxis("Mouse ScrollWheel"), 0.0f);
        if (Input.GetAxis("Vertical") != 0) {
            transform.Translate(Vector3.forward * movementSpeed * Input.GetAxis("Vertical"));
        }
        if (Input.GetAxis("Horizontal") != 0) {
            transform.Translate(Vector3.right * movementSpeed * Input.GetAxis("Horizontal"));
        }
        transform.eulerAngles += new Vector3(-Input.GetAxis("Mouse Y"), Input.GetAxis("Mouse X"), 0);
    }
}