using System;

namespace Brickcraft.World
{
    /// <summary>
    /// The game's time: days since the world began, the fraction being the time of day (0 midnight, 0.25
    /// sunrise, 0.5 noon, 0.75 sunset). A day lasts DayLength real seconds (world.dat, 20 minutes by
    /// default). The server runs it and saves it with the world (see WorldStorage); clients get it from the
    /// server now and then and run it on their side in between (see WorldNetwork). It stops while it's
    /// paused: in singleplayer, while the game menu is open.
    /// </summary>
    public static class WorldTime
    {
        public const float DefaultDayLength = 20 * 60;
        /// <summary>Where new worlds start: 7 in the morning of their first day.</summary>
        public const double NewWorldTime = 7.0 / 24;

        /// <summary>Days since the world began.</summary>
        public static double Time { get; set; } = 0.5;
        /// <summary>Real seconds a day lasts.</summary>
        public static float DayLength { get; set; } = DefaultDayLength;
        public static bool IsPaused { get; set; }
        /// <summary>The game menu (ESCPanel) is open on this game instance: in singleplayer, time waits.</summary>
        public static bool IsGameMenuOpen { get; set; }

        /// <summary>From 0 (midnight) to 1, 0.5 being noon.</summary>
        public static float TimeOfDay {
            get { return (float)(Time - Math.Floor(Time)); }
        }

        /// <summary>The time of day in hours, from 0 to 24.</summary>
        public static float Hours {
            get { return TimeOfDay * 24; }
        }

        /// <summary>Which day it is, counting from 0.</summary>
        public static int Day {
            get { return (int)Math.Floor(Time); }
        }

        /// <summary>The sun is up (between sunrise and sunset).</summary>
        public static bool IsDay {
            get { return TimeOfDay >= 0.25f && TimeOfDay < 0.75f; }
        }

        /// <summary>Moves it on by that many real seconds, unless it's paused.</summary>
        public static void Advance(float seconds) {
            if (!IsPaused && DayLength > 0) {
                Time += seconds / DayLength;
            }
        }

        /// <summary>The same day at that hour (0 to 24), or the next one if it's already past it.</summary>
        public static double NextTimeAt(float hours) {
            double target = Math.Floor(Time) + Math.Max(0, Math.Min(24, hours)) / 24.0;
            return target < Time ? target + 1 : target;
        }

        /// <summary>"Day 3, 07:12".</summary>
        public static string Describe() {
            // to the nearest minute (20:00 set as days is a hair under it), the day it rounds into
            double rounded = Math.Round(Time * 24 * 60) / (24 * 60);
            int minutes = (int)Math.Round((rounded - Math.Floor(rounded)) * 24 * 60) % (24 * 60);
            return "Day " + ((int)Math.Floor(rounded) + 1) + ", " + (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }
    }
}
