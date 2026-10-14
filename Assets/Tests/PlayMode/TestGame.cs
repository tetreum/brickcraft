using System;
using System.Collections;
using System.IO;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.World;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brickcraft.Tests
{
    /// <summary>
    /// The game played alone on a world of its own (a save named zz_test_..., with the game's usual seed), as a
    /// player would: the world scene loads, the spawn area is built and the player appears. Delete removes the save;
    /// a test that's stopped before it can (timed out) leaves it, so starting removes the ones left before.
    /// </summary>
    public class TestGame
    {
        private const float StartTimeout = 30;
        private const float StopTimeout = 30;
        private const string SavePrefix = "zz_test_";

        public readonly string saveName = SavePrefix + Guid.NewGuid().ToString("N").Substring(0, 8);

        private static string SavesFolder {
            get { return Path.Combine(Application.persistentDataPath, "saves"); }
        }

        public string SaveFolder {
            get { return Path.Combine(SavesFolder, saveName); }
        }

        public static GameObject Player {
            get { return NetworkClient.localPlayer != null ? NetworkClient.localPlayer.gameObject : null; }
        }

        /// <summary>The block (absolute) the player stands in.</summary>
        public static Vector3Int PlayerBlock {
            get { return BrickGrid.CellToBlock(BrickGrid.WorldToCell(Player.transform.position)); }
        }

        /// <summary>Plays its world until the player is in it, drawing it as near as the game allows (fewer chunks to load).</summary>
        public IEnumerator Start() {
            deleteLeftovers();
            GameSettings.RenderDistanceOverride = GameSettings.MinRenderDistance;
            GameSettings.MasterVolumeOverride = 0; // silent (a test about sound sets its own)
            BrickcraftNetworkManager.GetOrCreate().PlayWorld(saveName);
            float end = Time.realtimeSinceStartup + StartTimeout;
            while (Player == null) {
                Assert.Less(Time.realtimeSinceStartup, end, "the player didn't appear in " + StartTimeout + " seconds");
                Assert.IsFalse(BrickcraftNetworkManager.Instance.HasFailedToStart, "the server didn't start");
                yield return null;
            }
        }

        // the saves of tests stopped before deleting theirs (no game is on while another starts)
        private void deleteLeftovers() {
            if (!Directory.Exists(SavesFolder)) {
                return;
            }
            foreach (string folder in Directory.GetDirectories(SavesFolder, SavePrefix + "*")) {
                if (Path.GetFileName(folder) == saveName) {
                    continue;
                }
                try {
                    Directory.Delete(folder, true);
                } catch (IOException e) {
                    Debug.LogWarning("A test's save couldn't be deleted: " + folder + " (" + e.Message + ")");
                } catch (UnauthorizedAccessException e) {
                    Debug.LogWarning("A test's save couldn't be deleted: " + folder + " (" + e.Message + ")");
                }
            }
        }

        /// <summary>Leaves the game (saving it) and waits for the server to stop.</summary>
        public IEnumerator Stop() {
            if (BrickcraftNetworkManager.Instance == null) {
                yield break;
            }
            if (WorldNetwork.Storage != null) {
                WorldNetwork.Storage.SaveNow();
            }
            BrickcraftNetworkManager.Instance.Leave();
            float end = Time.realtimeSinceStartup + StopTimeout;
            // back in the menu, its scene loaded (Mirror keeps loadingSceneAsync once done)
            while ((NetworkServer.active || NetworkClient.active || (NetworkManager.loadingSceneAsync != null && !NetworkManager.loadingSceneAsync.isDone)
                    || SceneManager.GetActiveScene().path != BrickcraftNetworkManager.Instance.offlineScene)
                    && Time.realtimeSinceStartup < end) {
                yield return null;
            }
            yield return null;
        }

        /// <summary>Stops the game if it's on and deletes its save.</summary>
        public IEnumerator Delete() {
            yield return Stop();
            GameSettings.RenderDistanceOverride = null;
            GameSettings.MasterVolumeOverride = null;
            for (int attempt = 0; Directory.Exists(SaveFolder); attempt++) {
                try {
                    Directory.Delete(SaveFolder, true);
                } catch (IOException) when (attempt < 50) {
                    // the database or a region file closing
                } catch (UnauthorizedAccessException) when (attempt < 50) {
                }
                yield return new WaitForSecondsRealtime(0.1f);
            }
        }

        /// <summary>Waits until the condition holds, failing after that many seconds.</summary>
        public static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what) {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition()) {
                Assert.Less(Time.realtimeSinceStartup, end, what + " didn't happen in " + seconds + " seconds");
                yield return null;
            }
        }

        public static IEnumerator Wait(float seconds) {
            yield return new WaitForSeconds(seconds);
        }

        public static BlockType Block(Vector3Int block) {
            return WorldBehaviour.Instance.GetBlockType(block);
        }

        /// <summary>A floor of that block in the air, size blocks from the centre each way, to test on whatever the terrain.</summary>
        public static void Floor(Vector3Int centre, int size, BlockType type) {
            for (int x = -size; x <= size; x++) {
                for (int z = -size; z <= size; z++) {
                    Assert.IsTrue(WorldNetwork.ServerSetBlock(centre + new Vector3Int(x, 0, z), type, Placer.Now(0)), "the floor's chunks are loaded");
                }
            }
        }
    }
}
