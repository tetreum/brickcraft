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

