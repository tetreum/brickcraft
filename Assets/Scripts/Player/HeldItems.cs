using Brickcraft.Bricks;
using Brickcraft.Combat;
using Brickcraft.Network;
using UnityEngine;
using UnityEngine.Rendering;

namespace Brickcraft
{
    /// <summary>
    /// The weapon and shield a player is seen holding: its weapon (the item in its hand, if it's a weapon) in its
    /// right hand, the shield it wears in its left (see PlayerNetwork.heldItem, offhandItem). The local player sees
    /// them in front of its camera, the others on its body's hands. Weapons swing when they hit (Swing), shields
    /// go up in front while blocking. How each model sits in the hand comes from its item (Item.heldGrip...).
    /// </summary>
    public class HeldItems : MonoBehaviour
    {
        // first person: where the hands are, in the camera's space (past its near plane)
        private static readonly Vector3 RightHandView = new Vector3(0.3f, -0.32f, 0.65f);
        private static readonly Vector3 LeftHandView = new Vector3(-0.36f, -0.34f, 0.65f);
        private static readonly Vector3 BlockingView = new Vector3(-0.06f, -0.16f, 0.6f);
        // that close to the camera they're shown smaller, or they'd fill the screen
        private const float FirstPersonScale = 0.5f;
        // on a body: a raised shield's place, from its chest
        private const float BlockingForward = 0.45f;
        private const float BlockingLeft = 0.1f;

        // models held without saying how (Item.hasHeld) are fitted to this, in meters
        private const float FittedSize = 0.8f;
        // a swing: how long, and how far the weapon turns down (melee) or kicks back (ranged)
        private const float SwingDuration = 0.25f;
        private const float SwingAngle = 70;
        private const float KickBack = 0.12f;
        // a fully drawn bow: how far it comes back toward the camera, and how much it shakes
        private const float DrawBack = 0.12f;
        private const float DrawShake = 0.004f;
        // how fast a shield goes up and down, in parts of the way a second
        private const float RaiseSpeed = 8;

        private PlayerNetwork network;
        private Health health;
        private bool firstPerson;
        private Transform right;
        private Transform left;
        private GameObject rightModel;
        private GameObject leftModel;
        private Item rightItem;
        // bones of the body others see (null in first person)
        private Transform rightHand;
        private Transform leftHand;
        private Transform chest;
        private float swingTime;
        private float raised;

        /// <summary>The local player's: in front of its camera.</summary>
        public static HeldItems FirstPerson(PlayerNetwork network, Camera camera) {
            HeldItems held = create(network, camera.transform);
            held.firstPerson = true;
            return held;
        }

        /// <summary>Another player's: in its body's hands.</summary>
        public static HeldItems ForBody(PlayerNetwork network) {
            HeldItems held = create(network, network.transform);
            Animator body = network.GetComponentInChildren<PlayerCharacter>(true)?.GetComponent<Animator>();
            if (body != null && body.isHuman) {
                held.rightHand = body.GetBoneTransform(HumanBodyBones.RightHand);
                held.leftHand = body.GetBoneTransform(HumanBodyBones.LeftHand);
                held.chest = body.GetBoneTransform(HumanBodyBones.Chest) ?? body.GetBoneTransform(HumanBodyBones.Spine);
            }
            return held;
        }

        private static HeldItems create(PlayerNetwork network, Transform parent) {
            HeldItems held = network.gameObject.AddComponent<HeldItems>();
            held.network = network;
            held.health = network.GetComponent<Health>();
            held.right = new GameObject("RightHand").transform;
            held.right.SetParent(parent, false);
            held.left = new GameObject("LeftHand").transform;
            held.left.SetParent(parent, false);
            held.Refresh();
            return held;
        }

        private void OnDestroy() {
            if (right != null) {
                Destroy(right.gameObject);
            }
            if (left != null) {
                Destroy(left.gameObject);
            }
        }

        /// <summary>What it holds changed: the models are made again.</summary>
        public void Refresh() {
            if (right == null) {
                return;
            }
            Server.items.TryGetValue(network.heldItem ?? "", out Item held);
            Server.items.TryGetValue(network.offhandItem ?? "", out Item offhand);
            rightItem = held != null && held.type == Item.Type.Weapon ? held : null;
            show(ref rightModel, right, rightItem, network.heldColor);
            show(ref leftModel, left, offhand != null && offhand.IsShield ? offhand : null, offhand != null ? offhand.color : 0);
        }

