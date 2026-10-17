using System.Collections.Generic;
using System.IO;
using Brickcraft.Bricks.LDraw;
using Brickcraft.Network;
using Brickcraft.World;
using Brickcraft.World.CustomGenerator;
using UnityEngine;

namespace Brickcraft.Tutorials
{
    /// <summary>
    /// Export your first model to real life: a house stands in front of where the player starts, built as if by it
    /// (StreamingAssets/Tutorials/house.ldr, so the parts lister takes it as its own). It uses the parts lister on the
    /// house, fits the box around it and saves the list (see Network.PartsLister); the guide then tells it
    /// where the files are and that the BrickLink wanted list (.xml) can be uploaded to buy the parts.
    /// </summary>
    public static class FirstExport
    {
        public const string Id = "first_export";
        public const string Lister = "parts_lister";
        /// <summary>The house, in StreamingAssets.</summary>
        public const string HouseFile = "Tutorials/house.ldr";
        /// <summary>How far in front of where the player starts the house is, in blocks.</summary>
        public const int HouseAhead = 8;
        // its quarter turns, so its windows face the player
        private const int HouseTurns = 1;

        // a list was saved since the step to save one began
        private static bool saved;

        static FirstExport() {
            PartsLister.ListSaved += folder => saved = true;
        }

        public static readonly Tutorial Tutorial = new Tutorial() {
            id = Id,
            titleKey = "tutorial.first_export.title",
            descriptionKey = "tutorial.first_export.description",
            items = new[] {
                new TutorialItem(Lister, 1),
            },
            steps = new[] {
                new TutorialStep() { textKey = "tutorial.first_export.use", isDone = () => PartsListerView.IsOpen, overTime = true },
                new TutorialStep() {
                    textKey = "tutorial.first_export.save",
                    begin = () => saved = false,
                    isDone = () => saved,
                    overTime = true,
                },
            },
            doneKey = "tutorial.first_export.done",
            buildDemo = (start, player) => BuildHouse(start, player) > 0,
        };

        /// <summary>
        /// The house in front of where the player starts, its middle ahead of it, as placed by that player (its id);
        /// how many of its bricks went.
        /// </summary>
        public static int BuildHouse(Vector3 start, int playerId) {
            string path = Path.Combine(Application.streamingAssetsPath, HouseFile);
            if (!File.Exists(path)) {
                Debug.LogWarning("The tutorial's house, " + path + ", isn't there");
                return 0;
            }
            LDrawImport house = LDrawImport.Load(path);
            Vector3Int ahead = TutorialDemos.CellBeside(start, new Vector3Int(0, CustomChunkGenerator.FlatGrassHeight + 1, HouseAhead));
            // a quarter turn: its windows' side towards the player
            Vector3Int corner = ahead - new Vector3Int(house.SizeTurned(HouseTurns).x / 2, 0, 0);
            return Place(house.Placed(corner, HouseTurns), Placer.Now(playerId));
        }

        /// <summary>Places a model's bricks (see LDrawImport.Placed) as placed so, its attachments in their bricks; how many went.</summary>
        public static int Place(List<ImportedBrick> bricks, Placer placer) {
            Brick[] placed = new Brick[bricks.Count];
            int count = 0;
            for (int i = 0; i < bricks.Count; i++) {
                ImportedBrick brick = bricks[i];
                if (brick.IsAttachment || !Server.items.TryGetValue(brick.itemId, out Item item)) {
                    continue;
                }
                placed[i] = WorldNetwork.ServerPlaceBrick(item, item.ValidColor(brick.color),
                    new Bricks.BrickPlacement(item.brickModel, brick.origin, brick.rotation), placer);
                if (placed[i] != null) {
                    count++;
                }
            }
            for (int i = 0; i < bricks.Count; i++) {
                ImportedBrick brick = bricks[i];
                if (!brick.IsAttachment || !Server.items.TryGetValue(brick.itemId, out Item item)) {
                    continue;
                }
                Brick holder = placed[brick.attachedTo - 1];
                if (holder != null && PlayerNetwork.CanAttach(item.brickModel, holder)) {
                    int seat = Mathf.Clamp(brick.seat, 0, Server.slotOf(holder).Seats - 1);
                    if (WorldNetwork.ServerAttachBrick(item, item.ValidColor(brick.color), holder, placer, seat) != null) {
                        count++;
                    }
                }
            }
            return count;
        }
    }
}
