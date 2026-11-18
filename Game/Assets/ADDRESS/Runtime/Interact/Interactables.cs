using System.Collections.Generic;
using Address.Placement;
using UnityEngine;

namespace Address.Interact
{
    /// <summary>What an interactable needs from the game (implemented by GameRoot).</summary>
    public interface IInteractHost
    {
        Camera Cam { get; }
        Transform Player { get; }
        Inventory Pockets { get; }
        void SitOn(Seat s);
        void Search(Container c);
        void UseComputer(ComputerStation s);
        void Toast(string big, string small);
        void PlaySfx(AudioClip c, Vector3 at, float vol = 1f);
    }

    /// <summary>Anything you can use with E. The prompt says what will happen.</summary>
    public abstract class Interactable : MonoBehaviour
    {
        public abstract string Verb(IInteractHost h);       // null = nothing to do right now
        public abstract void Use(IInteractHost h);
    }

    /// <summary>Two-three pockets: small things you carry (cables).</summary>
    public class Inventory
    {
        public int cables;                                   // coiled cables (each takes a slot)
        public readonly List<PlacedObject> items = new();    // small things put away (inactive objects)
        public const int Slots = 4;
        public bool Full => cables + items.Count >= Slots;
        public string Describe() => cables + items.Count == 0 ? "карманы пусты" : string.Join(", ", Names());
        public IEnumerable<string> Names() { for (int i = 0; i < cables; i++) yield return "кабель"; foreach (var p in items) yield return Name(p); }
        public static string Name(PlacedObject p)
        {
            if (!p || !p.profile) return "?";
            return p.profile.group switch
            {
                "keyboard" => "клавиатура", "mouse" => "мышь", "cable" => "кабель", "drink" => "кружка", "paper" => "бумаги", "stationery" => "канцелярия",
                "book" => "книга", "box" => "коробка", "plant" => "растение", "clock" => "часы", "sign" => "табличка", "trash" => "пакет", _ => p.profile.id.Replace('_', ' ')
            };
        }
    }

    // ------------------------------------------------------------------ light switch

    /// <summary>A wall switch that really switches its room's ceiling lights (and their glowing diffusers).</summary>
    public class LightSwitch : Interactable
    {
        public readonly List<Light> lights = new();
        public readonly List<Renderer> diffusers = new();
        public bool on = true;
        public static readonly HashSet<Light> SwitchedOff = new();
        public AudioClip click;
        Transform lever;

        public override string Verb(IInteractHost h) => on ? "выключить свет" : "включить свет";

