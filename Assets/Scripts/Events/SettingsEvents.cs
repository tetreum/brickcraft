namespace Brickcraft.Events
{
    public sealed class SettingChangedEvent
    {
        /// <summary>Which one, see the constants of GameSettings.</summary>
        public string setting;
    }

    public sealed class LanguageChangedEvent
    {
        /// <summary>Its code, like "es" (see GameSettings.LanguageCodes).</summary>
        public string language;
    }
}
