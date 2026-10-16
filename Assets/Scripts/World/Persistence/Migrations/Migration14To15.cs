using System.IO;
using System.IO.Compression;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 14 to 15: chunk records keep which connected bricks are held on their holder's studs (a brick on a beam
    /// that's on an axle, see Bricks.BrickJoint.onStuds), in a section at their end. Records go from version 12 to 13,
    /// which ends with a varint count of them: 0 for the old ones. world.dat only changes its format number.
    /// </summary>
    public class Migration14To15 : SaveMigration
    {
        public override int From {
            get { return 14; }
        }

        public override string Description {
            get { return "technic: bricks on connected bricks' studs"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 14) {
                throw new InvalidDataException("world.dat isn't format 14");
            }
            SetFormat(header, 15);
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
            if (content[0] == 13) {
                return record;
            }
            if (content[0] != 12) {
                throw new InvalidDataException("Unknown chunk record version " + content[0]);
            }

            // the same record, version 13, with none on studs at its end
            using (MemoryStream output = new MemoryStream()) {
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                using (BinaryWriter writer = new BinaryWriter(deflate)) {
                    writer.Write((byte)13);
                    writer.Write(content, 1, content.Length - 1);
                    WriteVarUInt(writer, 0); // bricks on studs
                }
                return output.ToArray();
            }
        }
    }
}
