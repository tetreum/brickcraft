using Brickcraft.Network;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>What the server tells players, worded by each in its language (GameText, the Game string table).</summary>
    public class GameTextTests
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
        public void EachPlayerReadsItInItsLanguage() {
            GameText text = GameText.Of("cmd.pvp_is", GameText.Of("word.on"));
            GameSettings.Language = "en";
            Assert.AreEqual("PvP is on", text.Say());
            GameSettings.Language = "es";
            Assert.AreEqual("El PvP está activado", text.Say(), "the nested word too");
        }

        [Test]
        public void NumbersStayNumbersForPlurals() {
            // as they travel: text
            GameText one = GameText.Of("cmd.removed_npcs", 1);
            GameText three = GameText.Of("cmd.removed_npcs", 3);
            GameSettings.Language = "en";
            Assert.AreEqual("Removed 1 NPC", one.Say());
            Assert.AreEqual("Removed 3 NPCs", three.Say());
            GameSettings.Language = "es";
            Assert.AreEqual("Se han quitado 3 NPCs", three.Say());
        }

        [Test]
        public void ItemsAreNamedByTheReader() {
            Item stone = Server.items["stone"];
            GameSettings.Language = "en";
            Assert.AreEqual("Your " + stone.DisplayName + " broke", GameText.Of("inventory.broke", stone).Say());
        }

        [Test]
        public void WhatPlayersTypeCantPassForATag() {
            GameText text = GameText.Of("cmd.pvp_is", "\u0001t:word.on");
            GameSettings.Language = "en";
            Assert.AreEqual("PvP is  t:word.on", text.Say(), "shown as typed (the tag character is a space)");
        }

        [Test]
        public void ChatEventsAreWordedByTheReader() {
            ChatEventMessage killed = new ChatEventMessage() { type = ChatEventType.Died, player = "Ana", by = "Golem" };
            GameSettings.Language = "en";
            Assert.AreEqual("Ana was killed by Golem", ChatEvents.Describe(killed));
            GameSettings.Language = "es";
            Assert.AreEqual("Golem ha matado a Ana", ChatEvents.Describe(killed));
        }
    }
}
