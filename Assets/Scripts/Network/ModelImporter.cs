using System.Collections.Generic;
using System.IO;
using Brickcraft.Bricks;
using Brickcraft.Bricks.LDraw;
using Brickcraft.Combat;
using Brickcraft.Events;
using Brickcraft.World;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// A player's model importer (items tagged "modelimporter", see Item.IsModelImporter): used, it lists the LDraw
    /// models (.ldr, .mpd) in the imports folder of its game's data folder (next to the parts lists', see Folder),
    /// the player picks one and places its ghost on the map (see ModelImporterView), turning it as it likes. Its
    /// client reads the model (LDrawImport) and sends its bricks; the server places them all, or none if any can't
    /// go there (somewhere taken, not loaded, too far, in a player), as placed by the player.
    /// </summary>
    public class ModelImporter : NetworkBehaviour
    {
        /// <summary>The most bricks a model can have.</summary>
        public const int MaxBricks = 5000;
        /// <summary>How far from the player the model's middle can be.</summary>
        public const float MaxDistance = 48;
        private const int BricksPerMessage = 200;

        // server: the model's bricks received so far, and how many it has
        private readonly List<ImportedBrick> received = new List<ImportedBrick>();
        private int expected;

        private Health health;

        private void Awake() {
            health = GetComponent<Health>();
        }

        private int playerId {
            get { return connectionToClient != null && connectionToClient.authenticationData is ConnectedPlayer player ? player.record.Id : 0; }
        }

        // -------- client to server --------

        [Command]
        public void CmdBegin(int count) {
            received.Clear();
            expected = Mathf.Clamp(count, 0, MaxBricks);
        }

        [Command]
        public void CmdBricks(ImportedBrick[] bricks) {
            if (bricks == null || received.Count + bricks.Length > expected) {
                return;
            }
            received.AddRange(bricks);
        }

        /// <summary>Places the bricks received, holding the model importer in that slot.</summary>
        [Command]
        public void CmdPlace(int slot) {
            List<ImportedBrick> bricks = new List<ImportedBrick>(received);
            received.Clear();
            InventoryItem? held = GetComponent<PlayerInventory>().ServerGetSlot(slot);
            if (health.isDead || !held.HasValue || !Server.items.TryGetValue(held.Value.itemId, out Item importer) || !importer.IsModelImporter) {
                return;
            }
            if (bricks.Count == 0 || bricks.Count != expected) {
                TargetPlaced(0, GameText.Of("importer.incomplete"));
                return;
            }
            GameText refusal = check(bricks, out List<Item> items, out List<BrickPlacement> placements);
            if (!refusal.IsEmpty) {
                TargetPlaced(0, refusal);
                return;
            }
            // the bricks, then what hangs in their slots
            Placer placer = Placer.Now(playerId);
            Brick[] placed = new Brick[bricks.Count];
            for (int i = 0; i < bricks.Count; i++) {
                if (!bricks[i].IsAttachment) {
                    placed[i] = WorldNetwork.ServerPlaceBrick(items[i], items[i].ValidColor(bricks[i].color), placements[i], placer);
                }
            }
            int count = 0;
            for (int i = 0; i < bricks.Count; i++) {
                if (!bricks[i].IsAttachment) {
                    count++;
                    continue;
                }
                Brick holder = placed[bricks[i].attachedTo - 1];
                if (holder != null && PlayerNetwork.CanAttach(items[i].brickModel, holder)) {
                    int seat = Mathf.Clamp(bricks[i].seat, 0, Server.slotOf(holder).Seats - 1);
                    WorldNetwork.ServerAttachBrick(items[i], items[i].ValidColor(bricks[i].color), holder, placer, seat);
                    count++;
                }
            }
            TargetPlaced(count, new GameText());
        }

        // why they can't all be placed, empty if they can
        [Server]
        private GameText check(List<ImportedBrick> bricks, out List<Item> items, out List<BrickPlacement> placements) {
            items = new List<Item>(bricks.Count);
            placements = new List<BrickPlacement>(bricks.Count);
            HashSet<Vector3Int> cells = new HashSet<Vector3Int>();
            Bounds box = new Bounds();
            foreach (ImportedBrick brick in bricks) {
                if (brick.itemId == null || !Server.items.TryGetValue(brick.itemId, out Item item) || item.type != Item.Type.Brick
                        || item.brickModel == null || item.brickModel.IsAttachment != brick.IsAttachment) {
                    return GameText.Of("importer.bad_parts");
                }
                // attachments go in a brick of the model that comes before (checked once it's placed)
                if (brick.IsAttachment) {
                    if (brick.attachedTo > items.Count || bricks[brick.attachedTo - 1].IsAttachment) {
                        return GameText.Of("importer.bad_parts");
                    }
                    items.Add(item);
                    placements.Add(placements[brick.attachedTo - 1]);
                    continue;
                }
                BrickPlacement placement = new BrickPlacement(item.brickModel, brick.origin, brick.rotation);
                if (WorldBehaviour.Instance != null && !WorldBehaviour.Instance.IsGenerated(WorldChanges.ChunkOfBrick(brick.origin))) {
                    return GameText.Of("importer.not_loaded");
                }
                foreach (Vector3Int cell in placement.Cells) {
                    if (!cells.Add(cell) || !BrickGrid.IsCellFree(cell)) {
                        return GameText.Of("importer.blocked");
                    }
                }
                if (BrickPlacer.overlapsPlayer(placement)) {
                    return GameText.Of("importer.player_in_way");
                }
                if (placements.Count == 0) {
                    box = placement.WorldBounds;
                } else {
                    box.Encapsulate(placement.WorldBounds);
                }
                items.Add(item);
                placements.Add(placement);
            }
            if (Vector3.Distance(transform.position, box.center) > MaxDistance) {
                return GameText.Of("importer.too_far");
            }
            return new GameText();
        }

        // -------- server to client --------

        [TargetRpc]
        private void TargetPlaced(int count, GameText refusal) {
            Chat(refusal.IsEmpty ? Loc.Get(Loc.Game, "importer.placed", count) : refusal.Say());
        }

        // -------- client --------

        /// <summary>Client: places the model's bricks (where they go, see LDrawImport.Placed), holding the importer in that slot.</summary>
        public void Place(List<ImportedBrick> bricks, int slot) {
            CmdBegin(bricks.Count);
            for (int i = 0; i < bricks.Count; i += BricksPerMessage) {
                CmdBricks(bricks.GetRange(i, Mathf.Min(BricksPerMessage, bricks.Count - i)).ToArray());
            }
            CmdPlace(slot);
        }

        /// <summary>Folder of the models to import, in the game's data folder (next to the parts lists', see PartsLister.Folder).</summary>
        public static string Folder {
            get { return Path.GetFullPath(Path.Combine(Application.persistentDataPath, "imports")); }
        }

        /// <summary>The LDraw models in the folder (made if it isn't there), by name.</summary>
        public static List<string> Models() {
            List<string> models = new List<string>();
            try {
                Directory.CreateDirectory(Folder);
                foreach (string file in Directory.GetFiles(Folder)) {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension == ".ldr" || extension == ".mpd") {
                        models.Add(file);
                    }
                }
            } catch (IOException e) {
                Debug.LogWarning("Can't read " + Folder + ": " + e.Message);
            }
            models.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), System.StringComparison.OrdinalIgnoreCase));
            return models;
        }

        /// <summary>Client: a line in the chat, for this player only.</summary>
        public static void Chat(string text) {
            if (!string.IsNullOrEmpty(text)) {
                EventManager.ChatLineReceived.Raise(new ChatLineReceivedEvent() { text = text });
            }
        }
    }
}
