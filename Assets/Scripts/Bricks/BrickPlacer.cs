using UnityEngine;
using Brickcraft.World;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// Previews and places the selected brick on the <see cref="BrickGrid"/>.
    ///
    /// The target position is computed from the face the player is looking at: the brick is put
    /// right against that face and centered on the crosshair. By default it snaps to the world
    /// block grid (so 2x2 bricks line up with the terrain), holding Shift allows any stud.
    /// Mouse wheel or R rotates it.
    /// </summary>
    public class BrickPlacer : MonoBehaviour
    {
        // how far from the hit surface we sample to find the cell next to it
        private const float SurfaceOffset = 0.01f;

        private GameObject ghost;
        private BrickModel ghostModel;
        private Material ghostMaterial;
        private Color ghostColor;

        private int rotation;
        private BrickPlacement? target;

        private void Awake() {
            ghostMaterial = new Material(Game.Instance.transparentMaterial);
            ghostColor = ghostMaterial.GetColor("_BaseColor");
        }

        private void OnDestroy() {
            if (ghost != null) {
                Destroy(ghost);
            }
            Destroy(ghostMaterial);
        }

        /// <summary>Called by the player every frame with the result of its crosshair raycast.</summary>
        public void tick(bool hasHit, RaycastHit hit, UserItem selectedItem) {
            BrickModel model = selectedItem != null && selectedItem.item.type == Item.Type.Brick
                ? selectedItem.item.brickModel
                : null;

            setModel(model);

            if (model == null) {
                target = null;
                return;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0f || GameInput.GetButtonDown(GameInput.Rotate, KeyCode.R)) {
                rotation = (rotation + 1) % 4;
            } else if (scroll < 0f) {
                rotation = (rotation + 3) % 4;
            }

            target = hasHit ? computePlacement(hit, model) : (BrickPlacement?)null;

            bool isValid = target.HasValue && BrickGrid.IsFree(target.Value) && !overlapsPlayer(target.Value);

            updateGhost(isValid);

            if (isValid && GameInput.GetButtonDown(GameInput.Place, KeyCode.Mouse1)) {
                place(selectedItem, target.Value);
            }
        }

        public void hide() {
            target = null;
            if (ghost != null) {
                ghost.SetActive(false);
            }
        }

        private BrickPlacement computePlacement(RaycastHit hit, BrickModel model) {
            Vector3Int normal = dominantAxis(hit.normal);
            Vector3Int size = BrickPlacement.SizeFor(model, rotation);
            // relative to the floating origin, which is a whole number of chunks so block snapping still works
            Vector3 gridPoint = BrickGrid.WorldToLocalGrid(hit.point);
            Vector3Int originCell = FloatingOrigin.Cell;
            Vector3Int adjacent = BrickGrid.WorldToCell(hit.point + (Vector3)normal * SurfaceOffset);
            bool snapToBlocks = !GameInput.GetButton(GameInput.FreePlacement, KeyCode.LeftShift, KeyCode.RightShift);
            Vector3Int origin = Vector3Int.zero;

            for (int axis = 0; axis < 3; axis++) {
                if (normal[axis] > 0) {
                    // brick starts right after the face
                    origin[axis] = adjacent[axis];
                } else if (normal[axis] < 0) {
                    // brick ends right before the face
                    origin[axis] = adjacent[axis] - size[axis] + 1;
                } else if (axis == 1) {
                    // placing against a side face, sit on the hovered row
                    origin.y = snapToBlocks
                        ? BrickGrid.FloorDiv(adjacent.y, BrickGrid.PlatesPerBlock) * BrickGrid.PlatesPerBlock
                        : adjacent.y;
                } else {
                    // center the brick on the crosshair
                    float start = gridPoint[axis] - size[axis] / 2f;
                    origin[axis] = originCell[axis] + (snapToBlocks
                        ? Mathf.RoundToInt(start / BrickGrid.StudsPerBlock) * BrickGrid.StudsPerBlock
                        : Mathf.RoundToInt(start));
                }
            }

            return new BrickPlacement(model, origin, rotation);
        }

        // the server places it, and takes the item from our inventory if it could
        private void place(UserItem userItem, BrickPlacement placement) {
            Player.Instance.network.CmdPlaceBrick(userItem.id, userItem.health, placement.origin, (byte)placement.rotation);
        }

        public static bool overlapsPlayer(BrickPlacement placement) {
            Bounds bounds = placement.WorldBounds;
            bounds.Expand(-0.02f); // touching is fine

            foreach (Collider collider in Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore)) {
                if (collider.GetComponentInParent<Player>() != null) {
                    return true;
                }
            }
            return false;
        }

        private void setModel(BrickModel model) {
            if (model == ghostModel) {
                return;
            }
            ghostModel = model;

            if (ghost != null) {
                Destroy(ghost);
                ghost = null;
            }
            if (model == null) {
                return;
            }

            ghost = Instantiate(Server.brickPrefabs[model.type.ToString()]);
            ghost.name = "BrickPreview";
            ghost.SetActive(false);

            foreach (Collider collider in ghost.GetComponentsInChildren<Collider>(true)) {
                Destroy(collider);
            }
            foreach (Transform tr in ghost.GetComponentsInChildren<Transform>(true)) {
                tr.gameObject.layer = (int)Game.Layers.IgnoreRaycast;
            }
            foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>(true)) {
                renderer.sharedMaterial = ghostMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void updateGhost(bool isValid) {
            if (ghost == null) {
                return;
            }
            if (!target.HasValue) {
                ghost.SetActive(false);
                return;
            }

            BrickPlacement placement = target.Value;
            GameObject prefab = Server.brickPrefabs[placement.model.type.ToString()];

            ghost.transform.SetPositionAndRotation(placement.Position, placement.Rotation * prefab.transform.rotation);
            ghost.SetActive(true);

            Color color = isValid ? Color.white : Color.red;
            color.a = ghostColor.a;
            ghostMaterial.SetColor("_BaseColor", color);
        }

        private static Vector3Int dominantAxis(Vector3 v) {
            Vector3 abs = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

            if (abs.y >= abs.x && abs.y >= abs.z) {
                return new Vector3Int(0, v.y > 0 ? 1 : -1, 0);
            }
            if (abs.x >= abs.z) {
                return new Vector3Int(v.x > 0 ? 1 : -1, 0, 0);
            }
            return new Vector3Int(0, 0, v.z > 0 ? 1 : -1);
        }
    }
}
