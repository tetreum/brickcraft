using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Format 3 to 4: world.dat gets the world's mods (none), and chunk records, which went through three
    /// layouts while saves were format 3, all get the last one (record version 5):
    ///   3: blocks by name, bricks (id, item, x y z, rotation)
    ///   4: + who placed each block and brick, and when
    ///   5: + the colour of each block and brick
    /// Old blocks and bricks get no colour (-1: blocks keep their textures, bricks their item's default)
    /// and, coming from record version 3, nobody as who placed them.
    /// </summary>
    public class Migration3To4 : SaveMigration
    {
        public override int From {
            get { return 3; }
        }

        public override string Description {
            get { return "world mods, and chunk records with who placed blocks and bricks and their colours"; }
        }

        public override void Migrate(string saveFolder) {
            byte[] header = ReadHeader(saveFolder);
            if (FormatOf(header) != 3) {
                throw new InvalidDataException("world.dat isn't format 3");
            }
            // format 4 appends the mods: a u16 count, none here
            byte[] upgraded = new byte[header.Length + 2];
            Array.Copy(header, upgraded, header.Length);
            SetFormat(upgraded, 4);
            WriteHeader(saveFolder, upgraded);

            RewriteChunkRecords(saveFolder, upgradeRecord);
        }

        private struct Block
        {
            public int index;
            public int name;
            public int color;
            public int placerId;
            public ulong placedAt;
        }

        private struct Brick
        {
            public byte[] id;
            public string item;
            public int color;
            public int x, y, z;
            public byte rotation;
            public int placerId;
            public ulong placedAt;
        }

        private static byte[] upgradeRecord(byte[] record) {
            List<string> names = new List<string>();
            List<Block> blocks = new List<Block>();
            List<Brick> bricks = new List<Brick>();

            using (MemoryStream compressed = new MemoryStream(record))
            using (DeflateStream deflate = new DeflateStream(compressed, CompressionMode.Decompress))
            using (BinaryReader reader = new BinaryReader(deflate)) {
                byte version = reader.ReadByte();
                if (version == 5) {
                    return record; // already the last layout
                }
                if (version != 3 && version != 4) {
                    throw new InvalidDataException("Unknown chunk record version " + version);
                }
                int nameCount = (int)readVarUInt(reader);
                for (int i = 0; i < nameCount; i++) {
                    names.Add(reader.ReadString());
                }
                int blockCount = (int)readVarUInt(reader);
                int index = 0;
                for (int i = 0; i < blockCount; i++) {
                    index += (int)readVarUInt(reader);
                    Block block = new Block() { index = index, name = (int)readVarUInt(reader), color = -1 };
                    if (version >= 4) {
                        readPlacer(reader, out block.placerId, out block.placedAt);
                    }
                    blocks.Add(block);
                }
                int brickCount = (int)readVarUInt(reader);
                for (int i = 0; i < brickCount; i++) {
                    Brick brick = new Brick() {
                        id = reader.ReadBytes(16),
                        item = reader.ReadString(),
                        color = -1,
                    };
                    brick.x = readVarInt(reader);
                    brick.y = readVarInt(reader);
                    brick.z = readVarInt(reader);
                    brick.rotation = reader.ReadByte();
                    if (version >= 4) {
                        readPlacer(reader, out brick.placerId, out brick.placedAt);
                    }
                    bricks.Add(brick);
                }
            }

            using (MemoryStream output = new MemoryStream()) {
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                using (BinaryWriter writer = new BinaryWriter(deflate)) {
                    writer.Write((byte)5);
                    writeVarUInt(writer, (uint)names.Count);
                    foreach (string name in names) {
                        writer.Write(name);
                    }
                    writeVarUInt(writer, (uint)blocks.Count);
                    int previous = 0;
                    foreach (Block block in blocks) {
                        writeVarUInt(writer, (uint)(block.index - previous));
                        writeVarUInt(writer, (uint)block.name);
                        writeVarInt(writer, block.color);
                        writePlacer(writer, block.placerId, block.placedAt);
                        previous = block.index;
                    }
                    writeVarUInt(writer, (uint)bricks.Count);
                    foreach (Brick brick in bricks) {
                        writer.Write(brick.id);
                        writer.Write(brick.item);
                        writeVarInt(writer, brick.color);
                        writeVarInt(writer, brick.x);
                        writeVarInt(writer, brick.y);
                        writeVarInt(writer, brick.z);
                        writer.Write(brick.rotation);
                        writePlacer(writer, brick.placerId, brick.placedAt);
                    }
                }
                return output.ToArray();
            }
        }

        // varint player id, followed (when not 0) by varint unix seconds
        private static void readPlacer(BinaryReader reader, out int playerId, out ulong placedAt) {
            playerId = (int)readVarUInt(reader);
            placedAt = playerId != 0 ? readVarULong(reader) : 0;
        }

        private static void writePlacer(BinaryWriter writer, int playerId, ulong placedAt) {
            writeVarUInt(writer, (uint)playerId);
            if (playerId != 0) {
                writeVarULong(writer, placedAt);
            }
        }

        private static uint readVarUInt(BinaryReader reader) {
            return (uint)readVarULong(reader);
        }

        private static ulong readVarULong(BinaryReader reader) {
            ulong value = 0;
            int shift = 0;
            byte b;
            do {
                if (shift > 63) {
                    throw new InvalidDataException("Invalid varint");
                }
                b = reader.ReadByte();
                value |= (ulong)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            return value;
        }

        private static void writeVarUInt(BinaryWriter writer, uint value) {
            writeVarULong(writer, value);
        }

        private static void writeVarULong(BinaryWriter writer, ulong value) {
            while (value >= 0x80) {
                writer.Write((byte)(value | 0x80));
                value >>= 7;
            }
            writer.Write((byte)value);
        }

        private static int readVarInt(BinaryReader reader) {
            uint value = readVarUInt(reader);
            return (int)(value >> 1) ^ -(int)(value & 1);
        }

        private static void writeVarInt(BinaryWriter writer, int value) {
            writeVarUInt(writer, (uint)((value << 1) ^ (value >> 31)));
        }
    }
}
