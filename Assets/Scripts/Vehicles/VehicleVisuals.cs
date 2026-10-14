using System.Collections.Generic;
using Brickcraft.Bricks;
using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>
    /// A vehicle's moving parts as they look, on every peer: wheels roll as fast as the vehicle goes (and steer, where it's
    /// simulated), propellers spin with its power (Vehicle.power). Only the bricks turn, around their middle; their
    /// places in the vehicle don't change, and they're put back as they were when it stops (and before its shape is read,
    /// see Reset).
    /// </summary>
    public class VehicleVisuals
    {
        // degrees a second at full power
        private const float PropellerSpeed = 1440;
        // it's stopped below this (metres a second) for this long: its parts go back
        private const float Still = 0.05f;
        private const float StillTime = 0.5f;

        private class Spinner
        {
            public Brick brick;
            public Vector3 basePosition;
            public Quaternion baseRotation;
            // in the vehicle's space
            public Vector3 pivot;
            public Vector3 axis;
            public bool isWheel;
            // a wheel: the way it rolls, and how big it is
            public Vector3 rolls;
            public float radius;
            public float angle;
        }

        private readonly Vehicle vehicle;
        private readonly List<Spinner> spinners = new List<Spinner>();
        private Vector3 lastPosition;
        private float stillFor;
        private bool turned;

        public VehicleVisuals(Vehicle vehicle) {
            this.vehicle = vehicle;
            lastPosition = vehicle.transform.position;
        }

        /// <summary>Its bricks changed: the ones that move, found again (with the others as they were).</summary>
        public void Rebuild() {
            Reset();
            spinners.Clear();
            foreach (Brick brick in vehicle.grid.Bricks) {
                if (brick.gameObject == null || !Server.items.TryGetValue(brick.itemId, out Item item)) {
                    continue;
                }
                VehicleRole role = item.VehicleRole;
                if (role != VehicleRole.Wheel && role != VehicleRole.Propeller) {
                    continue;
                }
                Bounds mesh = VehicleShape.MeshBounds(brick, vehicle.transform);
                Transform tr = brick.gameObject.transform;
                Spinner spinner = new Spinner() {
                    brick = brick,
                    basePosition = tr.localPosition,
                    baseRotation = tr.localRotation,
                    pivot = mesh.center,
                    isWheel = role == VehicleRole.Wheel,
                };
                if (spinner.isWheel) {
                    spinner.rolls = mesh.size.x <= mesh.size.z ? Vector3.forward : Vector3.right;
                    spinner.axis = Vector3.Cross(Vector3.up, spinner.rolls);
                    spinner.radius = Mathf.Max(0.05f, mesh.extents.y);
                } else {
                    spinner.axis = VehicleDrive.PropellerAxis(mesh, Vector3.forward);
                }
                spinners.Add(spinner);
            }
        }

        /// <summary>Every moving part back as it was placed.</summary>
        public void Reset() {
            if (!turned) {
                return;
            }
            turned = false;
            foreach (Spinner spinner in spinners) {
                spinner.angle = 0;
                if (spinner.brick.gameObject != null) {
                    spinner.brick.gameObject.transform.localPosition = spinner.basePosition;
                    spinner.brick.gameObject.transform.localRotation = spinner.baseRotation;
                }
            }
        }

        /// <summary>After it moved this frame (by physics or the network).</summary>
        public void Update(float power) {
            Transform tr = vehicle.transform;
            float dt = Time.deltaTime;
            Vector3 velocity = dt > 0 ? (tr.position - lastPosition) / dt : Vector3.zero;
            lastPosition = tr.position;
            if (spinners.Count == 0) {
                return;
            }
            // stopped (and no propeller turning): as built
            stillFor = velocity.magnitude < Still && power == 0 ? stillFor + dt : 0;
            if (stillFor >= StillTime) {
                Reset();
                return;
            }
            Vector3 local = tr.InverseTransformDirection(velocity);
            foreach (Spinner spinner in spinners) {
                if (spinner.brick.gameObject == null) {
                    continue;
                }
                float steer = 0;
                if (spinner.isWheel) {
                    spinner.angle += Vector3.Dot(local, spinner.rolls) / spinner.radius * Mathf.Rad2Deg * dt;
                    steer = vehicle.IsSimulated ? vehicle.Drive.SteerOf(spinner.brick) : 0;
                } else {
                    spinner.angle += power * PropellerSpeed * dt;
                }
                spinner.angle = Mathf.Repeat(spinner.angle, 360);
                Quaternion turn = Quaternion.AngleAxis(steer, Vector3.up) * Quaternion.AngleAxis(spinner.angle, spinner.axis);
                Transform brick = spinner.brick.gameObject.transform;
                brick.localRotation = turn * spinner.baseRotation;
                brick.localPosition = spinner.pivot + turn * (spinner.basePosition - spinner.pivot);
                turned = true;
            }
        }
    }
}
