using System;
using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Bricks.LDraw;
using Brickcraft.Network;
using UnityEngine;

namespace Brickcraft
{
    /// <summary>
    /// The model importer's ghost (see Network.ModelImporter): the model picked, see-through, where the player
    /// looks (its box's middle on the face looked at, on the blocks' grid, any stud with Shift), white where it can
    /// go and red where it can't. R turns it, Place places it, Escape (or another slot) gives up. The player can walk
    /// around meanwhile; the server checks it all again.
    /// </summary>
    public class ModelImporterView : MonoBehaviour
    {
        // how far it can be put, looking
        private const float Reach = ModelImporter.MaxDistance;

        public static ModelImporterView Current { get; private set; }

        public static bool IsOpen {
            get { return Current != null; }
        }

        /// <summary>The frame the last one closed (its Place click mustn't open the window again).</summary>
        public static int ClosedFrame { get; private set; } = -1;

        /// <summary>Raised when one opens and closes.</summary>
        public static event Action Opened, Closed;

        /// <summary>Raised when the player uses the model importer in that slot: its window opens (see UI.ModelImporterPanel).</summary>
        public static event Action<int> WindowRequested;

        public static void RequestWindow(int slot) {
            WindowRequested?.Invoke(slot);
        }

        private LDrawImport model;
        private string modelName;
        private int slot;
        private GameObject ghost;
        private Material ghostMaterial;
        private Color ghostColor;
        private int turns;
        private Vector3Int? corner;
        private Vector3Int checkedCorner;
        private int checkedTurns = -1;
        private bool isValid;

        /// <summary>Starts placing the model read from that file, for the model importer in that slot.</summary>
        public static void Open(LDrawImport model, string name, int slot) {
            if (Current != null) {
                Current.Close();
            }
            ModelImporterView view = new GameObject("ModelImporterView").AddComponent<ModelImporterView>();
            view.model = model;
            view.modelName = name;
            view.slot = slot;
            view.build();
            Current = view;
            Opened?.Invoke();
        }

        public void Close() {
            ClosedFrame = Time.frameCount;
            if (Current == this) {
                Current = null;
                Closed?.Invoke();
            }
            Destroy(gameObject);
        }

        private void OnDestroy() {
            if (ghostMaterial != null) {
                Destroy(ghostMaterial);
            }
            if (Current == this) {
                Current = null;
                Closed?.Invoke();
            }
        }

        // the ghost: every brick's prefab where it goes, the box's lowest corner at its root
        private void build() {
            ghostMaterial = new Material(Game.Instance.transparentMaterial);
            ghostColor = ghostMaterial.GetColor("_BaseColor");
            ghost = new GameObject("ModelPreview");
            ghost.transform.SetParent(transform, false);
            GameObject[] parts = new GameObject[model.bricks.Count];
            for (int i = 0; i < model.bricks.Count; i++) {
                ImportedBrick brick = model.bricks[i];
                BrickModel brickModel = Server.items[brick.itemId].brickModel;
                GameObject part;
                if (brick.IsAttachment) {
                    // in its brick's slot, like the game puts it (Server.spawnBrick)
                    BrickSlot slot = parts[brick.attachedTo - 1].GetComponentInChildren<BrickSlot>(true);
                    part = Instantiate(brickModel.prefab, slot.point.position, slot.point.rotation * brickModel.prefab.transform.rotation, ghost.transform);
                } else {
                    BrickPlacement placement = new BrickPlacement(brickModel, brick.origin, brick.rotation);
                    Vector3Int size = placement.Size;
                    part = Instantiate(brickModel.prefab, ghost.transform, false);
                    part.transform.localPosition = Vector3.Scale((Vector3)brick.origin + new Vector3(size.x / 2f, 0, size.z / 2f), BrickGrid.CellSize);
                    part.transform.localRotation = placement.Rotation * brickModel.prefab.transform.rotation;
                }
                parts[i] = part;
                foreach (Collider collider in part.GetComponentsInChildren<Collider>(true)) {
                    Destroy(collider);
                }
                foreach (Transform child in part.GetComponentsInChildren<Transform>(true)) {
                    child.gameObject.layer = (int)Game.Layers.IgnoreRaycast;
                }
                foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true)) {
                    renderer.sharedMaterial = ghostMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            ghost.SetActive(false);
        }

