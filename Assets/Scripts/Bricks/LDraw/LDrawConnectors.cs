using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Brickcraft.Bricks.LDraw
{
    /// <summary>
    /// Where LDraw parts connect besides their studs (see Connector): StreamingAssets/LDraw/connectors.txt, built from
    /// the LDCad Shadow Library (CC BY-SA 4.0, see connectors-LICENSE.md) by the editor's LDCadConnectorBuilder. A line
    /// per connector: the part, M or F, whether it slides, its caps, its start, axis and side (LDU, LDraw's axes) and
    /// its sections (shape, radius, length). Read the first time a part's are asked for.
    /// </summary>
    public static class LDrawConnectors
    {
        public const string File = "connectors.txt";

        private static Dictionary<string, List<Connector>> byPart;

        /// <summary>Forgets the file read: the next part asked for reads it again.</summary>
        public static void Reload() {
            byPart = null;
        }

        /// <summary>
        /// A part's connectors in its model's space: LDraw's turned the game's way (Y up) and scaled, its LDraw origin
        /// there (see BrickModel.ldrawOrigin). None if it has none.
        /// </summary>
        public static Connector[] For(string partId, Vector3 origin) {
            load();
            if (partId == null || !byPart.TryGetValue(partId.ToLowerInvariant(), out List<Connector> connectors)) {
                return new Connector[0];
            }
            float scale = LDrawModelBuilder.Scale;
            Connector[] placed = new Connector[connectors.Count];
            for (int i = 0; i < connectors.Count; i++) {
                Connector c = connectors[i];
                placed[i] = new Connector() {
                    male = c.male,
                    slide = c.slide,
                    caps = c.caps,
                    start = new Vector3(c.start.x * scale, -c.start.y * scale, c.start.z * scale) + origin,
                    axis = new Vector3(c.axis.x, -c.axis.y, c.axis.z).normalized,
                    side = new Vector3(c.side.x, -c.side.y, c.side.z).normalized,
                    sections = c.sections,
                };
            }
            return placed;
        }

        private static void load() {
            if (byPart != null) {
                return;
            }
            byPart = new Dictionary<string, List<Connector>>();
            string path = Path.Combine(LDrawLibrary.Root, File);
            if (!System.IO.File.Exists(path)) {
                return;
            }
            int number = 0;
            foreach (string raw in System.IO.File.ReadAllLines(path)) {
                number++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) {
                    continue;
                }
                string[] f = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 16 || (f.Length - 13) % 3 != 0) {
                    Debug.LogWarning(path + ":" + number + " isn't a connector");
                    continue;
                }
                Connector connector = new Connector() {
                    male = f[1] == "M",
                    slide = f[2] == "1",
                    caps = f[3],
                    start = vector(f, 4),
                    axis = vector(f, 7),
                    side = vector(f, 10),
                };
                List<ConnectorSection> sections = new List<ConnectorSection>();
                for (int i = 13; i + 2 < f.Length; i += 3) {
                    sections.Add(new ConnectorSection() {
                        shape = f[i] == "A" ? ConnectorShape.Axle : f[i] == "S" ? ConnectorShape.Square : ConnectorShape.Round,
                        radius = num(f[i + 1]),
                        length = num(f[i + 2]),
                    });
                }
                connector.sections = sections.ToArray();
                string part = f[0].ToLowerInvariant();
                if (!byPart.TryGetValue(part, out List<Connector> list)) {
                    list = new List<Connector>();
                    byPart[part] = list;
                }
                list.Add(connector);
            }
        }

        private static Vector3 vector(string[] f, int at) {
            return new Vector3(num(f[at]), num(f[at + 1]), num(f[at + 2]));
        }

        private static float num(string text) {
            return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
