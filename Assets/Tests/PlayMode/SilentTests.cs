using NUnit.Framework;

namespace Brickcraft.Tests
{
    /// <summary>
    /// The whole Play Mode test run is silent (the master volume at 0, not saved): the menu between tests (its music)
    /// too, not only the games they play. A test about sound sets its own volume.
    /// </summary>
    [SetUpFixture]
    public class SilentTests
    {
        [OneTimeSetUp]
        public void Mute() {
            GameSettings.MasterVolumeOverride = 0;
        }

        [OneTimeTearDown]
        public void Unmute() {
            GameSettings.MasterVolumeOverride = null;
        }
    }
}