        public override void Use(IInteractHost h)
        {
            on = !on;
            foreach (var l in lights) { if (!l) continue; if (on) SwitchedOff.Remove(l); else SwitchedOff.Add(l); l.enabled = on; }
            var mpb = new MaterialPropertyBlock();
            foreach (var r in diffusers)
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_EmissionColor", on ? Color.white * 1.0f : Color.black);
                if (!on) mpb.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.52f)); else mpb.Clear();
                r.SetPropertyBlock(mpb);
            }
            h.PlaySfx(click, transform.position, 0.8f);
        }
    }

    // ------------------------------------------------------------------ seats

    /// <summary>Chair / armchair / sofa / bench you can sit on - only when it stands upright on the floor.</summary>
    public class Seat : Interactable
    {
        public PlacedObject po;
        public Vector3 localSeat;        // seat surface point (local)
        public override string Verb(IInteractHost h) => Usable ? "сесть" : null;

        public bool Usable =>
            po && po.state == ObjectState.Normal && !po.intentionalAnomaly && po.transform.up.y > 0.97f &&
            (po.GetComponent<Rigidbody>() == null || po.GetComponent<Rigidbody>().linearVelocity.sqrMagnitude < 0.01f) &&
            Physics.Raycast(po.transform.position + Vector3.up * 0.05f, Vector3.down, 0.12f, (1 << 8) | (1 << 9), QueryTriggerInteraction.Ignore);

        public Vector3 SeatPoint => po.transform.TransformPoint(localSeat);

        /// <summary>The way a sitter looks: away from the backrest. Found from the mesh (the backrest is the tall
        /// part) and confirmed by which side is free (a sofa against a wall faces the room).</summary>
        public Vector3 Facing()
        {
            var f = po.transform.forward; f.y = 0; f.Normalize();
            var rs = po.GetComponentsInChildren<Renderer>(); var b = rs.Length > 0 ? rs[0].bounds : new Bounds(po.transform.position, Vector3.one);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var seat = SeatPoint; float seatY = seat.y;
            // probe a little above the seat on both sides: the side with the backrest is blocked
            int mask = (1 << 8) | (1 << 9);
            float Blocked(Vector3 dir)
            {
                float hit = 0;
                foreach (var h in new[] { 0.25f, 0.45f })
                {
                    var o = new Vector3(seat.x, seatY + h, seat.z);
                    foreach (var rh in Physics.RaycastAll(o, dir, 1.2f, mask, QueryTriggerInteraction.Ignore)) hit += 1.2f - rh.distance;
                }
                return hit;
            }
            float back = Blocked(-f), front = Blocked(f);
            return back >= front ? f : -f;
        }
        public override void Use(IInteractHost h) { if (Usable) h.SitOn(this); }

        public static Seat TryAttach(PlacedObject po)
        {
            var g = po.profile.group;
            if (g != "chair" && g != "armchair" && g != "sofa" && g != "bench" && g != "stool") return null;
            SurfaceRegion best = null;
            foreach (var s in po.profile.surfaces)
                if (s.height > 0.25f && s.height < 0.7f && (best == null || s.area > best.area)) best = s;
            float y = best != null ? best.height : 0.45f;
            Vector3 c = Vector3.zero;
            if (best != null && best.cells.Count > 0)
            {
                foreach (var i in best.cells) c += new Vector3(best.origin.x + (i % best.w + 0.5f) * best.cell, 0, best.origin.y + (i / best.w + 0.5f) * best.cell);
                c /= best.cells.Count;
            }
            var seat = po.gameObject.AddComponent<Seat>(); seat.po = po; seat.localSeat = new Vector3(c.x, y, c.z);
            return seat;
        }
    }

    // ------------------------------------------------------------------ containers

    /// <summary>Shelves and cabinets can be searched once; what is inside is decided by the address (same for everyone).</summary>
    public class Container : Interactable
    {
        public PlacedObject po;
        public bool searched;
        public string loot = "";          // "", "cable", "mouse", "keyboard"
        public Openable open;
        public override string Verb(IInteractHost h) => searched ? null : open && open.HasParts ? open.Verb : po.profile.group == "cabinet" ? "открыть и осмотреть" : po.profile.group == "trash" ? "порыться в пакете" : po.profile.group == "box" || po.profile.group == "container" ? "заглянуть внутрь" : "осмотреть полки";
        public override void Use(IInteractHost h) { if (!searched) h.Search(this); }

        public static Container TryAttach(PlacedObject po, ulong seed)
        {
            var g = po.profile.group;
            if (g != "shelf" && g != "cabinet" && g != "cart" && g != "box" && g != "container" && g != "trash") return null;
            var c = po.gameObject.AddComponent<Container>(); c.po = po;
            c.open = Openable.Attach(po.gameObject, po.uid);
            var r = new Address.Core.Rng(Address.Core.Hashing.Combine(seed, (ulong)po.uid * 2654435761UL));
            float v = r.Value;
            c.loot = g == "trash" ? (v < 0.25f ? "cable" : "") : v < 0.36f ? "cable" : v < 0.50f ? "mouse" : v < 0.60f ? "keyboard" : "";
            return c;
        }
    }

    /// <summary>A coil of cable lying around: E puts it into a pocket.</summary>
    public class CablePickup : Interactable
    {
        public override string Verb(IInteractHost h) => h.Pockets.Full ? "карманы полны" : "взять кабель в карман";
        public override void Use(IInteractHost h)
        {
            if (h.Pockets.Full) return;
            h.Pockets.cables++;
            h.Toast("+ кабель", h.Pockets.Describe());
            var po = GetComponent<PlacedObject>(); if (po) po.gameObject.SetActive(false); else gameObject.SetActive(false);
        }
    }
}
