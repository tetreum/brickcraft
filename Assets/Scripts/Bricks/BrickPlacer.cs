using UnityEngine;
using Brickcraft.Vehicles;
using Brickcraft.World;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// Previews and places the selected brick on the <see cref="BrickGrid"/>.
    ///
    /// The target position is computed from the face the player is looking at: the brick is put
    /// right against that face and centered on the crosshair. By default it snaps to the world
    /// block grid (so 2x2 bricks line up with the terrain) along the axes where its size is a whole
    /// number of blocks, and to studs along the others (so a 1x1 fits on any stud); holding Shift
    /// allows any stud. On a placed brick it always snaps to studs, so it fits on any of them.
    /// Mouse wheel or R rotates it.
    ///
    /// Attachments (doors, see BrickAttachment) go in the slot of the brick looked at instead (a door frame,
    /// see BrickSlot), if they fit and it's free.
    ///
    /// On a vehicle's brick (see Vehicles.Vehicle) it goes on the vehicle, the same way but on its own cells, however it's
    /// turned. A wheel on the world starts a vehicle there instead of being one of its bricks.
    ///
    /// A part with connectors (technic: an axle, a wheel, a pin, see BrickJoints) aimed near a connector of the brick looked
    /// at that one of its own fits goes there instead, connected: as far along it as the crosshair, R turning it around it
    /// a quarter turn at a time, T putting it the other way round, G picking another of its connectors that fits.
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
        // connected placement: the other way round, and which of the part's fitting connectors
        private bool flip;
        private int connectorChoice;
        private readonly System.Collections.Generic.List<int> fitting = new System.Collections.Generic.List<int>();
        // the line of sight this near a connector connects to it, looked at this far past the surface it hits
        private const float ConnectDistance = 0.15f;
        private const float LookPast = 0.5f;

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
            if (model.IsAttachment) {
                target = null;
                tickAttachment(hasHit, hit, selectedItem, model);
                return;
            }

            // the mouse wheel picks the slot instead, see PlayerPanel
            if (GameInput.GetButtonDown(GameInput.Rotate)) {
                rotation = (rotation + 1) % 4;
            }
            if (GameInput.GetButtonDown(GameInput.Flip)) {
                flip = !flip;
            }
            if (GameInput.GetButtonDown(GameInput.NextConnector)) {
                connectorChoice++;
            }

            Brick looked = hasHit ? Server.findBrick(hit.collider) : null;
            if (looked != null && model.connectors.Length > 0 && tickConnected(hit, selectedItem, model, looked)) {
                target = null;
                return;
            }
            if (looked != null && looked.vehicle != null) {
                target = null;
                tickVehicle(hit, selectedItem, model, looked.vehicle);
                return;
            }

            target = hasHit ? computePlacement(hit, model) : (BrickPlacement?)null;

            bool isValid = target.HasValue && BrickGrid.IsFree(target.Value) && !overlapsPlayer(target.Value)
                && !BrickJoints.OverlapsConnected(target.Value.WorldBounds);

            updateGhost(isValid);

            if (isValid && GameInput.GetButtonDown(GameInput.Place)) {
                if (selectedItem.item.VehicleRole == VehicleRole.Wheel) {
                    Player.Instance.network.CmdCreateVehicle(selectedItem.slot, target.Value.origin, (byte)target.Value.rotation);
                } else {
                    place(selectedItem, target.Value);
                }
            }
        }

        // connected to the brick looked at, where the line of sight passes near one of its connectors that fits (holes
        // are inside bricks: a little past the surface too); false if none is that near
        private bool tickConnected(RaycastHit hit, UserItem selectedItem, BrickModel model, Brick holder) {
            Transform eye = Camera.main != null ? Camera.main.transform : null;
            if (eye == null) {
                return false;
            }
            Vector3 looking = (hit.point - eye.position).normalized;
            int holderConnector = BrickJoints.NearestFitting(holder, model, eye.position, hit.point + looking * LookPast, ConnectDistance, fitting, out Vector3 nearest);
            if (holderConnector < 0 || fitting.Count == 0) {
                return false;
            }
            int own = fitting[BrickJoints.Mod(connectorChoice, fitting.Count)];
            BrickJoint joint = new BrickJoint() {
                holderConnector = holderConnector,
                connector = own,
                slide = BrickJoints.SlideToward(holder, holderConnector, model.connectors[own], nearest),
                spin = rotation,
                flip = flip,
            };
            BrickJoints.PoseHeldBy(holder, model, joint, out Vector3 position, out Quaternion turned);
            Transform parent = BrickJoints.ParentOf(holder);
            ghost.transform.SetPositionAndRotation(parent != null ? parent.TransformPoint(position) : position, parent != null ? parent.rotation * turned : turned);
            ghost.SetActive(true);
            bool isValid = BrickJoints.WhyNot(holder, model, joint) == null;
            setGhostColor(isValid);

            if (isValid && GameInput.GetButtonDown(GameInput.Place)) {
                Player.Instance.network.CmdConnectBrick(selectedItem.slot, holder.id, joint.holderConnector, joint.connector, joint.slide, joint.spin, joint.flip);
            }
            return true;
        }

        // on a vehicle: in its cells, against the face looked at like on the world's grid
        private void tickVehicle(RaycastHit hit, UserItem selectedItem, BrickModel model, Vehicle vehicle) {
            BrickPlacement placement = computeVehiclePlacement(hit, model, vehicle.transform);
            bool isValid = vehicle.grid.IsFree(placement) && !VehicleSystem.OverlapsOthers(vehicle, placement);

            Transform tr = vehicle.transform;
            ghost.transform.SetPositionAndRotation(tr.TransformPoint(placement.LocalPosition), tr.rotation * placement.Rotation * model.prefab.transform.rotation);
            ghost.SetActive(true);
            setGhostColor(isValid);

            if (isValid && GameInput.GetButtonDown(GameInput.Place)) {
                Player.Instance.network.CmdPlaceVehicleBrick(selectedItem.slot, vehicle.netIdentity, placement.origin, (byte)placement.rotation);
            }
        }

        private BrickPlacement computeVehiclePlacement(RaycastHit hit, BrickModel model, Transform vehicle) {
            Vector3Int normal = dominantAxis(vehicle.InverseTransformDirection(hit.normal));
            Vector3Int size = BrickPlacement.SizeFor(model, rotation);
            Vector3 local = vehicle.InverseTransformPoint(hit.point);
            Vector3 cells = toCells(local);
            Vector3 beyond = toCells(local + (Vector3)normal * SurfaceOffset);
            Vector3Int adjacent = new Vector3Int(Mathf.FloorToInt(beyond.x), Mathf.FloorToInt(beyond.y), Mathf.FloorToInt(beyond.z));
            Vector3Int origin = Vector3Int.zero;

            for (int axis = 0; axis < 3; axis++) {
                if (normal[axis] > 0) {
                    origin[axis] = adjacent[axis];
                } else if (normal[axis] < 0) {
                    origin[axis] = adjacent[axis] - size[axis] + 1;
                } else if (axis == 1) {
                    origin.y = adjacent.y;
                } else {
                    // any stud: a vehicle's bricks have no blocks to line up with
                    origin[axis] = Mathf.RoundToInt(cells[axis] - size[axis] / 2f);
                }
            }
            return new BrickPlacement(model, origin, rotation);
        }

        private static Vector3 toCells(Vector3 local) {
            return new Vector3(local.x / BrickGrid.CellSize.x, local.y / BrickGrid.CellSize.y, local.z / BrickGrid.CellSize.z);
        }

        private void tickAttachment(bool hasHit, RaycastHit hit, UserItem selectedItem, BrickModel model) {
            Brick holder = hasHit ? Server.findBrick(hit.collider) : null;
            // looking at what's in it already: its brick
            if (holder != null && holder.attachedTo != null) {
                Server.bricks.TryGetValue(holder.attachedTo, out holder);
            }
            BrickSlot slot = holder != null ? Server.slotOf(holder) : null;
            if (slot == null || slot.fits != model.attachesTo) {
                ghost.SetActive(false);
                return;
            }
            bool isValid = Network.PlayerNetwork.CanAttach(model, holder);
            ghost.transform.SetPositionAndRotation(slot.point.position, slot.point.rotation * model.prefab.transform.rotation);
            ghost.SetActive(true);
            setGhostColor(isValid);

            if (isValid && GameInput.GetButtonDown(GameInput.Place)) {
                Player.Instance.network.CmdAttachBrick(selectedItem.slot, holder.id);
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
            // on a brick it goes on any of its studs: the block grid is the terrain's
            bool snapToBlocks = !GameInput.GetButton(GameInput.FreePlacement) && Server.findBrick(hit.collider) == null;
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
                    origin.y = snapToBlocks && size.y % BrickGrid.PlatesPerBlock == 0
                        ? BrickGrid.FloorDiv(adjacent.y, BrickGrid.PlatesPerBlock) * BrickGrid.PlatesPerBlock
                        : adjacent.y;
                } else {
                    // center the brick on the crosshair
                    float start = gridPoint[axis] - size[axis] / 2f;
                    origin[axis] = originCell[axis] + (snapToBlocks && size[axis] % BrickGrid.StudsPerBlock == 0
                        ? Mathf.RoundToInt(start / BrickGrid.StudsPerBlock) * BrickGrid.StudsPerBlock
                        : Mathf.RoundToInt(start));
                }
            }

            return new BrickPlacement(model, origin, rotation);
        }

        // the server places it, and takes the item from our inventory if it could
        private void place(UserItem userItem, BrickPlacement placement) {
            Player.Instance.network.CmdPlaceBrick(userItem.slot, placement.origin, (byte)placement.rotation);
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

            ghost = Instantiate(model.prefab);
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
            GameObject prefab = placement.model.prefab;

            ghost.transform.SetPositionAndRotation(placement.Position, placement.Rotation * prefab.transform.rotation);
            ghost.SetActive(true);
            setGhostColor(isValid);
        }

        private void setGhostColor(bool isValid) {
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
