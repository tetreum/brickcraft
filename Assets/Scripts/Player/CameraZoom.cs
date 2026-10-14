using UnityEngine;

namespace Brickcraft
{
    /// <summary>
    /// The place button's zoom (see Player): held with nothing in hand, the camera zooms in a little. Whether it does is
    /// decided when the button goes down, so what happens while it's held (the last potion drunk, falling asleep, a bed or
    /// door used) doesn't start it; it stops when the button is let go or the player can't zoom (frozen, typing...).
    /// </summary>
    public class CameraZoom
    {
        /// <summary>How much of the view is left zoomed in.</summary>
        public const float Factor = 0.6f;
        // how fast it goes in and out, of the whole view a second
        private const float Speed = 4f;

        /// <summary>The view's share now: 1 not zoomed, Factor zoomed in.</summary>
        public float Zoom { get; private set; } = 1;

        // this press zooms
        private bool zooming;

        /// <summary>
        /// A frame: the place button went down (pressed) or is held, and whether the player could zoom now (nothing in
        /// hand nor anything to use looked at, playing). Returns the view's share.
        /// </summary>
        public float Update(bool pressed, bool held, bool canZoom, float deltaTime) {
            if (pressed) {
                zooming = canZoom;
            }
            if (!held || !canZoom) {
                zooming = false;
            }
            Zoom = Mathf.MoveTowards(Zoom, zooming ? Factor : 1, Speed * deltaTime);
            return Zoom;
        }
    }
}
