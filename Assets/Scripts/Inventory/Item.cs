using System.Collections.Generic;
using UnityEngine;
using Brickcraft.Bricks;
using Brickcraft.World;

namespace Brickcraft
{
    public class Item
    {
        /// <summary>What kind of item it is, the "type" of its info.json.</summary>
        public enum Type
        {
            Brick = 1,
            /// <summary>Worn in one of the equipment slots (see armorSlot), it takes some of the damage.</summary>
            Armor = 2,
            /// <summary>Hits harder or faster from the hotbar, or from afar with ammo (a bow).</summary>
            Weapon = 3,
            Food = 4,
            /// <summary>Does nothing on its own: crafting ingredients, ammo (arrows).</summary>
            Material = 5,
            /// <summary>Used on something with the place button instead of placed (the parts lister, see its tags).</summary>
            Tool = 6,
        }

        /// <summary>Where an armor item is worn, see Inventory's equipment slots.</summary>
        public enum ArmorSlot
        {
            None = 0,
            Head = 1,
            Chest = 2,
            Legs = 3,
            Feet = 4,
            /// <summary>The left hand: shields.</summary>
            Offhand = 5,
        }
        /// <summary>Its slug, see Slugs.</summary>
        public string id;
        /// <summary>Bricks: their model's id, see Bricks.BrickModels.</summary>
        public string brickModelId;
        /// <summary>A special material (see Game.brickMaterials) used instead of its colour's, like Water.</summary>
        public string materialName;
        public int layer;
        public Type type;
        public string name;

        /// <summary>Its name in the player's language (see Loc).</summary>
        public string DisplayName {
            get { return Names.Item(id, name); }
        }
        /// <summary>Its folder, StreamingAssets/Items/[item] (see ItemDatabase).</summary>
        public string folder;

        public const int DefaultMaxStack = 64;

        /// <summary>How many fit in one inventory slot.</summary>
        public int maxStack = DefaultMaxStack;

        /// <summary>Health a hit with nothing in hand takes.</summary>
        public const int HandDamage = 1;
        /// <summary>Health a hit while holding it takes.</summary>
        public int damage = HandDamage;

        /// <summary>Hits a second with nothing in hand.</summary>
        public const float HandAttackSpeed = 2.5f;
        /// <summary>Hits a second while holding it.</summary>
        public float attackSpeed = HandAttackSpeed;
        /// <summary>How far a hit reaches, in meters (ranged weapons shoot instead, see range).</summary>
        public const float HandReach = 5;
        /// <summary>Ranged weapons: the item each shot uses up (a bow's arrows), null for others.</summary>
        public string ammo;
        /// <summary>Ranged weapons: how far a shot flies at most (aimed 45 degrees up), in meters.</summary>
        public float range;

        public const float DefaultDrawTime = 1;
        /// <summary>Ranged weapons: seconds the attack button is held to draw it fully (see Player): less, the shot is weaker.</summary>
        public float drawTime = DefaultDrawTime;
        /// <summary>The weakest a shot can be, as a part of a full draw's power.</summary>
        public const float MinShotPower = 0.15f;

        /// <summary>Ranged weapons: how fast fully drawn shots leave it to fly as far as its range, meters per second.</summary>
        public float ShotSpeed {
            get { return Mathf.Sqrt(range * Network.Projectile.Gravity); }
        }

        /// <summary>Armor: where it's worn, None for other items.</summary>
        public ArmorSlot armorSlot;
        /// <summary>Armor: its armor points; each one takes 4% of the damage (see Combat.Armor).</summary>
        public int armor;
        /// <summary>
        /// Weapons and armor: how many uses it lasts (hits given, or taken while worn) before breaking, 0 for
        /// forever. How worn a stack is goes in its InventoryItem.health (uses taken).
        /// </summary>
        public int durability;
        /// <summary>Tools that dig: how many times faster than a bare hand, 1 for everything else (see ItemInfo.digSpeed).</summary>
        public float digSpeed = 1;
        /// <summary>The light it gives, held and placed, null for none (see ItemInfo.light).</summary>
        public LightInfo light;
        /// <summary>Buckets: what they pick up and pour, null for other items (see ItemInfo.bucket).</summary>
        public BucketInfo bucket;
        /// <summary>Food: the health it gives back when used up (see ItemInfo.heal), 0 for other items.</summary>
        public int heal;

