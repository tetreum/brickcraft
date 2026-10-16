using System.Collections.Generic;
using Brickcraft.Events;
using Brickcraft.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// A seat's controls (see WiringTool): a row for each of its wires, the part, what it does (drive, steer, tilt), with
    /// what (the forward and back keys, left and right, up and down, or a key of its own: click it, then press one) and whether it's
    /// inverted; a part can have more wires (+), or lose one (x). Every change goes to the server at once
    /// (PlayerNetwork.CmdSetWires), and the rows show the vehicle's wires as they come back. Made the first time it's needed.
    /// </summary>
    public class WiringPanel : MonoBehaviour
    {
        private static WiringPanel window;

        private Text heading;
        private Text hint;
        private Text closeLabel;
        private RectTransform rows;
        private int slot;
        private Brick seat;
        private Vehicle watched;
        private bool isOpen;
        private bool isDirty;
        // the row waiting for its key, -1 if none
        private int listening = -1;
        private List<VehicleWire> shown = new List<VehicleWire>();

        /// <summary>Opens the controls of that seat, for the wiring tool in that slot.</summary>
        public static void Open(int slot, Brick seat) {
            if (window == null) {
                if (Menu.Instance == null) {
                    return;
                }
                window = create(Menu.Instance.transform);
            }
            window.slot = slot;
            window.seat = seat;
            window.listening = -1;
            window.watch(seat.vehicle);
            window.transform.SetAsLastSibling();
            window.gameObject.SetActive(true);
            window.relabel();
            window.refresh();
            if (!window.isOpen) {
                window.isOpen = true;
                DialogMode.Enter();
            }
        }

        public static void Close() {
            if (window == null || !window.isOpen) {
                return;
            }
            window.isOpen = false;
            window.listening = -1;
            window.watch(null);
            window.gameObject.SetActive(false);
            DialogMode.Exit();
        }

        // the rows follow the vehicle's wires (a frame later, all the changes at once)
        private void watch(Vehicle vehicle) {
            if (watched != null) {
                watched.wires.OnChange -= onWiresChanged;
            }
            watched = vehicle;
            if (watched != null) {
                watched.wires.OnChange += onWiresChanged;
            }
        }

        private void onWiresChanged(Mirror.SyncList<VehicleWire>.Operation op, int index, VehicleWire old) {
            isDirty = true;
        }

        private void Update() {
            if (seat == null || seat.vehicle == null || !Server.bricks.ContainsKey(seat.id)) {
                Close();
                return;
            }
            if (listening >= 0) {
                listenForKey();
                return;
            }
            if (GameInput.KeyDown(KeyCode.Escape)) {
                Close();
                return;
            }
            if (isDirty) {
                isDirty = false;
                refresh();
            }
        }

        // the next wire key pressed is the row's; Escape leaves it as it was
        private void listenForKey() {
            if (GameInput.KeyDown(KeyCode.Escape)) {
                listening = -1;
                showRows();
                return;
            }
            foreach (KeyCode key in VehicleControls.WireKeys) {
                if (GameInput.KeyDown(key)) {
                    int row = listening;
                    listening = -1;
                    change(row, wire => { wire.source = WireSource.Key; wire.key = (int)key; return wire; });
                    return;
                }
            }
        }

        private void OnEnable() {
            EventManager.LanguageChanged.Subscribe(onLanguageChanged);
        }

        private void OnDisable() {
            EventManager.LanguageChanged.Unsubscribe(onLanguageChanged);
        }

        private void onLanguageChanged(LanguageChangedEvent e) {
            relabel();
            refresh();
        }

        private void OnDestroy() {
            watch(null);
            if (isOpen) {
                isOpen = false;
                DialogMode.Exit();
            }
        }

        private void relabel() {
            heading.text = Loc.Get(Loc.UI, "wiring.title");
            closeLabel.text = Loc.Get(Loc.UI, "wiring.close");
        }

        // the seat's wires as the vehicle has them
        private void refresh() {
            shown = seat != null && seat.vehicle != null ? seat.vehicle.WiresOf(seat.id) : new List<VehicleWire>();
            showRows();
        }

        // a row for each wire shown
        private void showRows() {
            foreach (Transform row in rows) {
                Destroy(row.gameObject);
            }
            hint.text = Loc.Get(Loc.UI, shown.Count == 0 ? "wiring.empty" : "wiring.hint");
            for (int i = 0; i < shown.Count; i++) {
                addRow(i, shown[i]);
            }
        }

        private void addRow(int index, VehicleWire wire) {
            Font font = CodeUi.Font;
            GameObject row = new GameObject("Wire " + index, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(rows, false);
            row.GetComponent<LayoutElement>().preferredHeight = 32;
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            Text part = CodeUi.Label(row.transform, font, partName(wire.part), 14, FontStyle.Normal, CodeUi.TextColor);
            part.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            CodeUi.Clickable(row.transform, font, Loc.Get(Loc.UI, "wiring.action." + wire.action.ToString().ToLowerInvariant()),
                CodeUi.ButtonColor, 110, () => change(index, w => { w.action = (WireAction)(((int)w.action + 1) % 3); return w; }));

            string source = listening == index ? Loc.Get(Loc.UI, "wiring.press_key")
                : wire.source == WireSource.Throttle ? Loc.Get(Loc.UI, "wiring.source.throttle")
                : wire.source == WireSource.Steering ? Loc.Get(Loc.UI, "wiring.source.steering")
                : wire.source == WireSource.UpDown ? Loc.Get(Loc.UI, "wiring.source.updown")
                : Loc.Get(Loc.UI, "wiring.source.key", GameInput.KeyName((KeyCode)wire.key));
            CodeUi.Clickable(row.transform, font, source, CodeUi.ButtonColor, 190, () => nextSource(index));

            CodeUi.Clickable(row.transform, font, (wire.invert ? "✓ " : "") + Loc.Get(Loc.UI, "wiring.invert"),
                wire.invert ? CodeUi.ActionColor : CodeUi.ButtonColor, 100, () => change(index, w => { w.invert = !w.invert; return w; }));
            CodeUi.Clickable(row.transform, font, "+", CodeUi.ButtonColor, 32, () => duplicate(index));
            CodeUi.Clickable(row.transform, font, "✕", CodeUi.ButtonColor, 32, () => remove(index));
        }

        private string partName(string partId) {
            if (!Server.bricks.TryGetValue(partId, out Brick part) || !Server.items.TryGetValue(part.itemId, out Item item)) {
                return "?";
            }
            // which one of the same kind, by where it is
            Transform vehicle = seat.vehicle.transform;
            Vector3 at = part.LocalBounds.center - seat.LocalBounds.center;
            Vector3 forward = VehicleShape.Seat(seat, vehicle).forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            string side = Vector3.Dot(at, right) > 0.01f ? Loc.Get(Loc.UI, "wiring.right") : Vector3.Dot(at, right) < -0.01f ? Loc.Get(Loc.UI, "wiring.left") : "";
            string end = Vector3.Dot(at, forward) > 0.01f ? Loc.Get(Loc.UI, "wiring.front") : Vector3.Dot(at, forward) < -0.01f ? Loc.Get(Loc.UI, "wiring.back") : "";
            string where = (end + " " + side).Trim();
            return where.Length > 0 ? item.DisplayName + " (" + where + ")" : item.DisplayName;
        }

        // throttle, steering, up and down, then a key (waiting for it), then throttle again
        private void nextSource(int index) {
            VehicleWire wire = shown[index];
            if (wire.source == WireSource.Throttle) {
                change(index, w => { w.source = WireSource.Steering; return w; });
            } else if (wire.source == WireSource.Steering) {
                change(index, w => { w.source = WireSource.UpDown; return w; });
            } else if (wire.source == WireSource.UpDown) {
                listening = index;
                showRows();
            } else {
                change(index, w => { w.source = WireSource.Throttle; return w; });
            }
        }

        private void change(int index, System.Func<VehicleWire, VehicleWire> edit) {
            if (index < 0 || index >= shown.Count) {
                return;
            }
            List<VehicleWire> wires = new List<VehicleWire>(shown);
            wires[index] = edit(wires[index]);
            send(wires);
        }

        // another wire to the same part: a key of its own, to pick
        private void duplicate(int index) {
            List<VehicleWire> wires = new List<VehicleWire>(shown);
            VehicleWire copy = wires[index];
            copy.source = WireSource.Throttle;
            copy.invert = false;
            wires.Insert(index + 1, copy);
            send(wires);
        }

        private void remove(int index) {
            List<VehicleWire> wires = new List<VehicleWire>(shown);
            wires.RemoveAt(index);
            send(wires);
        }

        // the server has the last word: the rows show what comes back
        private void send(List<VehicleWire> wires) {
            shown = wires;
            if (Player.Instance != null) {
                Player.Instance.network.CmdSetWires(slot, seat.id, wires.ToArray());
            }
            showRows(); // as sent, until the vehicle's come back
        }

        private static WiringPanel create(Transform root) {
            Font font = CodeUi.Font;
            GameObject panel = new GameObject("WiringPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(root, false);
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(720, 460);
            panel.GetComponent<Image>().color = CodeUi.Background;
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 16);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            WiringPanel wiring = panel.AddComponent<WiringPanel>();
            wiring.heading = CodeUi.Label(panel.transform, font, "", 20, FontStyle.Bold, Color.white);
            wiring.hint = CodeUi.Label(panel.transform, font, "", 14, FontStyle.Normal, CodeUi.HintColor);

            // the rows, scrolling
            GameObject scroll = new GameObject("Wires", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D), typeof(LayoutElement));
            scroll.transform.SetParent(panel.transform, false);
            scroll.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.09f);
            scroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(scroll.transform, false);
            RectTransform contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.offsetMin = contentRect.offsetMax = Vector2.zero;
            VerticalLayoutGroup list = content.GetComponent<VerticalLayoutGroup>();
            list.padding = new RectOffset(8, 8, 8, 8);
            list.spacing = 4;
            list.childControlWidth = list.childControlHeight = true;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scrollRect = scroll.GetComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = (RectTransform)scroll.transform;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30;
            wiring.rows = contentRect;

            GameObject close = CodeUi.Clickable(panel.transform, font, "Close", CodeUi.ButtonColor, 0, Close);
            close.GetComponent<LayoutElement>().preferredHeight = 36;
            wiring.closeLabel = close.GetComponentInChildren<Text>();
            panel.SetActive(false);
            return wiring;
        }
    }
}
