using System.IO;
using System.IO.Compression;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 13 to 14: chunk records keep how connected bricks are held (an axle through a hole, see
    /// Bricks.BrickJoints), world bricks' and vehicle parts', in two sections at their end. Records go from version 11 to
    /// 12, which ends with a varint count of each: 0 and 0 for the old ones. world.dat only changes its format number.
    /// </summary>
    public class Migration13To14 : SaveMigration
    {
        public override int From {
            get { return 13; }
        }

        public override string Description {
            get { return "technic: connected bricks"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 13) {
                throw new InvalidDataException("world.dat isn't format 13");
            }
            SetFormat(header, 14);
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
            if (content[0] == 12) {
                return record;
            }
            if (content[0] != 11) {
                throw new InvalidDataException("Unknown chunk record version " + content[0]);
            }

            // the same record, version 12, with no connected bricks at its end
            using (MemoryStream output = new MemoryStream()) {
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                using (BinaryWriter writer = new BinaryWriter(deflate)) {
                    writer.Write((byte)12);
                    writer.Write(content, 1, content.Length - 1);
                    WriteVarUInt(writer, 0); // connected bricks
                    WriteVarUInt(writer, 0); // connected vehicle parts
                }
                return output.ToArray();
            }
        }
    }
}
