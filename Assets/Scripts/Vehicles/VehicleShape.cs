using System.Collections.Generic;
using Brickcraft.Bricks;
using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>Where a vehicle's player sits on a seat brick: on it, facing away from its backrest.</summary>
    public struct SeatShape
    {
        /// <summary>On its seat, in the vehicle's space.</summary>
        public Vector3 sitPoint;
        /// <summary>Where someone sitting on it looks, in the vehicle's space (along one of its axes).</summary>
        public Vector3 forward;
    }

    /// <summary>
    /// The shapes of a vehicle's parts from their meshes, in the vehicle's space (whatever their model and however
    /// they're turned): a wheel's size and axle, a seat's front.
    /// </summary>
    public static class VehicleShape
    {
        /// <summary>The box around a brick's meshes, in the vehicle's space.</summary>
        public static Bounds MeshBounds(Brick brick, Transform vehicle) {
            bool any = false;
            Bounds bounds = new Bounds();
            foreach (MeshFilter filter in brick.gameObject.GetComponentsInChildren<MeshFilter>()) {
                if (filter.sharedMesh == null) {
                    continue;
                }
                Bounds local = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++) {
                    Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = vehicle.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (any) {
                        bounds.Encapsulate(point);
                    } else {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                }
            }
            return any ? bounds : brick.placement.LocalBounds;
        }

        /// <summary>
        /// A seat's front and where to sit: its highest part is its backrest, it faces the other way (the vehicle's
        /// forward if it has none); one sits on the highest point of its front half.
        /// </summary>
        public static SeatShape Seat(Brick brick, Transform vehicle) {
            List<Vector3> points = vertices(brick, vehicle);
            Bounds bounds = MeshBounds(brick, vehicle);
            if (points.Count == 0) {
                return new SeatShape() { sitPoint = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z), forward = Vector3.forward };
            }

            Vector3 back = Vector3.zero;
            int high = 0;
            float backrest = bounds.min.y + bounds.size.y * 0.75f;
            foreach (Vector3 point in points) {
                if (point.y >= backrest) {
                    back += point - bounds.center;
                    high++;
                }
            }
            back /= Mathf.Max(1, high);
            Vector3 forward = Vector3.forward;
            float threshold = Mathf.Min(bounds.size.x, bounds.size.z) * 0.1f;
            if (Mathf.Abs(back.x) > Mathf.Abs(back.z) && Mathf.Abs(back.x) > threshold) {
                forward = new Vector3(-Mathf.Sign(back.x), 0, 0);
            } else if (Mathf.Abs(back.z) > threshold) {
                forward = new Vector3(0, 0, -Mathf.Sign(back.z));
            }

            float seat = float.MinValue;
            foreach (Vector3 point in points) {
                if (Vector3.Dot(point - bounds.center, forward) > 0) {
                    seat = Mathf.Max(seat, point.y);
                }
            }
            if (seat == float.MinValue) {
                seat = bounds.max.y;
            }
            return new SeatShape() { sitPoint = new Vector3(bounds.center.x, seat, bounds.center.z), forward = forward };
        }

        private static List<Vector3> vertices(Brick brick, Transform vehicle) {
            List<Vector3> points = new List<Vector3>();
            List<Vector3> mesh = new List<Vector3>();
            foreach (MeshFilter filter in brick.gameObject.GetComponentsInChildren<MeshFilter>()) {
                if (filter.sharedMesh == null || !filter.sharedMesh.isReadable) {
                    continue;
                }
                filter.sharedMesh.GetVertices(mesh);
                foreach (Vector3 vertex in mesh) {
                    points.Add(vehicle.InverseTransformPoint(filter.transform.TransformPoint(vertex)));
                }
            }
            return points;
        }
    }
}
