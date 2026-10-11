using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// How a server runs, from server.json next to the game (next to Mods/: the project's folder in the editor,
    /// next to [Game]_Data in builds), read every time a server starts and written with the defaults if it
    /// isn't there. They apply to players joining from elsewhere: the host's own player isn't asked for the
    /// password nor limited in how far it draws.
    /// </summary>
    public class ServerConfig
    {
        public const string FileName = "server.json";

        /// <summary>What players see of the server.</summary>
        public string name = "Brickcraft server";
        /// <summary>Players at once (the host's own player counts), the next ones are told it's full.</summary>
        public int maxPlayers = 16;
        /// <summary>What players need to type to join, none if empty.</summary>
        public string password = "";
        /// <summary>The furthest players get the world, in chunks: they draw as far as they ask within it.</summary>
        public int maxRenderDistance = 32;

        /// <summary>The configuration of the server running (the defaults until one starts).</summary>
        public static ServerConfig Current { get; private set; } = new ServerConfig();

        public static string FilePath {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName)); }
        }

        [JsonIgnore]
        public bool HasPassword {
            get { return !string.IsNullOrEmpty(password); }
        }

        /// <summary>Reads server.json into Current (writing it with the defaults when there's none).</summary>
        public static ServerConfig Load() {
            ServerConfig config = new ServerConfig();
            string path = FilePath;
            try {
                if (File.Exists(path)) {
                    config = JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText(path)) ?? new ServerConfig();
                } else {
                    File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
                    Debug.Log("Wrote the default server configuration in " + path);
                }
            } catch (Exception e) {
                // a broken file is left for its owner to fix, the server runs with the defaults
                Debug.LogError("Can't read " + path + " (" + e.Message + "), the server uses the defaults");
                config = new ServerConfig();
            }
            config.name = string.IsNullOrWhiteSpace(config.name) ? "Brickcraft server" : config.name.Trim();
            config.maxPlayers = Mathf.Max(1, config.maxPlayers);
            config.maxRenderDistance = Mathf.Clamp(config.maxRenderDistance, GameSettings.MinRenderDistance, GameSettings.MaxRenderDistance);
            Current = config;
            return config;
        }

        /// <summary>What clients send instead of the password itself.</summary>
        public static string HashPassword(string password) {
            if (string.IsNullOrEmpty(password)) {
                return "";
            }
            using (SHA256 sha = SHA256.Create()) {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes("brickcraft-server:" + password));
                StringBuilder hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) {
                    hex.Append(b.ToString("x2"));
                }
                return hex.ToString();
            }
        }

        /// <summary>Whether what a client sent matches the password (always, without one).</summary>
        public bool CheckPassword(string sentHash) {
            return !HasPassword || string.Equals(HashPassword(password), sentHash ?? "", StringComparison.Ordinal);
        }
    }
}
