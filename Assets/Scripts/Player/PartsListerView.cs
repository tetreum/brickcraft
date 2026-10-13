using System;
using Brickcraft.Bricks;
using Brickcraft.Network;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Brickcraft
{
    /// <summary>
    /// The local player's parts lister session (see Network.PartsLister): its controls are frozen and the camera
    /// flies free (right mouse to look, the movement keys, jump and Ctrl to go up and down, sprint for faster),
    /// never farther than CameraRange from a see-through box around its creation. Each face of the box is pushed
    /// or pulled with the mouse wheel or by dragging it (one stud sideways, one plate up and down; Shift for 4),
    /// or from the panel (see UI.PartsListerPanel), which saves the parts list or cancels.
    /// </summary>
    public class PartsListerView : MonoBehaviour
    {
        /// <summary>The faces, in Grow's order: west (-X), east (+X), bottom, top, south (-Z), north (+Z).</summary>
        public static readonly string[] FaceNames = { "West", "East", "Bottom", "Top", "South", "North" };

        /// <summary>How far from the box the camera can go, in meters.</summary>
        public const float CameraRange = 10;
        private const float FlySpeed = 6;
        private const float FastFlySpeed = 15;
        private const float LookSpeed = 2.5f;
        private const int FastSteps = 4;

        private static readonly Color FaceColor = new Color(0.3f, 0.7f, 1f, 0.1f);
        private static readonly Color HoveredFaceColor = new Color(0.45f, 0.85f, 1f, 0.3f);

        /// <summary>The open session, null when there's none.</summary>
        public static PartsListerView Current { get; private set; }
        public static bool IsOpen {
            get { return Current != null; }
        }

        /// <summary>A session opened, closed, or its box changed (the panel follows them).</summary>
        public static event Action Opened;
        public static event Action Closed;
        public static event Action BoxChanged;

        /// <summary>The box, in cells (max exclusive).</summary>
        public Vector3Int Min { get; private set; }
        public Vector3Int Max { get; private set; }

        private PartsLister owner;
        private Vector3Int startMin;
        private Vector3Int startMax;

        private Camera eye;
        private Transform eyeParent;
        private Vector3 eyeLocalPosition;
        private Quaternion eyeLocalRotation;
        private float yaw;
        private float pitch;

        private GameObject box;
        private Renderer[] faces;
        private LineRenderer edges;
        private int hovered = -1;
        private int dragged = -1;
        private Vector3Int dragMin;
        private Vector3Int dragMax;
        private float dragFrom;
        // a point of the dragged face's axis, fixed while it's dragged (the box's middle moves with the face)
        private Vector3 dragAnchor;
        // the pointer looking almost along the axis: where it points along it is too unsteady to follow
        private const float MinDragAngleSine = 0.25f;

        /// <summary>Starts a session around that box (cells, max exclusive), the creation's.</summary>
        public static void Open(PartsLister owner, Vector3Int min, Vector3Int max) {
            if (Current != null) {
                Current.Close();
            }
            if (Player.Instance == null) {
                return;
            }
            GameObject go = new GameObject("PartsListerView");
            PartsListerView view = go.AddComponent<PartsListerView>();
            view.owner = owner;
            view.startMin = view.Min = min;
            view.startMax = view.Max = max;
            Current = view;
            view.begin();
            Opened?.Invoke();
        }

        private void begin() {
            Player.Instance.freeze(Player.FreezeReason.PartsLister);
            // the player's camera flies off, back where it was when done
            eye = Camera.main;
            eyeParent = eye.transform.parent;
            eyeLocalPosition = eye.transform.localPosition;
            eyeLocalRotation = eye.transform.localRotation;
            eye.transform.SetParent(null, true);
            Vector3 euler = eye.transform.rotation.eulerAngles;
            yaw = euler.y;
            pitch = euler.x > 180 ? euler.x - 360 : euler.x;
            Game.unlockMouse();
            buildBox();
        }

        /// <summary>Ends it (saved, cancelled, or the server closed it): the player gets its camera and controls back.</summary>
        public void Close() {
            if (Current != this) {
                return;
            }
            Current = null;
            if (eye != null) {
                eye.transform.SetParent(eyeParent, false);
                eye.transform.localPosition = eyeLocalPosition;
                eye.transform.localRotation = eyeLocalRotation;
            }
            if (Player.Instance != null) {
                Player.Instance.unFreeze(Player.FreezeReason.PartsLister);
            }
            Game.lockMouse();
            Destroy(gameObject);
            Closed?.Invoke();
        }

        /// <summary>Saves the parts list of what's in the box under that name, its files' (the server answers with it).</summary>
        public void Save(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                return; // a list needs a name
            }
            if (owner != null) {
                owner.Save(Min, Max, name.Trim());
            }
            Close();
        }

        public void Cancel() {
            if (owner != null) {
                owner.Cancel();
            }
            Close();
        }

        private void OnDestroy() {
            if (box != null) {
                Destroy(box);
            }
            if (Current == this) { // the scene went away with it
                Current = null;
                Closed?.Invoke();
            }
        }

        /// <summary>
        /// Pushes a face out (steps more than 0) or pulls it in, in cells: never past the other side, nor more than
        /// PartsLister.MaxGrowth past the creation's box.
        /// </summary>
        public void Grow(int face, int steps) {
            SetFace(face, faceValue(face, Min, Max) + (face % 2 == 0 ? -steps : steps));
        }

        // a face's coordinate in cells (along its axis)
        private static int faceValue(int face, Vector3Int min, Vector3Int max) {
            int axis = face / 2;
            return face % 2 == 0 ? min[axis] : max[axis];
        }

        private void SetFace(int face, int value) {
            int axis = face / 2;
            Vector3Int min = Min, max = Max;
            if (face % 2 == 0) {
                min[axis] = Mathf.Clamp(value, startMin[axis] - PartsLister.MaxGrowth, max[axis] - 1);
            } else {
                max[axis] = Mathf.Clamp(value, min[axis] + 1, startMax[axis] + PartsLister.MaxGrowth);
            }
            if (min != Min || max != Max) {
                Min = min;
                Max = max;
                BoxChanged?.Invoke();
            }
        }

        /// <summary>The box's size in cells (studs sideways, plates up).</summary>
        public Vector3Int Size {
            get { return Max - Min; }
        }

        private Bounds worldBounds {
            get {
                Bounds bounds = new Bounds();
                bounds.SetMinMax(BrickGrid.CellToWorld(Min), BrickGrid.CellToWorld(Max));
                return bounds;
            }
        }

        // -------- input --------

        private void Update() {
            if (Player.Instance == null) {
                Close();
                return;
            }
            fly();
            editFaces();
        }

        private void fly() {
            bool looking = !GameInput.IsTyping && GameInput.KeyHeld(KeyCode.Mouse1);
            if (looking) {
                Game.lockMouse();
                yaw += GameInput.LookDelta.x * LookSpeed;
                pitch = Mathf.Clamp(pitch - GameInput.LookDelta.y * LookSpeed, -89, 89);
            } else if (GameInput.KeyUp(KeyCode.Mouse1)) {
                Game.unlockMouse();
            }
            Transform tr = eye.transform;
            tr.rotation = Quaternion.Euler(pitch, yaw, 0);
            if (GameInput.IsTyping) {
                return;
            }
            Vector2 move = GameInput.GetMovement();
            Vector3 direction = tr.forward * move.y + tr.right * move.x;
            if (GameInput.GetButton(GameInput.Jump)) {
                direction += Vector3.up;
            }
            if (GameInput.CtrlHeld) {
                direction += Vector3.down;
            }
            float speed = GameInput.GetButton(GameInput.Sprint) ? FastFlySpeed : FlySpeed;
            Vector3 position = tr.position + Vector3.ClampMagnitude(direction, 1) * speed * Time.unscaledDeltaTime;
            // never far from the box
            Bounds range = worldBounds;
            range.Expand(CameraRange * 2);
            tr.position = range.ClosestPoint(position);
        }

        // the face under the pointer lights up; the wheel or a drag moves it
        private void editFaces() {
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Ray ray = eye.ScreenPointToRay(GameInput.PointerPosition);
            bool fast = GameInput.ShiftHeld;

            if (dragged >= 0) {
                if (!GameInput.KeyHeld(KeyCode.Mouse0)) {
                    dragged = -1;
                } else {
                    if (!alongAxis(dragged, ray, out float along)) {
                        hovered = dragged;
                        return; // looking along the axis: the face stays where it is
                    }
                    int axis = dragged / 2;
                    // along the face's outward normal: out is lower coordinates for the west, bottom and south faces
                    int steps = Mathf.RoundToInt((along - dragFrom) / BrickGrid.CellSize[axis]);
                    Min = dragMin;
                    Max = dragMax;
                    SetFace(dragged, faceValue(dragged, dragMin, dragMax) + (dragged % 2 == 0 ? -steps : steps));
                    BoxChanged?.Invoke();
                }
                hovered = dragged;
                return;
            }

            hovered = overUI || Cursor.lockState == CursorLockMode.Locked ? -1 : faceUnder(ray);
            if (hovered < 0) {
                return;
            }
            float wheel = GameInput.ScrollDelta;
            if (wheel != 0) {
                Grow(hovered, (wheel > 0 ? 1 : -1) * (fast ? FastSteps : 1));
            }
            if (GameInput.KeyDown(KeyCode.Mouse0)) {
                dragAnchor = worldBounds.center;
                if (alongAxis(hovered, ray, out dragFrom)) {
                    dragged = hovered;
                    dragMin = Min;
                    dragMax = Max;
                }
            }
        }

        // where the pointer's ray passes closest to the line along a face's axis through dragAnchor (meters along it,
        // outward); false when the ray is almost parallel to it, as that point would jump around
        private bool alongAxis(int face, Ray ray, out float along) {
            Vector3 normal = normalOf(face);
            // the closest points of two lines: dragAnchor + normal * s and ray.origin + ray.direction * t
            Vector3 w = dragAnchor - ray.origin;
            float b = Vector3.Dot(normal, ray.direction);
            float d = Vector3.Dot(normal, w);
            float e = Vector3.Dot(ray.direction, w);
            float denominator = 1 - b * b; // the sine of the angle between them, squared
            if (denominator < MinDragAngleSine * MinDragAngleSine) {
                along = 0;
                return false;
            }
            along = (b * e - d) / denominator;
            return true;
        }

        private static Vector3 normalOf(int face) {
            Vector3 normal = Vector3.zero;
            normal[face / 2] = face % 2 == 0 ? -1 : 1;
            return normal;
        }

        // the nearest face the ray goes through (from outside or from inside the box), -1 for none
        private int faceUnder(Ray ray) {
            Bounds bounds = worldBounds;
            int nearest = -1;
            float distance = float.MaxValue;
            for (int face = 0; face < 6; face++) {
                int axis = face / 2;
                float plane = face % 2 == 0 ? bounds.min[axis] : bounds.max[axis];
                if (Mathf.Abs(ray.direction[axis]) < 0.0001f) {
                    continue;
                }
                float t = (plane - ray.origin[axis]) / ray.direction[axis];
                if (t <= 0 || t >= distance) {
                    continue;
                }
                Vector3 hit = ray.origin + ray.direction * t;
                bool onFace = true;
                for (int other = 0; other < 3 && onFace; other++) {
                    if (other != axis && (hit[other] < bounds.min[other] - 0.001f || hit[other] > bounds.max[other] + 0.001f)) {
                        onFace = false;
                    }
                }
                if (onFace) {
                    nearest = face;
                    distance = t;
                }
            }
            return nearest;
        }

        // -------- the box --------

        private void buildBox() {
            box = new GameObject("PartsListerBox");
            Material faceMaterial = Resources.Load<Material>("Materials/PartsListerBox");
            faces = new Renderer[6];
            for (int face = 0; face < 6; face++) {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.name = FaceNames[face];
                quad.layer = (int)Game.Layers.IgnoreRaycast;
                quad.transform.SetParent(box.transform, false);
                Renderer renderer = quad.GetComponent<Renderer>();
                renderer.material = new Material(faceMaterial);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                faces[face] = renderer;
            }
            GameObject outline = new GameObject("Edges");
            outline.transform.SetParent(box.transform, false);
            edges = outline.AddComponent<LineRenderer>();
            edges.material = Resources.Load<Material>("Materials/PartsListerEdge");
            edges.widthMultiplier = 0.03f;
            edges.positionCount = 16;
            edges.useWorldSpace = true;
            edges.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // every frame: the floating origin can move it, and it follows the box
        private void LateUpdate() {
            if (box == null) {
                return;
            }
            Bounds bounds = worldBounds;
            Vector3 size = bounds.size;
            for (int face = 0; face < 6; face++) {
                int axis = face / 2;
                Vector3 normal = normalOf(face);
                Transform quad = faces[face].transform;
                quad.position = bounds.center + Vector3.Scale(normal, size) * 0.5f;
                quad.rotation = Quaternion.LookRotation(normal);
                // a quad is 1x1 on its XY plane, which looks along the normal
                Vector3 across = axis == 0 ? new Vector3(size.z, size.y, 1) : axis == 1 ? new Vector3(size.x, size.z, 1) : new Vector3(size.x, size.y, 1);
                quad.localScale = across;
                faces[face].material.SetColor("_UnlitColor", face == hovered ? HoveredFaceColor : FaceColor);
            }
            Vector3 a = bounds.min, b = bounds.max;
            Vector3 b0 = new Vector3(a.x, a.y, a.z), b1 = new Vector3(b.x, a.y, a.z), b2 = new Vector3(b.x, a.y, b.z), b3 = new Vector3(a.x, a.y, b.z);
            Vector3 t0 = new Vector3(a.x, b.y, a.z), t1 = new Vector3(b.x, b.y, a.z), t2 = new Vector3(b.x, b.y, b.z), t3 = new Vector3(a.x, b.y, b.z);
            // one line through all 12 edges (some twice)
            edges.SetPositions(new[] { b0, b1, b2, b3, b0, t0, t1, b1, t1, t2, b2, t2, t3, b3, t3, t0 });
        }
    }
}
