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
    }
}
