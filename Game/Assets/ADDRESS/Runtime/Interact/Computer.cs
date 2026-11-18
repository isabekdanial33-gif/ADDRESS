using System.Collections.Generic;
using Address.Placement;
using UnityEngine;

namespace Address.Interact
{
    public enum DeviceKind { Tower, Monitor, Keyboard, Mouse, Laptop }

    /// <summary>
    /// A piece of a computer. The system unit (tower) is the hub; a monitor, keyboard and mouse work only when a cable
    /// runs from them to a tower. Cables are found in shelves and boxes (pockets), plugged with E, and they hang between
    /// the two ports; pull a device too far away and the cable comes out.
    /// </summary>
    public class Device : Interactable
    {
        public DeviceKind kind;
        public PlacedObject po;
        public Vector3 portLocal;
        public CableLink link;                 // cable to a tower (monitor/keyboard/mouse)
        public ComputerStation station;        // tower/laptop: its station; others: the one they are plugged into
        public Vector3 Port => transform.TransformPoint(portLocal);
        public bool Connected => kind == DeviceKind.Tower || kind == DeviceKind.Laptop || link != null;

        public override string Verb(IInteractHost h)
        {
            switch (kind)
            {
                case DeviceKind.Laptop: return "открыть ноутбук";
                case DeviceKind.Tower: return station.On ? (station.Ready ? "сесть за компьютер" : "выключить компьютер") : "включить компьютер";
                case DeviceKind.Monitor:
                    if (link == null) return h.Pockets.cables > 0 ? "подключить кабелем" : "монитор не подключён — нужен кабель";
                    return station != null && station.On ? "сесть за компьютер" : "монитор подключён";
                default:
                    if (link == null) return h.Pockets.cables > 0 ? "подключить кабелем к компьютеру" : null;
                    return null;
            }
        }

        public override void Use(IInteractHost h)
        {
            switch (kind)
            {
                case DeviceKind.Laptop: h.UseComputer(station); return;
                case DeviceKind.Tower:
                    if (station.On && station.Ready) { h.UseComputer(station); return; }
                    station.SetPower(!station.On); h.PlaySfx(station.beep, transform.position, 0.6f);
                    if (station.On && station.monitor == null) h.Toast("компьютер гудит", "но экрана нет — подключите монитор");
                    return;
                case DeviceKind.Monitor:
                    if (link == null) { TryConnect(h); return; }
                    if (station != null && station.On) h.UseComputer(station);
                    else h.Toast("экран тёмный", "включите системный блок");
                    return;
                default:
                    if (link == null) TryConnect(h);
                    return;
            }
        }

        void TryConnect(IInteractHost h)
        {
            if (h.Pockets.cables <= 0) { h.Toast("нужен кабель", "поищите на полках и в шкафах"); return; }
            var tower = ComputerStation.NearestTower(transform.position, 2.4f);
            if (tower == null) { h.Toast("не к чему подключить", "рядом нет системного блока"); return; }
            h.Pockets.cables--;
            tower.Plug(this);
            h.Toast("подключено", kind == DeviceKind.Monitor ? "монитор" : kind == DeviceKind.Keyboard ? "клавиатура" : "мышь");
        }
    }

    /// <summary>One computer: a tower (or a laptop) and whatever is plugged into it.</summary>
    public class ComputerStation
    {
        public static readonly List<ComputerStation> All = new();
        public Device hub, monitor, keyboard, mouse;
        public bool On { get; private set; }
        public bool laptop;
        public Renderer screen; public Material screenOff, screenOn;
        public AudioClip beep;
        public ulong seed;
        public bool Ready => laptop || (On && monitor != null);
        public bool HasKeyboard => laptop || keyboard != null;
        public bool HasMouse => laptop || mouse != null;
        public static Material cableMat;

        public void SetPower(bool on) { On = on; RefreshScreen(); }

