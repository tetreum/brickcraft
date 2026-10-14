using System.Collections;
using Brickcraft.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Brickcraft.Tests
{
    /// <summary>Texts of the interface in the player's language while playing (LocalizedText, Loc).</summary>
    public class LocalizedTextTests
    {
        private const string LanguageKey = "settings." + GameSettings.LanguageSetting;
        private string saved;
        private GameObject go;

        [SetUp]
        public void SetUp() {
            saved = PlayerPrefs.HasKey(LanguageKey) ? PlayerPrefs.GetString(LanguageKey) : null;
            GameSettings.Language = "en";
        }

        [TearDown]
        public void TearDown() {
            Object.Destroy(go);
            GameSettings.Language = saved ?? GameSettings.SystemLanguage;
            if (saved == null) {
                PlayerPrefs.DeleteKey(LanguageKey);
                PlayerPrefs.Save();
            }
        }

        [UnityTest]
        public IEnumerator ATextSaysItsKeyAndFollowsTheLanguage() {
            go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.SetActive(false);
            LocalizedText localized = go.AddComponent<LocalizedText>();
            localized.key = "settings.language";
            go.SetActive(true);
            Text text = go.GetComponent<Text>();
            yield return null;
            Assert.AreEqual("Language", text.text);

            GameSettings.Language = "es";
            yield return null;
            Assert.AreEqual("Idioma", text.text, "said again in Spanish");

            // hidden while it changes: said in it when it's shown again
            go.SetActive(false);
            GameSettings.Language = "en";
            go.SetActive(true);
            yield return null;
            Assert.AreEqual("Language", text.text);
        }
    }
}
