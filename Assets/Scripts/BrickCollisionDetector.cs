using UnityEngine;
using Brickcraft.UI;

namespace Brickcraft
{
    public class BrickCollisionDetector : MonoBehaviour
    {
        public static BrickCollisionDetector Instance;

        [HideInInspector]
        public static Transform pivot;

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

            int ignoreRayCastLayer = (int)Game.Layers.IgnoreRaycast;

            // We need to set a parent object so we can properly rotate the bricks
            // as well as place them using a stud position rather than it's center pos
            if (pivot == null) {
                pivot = new GameObject().transform;
                pivot.name = "Previewer";
                pivot.gameObject.layer = ignoreRayCastLayer;
            }
            transform.SetParent(pivot);
            pivot.position = new Vector3(0, 999, 0); // send it far away from player view while not in use

            // lower it's scale, so we don't trigger false collision positives
            // with nearby bricks
            transform.localScale = new Vector3(0.999f, 0.999f, 0.999f);
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
            setValid(false);
        }

        public void setCurrentBrickType(int brickType) {
            currentBrickType = brickType;
            transform.localPosition = Server.brickModels[currentBrickType].pivot;
        }

        private void Update() {
            if (PlayerPanel.Instance.selectedItem == null || 
                PlayerPanel.Instance.selectedItem.item.type != Item.Type.Brick) {
                return;
            }
            if (Input.GetAxis("Mouse ScrollWheel") != 0f && lastPos != Vector3.zero) {
                Vector3 rot;
                if (latestStudGrid.rotation == Quaternion.identity) {
                    rot = Input.GetAxis("Mouse ScrollWheel") > 0f ? Vector3.up : Vector3.down;
                } else {
                    rot = Input.GetAxis("Mouse ScrollWheel") > 0f ? Vector3.forward : Vector3.back;
                }
                pivot.RotateAround(currentStud, rot, 90);
            }
            if (Input.GetMouseButtonDown(1) && !isColliding) {
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
            // ignore collisions with studs
            if (other.name.StartsWith("GridStud")) {
                return;
            }

            // prevent resetting those vars each frame
            if (isColliding) {
                return;
            }

            isColliding = true;
            setValid(false);
        }

        void OnTriggerExit(Collider other) {
            isColliding = false;
            setValid(true);
        }

        private StudInfo hitPointToStud(RaycastHit hit) {
            // time to understand at which Stud is he looking at
            StudInfo stud = new StudInfo();

            // get grid dimensions
            string[] tmp = hit.collider.name.Replace("GridStud ", "").Split('x');
            stud.gridDimensions = new Vector2Int(int.Parse(tmp[0]), int.Parse(tmp[1]));

            // 1x1 are easy xD
            if (stud.gridDimensions.x == 1 && stud.gridDimensions.y == 1) {
                return stud;
            }

            // convert world coords to local ones
            var localHitpoint = hit.collider.transform.InverseTransformPoint(hit.point);

            // since localHitpoint is based on the center of the object, we need to sum half
            // of it's size
            localHitpoint.x += (stud.gridDimensions.x * Server.studSize) / 2;
            localHitpoint.z += (stud.gridDimensions.y * Server.studSize) / 2;

            for (int i = 1; i < (stud.gridDimensions.x + 1); i++) {
                if (localHitpoint.x < (i * Server.studSize)) {
                    stud.center.x = (i * Server.studSize) - (Server.studSize / 2) - ((stud.gridDimensions.x * Server.studSize) / 2);
                    stud.gridPosition.x = (i - 1);
                    break;
                }
            }
            for (int i = 1; i < (stud.gridDimensions.y + 1); i++) {
                if (localHitpoint.z < (i * Server.studSize)) {
                    stud.center.z = (i * Server.studSize) - (Server.studSize / 2) - ((stud.gridDimensions.y * Server.studSize) / 2);
                    stud.gridPosition.y = (i - 1);
                    break;
                }
            }

            return stud;
        }

        public void lookingAtStud(RaycastHit hit) {
            // player is not holding a brick
            if (PlayerPanel.Instance.selectedItem == null || PlayerPanel.Instance.selectedItem.item.type != Item.Type.Brick) {
                return;
            }

            var stud = hitPointToStud(hit);

            if (hit.transform == latestStudGrid && stud.gridPosition == latestStud) {
                return;
            }

            GameObject brickObj = hit.collider.transform.parent.gameObject;
            
            latestStudGrid = hit.transform;
            latestStud = stud.gridPosition;

            Vector3 studPos = hit.collider.transform.TransformPoint(stud.center);
            currentStud = studPos;

            if (!Server.bricks.ContainsKey(brickObj.name)) {
                Debug.LogError("Brick not found in server list " + brickObj.name);
                return;
            }

            // same model with all studs available, just put it over
            /*
             BrickModel selectedBrickModel = PlayerPanel.Instance.selectedItem.item.brickModel;
             Brick brick = Server.bricks[brickObj.name];
             if (brick.model.type == selectedBrickModel.type) {
                Vector3 pos = brickObj.transform.position;
                pos.y += brick.model.heightInPlates * Server.plateHeight;
                pos.y += 0.001f; // to make sure they don't collide, so we don't get a false isColliding=true
                move(pos, Quaternion.identity);
                return;
            }*/
            move(currentStud, latestStudGrid.rotation);
        }

        public void move(Vector3 pos, Quaternion rotation) {
            if (lastPos == pos) {
                return;
            }
            pivot.position = pos;
            pivot.rotation = rotation;
            transform.localRotation = Quaternion.identity; // localy reset child rotation
            setValid(true);
            lastPos = pos;
        }

        private void setValid (bool isValid) {
            m.SetTexture("_MainTex", isValid ? null : Game.Instance.redTexture);
        }
    }
}
