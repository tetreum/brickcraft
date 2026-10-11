using System.Collections.Generic;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// Shape of an NPC's info.json (StreamingAssets/NPCs/[npc]/info.json, Mods/[mod]/npcs/[npc]/info.json,
    /// see NpcDatabase). Every field but name is optional. Distances are in meters (a stud is
    /// 0.4, a block 0.8 wide and 0.48 high, a player 1.8 tall).
    /// </summary>
    public class NpcInfo
    {
        public const string ModelsFolder = "NpcModels";

        /// <summary>Identifies it, see Slugs. The folder's name if empty.</summary>
        public string id;
        /// <summary>What players see (in the chat when it kills them...).</summary>
        public string name;
        /// <summary>
        /// Its model, when its folder has no model.glb or model.gltf: a prefab of its mod's AssetBundles or
        /// Assets/Resources/NpcModels/[model].prefab (see NpcDatabase, NpcModel).
        /// </summary>
        public string model;
        /// <summary>The model's size.</summary>
        public float scale = 1;
        public int health = 20;
        /// <summary>Walking speed, meters per second.</summary>
        public float speed = 2;
        /// <summary>How high it steps up without jumping (a block is 0.48).</summary>
        public float stepHeight = 0.5f;
        /// <summary>
        /// Where it moves: "ground" (the default) walks and keeps out of fluids (water, lava). Swimming
        /// ("water", "amphibious") and flying ("air") come with the navigation grid; until then they walk.
        /// </summary>
        public string moves = Ground;
        /// <summary>How far down it walks off an edge (paths don't drop further).</summary>
        public float maxDrop = 3;

        public const string Ground = "ground";
        /// <summary>
        /// What it does when nobody tells it otherwise (see NpcBrain), and its aggro rules if it has none:
        /// "passive" runs from who hurts it; "neutral" (the default) fights back who hurts it until it loses
        /// them; "aggressive" also goes for players it sees, hears or bumps into (not in Peaceful worlds).
        /// </summary>
        public string behaviour = Neutral;
        public const string Passive = "passive";
        public const string Neutral = "neutral";
        public const string Aggressive = "aggressive";
        /// <summary>How far it sees (and how far it keeps chasing who it saw, half as far again).</summary>
        public float sightRange = 16;
        /// <summary>How far ahead it sees, in degrees from side to side.</summary>
        public float fieldOfView = 140;
        /// <summary>How far it hears noises (footsteps, digging, fights...), as far as each noise carries.</summary>
        public float hearingRange = 12;
        /// <summary>Seconds it remembers a target it lost from sight before giving up on it (it looks where it last was).</summary>
        public float memory = 8;
        /// <summary>
        /// What makes it go for someone, any of them: {"sense": "sight", "range": 16, "fov": 140} (seen),
        /// "hearing" (heard), "distance" (that close, seen or not), "attacked" (hurt by them). Missing: its
        /// behaviour's (see NpcBrain.DefaultAggro).
        /// </summary>
        public AggroRule[] aggro;
        /// <summary>Who it goes for: "players" (the default), "npcs" or "all".</summary>
        public string targets = "players";
        /// <summary>The item a player holds to tame it with the use key (it becomes their pet), null if it can't be.</summary>
        public string tameWith;
        public AttackInfo attack;
        /// <summary>What whoever kills it gets.</summary>
        public DropInfo[] drops;
        /// <summary>The model's animations for idle, walk, attack and death, if they're named otherwise.</summary>
        public Dictionary<string, string> animations;
        /// <summary>Where it appears by itself around players (see NpcSpawner), null if it doesn't.</summary>
        public SpawnInfo spawn;
    }

    /// <summary>Where and how an NPC appears by itself, see NpcSpawner.</summary>
    public class SpawnInfo
    {
        /// <summary>The blocks it appears on (their ids, like "grass" or "my_mod:moss"); any solid one if empty.</summary>
        public string[] on;
        /// <summary>How likely it is next to the others that could appear on the same place.</summary>
        public float weight = 1;
        /// <summary>How many appear together (they're a pack), at least and at most.</summary>
        public int minGroup = 1;
        public int maxGroup = 1;
        /// <summary>Between which heights, in blocks (the sea is at 64).</summary>
        public int minHeight = 0;
        public int maxHeight = 255;
    }

    public class AggroRule
    {
        public string sense;
        public float range;
        /// <summary>Sight only: degrees from side to side (its fieldOfView if 0).</summary>
        public float fov;

        public const string Sight = "sight";
        public const string Hearing = "hearing";
        public const string Distance = "distance";
        public const string Attacked = "attacked";
    }

    public class AttackInfo
    {
        public int damage = 2;
        /// <summary>How far it reaches, from its side.</summary>
        public float range = 1;
        /// <summary>Seconds between attacks.</summary>
        public float cooldown = 1.5f;
        /// <summary>Seconds into the attack animation when the hit lands (and is checked).</summary>
        public float hitTime = 0.5f;
        /// <summary>How hard it pushes who it hits, meters per second.</summary>
        public float knockback = 6;
    }

    public class DropInfo
    {
        /// <summary>The item's id.</summary>
        public string item;
        public int count = 1;
        /// <summary>From 0 to 1.</summary>
        public float chance = 1;
    }
}
