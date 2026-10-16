using System.Collections.Generic;
using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>The picked seat's wires (see WiringTool), as lines to their parts while the tool's in hand.</summary>
    public class WiringView : MonoBehaviour
    {
        private static readonly Color LineColor = new Color(1f, 0.85f, 0.2f);
        private const float Width = 0.04f;

        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private Material material;

        private void LateUpdate() {
            Player player = Player.Instance;
            Brick seat = player != null && player.SelectedItem != null && player.SelectedItem.item.IsWiringTool ? WiringTool.SelectedSeat : null;
            int shown = 0;
            if (seat != null) {
                Vector3 from = seat.WorldBounds.center;
                HashSet<string> parts = new HashSet<string>();
                foreach (VehicleWire wire in seat.vehicle.wires) {
                    if (wire.seat == seat.id && parts.Add(wire.part) && Server.bricks.TryGetValue(wire.part, out Brick part)) {
                        LineRenderer line = lineAt(shown++);
                        line.SetPosition(0, from);
                        line.SetPosition(1, part.WorldBounds.center);
                    }
                }
            }
            for (int i = 0; i < lines.Count; i++) {
                lines[i].enabled = i < shown;
            }
        }

        private LineRenderer lineAt(int index) {
            while (lines.Count <= index) {
                if (material == null) {
                    material = new Material(Resources.Load<Material>("Materials/PartsListerEdge"));
                    material.SetColor("_UnlitColor", LineColor);
                }
                GameObject go = new GameObject("Wire");
                go.transform.SetParent(transform, false);
                LineRenderer line = go.AddComponent<LineRenderer>();
                line.material = material;
                line.positionCount = 2;
                line.startWidth = line.endWidth = Width;
                line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lines.Add(line);
            }
            return lines[index];
        }

        private void OnDestroy() {
            if (material != null) {
                Destroy(material);
            }
        }
    }
}
