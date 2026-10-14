using System.Collections.Generic;
using Brickcraft.Events;
using Brickcraft.Mods;
using Brickcraft.Network;
using Mirror;
using MoonSharp.Interpreter;
using UnityEngine;

namespace Brickcraft.Scripting
{
    /// <summary>
    /// The "ui" table of mods' scripts: popups, toasts, titles and HUD panels on a player's screen.
    /// Scripts run on the server, so each call becomes a ModUiMessage to that player (shown by
    /// UI.ModUiPanel), and popups come back as a ModUiResponseMessage that calls the script's callback.
    ///   ui.popup(player, {title, text, buttons, input, position, offset}, callback)   returns its id; callback(button, input)
    ///   ui.close(player, id)
    ///   ui.toast(player, text, seconds or {seconds, position})
    ///   ui.title(player, title, subtitle, seconds or {seconds, position, offset})
    ///   ui.hud(player, key, {title, lines, progress, position}) / ui.removeHud(player, key)
    /// Positions are the names of Events.ModUiPositions ("topRight"...), offsets {x =, y =} in pixels.
    /// Documented for modders in docs/lua/ui.md.
    /// </summary>
    public static class LuaUi
    {
        public const int MaxTitleLength = 100;
        public const int MaxTextLength = 2000;
        public const int MaxButtons = 6;
        public const int MaxButtonLength = 40;
        public const int MaxLines = 20;
        public const int MaxInputLength = 200;
        /// <summary>Popups waiting for a player's answer, per player.</summary>
        public const int MaxOpenPopups = 10;
        public const float MaxSeconds = 30;

        private class Pending
        {
            public NetworkConnectionToClient connection;
            public Script script;
            public DynValue callback;
            public string[] buttons;
        }

        private static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        private static int nextPopupId = 1;

        public static void StartServer() {
            NetworkServer.RegisterHandler<ModUiResponseMessage>(onResponse);
        }

        public static void StopServer() {
            NetworkServer.UnregisterHandler<ModUiResponseMessage>();
            pending.Clear();
        }

        /// <summary>Forgets the popups waiting for an answer, when the scripts stop.</summary>
        public static void Reset() {
            pending.Clear();
        }

        /// <summary>A player left: its popups won't be answered.</summary>
        public static void PlayerLeft(NetworkConnectionToClient conn) {
            List<int> gone = new List<int>();
            foreach (KeyValuePair<int, Pending> entry in pending) {
                if (entry.Value.connection == conn) {
                    gone.Add(entry.Key);
                }
            }
            foreach (int id in gone) {
                pending.Remove(id);
            }
        }

        public static void Install(Script script, ModInfo mod) {
            Table ui = new Table(script);

            ui["popup"] = DynValue.NewCallback((context, args) => {
                PlayerHandle player = playerArg(args, 0, "popup");
                Table options = args.AsType(1, "popup", DataType.Table).Table;
                DynValue callback = args.Count > 2 && args[2].Type == DataType.Function ? args[2] : null;

                int open = 0;
                foreach (Pending waiting in pending.Values) {
                    if (waiting.connection == player.connection) {
                        open++;
                    }
                }
                if (open >= MaxOpenPopups) {
                    throw new ScriptRuntimeException("A player can have up to " + MaxOpenPopups + " popups waiting for an answer");
                }

                string[] buttons = stringList(options.Get("buttons"), MaxButtons, MaxButtonLength, "buttons");
                if (buttons.Length == 0) {
                    buttons = new[] { "OK" };
                }
                DynValue input = options.Get("input");
                bool hasInput = input.Type == DataType.Table || (input.Type == DataType.Boolean && input.Boolean);
                string inputText = hasInput && input.Type == DataType.Table ? text(input.Table.Get("text"), MaxInputLength) : "";
                string placeholder = hasInput && input.Type == DataType.Table ? text(input.Table.Get("placeholder"), MaxButtonLength * 2) : "";

                int id = nextPopupId++;
                ModUiMessage message = new ModUiMessage() {
                    kind = (byte)ModUiKind.Popup,
                    id = id,
                    title = text(options.Get("title"), MaxTitleLength),
                    text = text(options.Get("text"), MaxTextLength),
                    buttons = buttons,
                    hasInput = hasInput,
                    inputText = inputText,
                    inputPlaceholder = placeholder,
                };
                readPlacement(options, true, ref message);
                pending[id] = new Pending() { connection = player.connection, script = script, callback = callback, buttons = buttons };
                player.connection.Send(message);
                return DynValue.NewNumber(id);
            });

            ui["close"] = DynValue.NewCallback((context, args) => {
                PlayerHandle player = playerArg(args, 0, "close");
                int id = (int)args.AsType(1, "close", DataType.Number).Number;
                Pending popup;
                if (!pending.TryGetValue(id, out popup) || popup.script != script || popup.connection != player.connection) {
                    return DynValue.False;
                }
                // its callback isn't called: the script closed it itself
                pending.Remove(id);
                player.connection.Send(new ModUiMessage() { kind = (byte)ModUiKind.ClosePopup, id = id });
                return DynValue.True;
            });

            ui["toast"] = DynValue.NewCallback((context, args) => {
                PlayerHandle player = playerArg(args, 0, "toast");
                ModUiMessage message = new ModUiMessage() {
                    kind = (byte)ModUiKind.Toast,
                    text = text(args[1], MaxTextLength),
                };
                readTiming(args, 2, false, ref message);
                player.connection.Send(message);
                return DynValue.Nil;
            });

            ui["title"] = DynValue.NewCallback((context, args) => {
                PlayerHandle player = playerArg(args, 0, "title");
                ModUiMessage message = new ModUiMessage() {
                    kind = (byte)ModUiKind.Title,
                    title = text(args[1], MaxTitleLength),
                    text = text(args.Count > 2 ? args[2] : DynValue.Nil, MaxTitleLength),
                };
                readTiming(args, 3, true, ref message);
                player.connection.Send(message);
                return DynValue.Nil;
            });

            ui["hud"] = DynValue.NewCallback((context, args) => {
                PlayerHandle player = playerArg(args, 0, "hud");
                string key = args.AsType(1, "hud", DataType.String).String;
                Table options = args.AsType(2, "hud", DataType.Table).Table;
                DynValue progress = options.Get("progress");
                ModUiMessage message = new ModUiMessage() {
                    kind = (byte)ModUiKind.Hud,
                    key = mod.id + ":" + key, // panels of different mods don't clash
                    title = text(options.Get("title"), MaxTitleLength),
                    lines = stringList(options.Get("lines"), MaxLines, MaxTitleLength, "lines"),
                    progress = progress.Type == DataType.Number ? Mathf.Clamp01((float)progress.Number) : -1,
                };
                readPlacement(options, false, ref message);
                player.connection.Send(message);
                return DynValue.Nil;
            });

            ui["removeHud"] = DynValue.NewCallback((context, args) => {
                PlayerHandle player = playerArg(args, 0, "removeHud");
                string key = args.AsType(1, "removeHud", DataType.String).String;
                player.connection.Send(new ModUiMessage() { kind = (byte)ModUiKind.RemoveHud, key = mod.id + ":" + key });
                return DynValue.Nil;
            });

            script.Globals["ui"] = ui;
        }

