using NUnit.Framework;

namespace Brickcraft.Tests
{
    /// <summary>The place button's zoom (CameraZoom): only a press that starts with nothing to do zooms.</summary>
    public class CameraZoomTests
    {
        private const float Frame = 1 / 60f;
        private CameraZoom zoom;

        [SetUp]
        public void SetUp() {
            zoom = new CameraZoom();
        }

        // the button pressed this frame, then held for a second, the player able to zoom or not
        private void press(bool canZoom) {
            zoom.Update(true, true, canZoom, Frame);
            hold(canZoom);
        }

        private void hold(bool canZoom) {
            for (int i = 0; i < 60; i++) {
                zoom.Update(false, true, canZoom, Frame);
            }
        }

        private void release() {
            for (int i = 0; i < 60; i++) {
                zoom.Update(false, false, true, Frame);
            }
        }

        [Test]
        public void HoldingItWithNothingInHandZooms() {
            press(true);
            Assert.AreEqual(CameraZoom.Factor, zoom.Zoom, 0.001f);
            release();
            Assert.AreEqual(1, zoom.Zoom, 0.001f);
        }

        [Test]
        public void DrinkingTheLastPotionDoesntZoom() {
            // pressed with a potion in hand: it's drunk, the hand is empty while the button is still held
            zoom.Update(true, true, false, Frame);
            hold(true);
            Assert.AreEqual(1, zoom.Zoom, 0.001f);
        }

        [Test]
        public void FallingAsleepStopsItAndWakingUpDoesntStartIt() {
            press(true);
            hold(false); // asleep: frozen
            Assert.AreEqual(1, zoom.Zoom, 0.001f);
            hold(true); // awake, the button still held
            Assert.AreEqual(1, zoom.Zoom, 0.001f);
            release();
            press(true);
            Assert.AreEqual(CameraZoom.Factor, zoom.Zoom, 0.001f, "a new press zooms again");
        }

        [Test]
        public void UsingABedOrADoorDoesntZoom() {
            press(false); // looking at it: the click uses it
            Assert.AreEqual(1, zoom.Zoom, 0.001f);
        }
    }
}
