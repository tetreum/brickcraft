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
    /// turned. A vehicle part (a wheel, a seat, a propeller, a wing; see Vehicles.VehicleRole) on the world starts a
    /// vehicle there instead of being one of its bricks; aimed at water, it goes on its surface (a boat starts there).
    ///
    /// A part with connectors (technic: an axle, a wheel, a pin, see BrickJoints) aimed near a connector of the brick looked
    /// at that one of its own fits goes there instead, connected: as far along it as the crosshair (Z and X sliding it
    /// further back or forward, half a stud a time: a long axle through a short hole), R turning it around it a quarter
    /// turn at a time, T putting it the other way round, G picking another of its connectors that fits. Against a
    /// connected brick (a beam on an axle) it goes on that brick's own grid, held by it.
    ///
    /// The middle button puts it by one of its corners at the crosshair instead of centred on it (going round them,
    /// then back to the middle): next to something in the way, it reaches away from it. Placing goes back to the middle.
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
        // a sign turns its front (see SignText.FrontOf) towards the player, R turning it from there
        private bool facesPlayer;
        private BrickPlacement? target;
        // connected placement: the other way round, and which of the part's fitting connectors
        private bool flip;
        private int connectorChoice;
        // 0: centred on the crosshair; 1 to 4: by a corner, reaching +x+z, -x+z, -x-z, +x-z (its grid's)
        private int corner;
        private const int Corners = 4;
        // half studs slid with the keys, on top of where it's looked at; for the connection it was slid on
        private int slideOffset;
        private string slidOn;
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

            facesPlayer = selectedItem.item.IsSign;
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
            if (GameInput.GetButtonDown(GameInput.Corner)) {
                corner = (corner + 1) % (Corners + 1);
            }

            Brick looked = hasHit ? Server.findBrick(hit.collider) : null;
            if (looked != null && model.connectors.Length > 0 && tickConnected(hit, selectedItem, model, looked)) {
                target = null;
                return;
            }
            if (looked != null && BrickJoints.HoldsOnStuds(looked)) {
                target = null;
                tickOnStuds(hit, selectedItem, model, looked);
                return;
            }
            if (looked != null && looked.vehicle != null) {
                target = null;
                tickVehicle(hit, selectedItem, model, looked.vehicle);
                return;
            }

            // a vehicle part over water: on its surface (it has no collider, the line of sight goes through it)
            bool startsVehicle = selectedItem.item.VehicleRole != VehicleRole.None;
            if (looked == null && startsVehicle && waterSurface(hasHit, hit, out RaycastHit surface)) {
                hasHit = true;
                hit = surface;
            }
            target = hasHit ? computePlacement(hit, model) : (BrickPlacement?)null;

            bool isValid = target.HasValue && BrickGrid.IsFree(target.Value) && !overlapsPlayer(target.Value)
                && !BrickJoints.OverlapsConnected(target.Value.WorldBounds);

            updateGhost(isValid);

            if (isValid && GameInput.GetButtonDown(GameInput.Place)) {
                if (startsVehicle) {
                    Player.Instance.network.CmdCreateVehicle(selectedItem.slot, target.Value.origin, (byte)target.Value.rotation);
                } else {
                    place(selectedItem, target.Value);
                }
                corner = 0;
            }
        }

        // how far a vehicle part reaches over water with nothing behind it
        private const float WaterReach = 5;

        // where the line of sight first goes into water (before what it hits), as a hit on its surface
        private static bool waterSurface(bool hasHit, RaycastHit hit, out RaycastHit surface) {
            surface = default(RaycastHit);
            Transform eye = Camera.main != null ? Camera.main.transform : null;
            if (eye == null || Water.IsFluidBlockAt(eye.position)) {
                return false; // swimming: what's ahead
            }
            float reach = hasHit ? hit.distance : WaterReach;
            for (float along = 0; along <= reach; along += 0.05f) {
                Vector3 point = eye.position + eye.forward * along;
                if (!Water.IsFluidBlockAt(point)) {
                    continue;
                }
                Vector3Int block = BrickGrid.CellToBlock(BrickGrid.WorldToCell(point));
                surface.point = new Vector3(point.x, BrickGrid.CellToWorld(BrickGrid.BlockToCell(block + Vector3Int.up)).y, point.z);
                surface.normal = Vector3.up;
                return true;
            }
            return false;
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
            // another connection: slid from where it's looked at again
            string connection = holder.id + "/" + holderConnector + "/" + own;
            if (connection != slidOn) {
                slidOn = connection;
                slideOffset = 0;
            }
            int max = BrickJoints.MaxSlide(holder.model.connectors[holderConnector], model.connectors[own]);
            int looked = BrickJoints.SlideToward(holder, holderConnector, model.connectors[own], nearest);
            if (GameInput.GetButtonDown(GameInput.SlideForward)) {
                slideOffset = Mathf.Min(slideOffset + 1, max - looked);
            }
            if (GameInput.GetButtonDown(GameInput.SlideBack)) {
                slideOffset = Mathf.Max(slideOffset - 1, -max - looked);
            }
            BrickJoint joint = new BrickJoint() {
                holderConnector = holderConnector,
                connector = own,
                slide = Mathf.Clamp(looked + slideOffset, -max, max),
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
                corner = 0;
            }
            return true;
        }

        // against a connected brick: on its own grid, held by it
        private void tickOnStuds(RaycastHit hit, UserItem selectedItem, BrickModel model, Brick holder) {
            BrickJoints.GridOf(holder, out Vector3 grid, out Quaternion gridTurned);
            Transform parent = BrickJoints.ParentOf(holder);
            if (parent != null) {
                grid = parent.TransformPoint(grid);
                gridTurned = parent.rotation * gridTurned;
            }
            BrickPlacement placement = computeVehiclePlacement(hit, model, grid, gridTurned);
            BrickJoint joint = BrickJoint.OnStuds(placement.origin, placement.rotation);
            BrickJoints.PoseHeldBy(holder, model, joint, out Vector3 position, out Quaternion turned);
            ghost.transform.SetPositionAndRotation(parent != null ? parent.TransformPoint(position) : position, parent != null ? parent.rotation * turned : turned);
            ghost.SetActive(true);
            bool isValid = BrickJoints.WhyNot(holder, model, joint) == null;
            setGhostColor(isValid);

            if (isValid && GameInput.GetButtonDown(GameInput.Place)) {
                Player.Instance.network.CmdPlaceOnStuds(selectedItem.slot, holder.id, placement.origin, placement.rotation);
                corner = 0;
            }
        }

        // on a vehicle: in its cells, against the face looked at like on the world's grid
        private void tickVehicle(RaycastHit hit, UserItem selectedItem, BrickModel model, Vehicle vehicle) {
            BrickPlacement placement = computeVehiclePlacement(hit, model, vehicle.transform.position, vehicle.transform.rotation);
            bool isValid = vehicle.grid.IsFree(placement) && !VehicleSystem.OverlapsOthers(vehicle, placement);

            Transform tr = vehicle.transform;
            ghost.transform.SetPositionAndRotation(tr.TransformPoint(placement.LocalPosition), tr.rotation * placement.Rotation * model.prefab.transform.rotation);
            ghost.SetActive(true);
            setGhostColor(isValid);

            if (isValid && GameInput.GetButtonDown(GameInput.Place)) {
                Player.Instance.network.CmdPlaceVehicleBrick(selectedItem.slot, vehicle.netIdentity, placement.origin, (byte)placement.rotation);
                corner = 0;
            }
        }

        // the quarter turns it goes with on a grid turned that way: R's, and for a sign the ones that face its front to
        // the player (its model's -Z: its +Z the way the player looks)
        private int turnsOn(Quaternion gridTurned) {
            Camera eye = Camera.main;
            if (!facesPlayer || eye == null) {
                return rotation;
            }
            Vector3 looking = Quaternion.Inverse(gridTurned) * eye.transform.forward;
            int facing = Mathf.RoundToInt(Mathf.Atan2(looking.x, looking.z) / (Mathf.PI / 2));
            return BrickGrid.Mod(rotation + facing, 4);
        }

        // on a grid of its own that starts there, turned that way (a vehicle's cells, a connected brick's grid)
        private BrickPlacement computeVehiclePlacement(RaycastHit hit, BrickModel model, Vector3 gridStart, Quaternion gridTurned) {
            Quaternion back = Quaternion.Inverse(gridTurned);
            Vector3Int normal = dominantAxis(back * hit.normal);
            int turns = turnsOn(gridTurned);
            Vector3Int size = BrickPlacement.SizeFor(model, turns);
            Vector3 local = back * (hit.point - gridStart);
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
                    origin[axis] = startAlong(axis, cells[axis], size[axis]);
                }
            }
            return new BrickPlacement(model, origin, turns);
        }

        // its first cell along an axis of the face looked at (x or z; point in cells): centred on the crosshair, or by a
        // corner, from the cell looked at one way or the other
        private int startAlong(int axis, float point, int size) {
            if (corner == 0) {
                return Mathf.RoundToInt(point - size / 2f);
            }
            int looked = Mathf.FloorToInt(point);
            bool back = axis == 0 ? corner == 2 || corner == 3 : corner == 3 || corner == 4;
            return back ? looked - size + 1 : looked;
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
            int turns = turnsOn(Quaternion.identity);
            Vector3Int size = BrickPlacement.SizeFor(model, turns);
            // relative to the floating origin, which is a whole number of chunks so block snapping still works
            Vector3 gridPoint = BrickGrid.WorldToLocalGrid(hit.point);
            Vector3Int originCell = FloatingOrigin.Cell;
            Vector3Int adjacent = BrickGrid.WorldToCell(hit.point + (Vector3)normal * SurfaceOffset);
            // on a brick it goes on any of its studs: the block grid is the terrain's
            bool snapToBlocks = !GameInput.GetButton(GameInput.FreePlacement) && (hit.collider == null || Server.findBrick(hit.collider) == null);
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
                } else if (corner != 0) {
                    origin[axis] = originCell[axis] + startAlong(axis, gridPoint[axis], size[axis]);
                } else {
                    // center the brick on the crosshair
                    float start = gridPoint[axis] - size[axis] / 2f;
                    origin[axis] = originCell[axis] + (snapToBlocks && size[axis] % BrickGrid.StudsPerBlock == 0
                        ? Mathf.RoundToInt(start / BrickGrid.StudsPerBlock) * BrickGrid.StudsPerBlock
                        : Mathf.RoundToInt(start));
                }
            }

            return new BrickPlacement(model, origin, turns);
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
