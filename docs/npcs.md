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