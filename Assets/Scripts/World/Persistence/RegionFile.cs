using System;
using System.Collections.Generic;
using System.IO;

namespace Brickcraft.World
{
    /// <summary>
    /// Stores the records of a 32x32 chunk region in one file, so a big world isn't thousands of
    /// files and a single chunk can be read or rewritten without touching the others.
    ///
    /// Layout, in 4 KB sectors:
    ///   sectors 0-1: header, 1024 entries (one per chunk) of u32 first sector + u32 length in bytes
    ///   then the chunk records, each taking whole sectors
    /// A new version of a record always goes to free sectors and the header entry is updated after it,
    /// so a crash mid-save leaves either the old or the new version readable, never half of one.
    /// The sectors of the old version are reused afterwards.
    ///
    /// Thread safe.
    /// </summary>
    public class RegionFile : IDisposable
    {
        public const int ChunksPerSide = 32;
        public const string Extension = ".bcr";

        private const int SectorSize = 4096;
        private const int ChunkCount = ChunksPerSide * ChunksPerSide;
        private const int HeaderSectors = ChunkCount * 8 / SectorSize;

        private readonly object sync = new object();
        private readonly FileStream file;
        private readonly uint[] firstSectors = new uint[ChunkCount];
        private readonly uint[] lengths = new uint[ChunkCount];
        private readonly List<bool> usedSectors = new List<bool>();

        public RegionFile(string path) {
            file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

            if (file.Length < HeaderSectors * SectorSize) {
                file.SetLength(HeaderSectors * SectorSize);
            }
            for (int i = 0; i < HeaderSectors; i++) {
                usedSectors.Add(true);
            }

            byte[] header = new byte[HeaderSectors * SectorSize];
            readExactly(0, header);

            for (int i = 0; i < ChunkCount; i++) {
                firstSectors[i] = BitConverter.ToUInt32(header, i * 8);
                lengths[i] = BitConverter.ToUInt32(header, i * 8 + 4);

                if (lengths[i] > 0) {
                    markSectors(firstSectors[i], sectorsFor(lengths[i]), true);
                }
            }
        }

        public static int LocalIndex(int chunkX, int chunkZ) {
            return (chunkZ & (ChunksPerSide - 1)) * ChunksPerSide + (chunkX & (ChunksPerSide - 1));
        }

        /// <summary>Chunks of this region with a record, as local indexes.</summary>
        public List<int> GetStoredChunks() {
            lock (sync) {
                List<int> stored = new List<int>();

                for (int i = 0; i < ChunkCount; i++) {
                    if (lengths[i] > 0) {
                        stored.Add(i);
                    }
                }
                return stored;
            }
        }

        /// <summary>The record of a chunk, or null if it has none.</summary>
        public byte[] Read(int index) {
            lock (sync) {
                if (lengths[index] == 0) {
                    return null;
                }
                byte[] data = new byte[lengths[index]];
                readExactly((long)firstSectors[index] * SectorSize, data);

                return data;
            }
        }

        /// <summary>Stores the record of a chunk, null removes it.</summary>
        public void Write(int index, byte[] data) {
            lock (sync) {
                uint oldFirst = firstSectors[index];
                int oldSectors = lengths[index] > 0 ? sectorsFor(lengths[index]) : 0;

                if (data == null || data.Length == 0) {
                    setEntry(index, 0, 0);
                    markSectors(oldFirst, oldSectors, false);
                    return;
                }

                // the old version is still marked as used, so it can't be overwritten
                int sectors = sectorsFor((uint)data.Length);
                uint first = allocate(sectors);

                file.Seek((long)first * SectorSize, SeekOrigin.Begin);
                file.Write(data, 0, data.Length);
                file.Write(new byte[sectors * SectorSize - data.Length], 0, sectors * SectorSize - data.Length);

                file.Flush();

                setEntry(index, first, (uint)data.Length);
                markSectors(oldFirst, oldSectors, false);
            }
        }

        public void Flush() {
            lock (sync) {
                file.Flush(true);
            }
        }

        public void Dispose() {
            lock (sync) {
                file.Flush(true);
                file.Dispose();
            }
        }

        private void setEntry(int index, uint first, uint length) {
            firstSectors[index] = first;
            lengths[index] = length;

            byte[] entry = new byte[8];
            BitConverter.GetBytes(first).CopyTo(entry, 0);
            BitConverter.GetBytes(length).CopyTo(entry, 4);

            file.Seek(index * 8L, SeekOrigin.Begin);
            file.Write(entry, 0, entry.Length);
        }

        // first run of free sectors long enough, or the end of the file
        private uint allocate(int sectors) {
            int run = 0;

            for (int i = HeaderSectors; i < usedSectors.Count; i++) {
                run = usedSectors[i] ? 0 : run + 1;

                if (run == sectors) {
                    uint first = (uint)(i - sectors + 1);
                    markSectors(first, sectors, true);
                    return first;
                }
            }

            uint end = (uint)(usedSectors.Count - run);
            markSectors(end, sectors, true);
            return end;
        }

        private void markSectors(uint first, int count, bool used) {
            for (int i = 0; i < count; i++) {
                int sector = (int)first + i;

                while (usedSectors.Count <= sector) {
                    usedSectors.Add(false);
                }
                usedSectors[sector] = used;
            }
        }

        private static int sectorsFor(uint length) {
            return (int)((length + SectorSize - 1) / SectorSize);
        }

        private void readExactly(long position, byte[] buffer) {
            file.Seek(position, SeekOrigin.Begin);
            int read = 0;

            while (read < buffer.Length) {
                int count = file.Read(buffer, read, buffer.Length - read);
                if (count == 0) {
                    throw new EndOfStreamException("Region file is truncated: " + file.Name);
                }
                read += count;
            }
        }
    }
}
