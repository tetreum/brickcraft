using System.Collections.Generic;
using Brickcraft.Events;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace Brickcraft
{
    /// <summary>
    /// The game's texts in the player's language (com.unity.localization: the locales and string tables of
    /// Assets/Localization, loaded with their locale). The language is GameSettings.Language: it's applied when the game
    /// starts and whenever it changes, and once its texts are loaded EventManager.LanguageChanged is raised, so what
    /// shows text says it again.
    ///
    /// Texts are read by table and key (Loc.Get(Loc.UI, "settings.language")), with arguments for those that have them
    /// ({0}, or Smart Strings' {name} and plurals). A key the table hasn't is shown as itself, and warned about once.
    /// </summary>
    public static class Loc
    {
        /// <summary>Menus and panels.</summary>
        public const string UI = "UI";
        /// <summary>What happens while playing: chat lines, the server's messages, death and sleep screens.</summary>
        public const string Game = "Game";
        /// <summary>Actions and keys (see GameInput).</summary>
        public const string Controls = "Controls";
        /// <summary>Items', blocks' and NPCs' names, by id.</summary>
        public const string Items = "Items";

        private const string DefaultLanguage = "en";

        private static bool started;
        private static string current;
        private static readonly HashSet<string> warned = new HashSet<string>();

        /// <summary>The language the texts are in, like "es".</summary>
        public static string Language {
            get {
                start();
                return current;
            }
        }

        // every play session (EventManager's subscribers are cleared when one starts, even without a domain reload)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void init() {
            started = false;
            current = null;
            start();
        }

        private static void start() {
            if (started) {
                return;
            }
            started = true;
            EventManager.SettingChanged.Unsubscribe(onSettingChanged);
            EventManager.SettingChanged.Subscribe(onSettingChanged);
            apply(GameSettings.Language);
        }

        private static void onSettingChanged(SettingChangedEvent e) {
            if (e.setting == GameSettings.LanguageSetting) {
                apply(GameSettings.Language);
            }
        }

        // its locale selected and its tables loaded (they're small and preloaded with it), then everyone told
        private static void apply(string language) {
            LocalizationSettings.InitializationOperation.WaitForCompletion();
            Locale locale = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(language))
                ?? LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(DefaultLanguage));
            if (locale == null) {
                Debug.LogError("There's no locale for " + language + " nor " + DefaultLanguage + " in the localization settings");
                return;
            }
            string code = locale.Identifier.Code;
            if (code == current && LocalizationSettings.SelectedLocale == locale) {
                return;
            }
            LocalizationSettings.SelectedLocale = locale;
            LocalizationSettings.InitializationOperation.WaitForCompletion();
            current = code;
            EventManager.LanguageChanged.Raise(new LanguageChangedEvent() { language = code });
        }

        /// <summary>There's a locale for that language (see GameSettings.LanguageCodes).</summary>
        public static bool HasLanguage(string language) {
            LocalizationSettings.InitializationOperation.WaitForCompletion();
            return LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(language)) != null;
        }

        /// <summary>The text of a key in the player's language (English if it isn't translated yet).</summary>
        public static string Get(string table, string key) {
            return get(table, key, null);
        }

        /// <summary>The text of a key with its arguments ({0}..., or Smart Strings' names and plurals).</summary>
        public static string Get(string table, string key, params object[] arguments) {
            return get(table, key, arguments);
        }

        /// <summary>The key is in that table.</summary>
        public static bool Has(string table, string key) {
            start();
            StringTable strings = LocalizationSettings.StringDatabase.GetTable(table);
            return strings != null && strings.SharedData.GetEntry(key) != null;
        }

        /// <summary>That language's table has the key, with a text (it isn't left to fall back on English).</summary>
        public static bool Has(string table, string key, string language) {
            return !string.IsNullOrEmpty(Template(table, key, language));
        }

        /// <summary>A key's text as it's written in that language's table, its placeholders unfilled; null if it has none.</summary>
        public static string Template(string table, string key, string language) {
            start();
            Locale locale = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(language));
            StringTable strings = locale != null ? LocalizationSettings.StringDatabase.GetTable(table, locale) : null;
            StringTableEntry entry = strings != null ? strings.GetEntry(key) : null;
            return entry != null ? entry.Value : null;
        }

        private static string get(string table, string key, object[] arguments) {
            if (!Has(table, key)) {
                if (warned.Add(table + "/" + key)) {
                    Debug.LogWarning("The " + table + " table has no " + key);
                }
                return key;
            }
            return arguments != null && arguments.Length > 0
                ? LocalizationSettings.StringDatabase.GetLocalizedString(table, key, arguments)
                : LocalizationSettings.StringDatabase.GetLocalizedString(table, key);
        }
    }
}
