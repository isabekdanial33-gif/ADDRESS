using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Address.Core;
using Address.Placement;
using UnityEngine;

namespace Address.World
{
    public enum ExitForm { HandlelessDoor, Elevator, VentHatch, Archway }
    public enum ExitRole { Forward, Loop, False }

    /// <summary>A planned exit: where it is, what it looks like, where it leads. Pure data, decided before furnishing.</summary>
    public class ExitPlan
    {
        public int index; public int roomId; public ExitForm form; public ExitRole role;
        public WallSeg wall; public float along; public float width;
        public Vector3 position;     // bottom centre on the wall face
        public Vector3 normal;       // into the room
        public Vector3 Approach => position + normal * 0.9f;
        public bool reachable;       // verified after furnishing
        public Vector2Int approachCell;
        public ExitTrigger instance;
    }

    /// <summary>
    /// Chooses 2-4 hidden exits: far from the start in the room graph, dead ends preferred, never in the start room.
    /// Exit #0 is always a FORWARD exit in the most hidden room. Wall slots avoid openings and corners.
    /// </summary>
    public static class ExitPlanner
    {
        public static float Width(ExitForm f) => f switch { ExitForm.Elevator => 1.6f, ExitForm.VentHatch => 0.7f, ExitForm.Archway => 1.8f, _ => 0.95f };

        public static ExitForm? ForceForm;   // set by the builder for worlds with a single exit type

        public static List<ExitPlan> Plan(LayoutGenerator.Layout L, Rng rng)
        {
            // room graph distances
            var adj = L.rooms.ToDictionary(r => r.id, r => new List<int>());
            foreach (var o in L.openings) { adj[o.a].Add(o.b); adj[o.b].Add(o.a); }
            var dist = new Dictionary<int, int> { [L.startRoom] = 0 };
            var q = new Queue<int>(); q.Enqueue(L.startRoom);
            while (q.Count > 0) { int c = q.Dequeue(); foreach (var nb in adj[c]) if (!dist.ContainsKey(nb)) { dist[nb] = dist[c] + 1; q.Enqueue(nb); } }
            int maxD = dist.Values.Max();
            var scored = L.rooms.Where(r => r.id != L.startRoom && dist.ContainsKey(r.id))
                .Select(r => (room: r, score: dist[r.id] + (adj[r.id].Count == 1 ? 2.5f : 0f) + rng.Range(0f, 1.5f)))
                .OrderByDescending(t => t.score).ToList();
            // keep exits out of rooms adjacent to the start when possible
            var far = scored.Where(t => dist[t.room.id] >= Mathf.Min(2, maxD)).ToList();
            if (far.Count >= 2) scored = far;

            int n = Mathf.Clamp(rng.Range(2, 5), 1, scored.Count);
            var plans = new List<ExitPlan>();
            var usedRooms = new HashSet<int>();
            foreach (var (room, _) in scored)
            {
                if (plans.Count >= n) break;
                if (usedRooms.Contains(room.id)) continue;
                var form = ForceForm ?? PickForm(rng);
                var plan = Slot(room, form, rng);
                if (plan == null && form != ExitForm.VentHatch) plan = Slot(room, form = ExitForm.VentHatch, rng);
                if (plan == null) continue;
                plan.index = plans.Count;
                plan.role = plans.Count == 0 ? ExitRole.Forward : PickRole(rng);
                plans.Add(plan); usedRooms.Add(room.id);
            }
            return plans;
        }

        /// <summary>Exits of an open park: on the edge walls, far from the start and from each other. The first one is
        /// a tiled archway that leads (without any door) down into the Poolrooms.</summary>
        public static List<ExitPlan> PlanPark(LayoutGenerator.Layout L, Rng rng, Vector3 start)
        {
            var plans = new List<ExitPlan>(); var room = L.rooms[0];
            int n = rng.Range(3, 5);
            for (int tries = 0; tries < 200 && plans.Count < n; tries++)
            {
                var form = ExitForm.Archway;   // the park has only low glass at its edge: every way out is a pavilion
                var p = Slot(room, form, rng); if (p == null) continue;
                if (Vector3.Distance(p.position, start) < 12f) continue;
                if (plans.Any(o => Vector3.Distance(o.position, p.position) < 9f)) continue;
                p.index = plans.Count; p.role = plans.Count == 0 ? ExitRole.Forward : PickRole(rng);
                if (p.role == ExitRole.False && form == ExitForm.Archway) p.role = ExitRole.Forward;
                plans.Add(p);
            }
            return plans;
        }

        /// <summary>Level Run: a single door in the wall at the very end of the corridor.</summary>
        public static List<ExitPlan> PlanEnd(LayoutGenerator.Layout L)
        {
            var room = L.rooms[L.rooms.Count - 1];
            var ends = room.spec.walls.Where(w => w.normal.z < -0.9f).OrderByDescending(w => w.Length).ToList();
            if (ends.Count == 0) return null;
            var wall = ends[0];
            float wd = Width(ExitForm.HandlelessDoor), t = wall.Length * 0.5f;
            return new List<ExitPlan> { new ExitPlan { index = 0, role = ExitRole.Forward, roomId = room.id, form = ExitForm.HandlelessDoor, wall = wall, along = t, width = wd, position = wall.a + wall.Dir * t, normal = wall.normal } };
        }

