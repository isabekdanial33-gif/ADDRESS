using System.Collections.Generic;
using System.Linq;
using Address.Core;
using UnityEngine;

namespace Address.Placement
{
    /// <summary>
    /// Scenario composer: builds logical micro-scenes ("someone was here a minute ago") out of anchors and children.
    /// Each step is a placement request through the engine, so every object passes all checks.
    /// </summary>
    public class Scenarios
    {
        readonly PlacementEngine E;
        readonly ProfileDatabase db;
        public Scenarios(PlacementEngine engine, ProfileDatabase db) { E = engine; this.db = db; }

        string Style => E.style;
        string Mood => E.mood;

        PlacementProfile Pick(string group, Rng rng, string room = null) => db.Pick(group, rng, Style, Mood, room);

        PlacedObject Place(PlacementProfile p, System.Func<Rng, int, Pose?> cand, Rng rng, string tag, PlacedObject parent = null,
            PlacedObject anchor = null, ObjectState state = ObjectState.Normal, bool ignoreNav = false, int tries = -1)
        {
            if (p == null) return null;
            return E.TryPlace(new PlacementEngine.Request
            {
                profile = p, candidate = cand, supportParent = parent, anchor = anchor, state = state, tag = tag,
                ignoreNavReservation = ignoreNav, tries = tries
            }, rng);
        }

        // ------------------------------------------------------------------ workstation

        /// <summary>Desk (against a wall or free-standing) + chair tucked in + computer + desk clutter.</summary>
        public PlacedObject Workstation(RoomSpec room, Rng rng, bool againstWall)
        {
            var deskP = Pick("desk", rng, room.kind);
            if (deskP == null) return null;
            var desk = againstWall
                ? Place(deskP, E.AgainstWall(deskP, room.walls), rng, "workstation/desk")
                : Place(deskP, E.OnFloor(deskP, Shrink(room.bounds, 0.9f)), rng, "workstation/desk");
            if (desk == null) return null;
            var dp = desk.profile;

            // chair: in front of the desk (+Z side), facing the desk, pushed in as far as geometry allows
            var chairP = Pick("chair", rng, room.kind);
            if (chairP != null)
            {
                bool messy = rng.Chance(chairP.fallenChance);
                if (messy)
                {
                    var fallen = Quaternion.Euler(chairP.fallenEuler);
                    Place(chairP, (r, i) =>
                    {
                        var basePose = E.RelativeTo(desk, new Vector3(-dp.size.x * 0.4f, 0, dp.size.z * 0.5f + 0.5f), new Vector3(dp.size.x * 0.4f, 0, dp.size.z * 0.5f + 1.0f), 180, 180, false)(r, i);
                        if (basePose == null) return null;
                        var bp = basePose.Value; bp.rotation = bp.rotation * fallen; return bp;
                    }, rng, "workstation/chair-fallen", anchor: desk, state: ObjectState.Fallen);
                }
                else
                {
                    float zIn = dp.size.z * 0.5f - 0.30f, zOut = dp.size.z * 0.5f + chairP.size.z * 0.5f + 0.35f;
                    Place(chairP, E.RelativeTo(desk, new Vector3(-0.12f, 0, zIn), new Vector3(0.12f, 0, zOut), 180, 14), rng,
                        "workstation/chair", anchor: desk, ignoreNav: true, tries: 22);
                }
            }

            // desk top
            var topFilter = (System.Func<SurfaceRegion, bool>)(s => s.kind == "top");
            float hx = dp.size.x * 0.5f, hz = dp.size.z * 0.5f;
            var back = new Rect(-hx * 0.6f, -hz, hx * 1.2f, hz);           // back half (z < 0)
            var front = new Rect(-hx * 0.5f, 0, hx, hz);                   // front half
            var computer = Pick(rng.Chance(0.7f) ? "computer" : "tv", rng);
            PlacedObject comp = null;
            if (computer != null) comp = Place(computer, E.OnSurface(computer, desk, topFilter, back, 0, 10), rng, "workstation/computer", desk);
            Keyboard(desk, comp, rng);
            if (rng.Chance(0.5f)) { var lamp = Pick("desklamp", rng); if (lamp != null) Place(lamp, E.OnSurface(lamp, desk, topFilter, back, rng.Range(-40f, 40f), 20), rng, "workstation/lamp", desk); }
            int papers = rng.Range(0, 5);
            for (int i = 0; i < papers; i++) { var pp = Pick("paper", rng); if (pp != null) Place(pp, E.OnSurface(pp, desk, topFilter, null, rng.Range(-25f, 25f), 15), rng, "workstation/paper", desk); }
            int st = rng.Range(0, 4);
            for (int i = 0; i < st; i++) { var sp = Pick("stationery", rng); if (sp != null) Place(sp, E.OnSurface(sp, desk, topFilter, front), rng, "workstation/stationery", desk); }
            if (rng.Chance(0.55f)) { var d = Pick("drink", rng); if (d != null) Place(d, E.OnSurface(d, desk, topFilter), rng, "workstation/drink", desk); }
            if (rng.Chance(0.15f)) { var c = Pick("clock", rng); if (c != null) Place(c, E.OnSurface(c, desk, topFilter, back, rng.Range(-30f, 30f), 10), rng, "workstation/clock", desk); }
            return desk;
        }

