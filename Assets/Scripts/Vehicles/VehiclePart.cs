namespace Brickcraft.Vehicles
{
    /// <summary>
    /// What a part does on a vehicle (see Item.VehicleRole). Bricks without one are its body: they weigh and float, but
    /// do nothing else.
    /// </summary>
    public enum VehicleRole
    {
        None,
        /// <summary>It rolls: the first one placed on the ground starts a vehicle; wired to a seat, it drives or steers.</summary>
        Wheel,
        /// <summary>Where the driver sits; the parts wired to it are what it controls.</summary>
        Seat,
        /// <summary>It pushes the vehicle along its axis (boats, planes).</summary>
        Propeller,
        /// <summary>It lifts the vehicle while it moves through the air (planes).</summary>
        Wing,
        /// <summary>It turns the vehicle (a boat's rudder, a plane's flaps).</summary>
        Rudder,
    }

    public static class VehicleParts
    {
        /// <summary>
        /// A part's mass when its item doesn't say: by the cells its model takes (studs x studs x plates), so a 2x4 brick
        /// weighs 24 times a 1x1 plate.
        /// </summary>
        public const float MassPerCell = 0.05f;

        /// <summary>The role named in an item's info.json ("wheel", "seat"...), None if it isn't one.</summary>
        public static VehicleRole Parse(string role) {
            if (string.IsNullOrEmpty(role)) {
                return VehicleRole.None;
            }
            switch (role.Trim().ToLowerInvariant()) {
                case "wheel": return VehicleRole.Wheel;
                case "seat": return VehicleRole.Seat;
                case "propeller": return VehicleRole.Propeller;
                case "wing": return VehicleRole.Wing;
                case "rudder": return VehicleRole.Rudder;
                default: return VehicleRole.None;
            }
        }

        /// <summary>The role of a part by its tags: LDraw's categories (wheel, propeller, wing) and the seat tag.</summary>
        public static VehicleRole ByTags(System.Collections.Generic.ICollection<string> tags) {
            if (tags.Contains("seat")) {
                return VehicleRole.Seat;
            }
            if (tags.Contains("wheel")) {
                return VehicleRole.Wheel;
            }
            if (tags.Contains("propeller")) {
                return VehicleRole.Propeller;
            }
            if (tags.Contains("wing")) {
                return VehicleRole.Wing;
            }
            return VehicleRole.None;
        }
    }
}
