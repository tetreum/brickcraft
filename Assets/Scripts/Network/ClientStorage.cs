using System;
using System.Collections.Generic;
using Mirror;

namespace Brickcraft.Network
{
    /// <summary>
    /// On a client: the storage brick it's looking into, as the server sent it (see BrickStorage), for the
    /// storage panel (UI.StoragePanel), which asks for moves with PlayerInventory.CmdStorageMove.
    /// </summary>
    public static class ClientStorage
    {
        /// <summary>The brick looked into, null when none.</summary>
        public static string BrickId { get; private set; }
        public static string Title { get; private set; }
        public static int Slots { get; private set; }
        public static readonly List<InventoryItem> Items = new List<InventoryItem>();

        public static bool IsOpen {
            get { return BrickId != null; }
        }

        /// <summary>It opened, its contents changed, or it closed.</summary>
        public static event Action Changed;

        public static void Register() {
            BrickId = null;
            Items.Clear();
            NetworkClient.RegisterHandler<StorageOpenedMessage>(onOpened);
            NetworkClient.RegisterHandler<StorageContentsMessage>(onContents);
            NetworkClient.RegisterHandler<StorageClosedMessage>(onClosed);
        }

        /// <summary>The player closed it (the server is told).</summary>
        public static void Close() {
            if (BrickId == null) {
                return;
            }
            if (NetworkClient.isConnected && Player.Instance != null) {
                Player.Instance.network.CmdCloseStorage(BrickId);
            }
            BrickId = null;
            Items.Clear();
            Changed?.Invoke();
        }

        /// <summary>The stack in one of its slots, null if empty.</summary>
        public static InventoryItem? InSlot(int slot) {
            foreach (InventoryItem stack in Items) {
                if (stack.slot == slot) {
                    return stack;
                }
            }
            return null;
        }

        private static void onOpened(StorageOpenedMessage message) {
            BrickId = message.brickId;
            Title = GameText.Resolve(message.title);
            Slots = message.slots;
            Items.Clear();
            Items.AddRange(message.items ?? new InventoryItem[0]);
            Changed?.Invoke();
        }

        private static void onContents(StorageContentsMessage message) {
            if (message.brickId != BrickId) {
                return;
            }
            Items.Clear();
            Items.AddRange(message.items ?? new InventoryItem[0]);
            Changed?.Invoke();
        }

        private static void onClosed(StorageClosedMessage message) {
            if (message.brickId != BrickId) {
                return;
            }
            BrickId = null;
            Items.Clear();
            Changed?.Invoke();
        }
    }
}
