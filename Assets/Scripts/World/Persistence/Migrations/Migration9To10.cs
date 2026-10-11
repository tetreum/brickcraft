using System.IO;
using System.IO.Compression;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 9 to 10: chunk records keep what storage bricks (chests) have in them and the items lying on the
    /// ground, in two sections at their end. Records go from version 8 to 9, which ends with a varint count of
    /// storage bricks and one of drops: 0 and 0 for the old ones.
    /// world.dat only changes its format number.
    /// </summary>
    public class Migration9To10 : SaveMigration
    {
        public override int From {
            get { return 9; }
        }

        public override string Description {
            get { return "what chests keep, and items on the ground"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 9) {
                throw new InvalidDataException("world.dat isn't format 9");
            }
            SetFormat(header, 10);
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
            if (content[0] == 9) {
                return record;
            }
            if (content[0] != 8) {
                throw new InvalidDataException("Unknown chunk record version " + content[0]);
            }

            // the same record, version 9, with nothing stored at its end
            using (MemoryStream output = new MemoryStream()) {
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                using (BinaryWriter writer = new BinaryWriter(deflate)) {
                    writer.Write((byte)9);
                    writer.Write(content, 1, content.Length - 1);
                    WriteVarUInt(writer, 0); // storage
                    WriteVarUInt(writer, 0); // drops
                }
                return output.ToArray();
            }
        }
    }
}
