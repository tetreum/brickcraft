using Brickcraft.Network;
using NUnit.Framework;

namespace Brickcraft.Tests
{
    /// <summary>The chat's lines: events worded by each client (ChatEvents.Describe), and the text players and mods send (ChatEvents.Clean).</summary>
    public class ChatEventsTests
    {
        [Test]
        public void AModsEventIsItsText() {
            Assert.AreEqual("A dragon was seen near spawn", ChatEvents.Describe(new ChatEventMessage() { type = ChatEventType.Mod, reason = "A dragon was seen near spawn" }));
            Assert.IsNull(ChatEvents.Describe(new ChatEventMessage() { type = ChatEventType.Mod }), "nothing to show");
        }

        [Test]
        public void EventsFromANewerServerShowNothing() {
            Assert.IsNull(ChatEvents.Describe(new ChatEventMessage() { type = (ChatEventType)200, player = "Someone" }));
        }

        [Test]
        public void TextIsOneShortLine() {
            Assert.AreEqual("two lines", ChatEvents.Clean(" two\nlines\t"), "control characters are spaces");
            Assert.AreEqual(ChatMessage.MaxLength, ChatEvents.Clean(new string('a', ChatMessage.MaxLength + 50)).Length);
            Assert.AreEqual("", ChatEvents.Clean(null));
        }
    }
}
