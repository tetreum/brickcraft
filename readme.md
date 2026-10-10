[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/preview.gif)](https://github.com/tetreum/brickcraft/raw/main/Preview/preview.gif)

[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/world.gif)](https://github.com/tetreum/brickcraft/raw/main/Preview/world.gif)

[![Logo](https://github.com/tetreum/brickcraft/raw/main/Assets/Textures/Logo.png)](https://github.com/tetreum/brickcraft/raw/main/Assets/Textures/Logo.png)

# Brickcraft

WIP. Combining Lego like bricks + Minecraft buidling style in Unity.

[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/1.png)](https://github.com/tetreum/brickcraft/raw/main/Preview/1.png)

## How can i help?

- Character model: It's poorly made, needs a rework to also have clothes and hair separated so clothing system can be added in the future. Since minifigures are patented, the model is an advanced iteration that has things like knees.

## Settings

The Settings button of the main menu and the ESC menu opens the player's preferences (`GameSettings`, kept in PlayerPrefs). Changes apply right away and raise `EventManager.SettingChanged`.

- Language: only English for now.
- Auto Save: the host saves the world every 30 seconds. Off, it's still saved when regions unload and when the server stops.
- Show Coordinates: the player's block coordinates, top left.
- Crosshair
- Tutorial Hints: stored, there are no hints yet.

The Controls tab lists every action with its key: clicking one waits for the next key or mouse button (Escape cancels), and Reset to defaults brings the default keys back.

The Audio tab has the master volume (the whole game, `AudioListener.volume`), the music volume and the effects volume (SoundManager's effects and the player's steps). `SoundManager.PlayMusic` plays music at the music volume; there's no music in the game yet.

Its icons and rounded shapes are white sprites tinted in Unity, drawn by `python Tools/make_ui_sprites.py` (needs Pillow) into `Assets/Textures/UI/Sprites`.

## Multiplayer

Multiplayer uses [Mirror](https://mirror-networking.gitbook.io/docs), patched in one place (search for "Brickcraft patch" in `Assets/Mirror`, and apply it again after upgrading Mirror): `NetworkIdentity._connectionToClient` is `[NonSerialized]`, otherwise reloading scripts with the player prefab loaded throws "get_time is not allowed to be called during serialization". Every game is networked:

- **Singleplayer** lists the saved worlds, to play or delete them, or creates a new one with its name, seed (a number, any text, or empty for a random one), difficulty (Peaceful, Normal or Hard, kept with the world: NPCs don't hurt players in Peaceful worlds, and hurt them half as much again in Hard ones) and PvP (on by default: whether players can hurt each other, kept with the world and changed by admins with `/pvp`). The world runs on a host that doesn't listen for connections, so nobody else can join.
- **Host game** runs a host other players can join (UDP port 7777 by default, see the KcpTransport in `Resources/NetworkManager`).
- **Join game** connects to the address typed next to the button (`localhost` if empty).

The server owns the world. Clients generate the terrain from the server's seed and only receive what changed since (dug or placed blocks, bricks). Players ask the server to dig and place, and it checks the request before applying it.

The world has no edges, it's streamed around the players: the server keeps the chunks around every player loaded (`ViewDistance` on the World object, 8 chunks by default, plus a ring for their borders) and unloads the rest. Each client gets the chunks around its own player, nearest first, and live changes only for the chunks it has. Players spawn as soon as the chunks around the spawn are ready, and are held in place if they ever get to ground that isn't loaded yet.

To stay precise however far players go, the game uses a floating origin: game logic works in integer cells, blocks and chunks, Unity positions are relative to a chunk near the local player (subtracted while still integers), and when the player gets about 400 units away the origin moves and the scene shifts with it. Each peer has its own origin, so player positions are sent as absolute coordinates.

Players have health (`Combat.Health`, 20 by default, decided by the server; NPCs will have it too). Left clicking something with health hits it for the damage of the item in hand (`damage` in its info.json, 1 for a bare hand), at most every 0.4 seconds and from close by, and pushes it back; players can only be hit with PvP on. After a hit there's half a second nothing can hurt it again. The health bar above the bottom bar shows it. A dead player can't move or act: a popup says who killed it, and closing it respawns it at the spawn with all its health and its inventory (`PlayerNetwork.CmdRespawn`). Health isn't saved: players join with all of it.

Besides player messages, the chat shows events: players joining, leaving, being kicked, banned or killed. The server sends a `ChatEventMessage` saying what happened (type, player, who did it, reason) and each client words it (`ChatEvents.Describe`); to add an event, add a `ChatEventType`, its sentence in `Describe`, and send it with `ChatEvents.Send`.

The first player to join a save without admins (the host, starting singleplayer or a server) becomes its admin; everyone else joins as a `user`.

Admins can type commands in the chat:

- `/players`: online players and their ids.
- `/kick NICK|ID [reason]`: disconnects a player, who sees the reason in the menu.
- `/ban NICK|ID [reason]`: bans a player, online or not. Nobody can join from a banned player's machine either (except admins).
- `/unban NICK|ID`
- `/tp NICK|ID`: takes you to an online player.
- `/pvp [on|off]`: shows or changes whether players can hurt each other, saved with the world.
- `/spawnnpc NPC [count]`: puts `count` (1 by default, 20 at most) NPCs of that kind (like `golem`) a few meters in front of you.
- `/removenpcs`: takes every NPC out of the world.
- `/role NICK|ID [role]`: shows a player's role, or changes it (`user` or `admin`). Admins can't change their own.
- `/additem ITEM[@COLOR] [count] [NICK|ID]`: gives `count` (1 by default) of the item with id `ITEM` (like `dirt_2x4`) to a player, yourself by default. `@COLOR` gives it in a colour it can have, by colour id or name (`plate_2x2_yellow@4`, `brick_2x2@trans-clear`), its default one otherwise. Offline players get them in their saved inventory. `/additem ITEM NICK` works too, but a number after the item is always the count, so give a player by id with `/additem ITEM count ID`.

Admins also get an ALL tab in the inventory: every item of the game, searchable by name or id, 30 per page. Clicking one adds it to their inventory. The colour picker next to the search (a swatch per colour, hovering one shows its name) chooses the colour they're given in: only the items that can have it are listed. `Default` gives each item in its default colour.

The player list (held Tab) gives admins TP, Kick and Ban buttons on each player: right-click while it's shown frees the mouse to click them. They send the commands above, kicking and banning ask first, with an optional reason.

`NICK|ID` is a player name (any case) or, if no name matches, a player id. Admins can't kick or ban themselves, other admins or the host.

The game version is Player Settings → Version (`GameVersion.Current`), shown in the main menu. Clients can only join servers running exactly the same version, otherwise the menu says which version the server runs, so bump it on every release that changes what goes over the network.

Players are identified by the name typed in the menu plus their machine id (`SystemInfo.deviceUniqueIdentifier`): the first machine that uses a name owns it on that server, so nobody else can play with it.

### Saves

Every world is saved in its own folder, `<persistentDataPath>/saves/<save name>/`, named after the world (`SavedWorlds`). Hosting a game uses the `saveName` of the NetworkManager, `world` by default:

- `world.dat`: the world's name, seed, difficulty and generator version, when it was created and last played, which game versions created it and played it last, and its mods (see Mods).
- `regions/r.<x>.<z>.bcr`: what players changed (blocks and bricks), in region files of 32x32 chunks. The terrain itself is never saved, it's generated again from the seed and the changes are applied on top, so a save only grows with what players do, not with the size of the world. Each chunk record is compressed and can be rewritten alone; new versions are written before the old ones are released, so a crash can't leave half a chunk. Regions nobody changed have no file. Placed blocks and bricks also store who placed them (their id in `players.db`) and when (unix seconds); dug blocks don't. Bricks store their colour, and blocks placed in a colour other than their item's default one store it too (they're drawn with it instead of their textures).

Saves have a format number (`WorldStorage.FormatVersion`, written in `world.dat`) that covers all their files. Older saves are upgraded when they're played: each step is a migration in `Assets/Scripts/World/Persistence/Migrations` (`Migration3To4` upgrades format 3 to 4) and they're chained, so a save several formats old goes through each one. The save is backed up first in `<persistentDataPath>/backups/<save>-format<N>-<date>/` and put back if a migration fails. Migrations read and write the formats they convert between with their own code, so they keep working as the game changes. Saves of a newer game, older than `SaveMigrations.OldestSupported` or with no migration path stay in the list with why they can't be played. When a save's format changes (world.dat, chunk records, players.db), bump `FormatVersion` and add a migration from the previous one.

Regions are streamed with the world: the changes of a region are read when the server loads its first chunk, and saved and dropped from memory when it unloads its last one, so memory only holds the areas around players.
- `players.db`: the players database described below.

Changed chunks are written on a background thread every 30 seconds (`autosaveInterval`), when their region unloads and when the server stops, all through one ordered queue so a chunk is never overwritten by an older version. The test scene isn't saved.

The server keeps a SQLite database (`players.db`, via [unity-sqlite-net](https://github.com/gilzoide/unity-sqlite-net)) with:

- `players`: everyone that joined, with their hashed machine id, role (`user` or `admin`), experience, equipment (JSON, `{"head": {"id": "...", "count": 1, "components": [...]}, "chest": ..., "legs": ..., "feet": ...}`, null when nothing is worn), number of joins, play time, and where they were (absolute position and facing) when they left, saved on disconnect and with every autosave. Players come back there next time, the world streams in around that spot.
- `player_sessions`: every join, with its address and when the player joined and left.
- `inventory_items`: each player's inventory. Inventories only change on the server and are synced to their owner; new players get the `starterItems` of the NetworkManager.

To test with several instances, a build can be started with `-name <name>` and `-host` or `-join <address>`. `-test` starts the test scene, which has no button in the menu, and `-profileLoad` logs how long loading the world takes, step by step (`WorldLoadProfiler`). Playing a game scene straight from the editor starts a singleplayer game.

## Code structure

The code is split in assemblies, so each part only sees what it needs:

- `Brickcraft.Events` (`Assets/Scripts/Events`): the events below. It depends on nothing else of the game.
- `Brickcraft` (`Assets/Scripts`): the game itself: world, bricks, player, network, database.
- `Brickcraft.UI` (`Assets/Scripts/UI`): menus and panels. It uses the game, but the game never calls the UI: it raises events the UI listens to, and the UI owns its own shortcuts (inventory, menu, chat, player list).

Reusable UI controls live in `Assets/Scripts/UI/Controls`: `MenuButton` (the `Prefabs/UI/MenuButton` prefab: set its label, icon and Primary/Secondary variant, and its click on the Button), `SwitchToggle`, `SegmentedControl`, `ColorPicker`, and `Popup`, `Toast` and `HudPanel` (prefabs in `Prefabs/UI`, used by `ModUiPanel` to show what mods' scripts ask for, see [docs/lua/ui.md](docs/lua/ui.md)).

## Events

Parts of the game talk through events instead of calling each other. `EventManager` lists every event, and the event data classes live next to it:

```csharp
EventManager.PlayerRoleChanged.Subscribe(onRoleChanged);   // usually in OnEnable
EventManager.PlayerRoleChanged.Unsubscribe(onRoleChanged); // and in OnDisable
EventManager.PlayerRoleChanged.Raise(new PlayerRoleChangedEvent() { ... });
```

- `PlayerRoleChanged`: a player's role changed.
- `InventoryChanged`: the local player's inventory changed.
- `LocalPlayerStarted`: the player of this client spawned.
- `SelectedSlotChanged`: the local player selected another hotbar slot.
- `WorldLoadingStarted`: the world started loading.
- `ClientStarted`: this client connected to a server.
- `ChatLineReceived`: a chat message or something that happened (a player joined, left...).
- `Disconnected`: this client left the server or couldn't join it, with the reason to show.

To add an event, add its data class to `Assets/Scripts/Events` and a field to `EventManager`.




## How can i add a new item?

Each item has its own folder in `Assets/StreamingAssets/Items` (`<Game>_Data/StreamingAssets/Items` in builds), loaded when the game starts (`ItemDatabase`). A folder with the id of another item replaces it.

```
Items/marble/
    info.json       what the item is (required)
    icon.png        inventory icon (its block's top or main texture if missing)
    texture.png     its block's texture, on every side
    top.png         optional, overrides texture.png on that side (also side.png, bottom.png)
    model.obj       optional block model, the default 2x2 brick otherwise
    collider.obj    optional block collider, model.obj (or the default brick collider) otherwise
```

`info.json`:

```json
{
    "id": "marble",
    "name": "Marble 2x2",
    "type": "brick",
    "maxStack": 64,
    "brickModel": 3003,
    "material": "BrightBlue",
    "block": {
        "hardness": 2.5,
        "breakable": true,
        "replaceable": false,
        "transparent": false,
        "translucent": false,
        "drop": "marble"
    },
    "recipes": [
        {
            "quantity": 2,
            "ingredients": [
                { "id": "stone", "quantity": 1, "slot": 1 },
                { "id": "sand", "quantity": 1, "slot": 2 }
            ]
        }
    ]
}
```

- `id` identifies the item, in saves too: a slug (see Block and item ids below). The folder's name if missing.
- `name`: what players see. Required.
- `type`: `brick` (the default), `helmet`, `weapon` or `food`. Only bricks can be placed.
- `damage`: how much health a hit takes while holding it, 1 (a bare hand's) by default.
- `maxStack`: how many fit in one inventory slot, 64 by default. Dragging a stack onto the same item merges them as far as that allows.
- `brickModel`: the brick it places: a part number of the game's models (`"3003"`, see How can i add a new model) or a model of the mod (see Mods).
- `color`: its colour, a colour id of the palette (like `4` for Red, see How can i add a new brick material). `colors`: other colours it can have, `"all"` or a list of ids; only its default one if missing. Each inventory stack is of one colour, recipes take ingredients of any colour and make the default one. Blocks placed in another colour are drawn in it, see-through for transparent colours; blocks without textures (like `brick_2x2`) use their default colour. Items without an icon nor textures get a square of their colour.
- `material`: a special material instead of its colour's, from Canvas -> Game -> BrickMaterials (only `"Water"` for now). `layer` sets the Unity layer of placed bricks, like `"Water"`.
- `block`: for bricks that are also a world block (it's named like the item, and placing the brick exactly over a world block places the block):
  - `hardness`: seconds to dig it with bare hands. `breakable: false` makes it undiggable.
  - `replaceable`: bricks can be placed inside it (like water).
  - `transparent`: sky light goes through it (like leaves or water).
  - `fluid`: a liquid (like water and lava): NPCs that walk keep out of it.
  - `translucent`: drawn see-through (like water), with the terrain's translucent material (`Resources/Materials/Block_Translucent`, 60% opaque, its shader is `Resources/Shader/TerrainTranslucent`). What's behind it is drawn too, and its own sides only where they touch air.
  - `drop`: id of the item given when dug, like `"stone"`. The item itself if missing.
- `recipes`: ways to craft the item. Each makes `quantity` of it (1 by default) from its `ingredients`, each one an item `id`, a `quantity` (1 by default) and a crafting `slot`, 1 to 4 (left to right, top to bottom). Recipes are identified as `<item>#<number>`, like `dirt_2x4#1`.

Every field but `name` is optional. /Scenes/IconGenerator generates the missing `icon.png` of brick items.

Textures are square images (they're scaled to the biggest one). Models are OBJ files in game units: the pivot is at the center of the bottom face and a block spans 0.796 x 0.478 x 0.796 (the default model is in `StreamingAssets/Models`). Each triangle is drawn when the side of the block it's closest to is exposed to air (or, for opaque blocks, seen through a translucent one); triangles facing the inside of the block are drawn with the bottom side. Far from the camera (`DetailRadius` chunks, on the World object), blocks are drawn with their collider shape instead, so detailed models only cost where they can be seen.

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
```

`name` and `version` are required. `Mods/example` is a small mod to start from: a sturdy brick that takes three hits, a launcher that shoots plates up when used, and a welcome message. Builds don't copy the `Mods` folder, it's put next to the game by hand. Mod items are named after their mod: `items/catapult` is `my_castle:catapult` (their `info.json` can't set a prefix). Item ids in a mod's `info.json` (recipe ingredients, block drops) are first looked for in the mod, so `"gear"` means `my_castle:gear` if the mod has it and the game's `gear` otherwise; `"other_mod:gear"` names any other loaded item.

Mods' brick models are `[mod]:[name]`, and their items use them like the game's (`"brickModel": "catapult"` in the mod finds its own `my_castle:catapult` first). A model folder is like the game's file models (see How can i add a new model?). Complex models can come as prefabs in AssetBundles (`models/*.bundle`, built with Unity, this game's version): each prefab is a model named after it, with a BoxCollider of its size like the game's custom prefabs. Bundles are built for one platform, and their prefabs can only use components the game has (no code of their own). `Brickcraft > Generate missing item icons` renders mods' items' icons too.

Each world has its own mods, chosen when it's created (New world → Mods). They're saved in its `world.dat` (with the version it was last played with) and loaded when it's played, besides the game's own items. A world whose mods aren't installed shows them as missing in the list; it can still be played, but their items are unknown and their blocks become air.

Players joining a server need its world's mods: the same version with the same files (the server compares a SHA-256 of each mod's files). Otherwise they're refused and told which ones, like *"This server needs the mod My Castle 1.2 (you have 1.0)"*. Once accepted they play with the server's mods only, whatever else they have installed.

### Scripts

Mods can script their bricks in Lua ([MoonSharp](https://www.moonsharp.org/), Lua 5.2): `items/[item]/script.lua` handles the item's events (placed, hit, broken, used) and `scripts/*.lua` the mod's (loaded, players joining, leaving and chatting). Scripts run on the server only, sandboxed, and can spawn, move and remove bricks, change world blocks, message players and give them items, show them popups, toasts, titles and HUD panels, and run timers.

The reference, a page per section, is in [docs/lua](docs/lua/README.md).

## How can i add an NPC?

NPCs are creatures the server runs (`Npcs.Npc`): they wander, go for players, attack, take hits and die (see `Combat.Health`), and whoever kills one gets its drops. Each kind has a folder in `Assets/StreamingAssets/NPCs` (mods: `npcs/` in the mod's folder, ids `[mod]:[npc]`) with an `info.json` (`Npcs.NpcInfo`, loaded with the items by `Npcs.NpcDatabase`):

```json
{
    "name": "Golem",
    "model": "golem",
    "scale": 1,
    "health": 60,
    "speed": 1.6,
    "stepHeight": 0.5,
    "moves": "ground",
    "behaviour": "aggressive",
    "sightRange": 16,
    "attack": { "damage": 5, "range": 0.9, "cooldown": 1.8, "hitTime": 1.2, "knockback": 8 },
    "drops": [ { "item": "iron_ore", "count": 3, "chance": 1 } ],
    "animations": { "idle": "idle", "walk": "walk", "attack": "attack", "death": "death" }
}
```

- Distances are meters (a block is 0.8 wide and 0.48 high, a player 1.8 tall). `speed` is in meters per second, `stepHeight` how high it steps up without jumping.
- `moves`: `ground` (the default) walks and keeps out of fluids (blocks with `fluid`, and water bricks): it doesn't step onto them nor walk off an edge into them. `water`, `amphibious` and `air` (swimming and flying) come with the navigation grid; until then they walk like `ground` ones.
- `behaviour`: `neutral` (the default) wanders and fights back whoever hurts it, until it loses them (15 seconds, or too far); `aggressive` also goes for the nearest player within `sightRange` (not in Peaceful worlds).
- `attack`: `range` from its side, `cooldown` seconds between attacks, `hitTime` seconds into the attack animation when the hit lands (if the target is still in reach), `knockback` how hard it pushes.
- `drops`: items (and their `chance`, 0 to 1) whoever kills it gets in their inventory.
- `animations`: the model's names for its idle, walk, attack and death animations, if they're named otherwise.

For now NPCs walk straight to where they go, stepping up what's low enough (the navigation grid comes next). They only move and think in chunks within 4 chunks of a player (`Npcs.NpcSystem.ActiveRadius`), go away with the chunks the server unloads, and players only get the NPCs of the chunks they have (`Network.ChunkInterestManagement`).

`model` is a prefab of `Assets/Resources/NpcModels` (`Npcs.NpcModel`), made from an animated glTF model with `Brickcraft > Import NPC model (glTF)...` (like `Assets/Models/NPCs/Golem/Golem.gltf`, exported from [Blockbench](https://www.blockbench.net/)): its nodes, textures (unfiltered, for pixel art), an AnimationClip per animation and an Animator controller with a state for each, saved next to the glTF. A node named `hitbox` isn't drawn: it's where the NPC can be hit and what it bumps into (all its meshes otherwise). Parts no animation moves are merged into one mesh per moving part. Models move their nodes (no skinned meshes). Mods can't bring their own models yet.

## How can i add a new model?

Brick models (what items' `brickModel` names) are found by id when they're first needed, no list to keep: the game only lists the ids when it starts and reads a model the first time an item or a brick uses it, so the library can hold thousands of models while a world only loads what it uses (`Bricks.BrickModels`). An id is looked for in this order:

1. a mod's models (`Mods/[mod]/models/`, see Mods), named `[mod]:[name]`;
2. a custom prefab of the game, `Assets/Resources/BrickModels/[id].prefab`, for complex models;
3. a file model of the game, `StreamingAssets/BrickModels/[id]/`: most bricks, named by part number.

A file model's folder has `model.obj`, `model.glb` or `model.gltf` (its shape; OBJ materials and glTF materials are its parts), an optional `normal.png` (a normal map for its first uv set, like its bevels) and an optional `model.json`:

```json
{
    "scale": 0.05,
    "collider": "mesh",
    "parts": { "Glass": 47 },
    "width": 2, "depth": 2, "plates": 3
}
```

- `scale`: what its units are worth in the game's (1 for game units, where a stud is 0.398; the bricks from Mecabricks' OBJ exports are in millimeters, 0.05).
- `collider`: `"box"` (the default, a box of its size) or `"mesh"` (its shape, for slopes, arches...).
- `parts`: materials that keep a colour of their own (a palette colour id, like 47 Trans-Clear for a window); the others take the brick's colour.
- `width`, `depth`, `plates`: its size, if the one worked out from its shape is wrong (footprint in studs, height in plates rounded down so studs on top don't count; under 3 plates it's a plate).

The shape is moved so its pivot is at the center of its footprint, on its bottom face. OBJ files without normals get them smoothed under 60°, glTF primitives without normals are flat (only geometry is read from glTF: no textures, Draco or meshopt).

A custom prefab is named after its id. Its size comes from the BoxCollider on its root, which fills its footprint and height (studs left out), or, without one, from the mesh on its root (like file models: a mesh collider then lets players through openings, like the door frame 60596). Its pivot is at the center of its footprint, on its bottom face. Its materials (children's too) are replaced by the brick's colour, except the parts a `BrickModelParts` component gives a colour of their own.

Bricks have a state (`Brick.state`, an int saved with them and sent to players, 0 when placed) that their prefab can show: components implementing `IBrickState` hear it when the brick appears and when it changes (`WorldNetwork.ServerSetBrickState`). `BrickDoor` is one: its hinge turns the door open (1) or closed (0), and a left click on a brick with one toggles it (`PlayerNetwork.CmdToggleDoor`); holding the click still breaks it. 

Some models are attachments (a `BrickAttachment` on the prefab's root): their bricks don't go on the grid but in the slot of another brick (a `BrickSlot`, with the point their pivot goes to), when both say the same `fits`. Holding one, the preview shows it in the slot of the brick you look at, and placing puts it there if the slot is free (`PlayerNetwork.CmdAttachBrick`). An attached brick (`Brick.attachedTo`, saved and sent like its state) takes no cells, moves with its brick and is removed on its own; removing its brick removes it too, and whoever removed them gets both. Mods' `world.spawn` can't spawn attachments.

Doors are both: the door frame 60596 has a slot that fits `door 4x6`, and the door 7102 is an attachment that fits it, with its pivot on its hinge. Other doors and frames only need the same `fits` (a door with another size, another `fits`).

Items using a model are added like any item (see How can i add a new item?), with its id as `brickModel`. Their icons: `Brickcraft > Generate missing item icons` renders one for every brick item without `icon.png` (items with a world block use their block's texture instead), in its colour, without playing (`BrickIconRenderer`).

Models exported from [Mecabricks](https://www.mecabricks.com/) (a folder with `config.json`, `geometry.json` and its normals image, like `Assets/Models/Bricks/3009`) are imported with `Brickcraft > Import Mecabricks model...`: it makes the mesh (with its studs, which Mecabricks leaves out) and a custom prefab in `Resources/BrickModels` with its normal map (its bevelled edges, see `BrickNormalMap`), and renders the icons of the items made of it. Importing a model again updates its mesh and leaves its prefab as it is, so what was added to it (a mesh collider, a hinge) stays. Brick colour materials have normal mapping on (with `Textures/FlatNormal.png`) so models' own normal maps can replace it.

### Block and item ids

Items (and their blocks, named like them) are identified by slugs, so ones added by different people don't clash: lowercase letters, digits and `_`, like `dirt_2x4`. Mods can prefix theirs, like `mymod:red_brick` Saves store these names. While the game runs, blocks also get a number (what chunks store, one byte per block): built-in blocks keep the one of their `BlockType`, the others get free ones, and these numbers are never saved.

## How can i add a new brick material/texture?

1. They're stored in Assets/Materials/BrickColors/ (https://github.com/tetreum/brickcraft/tree/main/Assets/Materials/BrickColors)
2. List the new materials at Canvas (scene object) -> Game -> BrickMaterials var.

Brick colours are limited to a palette, Rebrickable's colour list: `Resources/BrickColorPalette` lists each colour (Rebrickable id, name, hex, whether it's transparent) and its material in `Assets/Materials/BrickColors/Palette/<id>_<Name>.mat`; the game finds them by colour id (`BrickColorPalette.Get(id)`). To add a colour, copy a material of that folder (an opaque or transparent one), change its colour, and add an entry for it to the palette.

## Credits

- Lua interpreter - MoonSharp, Marco Mastropaolo (BSD 3-Clause, see Assets/Plugins/MoonSharp/LICENSE.txt) - https://www.moonsharp.org/
- stone-SJH for fixing "Terrain mesh is randomly broken because of brick's top face" bug at https://github.com/tetreum/brickcraft/pull/2
- Tapping sound effect - https://freesound.org/people/rioforce/sounds/233654/
- Dig + remove block sound effect - https://freesound.org/people/Agaxly/sounds/213005/
- Brick models - https://www.mecabricks.com/
- Break texture - MooCwzRck - https://www.minecraftforum.net/forums/mapping-and-modding-java-edition/resource-packs/1223258-16x128x-1-4-5-compatible-okami-texture-pack?page=5
- World chunk system - Smjert - https://github.com/chraft/chunk-light-tester/
- Test monster - iJUNE - https://sketchfab.com/3d-models/free-dummy-monster-246678f908b548feb0f4cccaeef78756#download
- Color list - https://rebrickable.com/downloads/