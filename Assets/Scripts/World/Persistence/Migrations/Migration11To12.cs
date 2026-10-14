using System.IO;
using System.IO.Compression;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 11 to 12: chunk records keep the wires of the vehicles in them (see Vehicles.VehicleWire), in a section
    /// at their end. Records go from version 10 to 11, which ends with a varint count of wire lists: 0 for the old ones.
    /// world.dat only changes its format number.
    /// </summary>
    public class Migration11To12 : SaveMigration
    {
        public override int From {
            get { return 11; }
        }

        public override string Description {
            get { return "vehicles' wires"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 11) {
                throw new InvalidDataException("world.dat isn't format 11");
            }
            SetFormat(header, 12);
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
            if (content[0] == 11) {
                return record;
            }
            if (content[0] != 10) {
                throw new InvalidDataException("Unknown chunk record version " + content[0]);
            }

            // the same record, version 11, with no wires at its end
            using (MemoryStream output = new MemoryStream()) {
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                using (BinaryWriter writer = new BinaryWriter(deflate)) {
                    writer.Write((byte)11);
                    writer.Write(content, 1, content.Length - 1);
                    WriteVarUInt(writer, 0); // the vehicles' wires
                }
                return output.ToArray();
            }
        }
    }
}
