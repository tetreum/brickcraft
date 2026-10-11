using System.IO;
using System.IO.Compression;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 6 to 7: chunk records keep the NPCs that were in the chunk. Records go from version 6 to 7,
    /// which ends with a varint NPC count after the bricks: 0 for the old ones. world.dat only changes its
    /// format number. (players.db gets a pets table, which the game creates when it opens it.)
    /// </summary>
    public class Migration6To7 : SaveMigration
    {
        public override int From {
            get { return 6; }
        }

        public override string Description {
            get { return "NPCs saved with their chunks, pets with their owners"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 6) {
                throw new InvalidDataException("world.dat isn't format 6");
            }
            SetFormat(header, 7);
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
            if (content[0] == 7) {
                return record;
            }
            if (content[0] != 6) {
                throw new InvalidDataException("Unknown chunk record version " + content[0]);
            }

            // the same record, version 7, with no NPCs at its end
            using (MemoryStream output = new MemoryStream()) {
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                using (BinaryWriter writer = new BinaryWriter(deflate)) {
                    writer.Write((byte)7);
                    writer.Write(content, 1, content.Length - 1);
                    WriteVarUInt(writer, 0);
                }
                return output.ToArray();
            }
        }
    }
}
