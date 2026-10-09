[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/preview.gif)](https://github.com/tetreum/brickcraft/raw/main/Preview/preview.gif)

[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/world.gif)](https://github.com/tetreum/brickcraft/raw/main/Preview/world.gif)

[![Logo](https://github.com/tetreum/brickcraft/raw/main/Assets/Textures/Logo.png)](https://github.com/tetreum/brickcraft/raw/main/Assets/Textures/Logo.png)

# Brickcraft

WIP. Combining Lego like bricks + Minecraft buidling style in Unity.

[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/1.png)](https://github.com/tetreum/brickcraft/raw/main/Preview/1.png)

## Blog

[https://tetreum.github.io/brickcraft/](https://tetreum.github.io/brickcraft/)

## Controls

Every key is a button of the Input Manager (Edit → Project Settings → Input Manager), named in brackets, so it can be rebound. A button missing from the project falls back to the default key listed here (see `GameInput`).

- WASD - Move character (Horizontal, Vertical)
- Space - Jump (Jump)
- Left Shift (hold) - Sprint (Sprint)
- TAB (hold) - Player list (PlayerList)
- 1-9 - Select a fast inventory slot (Slot1 to Slot9)
- I - Open inventory (Inventory)
- Escape - Menu, or close the chat (Menu)
- Enter - Chat (Chat), Enter again to send
- Left click (hold) - Remove placed bricks and terrain blocks (Dig)
- Right click (having a block selected in inventory) - Adds a block (Place)
- Mouse wheel / R (having a block selected in inventory) - Rotates block (Rotate)
- Shift (hold, while placing) - Place on any stud instead of snapping to the terrain block grid (FreePlacement)

## Multiplayer

Multiplayer uses [Mirror](https://mirror-networking.gitbook.io/docs). Every game is networked:

- **Singleplayer** runs a host that doesn't listen for connections, so nobody else can join.
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
- `/additem ITEM [count] [NICK|ID]`: gives `count` (1 by default) of the item with id `ITEM` to a player, yourself by default. Offline players get them in their saved inventory. `/additem ITEM NICK` works too, but a number after the item is always the count, so give a player by id with `/additem ITEM count ID`.

Admins also get an ALL tab in the inventory: every item of the game, searchable by name or id, 30 per page. Clicking one adds it to their inventory.

`NICK|ID` is a player name (any case) or, if no name matches, a player id. Admins can't kick or ban themselves, other admins or the host.

The game version is Player Settings → Version (`GameVersion.Current`), shown in the main menu. Clients can only join servers running exactly the same version, otherwise the menu says which version the server runs, so bump it on every release that changes what goes over the network.

Players are identified by the name typed in the menu plus their machine id (`SystemInfo.deviceUniqueIdentifier`): the first machine that uses a name owns it on that server, so nobody else can play with it.

### Saves

Everything is saved in `<persistentDataPath>/saves/<save name>/` (`saveName` on the NetworkManager, `world` by default):

- `world.dat`: the world's seed, the generator version, when it was created and last saved, and which game versions created it and played it last.
- `regions/r.<x>.<z>.bcr`: what players changed (blocks and bricks), in region files of 32x32 chunks. The terrain itself is never saved, it's generated again from the seed and the changes are applied on top, so a save only grows with what players do, not with the size of the world. Each chunk record is compressed and can be rewritten alone; new versions are written before the old ones are released, so a crash can't leave half a chunk. Regions nobody changed have no file.

Regions are streamed with the world: the changes of a region are read when the server loads its first chunk, and saved and dropped from memory when it unloads its last one, so memory only holds the areas around players.
- `players.db`: the players database described below.

Changed chunks are written on a background thread every 30 seconds (`autosaveInterval`), when their region unloads and when the server stops, all through one ordered queue so a chunk is never overwritten by an older version. The test scene isn't saved.

The server keeps a SQLite database (`players.db`, via [unity-sqlite-net](https://github.com/gilzoide/unity-sqlite-net)) with:

- `players`: everyone that joined, with their hashed machine id, role (`user` or `admin`), experience, equipment (JSON, `{"head": {"id": "...", "count": 1, "components": [...]}, "chest": ..., "legs": ..., "feet": ...}`, null when nothing is worn), number of joins, play time, and where they were (absolute position and facing) when they left, saved on disconnect and with every autosave. Players come back there next time, the world streams in around that spot.
- `player_sessions`: every join, with its address and when the player joined and left.
- `inventory_items`: each player's inventory. Inventories only change on the server and are synced to their owner; new players get the `starterItems` of the NetworkManager.

To test with several instances, a build can be started with `-name <name>` and `-host` or `-join <address>`. Playing a game scene straight from the editor starts a singleplayer game.

## Code structure

The code is split in assemblies, so each part only sees what it needs:

- `Brickcraft.Events` (`Assets/Scripts/Events`): the events below. It depends on nothing else of the game.
- `Brickcraft` (`Assets/Scripts`): the game itself: world, bricks, player, network, database.
- `Brickcraft.UI` (`Assets/Scripts/UI`): menus and panels. It uses the game, but the game never calls the UI: it raises events the UI listens to, and the UI owns its own shortcuts (inventory, menu, chat, player list).

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


## How can i add a new block?

Each block has its own folder in `Assets/StreamingAssets/Blocks` (`<Game>_Data/StreamingAssets/Blocks` in builds), loaded when the game starts:

```
Blocks/marble/
    block.json      properties (required)
    texture.png     texture of every side
    top.png         optional, overrides texture.png on that side (also side.png, bottom.png)
    model.obj       optional terrain model, the default 2x2 brick otherwise
    collider.obj    optional collider, model.obj (or the default brick collider) otherwise
    icon.png        inventory icon of the block's item (its top or main texture if missing)
```

`block.json`:

```json
{
    "id": 200,
    "name": "marble",
    "hardness": 2.5,
    "breakable": true,
    "replaceable": false,
    "transparent": false,
    "dropItemId": 0,
    "item": {
        "id": 200,
        "name": "Marble 2x2",
        "brickModel": 3003,
        "material": "BrightBlue"
    }
}
```

- `id` (1-254) and `name` must be unique. A folder reusing an existing id overrides that block.
- `hardness`: seconds to dig it with bare hands. `breakable: false` makes it undiggable.
- `replaceable`: bricks can be placed inside it (like water).
- `transparent`: sky light goes through it (like leaves or water).
- `item`: optional item that places the block back. It needs an item id not used in Server.cs#items. Its icon is `icon.png`, which /Scenes/IconGenerator generates if missing; until then the game uses the block's `top.png` or `texture.png`.
- `dropItemId`: item given when dug. Defaults to the block's own item, `0` and no item means nothing.

Every field but `id` and `name` is optional.

Textures are square images (they're scaled to the biggest one). Models are OBJ files in game units: the pivot is at the center of the bottom face and a block spans 0.796 x 0.478 x 0.796 (the default model is in `StreamingAssets/Models`). Each triangle is drawn when the side of the block it's closest to is exposed to air; triangles facing the inside of the block are drawn with the bottom side. Far from the camera (`DetailRadius` chunks, on the World object), blocks are drawn with their collider shape instead, so detailed models only cost where they can be seen.

## How can i add a new model?

1. Brick models & their prefabs are stored in https://github.com/tetreum/brickcraft/tree/main/Assets/Models/Bricks
2. The icon is stored at https://github.com/tetreum/brickcraft/tree/main/Assets/Resources/Textures/Bricks
3. Prefab must be listed at Server -> prefabs scene object.
4. Model specs (footprint in studs and height in plates) must be added at Server.cs#setupBrickModels(). The model's pivot must be at the center of its footprint, on its bottom face, like the existing ones.
5. Items using it must be added at Server.cs#items var (https://github.com/tetreum/brickcraft/blob/main/Assets/Scripts/Server.cs#L12)
6. To generate it's icon, head to /Scenes/IconGenerator & simply hit Play. Items with missing icons will have their icon generated.

## How can i add a new brick material/texture?

1. They're stored in Assets/Materials/BrickColors/ (https://github.com/tetreum/brickcraft/tree/main/Assets/Materials/BrickColors)
2. List the new materials at Canvas (scene object) -> Game -> BrickMaterials var.

## Credits

- stone-SJH for fixing "Terrain mesh is randomly broken because of brick's top face" bug at https://github.com/tetreum/brickcraft/pull/2
- Tapping sound effect - https://freesound.org/people/rioforce/sounds/233654/
- Dig + remove block sound effect - https://freesound.org/people/Agaxly/sounds/213005/
- Brick models - https://www.mecabricks.com/
- Break texture - MooCwzRck - https://www.minecraftforum.net/forums/mapping-and-modding-java-edition/resource-packs/1223258-16x128x-1-4-5-compatible-okami-texture-pack?page=5
- World chunk system - Smjert - https://github.com/chraft/chunk-light-tester/
- Logo font/style - Sverdlychenko Studio - http://sverdlychenko.com/en/lego-font-design/