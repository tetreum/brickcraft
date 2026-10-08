using System;
using System.Collections.Generic;
using System.IO;
using SQLite;
using UnityEngine;

namespace Brickcraft.Network
{
    public static class PlayerRoles
    {
        public const string User = "user";
        public const string Admin = "admin";
    }

    [Table("players")]
    public class PlayerRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>Lowercase name, so names are unique regardless of case.</summary>
        [Unique, NotNull]
        public string NameKey { get; set; }

        [NotNull]
        public string Name { get; set; }

        /// <summary>SHA-256 of the id of the machine that owns this name, the id itself is never stored.</summary>
        [NotNull]
        public string TokenHash { get; set; }

        public long CreatedAt { get; set; }
        public long LastSeenAt { get; set; }
        public int TimesJoined { get; set; }
        public long PlayTimeSeconds { get; set; }

        public long Experience { get; set; }

        /// <summary>See <see cref="PlayerRoles"/>.</summary>
        public string Role { get; set; } = PlayerRoles.User;

        public bool IsAdmin {
            get { return Role == PlayerRoles.Admin; }
        }

        // where the player was in the world when it last left, absolute coordinates; null until then
        public double? LastX { get; set; }
        public double? LastY { get; set; }
        public double? LastZ { get; set; }

        /// <summary>Direction the player's body faced, in degrees around Y.</summary>
        public float? LastYaw { get; set; }

        public bool HasLastPosition {
            get { return LastX.HasValue && LastY.HasValue && LastZ.HasValue; }
        }
    }

    [Table("player_sessions")]
    public class PlayerSessionRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int PlayerId { get; set; }

        public string Address { get; set; }
        public long JoinedAt { get; set; }

        /// <summary>0 while the player is still connected.</summary>
        public long LeftAt { get; set; }
    }

    [Table("inventory_items")]
    public class InventoryItemRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int PlayerId { get; set; }

        public int Slot { get; set; }
        public int ItemId { get; set; }
        public int Quantity { get; set; }
        public int Health { get; set; }
    }

    /// <summary>
    /// The server's SQLite database: players, their sessions and their inventories.
    /// Lives in the save folder (players.db) and is only opened by the server. World changes are
    /// stored apart, in binary region files (see World.WorldStorage).
    /// </summary>
    public class GameDatabase : IDisposable
    {
        public string Path { get; private set; }

        private readonly SQLiteConnection connection;

        public GameDatabase(string saveFolder) {
            Directory.CreateDirectory(saveFolder);
            Path = System.IO.Path.Combine(saveFolder, "players.db");

            connection = new SQLiteConnection(Path);
            // the pragma returns the new mode, so it isn't a plain Execute
            connection.ExecuteScalar<string>("PRAGMA journal_mode=WAL");

            connection.CreateTable<PlayerRecord>();
            connection.CreateTable<PlayerSessionRecord>();
            connection.CreateTable<InventoryItemRecord>();

            // columns added to existing databases start empty
            connection.Execute("UPDATE players SET Role = ? WHERE Role IS NULL", PlayerRoles.User);
            connection.Execute("UPDATE players SET Experience = 0 WHERE Experience IS NULL");

            // sessions left open by a server that didn't shut down cleanly
            connection.Execute("UPDATE player_sessions SET LeftAt = JoinedAt WHERE LeftAt = 0");
        }

        public void Dispose() {
            connection.Close();
        }

        public static long Now() {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        // -------- players --------

        public PlayerRecord FindPlayer(string name) {
            string key = name.ToLowerInvariant();

            return connection.Table<PlayerRecord>().Where(p => p.NameKey == key).FirstOrDefault();
        }

        public PlayerRecord CreatePlayer(string name, string machineIdHash) {
            long now = Now();
            PlayerRecord player = new PlayerRecord() {
                NameKey = name.ToLowerInvariant(),
                Name = name,
                TokenHash = machineIdHash,
                CreatedAt = now,
                LastSeenAt = now,
                Role = PlayerRoles.User,
            };
            connection.Insert(player);

            return player;
        }

        public bool HasAdmin() {
            return connection.Table<PlayerRecord>().Where(p => p.Role == PlayerRoles.Admin).Count() > 0;
        }

        public void SetRole(PlayerRecord player, string role) {
            player.Role = role;
            connection.Update(player);
        }

        /// <summary>Remembers where the player is, to put it back there next time.</summary>
        public void SavePosition(PlayerRecord player, double x, double y, double z, float yaw) {
            player.LastX = x;
            player.LastY = y;
            player.LastZ = z;
            player.LastYaw = yaw;
            connection.Update(player);
        }

        // -------- sessions --------

        public PlayerSessionRecord StartSession(PlayerRecord player, string address) {
            long now = Now();
            PlayerSessionRecord session = new PlayerSessionRecord() {
                PlayerId = player.Id,
                Address = address,
                JoinedAt = now,
            };

            connection.RunInTransaction(() => {
                connection.Insert(session);

                player.TimesJoined++;
                player.LastSeenAt = now;
                connection.Update(player);
            });

            return session;
        }

        public void EndSession(PlayerRecord player, PlayerSessionRecord session) {
            long now = Now();

            connection.RunInTransaction(() => {
                session.LeftAt = now;
                connection.Update(session);

                player.LastSeenAt = now;
                player.PlayTimeSeconds += Math.Max(0, now - session.JoinedAt);
                connection.Update(player);
            });
        }

        // -------- inventories --------

        public List<InventoryItem> LoadInventory(int playerId) {
            List<InventoryItem> items = new List<InventoryItem>();

            foreach (InventoryItemRecord record in connection.Table<InventoryItemRecord>().Where(i => i.PlayerId == playerId)) {
                items.Add(new InventoryItem() {
                    slot = record.Slot,
                    itemId = record.ItemId,
                    quantity = record.Quantity,
                    health = record.Health,
                });
            }
            return items;
        }

        /// <summary>Replaces the stored inventory of a player, it's at most a few dozen rows.</summary>
        public void SaveInventory(int playerId, IEnumerable<InventoryItem> items) {
            connection.RunInTransaction(() => {
                connection.Execute("DELETE FROM inventory_items WHERE PlayerId = ?", playerId);

                foreach (InventoryItem item in items) {
                    connection.Insert(new InventoryItemRecord() {
                        PlayerId = playerId,
                        Slot = item.slot,
                        ItemId = item.itemId,
                        Quantity = item.quantity,
                        Health = item.health,
                    });
                }
            });
        }
    }
}
