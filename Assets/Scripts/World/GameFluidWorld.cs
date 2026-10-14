using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Network;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// The game's world as fluids see it (see FluidSimulator): its blocks (WorldBehaviour), changed through the server
    /// (saved and sent to the clients, see WorldNetwork), and its bricks (BrickGrid). Bricks tagged "washable" are
    /// washed away: dropped, or burnt.
    /// </summary>
    public class GameFluidWorld : IFluidWorld
    {
        public BlockType GetBlockType(Vector3Int block) {
            WorldBehaviour world = WorldBehaviour.Instance;
            return world != null ? world.GetBlockType(block) : BlockType.NULL;
        }

        public void SetBlock(Vector3Int block, BlockType type) {
            WorldNetwork.ServerSetBlock(block, type, null);
        }

        public bool HasBricksThatStay(Vector3Int block) {
            foreach (Brick brick in bricksIn(block)) {
                if (!isWashable(brick)) {
                    return true;
                }
            }
            return false;
        }

        public void WashAway(Vector3Int block, bool burns) {
            foreach (Brick brick in bricksIn(block)) {
                if (!isWashable(brick)) {
                    continue;
                }
                Vector3 at = brick.WorldBounds.center;
                foreach (Brick removed in WorldNetwork.ServerRemoveBrick(brick)) {
                    if (!burns) {
                        DroppedItems.ServerDrop(removed.itemId, removed.color, 1, 0, at, Vector3.zero);
                    }
                }
            }
        }

        // tagged so, and nothing attached to it, nor it to something
        private static bool isWashable(Brick brick) {
            return brick.item != null && brick.item.tags.Contains(Item.WashableTag) && brick.attachedTo == null && Server.attachmentOf(brick) == null;
        }

        // the bricks with a cell in that block
        private static List<Brick> bricksIn(Vector3Int block) {
            List<Brick> found = new List<Brick>();
            Vector3Int min = BrickGrid.BlockToCell(block);
            Vector3Int max = BrickGrid.BlockToCell(block + Vector3Int.one);
            for (int x = min.x; x < max.x; x++) {
                for (int y = min.y; y < max.y; y++) {
                    for (int z = min.z; z < max.z; z++) {
                        Brick brick = BrickGrid.GetBrickAt(new Vector3Int(x, y, z));
                        if (brick != null && !found.Contains(brick)) {
                            found.Add(brick);
                        }
                    }
                }
            }
            return found;
        }
    }
}
