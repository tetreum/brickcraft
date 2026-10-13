using Brickcraft.Bricks.LDraw;
using Brickcraft.World;
using NUnit.Framework;

namespace Brickcraft.Tests
{
    /// <summary>The game's items as loaded from StreamingAssets/Items, and their colour rules.</summary>
    public class ItemTests
    {
        private const int Red = 4, TransDarkBlue = 41, TransClear = 47;

        [SetUp]
        public void SetUp() {
            GameData.Load();
        }

        private static Item item(string id) {
            Assert.IsTrue(Server.items.TryGetValue(id, out Item found), id + " isn't loaded");
            return found;
        }

        [Test]
        public void ToolsDigFasterAndWear() {
            Item pickaxe = item("pickaxe");
            Assert.IsTrue(pickaxe.Digs);
            Assert.AreEqual(3, pickaxe.digSpeed);
            Assert.IsTrue(pickaxe.Wears);

            Assert.IsFalse(item("stone").Digs);
            Assert.IsFalse(item("stone").Wears);
        }

        [Test]
        public void TorchesGiveLightAndWashAway() {
            Item torch = item("torch");
            Assert.IsNotNull(torch.light);
            Assert.Greater(torch.light.range, 0);
            Assert.IsTrue(torch.tags.Contains("washable"));
        }

        [Test]
        public void BucketsAreResolved() {
            Item bucket = item("bucket"), full = item("water_bucket");
            Assert.AreEqual("water_bucket", bucket.bucket.filled);
            Assert.AreEqual("water", full.bucket.liquid);
            Assert.AreEqual("bucket", full.bucket.empty);
            Assert.IsTrue(item(full.bucket.liquid).blockType.HasValue, "it pours a block");
        }

        [Test]
        public void PotionsHeal() {
            Item potion = item("hp_potion");
            Assert.IsTrue(potion.Heals);
            Assert.AreEqual(10, potion.heal);
            Assert.IsNotNull(potion.brickModel, "the flask, LDraw's 93549");
            Assert.IsFalse(item("stone").Heals);
        }

        [Test]
        public void TheColorPickerTakesAnyColour() {
            Item picker = item("color_picker");
            Assert.IsTrue(picker.IsColorPicker);
            Assert.IsTrue(picker.AllowsColor(1));
            Assert.IsTrue(picker.AllowsColor(41));
            Assert.IsNotNull(picker.brickModel, "the paint palette, LDraw's 93551p01");
            Assert.IsFalse(item("pickaxe").IsColorPicker);
        }

        [Test]
        public void LavaHardensWithWater() {
            Fluid lava = GameData.Definition(GameData.Block("lava")).fluid;
            Fluid water = GameData.Definition(GameData.Block("water")).fluid;

            Assert.AreSame(water, lava.hardensWith);
            Assert.AreEqual("obsidian", lava.hardSource.name);
            Assert.AreEqual("cobblestone", lava.hardFlowing.name);
            Assert.AreEqual("stone", lava.hardBelow.name);
            Assert.AreEqual(3, lava.Reach);
            Assert.AreEqual(Fluid.MaxLevel, water.Reach);
        }

        [Test]
        public void GlassOnlyTakesTransparentColours() {
            Item glass = item("glass_2x2");
            Assert.AreEqual(TransDarkBlue, glass.color);
            Assert.IsFalse(glass.AllowsColor(Red));
            Assert.AreEqual(TransDarkBlue, glass.ValidColor(Red), "a solid colour falls back to its own");
            Assert.IsTrue(glass.AllowsColor(TransClear));
            Assert.AreEqual(TransClear, glass.ValidColor(TransClear));
        }

        [Test]
        public void WindowGlassFitsItsFrame() {
            LDrawPart frame = LDrawLibrary.CatalogPart("3761");
            LDrawPart glass = LDrawLibrary.CatalogPart("3762");
            Assume.That(frame != null && glass != null, "the catalogue has window 1x6x5 and its glass");

            Assert.AreEqual("glass:window 1x6x5", LDrawGlass.GlassFits(glass));
            Assert.AreEqual("glass:window 1x6x5", LDrawGlass.FrameFits(frame));
            Assert.IsNull(LDrawGlass.FrameFits(glass), "glass isn't a frame");
            Assert.IsNull(LDrawGlass.GlassFits(frame));
        }

        [Test]
        public void PanesArentFrames() {
            Assert.IsNull(LDrawGlass.FrameFits(new LDrawPart() { id = "test", name = "Window 1x6x5 Pane", category = "Window" }));
        }
    }
}