        static ExitForm PickForm(Rng rng) { float r = rng.Value; return r < 0.5f ? ExitForm.HandlelessDoor : r < 0.75f ? ExitForm.Elevator : ExitForm.VentHatch; }
        static ExitRole PickRole(Rng rng) { float r = rng.Value; return r < 0.55f ? ExitRole.Forward : r < 0.8f ? ExitRole.Loop : ExitRole.False; }

        static ExitPlan Slot(LayoutGenerator.Room room, ExitForm form, Rng rng)
        {
            float w = Width(form);
            var walls = room.spec.walls.Where(s => s.Length >= w + 0.5f).ToList();
            if (walls.Count == 0) return null;
            var wall = walls[rng.Range(0, walls.Count)];
            float t = rng.Range(w * 0.5f + 0.25f, wall.Length - w * 0.5f - 0.25f);
            return new ExitPlan
            {
                roomId = room.id, form = form, wall = wall, along = t, width = w,
                position = wall.a + wall.Dir * t, normal = wall.normal
            };
        }
    }

    /// <summary>
    /// Runtime exit: opens on interaction (leaf swings, elevator doors slide, hatch grille drops), then the dark
    /// space behind it pulls the player through when they step in.
    /// </summary>
    public class ExitTrigger : MonoBehaviour
    {
        public ExitPlan plan;
        public bool IsOpen { get; private set; }
        public bool Hidden { get; private set; }

        /// <summary>Hidden exits render nothing and cannot be touched (silent/photo places) until revealed.</summary>
        public void SetHidden(bool h)
        {
            Hidden = h;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = !h;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = !h;
        }
        public System.Action<ExitTrigger> onEnter;
        Transform leaf, doorL, doorR, grille;
        Vector3 lBase, rBase, gBase; Quaternion gRot; float lSign = -1, rSign = 1;
        public string Hint => plan.form == ExitForm.Elevator ? "call" : plan.form == ExitForm.VentHatch ? "pry open" : "push";

        public void Bind(ExitPlan p)
        {
            plan = p;
            leaf = Find("leaf"); doorL = Find("door_l"); doorR = Find("door_r"); grille = Find("grille");
            if (doorL) { lBase = doorL.localPosition; lSign = SideOf(doorL); }
            if (doorR) { rBase = doorR.localPosition; rSign = SideOf(doorR); }
            if (grille) { gBase = grille.localPosition; gRot = grille.localRotation; }
        }

        float SideOf(Transform t)
        {
            var r = t.GetComponentInChildren<Renderer>();
            if (!r) return 1;
            return transform.InverseTransformPoint(r.bounds.center).x >= 0 ? 1f : -1f;
        }

        Transform Find(string n) => GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);

        public void Activate()
        {
            if (IsOpen) return;
            IsOpen = true;
            if (Application.isPlaying) StartCoroutine(Open()); else OpenInstant();
        }

        IEnumerator Open()
        {
            float d = plan.form == ExitForm.Elevator ? 1.6f : 1.1f;
            if (plan.form == ExitForm.Elevator) yield return new WaitForSeconds(1.2f); // the lift takes a moment to "arrive"
            for (float t = 0; t < d; t += Time.deltaTime) { Pose01(Mathf.SmoothStep(0, 1, t / d)); yield return null; }
            Pose01(1);
        }

        void OpenInstant() => Pose01(1);

        void Pose01(float k)
        {
            // swing into the room: hinge on +x -> positive yaw, hinge on -x -> negative (glTF import mirrors X)
            if (leaf) leaf.localRotation = Quaternion.Euler(0, (leaf.localPosition.x > 0 ? 100f : -100f) * k, 0);
            // each panel slides outward from the centre line, whichever side the import put it on
            // first sink 6 cm into the wall pocket, then slide sideways inside it (hidden by the jambs)
            float sink = -0.06f * Mathf.Clamp01(k * 5f), slide = 0.47f * Mathf.Clamp01((k - 0.15f) / 0.85f);
            if (doorL) doorL.localPosition = lBase + new Vector3(lSign * slide, 0, sink);
            if (doorR) doorR.localPosition = rBase + new Vector3(rSign * slide, 0, sink);
            // the hatch grille comes off the wall and leans against it on the floor
            if (grille) { grille.localPosition = gBase + new Vector3(0.45f * k, -0.05f * k, 0.12f * k); grille.localRotation = gRot * Quaternion.Euler(-12f * k, 25f * k, 0); }
        }

        /// <summary>Player inside the opening (close to the wall plane, within the width)?</summary>
        public bool Contains(Vector3 p)
        {
            var local = transform.InverseTransformPoint(p);
            float reach = plan.form == ExitForm.VentHatch ? 0.55f : plan.form == ExitForm.Archway ? 1.2f : 0.45f;
            return !Hidden && IsOpen && Mathf.Abs(local.x) < plan.width * 0.45f && local.z < reach && local.z > -0.2f;
        }
    }
}
