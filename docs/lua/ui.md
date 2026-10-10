# UI

`ui.*` shows things on a player's screen: popups that can ask something, toasts, big titles and HUD panels. Every function takes the [player](players.md) first; to show something to everyone, loop over `world.players()`.

Scripts run on the server, so each call is sent to that player's game; nothing waits for it to arrive.

## Positions

Every element can go somewhere else than its usual place with `position`, one of `"center"`, `"top"`, `"bottom"`, `"left"`, `"right"`, `"topLeft"`, `"topRight"`, `"bottomLeft"` and `"bottomRight"`. It's placed against that side or corner of the screen, 40 pixels from its edges. Popups and titles also take an `offset`, `{x = 0, y = 0}`, to move them from there, in pixels of a 1920x1080 screen (`y` up; screens of other sizes scale it). Toasts and HUD panels stack at their position.

| Element | Usual place |
|---|---|
| Popup | `"center"` |
| Toast | `"top"` |
| Title | a bit above the center |
| HUD panel | `"topRight"` |

```lua
ui.toast(player, "Saved", {position = "bottomRight"})
ui.title(player, "Round 2", "Go!", {seconds = 4, position = "top", offset = {x = 0, y = -100}})
```

## ui.popup(player, options, callback)

Opens a window with a title, a text, buttons and optionally a text field. Returns the popup's id (for `ui.close`). If the player already has a popup open, this one waits its turn.

`options`:
- `title`: the window's title.
- `text`: its text.
- `buttons`: a list of button labels, up to 6 (`{"OK"}` by default). The first is the main one: Enter clicks it.
- `input`: `true`, or `{text = "...", placeholder = "..."}`, for a text field.
- `position` and `offset`: where it goes, see [Positions](#positions).

`callback(button, input)` is called when the player closes it: `button` is the label of the button clicked, or `nil` if the player dismissed it (its X, or Escape); `input` is what was in the text field (`""` without one). It isn't called if the player leaves first, or if the script closes it with `ui.close`.

```lua
ui.popup(player, {
  title = "Rename your castle",
  text = "Everyone will see the new name.",
  input = {placeholder = "Castle name"},
  buttons = {"Rename", "Cancel"},
}, function(button, name)
  if button == "Rename" and name ~= "" then
    shared.castleName = name
    ui.toast(player, "Your castle is now " .. name)
  end
end)
```

While a popup is open the player stands still and the mouse is free; keys don't do anything else.

## ui.close(player, id)

Closes one of the mod's popups (open or waiting) without calling its callback. Returns `false` if it's not open anymore.

## ui.toast(player, text, seconds)

Shows a short message under the top of the screen that fades away after `seconds` (3 by default, 0.5 to 30). Several toasts stack.

Instead of the seconds it takes a table `{seconds = 3, position = "bottomRight"}`, see [Positions](#positions).

```lua
ui.toast(player, "You found a gem!")
```

## ui.title(player, title, subtitle, seconds)

Shows a big title in the middle of the screen, with an optional smaller `subtitle` under it, for `seconds` (3 by default, 0.5 to 30). A new title replaces the one showing.

Instead of the seconds it takes a table `{seconds = 3, position = "top", offset = {x = 0, y = -100}}`, see [Positions](#positions).

```lua
for _, other in ipairs(world.players()) do
  ui.title(other, "Round 2", "Build a tower in 3 minutes", 4)
end
```

## ui.hud(player, key, options)

Shows a small panel at the top right of the screen, or updates it if the mod already showed one with that `key`. A mod can have several panels; mods can't touch each other's.

`options` (all optional):
- `title`: the panel's title.
- `lines`: a list of lines of text, up to 20.
- `progress`: a number from 0 to 1 for a progress bar (no bar without it).
- `position`: where it goes (`"topRight"` by default), see [Positions](#positions). Updating a panel with another position moves it.

```lua
ui.hud(player, "quest", {
  title = "Collect stones",
  lines = {"Stones: 7 / 10"},
  progress = 0.7,
})
```

## ui.removeHud(player, key)

Removes the mod's panel with that `key`.

## Limits

Titles and button labels are cut at 100 and 40 characters, texts at 2000, text fields at 200. A player can have up to 10 of a server's popups waiting for an answer. Texts can use [rich text](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/manual/StyledText.html) (`<b>`, `<color=#ff0000>`...).
