using System;
using System.Collections.Generic;
using Brickcraft.Events;
using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>
    /// The wiring tool (items tagged "wiring", see Item.IsWiringTool), on the local player's side. The dig button on a
    /// vehicle's seat picks it; then on that vehicle's parts it wires them to it, or unwires them (see
    /// PlayerNetwork.CmdWire, VehicleWires.Defaults). The place button on a seat opens its controls (UI.WiringPanel).
    /// While it's in hand, the picked seat's wires show as lines to their parts.
    /// </summary>
    public static class WiringTool
    {
        /// <summary>Raised when the player opens a seat's controls with the tool in that slot.</summary>
        public static event Action<int, Brick> PanelRequested;

        /// <summary>The seat picked, null if none (or it went).</summary>
        public static Brick SelectedSeat {
            get { return selected != null && selected.vehicle != null && Server.bricks.TryGetValue(selected.id, out Brick seat) && seat == selected ? selected : null; }
        }

        private static Brick selected;

        public static bool IsSeat(Brick brick) {
            return brick != null && brick.vehicle != null && Server.items.TryGetValue(brick.itemId, out Item item) && item.VehicleRole == VehicleRole.Seat;
        }

        /// <summary>The dig button with the tool in that slot, on that brick (null if none).</summary>
        public static void Click(int slot, Brick brick) {
            if (IsSeat(brick)) {
                selected = brick == SelectedSeat ? null : brick;
                if (selected != null) {
                    say("vehicle.seat_selected");
                }
                return;
            }
            if (brick == null || brick.vehicle == null) {
                return;
            }
            Brick seat = SelectedSeat;
            if (seat == null || seat.vehicle != brick.vehicle) {
                say("vehicle.select_seat_first");
                return;
            }
            if (!VehicleWires.CanWire(brick)) {
                say("vehicle.cannot_wire");
                return;
            }
            Player.Instance.network.CmdWire(slot, seat.id, brick.id);
        }

        /// <summary>The place button with the tool in that slot, on that brick: a seat's controls open.</summary>
        public static void Use(int slot, Brick brick) {
            if (IsSeat(brick)) {
                selected = brick;
                PanelRequested?.Invoke(slot, brick);
            }
        }

        // a line in the player's own chat, like the game's events
        private static void say(string key) {
            EventManager.ChatLineReceived.Raise(new ChatLineReceivedEvent() { sender = null, text = Network.GameText.Of(key).Say() });
        }
    }
}
