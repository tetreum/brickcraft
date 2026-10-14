using System.Collections.Generic;
using System.IO;
using Brickcraft.Mods;
using Newtonsoft.Json;
using UnityEngine;

namespace Brickcraft
{
    /// <summary>
    /// Names of items and NPCs in the player's language: the game's from the Items string table (item.[id],
    /// npc.[id]), mods' from their lang/[language].json (item.[id], npc.[id], their ids without the mod's prefix; see
    /// ModTexts), and their English name (info.json's) when there's no translation. LDraw parts keep their names,
    /// LEGO's (like brick colours).
    /// </summary>
    public static class Names
    {
        public static string Item(string id, string english) {
            return of(id, "item.", english);
        }

        public static string Npc(string id, string english) {
            return of(id, "npc.", english);
        }

        private static string of(string id, string kind, string english) {
            if (string.IsNullOrEmpty(id)) {
                return english;
            }
            int colon = id.IndexOf(':');
            if (colon > 0) {
                return ModTexts.Get(id.Substring(0, colon), kind + id.Substring(colon + 1)) ?? english;
            }
            return Loc.Has(Loc.Items, kind + id) ? Loc.Get(Loc.Items, kind + id) : english;
        }
    }

    /// <summary>
    /// The texts of the active mods in each language: Mods/[mod]/lang/[language].json, { "key": "text" } (English's for
    /// what a language hasn't). Read the first time they're needed, forgotten when other mods are activated.
    /// </summary>
    public static class ModTexts
    {
        public const string Folder = "lang";
        private const string DefaultLanguage = "en";

        private static readonly Dictionary<string, Dictionary<string, string>> files = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>A mod's text in that language (the player's by default), null if it has none.</summary>
        public static string Get(string mod, string key, string language = null) {
            language = string.IsNullOrEmpty(language) ? Loc.Language : language;
            if (textsOf(mod, language).TryGetValue(key, out string text)) {
                return text;
            }
            return language != DefaultLanguage && textsOf(mod, DefaultLanguage).TryGetValue(key, out text) ? text : null;
        }

        /// <summary>Mods changed (see ModDatabase.Activate): their files are read again.</summary>
        public static void Clear() {
            lock (files) {
                files.Clear();
            }
        }

        private static Dictionary<string, string> textsOf(string mod, string language) {
            string id = mod + "/" + language;
            lock (files) {
                if (files.TryGetValue(id, out Dictionary<string, string> texts)) {
                    return texts;
                }
                texts = read(mod, language);
                files[id] = texts;
                return texts;
            }
        }

        private static Dictionary<string, string> read(string mod, string language) {
            ModInfo info = null;
            foreach (ModInfo active in ModDatabase.Active) {
                if (active.id == mod) {
                    info = active;
                }
            }
            string path = info != null ? Path.Combine(info.folder, Folder, language + ".json") : null;
            if (path == null || !File.Exists(path)) {
                return new Dictionary<string, string>();
            }
            try {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path)) ?? new Dictionary<string, string>();
            } catch (System.Exception e) {
                Debug.LogWarning("The mod " + mod + "'s " + Folder + "/" + language + ".json can't be read: " + e.Message);
                return new Dictionary<string, string>();
            }
        }
    }
}
