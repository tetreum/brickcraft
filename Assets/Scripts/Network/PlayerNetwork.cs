using Brickcraft.Bricks;
using Brickcraft.World;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// Network side of a player. Players ask the server to change the world through commands,
    /// the server checks them, applies them through <see cref="WorldNetwork"/> and tells the
    /// player which items it used or got.
    ///
    /// Only the local player runs the first person controls; the others are just a body
    /// moved by their NetworkTransform.
    /// </summary>
    [RequireComponent(typeof(Player))]
    public class PlayerNetwork : NetworkBehaviour
    {
        // how far from the player the server accepts changes, a bit more than the player's reach
        private const float MaxReach = 8f;

        public override void OnStartClient() {
            if (!isLocalPlayer) {
                setupRemotePlayer();
            }
        }

        public override void OnStartLocalPlayer() {
            GetComponent<Player>().onStartLocalPlayer();
        }

        private void setupRemotePlayer() {
            GetComponent<Player>().enabled = false;
            GetComponent<FirstPersonController>().enabled = false;

            foreach (Camera cam in GetComponentsInChildren<Camera>(true)) {
                cam.enabled = false;
            }
            foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true)) {
                listener.enabled = false;
            }
            // only the local player swims
            foreach (TriggerDetector detector in GetComponentsInChildren<TriggerDetector>(true)) {
                detector.gameObject.SetActive(false);
            }

            // placeholder body, the local player doesn't see its own
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
        }

        // -------- client to server --------

        [Command]
        public void CmdPlaceBrick(int itemId, int health, Vector3Int origin, byte rotation) {
            if (!Server.items.TryGetValue(itemId, out Item item) || item.type != Item.Type.Brick) {
                return;
            }
            BrickPlacement placement = new BrickPlacement(item.brickModel, origin, rotation);

            if (!isInReach(placement.WorldBounds.center) || !BrickGrid.IsFree(placement) || BrickPlacer.overlapsPlayer(placement)) {
                return;
            }

            // a 2x2 brick that fits exactly in a world block becomes part of the world
            bool placedAsWorldBlock = item.blockType.HasValue
                && placement.MatchesWorldBlock
                && WorldNetwork.ServerSetBlock(BrickGrid.CellToBlock(placement.origin), item.blockType.Value);

            if (!placedAsWorldBlock) {
                WorldNetwork.ServerPlaceBrick(item, placement);
            }
            TargetItemUsed(itemId, health);
        }

        [Command]
        public void CmdRemoveBrick(string brickId) {
            if (!Server.bricks.TryGetValue(brickId, out Brick brick) || !isInReach(brick.placement.WorldBounds.center)) {
                return;
            }
            WorldNetwork.ServerRemoveBrick(brick);
            TargetItemReceived(brick.itemId);
        }

        [Command]
        public void CmdDigBlock(Vector3Int block) {
            if (WorldBehaviour.Instance == null) {
                return;
            }
            BlockType blockType = WorldBehaviour.Instance.GetBlockType(block);
            Vector3 blockCenter = BrickGrid.CellToWorld(BrickGrid.BlockToCell(block) + new Vector3(1, 1.5f, 1));

            if (!BlockDatabase.Get(blockType).isBreakable || !isInReach(blockCenter)) {
                return;
            }
            if (WorldNetwork.ServerSetBlock(block, BlockType.Air)) {
                Item item = Server.getItemForBlock(blockType);

                if (item != null) {
                    TargetItemReceived(item.id);
                }
            }
        }

        [Server]
        private bool isInReach(Vector3 position) {
            return Vector3.Distance(transform.position, position) <= MaxReach;
        }

        // -------- server to the player's client --------

        [TargetRpc]
        private void TargetItemUsed(int itemId, int health) {
            Player.Instance.removeItem(new UserItem() {
                id = itemId,
                health = health,
                quantity = 1
            });
            SoundManager.Instance.play(SoundManager.EFFECT_TAPPING);
        }

        [TargetRpc]
        private void TargetItemReceived(int itemId) {
            Player.Instance.addItem(new UserItem() {
                id = itemId,
                quantity = 1
            });
        }
    }
}
