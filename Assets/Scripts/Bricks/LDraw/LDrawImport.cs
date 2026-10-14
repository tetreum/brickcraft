using System.Collections.Generic;
using System.IO;
using Brickcraft.Network;
using UnityEngine;

namespace Brickcraft.Bricks.LDraw
{
    /// <summary>A brick of an imported model: its item, colour and where it goes on the grid (see LDrawImport).</summary>
    public struct ImportedBrick
    {
        public string itemId;
        /// <summary>Its colour, see BrickColorPalette.</summary>
        public int color;
        /// <summary>Its placement's origin cell.</summary>
        public Vector3Int origin;
        /// <summary>Its placement's rotation, in quarter turns.</summary>
        public byte rotation;
        /// <summary>For attachments (doors, glass), 1 + the index of the brick whose slot it goes in; 0 for others.</summary>
        public int attachedTo;

        public bool IsAttachment {
            get { return attachedTo > 0; }
        }
    }

    /// <summary>
    /// An LDraw model (.ldr, or .mpd with submodels) as bricks of the game, for the model importer (see
    /// Network.ModelImporter): every part that is an item (the game's own made of it, or the catalogue's, see
    /// PartsList.ItemOfPart) and sits on the grid, the inverse of LDrawWriter (a stud is 20 LDU, a plate 8, Y down);
    /// attachments (doors, glass, a chest's lid) go in the slot of the brick of the model where they hang. Parts it
    /// can't place are counted, not placed: those that aren't items, attachments with no slot of the model where they
    /// are, those turned other than by quarter turns around the vertical or off the studs, and those where another of
    /// its bricks already is. Origins are cells from its box's lowest corner.
    /// </summary>
    public class LDrawImport
    {
        private static readonly Matrix4x4 Flip = Matrix4x4.Scale(new Vector3(1, -1, 1));
        private static readonly Vector3 ToLDU = new Vector3(20 / Server.studSize, 8 / Server.plateHeight, 20 / Server.studSize);
        // how far from a cell a part can be and still go there (in cells): files round their numbers
        private const float Tolerance = 0.2f;

        public readonly List<ImportedBrick> bricks = new List<ImportedBrick>();
        /// <summary>Its box, in cells (unturned).</summary>
        public Vector3Int Size { get; private set; }

        /// <summary>Parts that aren't items of the game (their part numbers, each once).</summary>
        public readonly List<string> unknownParts = new List<string>();
        public int unknown, attachments, offGrid, overlapping;

        public int Skipped {
            get { return unknown + attachments + offGrid + overlapping; }
        }

        public static LDrawImport Load(string path) {
            return Read(File.ReadAllLines(path));
        }

        public static LDrawImport Read(string[] lines) {
            LDrawImport model = new LDrawImport();
            model.ownItems = findOwnItems();
            HashSet<Vector3Int> taken = new HashSet<Vector3Int>();
            List<BrickPlacement> placements = new List<BrickPlacement>();
            List<(LDrawContraption.Part part, Item item)> hanging = new List<(LDrawContraption.Part, Item)>();
            foreach (LDrawContraption.Part part in LDrawContraption.Parse(lines)) {
                model.add(part, taken, placements, hanging);
            }
            // once the bricks are there, what hangs from them
            foreach ((LDrawContraption.Part part, Item item) in hanging) {
                model.attach(part, item, placements);
            }
            model.moveToCorner(placements);
            return model;
        }

        // an old part number LDraw now has lettered versions of (3023 is 3023a and 3023b, the same shape): the first one's item
        private static Item renumbered(string part) {
            if (part.Length == 0 || !char.IsDigit(part[part.Length - 1])) {
                return null;
            }
            for (char version = 'a'; version <= 'e'; version++) {
                Item item = PartsList.ItemOfPart(part + version);
                if (item != null) {
                    return item;
                }
            }
            return null;
        }

        // the game's bricks made of an LDraw part (see findOwnItems), by part number
        private Dictionary<string, Item> ownItems;