        /// <summary>Its weapon swings (a bow kicks back), a hit.</summary>
        public void Swing() {
            swingTime = SwingDuration;
        }

        private void show(ref GameObject model, Transform hand, Item item, int color) {
            if (model != null) {
                Destroy(model);
                model = null;
            }
            if (item == null || item.brickModel == null || item.brickModel.prefab == null) {
                return;
            }
            model = Instantiate(item.brickModel.prefab);
            model.name = item.id;
            // just a picture: nothing hits it, nor does it stand in the way
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) {
                Destroy(collider);
            }
            foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) {
                if (!(behaviour is BrickNormalMap)) {
                    Destroy(behaviour);
                }
            }
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true)) {
                part.gameObject.layer = (int)Game.Layers.IgnoreRaycast;
            }
            BrickModels.ApplyColor(model, item.MaterialFor(color));
            if (firstPerson) {
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>()) {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            Vector3 grip = item.heldGrip;
            float scale = item.heldScale;
            if (!item.hasHeld) {
                Bounds bounds = boundsOf(model);
                grip = bounds.center;
                scale = FittedSize / Mathf.Max(0.01f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
            }
            if (firstPerson) {
                scale *= FirstPersonScale;
            }
            Transform tr = model.transform;
            tr.SetParent(hand, false);
            tr.localScale = Vector3.one * scale;
            tr.localRotation = item.heldRotation;
            tr.localPosition = -(item.heldRotation * grip) * scale;
        }

        // the model's size as placed (it's at the origin, unturned)
        private static Bounds boundsOf(GameObject model) {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) {
                return new Bounds(Vector3.zero, Vector3.one);
            }
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) {
                bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        private void LateUpdate() {
            bool visible = !health.isDead && (firstPerson || rightHand != null);
            right.gameObject.SetActive(visible);
            left.gameObject.SetActive(visible);
            if (!visible) {
                return;
            }

            bool blocking = firstPerson ? Player.Instance != null && Player.Instance.IsBlocking : network.blocking;
            raised = Mathf.MoveTowards(raised, blocking && leftModel != null ? 1 : 0, RaiseSpeed * Time.deltaTime);
            float smooth = Mathf.SmoothStep(0, 1, raised);

            // a swing turns the weapon down and back (sin goes up and down over its duration)
            swingTime = Mathf.Max(0, swingTime - Time.deltaTime);
            float swing = swingTime > 0 ? Mathf.Sin((1 - swingTime / SwingDuration) * Mathf.PI) : 0;
            bool ranged = rightItem != null && rightItem.IsRanged;
            Quaternion swingTurn = Quaternion.Euler(ranged ? 0 : swing * SwingAngle, 0, 0);
            Vector3 kick = ranged ? Vector3.back * swing * KickBack : Vector3.zero;

            if (firstPerson) {
                // drawing: the bow comes up the middle and closer, shaking a bit at full draw
                float drawn = Player.Instance != null ? Mathf.SmoothStep(0, 1, Player.Instance.DrawPower) : 0;
                if (drawn > 0) {
                    kick += new Vector3(-0.18f, 0.1f, -DrawBack) * drawn + Random.insideUnitSphere * (drawn >= 1 ? DrawShake : 0);
                }
                right.localPosition = RightHandView + kick;
                right.localRotation = swingTurn;
                left.localPosition = Vector3.Lerp(LeftHandView, BlockingView, smooth);
                left.localRotation = Quaternion.identity;
                return;
            }
            // on the body: where its hands are, facing where it looks (the animation's hand turns would spin them)
            Quaternion facing = transform.rotation;
            right.SetPositionAndRotation(rightHand.position + facing * kick, facing * swingTurn);
            Vector3 raisedAt = (chest != null ? chest.position : transform.position) + facing * new Vector3(-BlockingLeft, 0, BlockingForward);
            left.SetPositionAndRotation(Vector3.Lerp(leftHand.position, raisedAt, smooth), facing);
        }
    }
}
