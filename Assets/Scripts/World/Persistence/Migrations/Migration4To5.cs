using System.IO;
using System.IO.Compression;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 4 to 5: bricks have a state (Brick.state, like a door being open) and can be attached to
    /// another (Brick.attachedTo, a door in its frame). Chunk records go from version 5 to 6, which writes
    /// after each brick's placer a varint state and a byte saying whether the id (16 bytes) of the brick
    /// it's attached to follows: 0 and none for the old ones. world.dat only changes its format number.
    /// </summary>
    public class Migration4To5 : SaveMigration
    {
        public override int From {
            get { return 4; }
        }

        public override string Description {
            get { return "brick states (doors open or closed) and attachments (doors in frames)"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 4) {
                throw new InvalidDataException("world.dat isn't format 4");
            }
            SetFormat(header, 5);
            WriteHeader(saveFolder, header);

            RewriteChunkRecords(saveFolder, upgradeRecord);
        }

        private static byte[] upgradeRecord(byte[] record) {
            using (MemoryStream compressed = new MemoryStream(record))
            using (DeflateStream inflate = new DeflateStream(compressed, CompressionMode.Decompress))
            using (BinaryReader reader = new BinaryReader(inflate))
            using (MemoryStream output = new MemoryStream()) {
                byte version = reader.ReadByte();
                if (version == 6) {
                    return record;
                }
                if (version != 5) {
                    throw new InvalidDataException("Unknown chunk record version " + version);
                }
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                using (BinaryWriter writer = new BinaryWriter(deflate)) {
                    writer.Write((byte)6);

                    uint names = ReadVarUInt(reader);
                    WriteVarUInt(writer, names);
                    for (uint i = 0; i < names; i++) {
                        writer.Write(reader.ReadString());
                    }

                    // blocks: index delta, name, colour, placer
                    uint blocks = ReadVarUInt(reader);
                    WriteVarUInt(writer, blocks);
                    for (uint i = 0; i < blocks; i++) {
                        WriteVarUInt(writer, ReadVarUInt(reader));
                        WriteVarUInt(writer, ReadVarUInt(reader));
                        WriteVarInt(writer, ReadVarInt(reader));
                        copyPlacer(reader, writer);
                    }

                    // bricks: id, item, colour, x y z, rotation, placer, and now their state and what they're attached to
                    uint bricks = ReadVarUInt(reader);
                    WriteVarUInt(writer, bricks);
                    for (uint i = 0; i < bricks; i++) {
                        writer.Write(reader.ReadBytes(16));
                        writer.Write(reader.ReadString());
                        for (int k = 0; k < 4; k++) {
                            WriteVarInt(writer, ReadVarInt(reader));
                        }
                        writer.Write(reader.ReadByte());
                        copyPlacer(reader, writer);
                        WriteVarInt(writer, 0);
                        writer.Write((byte)0);
                    }
                }
                return output.ToArray();
            }
        }

        private static void copyPlacer(BinaryReader reader, BinaryWriter writer) {
            ReadPlacer(reader, out int playerId, out ulong placedAt);
            WritePlacer(writer, playerId, placedAt);
        }
    }
}
