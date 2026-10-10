# World

`world.*` reads and changes the world. Changes need that part of the world to be loaded (around players), and reach the players and the save like the changes players make. Positions are grid cells, see [Positions](README.md#positions).

## world.spawn(item, position, options)

Places a loose brick of `item` (an item id, like `"plate_1x1_green"` or `"my_mod:catapult"`) with its first cell at `position`. Returns the [brick](bricks.md), or `nil` if it doesn't fit there or that part of the world isn't loaded. Spawned bricks are placed by nobody (`placedBy` 0) and don't trigger `onPlaced`.

`options` is optional:
- `rotation`: quarter turns, 0 to 3 (0 by default).
- `color`: a colour id the item can have (its default colour otherwise).

It's an error if the item doesn't exist, isn't a brick, can't have that colour, or is an attachment (like a door, which only goes in a door frame).

```lua
local plate = world.spawn("plate_1x1_green", {x = 0, y = 250, z = 0}, {color = 4, rotation = 1})
if plate then
  log("spawned", plate)
end
```

## world.brickAt(position)

The loose [brick](bricks.md) that takes up that cell, or else the world block there (as a brick with `isBlock` true). `nil` if the cell is empty or not loaded.

## world.getBlock(position)

The item id of the world block that contains that cell, `nil` for air or if it isn't loaded.

```lua
if world.getBlock({x = 0, y = 249, z = 0}) == "stone" then ... end
```

## world.setBlock(position, item, color)

Changes the world block that contains that cell: to `item`'s block, or to air if `item` is `nil`. `color` is optional, a colour the item can have; without it (or with the item's default colour) the block is drawn with its textures. Returns `true` if it changed it, `false` if that part of the world isn't loaded or a loose brick is in the way. It's an error if the item has no world block or can't have that colour.

Blocks set by scripts are placed by nobody.

```lua
world.setBlock({x = 0, y = 249, z = 0}, "stone")
world.setBlock({x = 0, y = 249, z = 0}, "brick_2x2", 4) -- a red block
world.setBlock({x = 0, y = 249, z = 0}, nil)            -- air
```

## world.bricksIn(from, to)

A list of the loose [bricks](bricks.md) whose first cell is inside the box between the two corners (both included). World blocks aren't listed.

```lua
for _, brick in ipairs(world.bricksIn({x = -10, y = 240, z = -10}, {x = 10, y = 260, z = 10})) do
  log(brick)
end
```

## world.players()

A list of the [players](players.md) in the game.

```lua
for _, player in ipairs(world.players()) do
  player:message("Hello everyone")
end
```

## world.isLoaded(position)

Whether that part of the world is loaded, so it can be changed.
