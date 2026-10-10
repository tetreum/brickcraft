# Events

Events are global functions the game calls in your scripts when something happens. Define the ones you need; the rest are skipped. An event that returns `false` cancels what was happening, where noted.

Bricks placed, moved or removed by scripts (see [World](world.md) and [Bricks](bricks.md)) don't trigger events.

## Item events

In `items/[item]/script.lua`. They're called for the item's loose bricks and for its world blocks (2x2 bricks placed exactly over a block's place become part of the terrain).

### onPlaced(brick, player)

A player placed the item. The brick is already in the world and the item already left the player's inventory.

```lua
function onPlaced(brick, player)
  player:message("Placed at " .. tostring(brick.position))
end
```

### onHit(brick, player)

A player is breaking the brick: a loose brick is being removed, or a world block has been dug. **Return `false` and it stays**: it isn't removed and the player gets nothing.

```lua
function onHit(brick, player)
  if player.id ~= brick.placedBy then
    player:message("Only whoever placed it can break it")
    return false
  end
end
```

### onBroken(brick, player)

The brick was removed or dug out, and the player got its item. `brick.exists` is already `false`.

### onInteract(brick, player)

A player used the brick (the "Use brick" key, E by default) from within reach.

```lua
function onInteract(brick, player)
  player:give("dirt", 1)
end
```

## Mod events

In any file of `scripts/`. Each file can define them; all of them are called.

### onLoad()

The server started with the mod. It runs before the world is loaded, so `world` functions that need a loaded world fail or return nothing; start timers here, or wait for players to join.

### onPlayerJoined(player)

A player joined and appeared in the world.

```lua
function onPlayerJoined(player)
  player:message("Welcome, " .. player.name)
end
```

### onPlayerLeft(player)

A player left (or was kicked). Its `position` may already be `nil`.

### onChat(player, text)

A player sent a chat message (commands starting with `/` aren't passed). **Return `false` and it isn't sent** to the others.

```lua
function onChat(player, text)
  if text == "!where" then
    player:message("You're at " .. tostring(player.position))
    return false
  end
end
```
