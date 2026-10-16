using System.IO;
using System.IO.Compression;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 16 to 17: chunk records keep what's written on signs (see Bricks.SignText), in a section at their end.
    /// Records go from version 13 to 14, which ends with a varint count of them: 0 for the old ones. world.dat only
    /// changes its format number.
    /// </summary>
    public class Migration16To17 : SaveMigration
    {
        public override int From {
            get { return 16; }
        }

        public override string Description {
            get { return "signs"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 16) {
                throw new InvalidDataException("world.dat isn't format 16");
            }
            SetFormat(header, 17);
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
            if (content[0] == 14) {
                return record;
            }
            if (content[0] != 13) {
                throw new InvalidDataException("Unknown chunk record version " + content[0]);
            }

            // the same record, version 14, with no sign texts at its end
            using (MemoryStream output = new MemoryStream()) {
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                using (BinaryWriter writer = new BinaryWriter(deflate)) {
                    writer.Write((byte)14);
                    writer.Write(content, 1, content.Length - 1);
                    WriteVarUInt(writer, 0); // sign texts
                }
                return output.ToArray();
            }
        }
    }
}
