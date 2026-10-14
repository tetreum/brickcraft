using System.IO;
using System.IO.Compression;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 10 to 11: chunk records keep the vehicles left in them, in a section at their end. Records go from
    /// version 9 to 10, which ends with a varint count of vehicles: 0 for the old ones.
    /// world.dat only changes its format number.
    /// </summary>
    public class Migration10To11 : SaveMigration
    {
        public override int From {
            get { return 10; }
        }

        public override string Description {
            get { return "vehicles"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 10) {
                throw new InvalidDataException("world.dat isn't format 10");
            }
            SetFormat(header, 11);
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
            if (content[0] == 10) {
                return record;
            }
            if (content[0] != 9) {
                throw new InvalidDataException("Unknown chunk record version " + content[0]);
            }

            // the same record, version 10, with no vehicles at its end
            using (MemoryStream output = new MemoryStream()) {
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                using (BinaryWriter writer = new BinaryWriter(deflate)) {
                    writer.Write((byte)10);
                    writer.Write(content, 1, content.Length - 1);
                    WriteVarUInt(writer, 0); // vehicles
                }
                return output.ToArray();
            }
        }
    }
}
