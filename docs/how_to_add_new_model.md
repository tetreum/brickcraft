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