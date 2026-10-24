# Towns

Worlds generated from a seed (not the flat tutorial ones) get towns: a few buildings standing together,
drawn from the LDraw models in `StreamingAssets/Buildings` (`.mpd`/`.ldr`, see `Towns.TownBuildings`). About
half of the world's regions have one — a region being `TownPlanner.SpanChunks` chunks square — kept to the
middle of their region; each town's buildings (up to `MaxBuildings`) and how far each is turned come from the
seed (`TownPlanner.Plan`), so every game, and every reload, sees the same town.

The server places them as chunks are generated (`Towns.TownPlacer`, called from `Network.WorldNetwork`): when
the last chunk a town stands on exists, it lays a small pad under each building — filling the ground up to its
floor with the surface's own block, so a building doesn't float on a slope — and places its bricks as the
world's own. They're saved with the chunk (see [Saves](saves.md)) and sent to the clients like a player's
bricks, but belong to no one (`Placer.playerId` 0), so the parts lister ignores them. A town is built once: a
reloaded one comes back from its save rather than being placed again.

A building is left out where its ground is too uneven (`TownPlacer.MaxSlope`), where the surface is water or
leaves, or where something grows in the space it would fill; parts the game has no item for are skipped by
`Bricks.LDraw.LDrawImport`, as anywhere else a model is read.

Only the server builds towns, and only for worlds generated from a seed: the flat tutorial worlds have none. The buildings and their layout are covered by `Assets/Tests/EditMode/TownTests.cs`, and building one in a running world by `TownsAreBuiltWhereTheSeedPutsThem` in `Assets/Tests/PlayMode/WorldTests.cs`.