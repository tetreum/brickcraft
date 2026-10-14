using System;
using UnityEngine;

namespace Brickcraft
{
    /// <summary>
    /// The color picker (items tagged "colorpicker", see Item.IsColorPicker): the place button opens its window (see
    /// UI.ColorPickerPanel), where a colour is picked for it; the dig button paints the brick looked at with it (see
    /// Player, PlayerNetwork.CmdPaintBrick). The colour is its stack's: the palette shows it. While the window is open, the
    /// dig button on a brick picks that brick's colour.
    /// </summary>
    public static class ColorPickerTool
    {
        /// <summary>Raised when the player uses the color picker in that slot: its window opens.</summary>
        public static event Action<int> WindowRequested;

        public static void RequestWindow(int slot) {
            WindowRequested?.Invoke(slot);
        }

        /// <summary>The color picker in the slot takes that colour (the server checks it's one).</summary>
        public static void Pick(int slot, int color) {
            if (Player.Instance != null) {
                Player.Instance.network.CmdSetToolColor(slot, color);
            }
        }

        // how far away a brick's colour can be picked from
        private const float PickRange = 64;

        /// <summary>
        /// The colour of the brick or terrain block under the pointer (a block's is its brick item's, the grass' green),
        /// BrickColor.None if there's neither.
        /// </summary>
        public static int ColorUnderPointer() {
            Camera eye = Camera.main;
            if (eye == null) {
                return Bricks.BrickColor.None;
            }
            Ray ray = eye.ScreenPointToRay(GameInput.PointerPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, PickRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) {
                return Bricks.BrickColor.None;
            }
            Brick brick = Server.findBrick(hit.collider);
            if (brick != null) {
                return brick.color;
            }
            if (World.WorldBehaviour.Instance == null || !hit.collider.name.StartsWith("ChunkSlice")) {
                return Bricks.BrickColor.None;
            }
            // slightly inside the face hit: its block (like Player's)
            Vector3Int block = Bricks.BrickGrid.CellToBlock(Bricks.BrickGrid.WorldToCell(hit.point - hit.normal * 0.01f));
            World.BlockDefinition definition = World.BlockDatabase.Get(World.WorldBehaviour.Instance.GetBlockType(block));
            return definition != null && definition.itemId != null && Server.items.TryGetValue(definition.itemId, out Item item)
                ? item.color : Bricks.BrickColor.None;
        }

        /// <summary>The colour the color picker in hand paints with, BrickColor.None if it isn't in hand.</summary>
        public static int HeldColor {
            get {
                UserItem held = Player.Instance != null ? Player.Instance.SelectedItem : null;
                return held != null && held.item.IsColorPicker ? held.color : Bricks.BrickColor.None;
            }
        }
    }
}
