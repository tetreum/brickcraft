using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Brickcraft.World.Migrations
{
    /// <summary>
    /// Which saved worlds this version of the game can play, and upgrading old ones to the current
    /// format (WorldStorage.FormatVersion) through the migrations of this folder (see SaveMigration).
    ///
    /// When anything in a save changes format (world.dat, chunk records, players.db...): bump
    /// WorldStorage.FormatVersion and add a migration from the previous format. If old saves can't
    /// be upgraded anymore, raise OldestSupported instead.
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>Saves older than this format can't be played by this version of the game.</summary>
        public const int OldestSupported = 3;

        public static int Current {
            get { return WorldStorage.FormatVersion; }
        }

        /// <summary>Where saves are backed up before being upgraded.</summary>
        public static string BackupsFolder {
            get { return Path.Combine(Application.persistentDataPath, "backups"); }
        }

        private static Dictionary<int, SaveMigration> migrations;

        // every SaveMigration of the game, by the format it upgrades from
        private static Dictionary<int, SaveMigration> all() {
            if (migrations == null) {
                migrations = new Dictionary<int, SaveMigration>();
                foreach (Type type in typeof(SaveMigration).Assembly.GetTypes()) {
                    if (type.IsSubclassOf(typeof(SaveMigration)) && !type.IsAbstract) {
                        SaveMigration migration = (SaveMigration)Activator.CreateInstance(type);
                        if (migrations.ContainsKey(migration.From)) {
                            Debug.LogError("Two save migrations from format " + migration.From + ": " + type.Name + " and " + migrations[migration.From].GetType().Name);
                        }
                        migrations[migration.From] = migration;
                    }
                }
            }
            return migrations;
        }

        /// <summary>Null if this version of the game can play a save of that format (upgrading it if needed), otherwise why not.</summary>
        public static string CheckCompatibility(int format) {
            if (format > Current) {
                return "saved by a newer version of the game";
            }
            if (format < OldestSupported) {
                return "saved by a version too old to upgrade";
            }
            for (int from = format; from < Current; from++) {
                if (!all().ContainsKey(from)) {
                    return "no upgrade from format " + from + " to this version";
                }
            }
            return null;
        }

        public static bool NeedsUpgrade(int format) {
            return format < Current;
        }

        /// <summary>
        /// Upgrades the save in the folder to the current format, if it's older: backs it up, runs the
        /// migrations one after the other, and puts the backup back if one fails (then throws).
        /// </summary>
        public static void Upgrade(string saveFolder, int format) {
            string problem = CheckCompatibility(format);
            if (problem != null) {
                throw new InvalidDataException(problem);
            }
            if (!NeedsUpgrade(format)) {
                return;
            }
            string backup = Path.Combine(BackupsFolder, Path.GetFileName(saveFolder) + "-format" + format + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            copyFolder(saveFolder, backup);
            Debug.Log("Upgrading the world " + saveFolder + " from format " + format + " to " + Current + ", backed up in " + backup);

            try {
                for (int from = format; from < Current; from++) {
                    SaveMigration migration = all()[from];
                    Debug.Log("Save migration " + migration.From + " -> " + migration.To + ": " + migration.Description);
                    migration.Migrate(saveFolder);
                }
            } catch (Exception e) {
                Debug.LogError("Upgrading " + saveFolder + " failed, putting the backup back: " + e);
                Directory.Delete(saveFolder, true);
                copyFolder(backup, saveFolder);
                throw new InvalidDataException("Upgrading it to this version failed (" + e.Message + "), it's left as it was");
            }
        }

        private static void copyFolder(string from, string to) {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from)) {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            }
            foreach (string folder in Directory.GetDirectories(from)) {
                copyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
            }
        }
    }
}
