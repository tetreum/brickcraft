[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/preview.gif)](https://github.com/tetreum/brickcraft/raw/main/Preview/preview.gif)

[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/world.gif)](https://github.com/tetreum/brickcraft/raw/main/Preview/world.gif)

[![Logo](https://github.com/tetreum/brickcraft/raw/main/Assets/Textures/Logo.png)](https://github.com/tetreum/brickcraft/raw/main/Assets/Textures/Logo.png)

# Brickcraft

WIP. Combining Lego like bricks + Minecraft buidling style in Unity with the option to export the created models.

[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/1.png)](https://github.com/tetreum/brickcraft/raw/main/Preview/1.png)

## How can i help?

- Character model: It's poorly made, needs a rework to also have clothes and hair separated so clothing system can be added in the future. Since minifigures are patented, the model is an advanced iteration that has things like knees.
- Animal models: Rigged & animated brick made animal models are needed to fill the world with some life.
- Improve post processing visuals: Current visuals are clearly not toon.


## Multiplayer

Servers are configured in `server.json`, next to the game (next to `Mods/`: the project's folder in the editor, next to `[Game]_Data` in builds), read every time one starts and written with the defaults the first time:

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
- `maxRenderDistance`: the furthest players get the world, in chunks (6 to 96).


The first player to join a save without admins (the host, starting singleplayer or a server) becomes its admin; everyone else joins as a `user`. There are several commands available for actions like role changing, kicking, banning, etc.. Check `/help` in chat.

## Vehicles

Cars, boats and planes, built from bricks starting with a vehicle part (a wheel, a seat, a propeller, a wing; and technic axles, pins and holes), driven from a seat and wired with a screwdriver: see `docs/vehicles.md`. The main menu's Tutorials walk through building a first house (and sleeping in it), car, plane and boat, each in a flat world of its own.

## Documentation about modding, npcs and more

Check the docs/ directory.

## LDraw parts

The game ships a pruned copy of the [LDraw](https://www.ldraw.org) parts library (CC BY 4.0, see `StreamingAssets/LDraw/CAreadme.txt`): `StreamingAssets/LDraw/library.zip` holds the catalogue's parts, the subparts and primitives they use (low resolution primitives where LDraw has them; LDraw's studs have no logos), and `catalog.json` lists the catalogue: every part but minifigs and other figures, stickers, printed parts, sheets, other systems than the bricks, subparts, primitives, aliases and moved or obsolete parts. Every part of the catalogue is a brick item, `part_[part number]` (like `part_3001`, "Brick 2 x 4"), of any colour, tagged `ldraw` and its LDraw category; their icons are in `icons.zip`, loaded when shown.

A model id that's an LDraw part number is read from the library when nothing else has it (see `Bricks.LDraw.LDrawModelBuilder`): its faces facing out (BFC), its own colours (a window's glass) kept and the brick's colour on the rest, in the game's units (a stud is 20 LDU), its pivot in the middle of its footprint, at its bottom. The game's own models (StreamingAssets/BrickModels, custom prefabs) come first.

Where parts connect besides their studs (technic axles, pins and holes, bars) is `connectors.txt` (and where window frames take their glass, `glass.txt`), built from the [LDCad Shadow Library](https://github.com/RolandMelkert/LDCadShadowLibrary) with `Brickcraft > LDraw > Build connectors from LDCad shadow library folder...` (after the library, to rebuild it).

To update the library: unpack library.ldraw.org's complete.zip somewhere, then `Brickcraft > LDraw > Build library from complete.zip folder...` and `Brickcraft > LDraw > Render missing catalogue icons` (it can be run again to go on, rendering takes a while).


## Credits

- Lua interpreter - MoonSharp, Marco Mastropaolo (BSD 3-Clause, see Assets/Plugins/MoonSharp/LICENSE.txt) - https://www.moonsharp.org/
- stone-SJH for fixing "Terrain mesh is randomly broken because of brick's top face" bug at https://github.com/tetreum/brickcraft/pull/2
- World chunk system - Smjert - https://github.com/chraft/chunk-light-tester/
- Most brick models - LDraw parts library, LDraw.org and its contributors (CC BY 4.0, see `StreamingAssets/LDraw/CAreadme.txt`) - https://library.ldraw.org/ - LDraw™ is a trademark owned and licensed by the Jessiman Estate (https://www.ldraw.org)
- Where LDraw parts connect (axles, pins, holes, glass grooves) - LDCad Shadow Library, Roland Melkert and contributors (CC BY-SA 4.0; `StreamingAssets/LDraw/connectors.txt` and `glass.txt` are under it too, see `connectors-LICENSE.md`) - https://github.com/RolandMelkert/LDCadShadowLibrary
- Some brick models - https://www.mecabricks.com/
- Tapping sound effect - https://freesound.org/people/rioforce/sounds/233654/
- Dig + remove block sound effect - https://freesound.org/people/Agaxly/sounds/213005/
- Break texture - MooCwzRck - https://www.minecraftforum.net/forums/mapping-and-modding-java-edition/resource-packs/1223258-16x128x-1-4-5-compatible-okami-texture-pack?page=5
- Test monster - iJUNE - https://sketchfab.com/3d-models/free-dummy-monster-246678f908b548feb0f4cccaeef78756#download
- Color list - https://rebrickable.com/downloads/
- "The Moon" (https://skfb.ly/6s9oz) by SebastianSosnowski is licensed under Creative Commons Attribution (http://creativecommons.org/licenses/by/4.0/).
- "Sun" (https://skfb.ly/oVpEI) by Cybertron B-127 is licensed under Creative Commons Attribution (http://creativecommons.org/licenses/by/4.0/).
- Closing Glass Door by Kraftaggregat -- https://freesound.org/s/736558/ -- License: Creative Commons 0
- Chest open (Fantasy UI SFX Free) by heyheytheree -- https://freesound.org/s/872674/ -- License: Attribution 4.0
- Close chest sound effect by designerschoice -- https://freesound.org/s/806793/ -- License: Attribution 4.0
- A bucket of water with a rag by 16G_Panska_Veinlich_Jakub -- https://freesound.org/s/498572/ -- License: Creative Commons 0
- PloppyMix.wav by digifishmusic -- https://freesound.org/s/53763/ -- License: Attribution 4.0
- 8-Bit Sunrise song by Brylie Christopher - https://freemusicarchive.org/music/Brylie_Christopher_Oxley/contact
- 8 Bit Ancient Ruins by Kirk Osamayo - https://freemusicarchive.org/music/kirk-osamayo/contact
- 8-Bit Kung Fu by John Bartmann - https://freemusicarchive.org/music/John_Bartmann/contact
- Fortsetzung Folgt by Gumbel - https://freemusicarchive.org/music/Gumbel/contact
- Killing Bosses by Gumbel - https://freemusicarchive.org/music/Gumbel/contact