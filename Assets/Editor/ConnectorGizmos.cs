using Brickcraft;
using Brickcraft.Bricks;
using UnityEditor;
using UnityEngine;

namespace Brickcraft.Editor
{
    /// <summary>
    /// Draws the connectors (see Bricks.Connector) of the brick selected in the hierarchy, in the scene view, while
    /// playing: male ones cyan, female ones orange, a ring at each section of its radius.
    /// </summary>
    [InitializeOnLoad]
    public static class ConnectorGizmos
    {
        private static readonly Color Male = new Color(0.2f, 0.9f, 1f);
        private static readonly Color Female = new Color(1f, 0.6f, 0.1f);

        static ConnectorGizmos() {
            SceneView.duringSceneGui += draw;
        }

        private static void draw(SceneView view) {
            GameObject selected = Selection.activeGameObject;
            if (!Application.isPlaying || selected == null) {
                return;
            }
            Brick brick = brickOf(selected);
            if (brick == null || brick.model == null || brick.gameObject == null) {
                return;
            }
            Transform tr = brick.gameObject.transform;
            float scale = Bricks.LDraw.LDrawModelBuilder.Scale;
            foreach (Connector connector in brick.model.connectors) {
                Handles.color = connector.male ? Male : Female;
                Vector3 axis = tr.TransformDirection(connector.axis);
                Vector3 at = tr.TransformPoint(connector.start);
                Handles.DrawLine(at, tr.TransformPoint(connector.End), 3);
                foreach (ConnectorSection section in connector.sections) {
                    Handles.DrawWireDisc(at, axis, section.radius * scale);
                    at += axis * section.length * scale;
                    Handles.DrawWireDisc(at, axis, section.radius * scale);
                }
            }
        }

        private static Brick brickOf(GameObject selected) {
            foreach (Brick brick in Server.bricks.Values) {
                if (brick.gameObject != null && (brick.gameObject == selected || selected.transform.IsChildOf(brick.gameObject.transform))) {
                    return brick;
                }
            }
            return null;
        }
    }
}