        /// <summary>Two free-standing desks back to back (open-plan office island), each with its own chair and clutter.</summary>
        public void DeskIsland(RoomSpec room, Rng rng)
        {
            var deskP = Pick("desk", rng, room.kind);
            if (deskP == null) return;
            var a = Place(deskP, E.OnFloor(deskP, Shrink(room.bounds, 1.6f)), rng, "island/desk-a", tries: 24);
            if (a == null) return;
            var dp = a.profile;
            var b = Place(deskP, E.RelativeTo(a, new Vector3(0, 0, -dp.size.z - 0.01f), new Vector3(0, 0, -dp.size.z - 0.03f), 180, 0, false), rng, "island/desk-b", anchor: a, ignoreNav: false, tries: 4);
            foreach (var desk in new[] { a, b })
            {
                if (desk == null) continue;
                DressDesk(desk, room, rng);
            }
        }

        /// <summary>
        /// Semantic pair: a keyboard goes on the desk in FRONT of a desktop monitor (between monitor and chair),
        /// centred on it within 12 cm and facing the same way. Laptops have their own keyboard.
        /// </summary>
        void Keyboard(PlacedObject desk, PlacedObject monitor, Rng rng)
        {
            if (monitor == null || monitor.profile.id == "classic_laptop" || monitor.profile.group != "computer") return;
            var kb = Pick("keyboard", rng); if (kb == null) return;
            var local = desk.transform.InverseTransformPoint(monitor.transform.position);
            float frontOfMonitor = local.z + monitor.profile.size.z * 0.5f + 0.02f;
            var area = new Rect(local.x - 0.12f, frontOfMonitor, 0.24f, desk.profile.size.z * 0.5f - frontOfMonitor);
            // peripherals are sometimes missing: they lie somewhere else in the place (shelves, boxes, drawers)
            if (rng.Chance(0.72f)) Place(kb, E.OnSurface(kb, desk, s => s.kind == "top", area, 0, 4), rng, "workstation/keyboard", desk, tries: 14);
            var mouse = Pick("mouse", rng);
            if (mouse != null && rng.Chance(0.7f))
            {
                var marea = new Rect(local.x + 0.26f, frontOfMonitor, 0.16f, desk.profile.size.z * 0.5f - frontOfMonitor);
                Place(mouse, E.OnSurface(mouse, desk, s => s.kind == "top", marea, 0, 6), rng, "workstation/mouse", desk, tries: 12);
            }
            // the system unit stands on the floor beside the desk
            var tower = Pick("pctower", rng);
            if (tower != null && monitor.profile.id == "crt_monitor")
            {
                float hx = desk.profile.size.x * 0.5f + tower.size.x * 0.5f + 0.05f, hz = desk.profile.size.z * 0.5f;
                float side = rng.Chance(0.5f) ? 1 : -1;
                Place(tower, E.RelativeTo(desk, new Vector3(side * hx, 0, -hz * 0.4f), new Vector3(side * (hx + 0.1f), 0, hz * 0.2f), 0, 6, false), rng, "workstation/tower", anchor: desk, tries: 10);
            }
        }

