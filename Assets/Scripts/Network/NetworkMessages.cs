using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>Client to server: the client loaded the game scene and wants the world.</summary>
    public struct JoinWorldMessage : NetworkMessage { }

    /// <summary>Server to client: the seed the world is generated from.</summary>
    public struct WorldInfoMessage : NetworkMessage
    {
        public long seed;
    }

    /// <summary>Server to client: a world block changed (dug or placed).</summary>
    public struct BlockChangedMessage : NetworkMessage
    {
        public Vector3Int block;
        public byte blockType;
    }

    /// <summary>Server to client: a brick was placed.</summary>
    public struct BrickPlacedMessage : NetworkMessage
    {
        public string id;
        public int itemId;
        public Vector3Int origin;
        public byte rotation;
    }

    /// <summary>Server to client: a brick was removed.</summary>
    public struct BrickRemovedMessage : NetworkMessage
    {
        public string id;
    }

    /// <summary>Server to client: every change made before the client joined has been sent.</summary>
    public struct WorldSnapshotEndMessage : NetworkMessage { }
}
