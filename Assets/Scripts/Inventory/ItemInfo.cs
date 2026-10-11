namespace Brickcraft
{
    /// <summary>
    /// Shape of an item's info.json (StreamingAssets/Items/[item]/info.json, see ItemDatabase).
    /// Every field but name is optional.
    /// </summary>
    public class ItemInfo
    {
        /// <summary>Identifies the item (in saves too), see Slugs. The folder's name if empty.</summary>
        public string id;
        /// <summary>What players see.</summary>
        public string name;
        /// <summary>brick, armor, weapon, food or material.</summary>
        public string type = "brick";
        /// <summary>How many fit in one inventory slot.</summary>
        public int maxStack = Item.DefaultMaxStack;
        /// <summary>Health a hit takes while holding it, Item.HandDamage (a bare hand's) if missing.</summary>
        public int? damage;
        /// <summary>Weapons: hits a second (Item.HandAttackSpeed, a bare hand's, if missing).</summary>
        public float? attackSpeed;
        /// <summary>Ranged weapons (with ammo): how far they shoot, in meters (30 if missing).</summary>
        public float? range;
        /// <summary>Weapons that shoot (a bow): the item id each shot uses up (its arrows).</summary>
        public string ammo;
        /// <summary>Armor: where it's worn: head, chest, legs, feet or offhand (the left hand, shields).</summary>
        public string slot;
        /// <summary>Armor: armor points, each takes 4% of the damage (at most 80%, see Combat.Armor).</summary>
        public int armor;
        /// <summary>Weapons and armor: how many uses they last before breaking (hits given, or hits taken while worn), 0 for forever.</summary>
        public int durability;
        /// <summary>Weapons and shields: how players hold them, held by their middle and fitted to 0.8 meters if missing.</summary>
        public HeldInfo held;
        /// <summary>Its model (what's placed, dropped, and shown in its icon), a part number like "3003" or one of the mod's models (see Bricks.BrickModels).</summary>
        public string brickModel = "3003";
        /// <summary>Its default colour, a colour id of BrickColorPalette (without one, its model's own materials).</summary>
        public int? color;
        /// <summary>Other colours it can have, "all" or a list of colour ids. Only their default one if missing.</summary>
        public Newtonsoft.Json.Linq.JToken colors;
        /// <summary>Bricks: a special material used instead of their colour's (see Game.brickMaterials), like "Water".</summary>
        public string material;
        /// <summary>Bricks: the Unity layer of the placed brick (see Game.Layers), like "Water".</summary>
        public string layer;
        /// <summary>Bricks that are also a world block: how the block behaves. Null for other items.</summary>
        public BlockInfo block;
        /// <summary>Ways to craft the item.</summary>
        public RecipeInfo[] recipes;
        /// <summary>What it is to the game besides its type, like "bed" (NPCs sleep in placed beds).</summary>
        public string[] tags;
        /// <summary>Items tagged "storage": how many slots a placed one has, 1 to 54 (27 by default).</summary>
        public int storageSlots = Item.DefaultStorageSlots;
    }

    /// <summary>How players hold an item, the "held" of its info.json, in its model's space (as placed, meters).</summary>
    public class HeldInfo
    {
        /// <summary>The point of the model in the hand, [x, y, z].</summary>
        public float[] grip;
        /// <summary>How it's turned in the hand (Euler angles, degrees), [x, y, z]: the hand looks along +Z, +Y up.</summary>
        public float[] rotation;
        /// <summary>Its size in the hand (1: as placed).</summary>
        public float scale = 1;
    }

    /// <summary>The world block of a brick item, the "block" of its info.json.</summary>
    public class BlockInfo
    {
        /// <summary>Seconds needed to dig it with bare hands.</summary>
        public float hardness = 1;
        public bool breakable = true;
        /// <summary>Bricks can be placed where it is (like water).</summary>
        public bool replaceable = false;
        /// <summary>Sky light goes through it (like leaves or water).</summary>
        public bool transparent = false;
        /// <summary>Drawn see-through (like water).</summary>
        public bool translucent = false;
        /// <summary>A liquid (water, lava): NPCs that walk keep out of it.</summary>
        public bool fluid = false;
        /// <summary>Id of the item given when dug, the item itself if empty.</summary>
        public string drop;
    }

    public class RecipeInfo
    {
        /// <summary>How many items it makes.</summary>
        public int quantity = 1;
        public IngredientInfo[] ingredients;
    }

    public class IngredientInfo
    {
        /// <summary>The ingredient's item id.</summary>
        public string id;
        public int quantity = 1;
        /// <summary>Its crafting slot, 1 to 4 (left to right, top to bottom).</summary>
        public int slot;
    }
}
