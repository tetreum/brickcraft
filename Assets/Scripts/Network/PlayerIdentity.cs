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
        /// on platforms where Unity can't provide one.
        /// </summary>
        public static string MachineId {
            get {
                string id = SystemInfo.deviceUniqueIdentifier;

                if (!string.IsNullOrEmpty(id) && id != SystemInfo.unsupportedIdentifier) {
                    return id;
                }
                return fallbackId();
            }
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
