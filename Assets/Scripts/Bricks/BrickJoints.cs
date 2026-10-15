using System.Collections.Generic;
using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// How a connected brick (see BrickJoints) is held by another: by which of the holder's connectors and which of its
    /// own (see Connector), how far along the holder's axis their middles are (half studs, 10 LDU), turned how many
    /// quarter turns around it, and whether it faces the other way along it.
    /// </summary>
    public struct BrickJoint
    {
        public int holderConnector;
        public int connector;
        public int slide;
        public int spin;
        public bool flip;
    }

    /// <summary>
    /// Bricks connected off the grid: an axle through a wheel's hub or a technic brick's hole, a pin in a beam. Such a
    /// brick is held by another (Brick.attachedTo, its joint Brick.joint), placed where their connectors meet, turned
    /// in quarter turns around them; it takes no cells, and goes when its holder does. Two connectors fit (see Fits):
    /// an axle in an axle hole (locked) or in a round hole at least as wide (it spins), a round peg in a round hole as
    /// wide. Things slide along the ones that slide, the shorter one staying within the longer. Where it goes it may only
    /// meet what it connects to: its holder, and bricks with a connector that lines up with one of its own and fits.
    /// </summary>
    public static class BrickJoints
    {
        /// <summary>A slide step: half a stud.</summary>
        public const float SlideStepLdu = 10;
        // LDU a peg may be narrower or wider than its hole and still fit
        private const float RadiusTolerance = 0.6f;
        // connectors line up: their axes this parallel (cosine), this close
        private const float ParallelCos = 0.999f;
        private const float LineDistance = 0.01f;
        // touching what it doesn't connect to is fine, going into it isn't
        private const float OverlapMargin = 0.03f;

        // -------- fitting --------

        /// <summary>The radius of a connector's longest stretch (a pin's, not its collar's), LDU.</summary>
        public static float MainRadius(Connector connector) {
            float radius = 0, longest = -1;
            foreach (ConnectorSection section in connector.sections) {
                if (section.length > longest) {
                    longest = section.length;
                    radius = section.radius;
                }
            }
            return radius;
        }

        /// <summary>Whether the two connect (one male, one female); locked: they can't spin (an axle in an axle hole).</summary>
        public static bool Fits(Connector a, Connector b, out bool locked) {
            locked = false;
            if (a == null || b == null || a.male == b.male) {
                return false;
            }
            Connector male = a.male ? a : b, female = a.male ? b : a;
            if (female.HasAxleShape) {
                locked = true;
                return male.HasAxleShape;
            }
            float hole = MainRadius(female);
            if (male.HasAxleShape) {
                return 6 <= hole + RadiusTolerance;
            }
            return Mathf.Abs(MainRadius(male) - hole) <= RadiusTolerance;
        }

        public static bool Fits(Connector a, Connector b) {
            return Fits(a, b, out _);
        }

        /// <summary>How many slide steps either way they may be apart (0: only middle to middle).</summary>
        public static int MaxSlide(Connector a, Connector b) {
            if (!a.slide && !b.slide) {
                return 0;
            }
            float ldu = Mathf.Abs(a.Length - b.Length) / LDraw.LDrawModelBuilder.Scale / 2;
            return Mathf.FloorToInt(ldu / SlideStepLdu + 0.001f);
        }

        // -------- where --------

        /// <summary>
        /// Where a brick's object goes (its parent's space: the world's, or its vehicle's) when it's held by a brick whose
        /// object is there, that way.
        /// </summary>
        public static void PoseOf(Vector3 holderPosition, Quaternion holderRotation, BrickModel holderModel, BrickModel model, BrickJoint joint,
                out Vector3 position, out Quaternion rotation) {
            Connector held = holderModel.connectors[joint.holderConnector];
            Connector own = model.connectors[joint.connector];
            Vector3 axis = holderRotation * held.axis;
            Vector3 target = joint.flip ? -axis : axis;
            Vector3 side = Quaternion.AngleAxis(90 * Mod(joint.spin, 4), target) * (holderRotation * held.side);
            rotation = Quaternion.LookRotation(target, side) * Quaternion.Inverse(Quaternion.LookRotation(own.axis, own.side));
            Vector3 middle = holderPosition + holderRotation * held.Middle + axis * (joint.slide * SlideStepLdu * LDraw.LDrawModelBuilder.Scale);
            position = middle - rotation * own.Middle;
        }

        /// <summary>
        /// Where a brick's object is as it was placed (its parent's space), whatever its model shows now (a wheel turning):
        /// on the grid, on a vehicle's cells, or held by another (Brick.jointPosition, jointRotation).
        /// </summary>
        public static void BasePose(Brick brick, out Vector3 position, out Quaternion rotation) {
            if (brick.joint.HasValue) {
                position = brick.jointPosition;
                rotation = brick.jointRotation;
                return;
            }
            Quaternion prefab = brick.model != null && brick.model.prefab != null ? brick.model.prefab.transform.rotation : Quaternion.identity;
            rotation = brick.placement.Rotation * prefab;
            position = brick.vehicle != null ? brick.placement.LocalPosition : brick.placement.Position;
        }

        /// <summary>Where a brick held by that one, that way, would go (its parent's space).</summary>
        public static void PoseHeldBy(Brick holder, BrickModel model, BrickJoint joint, out Vector3 position, out Quaternion rotation) {
            BasePose(holder, out Vector3 at, out Quaternion turned);
            PoseOf(at, turned, holder.model, model, joint, out position, out rotation);
        }

        // -------- whether --------

        /// <summary>Whether a brick can hold others: one on the grid, on a vehicle's cells or held itself (not a door in its frame).</summary>
        public static bool CanHold(Brick holder) {
            return holder != null && holder.gameObject != null && holder.model != null && holder.model.connectors.Length > 0
                && (holder.attachedTo == null || holder.joint.HasValue);
        }

        /// <summary>
        /// Why that model can't be held by that brick that way, null if it can: the connectors fit, it's within their
        /// slide, and where it goes it meets nothing it doesn't connect to.
        /// </summary>
        public static string WhyNot(Brick holder, BrickModel model, BrickJoint joint) {
            if (!CanHold(holder)) {
                return "the holder can't hold anything";
            }
            if (model == null || model.connectors.Length == 0 || model.IsAttachment) {
                return "it has nothing to connect with";
            }
            if (joint.holderConnector < 0 || joint.holderConnector >= holder.model.connectors.Length || joint.connector < 0 || joint.connector >= model.connectors.Length) {
                return "no such connector";
            }
            Connector held = holder.model.connectors[joint.holderConnector], own = model.connectors[joint.connector];
            if (!Fits(held, own)) {
                return "the connectors don't fit";
            }
            if (Mathf.Abs(joint.slide) > MaxSlide(held, own)) {
                return "it slides too far";
            }
            PoseHeldBy(holder, model, joint, out Vector3 position, out Quaternion rotation);
            Transform parent = ParentOf(holder);
            Vector3 worldPosition = parent != null ? parent.TransformPoint(position) : position;
            Quaternion worldRotation = parent != null ? parent.rotation * rotation : rotation;
            Brick blocking = Blocking(model, worldPosition, worldRotation, holder);
            return blocking != null ? "something's in the way" : null;
        }

        /// <summary>Where a brick held by that one lives: the world (null) or its vehicle.</summary>
        public static Transform ParentOf(Brick holder) {
            return holder.vehicle != null ? holder.vehicle.transform : null;
        }

        /// <summary>
        /// The first brick (or the terrain: a brick-less hit gives a dummy) a model there would go into, that it doesn't
        /// connect to; null if none.
        /// </summary>
        public static Brick Blocking(BrickModel model, Vector3 worldPosition, Quaternion worldRotation, Brick holder) {
            Bounds mesh = MeshBounds(model);
            Vector3 half = mesh.extents - Vector3.one * OverlapMargin;
            if (half.x <= 0 || half.y <= 0 || half.z <= 0) {
                return null;
            }
            // bricks just placed or moved are where they are (transforms aren't synced to physics on their own)
            Physics.SyncTransforms();
            foreach (Collider collider in Physics.OverlapBox(worldPosition + worldRotation * mesh.center, half, worldRotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) {
                if (collider.GetComponentInParent<Network.PlayerNetwork>() != null) {
                    return Terrain;
                }
                Brick brick = Server.findBrick(collider);
                if (brick == null) {
                    return Terrain;
                }
                if (brick == holder || ConnectsTo(model, worldPosition, worldRotation, brick)) {
                    continue;
                }
                return brick;
            }
            return null;
        }

        /// <summary>Stands for what isn't a brick (the terrain, a player) in Blocking's answer.</summary>
        public static readonly Brick Terrain = new Brick() { id = "terrain" };

        /// <summary>Whether a model there has a connector that lines up with one of the brick's and fits it.</summary>
        public static bool ConnectsTo(BrickModel model, Vector3 worldPosition, Quaternion worldRotation, Brick brick) {
            if (brick.model == null || brick.gameObject == null || brick.model.connectors.Length == 0) {
                return false;
            }
            Transform other = brick.gameObject.transform;
            foreach (Connector own in model.connectors) {
                Vector3 start = worldPosition + worldRotation * own.start, axis = worldRotation * own.axis;
                foreach (Connector theirs in brick.model.connectors) {
                    if (!Fits(own, theirs)) {
                        continue;
                    }
                    if (LineUp(start, axis, own.Length, other.TransformPoint(theirs.start), other.TransformDirection(theirs.axis), theirs.Length)) {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Two connectors (start, unit axis, length) on one line, overlapping along it.</summary>
        public static bool LineUp(Vector3 startA, Vector3 axisA, float lengthA, Vector3 startB, Vector3 axisB, float lengthB) {
            if (Mathf.Abs(Vector3.Dot(axisA, axisB)) < ParallelCos) {
                return false;
            }
            Vector3 between = startB - startA;
            if ((between - axisA * Vector3.Dot(between, axisA)).magnitude > LineDistance * Server.studSize) {
                return false;
            }
            // their stretches along A's axis
            float a0 = 0, a1 = lengthA;
            float b0 = Vector3.Dot(between, axisA), b1 = b0 + Vector3.Dot(axisB, axisA) * lengthB;
            float bMin = Mathf.Min(b0, b1), bMax = Mathf.Max(b0, b1);
            return Mathf.Min(a1, bMax) - Mathf.Max(a0, bMin) > 0.001f;
        }

        // the box around a model's meshes, in its own space
        private static readonly Dictionary<BrickModel, Bounds> meshBounds = new Dictionary<BrickModel, Bounds>();

        public static Bounds MeshBounds(BrickModel model) {
            if (meshBounds.TryGetValue(model, out Bounds bounds)) {
                return bounds;
            }
            bool any = false;
            bounds = new Bounds();
            foreach (MeshFilter filter in model.prefab.GetComponentsInChildren<MeshFilter>(true)) {
                if (filter.sharedMesh == null) {
                    continue;
                }
                Bounds b = filter.sharedMesh.bounds;
                if (filter.transform != model.prefab.transform) {
                    // a child's: its box's corners in the model's space
                    for (int c = 0; c < 8; c++) {
                        Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        Vector3 p = model.prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                        if (any) { bounds.Encapsulate(p); } else { bounds = new Bounds(p, Vector3.zero); any = true; }
                    }
                } else if (any) {
                    bounds.Encapsulate(b);
                } else {
                    bounds = b;
                    any = true;
                }
            }
            meshBounds[model] = bounds;
            return bounds;
        }

        public static int Mod(int value, int by) {
            return ((value % by) + by) % by;
        }
    }
}
