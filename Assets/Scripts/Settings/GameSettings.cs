using Brickcraft.Events;
using UnityEngine;

namespace Brickcraft
{
    public enum Difficulty
    {
        Peaceful = 0,
        Normal = 1,
        Hard = 2,
    }

    /// <summary>
    /// The player's preferences, kept in PlayerPrefs. Changing one raises EventManager.SettingChanged
    /// with its name (the constants below), so whatever depends on it can update.
    /// </summary>
    public static class GameSettings
    {
        public const string LanguageSetting = "language";
        public const string DifficultySetting = "difficulty";
        public const string AutoSaveSetting = "autoSave";
        public const string ShowCoordinatesSetting = "showCoordinates";
        public const string CrosshairSetting = "crosshair";
        public const string TutorialHintsSetting = "tutorialHints";

        private const string KeyPrefix = "settings.";

        /// <summary>Languages the game can be played in, as codes and names to show.</summary>
        public static readonly string[] LanguageCodes = { "en" };
        public static readonly string[] LanguageNames = { "English" };

        public static string Language {
            get { return getString(LanguageSetting, "en"); }
            set { setString(LanguageSetting, value); }
        }

        public static Difficulty Difficulty {
            get { return (Difficulty)Mathf.Clamp(getInt(DifficultySetting, (int)Difficulty.Normal), 0, 2); }
            set { setInt(DifficultySetting, (int)value); }
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