        /// <summary>Food that heals: used (the place button), the player gets health back and one is gone.</summary>
        public bool Heals {
            get { return type == Type.Food && heal > 0; }
        }

        /// <summary>A tool that digs faster than a hand, worn by digging.</summary>
        public bool Digs {
            get { return digSpeed > 1; }
        }

        /// <summary>
        /// Weapons and shields: how a player holds its model (see HeldItems), in the hand's space (+Z where the
        /// player looks, +Y up): the point of the model in the hand, how it's turned, and its size (0: it's fitted).
        /// </summary>
        public Vector3 heldGrip;
        public Quaternion heldRotation = Quaternion.identity;
        public float heldScale;
        /// <summary>Whether its info.json says how it's held (heldGrip...), otherwise it's held by its middle.</summary>
        public bool hasHeld;

        /// <summary>Players hold it in their hand (right for weapons, left for shields), see HeldItems.</summary>
        public bool IsHeld {
            get { return type == Type.Weapon || armorSlot == ArmorSlot.Offhand; }
        }

        /// <summary>Worn in the left hand it blocks hits (see Network.PlayerNetwork.CmdBlock).</summary>
        public bool IsShield {
            get { return armorSlot == ArmorSlot.Offhand; }
        }

        /// <summary>Its info.json's BrickLink part number, null to use its model's id (see BricklinkPart).</summary>
        public string bricklinkId;

        /// <summary>Its info.json's LDraw part number, null to use its model's id (see LDrawPart).</summary>
        public string ldrawId;
        /// <summary>
        /// An LDraw part of the library shaped like its own (same size, same origin), to place it in LDraw models when
        /// the library hasn't its part (like 60623 for the door 7102), null for none.
        /// </summary>
        public string ldrawShape;
        /// <summary>Made of the real LEGO parts of the model.ldr in its folder (its info.json's "ldr": true), see Bricks.LDraw.LDrawContraption.</summary>
        public bool ldr;

        /// <summary>LDraw's part number of its model (LDraw models, see LDraw.LDrawWriter): its ldrawId, or its model's id without "uv".</summary>
        public string LDrawPart {
            get { return !string.IsNullOrEmpty(ldrawId) ? ldrawId : withoutUv(brickModelId); }
        }

        private static string withoutUv(string model) {
            model = model ?? "";
            return model.EndsWith("uv", System.StringComparison.OrdinalIgnoreCase) ? model.Substring(0, model.Length - 2) : model;
        }

        /// <summary>BrickLink's part number of its model: its bricklinkId, or its model's id without the "uv" (UV mapped) suffix of Mecabricks models.</summary>
        public string BricklinkPart {
            get {
                if (!string.IsNullOrEmpty(bricklinkId)) {
                    return bricklinkId;
                }
                string model = brickModelId ?? "";
                return model.EndsWith("uv", System.StringComparison.OrdinalIgnoreCase) ? model.Substring(0, model.Length - 2) : model;
            }
        }

        public bool IsRanged {
            get { return !string.IsNullOrEmpty(ammo); }
        }

        public bool Wears {
            get { return durability > 0; }
        }

        /// <summary>Its colour when it isn't given one (see BrickColorPalette), BrickColor.None if it has none.</summary>
        public int color = BrickColor.None;
        /// <summary>It can have any colour of the palette.</summary>
        public bool anyColor;
        /// <summary>Only transparent colours suit it (glass): others fall back to its default one.</summary>
        public bool transparentOnly;
        /// <summary>The colour its icon was rendered in, if not its default one (the LDraw catalogue's, see IconColor).</summary>
        public int iconColor = BrickColor.None;

