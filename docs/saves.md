### Saves

Every world is saved in its own folder, `<persistentDataPath>/saves/<save name>/`, named after the world (`SavedWorlds`). Hosting a game uses the `saveName` of the NetworkManager, `world` by default:

- `world.dat`: the world's name, seed, difficulty and generator version, when it was created and last played, which game versions created it and played it last, its mods (see Mods), PvP, its day length and the game's time (written with every autosave).
- `regions/r.<x>.<z>.bcr`: what players changed (blocks and bricks), in region files of 32x32 chunks. The terrain itself is never saved, it's generated again from the seed and the changes are applied on top, so a save only grows with what players do, not with the size of the world. Each chunk record is compressed and can be rewritten alone; new versions are written before the old ones are released, so a crash can't leave half a chunk. Regions nobody changed have no file. Placed blocks and bricks also store who placed them (their id in `players.db`) and when (unix seconds); dug blocks don't. Bricks store their colour, and blocks placed in a colour other than their item's default one store it too (they're drawn with it instead of their textures). The NPCs in a chunk are saved in its record too (never sent to clients).

Saves have a format number (`WorldStorage.FormatVersion`, written in `world.dat`) that covers all their files. Older saves are upgraded when they're played: each step is a migration in `Assets/Scripts/World/Persistence/Migrations` (`Migration3To4` upgrades format 3 to 4) and they're chained, so a save several formats old goes through each one. The save is backed up first in `<persistentDataPath>/backups/<save>-format<N>-<date>/` and put back if a migration fails. Migrations read and write the formats they convert between with their own code, so they keep working as the game changes. Saves of a newer game, older than `SaveMigrations.OldestSupported` or with no migration path stay in the list with why they can't be played. When a save's format changes (world.dat, chunk records, players.db), bump `FormatVersion` and add a migration from the previous one.

Regions are streamed with the world: the changes of a region are read when the server loads its first chunk, and saved and dropped from memory when it unloads its last one, so memory only holds the areas around players.
- `players.db`: the players database described below.

Changed chunks are written on a background thread every 30 seconds (`autosaveInterval`), when their region unloads and when the server stops, all through one ordered queue so a chunk is never overwritten by an older version. The test scene isn't saved.

The server keeps a SQLite database (`players.db`, via [unity-sqlite-net](https://github.com/gilzoide/unity-sqlite-net)) with:

- `players`: everyone that joined, with their hashed machine id, role (`user` or `admin`), experience, equipment (JSON, `{"head": {"id": "...", "count": 1, "components": [...]}, "chest": ..., "legs": ..., "feet": ...}`, null when nothing is worn), number of joins, play time, and where they were (absolute position and facing) and their health when they left, saved on disconnect and with every autosave. Players come back there next time with that health, the world streams in around that spot; those who left dead come back alive at their spawn point (their bed's, or the world's).
- `player_sessions`: every join, with its address and when the player joined and left.
- `inventory_items`: each player's inventory. Inventories only change on the server and are synced to their owner; new players get the `starterItems` of the NetworkManager.
- `pets`: the pets following each player while they're away (kind, health, attack mode), saved when they leave and with every autosave.

