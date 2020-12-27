using UnityEngine;
using Brickcraft.UI;

namespace Brickcraft
{
    public class BrickCollisionDetector : MonoBehaviour
    {
        public static BrickCollisionDetector Instance;

        [HideInInspector]
        public int currentBrickType;

        private Material m;
        public Shader originalShader;
        private bool isColliding = false;
        private Vector3 lastPos;
        private Vector3 currentStud;
        private Transform latestStudGrid;
        private Vector2Int latestStud;

        private void Awake() {
            Instance = this;

            gameObject.name = "Previewer";
			// lower it's scale, so we don't trigger false collision positives
			// with nearby bricks
            gameObject.transform.localScale = new Vector3(0.999f, 0.999f, 0.999f);

            // ignore raycasts
            int ignoreRayCastLayer = (int)Game.Layers.IgnoreRaycast;
            gameObject.layer = ignoreRayCastLayer;

            foreach (Transform trans in gameObject.GetComponentsInChildren<Transform>()) {
                trans.gameObject.layer = ignoreRayCastLayer;
            }

            m = GetComponent<Renderer>().material;
            originalShader = m.shader;
            m.shader = Game.Instance.transparentShader;

            Collider[] colliders = GetComponents<Collider>();

            foreach (Collider collider in colliders) {
                collider.isTrigger = true;
            }

            Rigidbody rigid = gameObject.AddComponent<Rigidbody>();
            rigid.isKinematic = true;
            setVisible(false);
        }

        private void Update() {
            if (PlayerPanel.Instance.selectedItem == null || 
                PlayerPanel.Instance.selectedItem.item.type != Item.Type.Brick ||
                isColliding) {
                return;
            }

            if (Input.GetAxis("Mouse ScrollWheel") != 0f && lastPos != Vector3.zero) {
                Vector3 rot;
                if (latestStudGrid.rotation == Quaternion.identity) {
                    rot = Input.GetAxis("Mouse ScrollWheel") > 0f ? Vector3.up : Vector3.down;
                } else {
                    rot = Input.GetAxis("Mouse ScrollWheel") > 0f ? Vector3.forward : Vector3.back;
                }
                transform.RotateAround(currentStud, rot, 90);
            }
            if (Input.GetMouseButtonDown(1)) {
                Server.Instance.spawnBrick(PlayerPanel.Instance.selectedItem.item, transform.position, transform.rotation);

                Player.Instance.removeItem(new UserItem() {
                    id = PlayerPanel.Instance.selectedItem.id,
                    health = PlayerPanel.Instance.selectedItem.health,
                    quantity = 1
                });

                resetVars();
            }
        }

        private void resetVars() {
            latestStudGrid = null;
            latestStud = Vector2Int.zero;
        }

        void OnTriggerStay(Collider other) {
            if (other.name.StartsWith("GridStud")) {
                return;
            }

            // prevent resetting those vars each frame
            if (isColliding) {
                return;
            }

            isColliding = true;
            setVisible(false);
        }

        void OnTriggerExit(Collider other) {
            isColliding = false;
            setVisible(true);
        }

        private Vector2Int hitPointToStud(RaycastHit hit) {
            // time to understand at which Stud is he looking at

            // get grid dimensions
            string[] tmp = hit.collider.name.Replace("GridStud ", "").Split('x');
            Vector2Int dimensions = new Vector2Int(int.Parse(tmp[0]), int.Parse(tmp[1]));
            Vector2Int selectedStud = new Vector2Int();

            // 1x1 are easy xD
            if (dimensions.x == 1 && dimensions.y == 1) {
                return selectedStud;
            }

            // convert world coords to local ones
            var localHitpoint = hit.collider.transform.InverseTransformPoint(hit.point);

            // since localHitpoint is based on the center of the object, we need to sum half
            // of it's size
            localHitpoint.x += (dimensions.x * Server.studSize) / 2;
            localHitpoint.z += (dimensions.y * Server.studSize) / 2;

            for (int i = 1; i < (dimensions.x + 1); i++) {
                if (localHitpoint.x < (i * Server.studSize)) {
                    selectedStud.x = (i - 1);
                    break;
                }
            }
            for (int i = 1; i < (dimensions.y + 1); i++) {
                if (localHitpoint.z < (i * Server.studSize)) {
                    selectedStud.y = (i - 1);
                    break;
                }
            }

            return selectedStud;
        }

        public void lookingAtStud(RaycastHit hit) {
            // player is not holding a brick
            if (PlayerPanel.Instance.selectedItem == null || PlayerPanel.Instance.selectedItem.item.type != Item.Type.Brick) {
                return;
            }

            var stud = hitPointToStud(hit);

            if (hit.transform == latestStudGrid && stud == latestStud) {
                return;
            }

            GameObject brickObj = hit.collider.transform.parent.gameObject;
            BrickModel selectedBrickModel = PlayerPanel.Instance.selectedItem.item.brickModel;

            latestStudGrid = hit.transform;
            latestStud = stud;
            Vector3 studPos = hit.collider.transform.TransformPoint(new Vector3(stud.x, 0, stud.y));
            currentStud = studPos;
            currentStud.x -= (Server.studSize / 2);
            currentStud.y -= (Server.studSize / 2);

            if (!Server.bricks.ContainsKey(brickObj.name)) {
                Debug.LogError("Brick not found in server list " + brickObj.name);
                return;
            }
            Brick brick = Server.bricks[brickObj.name];

            Vector3 pos;

            // same model with all studs available, just put it over
            if (brick.itemId == selectedBrickModel.type ||
                brick.model.studs.Count == selectedBrickModel.studs.Count) {
                pos = brickObj.transform.position;
                pos.y += brick.model.heightInPlates * Server.plateHeight;
                pos.y += 0.001f; // to make sure they don't collide, so we don't get a false isColliding=true
                move(pos, Quaternion.identity);
                return;
            }

            Quaternion rotation = latestStudGrid.rotation;

            pos = studPos;

            // i have no idea of what i'm doing here, but works in order to adjust brick's position
            // please, someone refactor this
            if (rotation == Quaternion.identity) {
                pos.z -= (Server.studSize / 2);
                pos.x -= Server.studSize;
            } else {
                pos.x -= Server.studSize;
                pos.y += Server.studSize;
            }

            move(pos, rotation);
        }

        public void move(Vector3 pos, Quaternion rotation) {
            if (lastPos == pos) {
                return;
            }
            transform.position = pos;
            transform.rotation = rotation;
            setVisible(true);
            lastPos = pos;
        }

        private void setVisible (bool isVisible) {
            GetComponent<MeshRenderer>().enabled = isVisible;
        }
    }
}
