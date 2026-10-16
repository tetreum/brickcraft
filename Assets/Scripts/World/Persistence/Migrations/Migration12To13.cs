using System.IO;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 12 to 13: world.dat ends with whether NPCs appear by themselves (see WorldStorage.NpcsOn): they did in the
    /// old worlds. Chunk records don't change.
    /// </summary>
    public class Migration12To13 : SaveMigration
    {
        public override int From {
            get { return 12; }
        }

        public override string Description {
            get { return "turning NPCs off"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 12) {
                throw new InvalidDataException("world.dat isn't format 12");
            }
            SetFormat(header, 13);
            byte[] upgraded = new byte[header.Length + 1];
            header.CopyTo(upgraded, 0);
            upgraded[header.Length] = 1; // NPCs on
            WriteHeader(saveFolder, upgraded);
        }
    }
}
