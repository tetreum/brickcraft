using System.Collections.Generic;
using Brickcraft.Npcs;
using Brickcraft.World;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// Who sees which networked objects: NPCs only go to the players that have their chunk (see WorldNetwork's
    /// streaming), everything else (players) to everybody. The host shares the server's world, so it sees them
    /// all, like every player in scenes without a streamed world.
    /// </summary>
    public class ChunkInterestManagement : InterestManagement
    {
        [Tooltip("Seconds between checks of who sees what")]
        public float rebuildInterval = 0.5f;

        private double nextRebuild;

        public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnectionToClient newObserver) {
            return isVisible(identity, newObserver);
        }

        public override void OnRebuildObservers(NetworkIdentity identity, HashSet<NetworkConnectionToClient> newObservers) {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn != null && conn.isAuthenticated && conn.identity != null && isVisible(identity, conn)) {
                    newObservers.Add(conn);
                }
            }
        }

        private static bool isVisible(NetworkIdentity identity, NetworkConnectionToClient conn) {
            if (identity.GetComponent<Npc>() == null || WorldBehaviour.Instance == null || conn is LocalConnectionToClient) {
                return true;
            }
            return WorldNetwork.HasSentChunk(conn, WorldBehaviour.ChunkAt(identity.transform.position));
        }

        [ServerCallback]
        private void Update() {
            if (NetworkTime.localTime >= nextRebuild) {
                nextRebuild = NetworkTime.localTime + rebuildInterval;
                RebuildAll();
            }
        }
    }
}
