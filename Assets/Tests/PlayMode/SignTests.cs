using System.Collections;
using Brickcraft.Bricks;
using Brickcraft.Network;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Brickcraft.Tests
{
    /// <summary>Signs (Bricks.SignText) in a game: written by who places them, shown on them, saved with the world.</summary>
    [Timeout(120000)]
    public class SignTests
    {
        private TestGame game;
        private string requested;
        private string requestedText;

        [UnitySetUp]
        public IEnumerator SetUp() {
            game = new TestGame();
            yield return game.Start();
            SignText.EditorRequested += onRequested;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            SignText.EditorRequested -= onRequested;
            UI.SignPanel.Close();
            yield return game.Delete();
        }

        private void onRequested(string brickId, string text) {
            requested = brickId;
            requestedText = text;
        }

        // what the sign shows on it, null if nothing
        private static string shown(Brick sign) {
            Text label = sign.gameObject.GetComponentInChildren<Text>(true);
            return label != null && label.GetComponentInParent<Canvas>().enabled ? label.text : null;
        }

        [UnityTest]
        public IEnumerator ASignsFrontFacesWhoPlacesIt() {
            yield return TestGame.StandStill();
            Player player = Player.Instance;
            int slot = Player.FirstFastSlot + 6;
            Assert.IsTrue(player.GetComponent<PlayerInventory>().ServerAdd("sign", Server.items["sign"].color, 1, 0, slot));
            player.selectFastSlot(6);
            BrickPlacer placer = Object.FindObjectOfType<BrickPlacer>();
            System.Reflection.FieldInfo target = typeof(BrickPlacer).GetField("target", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Bounds front = SignText.FrontOf(Server.items["sign"].brickModel);
            Assert.Less(front.center.z, 0, "its front: the flat side (-Z), not the framed one");
            foreach (Vector3 looking in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left }) {
                // looking that way, down at the ground ahead
                player.transform.rotation = Quaternion.LookRotation(looking);
                Camera.main.transform.localRotation = Quaternion.Euler(35, 0, 0);
                player.firstPersonController.ResetLook();
                yield return TestGame.WaitUntil(() => ((BrickPlacement?)target.GetValue(placer)).HasValue, 3, "a preview");
                yield return null;
                BrickPlacement preview = ((BrickPlacement?)target.GetValue(placer)).Value;
                Vector3 facing = preview.Rotation * Vector3.back; // its front's way
                Assert.Greater(Vector3.Dot(facing, -looking), 0.99f, "its front towards the player looking " + looking);
            }
        }

        [UnityTest]
        public IEnumerator APlacedSignIsWrittenAndKeepsItsText() {
            yield return TestGame.StandStill();
            Player player = Player.Instance;
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            int slot = Player.FirstFastSlot + 6;
            Assert.IsTrue(inventory.ServerAdd("sign", Server.items["sign"].color, 1, 0, slot));

            // in front of the player
            Transform eye = Camera.main.transform;
            BrickPlacement placement = new BrickPlacement(Server.items["sign"].brickModel, BrickGrid.WorldToCell(eye.position + player.transform.forward * 2.5f) - new Vector3Int(3, 3, 0));
            Assume.That(BrickGrid.IsFree(placement), "room in front of the player");
            player.network.CmdPlaceBrick(slot, placement.origin, (byte)placement.rotation);
            yield return TestGame.WaitUntil(() => requested != null, 5, "its placer asked to write it");
            Assert.IsTrue(Server.bricks.TryGetValue(requested, out Brick sign), "placed");
            Assert.AreEqual("", requestedText, "blank at first");
            Assert.AreEqual(requested, UI.SignPanel.Writing, "the editor open on it");

            UI.SignPanel.Save("  Welcome  \nto Brickcraft\n\u0007");
            yield return null;
            Assert.IsNull(UI.SignPanel.Writing, "closed");
            Assert.AreEqual("Welcome\nto Brickcraft", sign.text, "written, cleaned");
            yield return null;
            Assert.AreEqual("Welcome\nto Brickcraft", shown(sign), "shown on it");

            // clicked again: its text to change
            requested = null;
            player.network.CmdOpenSign(sign.id);
            yield return TestGame.WaitUntil(() => requested != null, 5, "the editor again");
            Assert.AreEqual("Welcome\nto Brickcraft", requestedText);
            UI.SignPanel.Close();

            // the world saved and played again: still written
            string id = sign.id;
            yield return game.Stop();
            yield return game.Start();
            yield return TestGame.WaitUntil(() => Server.bricks.ContainsKey(id), 10, "the sign back");
            yield return null;
            Assert.AreEqual("Welcome\nto Brickcraft", Server.bricks[id].text);
            // shown once it sees the player near (it checks every now and then)
            yield return TestGame.WaitUntil(() => shown(Server.bricks[id]) == "Welcome\nto Brickcraft", 3, "shown again");
        }
    }
}
