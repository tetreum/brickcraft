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
