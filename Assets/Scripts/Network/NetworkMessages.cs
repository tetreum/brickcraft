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
    }

    /// <summary>Server to client: a brick was placed.</summary>
    public struct BrickPlacedMessage : NetworkMessage
    {
        public string id;
        public string itemId;
        public Vector3Int origin;
        public byte rotation;
    }

    /// <summary>Server to client: a brick was removed.</summary>
    public struct BrickRemovedMessage : NetworkMessage
    {
        public string id;
        public Vector3Int origin;
    }
}
