using System.Linq;
using Address.Core;
using Address.Placement;
using Address.World;
using UnityEngine;

namespace Address.Interact
{
    /// <summary>After a level is built: turns placed objects into things you can use (deterministic per address).</summary>
    public static class InteractBuilder
    {
        static Material screenOff, screenOn, cable;

        public static void Attach(LevelBuilder.Result level)
        {
            ComputerStation.All.Clear();
            LightSwitch.SwitchedOff.Clear();
            var lit = Shader.Find("Universal Render Pipeline/Lit"); var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (!cable && lit) { cable = new Material(lit) { name = "cable" }; cable.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.03f)); cable.SetFloat("_Smoothness", 0.35f); }
            if (!screenOff && unlit) { screenOff = new Material(unlit) { name = "screen_off" }; screenOff.SetColor("_BaseColor", new Color(0.015f, 0.02f, 0.02f)); }
            if (!screenOn && unlit) { screenOn = new Material(unlit) { name = "screen_on" }; screenOn.SetColor("_BaseColor", new Color(0.05f, 0.42f, 0.45f)); }
            ComputerStation.cableMat = cable;
            var rng = new Rng(Hashing.Combine(level.address.BaseSeed, Hashing.Fnv64("interact:" + level.address.Generation)));
            ulong seed = rng.NextULong();

            var allLights = level.root.GetComponentsInChildren<Light>(true).Where(l => l.transform.parent && l.transform.parent.name == "Troffer").ToList();
            foreach (var po in level.engine.placed)
            {
                if (!po || !po.profile) continue;
                if (po.profile.support == SupportType.Wall)
                    foreach (var r in po.GetComponentsInChildren<Renderer>()) { r.receiveShadows = false; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                switch (po.profile.group)
                {
                    case "switch":
                    {
                        var room = level.rooms.FirstOrDefault(r => { var b = r.bounds; b.Expand(0.6f); return b.Contains(po.transform.position); });
                        if (room == null) break;
                        var sw = po.gameObject.AddComponent<LightSwitch>(); sw.click = Sfx.Click;
                        var b2 = room.bounds; b2.Expand(new Vector3(0.2f, 1f, 0.2f));
                        foreach (var l in allLights) if (b2.Contains(l.transform.position))
                        {
                            sw.lights.Add(l);
                            foreach (var r in l.transform.parent.GetComponentsInChildren<Renderer>())
                                if (r.sharedMaterials.Any(m => m && (m.name.Contains("light") || m.name.Contains("diffuser") || m.name.Contains("Light")))) sw.diffusers.Add(r);
                        }
                        break;
                    }
                    case "cable": po.gameObject.AddComponent<CablePickup>(); break;
                    case "pctower":
                    {
                        var d = AddDevice(po, DeviceKind.Tower, new Vector3(-0.04f, 0.27f, -0.235f));
                        var st = new ComputerStation { hub = d, beep = Sfx.Beep, seed = Hashing.Combine(seed, (ulong)po.uid) };
                        d.station = st; ComputerStation.All.Add(st);
                        break;
                    }
                    case "computer":
                        if (po.profile.id == "classic_laptop")
                        {
                            var d = AddDevice(po, DeviceKind.Laptop, Vector3.zero);
                            var st = new ComputerStation { hub = d, laptop = true, seed = Hashing.Combine(seed, (ulong)po.uid) }; st.SetPower(true);
                            d.station = st; ComputerStation.All.Add(st);
                        }
                        else if (po.profile.id == "crt_monitor")
                        {
                            AddDevice(po, DeviceKind.Monitor, new Vector3(0, 0.2f, -0.2f));
                            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(q.GetComponent<Collider>());
                            q.name = "screen"; q.transform.SetParent(po.transform, false);
                            q.transform.localPosition = new Vector3(0, 0.255f, 0.2065f); q.transform.localRotation = Quaternion.Euler(0, 180, 0); q.transform.localScale = new Vector3(0.30f, 0.25f, 1);
                            var r = q.GetComponent<Renderer>(); r.sharedMaterial = screenOff; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            var g = q.AddComponent<ScreenGlow>(); g.r = r; g.off = screenOff; g.on = screenOn;
                        }
                        break;
                    case "keyboard": AddDevice(po, DeviceKind.Keyboard, new Vector3(0, 0.03f, -0.085f)); break;
                    case "mouse": AddDevice(po, DeviceKind.Mouse, new Vector3(0, 0.01f, 0.065f)); break;
                    default:
                        if (Seat.TryAttach(po) == null) Container.TryAttach(po, seed);
                        break;
                }
            }
            // peripherals already on a desk next to a tower: most of them are plugged in, some computers are running
            foreach (var st in ComputerStation.All.Where(s => !s.laptop))
            {
                foreach (var d in level.root.GetComponentsInChildren<Device>())
                {
                    if (d.kind == DeviceKind.Tower || d.kind == DeviceKind.Laptop || d.link != null) continue;
                    if (Vector3.Distance(d.transform.position, st.hub.transform.position) > 1.9f) continue;
                    if (ComputerStation.NearestTower(d.transform.position, 1.9f) != st) continue;
                    if (rng.Chance(0.75f)) st.Plug(d);
                }
                if (st.monitor != null && rng.Chance(0.3f)) st.SetPower(true);
            }
        }

