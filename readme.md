[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/preview.gif)](https://github.com/tetreum/brickcraft/raw/main/Preview/preview.gif)

[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/world.gif)](https://github.com/tetreum/brickcraft/raw/main/Preview/world.gif)

[![Logo](https://github.com/tetreum/brickcraft/raw/main/Assets/Textures/Logo.png)](https://github.com/tetreum/brickcraft/raw/main/Assets/Textures/Logo.png)

# Brickcraft

WIP. Combining Lego like bricks + Minecraft buidling style in Unity.

[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/1.png)](https://github.com/tetreum/brickcraft/raw/main/Preview/1.png)

## Blog

[https://tetreum.github.io/brickcraft/](https://tetreum.github.io/brickcraft/)

## Controls

Players can change every key in Settings → Controls (their keys are kept in PlayerPrefs). Otherwise each one is a button of the Input Manager (Edit → Project Settings → Input Manager), named in brackets, so the project can change the defaults; a button missing from it falls back to the default key listed here. `GameInput.Bindings` lists them all.

- WASD - Move character (MoveForward, MoveBack, MoveLeft, MoveRight; the Horizontal and Vertical axes while the player didn't remap them)
- Space - Jump (Jump)
- Left Shift (hold) - Sprint (Sprint)
- TAB (hold) - Player list (PlayerList)
- 1-9 - Select a fast inventory slot (Slot1 to Slot9)
- I - Open inventory (Inventory). Drag items to move or swap them; right click slots while dragging to drop one unit in each (letting go then puts the rest back)
- Escape - Menu, or close the chat (Menu)
- Enter - Chat (Chat), Enter again to send
- Left click (hold) - Remove placed bricks and terrain blocks (Dig)
- Right click (having a block selected in inventory) - Adds a block (Place)
- Mouse wheel / R (having a block selected in inventory) - Rotates block (Rotate)
- Shift (hold, while placing) - Place on any stud instead of snapping to the terrain block grid (FreePlacement)

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

- **Singleplayer** lists the saved worlds, to play or delete them, or creates a new one with its name, seed (a number, any text, or empty for a random one) and difficulty (Peaceful, Normal or Hard, kept with the world; nothing uses it yet). The world runs on a host that doesn't listen for connections, so nobody else can join.
- **Host game** runs a host other players can join (UDP port 7777 by default, see the KcpTransport in `Resources/NetworkManager`).
- **Join game** connects to the address typed next to the button (`localhost` if empty).

The server owns the world. Clients generate the terrain from the server's seed and only receive what changed since (dug or placed blocks, bricks). Players ask the server to dig and place, and it checks the request before applying it.

The world has no edges, it's streamed around the players: the server keeps the chunks around every player loaded (`ViewDistance` on the World object, 8 chunks by default, plus a ring for their borders) and unloads the rest. Each client gets the chunks around its own player, nearest first, and live changes only for the chunks it has. Players spawn as soon as the chunks around the spawn are ready, and are held in place if they ever get to ground that isn't loaded yet.

To stay precise however far players go, the game uses a floating origin: game logic works in integer cells, blocks and chunks, Unity positions are relative to a chunk near the local player (subtracted while still integers), and when the player gets about 400 units away the origin moves and the scene shifts with it. Each peer has its own origin, so player positions are sent as absolute coordinates.

Besides player messages, the chat shows events: players joining, leaving, being kicked or banned. The server sends a `ChatEventMessage` saying what happened (type, player, who did it, reason) and each client words it (`ChatEvents.Describe`); to add an event, add a `ChatEventType`, its sentence in `Describe`, and send it with `ChatEvents.Send`.

The first player to join a save without admins (the host, starting singleplayer or a server) becomes its admin; everyone else joins as a `user`.

Admins can type commands in the chat:

- `/players`: online players and their ids.
- `/kick NICK|ID [reason]`: disconnects a player, who sees the reason in the menu.
- `/ban NICK|ID [reason]`: bans a player, online or not. Nobody can join from a banned player's machine either (except admins).
- `/unban NICK|ID`
- `/role NICK|ID [role]`: shows a player's role, or changes it (`user` or `admin`). Admins can't change their own.
- `/additem ITEM[@COLOR] [count] [NICK|ID]`: gives `count` (1 by default) of the item with id `ITEM` (like `dirt_2x4`) to a player, yourself by default. `@COLOR` gives it in a colour it can have, by colour id or name (`plate_2x2_yellow@4`, `brick_2x2@trans-clear`), its default one otherwise. Offline players get them in their saved inventory. `/additem ITEM NICK` works too, but a number after the item is always the count, so give a player by id with `/additem ITEM count ID`.

Admins also get an ALL tab in the inventory: every item of the game, searchable by name or id, 30 per page. Clicking one adds it to their inventory. The colour picker next to the search (a swatch per colour of `colors.csv`, hovering one shows its name) chooses the colour they're given in: only the items that can have it are listed. `Default` gives each item in its default colour.

`NICK|ID` is a player name (any case) or, if no name matches, a player id. Admins can't kick or ban themselves, other admins or the host.

The game version is Player Settings → Version (`GameVersion.Current`), shown in the main menu. Clients can only join servers running exactly the same version, otherwise the menu says which version the server runs, so bump it on every release that changes what goes over the network.

Players are identified by the name typed in the menu plus their machine id (`SystemInfo.deviceUniqueIdentifier`): the first machine that uses a name owns it on that server, so nobody else can play with it.

### Saves

Every world is saved in its own folder, `<persistentDataPath>/saves/<save name>/`, named after the world (`SavedWorlds`). Hosting a game uses the `saveName` of the NetworkManager, `world` by default:

- `world.dat`: the world's name, seed, difficulty and generator version, when it was created and last played, and which game versions created it and played it last.
- `regions/r.<x>.<z>.bcr`: what players changed (blocks and bricks), in region files of 32x32 chunks. The terrain itself is never saved, it's generated again from the seed and the changes are applied on top, so a save only grows with what players do, not with the size of the world. Each chunk record is compressed and can be rewritten alone; new versions are written before the old ones are released, so a crash can't leave half a chunk. Regions nobody changed have no file. Placed blocks and bricks also store who placed them (their id in `players.db`) and when (unix seconds); dug blocks don't. Bricks store their colour, and blocks placed in a colour other than their item's default one store it too (they're drawn with it instead of their textures).

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

Reusable UI controls live in `Assets/Scripts/UI/Controls`: `MenuButton` (the `Prefabs/UI/MenuButton` prefab: set its label, icon and Primary/Secondary variant, and its click on the Button), `SwitchToggle` and `SegmentedControl`.

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

## How can i help?

[https://tetreum.github.io/brickcraft/?/help](https://tetreum.github.io/brickcraft/?/help)


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
- `maxStack`: how many fit in one inventory slot, 64 by default. Dragging a stack onto the same item merges them as far as that allows.
- `brickModel`: the brick it places (see How can i add a new model).
- `color`: its colour, an id of `Assets/colors.csv` (like `4` for Red, see How can i add a new brick material). `colors`: other colours it can have, `"all"` or a list of ids; only its default one if missing. Each inventory stack is of one colour, recipes take ingredients of any colour and make the default one. Blocks placed in another colour are drawn in it, see-through for transparent colours; blocks without textures (like `brick_2x2`) use their default colour. Items without an icon nor textures get a square of their colour.
- `material`: a special material instead of its colour's, from Canvas -> Game -> BrickMaterials (only `"Water"` for now). `layer` sets the Unity layer of placed bricks, like `"Water"`.
- `block`: for bricks that are also a world block (it's named like the item, and placing the brick exactly over a world block places the block):
  - `hardness`: seconds to dig it with bare hands. `breakable: false` makes it undiggable.
  - `replaceable`: bricks can be placed inside it (like water).
  - `transparent`: sky light goes through it (like leaves or water).
  - `translucent`: drawn see-through (like water), with the terrain's translucent material (`Resources/Materials/Block_Translucent`, 60% opaque, its shader is `Resources/Shader/TerrainTranslucent`). What's behind it is drawn too, and its own sides only where they touch air.
  - `drop`: id of the item given when dug, like `"stone"`. The item itself if missing.
- `recipes`: ways to craft the item. Each makes `quantity` of it (1 by default) from its `ingredients`, each one an item `id`, a `quantity` (1 by default) and a crafting `slot`, 1 to 4 (left to right, top to bottom). Recipes are identified as `<item>#<number>`, like `dirt_2x4#1`.

Every field but `name` is optional. /Scenes/IconGenerator generates the missing `icon.png` of brick items.

Textures are square images (they're scaled to the biggest one). Models are OBJ files in game units: the pivot is at the center of the bottom face and a block spans 0.796 x 0.478 x 0.796 (the default model is in `StreamingAssets/Models`). Each triangle is drawn when the side of the block it's closest to is exposed to air (or, for opaque blocks, seen through a translucent one); triangles facing the inside of the block are drawn with the bottom side. Far from the camera (`DetailRadius` chunks, on the World object), blocks are drawn with their collider shape instead, so detailed models only cost where they can be seen.

## How can i add a new model?

1. Brick models & their prefabs are stored in https://github.com/tetreum/brickcraft/tree/main/Assets/Models/Bricks
2. Prefab must be listed at Server -> prefabs scene object.
3. Model specs (footprint in studs and height in plates) must be added at Server.cs#setupBrickModels(). The model's pivot must be at the center of its footprint, on its bottom face, like the existing ones.
4. Items using it are added like any item (see How can i add a new item?), with its number as `brickModel`.
5. To generate their icon, head to /Scenes/IconGenerator & simply hit Play. Items with missing icons will have their icon generated.

### Block and item ids

Items (and their blocks, named like them) are identified by slugs, so ones added by different people don't clash: lowercase letters, digits and `_`, like `dirt_2x4`. Mods can prefix theirs, like `mymod:red_brick` Saves store these names. While the game runs, blocks also get a number (what chunks store, one byte per block): built-in blocks keep the one of their `BlockType`, the others get free ones, and these numbers are never saved.

## How can i add a new brick material/texture?

1. They're stored in Assets/Materials/BrickColors/ (https://github.com/tetreum/brickcraft/tree/main/Assets/Materials/BrickColors)
2. List the new materials at Canvas (scene object) -> Game -> BrickMaterials var.

Brick colours are limited to Rebrickable's colour list, `Assets/colors.csv` (id, name, rgb, is_trans, ...). `Brickcraft > Generate brick colour materials` makes a material per colour in `Assets/Materials/BrickColors/Palette/<id>_<Name>.mat` (opaque ones copy `BrightGreen.mat`, transparent ones `TransparentBlue.mat`; Chrome, Metallic, Pearl, Glitter and Opal colours get their finish from the name) and `Resources/BrickColorPalette`, which finds them by colour id (`BrickColorPalette.Get(id)`). Running it again only updates the colour and finish of existing materials, so tweaks made in the editor are kept.

## Credits

- stone-SJH for fixing "Terrain mesh is randomly broken because of brick's top face" bug at https://github.com/tetreum/brickcraft/pull/2
- Tapping sound effect - https://freesound.org/people/rioforce/sounds/233654/
- Dig + remove block sound effect - https://freesound.org/people/Agaxly/sounds/213005/
- Brick models - https://www.mecabricks.com/
- Break texture - MooCwzRck - https://www.minecraftforum.net/forums/mapping-and-modding-java-edition/resource-packs/1223258-16x128x-1-4-5-compatible-okami-texture-pack?page=5
- World chunk system - Smjert - https://github.com/chraft/chunk-light-tester/
- Logo font/style - Sverdlychenko Studio - http://sverdlychenko.com/en/lego-font-design/