        /// <summary>Chair + desk-top items for an already placed desk.</summary>
        public void DressDesk(PlacedObject desk, RoomSpec room, Rng rng)
        {
            var dp = desk.profile;
            var chairP = Pick("chair", rng, room.kind);
            if (chairP != null)
            {
                float zIn = dp.size.z * 0.5f - 0.30f, zOut = dp.size.z * 0.5f + chairP.size.z * 0.5f + 0.35f;
                Place(chairP, E.RelativeTo(desk, new Vector3(-0.12f, 0, zIn), new Vector3(0.12f, 0, zOut), 180, 14), rng, "island/chair", anchor: desk, ignoreNav: true, tries: 22);
            }
            var topFilter = (System.Func<SurfaceRegion, bool>)(s => s.kind == "top");
            float hx = dp.size.x * 0.5f, hz = dp.size.z * 0.5f;
            var back = new Rect(-hx * 0.6f, -hz, hx * 1.2f, hz);
            var computer = Pick(rng.Chance(0.75f) ? "computer" : "tv", rng);
            PlacedObject comp = null;
            if (computer != null) comp = Place(computer, E.OnSurface(computer, desk, topFilter, back, 0, 10), rng, "island/computer", desk);
            Keyboard(desk, comp, rng);
            int n = rng.Range(1, 6);
            for (int i = 0; i < n; i++)
            {
                var g = rng.Pick(new[] { "paper", "paper", "stationery", "drink", "clock" });
                var pp = Pick(g, rng); if (pp != null) Place(pp, E.OnSurface(pp, desk, topFilter, null, rng.Range(-25f, 25f), 15), rng, "island/item", desk);
            }
        }

        // ------------------------------------------------------------------ storage

        public PlacedObject ShelfUnit(RoomSpec room, Rng rng, float fill = 0.6f)
        {
            var sp = Pick("shelf", rng, room.kind) ?? Pick("cabinet", rng, room.kind);
            if (sp == null) return null;
            var shelf = Place(sp, E.AgainstWall(sp, room.walls), rng, "storage/shelf");
            if (shelf == null) return null;
            var shelfGroups = new HashSet<string> { "paper", "box", "crate", "container", "stationery", "drink", "suitcase", "boots", "kettle", "clock", "toy", "camera", "tv", "plant" };
            foreach (var reg in shelf.profile.surfaces)
            {
                if (reg.kind == "inside") continue;
                // a shelf board is filled like a real one: several items, each chosen among those that physically fit
                int n = Mathf.Clamp(Mathf.RoundToInt(reg.area * 45f * fill * rng.Range(0.6f, 1.3f)), 1, 14);
                var regCopy = reg;
                // a run of the same item (row of binders, row of boxes) reads as "real storage"
                PlacementProfile run = null;
                for (int i = 0; i < n; i++)
                {
                    var p = (run != null && rng.Chance(0.6f)) ? run : db.PickAny(rng, Style, Mood, q =>
                        shelfGroups.Contains(q.group) && q.support != SupportType.Wall && q.support != SupportType.Ceiling &&
                        q.size.y < regCopy.clearance - 0.01f && q.size.x * q.size.z < regCopy.area * 0.7f && q.massClass != MassClass.Heavy);
                    if (p == null) break;
                    run ??= p;
                    float yaw = rng.Chance(0.7f) ? rng.Range(-6f, 6f) : 90 + rng.Range(-6f, 6f);
                    Place(p, E.OnSurface(p, shelf, s2 => s2 == regCopy, null, yaw), rng, "storage/shelf-item", shelf, tries: 10);
                }
            }
            return shelf;
        }

        /// <summary>Stack of stackable objects (boxes/crates/containers) near a wall or in a corner.</summary>
        public void Stack(RoomSpec room, Rng rng, int height)
        {
            var g = rng.Pick(new[] { "box", "box", "crate", "container", "suitcase" });
            var p = Pick(g, rng);
            if (p == null) return;
            var area = WallBand(room, rng, Mathf.Max(p.size.x, p.size.z) + 0.15f);
            var baseObj = Place(p, E.OnFloor(p, area), rng, "stack/base");
            var below = baseObj;
            for (int i = 1; i < height && below != null; i++)
            {
                var q = rng.Chance(0.75f) ? below.profile : Pick(g, rng);
                if (q == null || !q.canStack) break;
                below = Place(q, E.OnTopOf(q, below), rng, "stack/level" + i, below, tries: 10);
            }
        }

