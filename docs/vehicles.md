## Vehicles

Cars, boats and planes are built from bricks, like anything else, but they aren't part of the world: they move.

### Building one

Place a wheel (any part of LDraw's *Wheel* category) on the ground: it starts a vehicle. Bricks placed on a vehicle's bricks are its own, placed with studs and plates like the world's but on its own grid, so it can drive and turn. Removing a brick that held two parts together splits it into two vehicles where they are; placing a brick that touches both (lined up) joins them again.

What its parts do depends on their kind (their LDraw category, or `vehicle.role` in an item's `info.json`):

| Part | Role | |
|---|---|---|
| Seat (4079) | `seat` | Where players sit. The first one in drives. |
| Wheels | `wheel` | Drive and steer on the ground. |
| Propellers | `propeller` | Push along their axis: upright ones forward (and steer, like an outboard motor), flat ones up. |
| Wings | `wing` | Lift with speed. Wired, they're flaps: elevators, ailerons, rudders. |

Every brick weighs by its size (a 2x2 brick, 24 kg; `vehicle.mass` in `info.json` overrides it), and every brick floats: a vehicle in water is pushed up by the water its bricks are under.

A vehicle that nobody drives stays where it is, solid to build on. Vehicles are saved with the world.

### Technic: axles, pins and holes

Besides their studs, LDraw parts connect the way LEGO Technic does: an axle goes through a technic brick's hole or a wheel's axle hole, a pin into a beam. Hold a part that has such connections (an axle, a wheel, a pin, a beam...) and look at a matching one on a brick, in the world or on a vehicle: the preview snaps there, as far along it as you look (half a stud at a time).

| Key | |
|---|---|
| R | Turn it around the connection, a quarter turn |
| T | Put it the other way round |
| G | Use another of its connections that fits (which of a beam's holes goes on the axle) |
| Z / X | Slide it back / forward along the connection, half a stud at a time (a long axle through a short hole) |

What fits: an axle in an axle hole (it can't turn there) or in a round hole at least as wide (it spins); a pin, or a bar, in a round hole its size. Parts slide along axles and holes that let them, the shorter staying within the longer, and they can't go into anything they don't connect to. A connected part goes with what holds it: take the technic brick away and its axle and wheels go too.

Wheels with an axle hole (like *Wheel Rim 14x18 with Axlehole*) go on axles; others have a hub of their own size (*Wheel Rim 10x14*'s takes a bar, a wheel holder's pin). A car the technic way: a *Technic Brick 1x8 with Holes*, an axle through each end hole, a wheel on each axle end, a brick and a seat on top. Its axles turn with its wheels.

Where parts connect comes from the [LDCad Shadow Library](https://github.com/RolandMelkert/LDCadShadowLibrary) (most technic parts and wheels; the game guesses the rest).

### Driving

Click a seat to sit on it. The player's own keys drive (remap them in Settings > Controls):

| Key | |
|---|---|
| W / S | Throttle: forward and back |
| A / D | Steering (and rolling, in a plane) |
| Space | Brake |
| R / F | Up and down: a plane's nose |
| E | Get up |

Left moving, a vehicle brakes all its wheels; in the air it falls. If it gets stuck or overturned, `/reposition` (from its seat) puts it back on its wheels, above the ground under it.

### Wiring

A seat with no wires drives every part as it'd be wired by default: wheels and propellers drive with the throttle, the front wheels and upright propellers steer, wings behind the middle pitch (R/F), wings to the sides roll and upright ones turn (A/D).

To choose what each part does, use the wiring tool (a screwdriver):

- Left click a seat: it's picked; the parts wired to it show as lines.
- Left click a wheel, propeller or wing of the same vehicle: it's wired to the seat with its defaults; again, it's unwired. Once a seat has wires, only the wired parts do anything for its driver.
- Right click a seat: its controls. A row for each wire: the part, what it does (drive, steer or tilt), with what (the forward/back, left/right or up/down keys, or a key of its own: click it, then press the key) and whether it's inverted. `+` adds another wire to the same part, `x` removes one.

"W turns the back wheels forward" is a wire from each back wheel with *Drive* and the key *W*.

### Multiplayer

The driver's game moves the vehicle (the others see it where the driver has it); left, the server lets it come to rest. Anyone can drive and change any vehicle.
