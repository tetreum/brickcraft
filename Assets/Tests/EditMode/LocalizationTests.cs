using Brickcraft.Events;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Brickcraft.Tests
{
    /// <summary>The game's texts in the player's language (Loc): the language setting picks them.</summary>
    public class LocalizationTests
    {
        private const string LanguageKey = "settings." + GameSettings.LanguageSetting;
        private string saved;

        [SetUp]
        public void SetUp() {
            saved = PlayerPrefs.HasKey(LanguageKey) ? PlayerPrefs.GetString(LanguageKey) : null;
        }

        // the player's own choice back, or none
        [TearDown]
        public void TearDown() {
            GameSettings.Language = saved ?? GameSettings.SystemLanguage;
            if (saved == null) {
                PlayerPrefs.DeleteKey(LanguageKey);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void EveryLanguageOfTheSettingsHasALocale() {
            Assert.AreEqual(GameSettings.LanguageCodes.Length, GameSettings.LanguageNames.Length);
            foreach (string language in GameSettings.LanguageCodes) {
                Assert.IsTrue(Loc.HasLanguage(language), language);
            }
        }

        [Test]
        public void TheLanguageSettingPicksTheTexts() {
            string announced = null;
            System.Action<LanguageChangedEvent> changed = e => announced = e.language;
            EventManager.LanguageChanged.Subscribe(changed);
            try {
                GameSettings.Language = "en";
                Assert.AreEqual("en", Loc.Language);
                Assert.AreEqual("Language", Loc.Get(Loc.UI, "settings.language"));

                GameSettings.Language = "es";
                Assert.AreEqual("es", Loc.Language);
                Assert.AreEqual("es", announced, "told once its texts are loaded");
                Assert.AreEqual("Idioma", Loc.Get(Loc.UI, "settings.language"));
            } finally {
                EventManager.LanguageChanged.Unsubscribe(changed);
            }
        }

        [Test]
        public void AMissingKeyShowsItself() {
            LogAssert.Expect(LogType.Warning, "The UI table has no no.such.key");
            Assert.AreEqual("no.such.key", Loc.Get(Loc.UI, "no.such.key"));
            Assert.IsFalse(Loc.Has(Loc.UI, "no.such.key"));
        }
    }
}
