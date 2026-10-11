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
        /// <summary>How far a hit reaches with nothing in hand, in meters.</summary>
        public const float HandReach = 5;
        /// <summary>How far a hit reaches while holding it: more than a hand's for ranged weapons (a bow).</summary>
        public float reach = HandReach;
        /// <summary>Ranged weapons: the item each shot uses up (a bow's arrows), null for others.</summary>
        public string ammo;

        /// <summary>Armor: where it's worn, None for other items.</summary>
        public ArmorSlot armorSlot;
        /// <summary>Armor: its armor points; each one takes 4% of the damage (see Combat.Armor).</summary>
        public int armor;
        /// <summary>
        /// Weapons and armor: how many uses it lasts (hits given, or taken while worn) before breaking, 0 for
        /// forever. How worn a stack is goes in its InventoryItem.health (uses taken).
        /// </summary>
        public int durability;

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
        /// <summary>Other colours it can have, besides its default one.</summary>
        public HashSet<int> colors = new HashSet<int>();
        /// <summary>What it is to the game besides its type, from its info.json (lowercase), like "bed".</summary>
        public HashSet<string> tags = new HashSet<string>();

        public const string BedTag = "bed";
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

        public Texture2D icon {
            get { return iconTexture; }
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
            if (colorId == BrickColor.None || BrickColorPalette.Get(colorId) == null) {
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
