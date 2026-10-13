[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/preview.gif)](https://github.com/tetreum/brickcraft/raw/main/Preview/preview.gif)

[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/world.gif)](https://github.com/tetreum/brickcraft/raw/main/Preview/world.gif)

[![Logo](https://github.com/tetreum/brickcraft/raw/main/Assets/Textures/Logo.png)](https://github.com/tetreum/brickcraft/raw/main/Assets/Textures/Logo.png)

# Brickcraft

WIP. Combining Lego like bricks + Minecraft buidling style in Unity.

[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/1.png)](https://github.com/tetreum/brickcraft/raw/main/Preview/1.png)

## How can i help?

- Character model: It's poorly made, needs a rework to also have clothes and hair separated so clothing system can be added in the future. Since minifigures are patented, the model is an advanced iteration that has things like knees.
- Animal models: Rigged & animated brick made animal models are needed to fill the world with some life.
- Improve post processing visuals: Current visuals are clearly not toon.

## Settings

The Settings button of the main menu and the ESC menu opens the player's preferences (`GameSettings`, kept in PlayerPrefs). Changes apply right away and raise `EventManager.SettingChanged`.

- Language: only English for now.
- Show Coordinates: the player's block coordinates, top left.
- Crosshair
- Tutorial Hints: stored, there are no hints yet.

The Graphics tab has the screen's resolution (those of the screen, and the window's size if it isn't one of them) and display mode (fullscreen, borderless window or windowed), which Unity remembers between runs, and the render distance: how far the world is drawn, from 6 to 96 chunks, 16 by default. The client tells the server when joining and whenever it changes, and the server sends it the chunks that far, up to its `maxRenderDistance` (see Multiplayer); the camera's far plane follows it.

The Controls tab lists every action with its key: clicking one waits for the next key or mouse button (Escape cancels), and Reset to defaults brings the default keys back.

The Audio tab has the master volume (the whole game, `AudioListener.volume`), the music volume and the effects volume (SoundManager's effects and the player's steps). `SoundManager.PlayMusic` plays music at the music volume; there's no music in the game yet.

Its icons and rounded shapes are white sprites tinted in Unity, drawn by `python Tools/make_ui_sprites.py` (needs Pillow) into `Assets/Textures/UI/Sprites`.

## Multiplayer

Multiplayer uses [Mirror](https://mirror-networking.gitbook.io/docs), patched in one place (search for "Brickcraft patch" in `Assets/Mirror`, and apply it again after upgrading Mirror): `NetworkIdentity._connectionToClient` is `[NonSerialized]`, otherwise reloading scripts with the player prefab loaded throws "get_time is not allowed to be called during serialization". Every game is networked:

- **Singleplayer** lists the saved worlds, to play or delete them, or creates a new one with its name, seed (a number, any text, or empty for a random one), difficulty (Peaceful, Normal or Hard, kept with the world: NPCs don't hurt players in Peaceful worlds, and hurt them half as much again in Hard ones) PvP (on by default: whether players can hurt each other, kept with the world and changed by admins with `/pvp`) and day length (10, 20, 30 or 60 minutes, 20 by default; admins change it with `/daylength`). The world runs on a host that doesn't listen for connections, so nobody else can join.
- **Host game** runs a host other players can join (UDP port 7777 by default, see the KcpTransport in `Resources/NetworkManager`).
- **Join game** connects to the address typed next to the button (`localhost` if empty), with the server's password in the box beside it if it has one (it's sent hashed, and never saved).

Servers are configured in `server.json`, next to the game (next to `Mods/`: the project's folder in the editor, next to `[Game]_Data` in builds), read every time one starts and written with the defaults the first time (`Network.ServerConfig`; it's in `.gitignore`):

```json
{
  "name": "Brickcraft server",
  "maxPlayers": 16,
  "password": "",
  "maxRenderDistance": 32
}
```

- `name`: what players see: the title of their player list, and the loading screen while they join.
- `maxPlayers`: players at once, the host's own player included; the next ones are told the server is full.
- `password`: what players need to join, none if empty.
- `maxRenderDistance`: the furthest players get the world, in chunks (6 to 96). Each draws as far as their own setting within it: asking for 40 on a server allowing 32 gets 32 (their settings say so).

The password and the render distance limit don't apply to the host's own player.

The server owns the world. Clients generate the terrain from the server's seed and only receive what changed since (dug or placed blocks, bricks). Players ask the server to dig and place, and it checks the request before applying it.

The world has no edges, it's streamed around the players: the server keeps the chunks around every player loaded (as far as that player's render distance, plus a ring for their borders) and unloads the rest. Each client gets the chunks around its own player, nearest first, and live changes only for the chunks it has. Players spawn as soon as the chunks around the spawn are ready, and are held in place if they ever get to ground that isn't loaded yet.

To stay precise however far players go, the game uses a floating origin: game logic works in integer cells, blocks and chunks, Unity positions are relative to a chunk near the local player (subtracted while still integers), and when the player gets about 400 units away the origin moves and the scene shifts with it. Each peer has its own origin, so player positions are sent as absolute coordinates.

Worlds have days and nights (`World.WorldTime`): the game's time is days since the world began, its fraction the time of day (the sun rises at 6 and sets at 18), and a day lasts the world's day length in real time. The server runs it and saves it with the world, so it goes on where it was; it tells clients when they join and every few seconds, and they run it on their side in between. In singleplayer it waits while the game menu (Escape) is open; in multiplayer it goes on as long as the server runs. The sky follows it (`World.DayNightCycle`): the scene's Sun light goes round (the physically based sky makes dawns, dusks and nights out of where it is, and ambient light follows the sky), a moon (a copy of the Sun light, dimmer and bluish, its face `Resources/World/moon_disc.png`) goes opposite, and the stars (the sky's space emission, `Textures/World/stars.png`) turn with them.

Parts lists: the Parts Lister (`Items/parts_lister`, a `tool`) lists the real LEGO parts of what a player built, to build it for real. Used (place button) on a brick or block the player placed, its controls freeze and the camera flies free (right mouse to look; move keys, jump and Ctrl to fly; sprint for faster), never farther than 10 meters from a see-through box around its creation: the bricks and blocks it placed connected to that one (`Network.PartsList`, up to 128 studs or plates along each side). Each face of the box moves with the mouse wheel or by dragging it (Shift: 4 at a time), or from the panel on the right, up to 32 studs past the creation. "Save parts list" (the list needs a name, which names its files) writes `parts_lists/[name]/[name].csv` in the game's data folder (a folder for each list, with its other files) (`brickModel,colorId,colorName,name,count`, colours by the palette's ids): the parts it placed whose middle is in the box, of models that are part numbers (they start with a digit), or the parts of items made of several (their `ldr`), by part and colour; blocks it placed count as their 2x2 brick, terrain and other players' bricks don't. Next to it, `[name].xml` is the same list as a BrickLink wanted list, to upload there: BrickLink's part numbers (an item's `bricklinkId`, or its model's id without the `uv` suffix of Mecabricks models) and colours (the palette's `bricklinkId`; colours BrickLink doesn't have go without one, any colour). And `[name].ldr` is the creation as an LDraw model (LeoCAD, Studio, LDView open it): every part where it is, from the box's corner, in LDraw's colours (the palette's `ldrawId`; colours LDraw doesn't have are left uncoloured) and part numbers (an item's `ldrawId`, or its model's id without `uv`). The list's folder is told in the chat (to that player only) and copied to the clipboard. Then the creation becomes a model of its own (`Bricks.CreationModel`), a mod ready to use in `[name]/mod/[id]/` (its id the name in lowercase, `_` for the rest): copied into `Mods/` (and added to a world) it's an item of one brick, the whole creation, with its icon and its parts (`model.ldr`, so parts lists count them); its `items/[id]` and `models/[id]` folders can also go in `StreamingAssets/Items` and `StreamingAssets/BrickModels` to make it one of the game's. The model is every part the player placed in the box (whatever they are) merged into one OBJ, a material for each colour, without what can't be seen: studs under other parts, faces against parts that fill that side, then (by rays sent every way from each face that's left) whatever no ray gets out of the creation from, like the insides of bricks walled in by others; transparent parts hide nothing. It has an exact collider (it can be walked into) and fills the grid cells of its parts' footprint; doors and lids are kept as they were, they don't move. Cancel (or Escape) closes it; getting hurt too.

