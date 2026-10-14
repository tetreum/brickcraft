# Timer

`timer.*` runs functions later. Timers run on the server between frames, each call with the same limits as events (see [How they run](README.md#how-they-run)); an error in one is logged and a repeating timer keeps going. They stop when the server stops and aren't saved. A mod can have up to 256 timers at once.

## timer.after(seconds, fn)

Calls `fn` once, `seconds` from now. Returns the timer's id.

```lua
timer.after(2, function()
  player:message("Two seconds later")
end)
```

## timer.every(seconds, fn)

Calls `fn` every `seconds` (0.05 at least) until it's cancelled. Returns the timer's id.

```lua
local ticks = 0
local id
id = timer.every(1, function()
  ticks = ticks + 1
  if ticks == 10 then
    timer.cancel(id)
  end
end)
```

## timer.cancel(id)

Stops one of the mod's timers. Returns `false` if there's no such timer (it already ran, or was cancelled).
