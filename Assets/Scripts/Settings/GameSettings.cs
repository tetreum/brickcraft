using Brickcraft.Events;
using UnityEngine;

namespace Brickcraft
{
    /// <summary>
    /// The player's preferences, kept in PlayerPrefs. Changing one raises EventManager.SettingChanged
    /// with its name (the constants below), so whatever depends on it can update.
    /// </summary>
    public static class GameSettings
    {
        public const string LanguageSetting = "language";
        public const string AutoSaveSetting = "autoSave";
        public const string ShowCoordinatesSetting = "showCoordinates";
        public const string CrosshairSetting = "crosshair";
        public const string TutorialHintsSetting = "tutorialHints";
        public const string MasterVolumeSetting = "masterVolume";
        public const string MusicVolumeSetting = "musicVolume";
        public const string EffectsVolumeSetting = "effectsVolume";

        private const string KeyPrefix = "settings.";

        /// <summary>Languages the game can be played in, as codes and names to show.</summary>
        public static readonly string[] LanguageCodes = { "en" };
        public static readonly string[] LanguageNames = { "English" };

        public static string Language {
            get { return getString(LanguageSetting, "en"); }
            set { setString(LanguageSetting, value); }
        }

        /// <summary>The host saves the world every now and then, not only when it stops.</summary>
        public static bool AutoSave {
            get { return getBool(AutoSaveSetting, true); }
            set { setBool(AutoSaveSetting, value); }
        }

        public static bool ShowCoordinates {
            get { return getBool(ShowCoordinatesSetting, false); }
            set { setBool(ShowCoordinatesSetting, value); }
        }

        public static bool Crosshair {
            get { return getBool(CrosshairSetting, true); }
            set { setBool(CrosshairSetting, value); }
        }

        public static bool TutorialHints {
            get { return getBool(TutorialHintsSetting, true); }
            set { setBool(TutorialHintsSetting, value); }
        }

        /// <summary>Volume of the whole game, from 0 to 1.</summary>
        public static float MasterVolume {
            get { return getVolume(MasterVolumeSetting, 1f); }
            set { setFloat(MasterVolumeSetting, Mathf.Clamp01(value)); }
        }

        /// <summary>Volume of the music, from 0 to 1, on top of the master volume.</summary>
        public static float MusicVolume {
            get { return getVolume(MusicVolumeSetting, 0.7f); }
            set { setFloat(MusicVolumeSetting, Mathf.Clamp01(value)); }
        }

        /// <summary>Volume of sound effects (digging, steps...), from 0 to 1, on top of the master volume.</summary>
        public static float EffectsVolume {
            get { return getVolume(EffectsVolumeSetting, 1f); }
            set { setFloat(EffectsVolumeSetting, Mathf.Clamp01(value)); }
        }

        private static float getVolume(string setting, float fallback) {
            return Mathf.Clamp01(PlayerPrefs.GetFloat(KeyPrefix + setting, fallback));
        }

        private static void setFloat(string setting, float value) {
            if (PlayerPrefs.HasKey(KeyPrefix + setting) && Mathf.Approximately(PlayerPrefs.GetFloat(KeyPrefix + setting), value)) {
                return;
            }
            PlayerPrefs.SetFloat(KeyPrefix + setting, value);
            changed(setting);
        }

        private static string getString(string setting, string fallback) {
            return PlayerPrefs.GetString(KeyPrefix + setting, fallback);
        }

        private static int getInt(string setting, int fallback) {
            return PlayerPrefs.GetInt(KeyPrefix + setting, fallback);
        }

        private static bool getBool(string setting, bool fallback) {
            return getInt(setting, fallback ? 1 : 0) != 0;
        }

        private static void setString(string setting, string value) {
            if (getString(setting, null) == value) {
                return;
            }
            PlayerPrefs.SetString(KeyPrefix + setting, value);
            changed(setting);
        }

        private static void setInt(string setting, int value) {
            if (PlayerPrefs.HasKey(KeyPrefix + setting) && getInt(setting, 0) == value) {
                return;
            }
            PlayerPrefs.SetInt(KeyPrefix + setting, value);
            changed(setting);
        }

        private static void setBool(string setting, bool value) {
            setInt(setting, value ? 1 : 0);
        }

        private static void changed(string setting) {
            PlayerPrefs.Save();
            EventManager.SettingChanged.Raise(new SettingChangedEvent() { setting = setting });
        }
    }
}
