using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Brickcraft.Bricks;
using Brickcraft.Combat;
using Brickcraft.Events;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// A player's parts lister (items tagged "partslister", see Item.IsPartsLister): used on a brick or block it
    /// placed, a box goes around its creation (PartsList.Creation) and it looks at it from a free camera, growing
    /// or shrinking the box's faces (see PartsListerView). Saving lists the real LEGO parts it placed in the box
    /// (PartsList.Count), which its client writes as a CSV file: its path is told in the chat (to it only) and copied.
    ///
    /// The server keeps the session: it checks the brick is the player's, that the box stays near the creation
    /// (MaxGrowth, so the camera, which stays near the box, can't go scouting) and counts the parts; it ends when
    /// the player saves, cancels, gets hurt or dies.
    /// </summary>
    public class PartsLister : NetworkBehaviour
    {
        /// <summary>How many cells each face can be pushed out past the creation's box.</summary>
        public const int MaxGrowth = 32;
        // how far from the player the brick it's used on can be (a bit more than its reach)
        private const float MaxDistance = 8;

        // server: the session, and the creation's box it started with (cells, max exclusive)
        private bool active;
        private Vector3Int startMin;
        private Vector3Int startMax;

        private Health health;

        private void Awake() {
            health = GetComponent<Health>();
        }

        public override void OnStartServer() {
            health.ServerDamaged += onServerDamaged;
        }

        public override void OnStopServer() {
            health.ServerDamaged -= onServerDamaged;
        }

        // hurt (or dead), it stops looking
        [Server]
        private void onServerDamaged(DamageInfo damage) {
            if (active) {
                active = false;
                TargetEnd("You were hurt: the parts lister closed");
            }
        }

        private int playerId {
            get { return connectionToClient != null && connectionToClient.authenticationData is ConnectedPlayer player ? player.record.Id : 0; }
        }

        // -------- client to server --------

        /// <summary>Uses the parts lister in the inventory slot on a brick: it has to be one the player placed.</summary>
        [Command]
        public void CmdStartOnBrick(int slot, string brickId) {
            if (!canStart(slot) || brickId == null || !Server.bricks.TryGetValue(brickId, out Brick brick)) {
                return;
            }
            // an attachment (a door) is its holder's place
            if (brick.attachedTo != null && Server.bricks.TryGetValue(brick.attachedTo, out Brick holder)) {
                brick = holder;
            }
            if (WorldNetwork.ServerPlacerOf(brick).playerId != playerId || playerId == 0) {
                tell("The parts lister only works on bricks you placed");
                return;
            }
            if (Vector3.Distance(transform.position, brick.placement.WorldBounds.center) > MaxDistance) {
                return;
            }
            PartsList.Creation(playerId, brick, out startMin, out startMax);
            start();
        }

        /// <summary>Uses it on a world block: one the player placed (a 2x2 brick put over a block).</summary>
        [Command]
        public void CmdStartOnBlock(int slot, Vector3Int block) {
            if (!canStart(slot)) {
                return;
            }
            if (!PartsList.IsPlacedBlock(playerId, block) || playerId == 0) {
                tell("The parts lister only works on bricks you placed");
                return;
            }
            Vector3 center = BrickGrid.CellToWorld(BrickGrid.BlockToCell(block), new Vector3(1, 1.5f, 1));
            if (Vector3.Distance(transform.position, center) > MaxDistance) {
                return;
            }
            PartsList.Creation(playerId, block, out startMin, out startMax);
            start();
        }

        // alive, holding a parts lister, not already looking
        [Server]
        private bool canStart(int slot) {
            InventoryItem? held = GetComponent<PlayerInventory>().ServerGetSlot(slot);
            return !active && !health.isDead && held.HasValue && Server.items.TryGetValue(held.Value.itemId, out Item item) && item.IsPartsLister;
        }

        [Server]
        private void start() {
            active = true;
            TargetStart(startMin, startMax);
        }

        /// <summary>Lists the parts it placed in the box (cells, max exclusive), which has to stay near its creation.</summary>
        [Command]
        public void CmdSave(Vector3Int min, Vector3Int max) {
            if (!active) {
                return;
            }
            active = false;
            Vector3Int lowest = startMin - Vector3Int.one * MaxGrowth;
            Vector3Int highest = startMax + Vector3Int.one * MaxGrowth;
            if (min.x >= max.x || min.y >= max.y || min.z >= max.z
                    || min.x < lowest.x || min.y < lowest.y || min.z < lowest.z || max.x > highest.x || max.y > highest.y || max.z > highest.z) {
                TargetEnd("That box is too far from your creation");
                return;
            }
            TargetList(PartsList.Count(playerId, min, max).ToArray());
        }

        [Command]
        public void CmdCancel() {
            active = false;
        }

        [Server]
        private void tell(string text) {
            connectionToClient.Send(new ChatMessage() { sender = ChatCommands.ServerName, text = text });
        }

        // -------- server to client --------

        [TargetRpc]
        private void TargetStart(Vector3Int min, Vector3Int max) {
            PartsListerView.Open(this, min, max);
        }

        [TargetRpc]
        private void TargetEnd(string reason) {
            if (PartsListerView.Current != null) {
                PartsListerView.Current.Close();
            }
            chat(reason);
        }

        // the parts: written as a CSV file, whose path is told and copied
        [TargetRpc]
        private void TargetList(PartsListRow[] rows) {
            if (PartsListerView.Current != null) {
                PartsListerView.Current.Close();
            }
            if (rows.Length == 0) {
                chat("There are no LEGO parts of yours in that box");
                return;
            }
            string path;
            try {
                path = WriteCsv(rows);
            } catch (Exception e) {
                chat("The parts list couldn't be saved: " + e.Message);
                return;
            }
            GUIUtility.systemCopyBuffer = path;
            int parts = 0;
            foreach (PartsListRow row in rows) {
                parts += row.count;
            }
            chat("Parts list saved (" + parts + " parts, copied to the clipboard): " + path);
        }

        // -------- client --------

        /// <summary>Client: the view saves the box (see PartsListerView).</summary>
        public void Save(Vector3Int min, Vector3Int max) {
            CmdSave(min, max);
        }

        public void Cancel() {
            CmdCancel();
        }

        /// <summary>Folder of the saved parts lists, in the game's data folder.</summary>
        public static string Folder {
            get { return Path.GetFullPath(Path.Combine(Application.persistentDataPath, "parts_lists")); }
        }

        /// <summary>Writes the parts as a CSV file (part, colour id and name, the item's name, count); returns its path.</summary>
        public static string WriteCsv(IList<PartsListRow> rows) {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, "parts_list_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv");
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("brickModel,colorId,colorName,name,count");
            foreach (PartsListRow row in rows) {
                BrickColor color = BrickColorPalette.Get(row.color);
                csv.Append(field(row.brickModel)).Append(',')
                    .Append(row.color == BrickColor.None ? "" : row.color.ToString()).Append(',')
                    .Append(field(color != null ? color.name : "")).Append(',')
                    .Append(field(row.name)).Append(',')
                    .Append(row.count).AppendLine();
            }
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
            return path;
        }

        // quoted when it has commas, quotes or line breaks
        private static string field(string value) {
            value = value ?? "";
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) {
                return value;
            }
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        // a chat line for this player only
        private static void chat(string text) {
            if (!string.IsNullOrEmpty(text)) {
                EventManager.ChatLineReceived.Raise(new ChatLineReceivedEvent() { text = text });
            }
        }
    }
}