        public void RefreshScreen()
        {
            if (laptop) return;
            foreach (var d in new[] { monitor })
                if (d) { var s = d.GetComponentInChildren<ScreenGlow>(); if (s) s.Set(On); }
        }

        public void Plug(Device d)
        {
            if (d.link) Object.Destroy(d.link.gameObject);
            var go = new GameObject("Cable"); go.transform.SetParent(hub.transform.parent, false);
            var link = go.AddComponent<CableLink>(); link.a = d; link.b = hub; link.Init(cableMat);
            d.link = link; d.station = this;
            if (d.kind == DeviceKind.Monitor) monitor = d; else if (d.kind == DeviceKind.Keyboard) keyboard = d; else if (d.kind == DeviceKind.Mouse) mouse = d;
            RefreshScreen();
        }

        public void Unplug(Device d)
        {
            if (monitor == d) monitor = null; if (keyboard == d) keyboard = null; if (mouse == d) mouse = null;
            d.link = null; d.station = null;
            if (d.kind == DeviceKind.Monitor) { var s = d.GetComponentInChildren<ScreenGlow>(); if (s) s.Set(false); }
        }

        public static ComputerStation NearestTower(Vector3 p, float max)
        {
            ComputerStation best = null; float bd = max;
            foreach (var s in All) if (s.hub && !s.laptop) { float d = Vector3.Distance(s.hub.transform.position, p); if (d < bd) { bd = d; best = s; } }
            return best;
        }
    }

    /// <summary>The glowing face of a CRT when the computer runs.</summary>
    public class ScreenGlow : MonoBehaviour
    {
        public Renderer r; public Material off, on; float t; bool state;
        public void Set(bool v) { state = v; t = 0; if (r) r.sharedMaterial = v ? on : off; }
        void Update() { if (!state || !r) return; t += Time.deltaTime; if (t < 0.6f) r.enabled = (int)(t * 20) % 2 == 0; else r.enabled = true; }
    }

    /// <summary>A sagging cable between two ports (catenary-like curve), re-shaped every frame; snaps out when stretched.</summary>
    public class CableLink : MonoBehaviour
    {
        public Device a, b;
        LineRenderer lr; const int N = 18;
        public void Init(Material m)
        {
            lr = gameObject.AddComponent<LineRenderer>(); lr.positionCount = N; lr.widthMultiplier = 0.009f; lr.numCapVertices = 2; lr.numCornerVertices = 2;
            lr.sharedMaterial = m; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; lr.alignment = LineAlignment.View;
            Shape();
        }
        void LateUpdate()
        {
            if (!a || !b || !a.gameObject.activeInHierarchy) { Break(false); return; }
            if (Vector3.Distance(a.Port, b.Port) > 2.8f) { Break(true); return; }
            Shape();
        }
        void Shape()
        {
            Vector3 p0 = a.Port, p1 = b.Port; float len = Vector3.Distance(p0, p1);
            float sag = Mathf.Clamp(1.4f - len * 0.4f, 0.05f, 0.5f) * 0.6f;
            float floorY = Mathf.Min(p0.y, p1.y) - 2f;
            if (Physics.Raycast((p0 + p1) * 0.5f, Vector3.down, out var hit, 3f, (1 << 8) | (1 << 9), QueryTriggerInteraction.Ignore)) floorY = hit.point.y + 0.005f;
            for (int i = 0; i < N; i++)
            {
                float t = i / (N - 1f); var p = Vector3.Lerp(p0, p1, t);
                p.y -= sag * 4f * t * (1 - t) * (0.6f + len * 0.5f);
                p.y = Mathf.Max(p.y, floorY);
                lr.SetPosition(i, p);
            }
        }
        public System.Action<CableLink> onBreak;
        void Break(bool stretched)
        {
            if (a && a.station != null) a.station.Unplug(a);
            onBreak?.Invoke(this);
            Destroy(gameObject);
        }
    }
}
