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
        /// <summary>brick, helmet, weapon or food.</summary>
        public string type = "brick";
        /// <summary>How many fit in one inventory slot.</summary>
        public int maxStack = Item.DefaultMaxStack;
        /// <summary>Bricks: the model (see Server.setupBrickModels) and material (see Game.brickMaterials).</summary>
        public int brickModel = 3003;
        public string material;
        /// <summary>Bricks: the Unity layer of the placed brick (see Game.Layers), like "Water".</summary>
        public string layer;
        /// <summary>Bricks that are also a world block: how the block behaves. Null for other items.</summary>
        public BlockInfo block;
        /// <summary>Ways to craft the item.</summary>
        public RecipeInfo[] recipes;
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
