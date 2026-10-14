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
    ///
    /// In water its bricks float: each is pushed up by the water it's under (bricks are hollow: they all float) and slowed
    /// by it. Propellers push along their axis (the thin one of their mesh), the way the driver faces, or up if they lie
    /// flat; the ones behind the centre of mass steer by turning their push, like an outboard motor.
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
        /// <summary>Full throttle on a vehicle's propellers, metres per second each second.</summary>
        public const float PropellerAcceleration = 4;
        // kilograms a cubic metre, and how much a brick in it is slowed (of the water it moves, each second)
        private const float WaterDensity = 1000;
        private const float WaterDrag = 0.8f;
        // a brick's part under water, found to a sixteenth
        private const int SurfaceSteps = 4;

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

        // a brick that floats: where it is in the vehicle, how much water it moves under
        private struct Floater
        {
            public Vector3 bottom;
            public Vector3 top;
            public float volume;
        }

        private class Propeller
        {
            public Vector3 centre;
            // the way it pushes, forward
            public Vector3 axis;
            // it lies upright: its push turns to steer (the other way if it's ahead of the centre of mass)
            public float steerSign;
            public readonly List<VehicleWire> wires = new List<VehicleWire>();
            public bool isDriven;
        }

        private readonly Vehicle vehicle;
        private readonly List<Wheel> wheels = new List<Wheel>();
        private readonly List<Floater> floaters = new List<Floater>();
        private readonly List<Propeller> propellers = new List<Propeller>();
        private int drivenPropellers;
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

        public int PropellerCount {
            get { return propellers.Count; }
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

            floaters.Clear();
            float cell = Server.studSize * Server.studSize * Server.plateHeight;
            foreach (Brick brick in vehicle.grid.Bricks) {
                Bounds bounds = brick.placement.LocalBounds;
                Vector3Int size = brick.placement.Size;
                floaters.Add(new Floater() {
                    bottom = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z),
                    top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z),
                    volume = size.x * size.y * size.z * cell,
                });
            }

            propellers.Clear();
            drivenPropellers = 0;
            foreach (Brick brick in vehicle.grid.Bricks) {
                if (!Server.items.TryGetValue(brick.itemId, out Item item) || item.VehicleRole != VehicleRole.Propeller || brick.gameObject == null) {
                    continue;
                }
                Propeller propeller = addPropeller(brick, body.centerOfMass);
                foreach (VehicleWire wire in seatWires) {
                    if (wire.part == brick.id) {
                        propeller.wires.Add(wire);
                        propeller.isDriven |= wire.action == WireAction.Drive;
                    }
                }
                if (!isWired) {
                    propeller.isDriven = Vector3.Dot(propeller.axis, forward) > 0.5f;
                }
                if (propeller.isDriven) {
                    drivenPropellers++;
                }
                propellers.Add(propeller);
            }
            SetSimulated(isSimulated);
        }

        /// <summary>
        /// The way a propeller pushes, in the vehicle's space: along the thin axis of its mesh, forward for the driver (or
        /// up, lying flat; or to the driver's right, across).
        /// </summary>
        public static Vector3 PropellerAxis(Bounds mesh, Vector3 forward) {
            Vector3 size = mesh.size;
            Vector3 axis = size.y <= size.x && size.y <= size.z ? Vector3.up : size.x <= size.z ? Vector3.right : Vector3.forward;
            if (axis == Vector3.up) {
                return axis;
            }
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float along = Vector3.Dot(axis, forward);
            if (Mathf.Abs(along) > 0.5f) {
                return axis * Mathf.Sign(along);
            }
            return Vector3.Dot(axis, right) >= 0 ? axis : -axis;
        }

        private Propeller addPropeller(Brick brick, Vector3 centreOfMass) {
            Bounds bounds = VehicleShape.MeshBounds(brick, vehicle.transform);
            Vector3 axis = PropellerAxis(bounds, forward);
            float ahead = Vector3.Dot(bounds.center - centreOfMass, forward);
            return new Propeller() {
                centre = bounds.center,
                axis = axis,
                steerSign = axis == Vector3.up ? 0 : ahead > 0 ? 1 : -1,
            };
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

        /// <summary>Drives it for a physics step, and floats it.</summary>
        public void Apply(VehicleInput input) {
            applyWheels(input);
            applyPropellers(input);
            applyWater();
        }

        // what the part's wires add up to, for an action
        private static float wired(List<VehicleWire> wires, WireAction action, VehicleInput input) {
            float value = 0;
            foreach (VehicleWire wire in wires) {
                if (wire.action == action) {
                    value += VehicleWires.Value(wire, input);
                }
            }
            return Mathf.Clamp(value, -1, 1);
        }

        private void applyPropellers(VehicleInput input) {
            Rigidbody body = vehicle.Body;
            Transform tr = vehicle.transform;
            foreach (Propeller propeller in propellers) {
                float drive, steer;
                if (isWired) {
                    drive = wired(propeller.wires, WireAction.Drive, input);
                    steer = wired(propeller.wires, WireAction.Steer, input);
                } else {
                    drive = propeller.isDriven ? Mathf.Clamp(input.throttle, -1, 1) : 0;
                    steer = Mathf.Clamp(input.steer, -1, 1);
                }
                if (!propeller.isDriven || drive == 0) {
                    continue;
                }
                Vector3 push = tr.TransformDirection(Quaternion.AngleAxis(steer * MaxSteer * propeller.steerSign, Vector3.up) * propeller.axis);
                if (Vector3.Dot(body.velocity, push) * Mathf.Sign(drive) > MaxSpeed) {
                    continue;
                }
                body.AddForceAtPosition(push * drive * body.mass * PropellerAcceleration / drivenPropellers, tr.TransformPoint(propeller.centre));
            }
        }

        // each brick under water is pushed up by the water it moves, and slowed by it
        private void applyWater() {
            Rigidbody body = vehicle.Body;
            Transform tr = vehicle.transform;
            float g = -Physics.gravity.y;
            foreach (Floater floater in floaters) {
                Vector3 bottom = tr.TransformPoint(floater.bottom);
                if (!World.Water.IsAt(bottom)) {
                    continue;
                }
                Vector3 top = tr.TransformPoint(floater.top);
                float under = 1;
                if (!World.Water.IsAt(top)) {
                    // where the surface is, between its bottom and its top
                    float low = 0, high = 1;
                    for (int step = 0; step < SurfaceSteps; step++) {
                        float middle = (low + high) / 2;
                        if (World.Water.IsAt(Vector3.Lerp(bottom, top, middle))) {
                            low = middle;
                        } else {
                            high = middle;
                        }
                    }
                    under = (low + high) / 2;
                }
                Vector3 centre = Vector3.Lerp(bottom, top, under / 2);
                float water = under * floater.volume * WaterDensity;
                body.AddForceAtPosition(Vector3.up * water * g - body.GetPointVelocity(centre) * water * WaterDrag, centre);
            }
        }

        private void applyWheels(VehicleInput input) {
            Rigidbody body = vehicle.Body;
            float speed = Vector3.Dot(body.velocity, vehicle.transform.TransformDirection(forward));
            float g = -Physics.gravity.y;
            float count = Mathf.Max(1, wheels.Count);
            float brakeKey = Mathf.Clamp01(input.brake);

            foreach (Wheel wheel in wheels) {
                float drive = 0, steer = 0;
                bool brakes = true;
                if (isWired) {
                    drive = wired(wheel.wires, WireAction.Drive, input);
                    steer = wired(wheel.wires, WireAction.Steer, input);
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
