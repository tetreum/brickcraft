using System;
using System.IO;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 5 to 6: world.dat ends with whether players can hurt each other (a bool, after the mods):
    /// on for the old worlds, like new ones.
    /// </summary>
    public class Migration5To6 : SaveMigration
    {
        public override int From {
            get { return 5; }
        }

        public override string Description {
            get { return "PvP setting"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 5) {
                throw new InvalidDataException("world.dat isn't format 5");
            }
            byte[] upgraded = new byte[header.Length + 1];
            Array.Copy(header, upgraded, header.Length);
            upgraded[header.Length] = 1;
            SetFormat(upgraded, 6);
            WriteHeader(saveFolder, upgraded);
        }
    }
}
