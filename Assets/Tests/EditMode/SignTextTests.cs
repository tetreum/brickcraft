using Brickcraft.Bricks;
using NUnit.Framework;

namespace Brickcraft.Tests
{
    /// <summary>What a sign takes (Bricks.SignText.Clean) and who can write on one (SignText.CanWrite).</summary>
    public class SignTextTests
    {
        [Test]
        public void ItsLinesAreTrimmedAndControlsLeftOut() {
            Assert.AreEqual("Hello\nworld", SignText.Clean("  Hello \r\n\tworld\u0007  "));
            Assert.AreEqual("", SignText.Clean(null));
            Assert.AreEqual("", SignText.Clean(" \n \n"), "only blank lines: nothing");
            Assert.AreEqual("<b>bold</b>", SignText.Clean("<b>bold</b>"), "tags stay text (they're shown as they are)");
        }

        [Test]
        public void ItHoldsSoManyLinesAndCharacters() {
            Assert.AreEqual(SignText.MaxLines, SignText.Clean("1\n2\n3\n4\n5\n6\n7\n8").Split('\n').Length);
            Assert.AreEqual(SignText.MaxLength, SignText.Clean(new string('a', 500)).Length);
        }

        [Test]
        public void OnlyWhoPlacedItAndAdminsWriteOnIt() {
            Assert.IsTrue(SignText.CanWrite(7, false, 7), "who placed it");
            Assert.IsFalse(SignText.CanWrite(8, false, 7), "someone else");
            Assert.IsTrue(SignText.CanWrite(8, true, 7), "an admin");
            Assert.IsTrue(SignText.CanWrite(8, false, 0), "one no player placed");
        }
    }
}