Model importing: the Model Importer (`Items/model_importer`, a `tool`) places LDraw models (`.ldr`, or `.mpd` with submodels: what the Parts Lister writes, LeoCAD, Studio...) from the `imports` folder of the game's data folder, next to `parts_lists`. Used, it lists them; the one picked follows the crosshair as a see-through ghost (white where it fits, red where it doesn't; R turns it, Shift puts it on any stud instead of the blocks' grid, the place button places it, Escape or another slot gives up), up to 48 meters away. Its parts become the game's bricks (`Bricks.LDraw.LDrawImport`): the game's item of that part (its `ldrawId`, like the chest), or the catalogue's; doors, glass and lids go in the slot of the brick they hang from. Parts the grid can't hold are left out and told in the chat: parts the library hasn't, tilted ones, turned other than by quarter turns, off the studs, or where another part already is; mirrored ones are their brick turned. The server places them all, as placed by the player, or none if anything is in the way (`Network.ModelImporter`, up to 5000 bricks).

Storage: bricks of items tagged `storage` keep items, like the chest (`Items/chest`, model 4738v4, 27 slots; `Network.BrickStorage`). A click on one (or on its lid) opens it: the storage panel (`UI.StoragePanel`) shows its slots over the player's inventory, like the inventory without its crafting, and stacks are dragged between the two and the hotbar (or one unit with a right click while dragging). Only one player looks into one at a time: the others are told someone is inside. It closes with Escape or the inventory key, and when the player walks away (6 meters), dies, or it's broken. What it keeps is saved with its chunk (never sent with chunks: players get it when they open it). The chest's lid (`Items/chest_lid`, model 4739) attaches to it like a door to its frame (`BrickSlot` "chest lid", a `BrickAttachment` with a `BrickLid` hinge) and stays open while someone looks inside. Without a lid anyone can open a chest; with one only whoever placed the chest, and admins. Anyone can break a chest: what it kept falls around it.

