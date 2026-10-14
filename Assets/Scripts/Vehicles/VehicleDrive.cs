using System.Collections.Generic;
using Brickcraft.Bricks;
using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>
    /// How a vehicle moves when it's simulated (see Vehicle.IsSimulated): its mass and centre of mass by its parts'
    /// (see Item.Mass), and a WheelCollider for each wheel, its size and axle by the wheel's mesh. Its suspension carries
    /// its weight halfway down, whatever it weighs.
    ///
    /// The wheels wired to the driver's seat do what their wires say (see VehicleWire), and brake with the brake key. With
    /// no wires from that seat, every wheel that rolls the way the driver faces is driven and the ones ahead of the centre
    /// of mass steer, so a simple car drives as it's built.
    /// </summary>
    public class VehicleDrive
    {
        /// <summary>Full throttle, metres per second each second.</summary>
        public const float Acceleration = 6;
        /// <summary>Metres per second the motor stops pushing at.</summary>
        public const float MaxSpeed = 25;
        public const float MaxSteer = 30;
        // how far a wheel goes up and down, of its radius
        private const float Suspension = 0.3f;
        // it stops at a g's worth
        private const float Braking = 1.2f;
        // a little braking with no throttle, so it doesn't roll forever
        private const float Rolling = 0.05f;

        private class Wheel
        {
            public WheelCollider collider;
            public Brick brick;
            public float radius;
            // a wheel goes the way its body points (whatever its own transform's turned): one that rolls along the
            // body's X is steered a quarter turn
            public float heading;
            // 1 if it rolls forward the way the driver faces, -1 backwards, 0 across
            public float motor;
            // with no wires: it's ahead of the centre of mass, it steers
            public bool steers;
            // the driver's seat's wires to it
            public readonly List<VehicleWire> wires = new List<VehicleWire>();
            public bool isDriven;
        }

        private readonly Vehicle vehicle;
        private readonly List<Wheel> wheels = new List<Wheel>();
        private Vector3 forward = Vector3.forward;
        private bool isSimulated;
        private bool isWired;
        private int driven;

        public VehicleDrive(Vehicle vehicle) {
            this.vehicle = vehicle;
        }

        public int WheelCount {
            get { return wheels.Count; }
        }

        /// <summary>
        /// Its parts changed, or who drives it: the way they face (in the vehicle's space) and their seat's wires (none if
        /// nobody drives it).
        /// </summary>
        public void Rebuild(Vector3 forward, List<VehicleWire> seatWires) {
            this.forward = forward;
            Rigidbody body = vehicle.Body;
            Transform tr = vehicle.transform;

            float mass = 0;
            foreach (Brick brick in vehicle.grid.Bricks) {
                mass += Server.items.TryGetValue(brick.itemId, out Item item) ? item.Mass : VehicleParts.MassPerCell;
            }
            body.mass = Mathf.Max(mass, 0.1f);
            body.centerOfMass = vehicle.grid.CentreOfMass;

            // the physics engine takes convex shapes only on bodies that move
            foreach (MeshCollider mesh in tr.GetComponentsInChildren<MeshCollider>()) {
                mesh.convex = true;
            }

            foreach (Wheel wheel in wheels) {
                if (wheel.collider != null) {
                    Object.Destroy(wheel.collider.gameObject);
                }
            }
            wheels.Clear();
            isWired = seatWires.Count > 0;
            driven = 0;
            foreach (Brick brick in vehicle.grid.Bricks) {
                if (!Server.items.TryGetValue(brick.itemId, out Item item) || item.VehicleRole != VehicleRole.Wheel || brick.gameObject == null) {
                    continue;
                }
                Wheel wheel = addWheel(brick, item, body.centerOfMass);
                foreach (VehicleWire wire in seatWires) {
                    if (wire.part == brick.id) {
                        wheel.wires.Add(wire);
                        wheel.isDriven |= wire.action == WireAction.Drive;
                    }
                }
                if (!isWired) {
                    wheel.isDriven = wheel.motor != 0;
                }
                if (wheel.isDriven) {
                    driven++;
                }
                wheels.Add(wheel);
            }
            float load = body.mass * -Physics.gravity.y / Mathf.Max(1, wheels.Count);
            foreach (Wheel wheel in wheels) {
                tune(wheel, load);
            }
            SetSimulated(isSimulated);
        }

        private Wheel addWheel(Brick brick, Item item, Vector3 centreOfMass) {
            Bounds bounds = VehicleShape.MeshBounds(brick, vehicle.transform);
            bool axleAlongX = bounds.size.x <= bounds.size.z;
            float radius = Mathf.Max(0.05f, bounds.extents.y);
            float travel = radius * Suspension;

            GameObject go = new GameObject("Wheel " + brick.id);
            go.transform.SetParent(vehicle.transform, false);
            // at rest (halfway down) where the wheel is
            go.transform.localPosition = bounds.center + Vector3.up * travel * 0.5f;
            float heading = axleAlongX ? 0 : 90;
            WheelCollider collider = go.AddComponent<WheelCollider>();
            collider.radius = radius;
            collider.suspensionDistance = travel;
            collider.mass = Mathf.Max(0.5f, item.Mass);

            float along = Vector3.Dot(Quaternion.Euler(0, heading, 0) * Vector3.forward, forward);
            return new Wheel() {
                collider = collider,
                brick = brick,
                radius = radius,
                heading = heading,
                motor = Mathf.Abs(along) > 0.5f ? Mathf.Sign(along) : 0,
                steers = Vector3.Dot(bounds.center - centreOfMass, forward) > Server.studSize * 0.5f,
            };
        }

        // the spring holds the load halfway down its travel
        private static void tune(Wheel wheel, float load) {
            WheelCollider collider = wheel.collider;
            float spring = load / (collider.suspensionDistance * 0.5f);
            float sprung = load / -Physics.gravity.y;
            collider.suspensionSpring = new JointSpring() {
                spring = spring,
                damper = 2 * Mathf.Sqrt(spring * sprung) * 0.6f,
                targetPosition = 0.5f,
            };
            collider.forceAppPointDistance = collider.radius * 0.5f; // less rolling over in turns
        }

        /// <summary>
        /// It's simulated here or not: wheels roll on their WheelColliders then, their bricks' own colliders don't touch
        /// the ground (they're still solid to build on, parked).
        /// </summary>
        public void SetSimulated(bool simulated) {
            isSimulated = simulated;
            foreach (Wheel wheel in wheels) {
                if (wheel.brick.gameObject == null) {
                    continue;
                }
                foreach (Collider collider in wheel.brick.gameObject.GetComponentsInChildren<Collider>()) {
                    collider.isTrigger = simulated;
                }
            }
        }

        /// <summary>Drives it for a physics step.</summary>
        public void Apply(VehicleInput input) {
            Rigidbody body = vehicle.Body;
            float speed = Vector3.Dot(body.velocity, vehicle.transform.TransformDirection(forward));
            float g = -Physics.gravity.y;
            float count = Mathf.Max(1, wheels.Count);
            float brakeKey = Mathf.Clamp01(input.brake);

            foreach (Wheel wheel in wheels) {
                float drive = 0, steer = 0;
                bool brakes = true;
                if (isWired) {
                    foreach (VehicleWire wire in wheel.wires) {
                        if (wire.action == WireAction.Drive) {
                            drive += VehicleWires.Value(wire, input);
                        } else {
                            steer += VehicleWires.Value(wire, input);
                        }
                    }
                    brakes = wheel.wires.Count > 0;
                } else {
                    drive = wheel.isDriven ? input.throttle : 0;
                    steer = wheel.steers ? input.steer : 0;
                }
                drive = Mathf.Clamp(drive, -1, 1);
                steer = Mathf.Clamp(steer, -1, 1);

                // forward for the driver: the way the wheel rolls (its own way, if it rolls across)
                float direction = wheel.motor != 0 ? wheel.motor : 1;
                float brake = brakes ? brakeKey : 0;
                // the other way while moving brakes first
                float rolling = speed * direction;
                if (drive * rolling < -0.5f) {
                    brake = Mathf.Max(brake, Mathf.Abs(drive));
                    drive = 0;
                }
                if (Mathf.Abs(speed) > MaxSpeed && drive * rolling > 0) {
                    drive = 0;
                }

                WheelCollider collider = wheel.collider;
                collider.motorTorque = wheel.isDriven ? direction * drive * body.mass * Acceleration * wheel.radius / Mathf.Max(1, driven) : 0;
                collider.brakeTorque = body.mass * g * wheel.radius / count * (brake > 0 ? brake * Braking : drive == 0 ? Rolling : 0);
                collider.steerAngle = wheel.heading + steer * MaxSteer;
            }
        }
    }
}