        /// <summary>The colour its icon shows (see UI.ItemIconTint).</summary>
        public int IconColor {
            get { return iconColor != BrickColor.None ? iconColor : color; }
        }
        /// <summary>Other colours it can have, besides its default one.</summary>
        public HashSet<int> colors = new HashSet<int>();
        /// <summary>What it is to the game besides its type, from its info.json (lowercase), like "bed".</summary>
        public HashSet<string> tags = new HashSet<string>();

        public const string BedTag = "bed";
        /// <summary>Bricks flowing water breaks and carries away as items (a torch), see World.FluidSimulator.</summary>
        public const string WashableTag = "washable";
        /// <summary>Tools: used on a brick of the player's, it lists the LEGO parts of its creation (see Network.PartsLister).</summary>
        public const string PartsListerTag = "partslister";

        public bool IsPartsLister {
            get { return type == Type.Tool && tags.Contains(PartsListerTag); }
        }
        /// <summary>
        /// Tools: the place button picks a colour (the stack's), the dig button paints a placed brick with it (see
        /// PlayerNetwork.CmdPaintBrick).
        /// </summary>
        public const string ColorPickerTag = "colorpicker";

        public bool IsColorPicker {
            get { return type == Type.Tool && tags.Contains(ColorPickerTag); }
        }
        /// <summary>Tools: used, it places an LDraw model from the player's imports folder (see Network.ModelImporter).</summary>
        public const string ModelImporterTag = "modelimporter";

        public bool IsModelImporter {
            get { return type == Type.Tool && tags.Contains(ModelImporterTag); }
        }
        /// <summary>Placed, it keeps items (see Network.BrickStorage).</summary>
        public const string StorageTag = "storage";
        public const int DefaultStorageSlots = 27;
        /// <summary>The most a storage item can have (the storage panel scrolls past 27).</summary>
        public const int MaxStorageSlots = 54;

        /// <summary>Items tagged "storage": how many slots a placed one has.</summary>
        public int storageSlots = DefaultStorageSlots;

        public bool IsStorage {
            get { return tags.Contains(StorageTag); }
        }

        // World block this item turns into when placed exactly over a world block (2x2 bricks only)
        public BlockType? blockType;

        // its icon, loaded from iconFile (see ItemDatabase)
        public string iconFile;
        public Texture2D iconTexture;

        /// <summary>Loads its icon the first time it's shown (thousands of LDraw parts' would slow the start), null for none.</summary>
        public System.Func<Texture2D> iconLoader;

        public Texture2D icon {
            get {
                if (iconTexture == null && iconLoader != null) {
                    iconTexture = iconLoader();
                    iconLoader = null;
                }
                return iconTexture;
            }
        }
        public BrickModel brickModel {
            get {
                return BrickModels.Get(brickModelId);
            }
        }

        /// <summary>It can have colours other than its default one.</summary>
        public bool IsColorable {
            get { return anyColor || colors.Count > 0; }
        }

        public bool AllowsColor(int colorId) {
            if (colorId == color) {
                return true;
            }
            BrickColor brickColor = colorId == BrickColor.None ? null : BrickColorPalette.Get(colorId);
            if (brickColor == null || (transparentOnly && !brickColor.isTransparent)) {
                return false;
            }
            return anyColor || colors.Contains(colorId);
        }

        /// <summary>The colour, or the item's default one if it can't have it (a colour removed from the palette).</summary>
        public int ValidColor(int colorId) {
            return AllowsColor(colorId) ? colorId : color;
        }

        /// <summary>The material of its brick in that colour, null for the prefab's own.</summary>
        public Material MaterialFor(int colorId) {
            if (!string.IsNullOrEmpty(materialName)) {
                return Game.Instance.getBrickMaterial(materialName);
            }
            BrickColor brickColor = BrickColorPalette.Get(ValidColor(colorId));
            return brickColor != null ? brickColor.material : null;
        }
    }
}