        // the game's own bricks that are an LDraw part (their ldrawId: the chest, the door 7102...): they're more than
        // the catalogue's brick of it (a chest keeps items), and its model is theirs
        private static Dictionary<string, Item> findOwnItems() {
            Dictionary<string, Item> items = new Dictionary<string, Item>();
            foreach (Item item in Server.items.Values) {
                if (item.type == Item.Type.Brick && !string.IsNullOrEmpty(item.ldrawId) && !items.ContainsKey(item.ldrawId)) {
                    items[item.ldrawId] = item;
                }
            }
            return items;
        }

        private void add(LDrawContraption.Part part, HashSet<Vector3Int> taken, List<BrickPlacement> placements, List<(LDrawContraption.Part, Item)> hanging) {
            Item item = ownItems.TryGetValue(part.part, out Item own) ? own : PartsList.ItemOfPart(part.part) ?? renumbered(part.part);
            BrickModel brickModel = item != null && item.type == Item.Type.Brick ? item.brickModel : null;
            if (brickModel == null) {
                unknown++;
                if (!unknownParts.Contains(part.part)) {
                    unknownParts.Add(part.part);
                }
                return;
            }
            if (brickModel.IsAttachment) {
                hanging.Add((part, item));
                return;
            }
            if (!TryPlace(part, item, brickModel, out BrickPlacement placement)) {
                offGrid++;
                return;
            }
            foreach (Vector3Int cell in placement.Cells) {
                if (taken.Contains(cell)) {
                    overlapping++;
                    return;
                }
            }
            foreach (Vector3Int cell in placement.Cells) {
                taken.Add(cell);
            }
            placements.Add(placement);
            bricks.Add(new ImportedBrick() { itemId = item.id, color = colorOf(part.color, item), rotation = (byte)placement.rotation });
        }

        // an attachment goes in the free slot that fits it where it hangs (its pivot: a door's hinge, see BrickSlot.point)
        private void attach(LDrawContraption.Part part, Item item, List<BrickPlacement> placements) {
            BrickModel brickModel = item.brickModel;
            Vector3 at = PivotOf(part, brickModel, item);
            int holder = -1;
            float nearest = MaxAttachDistance;
            for (int i = 0; i < bricks.Count; i++) {
                if (bricks[i].IsAttachment || isHolding(i)) {
                    continue;
                }
                BrickSlot slot = SlotOf(placements[i].model);
                if (slot == null || slot.fits != brickModel.attachesTo) {
                    continue;
                }
                float distance = Vector3.Distance(at, SlotPoint(placements[i], slot));
                if (distance < nearest) {
                    nearest = distance;
                    holder = i;
                }
            }
            if (holder < 0) {
                attachments++;
                return;
            }
            placements.Add(placements[holder]);
            bricks.Add(new ImportedBrick() { itemId = item.id, color = colorOf(part.color, item), rotation = (byte)placements[holder].rotation, attachedTo = holder + 1 });
        }

        // how far (game units) an attachment can be from a slot and still go in it: files round their numbers
        private const float MaxAttachDistance = 0.1f;