        private void Update() {
            Player player = Player.Instance;
            // another slot, or dead: it gives up
            if (player == null || player.SelectedItem == null || !player.SelectedItem.item.IsModelImporter || player.SelectedSlot != slot) {
                Close();
                return;
            }
            if (GameInput.IsTyping || player.isFrozen) {
                return;
            }
            if (GameInput.GetButtonDown(GameInput.Rotate)) {
                turns = (turns + 1) % 4;
            }
            aim();
            if (corner.HasValue && GameInput.GetButtonDown(GameInput.Place)) {
                if (isValid) {
                    player.GetComponent<ModelImporter>().Place(model.Placed(corner.Value, turns), slot);
                    ModelImporter.Chat("Placing " + modelName + "...");
                    Close();
                } else {
                    ModelImporter.Chat("The model can't go there");
                }
            }
        }

        // where the player looks: the box's middle against that face
        private void aim() {
            Camera camera = Camera.main;
            if (camera == null || !Physics.Raycast(camera.transform.position, camera.transform.forward, out RaycastHit hit, Reach,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) {
                corner = null;
                ghost.SetActive(false);
                return;
            }
            Vector3Int cell = BrickGrid.WorldToCell(hit.point + hit.normal * 0.01f);
            Vector3Int size = model.SizeTurned(turns);
            Vector3Int at = new Vector3Int(cell.x - size.x / 2, cell.y, cell.z - size.z / 2);
            // below what's looked at from underneath
            if (hit.normal.y < -0.5f) {
                at.y -= size.y - 1;
            }
            bool anyStud = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!anyStud) {
                at.x = BrickGrid.FloorDiv(at.x, BrickGrid.StudsPerBlock) * BrickGrid.StudsPerBlock;
                at.z = BrickGrid.FloorDiv(at.z, BrickGrid.StudsPerBlock) * BrickGrid.StudsPerBlock;
            }
            corner = at;
            if (at != checkedCorner || turns != checkedTurns) {
                checkedCorner = at;
                checkedTurns = turns;
                isValid = fits(at);
            }

            // turned around the corner, then moved back into the box (see LDrawImport.Placed)
            Vector3Int offset = turns == 1 ? new Vector3Int(0, 0, model.Size.x) : turns == 2 ? new Vector3Int(model.Size.x, 0, model.Size.z)
                : turns == 3 ? new Vector3Int(model.Size.z, 0, 0) : Vector3Int.zero;
            ghost.transform.SetPositionAndRotation(BrickGrid.CellToWorld(at + offset), Quaternion.Euler(0, 90 * turns, 0));
            ghost.SetActive(true);
            Color color = isValid ? Color.white : Color.red;
            color.a = ghostColor.a;
            ghostMaterial.SetColor("_BaseColor", color);
        }

        // what the server checks (see ModelImporter): every cell free, no player in the way, near enough
        private bool fits(Vector3Int at) {
            List<ImportedBrick> placed = model.Placed(at, turns);
            foreach (ImportedBrick brick in placed) {
                if (brick.IsAttachment) {
                    continue; // in its brick's cells
                }
                BrickPlacement placement = new BrickPlacement(Server.items[brick.itemId].brickModel, brick.origin, brick.rotation);
                if (!BrickGrid.IsFree(placement) || BrickPlacer.overlapsPlayer(placement)) {
                    return false;
                }
            }
            Vector3 middle = BrickGrid.CellToWorld(at, (Vector3)model.SizeTurned(turns) * 0.5f);
            return Player.Instance != null && Vector3.Distance(Player.Instance.transform.position, middle) <= ModelImporter.MaxDistance;
        }
    }
}