Items on the ground (`Network.DroppedItem`, `Network.DroppedItems`): the item's model, small, turning and bobbing, falling onto what's under it. Walking onto one picks it up, as much as fits in the inventory (the rest stays); drops of the same stack close together merge; each goes 5 minutes after it fell. They're saved with their chunks (with the time they had left), and players only get those of the chunks they have. Things drop from broken chests, from NPCs killed by players (their `drops`), and from players: the Drop item key (Q) throws one of what's in hand (with Ctrl, the stack), and letting go of a dragged stack outside the inventory throws it; what a player throws can't be picked up for 2 seconds.

Players sleep in beds (bricks of items tagged `bed`, like `Items/bed`; `Network.PlayerSleep`): a click on one at night (18:00 to 06:00) lies the player in it, at its `SleepPoint`, unless someone else is in it or something is after them (the chat says why not); holding the click still breaks it. A sleeping player can't move or open the inventory, but can chat and look at the player list; the screen dims and says how many are asleep and how many are needed (`UI.SleepPanel`), with a button to get up; other players see them lying in the bed. When at least half the players are asleep, after 3 seconds the night is skipped: the time goes to 06:00 and everyone wakes up. Being hurt, the bed being broken or morning wake a player too. NPCs don't lie in a bed a player sleeps in. A bed clicked becomes the player's spawn point, even by day when it can't sleep in it: dying, it comes back beside it (the world's spawn otherwise) until it clicks another; when the bed goes away (broken, or removed by a script) everyone who had it loses it.

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
- `/time [day|noon|night|midnight|HH:MM]`: shows the game's time, or moves it on to the next time it's that hour (day is 7:00, night 20:00).
- `/daylength [minutes]`: shows or changes how long a day lasts (1 to 1440 minutes), saved with the world.
- `/spawnnpc NPC [count]`: puts `count` (1 by default, 20 at most) NPCs of that kind (like `golem`) a few meters in front of you; several are a pack.
- `/removenpcs`: takes every NPC out of the world.
- `/navdebug`: draws where NPCs can walk and their paths in the editor's scene view.
- `/role NICK|ID [role]`: shows a player's role, or changes it (`user` or `admin`). Admins can't change their own.
- `/additem ITEM[@COLOR] [count] [NICK|ID]`: gives `count` (1 by default) of the item with id `ITEM` (like `dirt_2x4`) to a player, yourself by default. `@COLOR` gives it in a colour it can have, by colour id or name (`plate_2x2_yellow@4`, `brick_2x2@trans-clear`), its default one otherwise. Offline players get them in their saved inventory. `/additem ITEM NICK` works too, but a number after the item is always the count, so give a player by id with `/additem ITEM count ID`.

Admins also get an ALL tab in the inventory: every item of the game, searchable by name or id, 30 per page. Clicking one adds it to their inventory. The colour picker next to the search (a swatch per colour, hovering one shows its name) chooses the colour they're given in: only the items that can have it are listed. `Default` gives each item in its default colour.

The player list (held Tab) gives admins TP, Kick and Ban buttons on each player: right-click while it's shown frees the mouse to click them. They send the commands above, kicking and banning ask first, with an optional reason.

`NICK|ID` is a player name (any case) or, if no name matches, a player id. Admins can't kick or ban themselves, other admins or the host.

The game version is Player Settings → Version (`GameVersion.Current`), shown in the main menu. Clients can only join servers running exactly the same version, otherwise the menu says which version the server runs, so bump it on every release that changes what goes over the network.

Players are identified by the name typed in the menu plus their machine id (`SystemInfo.deviceUniqueIdentifier`): the first machine that uses a name owns it on that server, so nobody else can play with it.

### Saves

Every world is saved in its own folder, `<persistentDataPath>/saves/<save name>/`, named after the world (`SavedWorlds`). Hosting a game uses the `saveName` of the NetworkManager, `world` by default:

- `world.dat`: the world's name, seed, difficulty and generator version, when it was created and last played, which game versions created it and played it last, its mods (see Mods), PvP, its day length and the game's time (written with every autosave).
- `regions/r.<x>.<z>.bcr`: what players changed (blocks and bricks), in region files of 32x32 chunks. The terrain itself is never saved, it's generated again from the seed and the changes are applied on top, so a save only grows with what players do, not with the size of the world. Each chunk record is compressed and can be rewritten alone; new versions are written before the old ones are released, so a crash can't leave half a chunk. Regions nobody changed have no file. Placed blocks and bricks also store who placed them (their id in `players.db`) and when (unix seconds); dug blocks don't. Bricks store their colour, and blocks placed in a colour other than their item's default one store it too (they're drawn with it instead of their textures). The NPCs in a chunk are saved in its record too (never sent to clients).

Saves have a format number (`WorldStorage.FormatVersion`, written in `world.dat`) that covers all their files. Older saves are upgraded when they're played: each step is a migration in `Assets/Scripts/World/Persistence/Migrations` (`Migration3To4` upgrades format 3 to 4) and they're chained, so a save several formats old goes through each one. The save is backed up first in `<persistentDataPath>/backups/<save>-format<N>-<date>/` and put back if a migration fails. Migrations read and write the formats they convert between with their own code, so they keep working as the game changes. Saves of a newer game, older than `SaveMigrations.OldestSupported` or with no migration path stay in the list with why they can't be played. When a save's format changes (world.dat, chunk records, players.db), bump `FormatVersion` and add a migration from the previous one.

Regions are streamed with the world: the changes of a region are read when the server loads its first chunk, and saved and dropped from memory when it unloads its last one, so memory only holds the areas around players.
- `players.db`: the players database described below.

Changed chunks are written on a background thread every 30 seconds (`autosaveInterval`), when their region unloads and when the server stops, all through one ordered queue so a chunk is never overwritten by an older version. The test scene isn't saved.

The server keeps a SQLite database (`players.db`, via [unity-sqlite-net](https://github.com/gilzoide/unity-sqlite-net)) with:

- `players`: everyone that joined, with their hashed machine id, role (`user` or `admin`), experience, equipment (JSON, `{"head": {"id": "...", "count": 1, "components": [...]}, "chest": ..., "legs": ..., "feet": ...}`, null when nothing is worn), number of joins, play time, and where they were (absolute position and facing) when they left, saved on disconnect and with every autosave. Players come back there next time, the world streams in around that spot.
- `player_sessions`: every join, with its address and when the player joined and left.
- `inventory_items`: each player's inventory. Inventories only change on the server and are synced to their owner; new players get the `starterItems` of the NetworkManager.
- `pets`: the pets following each player while they're away (kind, health, attack mode), saved when they leave and with every autosave.

To test with several instances, a build can be started with `-name <name>` and `-host` (`-world <save>` picks the world, `world` by default) or `-join <address>` (and `-password <password>`). Players are their computer, so each instance of the same computer joining the same server needs `-identity <tag>` too (any word, a different one each); only development builds read it. `-test` starts the test scene, which has no button in the menu, and `-profileLoad` logs how long loading the world takes, step by step (`WorldLoadProfiler`). Playing a game scene straight from the editor starts a singleplayer game.

Automated tests (Unity Test Framework) are in `Assets/Tests`: Window -> General -> Test Runner runs them, or `Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml`. The Edit Mode ones run without a game: fluids flow in a world of blocks in memory (`TestWorld`, through `IFluidWorld`), with the game's items and blocks, and who joins as whom is `JoinRules`. The Play Mode ones (`-testPlatform PlayMode`) play the game alone on a world of their own (`TestGame`, a save named `zz_test_...` deleted afterwards, drawn as near as the game allows so few chunks load), in about 15 seconds.

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
- `type`: `brick` (the default), `armor`, `weapon`, `food` or `material` (does nothing on its own: ingredients, arrows). Only bricks can be placed.
- `damage`: how much health a hit takes while holding it, 1 (a bare hand's) by default. `attackSpeed`: hits a second while holding it, 2.5 (a bare hand's) by default. Weapons hit from the hotbar with a left click, like a bare hand.
- `ammo` and `range`: weapons that shoot (the bow): the item each shot uses up (`"arrow"`) and how far a shot flies at most (aimed 45 degrees up), 60 meters by default. `drawTime`: seconds the attack button is held to draw them fully, 1 by default: letting go shoots, and shots drawn less fly slower and hurt less. Shots fly where the player looks, falling as they go, and hurt the first thing with health they hit; what they hit in the world they stick in, and players walking onto them get them back (they go after a minute). A raised shield makes them bounce off.
- `slot` and `armor`: armor: where it's worn, `head`, `chest`, `legs`, `feet` or `offhand` (the left hand, shields), and its armor points. Players drag armor into their equipment slots, beside crafting in the inventory; each point worn takes 4% of a hit (at most 80%, a hit always takes 1). Holding the place button (right click) with a shield worn (`offhand`) raises it, unless a brick is in hand: hits from the front do nothing but wear it, and the player can't attack meanwhile.
- `digSpeed`: tools that dig: how many times faster than a bare hand they dig blocks and bricks (`3` for the pickaxe). Each one dug wears them (see `durability`).
- `light`: it gives light, placed and in hand (the torch): `{ "range": 8, "intensity": 400, "color": [1, 0.72, 0.4] }`, how far it reaches (meters), how bright (lumens) and its colour (warm by default).
- `bucket`: buckets. An empty one has `"filled"`, the item it becomes when the place button (right click) is used on its liquid; a full one has `"liquid"`, the item of the block it pours (`"water"`) against what's looked at, and `"empty"`, what it becomes after.
- `iconTurn`: degrees its model is turned around its up axis for its generated icon, for models seen end-on otherwise (the pickaxe).
- `held`: weapons, tools and shields: how players hold their model (weapons and tools in the right hand, shields in the left), `{ "grip": [x, y, z], "rotation": [x, y, z], "scale": 0.5 }`: the point of the model in the hand, how it's turned (degrees; the hand looks along +Z, +Y up) and its size. Without it, it's held by its middle, fitted to 0.8 meters.
- `durability`: weapons, armor and digging tools: how many uses they last (hits given, shots, hits taken while worn, or blocks and bricks dug) before breaking, forever if missing. A bar on the icon shows what's left. Armor and items that wear don't stack.
- `maxStack`: how many fit in one inventory slot, 64 by default. Dragging a stack onto the same item merges them as far as that allows.
- `brickModel`: the brick it places (other items: how it looks, dropped and in its icon): a part number of the game's models (`"3003"`, see How can i add a new model) or a model of the mod (see Mods).
- `bricklinkId`: BrickLink's part number of its model when it isn't the model's id, like `"4738a"` for the chest (model `4738v4`): parts lists use it (see Parts lists).
- `ldrawId`: LDraw's part number of its model when it isn't the model's id (without `uv`): parts lists' LDraw models use it.
- `ldr`: `true` when its folder has a `model.ldr` with the real LEGO parts it's made of (its model isn't one: a creation saved by the Parts Lister...): parts lists count those parts instead, and LDraw models put them where it is. The file's origin is its brick's pivot (the middle of its footprint, on its bottom), with LDraw's axes; parts of colour 16 take the brick's colour.
- `color`: its colour, a colour id of the palette (like `4` for Red, see How can i add a new brick material). `colors`: other colours it can have, `"all"`, `"transparent"` (any transparent one, for glass: others fall back to its default) or a list of ids; only its default one if missing. Each inventory stack is of one colour, recipes take ingredients of any colour and make the default one. Blocks placed in another colour are drawn in it, see-through for transparent colours; blocks without textures (like `brick_2x2`) use their default colour. Items without an icon nor textures get a square of their colour.
- `material`: a special material instead of its colour's, from Canvas -> Game -> BrickMaterials (only `"Water"` for now). `layer` sets the Unity layer of placed bricks, like `"Water"`.
- `block`: for bricks that are also a world block (it's named like the item, and placing the brick exactly over a world block places the block):
  - `hardness`: seconds to dig it with bare hands. `breakable: false` makes it undiggable.
  - `replaceable`: bricks can be placed inside it (like water).
  - `transparent`: sky light goes through it (like leaves or water).
  - `fluid`: a liquid (like water and lava): it doesn't collide, players go in and swim (they sink unless jump is held, at the surface too; they move along where they look once their eyes are under, and climb out at an edge with jump; wading slows them) and drown with their eyes under it (15 seconds of air, shown above the hotbar, then 2 health a second; armor doesn't help), items on the ground float up to its surface, and NPCs that walk keep out of it.
  - `translucent`: drawn see-through (like water), with the terrain's translucent material (`Resources/Materials/Block_Translucent`, 60% opaque, its shader is `Resources/Shader/TerrainTranslucent`). What's behind it is drawn too, and its own sides only where they touch air.
  - `flow`: fluids that flow, like Minecraft's water: `{ "reach": 7, "seconds": 0.25 }`, how many blocks they spread on flat ground (1 to 7) and the seconds per block. Placed (a bucket), the block is a source: it flows down as far as it goes, and sideways a level weaker each block toward the nearest way down within 4 blocks (every way if there's none); without a source it dries up, and two sources beside a block on solid ground make it one. Its levels are blocks of their own, `[block]_level_1` to `_7` and `[block]_falling`, lower each level and sloped at their edges (LDraw's 3039 and 3045). `{ "of": "water" }` makes a block a source of another fluid (`still_water`, what lakes are made of). Water only flows where a block next to it changes, so lakes stay as they are until then. Flowing water pushes players, NPCs and items on the ground along (1.39 m/s, from where it's higher to where it's lower, down where it falls), and washes away bricks tagged `washable`: they drop, and the current carries them (`"burns": true` in `flow`, lava's, destroys them instead). `"hardens": { "with": "water", "source": "obsidian", "flowing": "cobblestone", "below": "stone" }` (lava's, which spreads 3 blocks every 1.5 seconds): where the `with` fluid touches it, its sources become `source` and what flows of it `flowing`, and the `with` fluid it flows down onto becomes `below`.
  - `drop`: id of the item given when dug, like `"stone"`. The item itself if missing.
  - `digSound`: the sound effect played while it's dug, a file of `Resources/Sound/Effects` without its extension (`"PickaxeDig"` for stone and ores, `"HitTree"` for wood). `"Dig"` if missing.
- `tags`: what it is to the game besides its type, like `["bed"]`: NPCs sleep in placed bricks of items tagged `bed` (see How can i add an NPC?), players sleep in them, bricks of items tagged `storage` keep items (see Storage), tools tagged `partslister` list the LEGO parts of a creation (see Parts lists), and flowing water washes away bricks tagged `washable` (the torch).
- `storageSlots`: items tagged `storage`: how many slots a placed one has, 1 to 54, 27 by default (the storage panel scrolls past 27). Lowering it, what a placed one kept past its slots falls out the next time it's opened.
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
    "maxDrop": 3,
    "behaviour": "aggressive",
    "sightRange": 16,
    "fieldOfView": 140,
    "hearingRange": 12,
    "memory": 8,
    "aggro": [
        { "sense": "sight", "range": 16, "fov": 140 },
        { "sense": "hearing", "range": 9 },
        { "sense": "distance", "range": 3 },
        { "sense": "attacked" }
    ],
    "targets": "players",
    "tameWith": "iron_ore",
    "attack": { "damage": 5, "range": 0.9, "cooldown": 1.8, "hitTime": 1.2, "knockback": 8 },
    "drops": [ { "item": "iron_ore", "count": 3, "chance": 1 } ],
    "spawn": { "on": ["stone", "gravel", "snow"], "weight": 1, "minGroup": 1, "maxGroup": 2, "minHeight": 0, "maxHeight": 255, "time": "any", "atDay": "stay" },
    "schedule": [
        { "time": "21:00-06:30", "activity": "sleep" },
        { "time": "06:30-21:00", "activity": "wander", "range": 16 }
    ],
    "animations": { "idle": "idle", "walk": "walk", "attack": "attack", "death": "death" }
}
```

- Distances are meters (a block is 0.8 wide and 0.48 high, a player 1.8 tall). `speed` is in meters per second, `stepHeight` how high it steps up without jumping, `maxDrop` how far down it walks off an edge.
- `moves`: `ground` (the default) walks and keeps out of fluids (blocks with `fluid`, and water bricks): it doesn't step onto them nor walk off an edge into them. `water`, `amphibious` and `air` (swimming and flying) come with the navigation grid; until then they walk like `ground` ones.
- What it notices (`Npcs.NpcPerception`): what it sees (within `sightRange` and `fieldOfView`, in degrees, and not hidden: lines of sight are cast in batches on worker threads), what it hears within `hearingRange` (players' footsteps, louder running; digging, placing, fights and doors, each carrying so far) and who hurts it. It remembers where each was for `memory` seconds.
- `aggro`: what makes it go for someone, any of these: seen (within `range`, in its `fov`), heard (within `range`), that close (`distance`, seen or not), or hurt by them (`attacked`). Without it, its `behaviour`'s: `passive` none (it runs from whoever hurts it), `neutral` (the default) `attacked`, `aggressive` all four (and only `attacked` for players in Peaceful worlds). `targets`: who it goes for, `players` (the default), `npcs` or `all` (anyone who hurts it, always).
- It goes for the nearest of them; one it loses from sight it looks for where they last were, then gives up (`Npcs.NpcBrain`).
- `tameWith`: the item a player holds to make it their pet with the use key (E): it follows them (catching up when left behind) and defends them. Using a pet again gives its orders: follow or stay, and what it attacks (nothing; who hurts its owner or whom they hit; hostile NPCs too; players too, with PvP). Nobody can hurt their own pets, nor others' without PvP. Pets following their owner leave with them and come back next to them when they join (saved in players.db); pets told to stay stay where they are.
- `attack`: `range` from its side, `cooldown` seconds between attacks, `hitTime` seconds into the attack animation when the hit lands (if the target is still in reach), `knockback` how hard it pushes.
- `drops`: items (and their `chance`, 0 to 1) that fall on the ground when a player kills it (pets drop nothing).
- `spawn`: where it appears by itself (`Npcs.NpcSpawner`): on which blocks (`on`, their ids; any solid one if empty), how likely next to the others that fit the same place (`weight`), how many together (`minGroup` to `maxGroup`, a pack) and between which heights in blocks (the sea is at 64). Every few seconds, around each player with fewer than 8 wild NPCs within 64 meters, a place on the surface 24 to 44 meters from every player is picked and one of the NPCs whose rules fit appears there with its group; never on fluids, and aggressive ones not in Peaceful worlds. `time`: when it appears (`"day"` 06:00 to 20:00, `"night"` 20:00 to 06:00, `"HH:MM-HH:MM"`, which can go past midnight; any time by default). `atDay`: what those that appeared by themselves do once it's no longer their time: `"stay"` (the default) or `"despawn"`: they go, a few at a time, when no player is within 32 meters (and they aren't fighting); pets and NPCs spawned by admins or scripts never do. Without `spawn` it only appears when spawned (`/spawnnpc`, scripts).
- `schedule`: what it does at each time of day when it has nobody to fight, run from or follow (`Npcs.NpcBrain`): the first entry whose `time` (written like `spawn`'s) it is. `activity` is `wander` (around, within `range` meters of its home if given), `stay` (at home), `sleep` or any other name, which its script handles (`npc.activity`, `onActivity`; the brain only stands it still). Its home is where it appeared. To sleep it claims the nearest free bed within 32 meters of home (a brick of an item tagged `bed`, see `Npcs.NpcBeds`) and lies on it, at the bed's `SleepPoint` (a child of the prefab: the sleeper's feet go there, its head the other way from where it faces); without a bed it lies down at home, and where it is if it can't get there. Lying, it plays its `sleep` animation, or lies on its back playing `idle` if its model has none. Asleep it doesn't see and hears a third as far: being hurt or a noise close by wakes it, and it goes back to sleep once nothing's going on. Home, bed and whether it appeared by itself are saved with it; pets have no schedule. The game's bed is `Items/bed` (model `Resources/BrickModels/bed`, from `Models/Items/Bed.fbx`).
- Mods' NPCs can have a `script.lua` with their events and orders (see [docs/lua/npcs.md](docs/lua/npcs.md)): `onThink` gets what they perceive and can take over their thinking.
- `animations`: the model's names for its idle, walk, attack and death animations, if they're named otherwise.

NPCs chase along paths of the navigation grid (`Npcs.Navigation.NavGrid`), on the server: the brick grid's cells (studs sideways, plates up) seen as columns, each with its spans, where there's solid ground with room above. Solid is the terrain's blocks (fluids and replaceable blocks aren't) and the cells bricks fill: their placement, or for bricks with other colliders (a door frame's opening, a door, open or closed) the cells their colliders touch. Tiles are chunks, built around NPCs on worker threads (Burst jobs) and built again when blocks, bricks or doors change there; paths (A* in a job) step up and drop down as far as the NPC can, with room for its whole body (its capsule's width and height), and are straightened. A path is asked for again as the target moves; one that can't get there takes the NPC as close as it can, and if that's no closer it gives up on the target for a while. Something the grid doesn't know of in the way (an NPC, a player) makes it ask for a new path or go sideways for a moment. `/navdebug` draws the floors around NPCs and their paths in the editor's scene view. They only move and think in chunks within 4 chunks of a player (`Npcs.NpcSystem.ActiveRadius`) that can be walked on, and players only get the NPCs of the chunks they have (`Network.ChunkInterestManagement`). NPCs are saved with the chunk they're in (its record in the region file: kind, position, health, a staying pet's owner and orders, its pack) when it's unloaded, when the world autosaves and when the server stops, and come back when it's loaded again; their packs come back together.

Its model (`Npcs.NpcModel`) is, the first found:

1. a `model.glb` or `model.gltf` in its folder: an animated glTF model (like Blockbench's exports), built when the game loads its NPCs (`Npcs.NpcModelBuilder`), the easy way for mods (`Mods/example/npcs/moss_golem`);
2. for a mod's NPC, the prefab named its `model` in one of the mod's AssetBundles (`npcs/*.bundle`, built with Unity, this game's version: prefabs made with the importer below, with no code of their own);
3. `model`, a prefab of `Assets/Resources/NpcModels`, made from an animated glTF model with `Brickcraft > Import NPC model (glTF)...` (like `Assets/Models/NPCs/Golem/Golem.gltf`, exported from [Blockbench](https://www.blockbench.net/)): its meshes, textures, materials, an AnimationClip per animation and an Animator controller with a state for each, saved next to the glTF.

Either way the model is built the same: a GameObject per node, textures unfiltered (pixel art), materials copied from `Resources/Materials/NpcModel` (or `NpcModelDoubleSided`) with the glTF's colour, texture and alpha cutoff. A node named `hitbox` isn't drawn: it's where the NPC can be hit and what it bumps into (all its meshes otherwise). Parts no animation moves are merged into one mesh per moving part. Models move their nodes (no skinned meshes); animations named `idle`, `walk`, `run`, `swim` and `fly` loop. Blockbench models face its north, so they're turned around (NPCs walk towards their +Z).

NPCs together (`Npcs.NpcSteering`, `Npcs.NpcGroups`): on their way, NPCs dodge each other and players, each taking a velocity close to the one it wants that doesn't run into anyone soon (sampled reciprocal velocity obstacles: two NPCs each move half out of the other's way), worked out for all of them at once on worker threads. Attacking someone, each takes its own place on a ring around them, the free one nearest where it comes from; only 3 hit at once (`NpcGroups.MaxAttackers`), the others wait their turn on a ring further out, facing them, and take a place as soon as one is free. NPCs spawned together (`/spawnnpc` with a count, or `npc:joinPack` from scripts) are a pack: with nothing to do they follow the leader (the first living one) in a wedge pointing where it walks, and when one goes for someone the others within 20 meters do too.

## LDraw parts

The game ships a pruned copy of the [LDraw](https://www.ldraw.org) parts library (CC BY 4.0, see `StreamingAssets/LDraw/CAreadme.txt`): `StreamingAssets/LDraw/library.zip` holds the catalogue's parts, the subparts and primitives they use (low resolution primitives where LDraw has them; LDraw's studs have no logos), and `catalog.json` lists the catalogue: every part but minifigs and other figures, stickers, printed parts, sheets, other systems than the bricks, subparts, primitives, aliases and moved or obsolete parts. Every part of the catalogue is a brick item, `part_[part number]` (like `part_3001`, "Brick 2 x 4"), of any colour, tagged `ldraw` and its LDraw category; their icons are in `icons.zip`, loaded when shown.

A model id that's an LDraw part number is read from the library when nothing else has it (see `Bricks.LDraw.LDrawModelBuilder`): its faces facing out (BFC), its own colours (a window's glass) kept and the brick's colour on the rest, in the game's units (a stud is 20 LDU), its pivot in the middle of its footprint, at its bottom. The game's own models (StreamingAssets/BrickModels, custom prefabs) come first.

To update the library: unpack library.ldraw.org's complete.zip somewhere, then `Brickcraft > LDraw > Build library from complete.zip folder...` and `Brickcraft > LDraw > Render missing catalogue icons` (it can be run again to go on, rendering takes a while).

## How can i add a new model?

Brick models (what items' `brickModel` names) are found by id when they're first needed, no list to keep: the game only lists the ids when it starts and reads a model the first time an item or a brick uses it, so the library can hold thousands of models while a world only loads what it uses (`Bricks.BrickModels`). An id is looked for in this order:

1. a mod's models (`Mods/[mod]/models/`, see Mods), named `[mod]:[name]`;
2. a custom prefab of the game, `Assets/Resources/BrickModels/[id].prefab`, for complex models;
3. a file model of the game, `StreamingAssets/BrickModels/[id]/`: tuned bricks, named by part number;
4. an LDraw part (see LDraw parts), by part number.

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
- `keepPivot`: `true` when the file's origin is already its pivot (the middle of its footprint, on its bottom): it isn't moved to the middle of its shape, which what sticks out of it would put off the grid. Creations' models have it.
- `parts`: materials that keep a colour of their own (a palette colour id, like 47 Trans-Clear for a window); the others take the brick's colour.
- `width`, `depth`, `plates`: its size, if the one worked out from its shape is wrong (footprint in studs, height in plates rounded down so studs on top don't count; under 3 plates it's a plate).

The shape is moved so its pivot is at the center of its footprint, on its bottom face. OBJ files without normals get them smoothed under 60°, glTF primitives without normals are flat (only geometry is read from glTF: no textures, Draco or meshopt).

A custom prefab is named after its id. Its size comes from the BoxCollider on its root, which fills its footprint and height (studs left out), or, without one, from the mesh on its root (like file models: a mesh collider then lets players through openings, like the door frame 60596). Its pivot is at the center of its footprint, on its bottom face. Its materials (children's too) are replaced by the brick's colour, except the parts a `BrickModelParts` component gives a colour of their own.

Bricks have a state (`Brick.state`, an int saved with them and sent to players, 0 when placed) that their prefab can show: components implementing `IBrickState` hear it when the brick appears and when it changes (`WorldNetwork.ServerSetBrickState`). `BrickDoor` is one: its hinge turns the door open (1) or closed (0), and a left click on a brick with one toggles it (`PlayerNetwork.CmdToggleDoor`); holding the click still breaks it. 

Some models are attachments (a `BrickAttachment` on the prefab's root): their bricks don't go on the grid but in the slot of another brick (a `BrickSlot`, with the point their pivot goes to), when both say the same `fits`. Holding one, the preview shows it in the slot of the brick you look at, and placing puts it there if the slot is free (`PlayerNetwork.CmdAttachBrick`). An attached brick (`Brick.attachedTo`, saved and sent like its state) takes no cells, moves with its brick and is removed on its own; removing its brick removes it too, and whoever removed them gets both. Mods' `world.spawn` can't spawn attachments.

Bricks fill the cells of their box, but models of LDraw parts and file models only fill the columns their shape covers (`Bricks.BrickFootprint`, worked out when they load): a corner brick leaves its notch free for another part, and has a box collider for each row of filled columns instead of one for its box. Prefab models fill their box.

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
- "The Moon" (https://skfb.ly/6s9oz) by SebastianSosnowski is licensed under Creative Commons Attribution (http://creativecommons.org/licenses/by/4.0/).
- "Sun" (https://skfb.ly/oVpEI) by Cybertron B-127 is licensed under Creative Commons Attribution (http://creativecommons.org/licenses/by/4.0/).
- Closing Glass Door by Kraftaggregat -- https://freesound.org/s/736558/ -- License: Creative Commons 0
- Chest open (Fantasy UI SFX Free) by heyheytheree -- https://freesound.org/s/872674/ -- License: Attribution 4.0
- Close chest sound effect by designerschoice -- https://freesound.org/s/806793/ -- License: Attribution 4.0
- A bucket of water with a rag by 16G_Panska_Veinlich_Jakub -- https://freesound.org/s/498572/ -- License: Creative Commons 0
- PloppyMix.wav by digifishmusic -- https://freesound.org/s/53763/ -- License: Attribution 4.0