## Mods

Mods are folders in `Mods/`, next to the game (next to `[Game]_Data`, or the project's folder in the editor). The folder's name is the mod's id (lowercase letters, digits and `_`):

```
Mods/
  my_castle/
    index.json        { "name": "My Castle", "author": "Me", "version": "1.0", "description": "Catapults and towers" }
    items/
      catapult/       an item, like the folders of StreamingAssets/Items (info.json, icon.png, textures, model.obj...)
    models/
      catapult/       a brick model: model.obj, model.glb or model.gltf, and optionally normal.png and model.json
      towers.bundle   optional: an AssetBundle with prefabs, each a model named after it
    npcs/
      wolf/           an NPC: info.json, optionally model.glb or model.gltf and script.lua (see How can i add an NPC?)
      beasts.bundle   optional: an AssetBundle with NPC model prefabs, each named after it
```

`name` and `version` are required. `Mods/example` is a small mod to start from: a sturdy brick that takes three hits, a launcher that shoots plates up when used, a moss golem (an NPC with its own glTF model and script, that appears on grass and is tamed with leaves) and a welcome message. Builds don't copy the `Mods` folder, it's put next to the game by hand. Mod items are named after their mod: `items/catapult` is `my_castle:catapult` (their `info.json` can't set a prefix). Item ids in a mod's `info.json` (recipe ingredients, block drops) are first looked for in the mod, so `"gear"` means `my_castle:gear` if the mod has it and the game's `gear` otherwise; `"other_mod:gear"` names any other loaded item.

Mods' brick models are `[mod]:[name]`, and their items use them like the game's (`"brickModel": "catapult"` in the mod finds its own `my_castle:catapult` first). A model folder is like the game's file models (see How can i add a new model?). Complex models can come as prefabs in AssetBundles (`models/*.bundle`, built with Unity, this game's version): each prefab is a model named after it, with a BoxCollider of its size like the game's custom prefabs. Bundles are built for one platform, and their prefabs can only use components the game has (no code of their own). `Brickcraft > Generate missing item icons` renders mods' items' icons too.

Each world has its own mods, chosen when it's created (New world → Mods). They're saved in its `world.dat` (with the version it was last played with) and loaded when it's played, besides the game's own items. A world whose mods aren't installed shows them as missing in the list; it can still be played, but their items are unknown and their blocks become air.

Players joining a server need its world's mods: the same version with the same files (the server compares a SHA-256 of each mod's files). Otherwise they're refused and told which ones, like *"This server needs the mod My Castle 1.2 (you have 1.0)"*. Once accepted they play with the server's mods only, whatever else they have installed.

### Scripts

Mods can script their bricks in Lua ([MoonSharp](https://www.moonsharp.org/), Lua 5.2): `items/[item]/script.lua` handles the item's events (placed, hit, broken, used) and `scripts/*.lua` the mod's (loaded, players joining, leaving and chatting). Scripts run on the server only, sandboxed, and can spawn, move and remove bricks, change world blocks, message players and give them items, show them popups, toasts, titles and HUD panels, and run timers.

The reference, a page per section, is in [docs/lua](docs/lua/README.md).