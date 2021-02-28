using UnityEngine;
using Brickcraft.UI;
using Brickcraft.World;

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

            Renderer renderer = GetComponent<Renderer>();
            // disable shadows to make it easier to see when placing
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.sharedMaterial = Game.Instance.transparentMaterial;
            m = renderer.sharedMaterial;

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
            if (Input.GetAxis("Mouse ScrollWheel") != 0f && lastPos != Vector3.zero && latestStudGrid != null) {
                Vector3 rot;
                if (latestStudGrid.localRotation == Quaternion.identity) {
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
            } else if (other.name.StartsWith("ChunkSlice")) {
                /*
                RaycastHit hit;
                Physics.Raycast(transform.position, other.transform.position, out hit);
                var pos = other.gameObject.GetComponent<Collider>().ClosestPointOnBounds(transform.position);
                Debug.Log(other.transform.InverseTransformPoint(pos) + "-" + transform.InverseTransformPoint(pos));
                */
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
            if (hit.collider.name.StartsWith("GridStud")) {
                string[] tmp = hit.collider.name.Replace("GridStud ", "").Replace("GridStudBottom ", "").Split('x');
                stud.gridDimensions = new Vector2Int(int.Parse(tmp[0]), int.Parse(tmp[1]));
            } else {
                // is looking at world stud
                // so grid is a 16x16 (chunk slice) made by 2x2 bricks
                stud.gridDimensions = new Vector2Int(Chunk.SliceHeight * 2, Chunk.SliceHeight * 2);
            }

            // 1x1 are easy xD
            if (stud.gridDimensions.x == 1 && stud.gridDimensions.y == 1) {
                return stud;
            }

            // convert world coords to local ones
            Vector3 localHitpoint = hit.collider.transform.InverseTransformPoint(hit.point);

            if (hit.collider.name.StartsWith("ChunkSlice")) { // chunkSlices have center wrongly set
                Vector3 localCenter = hit.collider.transform.InverseTransformPoint(hit.collider.transform.GetComponent<Collider>().bounds.center);
                localHitpoint -= localCenter;
            }

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

        public bool isLookingAtWorldStud (RaycastHit hit) {
            Vector3[] vertices = getTriangleVertices(hit.transform.GetComponent<MeshCollider>().sharedMesh, hit.triangleIndex);

            Vector3Int equal = new Vector3Int(
                vertices[0].x == vertices[1].x && vertices[1].x == vertices[2].x ? 1 : 0,
                vertices[0].y == vertices[1].y && vertices[1].y == vertices[2].y ? 1 : 0,
                vertices[0].z == vertices[1].z && vertices[1].z == vertices[2].z ? 1 : 0
            );

            // y is the same OR there are no equal values (round triangle) then we know that is top or bottom face
            return vertices[0].y == vertices[1].y && vertices[1].y == vertices[2].y || equal == Vector3Int.zero;
        }

        Vector3[] getTriangleVertices(Mesh mesh, int triangleIndex) {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            Vector3[] output = new Vector3[3];

            output[0] = vertices[triangles[triangleIndex * 3 + 0]];
            output[1] = vertices[triangles[triangleIndex * 3 + 1]];
            output[2] = vertices[triangles[triangleIndex * 3 + 2]];

            return output;
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

            Quaternion rot = pivot.rotation; // keep current rotation

            // if old stud and new stud don't have the same rotation,
            // pivot rotation will be invalid, so we reset it to new stud's rotation
            if (latestStudGrid == null || hit.transform.localRotation != latestStudGrid.localRotation) {
                rot = hit.transform.localRotation;
            }

            latestStudGrid = hit.transform;
            latestStud = stud.gridPosition;

            Vector3 studPos = hit.collider.transform.TransformPoint(stud.center);
            

            // height needs to be corrected for bottom studs
            if (hit.collider.name.Contains("Bottom")) {
                studPos.y -= PlayerPanel.Instance.selectedItem.item.brickModel.heightInPlates * Server.plateHeight;
            } else if (hit.collider.name.StartsWith("ChunkSlice")) { // chunkSlices have center wrongly set
                Vector3 localCenter = hit.collider.transform.InverseTransformPoint(hit.collider.transform.GetComponent<Collider>().bounds.center);
                studPos += localCenter;
                studPos.y = hit.point.y;
                studPos.y -= 0.089f; // supper ugly hack
            }
            
            currentStud = studPos;

            GameObject brickObj = hit.collider.transform.parent.gameObject;

            if (!Server.bricks.ContainsKey(brickObj.name) && !hit.collider.name.StartsWith("ChunkSlice")) {
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
            move(currentStud, rot);
        }

        public void move(Vector3 pos, Quaternion rotation) {
            if (lastPos == pos) {
                return;
            }
            pivot.position = pos;
            pivot.rotation = rotation;
            transform.localRotation = Quaternion.identity; // localy reset child rotation as it should always be identity
            isColliding = false;
            setValid(true);
            lastPos = pos;
        }

        private void setValid (bool isValid) {
            m.SetColor("_BaseColor", isValid ? Color.white : Color.red);
        }
    }
}