        // a popup was answered: its callback gets the button's label (nil if dismissed) and the text field's text
        private static void onResponse(NetworkConnectionToClient conn, ModUiResponseMessage message) {
            Pending popup;
            if (!pending.TryGetValue(message.id, out popup) || popup.connection != conn) {
                return;
            }
            pending.Remove(message.id);
            if (popup.callback == null) {
                return;
            }
            DynValue button = message.button >= 0 && message.button < popup.buttons.Length
                ? DynValue.NewString(popup.buttons[message.button])
                : DynValue.Nil;
            string input = message.input ?? "";
            if (input.Length > MaxInputLength) {
                input = input.Substring(0, MaxInputLength);
            }
            ModScripts.CallFunction(popup.script, popup.callback, "popup " + message.id, button, DynValue.NewString(input));
        }

        private static PlayerHandle playerArg(CallbackArguments args, int index, string function) {
            DynValue value = args[index];
            PlayerHandle player = value.Type == DataType.UserData ? value.UserData.Object as PlayerHandle : null;
            if (player == null) {
                throw new ScriptRuntimeException("ui." + function + " needs a player first");
            }
            if (player.connection == null || !player.connection.isReady) {
                throw new ScriptRuntimeException("ui." + function + ": " + player.name + " isn't in the game");
            }
            return player;
        }

        private static string text(DynValue value, int maxLength) {
            if (value.IsNil()) {
                return "";
            }
            string result = value.ToPrintString();
            return result.Length > maxLength ? result.Substring(0, maxLength) : result;
        }

        private static string[] stringList(DynValue value, int maxCount, int maxLength, string what) {
            if (value.IsNil()) {
                return new string[0];
            }
            if (value.Type != DataType.Table) {
                throw new ScriptRuntimeException(what + " should be a list of texts");
            }
            List<string> list = new List<string>();
            for (int i = 1; i <= value.Table.Length && list.Count < maxCount; i++) {
                list.Add(text(value.Table.Get(i), maxLength));
            }
            return list.ToArray();
        }

        // toasts and titles take their seconds, or a table {seconds, position, offset}
        private static void readTiming(CallbackArguments args, int index, bool allowOffset, ref ModUiMessage message) {
            message.seconds = 3;
            if (args.Count <= index || args[index].IsNil()) {
                return;
            }
            DynValue value = args[index];
            if (value.Type == DataType.Number) {
                message.seconds = Mathf.Clamp((float)value.Number, 0.5f, MaxSeconds);
                return;
            }
            if (value.Type != DataType.Table) {
                throw new ScriptRuntimeException("expected the seconds, or a table with seconds and position");
            }
            DynValue seconds = value.Table.Get("seconds");
            if (seconds.Type == DataType.Number) {
                message.seconds = Mathf.Clamp((float)seconds.Number, 0.5f, MaxSeconds);
            }
            readPlacement(value.Table, allowOffset, ref message);
        }

        // "position" (a name of ModUiPositions) and, where it makes sense, "offset" {x =, y =} in pixels
        private static void readPlacement(Table options, bool allowOffset, ref ModUiMessage message) {
            DynValue position = options.Get("position");
            if (!position.IsNil()) {
                UnityEngine.Vector2 anchor;
                if (position.Type != DataType.String || !ModUiPositions.TryGetAnchor(position.String, out anchor)) {
                    throw new ScriptRuntimeException("position should be one of " + string.Join(", ", ModUiPositions.Names));
                }
                message.position = position.String;
            }
            DynValue offset = options.Get("offset");
            if (offset.IsNil()) {
                return;
            }
            if (!allowOffset) {
                throw new ScriptRuntimeException("offset only works for popups and titles");
            }
            if (offset.Type != DataType.Table) {
                throw new ScriptRuntimeException("offset should be a table {x = 0, y = 0}");
            }
            DynValue x = offset.Table.Get("x"), y = offset.Table.Get("y");
            message.offsetX = x.Type == DataType.Number ? Mathf.Clamp((float)x.Number, -2000, 2000) : 0;
            message.offsetY = y.Type == DataType.Number ? Mathf.Clamp((float)y.Number, -2000, 2000) : 0;
        }
    }
}