        private bool isHolding(int index) {
            foreach (ImportedBrick brick in bricks) {
                if (brick.attachedTo == index + 1) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>A model's slot (see BrickSlot), null if it has none.</summary>
        public static BrickSlot SlotOf(BrickModel model) {
            return model.prefab != null ? model.prefab.GetComponentInChildren<BrickSlot>(true) : null;
        }

        /// <summary>Where a slot's point is for a brick placed so (game units, from the cells' origin: see PivotOf).</summary>
        public static Vector3 SlotPoint(BrickPlacement placement, BrickSlot slot) {
            Vector3Int size = placement.Size;
            Vector3 pivot = Vector3.Scale((Vector3)placement.origin + new Vector3(size.x / 2f, 0, size.z / 2f), BrickGrid.CellSize);
            // the prefab's own rotation is in the offset, like when it's spawned (Server.createBrickObject)
            Vector3 offset = slot.point.position - placement.model.prefab.transform.position;
            return pivot + placement.Rotation * offset;
        }

        /// <summary>Where an LDraw part's brick pivot is (game units, from the model's origin): the inverse of LDrawWriter.Place.</summary>
        public static Vector3 PivotOf(LDrawContraption.Part part, BrickModel brickModel, Item item) {
            Matrix4x4 m = part.Matrix;
            m.SetColumn(3, new Vector4(0, 0, 0, 1));
            Matrix4x4 r = Flip * m * Flip;
            Vector3 origin = LDrawWriter.OriginOf(brickModel, item.LDrawPart, item.ldrawShape);
            Vector3 ldu = Flip.MultiplyPoint3x4(part.position) - r.MultiplyVector(origin);
            return new Vector3(ldu.x / ToLDU.x, ldu.y / ToLDU.y, ldu.z / ToLDU.z);
        }

        /// <summary>
        /// Where an LDraw part of that item goes on the grid, origin cells from the model's origin: false if it's
        /// turned other than by quarter turns around the vertical, or off the studs.
        /// </summary>
        public static bool TryPlace(LDrawContraption.Part part, Item item, BrickModel brickModel, out BrickPlacement placement) {
            placement = default(BrickPlacement);
            // the rotation with the game's axes (see LDrawWriter.Place): a quarter turn around Y, or it isn't on the grid
            Matrix4x4 m = part.Matrix;
            m.SetColumn(3, new Vector4(0, 0, 0, 1));
            Matrix4x4 r = Flip * m * Flip;
            if (Mathf.Abs(r.m11 - 1) > 0.01f || Mathf.Abs(r.m01) > 0.01f || Mathf.Abs(r.m10) > 0.01f || Mathf.Abs(r.m12) > 0.01f || Mathf.Abs(r.m21) > 0.01f) {
                return false;
            }
            // the turn where its front (local Z) goes; mirrored left to right (LDraw files often have bricks so, they're
            // symmetric) it's the same turn
            int turns = BrickGrid.Mod(Mathf.RoundToInt(Mathf.Atan2(r.m02, r.m22) * Mathf.Rad2Deg / 90), 4);
            Quaternion rotation = Quaternion.Euler(0, 90 * turns, 0);
            Matrix4x4 turn = Matrix4x4.Rotate(rotation);
            bool front = Mathf.Abs(r.m02 - turn.m02) < 0.01f && Mathf.Abs(r.m22 - turn.m22) < 0.01f;
            bool side = Mathf.Abs(Mathf.Abs(r.m00) - Mathf.Abs(turn.m00)) < 0.01f && Mathf.Abs(Mathf.Abs(r.m20) - Mathf.Abs(turn.m20)) < 0.01f
                && Mathf.Abs(r.m00 * turn.m20 - r.m20 * turn.m00) < 0.01f;
            if (!front || !side) {
                return false; // turned other than by quarter turns, or scaled
            }
            // mirrored, a part that doesn't fill its box (a corner brick) is another turn of it
            if (r.determinant < 0 && brickModel.footprint != null) {
                turns = mirroredTurn(r, brickModel, turns);
                rotation = Quaternion.Euler(0, 90 * turns, 0);
            }

            // its pivot (the middle of its footprint, at its bottom), from where its LDraw origin is
            Vector3 pivot = PivotOf(part, brickModel, item);
            Vector3Int size = BrickPlacement.SizeFor(brickModel, turns);
            Vector3 cell = new Vector3(pivot.x / Server.studSize - size.x / 2f, pivot.y / Server.plateHeight, pivot.z / Server.studSize - size.z / 2f);
            Vector3Int rounded = Vector3Int.RoundToInt(cell);
            // up and down: a quarter plate off goes to the nearest plate (files have whole sub-assemblies 2 LDU up),
            // half a plate off (a baseplate is that thick) in the plate below
            float up = cell.y - Mathf.Floor(cell.y);
            if (Mathf.Abs(up - 0.5f) < 0.2f) {
                rounded.y = Mathf.FloorToInt(cell.y);
            } else if (Mathf.Abs(cell.y - rounded.y) > 0.3f) {
                return false;
            }
            // sideways the studs have to line up
            if (Mathf.Abs(cell.x - rounded.x) > Tolerance || Mathf.Abs(cell.z - rounded.z) > Tolerance) {
                return false;
            }
            placement = new BrickPlacement(brickModel, rounded, turns);
            return true;
        }

        // the turn whose filled columns are where the mirrored part's are (that one if none is)
        private static int mirroredTurn(Matrix4x4 r, BrickModel brickModel, int fallback) {
            HashSet<Vector2Int> mirrored = new HashSet<Vector2Int>();
            for (int x = 0; x < brickModel.width; x++) {
                for (int z = 0; z < brickModel.depth; z++) {
                    if (brickModel.Fills(x, z)) {
                        // the column's middle from the pivot in half studs, where the part's matrix puts it
                        int px = 2 * x + 1 - brickModel.width, pz = 2 * z + 1 - brickModel.depth;
                        mirrored.Add(new Vector2Int(Mathf.RoundToInt(r.m00 * px + r.m02 * pz), Mathf.RoundToInt(r.m20 * px + r.m22 * pz)));
                    }
                }
            }
            for (int turn = 0; turn < 4; turn++) {
                BrickPlacement placement = new BrickPlacement(brickModel, Vector3Int.zero, turn);
                Vector3Int size = placement.Size;
                HashSet<Vector2Int> columns = new HashSet<Vector2Int>();
                for (int x = 0; x < size.x; x++) {
                    for (int z = 0; z < size.z; z++) {
                        if (placement.FillsColumn(x, z)) {
                            columns.Add(new Vector2Int(2 * x + 1 - size.x, 2 * z + 1 - size.z));
                        }
                    }
                }
                if (columns.SetEquals(mirrored)) {
                    return turn;
                }
            }
            return fallback;
        }

        // the palette's colour of an LDraw code (16 the item's own), one the item can have
        private static int colorOf(int ldrawColor, Item item) {
            BrickColor color = ldrawColor == LDrawModelBuilder.MainColor ? null : BrickColorPalette.FromLDraw(ldrawColor);
            return item.ValidColor(color != null ? color.id : item.color);
        }

        // origins from the box's lowest corner, and its size
        private void moveToCorner(List<BrickPlacement> placements) {
            if (placements.Count == 0) {
                return;
            }
            Vector3Int min = placements[0].origin, max = placements[0].origin + placements[0].Size;
            foreach (BrickPlacement placement in placements) {
                min = Vector3Int.Min(min, placement.origin);
                max = Vector3Int.Max(max, placement.origin + placement.Size);
            }
            for (int i = 0; i < bricks.Count; i++) {
                ImportedBrick brick = bricks[i];
                brick.origin = placements[i].origin - min;
                bricks[i] = brick;
            }
            Size = max - min;
        }

        /// <summary>Its box's size turned that many quarter turns.</summary>
        public Vector3Int SizeTurned(int turns) {
            return turns % 2 == 0 ? Size : new Vector3Int(Size.z, Size.y, Size.x);
        }

        /// <summary>
        /// Its bricks with the model turned that many quarter turns around the vertical (like a brick is, see
        /// BrickPlacement.Rotation) and its box's lowest corner at that cell.
        /// </summary>
        public List<ImportedBrick> Placed(Vector3Int corner, int turns) {
            turns = BrickGrid.Mod(turns, 4);
            List<ImportedBrick> placed = new List<ImportedBrick>(bricks.Count);
            foreach (ImportedBrick brick in bricks) {
                // attachments are where their brick is
                BrickModel brickModel = brick.IsAttachment ? Server.items[bricks[brick.attachedTo - 1].itemId].brickModel : Server.items[brick.itemId].brickModel;
                Vector3Int size = BrickPlacement.SizeFor(brickModel, brick.rotation);
                Vector3Int min = brick.origin, max = brick.origin + size;
                int width = Size.x, depth = Size.z;
                for (int turn = 0; turn < turns; turn++) {
                    // a quarter turn takes forward (+z) to the right (+x): (x, z) to (z, -x), back inside the box
                    Vector3Int turnedMin = new Vector3Int(min.z, min.y, width - max.x);
                    Vector3Int turnedMax = new Vector3Int(max.z, max.y, width - min.x);
                    min = turnedMin;
                    max = turnedMax;
                    int swap = width;
                    width = depth;
                    depth = swap;
                }
                placed.Add(new ImportedBrick() {
                    itemId = brick.itemId, color = brick.color, origin = corner + min, rotation = (byte)((brick.rotation + turns) % 4),
                    attachedTo = brick.attachedTo,
                });
            }
            return placed;
        }
    }
}
