using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// A stack of items lying on the ground (see DroppedItems): the item's model, small, turning and bobbing.
    /// The server makes it fall onto what's under it (or float up to the surface of water), merges it with drops of the same item next to it, gives
    /// it to players who walk onto it (as much as fits, the rest stays) and takes it away after a while.
    /// Players get the drops of the chunks they have (see ChunkInterestManagement).
    /// </summary>
    public class DroppedItem : NetworkBehaviour
    {
        /// <summary>Its item (see Slugs), colour and how many.</summary>
        [SyncVar(hook = nameof(onItemChanged))] public string itemId;
        [SyncVar(hook = nameof(onColorChanged))] public int color;
        [SyncVar] public int quantity;
        /// <summary>Server: the stack's health (see InventoryItem.health).</summary>
        public int health;

        /// <summary>How big its model is drawn, its largest side in meters.</summary>
        public const float ModelSize = 0.3f;
        private const float SpinSpeed = 90;
        private const float BobHeight = 0.05f;
        private const float Gravity = 15;
        private const float FallLimit = -200;
        // in water it rises this fast to float this far under the surface (see World.Water)
        private const float FloatSpeed = 1.5f;
        private const float FloatDepth = 0.1f;
        // what it lands on: not water, nor players (Ignore Raycast)
        private const int SolidLayers = Physics.DefaultRaycastLayers & ~(1 << (int)Game.Layers.Water);

        /// <summary>Server: it can be picked up from then (it just dropped).</summary>
        public double PickableAt { get; set; }
        /// <summary>Server: it goes then.</summary>
        public double DespawnAt { get; set; }
        /// <summary>Server: moving (thrown out of a chest...), until it lands.</summary>
        public Vector3 Velocity { get; set; }

        private bool isResting;
        private Transform model;
        private float phase;

        public override void OnStartServer() {
            DroppedItems.Register(this);
        }

        public override void OnStopServer() {
            DroppedItems.Unregister(this);
        }

        public override void OnStartClient() {
            phase = Random.Range(0f, Mathf.PI * 2);
            buildModel();
        }

        private void onItemChanged(string oldItem, string newItem) {
            buildModel();
        }

        private void onColorChanged(int oldColor, int newColor) {
            buildModel();
        }

        // its brick, small, without colliders: what the game draws placed (see Server.createBrickObject)
        private void buildModel() {
            if (!isClient || string.IsNullOrEmpty(itemId) || !Server.items.TryGetValue(itemId, out Item item) || item.brickModel == null) {
                return;
            }
            if (model != null) {
                Destroy(model.gameObject);
            }
            GameObject shown = Server.Instance.createBrickObject(item, color, transform.position, Quaternion.identity);
            foreach (Collider collider in shown.GetComponentsInChildren<Collider>()) {
                Destroy(collider);
            }
            foreach (MonoBehaviour behaviour in shown.GetComponentsInChildren<MonoBehaviour>()) {
                behaviour.enabled = false;
            }
            Bounds bounds = new Bounds(shown.transform.position, Vector3.zero);
            foreach (Renderer part in shown.GetComponentsInChildren<Renderer>()) {
                bounds.Encapsulate(part.bounds);
            }
            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = largest > 0 ? ModelSize / largest : 1;
            // centred on it, so it turns around its middle
            GameObject pivot = new GameObject("Model");
            pivot.transform.SetParent(transform, false);
            shown.transform.SetParent(pivot.transform, true);
            shown.transform.localPosition = (shown.transform.position - bounds.center);
            pivot.transform.localScale = Vector3.one * scale;
            model = pivot.transform;
        }

        private void Update() {
            if (isServer) {
                serverUpdate();
            }
            if (model != null) {
                model.localRotation = Quaternion.Euler(0, Time.time * SpinSpeed + phase * Mathf.Rad2Deg, 0);
                model.localPosition = Vector3.up * (ModelSize / 2 + BobHeight + Mathf.Sin(Time.time * 2 + phase) * BobHeight);
            }
        }

        // falling (and sliding out where it was thrown) until something holds it up, or floating up in water
        private void serverUpdate() {
            if (isResting) {
                return;
            }
            float delta = Time.deltaTime;
            Vector3 here = transform.position;
            if (World.Water.IsAt(here + Vector3.up * FloatDepth)) {
                Velocity = Vector3.zero;
                transform.position = here + Vector3.up * FloatSpeed * delta;
                return;
            }
            if (World.Water.IsAt(here)) {
                Velocity = Vector3.zero;
                isResting = true; // floating at the surface
                return;
            }
            Vector3 velocity = Velocity;
            velocity.y -= Gravity * delta;
            Vector3 step = velocity * delta;
            Vector3 position = transform.position;
            if (step.y < 0 && Physics.Raycast(position + Vector3.up * 0.05f, Vector3.down, out RaycastHit ground, -step.y + 0.05f, SolidLayers, QueryTriggerInteraction.Ignore)) {
                transform.position = new Vector3(position.x + step.x, ground.point.y, position.z + step.z);
                Velocity = Vector3.zero;
                isResting = true;
                return;
            }
            // sideways into a wall: it stops going that way
            Vector3 sideways = new Vector3(step.x, 0, step.z);
            if (sideways.sqrMagnitude > 0 && Physics.Raycast(position + Vector3.up * 0.1f, sideways.normalized, sideways.magnitude + 0.1f, SolidLayers, QueryTriggerInteraction.Ignore)) {
                velocity.x = velocity.z = 0;
                step.x = step.z = 0;
            }
            Velocity = velocity;
            transform.position = position + step;
            if (transform.position.y < FallLimit) {
                NetworkServer.Destroy(gameObject);
            }
        }

        /// <summary>Server: what was under it changed (a block dug...): it may fall again.</summary>
        public void Unsettle() {
            isResting = false;
        }
    }
}