        /// <summary>Make a runtime-spawned object usable (found peripherals, cables).</summary>
        public static void Decorate(PlacedObject po)
        {
            switch (po.profile.group)
            {
                case "keyboard": AddDevice(po, DeviceKind.Keyboard, new Vector3(0, 0.03f, -0.085f)); break;
                case "mouse": AddDevice(po, DeviceKind.Mouse, new Vector3(0, 0.01f, 0.065f)); break;
                case "cable": po.gameObject.AddComponent<CablePickup>(); break;
            }
        }

        static Device AddDevice(PlacedObject po, DeviceKind k, Vector3 port)
        {
            var d = po.gameObject.AddComponent<Device>(); d.kind = k; d.po = po; d.portLocal = port; return d;
        }
    }

    /// <summary>Small synthesised UI/world sounds (no assets, no loops).</summary>
    public static class Sfx
    {
        static AudioClip click, beep, rustle;
        public static AudioClip Click => click ? click : click = Make("switch_click", 0.06f, (t, i) => (Random01(i) * 2 - 1) * Mathf.Exp(-t * 90f) * 0.8f + Mathf.Sin(t * 2 * Mathf.PI * 2200f) * Mathf.Exp(-t * 160f) * 0.3f);
        public static AudioClip Beep => beep ? beep : beep = Make("pc_beep", 0.18f, (t, i) => Mathf.Sign(Mathf.Sin(t * 2 * Mathf.PI * 990f)) * 0.18f * (t < 0.17f ? 1 : 0));
        public static AudioClip Rustle => rustle ? rustle : rustle = Make("rustle", 1.1f, (t, i) =>
        {
            float env = 0.5f + 0.5f * Mathf.Sin(t * 23f) * Mathf.Sin(t * 7.3f);
            return (Random01(i) * 2 - 1) * 0.35f * env * Mathf.Clamp01(1.1f - t) * Mathf.Clamp01(t * 20f);
        });
        static AudioClip thud, tick;
        public static AudioClip DistantThud => thud ? thud : thud = Make("distant_thud", 1.6f, (t, i) => (Mathf.Sin(t * 2 * Mathf.PI * (48f - t * 14f)) * 0.9f + (Random01(i) * 2 - 1) * 0.25f * Mathf.Exp(-t * 12f)) * Mathf.Exp(-t * 2.6f) * Mathf.Clamp01(t * 60f));
        public static AudioClip MetalTick => tick ? tick : tick = Make("metal_tick", 1.2f, (t, i) => (Mathf.Sin(t * 2 * Mathf.PI * 820f) * 0.5f + Mathf.Sin(t * 2 * Mathf.PI * 1310f) * 0.35f + Mathf.Sin(t * 2 * Mathf.PI * 2215f) * 0.2f) * Mathf.Exp(-t * 5.5f) * Mathf.Clamp01(t * 400f) * 0.6f);
        static float Random01(int i) { uint x = (uint)i * 747796405u + 2891336453u; x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u; return ((x >> 22) ^ x) / 4294967295f; }
        static AudioClip Make(string n, float len, System.Func<float, int, float> f)
        {
            int sr = 44100, c = (int)(sr * len); var d = new float[c]; float lp = 0;
            for (int i = 0; i < c; i++) { float v = f((float)i / sr, i); lp += (v - lp) * 0.6f; d[i] = lp; }
            var a = AudioClip.Create(n, c, 1, sr, false); a.SetData(d, 0); return a;
        }
    }
}
