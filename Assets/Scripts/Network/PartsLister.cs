using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Brickcraft.Bricks;
using Brickcraft.Bricks.LDraw;
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
    /// (PartsList.Count), which its client writes, under the name the player gives it, as a CSV file, a BrickLink
    /// wanted list (XML) and an LDraw model (.ldr, PartsList.Placements), in a folder of their own whose path is told
    /// in the chat (to it only) and copied.
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
                TargetEnd(GameText.Of("partslister.hurt"));
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
                tell("partslister.not_yours");
                return;
            }
            if (Vector3.Distance(transform.position, brick.WorldBounds.center) > MaxDistance) {
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
                tell("partslister.not_yours");
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
                TargetEnd(GameText.Of("partslister.too_far"));
                return;
            }
            // the LDraw model's parts first (in batches: big creations are bigger than a message), then the list
            List<LDrawPlacement> placements = PartsList.Placements(playerId, min, max);
            for (int i = 0; i < placements.Count; i += PlacementsPerMessage) {
                TargetPlacements(placements.GetRange(i, Mathf.Min(PlacementsPerMessage, placements.Count - i)).ToArray());
            }
            // and what the creation's model is made of
            List<CreationPart> parts = PartsList.CreationParts(playerId, min, max);
            for (int i = 0; i < parts.Count; i += PlacementsPerMessage) {
                TargetCreationParts(parts.GetRange(i, Mathf.Min(PlacementsPerMessage, parts.Count - i)).ToArray());
            }
            TargetList(PartsList.Count(playerId, min, max).ToArray());
        }

        private const int PlacementsPerMessage = 200;

        /// <summary>Client: a parts list was written (its files' folder), see TargetList.</summary>
        public static event Action<string> ListSaved;

        [Command]
        public void CmdCancel() {
            active = false;
        }

        [Server]
        private void tell(string key) {
            GameTexts.Send(connectionToClient, key);
        }

        // -------- server to client --------

        [TargetRpc]
        private void TargetStart(Vector3Int min, Vector3Int max) {
            PartsListerView.Open(this, min, max);
        }

        [TargetRpc]
        private void TargetEnd(GameText reason) {
            if (PartsListerView.Current != null) {
                PartsListerView.Current.Close();
            }
            chat(reason.Say());
        }

        // client: the LDraw model's parts received so far (see CmdSave), written with the list
        private readonly List<LDrawPlacement> placementsReceived = new List<LDrawPlacement>();

        [TargetRpc]
        private void TargetPlacements(LDrawPlacement[] placements) {
            placementsReceived.AddRange(placements);
        }

        // client: the creation's parts received so far, its model made with the list
        private readonly List<CreationPart> partsReceived = new List<CreationPart>();

        [TargetRpc]
        private void TargetCreationParts(CreationPart[] parts) {
            partsReceived.AddRange(parts);
        }

        // the parts: written as a CSV file, whose path is told and copied
        [TargetRpc]
        private void TargetList(PartsListRow[] rows) {
            List<LDrawPlacement> placements = new List<LDrawPlacement>(placementsReceived);
            placementsReceived.Clear();
            List<CreationPart> creation = new List<CreationPart>(partsReceived);
            partsReceived.Clear();
            if (PartsListerView.Current != null) {
                PartsListerView.Current.Close();
            }
            if (rows.Length == 0) {
                chat(Loc.Get(Loc.Game, "partslister.empty"));
                return;
            }
            string path;
            int colorless;
            int ldrawColorless;
            try {
                path = WriteCsv(rows, pendingName);
                colorless = WriteXml(rows, Path.ChangeExtension(path, ".xml"));
                ldrawColorless = LDrawWriter.Write(Path.ChangeExtension(path, ".ldr"), pendingName, placements);
            } catch (Exception e) {
                chat(Loc.Get(Loc.Game, "partslister.save_failed", e.Message));
                return;
            }
            string folder = Path.GetDirectoryName(path);
            GUIUtility.systemCopyBuffer = folder;
            int parts = 0;
            foreach (PartsListRow row in rows) {
                parts += row.count;
            }
            string file = Path.GetFileNameWithoutExtension(path);
            chat(Loc.Get(Loc.Game, "partslister.saved", parts, folder));
            ListSaved?.Invoke(folder);
            chat(Loc.Get(Loc.Game, "partslister.files", file,
                colorless > 0 ? " " + Loc.Get(Loc.Game, "partslister.bricklink_colorless", colorless) : "",
                ldrawColorless > 0 ? " " + Loc.Get(Loc.Game, "partslister.ldraw_colorless", ldrawColorless) : ""));

            // the creation as a model of its own, a mod ready to copy (it takes a moment: hidden faces are looked for)
            if (creation.Count > 0) {
                chat(Loc.Get(Loc.Game, "partslister.making_model"));
                StartCoroutine(Bricks.CreationModel.Make(creation, placements, pendingName, GetComponent<PlayerNetwork>().playerName, Path.Combine(folder, "mod"), result => chat(result)));
            }
        }

        // -------- client --------

        // client: the name the player gave the list it's waiting for
        private string pendingName;

        /// <summary>Client: the view saves the box (see PartsListerView), under that name (its files').</summary>
        public void Save(Vector3Int min, Vector3Int max, string name) {
            pendingName = name;
            placementsReceived.Clear();
            partsReceived.Clear();
            CmdSave(min, max);
        }

        public void Cancel() {
            CmdCancel();
        }

        /// <summary>Folder of the saved parts lists, in the game's data folder.</summary>
        public static string Folder {
            get { return Path.GetFullPath(Path.Combine(Application.persistentDataPath, "parts_lists")); }
        }

        /// <summary>A file name for a parts list: the name the player gave it without what files can't have, its date if empty.</summary>
        public static string FileNameFor(string name) {
            StringBuilder clean = new StringBuilder();
            foreach (char c in (name ?? "").Trim()) {
                clean.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            }
            string file = clean.ToString().Trim().TrimEnd('.');
            if (file.Length > MaxNameLength) {
                file = file.Substring(0, MaxNameLength);
            }
            return file.Length > 0 ? file : "parts_list_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        }

        /// <summary>The longest a parts list's name can be.</summary>
        public const int MaxNameLength = 60;

        /// <summary>
        /// Writes the parts as a CSV file named after the list (part, colour id and name, the item's name, count), in
        /// a folder of its own with that name (where its other files go), not over another list of that name
        /// ("name (2)"...); returns its path.
        /// </summary>
        public static string WriteCsv(IList<PartsListRow> rows, string name) {
            string file = FileNameFor(name);
            string folder = Path.Combine(Folder, file);
            for (int copy = 2; Directory.Exists(folder) || File.Exists(folder); copy++) {
                folder = Path.Combine(Folder, file + " (" + copy + ")");
            }
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, Path.GetFileName(folder) + ".csv");
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

        /// <summary>
        /// Writes the parts as a BrickLink wanted list (XML, to upload on BrickLink): each part's BrickLink number,
        /// colour (BrickColor.bricklinkId) and count. Lines of colours BrickLink doesn't have go without one (any
        /// colour); returns how many.
        /// </summary>
        public static int WriteXml(IList<PartsListRow> rows, string path) {
            // BrickLink counts by its own part and colour: two of our parts can be one of its
            Dictionary<string, int> counts = new Dictionary<string, int>();
            List<string> order = new List<string>();
            int colorless = 0;
            foreach (PartsListRow row in rows) {
                BrickColor color = BrickColorPalette.Get(row.color);
                int bricklinkColor = color != null ? color.bricklinkId : BrickColor.None;
                if (bricklinkColor == BrickColor.None && row.color != BrickColor.None) {
                    colorless++;
                }
                string key = (string.IsNullOrEmpty(row.bricklinkPart) ? row.brickModel : row.bricklinkPart) + "\n" + bricklinkColor;
                if (!counts.ContainsKey(key)) {
                    order.Add(key);
                    counts[key] = 0;
                }
                counts[key] += row.count;
            }
            StringBuilder xml = new StringBuilder();
            xml.Append("<INVENTORY>\n");
            foreach (string key in order) {
                string[] parts = key.Split('\n');
                xml.Append("<ITEM>\n<ITEMTYPE>P</ITEMTYPE>\n");
                xml.Append("<ITEMID>").Append(System.Security.SecurityElement.Escape(parts[0])).Append("</ITEMID>\n");
                if (parts[1] != BrickColor.None.ToString()) {
                    xml.Append("<COLOR>").Append(parts[1]).Append("</COLOR>\n");
                }
                xml.Append("<MINQTY>").Append(counts[key]).Append("</MINQTY>\n</ITEM>\n");
            }
            xml.Append("</INVENTORY>\n");
            File.WriteAllText(path, xml.ToString(), new UTF8Encoding(false));
            return colorless;
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
