using System.IO;
using Brickcraft.Network;
using Brickcraft.Npcs;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>Items' and NPCs' names in the player's language (Names); mods' are tested while playing (ModTextTests).</summary>
    public class NameTests
    {
        private const string LanguageKey = "settings." + GameSettings.LanguageSetting;
        private string saved;

        [SetUp]
        public void SetUp() {
            saved = PlayerPrefs.HasKey(LanguageKey) ? PlayerPrefs.GetString(LanguageKey) : null;
            GameData.Load();
        }

        [TearDown]
        public void TearDown() {
            GameSettings.Language = saved ?? GameSettings.SystemLanguage;
            if (saved == null) {
                PlayerPrefs.DeleteKey(LanguageKey);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void ItemsAreNamedInThePlayersLanguage() {
            GameSettings.Language = "en";
            Assert.AreEqual("Stone 2x2", Server.items["stone"].DisplayName);
            GameSettings.Language = "es";
            Assert.AreEqual("Piedra 2x2", Server.items["stone"].DisplayName);
            // LDraw's parts keep LEGO's names
            Assert.AreEqual(Server.items["part_3001"].name, Server.items["part_3001"].DisplayName);
        }

        [Test]
        public void EveryItemAndNpcOfTheGameHasItsNameInEveryLanguage() {
            foreach (string folder in Directory.GetDirectories(Path.Combine(Application.streamingAssetsPath, "Items"))) {
                string id = Path.GetFileName(folder);
                foreach (string language in GameSettings.LanguageCodes) {
                    Assert.IsTrue(Loc.Has(Loc.Items, "item." + id, language), id + " in " + language);
                }
            }
            foreach (string folder in Directory.GetDirectories(Path.Combine(Application.streamingAssetsPath, "NPCs"))) {
                string id = Path.GetFileName(folder);
                foreach (string language in GameSettings.LanguageCodes) {
                    Assert.IsTrue(Loc.Has(Loc.Items, "npc." + id, language), id + " in " + language);
                }
            }
        }

        [Test]
        public void AnNpcSentByItsIdIsNamedByTheReader() {
            string killer = GameText.Reference(NpcDatabase.Get("golem"));
            GameSettings.Language = "es";
            Assert.AreEqual("Gólem", GameText.Resolve(killer));
            Assert.AreEqual("Ana", GameText.Resolve("Ana"), "a player's name as it is");
        }
    }
}