        // ------------------------------------------------------------------ lounge

        public void Lounge(RoomSpec room, Rng rng)
        {
            var sofaP = Pick("sofa", rng, "lounge");
            PlacedObject sofa = sofaP != null ? Place(sofaP, E.AgainstWall(sofaP, room.walls), rng, "lounge/sofa") : null;
            PlacedObject table = null;
            var tableP = Pick("coffeetable", rng) ?? Pick("smalltable", rng);
            if (sofa != null && tableP != null)
            {
                float z0 = sofa.profile.size.z * 0.5f + 0.35f + tableP.size.z * 0.5f;
                table = Place(tableP, E.RelativeTo(sofa, new Vector3(-0.1f, 0, z0), new Vector3(0.1f, 0, z0 + 0.2f), 90, 3, false), rng, "lounge/table", anchor: sofa);
                if (table == null) table = Place(tableP, E.RelativeTo(sofa, new Vector3(-0.1f, 0, z0), new Vector3(0.1f, 0, z0 + 0.25f), 0, 3, false), rng, "lounge/table", anchor: sofa);
            }
            else if (tableP != null) table = Place(tableP, E.OnFloor(tableP, Shrink(room.bounds, 1.2f)), rng, "lounge/table");

            if (table != null)
            {
                // armchairs facing the table from its sides
                int chairs = rng.Range(1, 3);
                for (int i = 0; i < chairs; i++)
                {
                    var ap = Pick("armchair", rng) ?? Pick("chair", rng, "lounge");
                    if (ap == null) break;
                    float side = (i == 0 ? 1 : -1);
                    var tp = table.profile;
                    // place on table's local +/-X side, facing the table
                    Place(ap, (r, k) =>
                    {
                        float d = Mathf.Max(tp.size.x, tp.size.z) * 0.5f + ap.size.z * 0.5f + r.Range(0.25f, 0.6f);
                        var local = new Vector3(side * d, 0, r.Range(-0.3f, 0.3f));
                        var t = table.transform;
                        return new Pose(t.TransformPoint(local), t.rotation * PlacementEngine.Yaw(side > 0 ? -90 : 90) * PlacementEngine.Yaw(r.Range(-20f, 20f)), 1f);
                    }, rng, "lounge/armchair", anchor: table);
                }
                int items = rng.Range(0, 4);
                for (int i = 0; i < items; i++)
                {
                    var ip = Pick(rng.Pick(new[] { "paper", "drink", "toy", "camera", "clock", "kettle" }), rng);
                    if (ip != null) Place(ip, E.OnSurface(ip, table, s => s.kind == "top"), rng, "lounge/table-item", table);
                }
            }
            // side table with a TV or a plant
            var st = Pick("smalltable", rng);
            if (st != null)
            {
                var side = Place(st, E.AgainstWall(st, room.walls, 0.03f), rng, "lounge/sidetable");
                if (side != null)
                {
                    var tv = Pick(rng.Chance(0.5f) ? "tv" : "clock", rng);
                    if (tv != null) Place(tv, E.OnSurface(tv, side, s => s.kind == "top", null, 0, 12), rng, "lounge/sidetable-item", side);
                }
            }
            Ceiling(room, rng, rng.Chance(0.5f) ? "fan" : "pendant");
        }

        // ------------------------------------------------------------------ singles

        public void AgainstWallSingle(RoomSpec room, Rng rng, string group)
        {
            var p = Pick(group, rng, room.kind); if (p == null) return;
            Place(p, E.AgainstWall(p, room.walls, 0.02f), rng, "single/" + group);
        }

        public void FloorSingle(RoomSpec room, Rng rng, string group, bool nearWall = true)
        {
            var p = Pick(group, rng, room.kind); if (p == null) return;
            bool fallen = rng.Chance(p.fallenChance);
            if (fallen)
            {
                var rot = Quaternion.Euler(p.fallenEuler);
                Place(p, E.OnFloor(p, nearWall ? WallBand(room, rng, 1.0f) : Shrink(room.bounds, 0.4f), rot), rng, "single-fallen/" + group, state: ObjectState.Fallen);
            }
            else Place(p, E.OnFloor(p, nearWall ? WallBand(room, rng, Mathf.Max(p.size.x, p.size.z) + 0.1f) : Shrink(room.bounds, 0.4f)), rng, "single/" + group);
        }

