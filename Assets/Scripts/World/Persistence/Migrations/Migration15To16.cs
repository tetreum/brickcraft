using System.IO;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 15 to 16: world.dat ends with whether its terrain is flat and the tutorial it's for (see
    /// WorldStorage.Flat, Tutorial): neither, for the old worlds. Chunk records don't change.
    /// </summary>
    public class Migration15To16 : SaveMigration
    {
        public override int From {
            get { return 15; }
        }

        public override string Description {
            get { return "flat worlds and tutorials"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 15) {
                throw new InvalidDataException("world.dat isn't format 15");
            }
            SetFormat(header, 16);
            byte[] upgraded = new byte[header.Length + 2];
            header.CopyTo(upgraded, 0);
            upgraded[header.Length] = 0; // not flat
            upgraded[header.Length + 1] = 0; // no tutorial: an empty string
            WriteHeader(saveFolder, upgraded);
        }
    }
}
