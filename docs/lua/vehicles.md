# Vehicles

`world.vehicles()`, `player.vehicle` and the vehicle events (see [Events](events.md#onvehicleenterplayer-vehicle)) give scripts vehicles (see [Vehicles](../vehicles.md)). Use `:` to call their methods (`vehicle:reposition()`).

## Fields

| Field | |
|---|---|
| `id` | Its id, the same every time the world is played. |
| `exists` | Whether it's still in the world: taken apart, or in a part of the world that was unloaded, it isn't. |
| `position` | The cell it's in (its first part's), see [Positions](README.md#positions). `nil` once it's gone. |
| `speed` | How fast it goes, in metres a second. |
| `parts` | How many bricks it's made of. |
| `driver` | The [player](players.md) driving it, `nil` if nobody is. |

## vehicle:reposition()

Puts it back on its wheels: upright, still facing the same way, just above the ground under it. Like `/reposition`.

```lua
-- every minute, the vehicles nobody drives are put back on their wheels
timer.every(60, function()
  for _, vehicle in ipairs(world.vehicles()) do
    if vehicle.driver == nil and vehicle.speed < 0.1 then
      vehicle:reposition()
    end
  end
end)
```
