using Brickcraft.Mods;
using Brickcraft.Scripting;
using MoonSharp.Interpreter;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>Mods' names and texts in each language (their lang/[language].json, see ModTexts), with Mods/example.</summary>
    public class ModTextTests
    {
        private const string LanguageKey = "settings." + GameSettings.LanguageSetting;
        private string saved;

        [SetUp]
        public void SetUp() {
            saved = PlayerPrefs.HasKey(LanguageKey) ? PlayerPrefs.GetString(LanguageKey) : null;
            Assume.That(ModDatabase.Find("example"), Is.Not.Null, "the example mod is in Mods/");
            ModDatabase.Activate(new[] { "example" });
        }

        [TearDown]
        public void TearDown() {
            ModDatabase.Activate(new string[0]);
            GameSettings.Language = saved ?? GameSettings.SystemLanguage;
            if (saved == null) {
                PlayerPrefs.DeleteKey(LanguageKey);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void ModsNameTheirItemsAndNpcs() {
            GameSettings.Language = "es";
            Assert.AreEqual("Lanzador", Server.items["example:launcher"].DisplayName);
            Assert.AreEqual("Gólem de musgo", Npcs.NpcDatabase.Get("example:moss_golem").DisplayName);
            GameSettings.Language = "en";
            Assert.AreEqual("Launcher", Server.items["example:launcher"].DisplayName);
        }

        [Test]
        public void ScriptsSayTheModsTextsInAPlayersLanguage() {
            Script script = new Script(CoreModules.Preset_SoftSandbox);
            LuaApi.Install(script, ModDatabase.Find("example"));
            Assert.AreEqual("Cae la noche del día 3. Hay gólems por ahí.", script.DoString("return text('es', 'night', 3)").String);
            Assert.AreEqual("Night is falling on day 3. Golems are about.", script.DoString("return text('fr', 'night', 3)").String, "English for a language it hasn't");
            Assert.AreEqual("no.such.text", script.DoString("return text('es', 'no.such.text')").String);
        }
    }
}
