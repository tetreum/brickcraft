using UnityEngine;

namespace Brickcraft.Bricks
{
    public enum ConnectorShape : byte
    {
        Round,
        /// <summary>A technic axle's cross: it turns in quarter turns.</summary>
        Axle,
        Square,
    }

    /// <summary>A stretch of a connector: its shape, its radius and how long it is (LDU, LDraw's units).</summary>
    public struct ConnectorSection
    {
        public ConnectorShape shape;
        public float radius;
        public float length;
    }

    /// <summary>
    /// Where a brick model connects besides its studs: a technic axle, pin or hole, a bar... (see LDraw.LDrawConnectors).
    /// A cylinder, male (it goes into something) or female (something goes into it), made of sections along its axis
    /// from its start, in the model's space.
    /// </summary>
    public class Connector
    {
        public bool male;
        /// <summary>What goes in may slide along it (an axle through a hole), else it sits at its ends.</summary>
        public bool slide;
        /// <summary>LDCad's: "none" (open), "one" (closed at its end), "two", "A" (closed at its start), "B".</summary>
        public string caps = "none";
        /// <summary>Where its first section begins (the model's space, the game's units).</summary>
        public Vector3 start;
        /// <summary>The way its sections go, a unit vector.</summary>
        public Vector3 axis;
        /// <summary>A unit vector across it: how an axle's cross is turned.</summary>
        public Vector3 side;
        public ConnectorSection[] sections = new ConnectorSection[0];

        /// <summary>How long it is, in the game's units.</summary>
        public float Length {
            get {
                float length = 0;
                foreach (ConnectorSection section in sections) {
                    length += section.length;
                }
                return length * LDraw.LDrawModelBuilder.Scale;
            }
        }

        public Vector3 End {
            get { return start + axis * Length; }
        }

        public Vector3 Middle {
            get { return start + axis * (Length / 2); }
        }

        /// <summary>It has a stretch shaped like an axle (it doesn't spin in its axle hole).</summary>
        public bool HasAxleShape {
            get {
                foreach (ConnectorSection section in sections) {
                    if (section.shape == ConnectorShape.Axle) {
                        return true;
                    }
                }
                return false;
            }
        }

        public override string ToString() {
            string text = (male ? "male" : "female") + (slide ? " sliding" : "") + " from " + start + " along " + axis + ":";
            foreach (ConnectorSection section in sections) {
                text += " " + section.shape + " r" + section.radius + " l" + section.length;
            }
            return text;
        }
    }
}
