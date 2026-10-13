using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Bricks.LDraw;
using Brickcraft.World;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>One line of a parts list: a real LEGO part (its model, a part number) in one colour, and how many.</summary>
    public struct PartsListRow
    {
        public string brickModel;
        /// <summary>Its colour (see BrickColorPalette), BrickColor.None if it has none.</summary>
        public int color;
        /// <summary>What the game calls it (the first item of that part and colour found).</summary>
        public string name;
        /// <summary>BrickLink's part number (see Item.BricklinkPart).</summary>
        public string bricklinkPart;
        public int count;
    }

    /// <summary>A part of a creation where it is (see CreationModel): its item, colour, and place from the box's corner.</summary>
    public struct CreationPart
    {
        public string itemId;
        public int color;
        /// <summary>Its pivot from the box's corner, in the game's units, and its rotation.</summary>
        public Vector3 position;
        public Quaternion rotation;
        /// <summary>The cells it fills from the box's corner (max exclusive); none for attachments (doors...), which hang.</summary>
        public Vector3Int cellsMin;
        public Vector3Int cellsMax;
    }

    /// <summary>
    /// The server's side of the parts lister (see PartsLister): a player's creation around one of its bricks, and
    /// the real LEGO parts it placed in a box (those it placed, or what items of several are made of, see
    /// LDrawContraption). Its parts are the bricks and world blocks it placed (2x2 bricks put
    /// over a block become one, part 3003), never generated terrain nor other players' bricks.
    /// </summary>
    public static class PartsList
    {
        /// <summary>The most a creation (and so the box) can be along each axis, in cells: what the search stops at.</summary>
        public const int MaxSize = 128;

        // the cells one of its parts fills: a brick's, or a world block's (2x3x2)
        private struct Part
        {
            public Brick brick;
            public Vector3Int block;
        }

        /// <summary>
        /// The box (cells, max exclusive) around the player's creation that touches that brick: the bricks and
        /// blocks it placed connected to it (face to face), up to MaxSize along each axis.
        /// </summary>
        public static void Creation(int playerId, Brick start, out Vector3Int min, out Vector3Int max) {
            creation(playerId, new Part() { brick = start }, out min, out max);
        }

        /// <summary>The same around a world block it placed.</summary>
        public static void Creation(int playerId, Vector3Int startBlock, out Vector3Int min, out Vector3Int max) {
            creation(playerId, new Part() { block = startBlock }, out min, out max);
        }

        /// <summary>Whether the player placed that world block (it's there and it was its).</summary>
        public static bool IsPlacedBlock(int playerId, Vector3Int block) {
            BlockType type = WorldBehaviour.Instance != null ? WorldBehaviour.Instance.GetBlockType(block) : BlockType.NULL;
            return type != BlockType.Air && type != BlockType.NULL && WorldNetwork.ServerPlacerOf(block).playerId == playerId;
        }

        private static void creation(int playerId, Part start, out Vector3Int min, out Vector3Int max) {
            HashSet<string> bricks = new HashSet<string>();
            HashSet<Vector3Int> blocks = new HashSet<Vector3Int>();
            Queue<Part> open = new Queue<Part>();
            open.Enqueue(start);
            if (start.brick != null) {
                bricks.Add(start.brick.id);
            } else {
                blocks.Add(start.block);
            }
            min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            Vector3Int limitMin = Vector3Int.zero, limitMax = Vector3Int.zero;
            bool first = true;

            while (open.Count > 0) {
                Part part = open.Dequeue();
                List<Vector3Int> cells = cellsOf(part);
                if (first) {
                    // the search stays within MaxSize around where it started
                    Vector3Int center = cells[0];
                    limitMin = center - Vector3Int.one * (MaxSize / 2);
                    limitMax = center + Vector3Int.one * (MaxSize / 2);
                    first = false;
                }
                foreach (Vector3Int cell in cells) {
                    if (!within(cell, limitMin, limitMax)) {
                        continue;
                    }
                    min = Vector3Int.Min(min, cell);
                    max = Vector3Int.Max(max, cell + Vector3Int.one);
                    foreach (Vector3Int side in Sides) {
                        Vector3Int next = cell + side;
                        if (!within(next, limitMin, limitMax)) {
                            continue;
                        }
                        Brick brick = BrickGrid.GetBrickAt(next);
                        if (brick != null) {
                            if (!bricks.Contains(brick.id) && WorldNetwork.ServerPlacerOf(brick).playerId == playerId) {
                                bricks.Add(brick.id);
                                open.Enqueue(new Part() { brick = brick });
                            }
                            continue;
                        }
                        Vector3Int block = BrickGrid.CellToBlock(next);
                        if (!blocks.Contains(block) && IsPlacedBlock(playerId, block)) {
                            blocks.Add(block);
                            open.Enqueue(new Part() { block = block });
                        }
                    }
                }
            }
        }

        private static readonly Vector3Int[] Sides = {
            Vector3Int.left, Vector3Int.right, Vector3Int.down, Vector3Int.up, new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1),
        };

        private static bool within(Vector3Int cell, Vector3Int min, Vector3Int max) {
            return cell.x >= min.x && cell.y >= min.y && cell.z >= min.z && cell.x < max.x && cell.y < max.y && cell.z < max.z;
        }

        private static List<Vector3Int> cellsOf(Part part) {
            List<Vector3Int> cells = new List<Vector3Int>();
            if (part.brick != null) {
                cells.AddRange(part.brick.placement.Cells);
                return cells;
            }
            Vector3Int corner = BrickGrid.BlockToCell(part.block);
            for (int x = 0; x < BrickGrid.StudsPerBlock; x++) {
                for (int y = 0; y < BrickGrid.PlatesPerBlock; y++) {
                    for (int z = 0; z < BrickGrid.StudsPerBlock; z++) {
                        cells.Add(corner + new Vector3Int(x, y, z));
                    }
                }
            }
            return cells;
        }

        /// <summary>
        /// The real LEGO parts the player placed in the box (cells, max exclusive): its bricks and world blocks whose
        /// middle is inside, with what's attached to them, of models that are part numbers (they start with a digit),
        /// by part and colour, sorted.
        /// </summary>
        public static List<PartsListRow> Count(int playerId, Vector3Int min, Vector3Int max) {
            Dictionary<string, PartsListRow> rows = new Dictionary<string, PartsListRow>();

            foreach (Brick brick in BrickGrid.BricksNear(min, max)) {
                Vector3 middle = (Vector3)brick.placement.origin + (Vector3)brick.placement.Size * 0.5f;
                if (!inside(middle, min, max)) {
                    continue;
                }
                if (WorldNetwork.ServerPlacerOf(brick).playerId == playerId) {
                    add(rows, brick.item, brick.color);
                }
                Brick attached = Server.attachmentOf(brick);
                if (attached != null && WorldNetwork.ServerPlacerOf(attached).playerId == playerId) {
                    add(rows, attached.item, attached.color);
                }
            }

            Vector3Int firstBlock = BrickGrid.CellToBlock(min);
            Vector3Int lastBlock = BrickGrid.CellToBlock(max - Vector3Int.one);
            Vector3 blockMiddle = new Vector3(BrickGrid.StudsPerBlock, BrickGrid.PlatesPerBlock, BrickGrid.StudsPerBlock) * 0.5f;
            for (int x = firstBlock.x; x <= lastBlock.x; x++) {
                for (int y = firstBlock.y; y <= lastBlock.y; y++) {
                    for (int z = firstBlock.z; z <= lastBlock.z; z++) {
                        Vector3Int block = new Vector3Int(x, y, z);
                        if (!inside((Vector3)BrickGrid.BlockToCell(block) + blockMiddle, min, max) || !IsPlacedBlock(playerId, block)) {
                            continue;
                        }
                        BlockDefinition definition = BlockDatabase.Get(WorldBehaviour.Instance.GetBlockType(block));
                        if (string.IsNullOrEmpty(definition.itemId) || !Server.items.TryGetValue(definition.itemId, out Item item)) {
                            continue;
                        }
                        int color = WorldBehaviour.Instance.GetBlockColor(x, y, z);
                        add(rows, item, color == BrickColor.None ? item.color : color);
                    }
                }
            }

            List<PartsListRow> list = new List<PartsListRow>(rows.Values);
            list.Sort((a, b) => a.brickModel != b.brickModel ? string.CompareOrdinal(a.brickModel, b.brickModel) : a.color.CompareTo(b.color));
            return list;
        }

        /// <summary>
        /// The same parts where they are, as an LDraw model whose origin is the box's corner (see LDrawWriter): a
        /// line each, attachments where they hang (open doors open), blocks as their 2x2 brick.
        /// </summary>
        public static List<LDrawPlacement> Placements(int playerId, Vector3Int min, Vector3Int max) {
            List<LDrawPlacement> placements = new List<LDrawPlacement>();
            Vector3 corner = BrickGrid.CellToWorld(min);

            foreach (Brick brick in BrickGrid.BricksNear(min, max)) {
                Vector3 middle = (Vector3)brick.placement.origin + (Vector3)brick.placement.Size * 0.5f;
                if (!inside(middle, min, max)) {
                    continue;
                }
                if (WorldNetwork.ServerPlacerOf(brick).playerId == playerId) {
                    place(placements, brick.item, brick.color, brick.placement.Position, brick.placement.Rotation, brick.placement.model, corner);
                }
                Brick attached = Server.attachmentOf(brick);
                if (attached != null && attached.gameObject != null && WorldNetwork.ServerPlacerOf(attached).playerId == playerId) {
                    Transform at = attached.gameObject.transform;
                    place(placements, attached.item, attached.color, at.position, at.rotation, attached.model, corner);
                }
            }

            Vector3Int firstBlock = BrickGrid.CellToBlock(min);
            Vector3Int lastBlock = BrickGrid.CellToBlock(max - Vector3Int.one);
            Vector3 blockMiddle = new Vector3(BrickGrid.StudsPerBlock, BrickGrid.PlatesPerBlock, BrickGrid.StudsPerBlock) * 0.5f;
            Vector3 brickTop = new Vector3(0, BrickGrid.PlatesPerBlock * 8, 0); // in LDU
            for (int x = firstBlock.x; x <= lastBlock.x; x++) {
                for (int y = firstBlock.y; y <= lastBlock.y; y++) {
                    for (int z = firstBlock.z; z <= lastBlock.z; z++) {
                        Vector3Int block = new Vector3Int(x, y, z);
                        Vector3Int cell = BrickGrid.BlockToCell(block);
                        if (!inside((Vector3)cell + blockMiddle, min, max) || !IsPlacedBlock(playerId, block)) {
                            continue;
                        }
                        BlockDefinition definition = BlockDatabase.Get(WorldBehaviour.Instance.GetBlockType(block));
                        if (string.IsNullOrEmpty(definition.itemId) || !Server.items.TryGetValue(definition.itemId, out Item item) || !isListed(item)) {
                            continue;
                        }
                        int color = WorldBehaviour.Instance.GetBlockColor(x, y, z);
                        Vector3 pivot = BrickGrid.CellToWorld(cell, new Vector3(BrickGrid.StudsPerBlock / 2f, 0, BrickGrid.StudsPerBlock / 2f));
                        placements.Add(LDrawWriter.Place(item.LDrawPart, color == BrickColor.None ? item.color : color, pivot, Quaternion.identity,
                            item.brickModel != null ? LDrawWriter.OriginOf(item.brickModel, item.LDrawPart, item.ldrawShape) : brickTop, corner));
                    }
                }
            }
            return placements;
        }

        /// <summary>
        /// Every part the player placed in the box (whatever it is, real LEGO part or not), where it is: what its
        /// creation's model is made of (see CreationModel). Blocks are their 2x2 brick.
        /// </summary>
        public static List<CreationPart> CreationParts(int playerId, Vector3Int min, Vector3Int max) {
            List<CreationPart> parts = new List<CreationPart>();
            Vector3 corner = BrickGrid.CellToWorld(min);
            foreach (Brick brick in BrickGrid.BricksNear(min, max)) {
                Vector3 middle = (Vector3)brick.placement.origin + (Vector3)brick.placement.Size * 0.5f;
                if (!inside(middle, min, max)) {
                    continue;
                }
                if (WorldNetwork.ServerPlacerOf(brick).playerId == playerId && brick.item != null) {
                    parts.Add(new CreationPart() {
                        itemId = brick.itemId, color = brick.color,
                        position = brick.placement.Position - corner, rotation = brick.placement.Rotation,
                        cellsMin = brick.placement.origin - min, cellsMax = brick.placement.origin + brick.placement.Size - min,
                    });
                }
                Brick attached = Server.attachmentOf(brick);
                if (attached != null && attached.gameObject != null && WorldNetwork.ServerPlacerOf(attached).playerId == playerId && attached.item != null) {
                    Transform at = attached.gameObject.transform;
                    parts.Add(new CreationPart() { itemId = attached.itemId, color = attached.color, position = at.position - corner, rotation = at.rotation });
                }
            }
            Vector3Int firstBlock = BrickGrid.CellToBlock(min);
            Vector3Int lastBlock = BrickGrid.CellToBlock(max - Vector3Int.one);
            Vector3 blockMiddle = new Vector3(BrickGrid.StudsPerBlock, BrickGrid.PlatesPerBlock, BrickGrid.StudsPerBlock) * 0.5f;
            Vector3Int blockSize = new Vector3Int(BrickGrid.StudsPerBlock, BrickGrid.PlatesPerBlock, BrickGrid.StudsPerBlock);
            for (int x = firstBlock.x; x <= lastBlock.x; x++) {
                for (int y = firstBlock.y; y <= lastBlock.y; y++) {
                    for (int z = firstBlock.z; z <= lastBlock.z; z++) {
                        Vector3Int block = new Vector3Int(x, y, z);
                        Vector3Int cell = BrickGrid.BlockToCell(block);
                        if (!inside((Vector3)cell + blockMiddle, min, max) || !IsPlacedBlock(playerId, block)) {
                            continue;
                        }
                        BlockDefinition definition = BlockDatabase.Get(WorldBehaviour.Instance.GetBlockType(block));
                        if (string.IsNullOrEmpty(definition.itemId) || !Server.items.TryGetValue(definition.itemId, out Item item)) {
                            continue;
                        }
                        int color = WorldBehaviour.Instance.GetBlockColor(x, y, z);
                        Vector3 pivot = BrickGrid.CellToWorld(cell, new Vector3(BrickGrid.StudsPerBlock / 2f, 0, BrickGrid.StudsPerBlock / 2f));
                        parts.Add(new CreationPart() {
                            itemId = item.id, color = color == BrickColor.None ? item.color : color,
                            position = pivot - corner, rotation = Quaternion.identity,
                            cellsMin = cell - min, cellsMax = cell + blockSize - min,
                        });
                    }
                }
            }
            return parts;
        }

        // a real LEGO part (see IsRealPart), by its LDraw number
        private static bool isListed(Item item) {
            return item != null && IsRealPart(item.brickModelId) && IsRealPart(item.LDrawPart);
        }

        // a brick in the LDraw model: its part, or the parts it's made of (see LDrawContraption)
        private static void place(List<LDrawPlacement> placements, Item item, int color, Vector3 position, Quaternion rotation, BrickModel model, Vector3 corner) {
            if (isListed(item)) {
                placements.Add(LDrawWriter.Place(item.LDrawPart, color, position, rotation, LDrawWriter.OriginOf(model, item.LDrawPart, item.ldrawShape), corner));
                return;
            }
            List<LDrawContraption.Part> parts = item != null && !IsRealPart(item.brickModelId) ? LDrawContraption.PartsOf(item) : null;
            if (parts == null) {
                return;
            }
            // its file's origin is the brick's pivot
            LDrawPlacement pivot = LDrawWriter.Place("", color, position, rotation, Vector3.zero, corner);
            Matrix4x4 at = new LDrawContraption.Part() { position = pivot.position, row0 = pivot.row0, row1 = pivot.row1, row2 = pivot.row2 }.Matrix;
            foreach (LDrawContraption.Part part in parts) {
                Matrix4x4 m = at * part.Matrix;
                placements.Add(new LDrawPlacement() {
                    part = part.part, color = paletteColor(part.color, color),
                    position = m.GetColumn(3), row0 = m.GetRow(0), row1 = m.GetRow(1), row2 = m.GetRow(2),
                });
            }
        }

        // a contraption's part's colour in the palette: 16 is the brick's
        private static int paletteColor(int ldrawColor, int brickColor) {
            if (ldrawColor == LDrawModelBuilder.MainColor) {
                return brickColor;
            }
            BrickColor color = BrickColorPalette.FromLDraw(ldrawColor);
            return color != null ? color.id : BrickColor.None;
        }

        private static bool inside(Vector3 point, Vector3Int min, Vector3Int max) {
            return point.x >= min.x && point.y >= min.y && point.z >= min.z && point.x < max.x && point.y < max.y && point.z < max.z;
        }

        /// <summary>Whether a model is a real LEGO part: its id is a part number (it starts with a digit, like 3003 or 4738v4).</summary>
        public static bool IsRealPart(string brickModel) {
            return !string.IsNullOrEmpty(brickModel) && char.IsDigit(brickModel[0]);
        }

        // a brick in the list: its part, or the parts it's made of (an item with "ldr": true, see LDrawContraption)
        private static void add(Dictionary<string, PartsListRow> rows, Item item, int color) {
            if (item == null) {
                return;
            }
            if (IsRealPart(item.brickModelId)) {
                add(rows, item.brickModelId, color, item.name, item.BricklinkPart);
                return;
            }
            List<LDrawContraption.Part> parts = LDrawContraption.PartsOf(item);
            if (parts == null) {
                return;
            }
            foreach (LDrawContraption.Part part in parts) {
                if (!IsRealPart(part.part)) {
                    continue;
                }
                // named like the game's item of that part, or LDraw's
                Item partItem = ItemOfPart(part.part);
                LDrawPart catalog = LDrawLibrary.CatalogPart(part.part);
                string name = partItem != null ? partItem.name : catalog != null ? catalog.name : part.part;
                add(rows, part.part, paletteColor(part.color, color), name, partItem != null ? partItem.BricklinkPart : part.part);
            }
        }

        /// <summary>The game's item of an LDraw part: its catalogue item, or one of the game's made of it (the door 7102), null if none.</summary>
        public static Item ItemOfPart(string part) {
            if (Server.items.TryGetValue(ItemDatabase.LDrawItemPrefix + part, out Item item)) {
                return item;
            }
            foreach (Item other in Server.items.Values) {
                if (IsRealPart(other.brickModelId) && other.LDrawPart == part) {
                    return other;
                }
            }
            return null;
        }

        private static void add(Dictionary<string, PartsListRow> rows, string brickModel, int color, string name, string bricklinkPart) {
            string key = brickModel + "/" + color;
            if (rows.TryGetValue(key, out PartsListRow row)) {
                row.count++;
                rows[key] = row;
            } else {
                rows[key] = new PartsListRow() { brickModel = brickModel, color = color, name = name, bricklinkPart = bricklinkPart, count = 1 };
            }
        }
    }
}
