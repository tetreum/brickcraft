using Brickcraft.World;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// NetworkTransform that syncs absolute positions: every peer has its own floating origin
    /// (see <see cref="FloatingOrigin"/>), so positions are converted when sent and applied.
    /// Interpolation buffers only hold absolute positions, so shifting the origin doesn't
    /// disturb them.
    /// </summary>
    public class OriginAwareNetworkTransform : NetworkTransformUnreliable
    {
        protected override Vector3 GetPosition() {
            return FloatingOrigin.ToAbsolute(base.GetPosition());
        }

        protected override void SetPosition(Vector3 position) {
            base.SetPosition(FloatingOrigin.ToLocal(position));
        }
    }
}
