using System.Collections.Generic;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// Shape of an NPC's info.json (StreamingAssets/NPCs/[npc]/info.json, Mods/[mod]/npcs/[npc]/info.json,
    /// see NpcDatabase). Every field but name and model is optional. Distances are in meters (a stud is
    /// 0.4, a block 0.8 wide and 0.48 high, a player 1.8 tall).
    /// </summary>
    public class NpcInfo
    {
        public const string ModelsFolder = "NpcModels";

        /// <summary>Identifies it, see Slugs. The folder's name if empty.</summary>
        public string id;
        /// <summary>What players see (in the chat when it kills them...).</summary>
        public string name;
        /// <summary>Its model: Assets/Resources/NpcModels/[model].prefab (see NpcModel).</summary>
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

        public const string Ground = "ground";
        /// <summary>
        /// "neutral" (the default): wanders, and fights back who hurts it until it loses them.
        /// "aggressive": also goes for the nearest player it sees (not in Peaceful worlds).
        /// </summary>
        public string behaviour = "neutral";
        /// <summary>How far it notices players.</summary>
        public float sightRange = 16;
        public AttackInfo attack;
        /// <summary>What whoever kills it gets.</summary>
        public DropInfo[] drops;
        /// <summary>The model's animations for idle, walk, attack and death, if they're named otherwise.</summary>
        public Dictionary<string, string> animations;
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
