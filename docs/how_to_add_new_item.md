
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
- `heal`: food: the health it gives back (`10` for the HP Potion, a player has 20). Used with the place button while hurt, one is gone.
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