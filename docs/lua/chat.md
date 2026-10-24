# Chat

`chat.*` writes in everybody's chat. To write to one player only, use [`player:message`](players.md#playermessagetext); to read what players write, the [`onChat`](events.md) event.

Text is one line: line breaks and other control characters become spaces, and it's cut at 200 characters. Empty text isn't sent.

## chat.say(text)

Shows `text` in every player's chat, from the server, like an admin announcement.

```lua
chat.say("The arena opens in 5 minutes")
```

## chat.event(text)

Shows `text` in every player's chat as something that happened, the way "X joined the game" is shown: no sender, just the line.

```lua
function onBroken(brick, player)
  chat.event(player.name .. " broke the dragon's egg")
end
```

## chat.command(name, usage, fn)

Adds a command of the mod's own: when a player types `/name ...`, `fn(player, args)` runs on the server (`player` may be `nil`, `args` is the text after the command). `usage` is how it's listed in the game's unknown-command reply; without it, `/name` is shown. A name the game already has (`/kick`, `/closest`...) can't be taken, and neither can another mod's.

**Everyone can run it** (like `/reposition`): check the player yourself (its `id` is in `players.db`) if it should be admins only.

```lua
chat.command("where", "/where", function(player, args)
  if player == nil then return end
  player:message("You're at " .. tostring(player.position))
end)
```

Commands are added when the mod's scripts load and forgotten when the server stops. `/` messages aren't passed to [`onChat`](events.md); a command no one knows shows the game's reply, which lists the mods' commands too.

```lua
-- what follows "!heal" goes to the command as args
chat.command("heal", "/heal AMOUNT", function(player, args)
  local amount = tonumber(args)
  if player ~= nil and amount ~= nil then
    player:heal(amount)
    chat.event(player.name .. " was healed")
  end
end)
```
