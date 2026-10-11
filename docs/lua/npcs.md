# NPCs

Mods can add creatures: a folder in `npcs/` with an `info.json` (what it is, see the NPC section of the [readme](../../readme.md#how-can-i-add-an-npc)) and, optionally, a `script.lua` with its events. Without a script it does what its `behaviour` says; with one, the script hears what happens to it and can take over its thinking.

```
Mods/my_mod/
  npcs/wolf/info.json     the wolf (its id is "my_mod:wolf")
  npcs/wolf/script.lua    its events
```

NPCs run on the server, a few times a second each, and only near players. Use `:` to call their methods (`npc:attack(player)`).

## Fields

| Field | |
|---|---|
| `id` | Its network id, the same for as long as it exists. |
| `type` | Its kind, like `"golem"` or `"my_mod:wolf"`. |
| `name` | Its name. |
| `exists` | It's still in the world and alive. |
| `position` | The cell its feet are in, see [Positions](README.md#positions). `nil` once it's gone. |
| `health`, `maxHealth` | Its health now and when full. |
| `target` | Who it's after, a [player](players.md) or an NPC, `nil` if nobody. |
| `owner` | If it's a pet, its owner (a [player](players.md); `nil` while they're away). `nil` if it's wild. |

## Orders

What it does next. Its own brain gives it orders too, a few times a second: to be the only one giving them, return `false` from `onThink`.

| Method | |
|---|---|
| `npc:attack(who)` | Goes after a player or an NPC and hits it when it's in reach. |
| `npc:moveTo(position)` | Walks there (around what's in the way). |
| `npc:follow(who, distance)` | Walks after a player or an NPC, staying within `distance` meters (2 by default). |
| `npc:wander()` | Walks around, now and then. |
| `npc:stop()` | Stands still. |

## Other methods

| Method | |
|---|---|
| `npc:damage(amount)` | Hurts it, by nobody. Returns `false` if it couldn't be hurt (dead, hurt less than half a second ago, or its `onDamaged` said no). |
| `npc:heal(amount)` | Gives it health back, up to its maximum. |
| `npc:tame(player)` | Makes it the player's pet. |
| `npc:remove()` | Takes it out of the world; nobody gets its drops. Returns `false` if it was gone already. |

## Events

In the NPC's `script.lua`.

### onSpawn(npc)

It appeared in the world.

### onThink(npc, senses)

It's thinking (a few times a second). `senses` is what it perceives:

- `senses.seen`: the players and NPCs it sees (within its `sightRange` and `fieldOfView`, and not hidden behind something).
- `senses.heard`: the noises it heard since it last thought, each `{kind = "steps", position = {...}, source = player}`. Kinds: `steps`, `sprint`, `dig`, `place`, `fight`, `door`. `source` is who made it, if anyone.
- `senses.attackedBy`: who hurt it since it last thought, or `nil`.

**Return `false` and its own brain stays out of it**: the orders your script gives are all it does. Return anything else (or nothing) and it thinks as usual afterwards.

```lua
-- a coward: runs from whoever it sees
function onThink(npc, senses)
  local them = senses.seen[1]
  if them then
    local p, q = npc.position, them.position
    npc:moveTo({x = p.x + (p.x - q.x), y = p.y, z = p.z + (p.z - q.z)})
    return false
  end
end
```

### onDamaged(npc, amount, attacker)

Something is about to hurt it by `amount`; `attacker` is who (a player or an NPC), or `nil`. **Return `false` and it isn't hurt.**

### onDeath(npc, killer)

It died; `killer` is who killed it, or `nil`.

### onTargetLost(npc, target)

It gave up on who it was after: it lost them from sight for longer than it remembers, looked where they were and they weren't there, or it can't get to them.

### onInteract(npc, player)

A player used it (the "Use brick" key, E by default). **Return `false`** to keep the game from doing anything else (taming it, or a pet's orders).
