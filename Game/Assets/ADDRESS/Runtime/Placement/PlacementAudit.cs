using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Address.Placement
{
    /// <summary>
    /// Independent verification of a generated level. It does NOT trust the placement engine:
    ///  - Intersection: exact penetration depth between every pair of colliders (props vs props / architecture)
    ///  - Support:      physical settle test - all movable objects become rigid bodies and the world is simulated;
    ///                  anything that falls, slides, tips or is pushed out of an intersection moves and is reported
    ///  - Surface:      the thing under an object's centre must be its declared support
    ///  - Tilt:         upright objects must be within their allowed tilt
    ///  - Wall/Ceiling: hung objects must touch their wall/ceiling
    ///  - Navigation:   every walkable cell stays reachable from the start, walkways stay open
    /// Intentional anomalies are exempt from support/tilt rules but never from intersection.
    /// </summary>
    public class PlacementAudit
    {
        public class Issue
        {
            public PlacedObject obj; public string kind; public string detail; public float value;
            public override string ToString() => $"{kind} {(obj ? obj.name : "-")} [{obj?.placedBy}] {detail}";
        }

        public readonly List<Issue> issues = new();
        public int objects, anomalies, settleTested;
        public float maxPenetration, maxDrift;
        public int unreachableCells;

        readonly int archMask, propsMask;
        public PlacementAudit(int archLayer, int propsLayer) { archMask = 1 << archLayer; propsMask = 1 << propsLayer; }

        public void Run(IList<PlacedObject> placed, NavGrid nav, Vector3 start, bool settle = true)
        {
            Physics.SyncTransforms();
            objects = placed.Count;
            anomalies = placed.Count(p => p && p.intentionalAnomaly);
            Intersections(placed);
            foreach (var po in placed)
            {
                if (!po) continue;
                var p = po.profile;
                if (p.support == SupportType.Wall) { WallCheck(po); continue; }
                if (p.support == SupportType.Ceiling) { CeilingCheck(po); continue; }
                if (po.intentionalAnomaly) continue;
                Tilt(po);
                SurfaceCheck(po);
            }
            if (settle) SettleTest(placed);
            if (nav != null)
            {
                float f = nav.ReachableFraction(start, out unreachableCells);
                if (unreachableCells > 0) issues.Add(new Issue { kind = "Navigation", detail = $"{unreachableCells} walkable cells unreachable ({f:P1} reachable)", value = unreachableCells });
            }
        }

        // ------------------------------------------------------------------ intersections

        void Intersections(IList<PlacedObject> placed)
        {
            var buf = new Collider[64];
            var reported = new HashSet<(int, int)>();
            foreach (var po in placed)
            {
                if (!po) continue;
                foreach (var bc in po.GetComponentsInChildren<BoxCollider>())
                {
                    var t = bc.transform;
                    var c = t.TransformPoint(bc.center);
                    var half = Vector3.Scale(bc.size * 0.5f, t.lossyScale) - Vector3.one * 0.0015f;
                    if (half.x <= 0 || half.y <= 0 || half.z <= 0) continue;
                    int n = Physics.OverlapBoxNonAlloc(c, half, buf, t.rotation, archMask | propsMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                    {
                        var other = buf[i];
                        var otherPo = other.GetComponentInParent<PlacedObject>();
                        if (otherPo == po) continue;
                        if (!Physics.ComputePenetration(bc, t.position, t.rotation, other, other.transform.position, other.transform.rotation, out _, out float depth)) continue;
                        if (depth <= 0.0035f) continue;
                        int a = po.uid, b = otherPo ? otherPo.uid : -other.GetInstanceID();
                        var key = a < b ? (a, b) : (b, a);
                        if (!reported.Add(key)) continue;
                        maxPenetration = Mathf.Max(maxPenetration, depth);
                        issues.Add(new Issue { obj = po, kind = "Intersection", detail = $"with {(otherPo ? otherPo.name : other.name)} depth {depth * 100:F1}cm", value = depth });
                    }
                }
            }
        }

        // ------------------------------------------------------------------ geometric rules

        void Tilt(PlacedObject po)
        {
            if (po.state != ObjectState.Normal) return;
            float tilt = Vector3.Angle(po.transform.up, Vector3.up);
            if (tilt > po.profile.maxTiltDeg + 0.05f) issues.Add(new Issue { obj = po, kind = "Tilt", detail = $"{tilt:F1}deg > {po.profile.maxTiltDeg}", value = tilt });
        }

        /// <summary>
        /// Independent support check on the real scene colliders: rays from the underside of every low box.
        /// Floating = no point of the underside within 1.2 cm of anything; WrongSurface = touches something that is
        /// neither the declared support nor (for stackables) another stackable.
        /// </summary>
        void SurfaceCheck(PlacedObject po)
        {
            if (po.state != ObjectState.Normal) return;
            float lowest = float.MaxValue;
            var boxes = po.GetComponentsInChildren<BoxCollider>();
            foreach (var bc in boxes) lowest = Mathf.Min(lowest, bc.bounds.min.y);
            int contacts = 0; string wrong = null;
            foreach (var bc in boxes)
            {
                var bb = bc.bounds;
                if (bb.min.y > lowest + 0.012f) continue;
                var t = bc.transform; var h = bc.size * 0.5f;
                foreach (var (sx, sz) in new[] { (0f, 0f), (-0.9f, -0.9f), (0.9f, -0.9f), (-0.9f, 0.9f), (0.9f, 0.9f) })
                {
                    var w = t.TransformPoint(bc.center + new Vector3(sx * h.x, -h.y, sz * h.z));
                    var o = new Vector3(w.x, w.y + 0.01f, w.z);
                    var hits = Physics.RaycastAll(o, Vector3.down, 0.025f, archMask | propsMask, QueryTriggerInteraction.Ignore);
                    float bestD = float.MaxValue; Collider best = null;
                    foreach (var hh in hits)
                    {
                        if (hh.collider.GetComponentInParent<PlacedObject>() == po) continue;
                        if (hh.distance < bestD) { bestD = hh.distance; best = hh.collider; }
                    }
                    if (best == null || bestD - 0.01f > 0.012f) continue;
                    contacts++;
                    var owner = best.GetComponentInParent<PlacedObject>();
                    bool expected = po.supportParent ? owner == po.supportParent : (owner == null || (owner.profile.canStack && po.profile.canStack));
                    if (!expected) wrong = owner ? owner.name : best.name;
                }
            }
            if (wrong != null) issues.Add(new Issue { obj = po, kind = "WrongSurface", detail = $"touches {wrong}, declared support {(po.supportParent ? po.supportParent.name : "floor")}" });
            else if (contacts == 0) issues.Add(new Issue { obj = po, kind = "Floating", detail = "underside touches nothing" });
        }

        void WallCheck(PlacedObject po)
        {
            var back = -po.transform.forward;
            var o = po.transform.TransformPoint(new Vector3(0, po.profile.size.y * 0.5f, -po.profile.size.z * 0.5f + 0.02f));
            if (!Physics.Raycast(o, back, out var h, 0.04f, archMask, QueryTriggerInteraction.Ignore))
                issues.Add(new Issue { obj = po, kind = "WallDetached", detail = "no wall behind" });
            else if (h.distance - 0.02f > 0.012f)
                issues.Add(new Issue { obj = po, kind = "WallDetached", detail = $"gap {(h.distance - 0.02f) * 100:F1}cm" });
        }

        void CeilingCheck(PlacedObject po)
        {
            var o = po.transform.position + Vector3.up * (po.profile.size.y - 0.02f);
            if (!Physics.Raycast(o, Vector3.up, out var h, 0.04f, archMask, QueryTriggerInteraction.Ignore) || h.distance - 0.02f > 0.012f)
                issues.Add(new Issue { obj = po, kind = "CeilingDetached", detail = "not touching ceiling" });
        }

        // ------------------------------------------------------------------ physics settle test

        void SettleTest(IList<PlacedObject> placed)
        {
            var mode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            var bodies = new List<(PlacedObject po, Rigidbody rb, Vector3 pos, Quaternion rot)>();
            foreach (var po in placed)
            {
                if (!po) continue;
                var rb = po.gameObject.AddComponent<Rigidbody>();
                bool dynamic = !po.intentionalAnomaly && po.profile.support != SupportType.Wall && po.profile.support != SupportType.Ceiling && po.profile.massClass != MassClass.Fixed;
                rb.isKinematic = !dynamic;
                rb.mass = Mathf.Max(0.05f, po.profile.mass);
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rb.centerOfMass = po.profile.centerOfMass;
                rb.sleepThreshold = 0.01f;
                bodies.Add((po, rb, po.transform.position, po.transform.rotation));
                if (dynamic) settleTested++;
            }
            for (int i = 0; i < 90; i++) Physics.Simulate(1f / 60f);
            foreach (var (po, rb, pos, rot) in bodies)
            {
                if (rb.isKinematic) continue;
                float d = Vector3.Distance(po.transform.position, pos);
                float a = Quaternion.Angle(po.transform.rotation, rot);
                maxDrift = Mathf.Max(maxDrift, d);
                if (d > 0.015f || a > 3f)
                    issues.Add(new Issue { obj = po, kind = "Unstable", detail = $"moved {d * 100:F1}cm rotated {a:F1}deg in 1.5s of physics", value = d });
            }
            // restore exact poses
            foreach (var (po, rb, pos, rot) in bodies)
            {
                Object.DestroyImmediate(rb);
                po.transform.SetPositionAndRotation(pos, rot);
            }
            Physics.SyncTransforms();
            Physics.simulationMode = mode;
        }

        public Dictionary<string, int> CountByKind()
        {
            var d = new Dictionary<string, int>();
            foreach (var i in issues) { d.TryGetValue(i.kind, out int n); d[i.kind] = n + 1; }
            return d;
        }
    }
}
