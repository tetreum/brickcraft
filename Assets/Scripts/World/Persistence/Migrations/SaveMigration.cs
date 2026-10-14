using System;
using System.IO;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Upgrades a saved world from one format (see WorldStorage.FormatVersion) to the next. Each
    /// migration is a file of this folder; SaveMigrations finds them and chains them, so a save
    /// several formats old goes through each one in turn.
    ///
    /// A migration works on the files of the save folder (world.dat, regions/, players.db) and has
    /// to keep working as the game changes: it reads and writes the formats it converts between with
    /// its own code, never with the game's current serializers.
    /// </summary>
    public abstract class SaveMigration
    {
        /// <summary>The format it upgrades from, to From + 1.</summary>
        public abstract int From { get; }

        /// <summary>What it changes, for the log.</summary>
        public abstract string Description { get; }

        public int To {
            get { return From + 1; }
        }

        /// <summary>Upgrades the save in the folder. Throwing aborts the upgrade (the save is restored).</summary>
        public abstract void Migrate(string saveFolder);

        // -------- helpers --------

        /// <summary>world.dat's bytes. Its first 6 are always the magic (u32) and the format (u16).</summary>
        protected static byte[] ReadHeader(string saveFolder) {
            return File.ReadAllBytes(Path.Combine(saveFolder, WorldStorage.HeaderFile));
        }

        protected static void WriteHeader(string saveFolder, byte[] header) {
            File.WriteAllBytes(Path.Combine(saveFolder, WorldStorage.HeaderFile), header);
        }

        /// <summary>The format written in world.dat's bytes.</summary>
        protected static int FormatOf(byte[] header) {
            return BitConverter.ToUInt16(header, 4);
        }

        protected static void SetFormat(byte[] header, int format) {
            byte[] value = BitConverter.GetBytes((ushort)format);
            header[4] = value[0];
            header[5] = value[1];
        }

        /// <summary>Rewrites every chunk record of the regions: convert gets a record and returns it upgraded (or the same).</summary>
        protected static void RewriteChunkRecords(string saveFolder, Func<byte[], byte[]> convert) {
            string folder = Path.Combine(saveFolder, WorldStorage.RegionsFolder);
            if (!Directory.Exists(folder)) {
                return;
            }
            foreach (string path in Directory.GetFiles(folder, "*" + RegionFile.Extension)) {
                using (RegionFile region = new RegionFile(path)) {
                    foreach (int index in region.GetStoredChunks()) {
                        byte[] record = region.Read(index);
                        byte[] upgraded = convert(record);
                        if (upgraded != record) {
                            region.Write(index, upgraded);
                        }
                    }
                }
            }
        }

        // the varints of chunk records: unsigned in 7 bit groups, signed zigzagged
        protected static uint ReadVarUInt(BinaryReader reader) {
            return (uint)ReadVarULong(reader);
        }

        protected static ulong ReadVarULong(BinaryReader reader) {
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

        protected static void WriteVarUInt(BinaryWriter writer, uint value) {
            WriteVarULong(writer, value);
        }

        protected static void WriteVarULong(BinaryWriter writer, ulong value) {
            while (value >= 0x80) {
                writer.Write((byte)(value | 0x80));
                value >>= 7;
            }
            writer.Write((byte)value);
        }

        protected static int ReadVarInt(BinaryReader reader) {
            uint value = ReadVarUInt(reader);
            return (int)(value >> 1) ^ -(int)(value & 1);
        }

        protected static void WriteVarInt(BinaryWriter writer, int value) {
            WriteVarUInt(writer, (uint)((value << 1) ^ (value >> 31)));
        }

        // varint player id, followed (when not 0) by varint unix seconds
        protected static void ReadPlacer(BinaryReader reader, out int playerId, out ulong placedAt) {
            playerId = (int)ReadVarUInt(reader);
            placedAt = playerId != 0 ? ReadVarULong(reader) : 0;
        }

        protected static void WritePlacer(BinaryWriter writer, int playerId, ulong placedAt) {
            WriteVarUInt(writer, (uint)playerId);
            if (playerId != 0) {
                WriteVarULong(writer, placedAt);
            }
        }
    }
}
