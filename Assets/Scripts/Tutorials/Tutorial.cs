using System;
using System.Collections.Generic;

namespace Brickcraft.Tutorials
{
    /// <summary>
    /// A tutorial: a world of its own (flat and peaceful, made new each time, see World.SavedWorlds.CreateTutorial)
    /// where the player gets what it needs in its hotbar and is walked through steps, each done when the world shows
    /// it (see UI.TutorialGuide). The menu's Tutorials lists them (see All).
    /// </summary>
    public class Tutorial
    {
        public string id;
        /// <summary>Its title and what it teaches, keys of the UI string table (see Loc).</summary>
        public string titleKey;
        public string descriptionKey;
        /// <summary>Its world's terrain (see World.SavedWorlds.CreateTutorial).</summary>
        public World.TerrainKind terrain = World.TerrainKind.Flat;
        /// <summary>What the player gets, in its hotbar from the first slot, one stack a slot.</summary>
        public TutorialItem[] items;
        public TutorialStep[] steps;
        /// <summary>
        /// Builds what it builds, finished, beside where the player starts (given), to look at, as placed by that
        /// player (its id) if it's to be the player's; false if it couldn't. Null if it has none.
        /// </summary>
        public Func<UnityEngine.Vector3, int, bool> buildDemo;
        /// <summary>What the guide says once it's all done, a key of the UI string table (see UI.TutorialGuide).</summary>
        public string doneKey = "tutorial.done";
        /// <summary>Not in the menu's list for now (its worlds still play it).</summary>
        public bool hidden;

        /// <summary>The game's tutorials, in the order the menu shows them (but the hidden ones).</summary>
        public static IReadOnlyList<Tutorial> All {
            get { return new[] { FirstHouse.Tutorial, FirstCar.Tutorial, FirstExport.Tutorial, FirstPlane.Tutorial, FirstBoat.Tutorial }; }
        }

        /// <summary>The tutorial with that id, null if there's none.</summary>
        public static Tutorial Find(string id) {
            foreach (Tutorial tutorial in All) {
                if (tutorial.id == id) {
                    return tutorial;
                }
            }
            return null;
        }

        /// <summary>Its finished vehicle, built beside where the player (its id) starts (the first time it joins its world).</summary>
        public void ServerBuildDemo(UnityEngine.Vector3 start, int playerId) {
            if (buildDemo == null) {
                return;
            }
            try {
                if (!buildDemo(start, playerId)) {
                    UnityEngine.Debug.LogWarning("The tutorial " + id + "'s finished vehicle couldn't be built");
                }
            } catch (Exception e) {
                UnityEngine.Debug.LogError("Building the tutorial " + id + "'s finished vehicle: " + e);
            }
        }

        /// <summary>The player of a tutorial's world gets its items (the first time it joins it).</summary>
        public void ServerGive(Network.PlayerInventory inventory) {
            for (int i = 0; i < items.Length; i++) {
                string itemId = items[i].itemId;
                int color = Server.items.TryGetValue(itemId, out Item item) ? item.color : Bricks.BrickColor.None;
                inventory.ServerAdd(itemId, color, items[i].quantity, 0, Player.FirstFastSlot + i);
            }
        }
    }

    public struct TutorialItem
    {
        public string itemId;
        public int quantity;

        public TutorialItem(string itemId, int quantity) {
            this.itemId = itemId;
            this.quantity = quantity;
        }
    }

    /// <summary>What the player is told to do (a key of the UI string table), and how the game knows it's done.</summary>
    public class TutorialStep
    {
        public string textKey;
        public Func<bool> isDone;
        /// <summary>When it becomes the step to do (to remember where things are), if anything.</summary>
        public Action begin;
        /// <summary>
        /// It's done over time (driving somewhere), not by something placed or a seat taken: it's checked every frame
        /// instead of when the world changes.
        /// </summary>
        public bool overTime;
    }
}
