# Globals

## log(...)

Writes its arguments, separated by spaces, to the server log, prefixed with the mod's id. Bricks, players and positions print readably.

```lua
log("placed", brick, "by", player)  -- [my_mod] placed brick my_mod:catapult at (4, 252, 0) by player Ann
```

## print(...)

The same as `log`.

## text(player, key, ...)

The mod's text of `key` in the player's language: from its `lang/[language].json` (see the Mods section of the readme), English's if that language hasn't it, `key` itself if neither has. `{0}`, `{1}`... are replaced by the arguments after the key. Instead of a player, a language can be given (`"es"`).

```lua
-- lang/en.json: { "welcome": "Welcome, {0}!" }   lang/es.json: { "welcome": "¡Bienvenido, {0}!" }
function onPlayerJoined(player)
  player:message(text(player, "welcome", player.name))
end
```

## shared

A table shared by all of the mod's scripts (each file has its own globals otherwise). It lasts until the server stops; nothing in it is saved.

```lua
-- scripts/main.lua
function onLoad()
  shared.launches = 0
end

-- items/launcher/script.lua
function onInteract(brick, player)
  shared.launches = shared.launches + 1
end
```

## Lua's standard library

Scripts get `string`, `table`, `math`, `bit32`, coroutines (`coroutine`), metatables (`setmetatable`, `getmetatable`, `rawget`...), error handling (`pcall`, `error`, `assert`), the basic functions (`pairs`, `ipairs`, `tostring`, `tonumber`, `type`, `select`...) and the time functions of `os` (`os.time`, `os.clock`, `os.date`).

They don't get `io`, the rest of `os`, `load`, `loadstring`, `dofile`, `require` or `debug`: scripts can't touch files or load more code.
