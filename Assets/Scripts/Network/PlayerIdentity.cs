using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// Who this player is: the name typed in the menu, plus the id of their machine.
    /// Servers link a name to the first machine that uses it, so nobody else can use it.
    /// </summary>
    public static class PlayerIdentity
    {
        public const int MinNameLength = 3;
        public const int MaxNameLength = 16;

        private const string NameKey = "playerName";
        private const string FallbackIdKey = "fallbackMachineId";

        public static string Name {
            get {
                string name = PlayerPrefs.GetString(NameKey, "");

                if (ValidateName(name) != null) {
                    name = "Player" + UnityEngine.Random.Range(1000, 10000);
                    Name = name;
                }
                return name;
            }
            set {
                PlayerPrefs.SetString(NameKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// SystemInfo.deviceUniqueIdentifier, or a random id generated once and stored locally
        /// on platforms where Unity can't provide one. The "-identity [tag]" command line option (development builds
        /// only) adds that tag:
        /// players are their machine (see BrickcraftAuthenticator), so instances of the same computer joining
        /// the same server (to test) need one each.
        /// </summary>
        public static string MachineId {
            get {
                string id = SystemInfo.deviceUniqueIdentifier;

                if (string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier) {
                    id = fallbackId();
                }
                string tag = identityTag();
                return tag != null ? id + ":" + tag : id;
            }
        }

        // the "-identity" command line option's tag, null without it; only development builds (and the editor) read it,
        // so players can't pass themselves off as another player of their computer
        private static string identityTag() {
            if (!Debug.isDebugBuild) {
                return null;
            }
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) {
                if (args[i] == "-identity" && args[i + 1].Trim().Length > 0) {
                    return args[i + 1].Trim();
                }
            }
            return null;
        }

        private static string fallbackId() {
            string id = PlayerPrefs.GetString(FallbackIdKey, "");

            if (id.Length == 0) {
                byte[] bytes = new byte[32];
                using (RandomNumberGenerator random = RandomNumberGenerator.Create()) {
                    random.GetBytes(bytes);
                }
                id = toHex(bytes);
                PlayerPrefs.SetString(FallbackIdKey, id);
                PlayerPrefs.Save();
            }
            return id;
        }

        /// <summary>Null if the name is valid, otherwise why it isn't.</summary>
        public static string ValidateName(string name) {
            if (string.IsNullOrEmpty(name) || name.Length < MinNameLength || name.Length > MaxNameLength) {
                return "Names must have between " + MinNameLength + " and " + MaxNameLength + " characters";
            }
            foreach (char c in name) {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') {
                    return "Names can only have letters, numbers, - and _";
                }
            }
            return null;
        }

        /// <summary>A valid name like that one no player of the save has (cut to fit, numbered if taken).</summary>
        public static string FreeName(string name, GameDatabase database) {
            string free = name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;
            for (int i = 2; database.FindPlayer(free) != null; i++) {
                string suffix = "_" + i;
                free = (name.Length + suffix.Length > MaxNameLength ? name.Substring(0, MaxNameLength - suffix.Length) : name) + suffix;
            }
            return free;
        }

        public static string HashMachineId(string machineId) {
            using (SHA256 sha = SHA256.Create()) {
                return toHex(sha.ComputeHash(Encoding.UTF8.GetBytes(machineId ?? "")));
            }
        }

        private static string toHex(byte[] bytes) {
            StringBuilder hex = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) {
                hex.Append(b.ToString("x2"));
            }
            return hex.ToString();
        }
    }
}
