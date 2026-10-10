# Bricks

Events and [world](world.md) functions give scripts bricks: loose bricks, and world blocks (2x2 bricks that are part of the terrain, `isBlock` true). Keep them as long as you like: once a brick is gone, `exists` is `false` and its methods do nothing and return `false`.

A brick's fields are read when you get it. If something else changes the brick later (a player, another script), get it again (`world.brickAt`) to see the change.

## Fields

| Field | |
|---|---|
| `item` | Its item's id, like `"my_mod:catapult"`. |
| `position` | Its first cell, see [Positions](README.md#positions). Methods of this brick keep it up to date. |
| `color` | Its colour id. `-1` for world blocks drawn with their textures. |
| `rotation` | Quarter turns, 0 to 3 (0 for world blocks). |
| `isBlock` | It's part of the terrain, not a loose brick. |
| `placedBy` | The id of the player that placed it (see [Players](players.md)), `0` if no player did (generated, or placed by a script). |
| `placedAt` | When it was placed, in unix seconds; `0` if no player placed it. |
| `exists` | It's still in the world. |

## brick:move(position)

Moves it so its first cell is at `position`. Loose bricks can go wherever their cells are free; world blocks go to another block's place that's empty (air or water) with no loose bricks in it. It keeps its colour, rotation and who placed it, and what's attached to it (a door in a frame) moves along. Returns `false`, and the brick stays, if there's no room, that part of the world isn't loaded, the brick isn't there anymore, or it's an attachment (they move with their brick).

```lua
-- one plate up every 0.05 seconds, until something is in the way
local id
id = timer.every(0.05, function()
  local p = plate.position
  if not plate:move({x = p.x, y = p.y + 1, z = p.z}) then
    timer.cancel(id)
  end
end)
```

## brick:remove()

Takes it out of the world, with what's attached to it; nobody gets their items, and `onBroken` isn't called. Returns `false` if it wasn't there anymore.
