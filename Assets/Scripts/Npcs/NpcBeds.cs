using System.Collections.Generic;
using Brickcraft.Bricks;
using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// Who sleeps in which bed, on the server. Beds are bricks whose item has the "bed" tag (see Item.tags);
    /// a child named SleepPoint is where a sleeper's feet go and which way they lie (it faces the feet, the
    /// head goes the other way). An NPC that goes to sleep claims the nearest free bed within SearchRadius of
    /// its home (found through BrickGrid's chunks), and keeps it (saved with it) while the bed is there.
    /// </summary>
    public static class NpcBeds
    {
        public const float SearchRadius = 32;
        public const string SleepPointName = "SleepPoint";

        // the NPC sleeping in each bed, by brick id
        private static readonly Dictionary<string, Npc> claims = new Dictionary<string, Npc>();

        public static void Clear() {
            claims.Clear();
        }

        public static bool IsBed(Brick brick) {
            return brick != null && brick.gameObject != null && Server.items.TryGetValue(brick.itemId, out Item item) && item.tags.Contains(Item.BedTag);
        }

        /// <summary>Its bed, if it still has one (a bed that's gone is let go).</summary>
        public static Brick BedOf(Npc npc) {
            if (npc.BedId == null) {
                return null;
            }
            if (Server.bricks.TryGetValue(npc.BedId, out Brick bed) && IsBed(bed) && claims.TryGetValue(bed.id, out Npc sleeper) && sleeper == npc) {
                return bed;
            }
            Release(npc);
            return null;
        }

        /// <summary>Its bed, or the nearest free one around its home (null if there's none).</summary>
        public static Brick Claim(Npc npc) {
            Brick bed = BedOf(npc);
            if (bed != null) {
                return bed;
            }
            int cells = Mathf.CeilToInt(SearchRadius / Server.studSize);
            Vector3Int home = npc.Home;
            Vector3 homePosition = npc.HomePosition;
            float best = SearchRadius;
            foreach (Brick candidate in BrickGrid.BricksNear(home - new Vector3Int(cells, 0, cells), home + new Vector3Int(cells, 0, cells))) {
                if (!IsBed(candidate) || isTaken(candidate, npc)) {
                    continue;
                }
                float distance = Vector3.Distance(candidate.placement.Position, homePosition);
                if (distance <= best) {
                    best = distance;
                    bed = candidate;
                }
            }
            if (bed != null) {
                claims[bed.id] = npc;
                npc.BedId = bed.id;
            }
            return bed;
        }

        /// <summary>It had that bed (back from a save): it's its again if it's still there and free.</summary>
        public static void Restore(Npc npc, string bedId) {
            if (bedId != null && Server.bricks.TryGetValue(bedId, out Brick bed) && IsBed(bed) && !isTaken(bed, npc)) {
                claims[bed.id] = npc;
                npc.BedId = bed.id;
            }
        }

        public static void Release(Npc npc) {
            if (npc.BedId != null && claims.TryGetValue(npc.BedId, out Npc sleeper) && sleeper == npc) {
                claims.Remove(npc.BedId);
            }
            npc.BedId = null;
        }

        /// <summary>Where a sleeper lies in it: its SleepPoint, or the bed's middle.</summary>
        public static Transform SleepPointOf(Brick bed) {
            foreach (Transform child in bed.gameObject.GetComponentsInChildren<Transform>()) {
                if (child.name == SleepPointName) {
                    return child;
                }
            }
            return bed.gameObject.transform;
        }

        // someone else alive sleeps in it
        private static bool isTaken(Brick bed, Npc npc) {
            return claims.TryGetValue(bed.id, out Npc sleeper) && sleeper != null && sleeper != npc && !sleeper.Health.isDead;
        }
    }
}
