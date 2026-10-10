# Players

Events and `world.players()` give scripts players. Use `:` to call their methods (`player:message("hi")`).

## Fields

| Field | |
|---|---|
| `id` | Its id on the server, the same every time it plays there. Bricks remember it as `placedBy`. |
| `name` | Its name. |
| `position` | The cell its feet are in, see [Positions](README.md#positions). `nil` once it left. |

## player:message(text)

Shows `text` in the player's chat, from the server.

```lua
player:message("Welcome back, " .. player.name)
```

## player:give(item, count, color)

Puts items in the player's inventory: `count` (1 by default) of `item`, in `color` if given (a colour the item can have) or the item's default one. Returns `false`, and gives nothing, if they don't all fit. It's an error if the item doesn't exist, the count is below 1 or the item can't have that colour.

```lua
if not player:give("brick_2x2", 10, 4) then
  player:message("Your inventory is full")
end
```