        public void WallDecor(RoomSpec room, Rng rng, string group)
        {
            var p = Pick(group, rng, room.kind); if (p == null) return;
            E.TryPlace(new PlacementEngine.Request { profile = p, candidate = E.OnWall(p, room.walls), wallSupport = true, tag = "wall/" + group }, rng);
        }

        public void Ceiling(RoomSpec room, Rng rng, string group)
        {
            var p = Pick(group, rng, room.kind); if (p == null) return;
            E.TryPlace(new PlacementEngine.Request { profile = p, candidate = E.OnCeiling(p, Shrink(room.bounds, 0.6f)), ceilingSupport = true, tag = "ceiling/" + group }, rng);
        }

        // ------------------------------------------------------------------ intentional anomaly

        /// <summary>
        /// Objects floating in a slow ring at a fixed height band. Collision is still enforced (they never intersect anything);
        /// they are flagged as intentional so the auditor does not count missing support as an error.
        /// </summary>
        public List<PlacedObject> FloatingRing(RoomSpec room, Rng rng, int count)
        {
            var list = new List<PlacedObject>();
            var c = room.bounds.center; c.y = room.FloorY;
            float radius = Mathf.Min(room.bounds.size.x, room.bounds.size.z) * 0.25f;
            float baseH = rng.Range(0.9f, 1.3f);
            for (int i = 0; i < count; i++)
            {
                var p = db.PickAny(rng, Style, Mood, q => q.massClass == MassClass.Medium && q.support == SupportType.Floor && q.size.y < 1.1f);
                if (p == null) break;
                float ang = i * Mathf.PI * 2f / count;
                var po = E.TryPlace(new PlacementEngine.Request
                {
                    profile = p, anomaly = true, tag = "anomaly/floating-ring", collisionMargin = 0.06f,
                    candidate = (r, k) =>
                    {
                        var pos = c + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * (radius + k * 0.05f) + Vector3.up * (baseH + Mathf.Sin(ang * 2) * 0.15f);
                        var rot = Quaternion.Euler(r.Range(-25f, 25f), ang * Mathf.Rad2Deg + 90, r.Range(-25f, 25f));
                        return new Pose(pos, rot, 1f);
                    }
                }, rng);
                if (po != null) { po.gameObject.AddComponent<AnomalyMotion>().Setup(c, 0.08f + rng.Value * 0.05f, 0.04f); list.Add(po); }
            }
            return list;
        }

        // ------------------------------------------------------------------ areas

        public static Bounds Shrink(Bounds b, float m)
        {
            var s = b.size; s.x = Mathf.Max(0.1f, s.x - 2 * m); s.z = Mathf.Max(0.1f, s.z - 2 * m);
            return new Bounds(b.center, s);
        }

        /// <summary>A band along a random wall (objects that live near walls: boxes, bins, signs).</summary>
        public static Bounds WallBand(RoomSpec room, Rng rng, float depth)
        {
            var b = room.bounds;
            int side = rng.Range(0, 4);
            var min = b.min; var max = b.max;
            switch (side)
            {
                case 0: max.x = min.x + depth; break;
                case 1: min.x = max.x - depth; break;
                case 2: max.z = min.z + depth; break;
                default: min.z = max.z - depth; break;
            }
            var res = new Bounds(); res.SetMinMax(min, max); return res;
        }
    }

    /// <summary>Slow orbit/bob for intentionally floating objects (visual only, deterministic phase).</summary>
    public class AnomalyMotion : MonoBehaviour
    {
        Vector3 pivot; float speed, bob; Vector3 start; float phase;
        public void Setup(Vector3 pivot, float speed, float bob) { this.pivot = pivot; this.speed = speed; this.bob = bob; start = transform.position; phase = start.x * 1.7f + start.z; }
        void Update()
        {
            float t = Time.time;
            // vertical bob only: the placement engine reserved +/- bob around the object, so it never touches anything
            var p = transform.position; p.y = start.y + Mathf.Sin(t * 0.6f + phase) * bob; transform.position = p;
        }
    }
}
