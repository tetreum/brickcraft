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
        /// <summary>brick, armor, weapon, food, material or tool.</summary>
        public string type = "brick";
        /// <summary>How many fit in one inventory slot.</summary>
        public int maxStack = Item.DefaultMaxStack;
        /// <summary>Health a hit takes while holding it, Item.HandDamage (a bare hand's) if missing.</summary>
        public int? damage;
        /// <summary>Weapons: hits a second (Item.HandAttackSpeed, a bare hand's, if missing).</summary>
        public float? attackSpeed;
        /// <summary>Ranged weapons (with ammo): how far a shot flies at most (aimed 45 degrees up), in meters (60 if missing).</summary>
        public float? range;
        /// <summary>Weapons that shoot (a bow): the item id each shot uses up (its arrows).</summary>
        public string ammo;
        /// <summary>Weapons that shoot: seconds the attack button is held to draw them fully (1 if missing); shots drawn less fly slower and hurt less.</summary>
        public float? drawTime;
        /// <summary>Armor: where it's worn: head, chest, legs, feet or offhand (the left hand, shields).</summary>
        public string slot;
        /// <summary>Armor: armor points, each takes 4% of the damage (at most 80%, see Combat.Armor).</summary>
        public int armor;
        /// <summary>Weapons and armor: how many uses they last before breaking (hits given, or hits taken while worn), 0 for forever.</summary>
        public int durability;
        /// <summary>BrickLink's part number of its model, when it isn't the model's id (its "uv" suffix taken off), like "4738a" for the chest.</summary>
        public string bricklinkId;
        /// <summary>LDraw's part number of its model (its file without ".dat"), when it isn't the model's id (its "uv" suffix taken off).</summary>
        public string ldrawId;
        /// <summary>An LDraw part of the library shaped like its own (same size and origin), for LDraw models when the library hasn't its part.</summary>
        public string ldrawShape;
        /// <summary>
        /// Its folder has a model.ldr with the real LEGO parts it's made of (its model isn't one: a creation saved as a
        /// mod...): parts lists count those (see Bricks.LDraw.LDrawContraption).
        /// </summary>
        public bool ldr;
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
        /// <summary>
        /// Tools that dig: how many times faster than a bare hand (more than 1). Each block or brick dug with it wears
        /// it (see durability).
        /// </summary>
        public float? digSpeed;
        /// <summary>It gives light, held and placed (a torch).</summary>
        public LightInfo light;
        /// <summary>Buckets: what they pick up and pour (see BucketInfo).</summary>
        public BucketInfo bucket;
        /// <summary>Degrees its model is turned around its up axis for its icon (see BrickIconRenderer), for those seen end-on.</summary>
        public float iconTurn;
    }

    /// <summary>The light an item gives, the "light" of its info.json.</summary>
    public class LightInfo
    {
        /// <summary>How far it reaches, in meters.</summary>
        public float range = 8;
        /// <summary>How bright, in lumens (a candle is about 12, a light bulb 800).</summary>
        public float intensity = 400;
        /// <summary>Its colour, [r, g, b] from 0 to 1: warm by default.</summary>
        public float[] color;
    }

    /// <summary>
    /// A bucket, the "bucket" of its info.json: an empty one has "filled" (the item it becomes picking up the liquid
    /// of that item's "liquid"), a full one "liquid" (the item of the block it pours) and "empty" (what's left after).
    /// </summary>
    public class BucketInfo
    {
        public string filled;
        public string liquid;
        public string empty;
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
        /// <summary>The sound effect played while it's dug (Resources/Sound/Effects), like "PickaxeDig"; "Dig" if empty.</summary>
        public string digSound;
        /// <summary>Fluids: how they flow (see World.Fluid), still if missing.</summary>
        public FlowInfo flow;
    }

    /// <summary>How a fluid block flows, the "flow" of its block (see World.Fluid and World.FluidSimulator).</summary>
    public class FlowInfo
    {
        /// <summary>How many blocks it spreads on flat ground from a source, 1 to 7.</summary>
        public int reach = 7;
        /// <summary>Seconds it takes to spread one block.</summary>
        public float seconds = 0.25f;
        /// <summary>A block that is a source of another fluid (still_water of water): its name, the rest is that one's.</summary>
        public string of;
        /// <summary>What it becomes where it meets another fluid (lava and water), see HardenInfo.</summary>
        public HardenInfo hardens;
        /// <summary>What it washes away (bricks tagged "washable") burns: it's gone, not dropped (lava).</summary>
        public bool burns;
    }

    /// <summary>
    /// What a fluid becomes where another touches it (Minecraft's lava and water): its sources "source" (obsidian),
    /// what flows of it "flowing" (cobblestone), and the other fluid it flows down onto "below" (stone). Blocks' names.
    /// </summary>
    public class HardenInfo
    {
        /// <summary>The other fluid's block, like "water".</summary>
        public string with;
        public string source;
        public string flowing;
        public string below;
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
