# Lua scripting

Mods can script their bricks in Lua ([MoonSharp](https://www.moonsharp.org/), Lua 5.2). This is the reference of what scripts can do, a page per section. For what a mod is and how worlds and servers use them, see the Mods section of the [readme](../../readme.md#mods); `Mods/example` is a small mod to start from.

| Section | What it covers |
|---|---|
| [Events](events.md) | The functions the game calls in your scripts: a brick was placed, hit, broken or used; a player joined, left or chatted; the mod loaded. |
| [Globals](globals.md) | `log`, `print`, the `shared` table and the parts of Lua's standard library scripts get. |
| [World](world.md) | `world.*`: spawning bricks, reading and changing world blocks, finding bricks and players. |
| [Bricks](bricks.md) | What a brick is to a script: its fields, `move` and `remove`. |
| [Players](players.md) | What a player is to a script: its fields, `message` and `give`. |
| [Timer](timer.md) | `timer.*`: running functions later or every so often. |

## Where scripts go

```
Mods/my_mod/
  items/catapult/script.lua   the catapult's events (onPlaced, onHit, onBroken, onInteract)
  scripts/*.lua               the mod's events (onLoad, onPlayerJoined, onPlayerLeft, onChat), any number of files
```

```lua
-- Mods/my_mod/items/vault/script.lua: a brick that takes three hits to break
local hits = 0

function onHit(brick, player)
  hits = hits + 1
  if hits < 3 then
    player:message("It's sturdy (" .. hits .. "/3)")
    return false
  end
end
```

## How they run

- **On the server only.** What scripts do goes through the server and reaches the players (and the save) like any change a player makes, so scripts can't be used to cheat, and players never run code they downloaded.
- **One Lua state per mod.** Mods can't see each other's scripts.
- **Each file has its own globals**, so two items can both define `onPlaced`. Globals of the mod (the API, `shared`) are read through them; `shared` is the place for state the mod's files share.
- **Sandboxed.** No files, no loading code, no `os` but the time; see [Globals](globals.md).
- **Limited.** A call (an event, a timer) that runs more than a million Lua instructions is stopped. An event handler that fails 5 times is turned off until the server restarts. Errors are logged with the file and line (`[my_mod] items/vault/script.lua:(5,2-20): ...`) and never stop the game.

## Positions

Positions are cells of the brick grid: `x` and `z` count studs, `y` counts plates. A 2x2 world block is 2 x 3 x 2 cells, and its position is its lowest corner cell. Functions take a table `{x = 0, y = 0, z = 0}` or a position a brick or player returned (`brick.position`), and return positions with `x`, `y` and `z` fields.

```lua
local p = brick.position
local above = {x = p.x, y = p.y + 3, z = p.z} -- one brick (3 plates) higher
```

## Adding a section

Each section of the API (UI, AI...) gets its own page here, listed in the table above. Its functions live in `Assets/Scripts/Scripting` (`LuaApi` installs them in every mod's Lua state).
