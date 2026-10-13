using System.Collections;
using Brickcraft.Network;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;

namespace Brickcraft.Tests
{
    /// <summary>
    /// The game played with a keyboard and a mouse: the Input System's test devices (InputTestFixture) pressed for the
    /// real player, in a world of its own (TestGame).
    /// </summary>
    [Timeout(120000)]
    public class InputTests
    {
        private TestGame game;
        private InputTestFixture input;
        private Keyboard keyboard;
        private Mouse mouse;

        [UnitySetUp]
        public IEnumerator SetUp() {
            game = new TestGame();
            yield return game.Start();
            // the real devices out of the way, test ones instead; GameInput's actions made for them
            input = new InputTestFixture();
            input.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            GameInput.Reload();
            yield return frames(2);
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            input.TearDown();
            GameInput.Reload();
            yield return game.Delete();
        }

        private static IEnumerator frames(int count) {
            for (int i = 0; i < count; i++) {
                yield return null;
            }
        }

        // game time, however fast frames go
        private static IEnumerator seconds(float time) {
            yield return new WaitForSeconds(time);
        }

        private static Player player {
            get { return Player.Instance; }
        }

        // a fast inventory slot (0 to 8) in hand, with that item (or nothing)
        private static void hold(int index, string itemId, int quantity) {
            int slot = Player.FirstFastSlot + index;
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            InventoryItem? there = inventory.ServerGetSlot(slot);
            if (there.HasValue) {
                inventory.ServerRemoveFromSlot(slot, there.Value.quantity);
            }
            if (itemId != null) {
                Assert.IsTrue(inventory.ServerAdd(itemId, Server.items[itemId].color, quantity, 0, slot));
            }
            if (player.SelectedSlot != slot) {
                player.selectFastSlot(index);
            }
        }

        private static int quantityIn(int index) {
            InventoryItem? there = player.GetComponent<PlayerInventory>().ServerGetSlot(Player.FirstFastSlot + index);
            return there.HasValue ? there.Value.quantity : 0;
        }

        [UnityTest]
        public IEnumerator HoldingForwardWalksButNotWhileChatting() {
            Vector3 start = player.transform.position;
            input.Press(keyboard.wKey);
            yield return frames(30);
            input.Release(keyboard.wKey);
            Assert.Greater(Vector3.Distance(start, player.transform.position), 0.3f, "walked");

            input.PressAndRelease(keyboard.enterKey); // the chat opens: keys are text
            yield return frames(2);
            Assert.IsTrue(GameInput.IsTyping, "the chat is open");
            start = player.transform.position;
            input.Press(keyboard.wKey);
            yield return frames(30);
            input.Release(keyboard.wKey);
            Vector3 moved = player.transform.position - start;
            Assert.Less(new Vector2(moved.x, moved.z).magnitude, 0.05f, "typing a w doesn't walk");
            input.PressAndRelease(keyboard.escapeKey);
            yield return frames(2);
        }

        [UnityTest]
        public IEnumerator TheMouseTurnsThePlayer() {
            float before = player.transform.eulerAngles.y;
            input.Set(mouse.delta, new Vector2(100, 0));
            yield return frames(2);
            // a tenth of the pixels, times the sensitivity (2): like the classic input
            Assert.AreEqual(20, Mathf.DeltaAngle(before, player.transform.eulerAngles.y), 0.5f);
        }

        [UnityTest]
        public IEnumerator RightClickWithNothingInHandZooms() {
            hold(8, null, 0);
            yield return frames(2);
            float view = Camera.main.fieldOfView;
            input.Press(mouse.rightButton);
            yield return seconds(0.5f);
            Assert.Less(Camera.main.fieldOfView, view * 0.8f, "zoomed in");
            input.Release(mouse.rightButton);
            yield return seconds(0.5f);
            Assert.AreEqual(view, Camera.main.fieldOfView, 0.1f, "back out");
        }

        [UnityTest]
        public IEnumerator DrinkingTheLastPotionDoesntZoom() {
            Combat.Health health = player.GetComponent<Combat.Health>();
            Assert.IsTrue(health.ServerDamage(new Combat.DamageInfo() { amount = 5, kind = Combat.DamageInfo.Kind.Script }));
            hold(7, "hp_potion", 1);
            yield return frames(2);
            float view = Camera.main.fieldOfView;
            input.Press(mouse.rightButton);
            yield return seconds(0.5f);
            Assert.AreEqual(0, quantityIn(7), "drunk");
            Assert.AreEqual(health.maxHealth, health.health);
            Assert.AreEqual(view, Camera.main.fieldOfView, 0.1f, "the hand is empty now, but the press was a drink");
            input.Release(mouse.rightButton);
            yield return frames(2);
        }

        [UnityTest]
        public IEnumerator QDropsOneAndCtrlQTheStack() {
            hold(6, "stone", 5);
            yield return frames(2);
            input.PressAndRelease(keyboard.qKey);
            yield return frames(5);
            Assert.AreEqual(4, quantityIn(6));

            input.Press(keyboard.leftCtrlKey);
            input.PressAndRelease(keyboard.qKey);
            yield return frames(5);
            input.Release(keyboard.leftCtrlKey);
            Assert.AreEqual(0, quantityIn(6));
        }

        private float highest;

        // a key held a moment (the player's FixedUpdate sees it), the player's highest point over that and the next half second
        private IEnumerator press(KeyControl key) {
            highest = player.transform.position.y;
            input.Press(key);
            float end = Time.time + 0.6f;
            bool released = false;
            while (Time.time < end) {
                yield return null;
                highest = Mathf.Max(highest, player.transform.position.y);
                if (!released && Time.time > end - 0.5f) {
                    input.Release(key);
                    released = true;
                }
            }
        }

        private float highestOver(float ground) {
            return Mathf.Max(highest, ground);
        }

        [UnityTest]
        public IEnumerator ARemappedKeyWorksAndItsOldOneDoesnt() {
            string saved = PlayerPrefs.GetString("binding.path." + GameInput.Jump, "");
            try {
                GameInput.Remap(GameInput.Jump, "<Keyboard>/j");
                yield return seconds(1); // on the ground
                float ground = player.transform.position.y;

                yield return press(keyboard.spaceKey);
                Assert.Less(highestOver(ground) - ground, 0.1f, "Space doesn't jump any more");
                yield return seconds(0.2f);

                yield return press(keyboard.jKey);
                Assert.Greater(highestOver(ground) - ground, 0.3f, "J jumps");
            } finally {
                if (saved.Length > 0) {
                    GameInput.Remap(GameInput.Jump, saved);
                } else {
                    PlayerPrefs.DeleteKey("binding.path." + GameInput.Jump);
                    PlayerPrefs.Save();
                    GameInput.Reload();
                }
            }
        }
    }
}
