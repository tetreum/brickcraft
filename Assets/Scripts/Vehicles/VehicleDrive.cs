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
    /// Its parts do what the driver's seat's wires say (see VehicleWire; a seat with none of its own has every part's
    /// defaults, see VehicleWires.AllDefaults): wheels drive and steer, and the ones wired brake with the brake key.
    ///
    /// In water its bricks float: each is pushed up by the water it's under (bricks are hollow: they all float) and slowed
    /// by it. Propellers push along their axis (the thin one of their mesh), the way the driver faces, or up if they lie
    /// flat; upright ones steer by turning their push, like an outboard motor.
    ///
    /// Wings lift as they move through the air, with the square of their speed: flat ones up (a little even level, more
    /// the more their front is raised), upright ones sideways (only what hits them). Tilting them (control surfaces) lifts
    /// more or less.
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
        // newtons for each square metre of wing at a metre a second (squared): a flat wing's lift level, what a full tilt
        // adds or takes, what air hitting it head on pushes, and how it slows it
        private const float Camber = 1.6f;
        private const float TiltLift = 1.2f;
        private const float Plate = 2;
        private const float AirDrag = 0.05f;

        private class Wheel
        {
            public WheelCollider collider;
            public Brick brick;
            public float radius;
            // a wheel goes the way its body points (whatever its own transform's turned): one that rolls along the
            // body's X is steered a quarter turn
            public float heading;
            // 1 if it rolls forward the way the driver faces, -1 backwards, 0 across (its own way, then)
            public float motor;
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
            // it stands upright: its push turns to steer (the other way if it's ahead of the centre of mass)
            public float steerSign;
            public readonly List<VehicleWire> wires = new List<VehicleWire>();
            public bool isDriven;
        }

        private class Wing
        {
            public Vector3 centre;
            // the way it lifts: up, or to the driver's right
            public Vector3 normal;
            public float area;
            public readonly List<VehicleWire> wires = new List<VehicleWire>();
        }

        private readonly Vehicle vehicle;
        private readonly List<Wheel> wheels = new List<Wheel>();
        private readonly List<Floater> floaters = new List<Floater>();
        private readonly List<Propeller> propellers = new List<Propeller>();
        private readonly List<Wing> wings = new List<Wing>();
        private Vector3 forward = Vector3.forward;
        private bool isSimulated;
        private int driven, drivenPropellers;

        public VehicleDrive(Vehicle vehicle) {
            this.vehicle = vehicle;
        }

        public int WheelCount {
            get { return wheels.Count; }
        }

        public int PropellerCount {
            get { return propellers.Count; }
        }

        public int WingCount {
            get { return wings.Count; }
        }

        /// <summary>How hard its propellers push, -1 to 1 (on average, the last physics step).</summary>
        public float Power { get; private set; }

        /// <summary>How far a wheel is steered now, in degrees (0 if it isn't one, or it isn't simulated here).</summary>
        public float SteerOf(Brick brick) {
            foreach (Wheel wheel in wheels) {
                if (wheel.brick == brick && wheel.collider != null) {
                    return wheel.collider.steerAngle - wheel.heading;
                }
            }
            return 0;
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
            floaters.Clear();
            propellers.Clear();
            wings.Clear();
            driven = drivenPropellers = 0;
            float cell = Server.studSize * Server.studSize * Server.plateHeight;
            foreach (Brick brick in vehicle.grid.Bricks) {
                Bounds bounds = brick.LocalBounds;
                Vector3Int size = brick.placement.Size;
                floaters.Add(new Floater() {
                    bottom = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z),
                    top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z),
                    // a connected part (an axle) takes no cells: about half its box
                    volume = brick.joint.HasValue ? bounds.size.x * bounds.size.y * bounds.size.z / 2 : size.x * size.y * size.z * cell,
                });

                if (!Server.items.TryGetValue(brick.itemId, out Item item) || brick.gameObject == null) {
                    continue;
                }
                List<VehicleWire> wires = seatWires.FindAll(wire => wire.part == brick.id);
                bool drives = wires.Exists(wire => wire.action == WireAction.Drive);
                switch (item.VehicleRole) {
                    case VehicleRole.Wheel:
                        Wheel wheel = addWheel(brick, item);
                        wheel.wires.AddRange(wires);
                        wheel.isDriven = drives;
                        driven += drives ? 1 : 0;
                        wheels.Add(wheel);
                        break;
                    case VehicleRole.Propeller:
                        Propeller propeller = addPropeller(brick, body.centerOfMass);
                        propeller.wires.AddRange(wires);
                        propeller.isDriven = drives;
                        drivenPropellers += drives ? 1 : 0;
                        propellers.Add(propeller);
                        break;
                    case VehicleRole.Wing:
                        Wing wing = addWing(brick);
                        wing.wires.AddRange(wires);
                        wings.Add(wing);
                        break;
                }
            }
            float load = body.mass * -Physics.gravity.y / Mathf.Max(1, wheels.Count);
            foreach (Wheel wheel in wheels) {
                tune(wheel, load);
            }
            SetSimulated(isSimulated);
        }

        private Wheel addWheel(Brick brick, Item item) {
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
        /// The way a propeller pushes (or a wing lifts), in the vehicle's space: along the thin axis of its mesh, forward for
        /// the driver (or up, lying flat; or to the driver's right, across).
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

        // its area: its two longest sides
        private Wing addWing(Brick brick) {
            Bounds bounds = VehicleShape.MeshBounds(brick, vehicle.transform);
            Vector3 size = bounds.size;
            float smallest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            return new Wing() {
                centre = bounds.center,
                normal = PropellerAxis(bounds, forward),
                area = size.x * size.y * size.z / Mathf.Max(smallest, 0.001f),
            };
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

        /// <summary>Drives it for a physics step, floats it and flies it.</summary>
        public void Apply(VehicleInput input) {
            applyWheels(input);
            applyPropellers(input);
            applyWater();
            applyAir(input);
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

        private void applyWheels(VehicleInput input) {
            Rigidbody body = vehicle.Body;
            float speed = Vector3.Dot(body.velocity, vehicle.transform.TransformDirection(forward));
            float g = -Physics.gravity.y;
            float count = Mathf.Max(1, wheels.Count);
            float brakeKey = Mathf.Clamp01(input.brake);

            foreach (Wheel wheel in wheels) {
                float drive = wired(wheel.wires, WireAction.Drive, input);
                float steer = wired(wheel.wires, WireAction.Steer, input);
                // forward for the driver: the way the wheel rolls (its own way, if it rolls across)
                float direction = wheel.motor != 0 ? wheel.motor : 1;
                // the brake key brakes the wheels wired to the seat; left, they all brake
                float brake = wheel.wires.Count > 0 || input.parked ? brakeKey : 0;
                // the other way while moving brakes first (both for the driver: forward is forward, however the wheel's turned)
                if (drive * speed < -0.5f) {
                    brake = Mathf.Max(brake, Mathf.Abs(drive));
                    drive = 0;
                }
                if (Mathf.Abs(speed) > MaxSpeed && drive * speed > 0) {
                    drive = 0;
                }

                WheelCollider collider = wheel.collider;
                collider.motorTorque = wheel.isDriven ? direction * drive * body.mass * Acceleration * wheel.radius / Mathf.Max(1, driven) : 0;
                collider.brakeTorque = body.mass * g * wheel.radius / count * (brake > 0 ? brake * Braking : drive == 0 ? Rolling : 0);
                collider.steerAngle = wheel.heading + steer * MaxSteer;
            }
        }

        private void applyPropellers(VehicleInput input) {
            Rigidbody body = vehicle.Body;
            Transform tr = vehicle.transform;
            float power = 0;
            foreach (Propeller propeller in propellers) {
                float drive = wired(propeller.wires, WireAction.Drive, input);
                float steer = wired(propeller.wires, WireAction.Steer, input);
                if (!propeller.isDriven || drive == 0) {
                    continue;
                }
                power += drive / drivenPropellers;
                Vector3 push = tr.TransformDirection(Quaternion.AngleAxis(steer * MaxSteer * propeller.steerSign, Vector3.up) * propeller.axis);
                if (Vector3.Dot(body.velocity, push) * Mathf.Sign(drive) > MaxSpeed) {
                    continue;
                }
                body.AddForceAtPosition(push * drive * body.mass * PropellerAcceleration / drivenPropellers, tr.TransformPoint(propeller.centre));
            }
            Power = power;
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

        // wings lift with the air going past them, and the air hitting them pushes them back
        private void applyAir(VehicleInput input) {
            Rigidbody body = vehicle.Body;
            Transform tr = vehicle.transform;
            foreach (Wing wing in wings) {
                Vector3 at = tr.TransformPoint(wing.centre);
                if (World.Water.IsAt(at)) {
                    continue;
                }
                Vector3 velocity = body.GetPointVelocity(at);
                Vector3 normal = tr.TransformDirection(wing.normal);
                float across = Vector3.Dot(velocity, normal);
                float past = (velocity - normal * across).sqrMagnitude;
                float lift = (wing.normal == Vector3.up ? Camber : 0) + TiltLift * wired(wing.wires, WireAction.Tilt, input);
                Vector3 force = normal * wing.area * (lift * past - Plate * across * velocity.magnitude) - velocity * velocity.magnitude * wing.area * AirDrag;
                body.AddForceAtPosition(force, at);
            }
        }
    }
}
