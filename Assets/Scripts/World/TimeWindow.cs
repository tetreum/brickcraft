using System;
using System.Globalization;

namespace Brickcraft.World
{
    /// <summary>
    /// A part of the day, as info.json files and scripts write it: "day" (06:00 to 20:00), "night" (20:00 to
    /// 06:00), "any", or "HH:MM-HH:MM" (it can go past midnight, like "21:00-06:30"). See WorldTime.
    /// </summary>
    public struct TimeWindow
    {
        public const float NightStart = 20;
        public const float NightEnd = 6;

        /// <summary>Hours, 0 to 24.</summary>
        public float from;
        public float to;
        public bool always;

        public static readonly TimeWindow Always = new TimeWindow() { always = true };
        public static readonly TimeWindow Day = new TimeWindow() { from = NightEnd, to = NightStart };
        public static readonly TimeWindow Night = new TimeWindow() { from = NightStart, to = NightEnd };

        /// <summary>The window it says, Always for null or empty; false (and Always) if it can't be read.</summary>
        public static bool TryParse(string text, out TimeWindow window) {
            window = Always;
            if (string.IsNullOrWhiteSpace(text)) {
                return true;
            }
            switch (text.Trim().ToLowerInvariant()) {
                case "any":
                    return true;
                case "day":
                    window = Day;
                    return true;
                case "night":
                    window = Night;
                    return true;
            }
            string[] parts = text.Split('-');
            if (parts.Length != 2 || !TryParseHour(parts[0], out float from) || !TryParseHour(parts[1], out float to)) {
                return false;
            }
            window = new TimeWindow() { from = from, to = to };
            return true;
        }

        /// <summary>"HH:MM" (or "HH") in hours.</summary>
        public static bool TryParseHour(string text, out float hours) {
            hours = 0;
            string[] parts = text.Trim().Split(':');
            if (parts.Length < 1 || parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)) {
                return false;
            }
            int m = 0;
            if (parts.Length == 2 && !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out m)) {
                return false;
            }
            if (h < 0 || h > 24 || m < 0 || m > 59) {
                return false;
            }
            hours = h + m / 60f;
            return true;
        }

        /// <summary>That time of day (hours) is in it.</summary>
        public bool Contains(float hours) {
            if (always) {
                return true;
            }
            return from <= to ? hours >= from && hours < to : hours >= from || hours < to;
        }

        /// <summary>It's in it now (WorldTime.Hours).</summary>
        public bool IsNow {
            get { return Contains(WorldTime.Hours); }
        }

        public override string ToString() {
            return always ? "any" : hour(from) + "-" + hour(to);
        }

        private static string hour(float hours) {
            int minutes = (int)Math.Round(hours * 60);
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }
    }
}
