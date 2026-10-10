using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>Client to server: the client loaded the game scene and wants the world.</summary>
    public struct JoinWorldMessage : NetworkMessage { }

    /// <summary>Server to client: how to generate the world and where players spawn.</summary>
    public struct WorldInfoMessage : NetworkMessage
    {
        public long seed;
        public byte difficulty;
        public Vector2Int spawnChunk;

        /// <summary>Chunks drawn around the player, the server sends one more ring for their borders.</summary>
        public int viewDistance;
    }

    /// <summary>
    /// Server to client: a chunk entered the client's area. Its changes come in one or more pages
    /// serialized by ChunkChangesSerializer, the client generates the chunk after the last one.
    /// </summary>
    public struct ChunkChangesMessage : NetworkMessage
    {
        public int chunkX;
        public int chunkZ;
        public byte[] data;
        public bool isLast;
    }

    /// <summary>Server to client: a chunk left the client's area, it can forget it.</summary>
    public struct ChunkUnloadMessage : NetworkMessage
    {
        public int chunkX;
        public int chunkZ;
    }

    /// <summary>Server to clients: a chat message, already checked by the server.</summary>
    public struct ChatMessage : NetworkMessage
    {
        /// <summary>Longest message players can send.</summary>
        public const int MaxLength = 200;

        public string sender;
        public string text;
    }

    /// <summary>Server to client: a world block changed (dug or placed).</summary>
    public struct BlockChangedMessage : NetworkMessage
    {
        public Vector3Int block;
        /// <summary>The block's name: numbers are only valid in the game that gave them (see BlockDatabase).</summary>
        public string blockName;
        /// <summary>Colour it's drawn with (see BrickColorPalette), -1 for its own textures.</summary>
        public int color;
        /// <summary>Who placed it (players.db id) and when (unix seconds), 0 when dug.</summary>
        public int placedBy;
        public long placedAt;
    }

    /// <summary>Server to client: a brick was placed.</summary>
    public struct BrickPlacedMessage : NetworkMessage
    {
        public string id;
        public string itemId;
        /// <summary>Its colour, see BrickColorPalette.</summary>
        public int color;
        public Vector3Int origin;
        public byte rotation;
        /// <summary>Who placed it (players.db id, 0 when no player did) and when (unix seconds).</summary>
        public int placedBy;
        public long placedAt;
        /// <summary>See Brick.state.</summary>
        public int state;
        /// <summary>The brick it's attached to (see Brick.attachedTo), null for bricks on the grid.</summary>
        public string attachedTo;
    }

    /// <summary>Server to client: a brick's state changed (a door opened...), see Brick.state.</summary>
    public struct BrickStateMessage : NetworkMessage
    {
        public string id;
        public Vector3Int origin;
        public int state;
    }

    /// <summary>Server to client: a brick was removed.</summary>
    public struct BrickRemovedMessage : NetworkMessage
    {
        public string id;
        public Vector3Int origin;
    }
}
