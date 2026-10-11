using System;
using System.IO;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 7 to 8: world.dat ends with how long a day lasts (a float, real seconds) and the game's time
    /// (a double, days since the world began), after PvP: 20 minutes and 7 in the morning of the first day for
    /// the old worlds, like new ones.
    /// </summary>
    public class Migration7To8 : SaveMigration
    {
        public override int From {
            get { return 7; }
        }

        public override string Description {
            get { return "day length and game time"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 7) {
                throw new InvalidDataException("world.dat isn't format 7");
            }
            byte[] dayLength = BitConverter.GetBytes(20f * 60f);
            byte[] time = BitConverter.GetBytes(7.0 / 24);
            byte[] upgraded = new byte[header.Length + dayLength.Length + time.Length];
            Array.Copy(header, upgraded, header.Length);
            Array.Copy(dayLength, 0, upgraded, header.Length, dayLength.Length);
            Array.Copy(time, 0, upgraded, header.Length + dayLength.Length, time.Length);
            SetFormat(upgraded, 8);
            WriteHeader(saveFolder, upgraded);
        }
    }
}
