using System;
using Brickcraft.Bricks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brickcraft.World
{
    /// <summary>
    /// Keeps the local player near Unity's (0,0,0). Floats only have 24 bits of precision, so far
    /// from the origin positions get coarse (about 1 mm at 10 km, 8 mm at 100 km): rendering
    /// jitters, physics gets unreliable and bricks stop lining up.
    ///
    /// The game itself works in integer cells, blocks and chunks, which are exact anywhere. Unity
    /// positions are relative to <see cref="Chunk"/>, and the integer origin is subtracted before
    /// converting to floats (see <see cref="BrickGrid"/>), so precision is the same everywhere.
    /// When the player gets too far, the origin moves by whole chunks and every object in the
    /// scene is shifted back by the same amount.
    ///
    /// Each peer has its own origin, so positions sent over the network are absolute
    /// (see Network.OriginAwareNetworkTransform).
    /// </summary>
    public static class FloatingOrigin
    {
        // chunks the player can get from the origin before it moves (about 400 units)
        public const int RecenterDistance = 32;

        /// <summary>The chunk at Unity's origin.</summary>
        public static Vector2Int Chunk { get; private set; }

        /// <summary>The cell at Unity's origin, a multiple of the chunk size so grids stay aligned.</summary>
        public static Vector3Int Cell {
            get { return new Vector3Int(Chunk.x * 16 * BrickGrid.StudsPerBlock, 0, Chunk.y * 16 * BrickGrid.StudsPerBlock); }
        }

        /// <summary>Unity position of the origin in absolute coordinates.</summary>
        public static Vector3 Offset {
            get { return new Vector3(Chunk.x * 16 * Server.brickWidth, 0, Chunk.y * 16 * Server.brickWidth); }
        }

        /// <summary>Raised after the scene was shifted, with how much everything moved.</summary>
        public static event Action<Vector3> Shifted;

        public static void Reset() {
            Chunk = Vector2Int.zero;
        }

        public static Vector3 ToAbsolute(Vector3 local) {
            return local + Offset;
        }

        public static Vector3 ToLocal(Vector3 absolute) {
            return absolute - Offset;
        }

        /// <summary>Absolute position in doubles, exact however far it is (for saving it).</summary>
        public static void ToAbsolute(Vector3 local, out double x, out double y, out double z) {
            x = (double)Chunk.x * 16 * Server.brickWidth + local.x;
            y = local.y;
            z = (double)Chunk.y * 16 * Server.brickWidth + local.z;
        }

        public static Vector3 ToLocal(double x, double y, double z) {
            return new Vector3(
                (float)(x - (double)Chunk.x * 16 * Server.brickWidth),
                (float)y,
                (float)(z - (double)Chunk.y * 16 * Server.brickWidth)
            );
        }

        /// <summary>Moves the origin to the given position's chunk if it's too far from it.</summary>
        public static void Recenter(Vector3 localPosition) {
            Vector2Int chunk = WorldBehaviour.ChunkAt(localPosition);
            Vector2Int distance = chunk - Chunk;

            if (Math.Abs(distance.x) <= RecenterDistance && Math.Abs(distance.y) <= RecenterDistance) {
                return;
            }

            Vector3 shift = -new Vector3(distance.x * 16 * Server.brickWidth, 0, distance.y * 16 * Server.brickWidth);
            Chunk = chunk;

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects()) {
                // screen space UI isn't in the world
                if (root.GetComponent<RectTransform>() == null) {
                    root.transform.position += shift;
                }
            }
            // colliders (character controllers included) follow their transforms right away
            Physics.SyncTransforms();

            Shifted?.Invoke(shift);
        }
    }
}
