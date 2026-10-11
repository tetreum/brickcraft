using System.IO;
using System.IO.Compression;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 8 to 9: NPCs saved in chunk records also keep whether they appeared by themselves, their home and
    /// their bed, in a section after them. Records go from version 7 to 8, which ends with a varint count of
    /// those (0 for the old ones: no home, not natural). world.dat only changes its format number.
    /// </summary>
    public class Migration8To9 : SaveMigration
    {
        public override int From {
            get { return 8; }
        }

        public override string Description {
            get { return "NPC homes, beds and natural spawns"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 8) {
                throw new InvalidDataException("world.dat isn't format 8");
            }
            SetFormat(header, 9);
            WriteHeader(saveFolder, header);

            RewriteChunkRecords(saveFolder, upgradeRecord);
        }

        private static byte[] upgradeRecord(byte[] record) {
            byte[] content;
            using (MemoryStream compressed = new MemoryStream(record))
            using (DeflateStream inflate = new DeflateStream(compressed, CompressionMode.Decompress))
            using (MemoryStream inflated = new MemoryStream()) {
                inflate.CopyTo(inflated);
                content = inflated.ToArray();
            }
            if (content.Length == 0) {
                throw new InvalidDataException("Empty chunk record");
            }
            if (content[0] == 8) {
                return record;
            }
            if (content[0] != 7) {
                throw new InvalidDataException("Unknown chunk record version " + content[0]);
            }

            // the same record, version 8, with no NPC extras at its end
            using (MemoryStream output = new MemoryStream()) {
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                using (BinaryWriter writer = new BinaryWriter(deflate)) {
                    writer.Write((byte)8);
                    writer.Write(content, 1, content.Length - 1);
                    WriteVarUInt(writer, 0);
                }
                return output.ToArray();
            }
        }
    }
}
