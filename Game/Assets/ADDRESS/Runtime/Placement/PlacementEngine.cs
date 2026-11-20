using System;
using System.Collections.Generic;
using Address.Core;
using UnityEngine;

namespace Address.Placement
{
    /// <summary>
    /// Places objects so they are physically and semantically plausible.
    /// Every candidate pose runs the 9-step pipeline (cheap checks first):
    ///   Style -> Bounds -> Navigation -> Collision -> Surface -> Support -> Rotation -> Semantic -> Density
    /// If no candidate survives, the object is NOT placed (never placed wrongly to reach a count).
    /// Geometry comes from the PlacementProfile (box set, footprint hull, surfaces) so checks run before instantiation.
    /// </summary>
    public class PlacementEngine
    {
        public const float ContactEps = 0.004f;   // max gap for a point to count as touching its support
        public const float OverlapTol = 0.001f;   // boxes may touch / interpenetrate by less than this (physics pushes apart anything deeper)
        const float RayLift = 0.25f;

        public readonly int archMask, propsMask, solidMask;
        public readonly PlacementStats stats = new();
        public readonly List<PlacedObject> placed = new();
        public string style = "backrooms";
        public string mood = "neutral";
        public Transform root;
        public NavGrid nav;
        /// <summary>Doors and exits standing in walls: nothing may hang on the wall over them (pictures, clocks, boards).</summary>
        public readonly List<Bounds> wallKeepOut = new();
        public RoomSpec room;
        public int maxCandidates = 16;
        public Vector3? navStart;             // when set, placements that cut off walkable cells are refused
        int unreachableBaseline = -1;

        readonly List<(Rect2 zone, PlacedObject owner, float yMin, float yMax)> clearanceZones = new();
        readonly Collider[] buf = new Collider[64];
        int uidCounter;

        public PlacementEngine(int archLayer, int propsLayer, Transform root)
        {
            archMask = 1 << archLayer; propsMask = 1 << propsLayer; solidMask = archMask | propsMask;
            this.root = root;
        }

        // ============================================================== public API

        public delegate bool SemanticRule(Pose pose, out string why);

        public class Request
        {
            public PlacementProfile profile;
            public Func<Rng, int, Pose?> candidate;   // generates candidate #i (may return null = skip)
            public PlacedObject supportParent;          // expected support (null = architecture)
            public PlacedObject anchor;                 // related object (its clearance zone may be used)
            public SemanticRule semantic;
            public ObjectState state = ObjectState.Normal;
            public bool anomaly;                        // intentional anomaly: skip Surface/Support/Rotation, keep Collision
            public bool wallSupport, ceilingSupport;
            public string tag = "";
            public int tries = -1;
            public bool ignoreNavReservation;            // e.g. objects under a desk
            public float collisionMargin;                // extra free space required around the object (moving anomalies)
        }

        public PlacedObject TryPlace(Request req, Rng rng)
        {
            stats.requested++;
            var p = req.profile;
            if (p == null || p.prefab == null || p.disabled) { stats.skipped++; return null; }
            // 7. Style compatibility (per object, not per pose)
            if (!p.AllowsStyle(style) || !p.AllowsMood(mood)) { stats.Reject(Check.Style); stats.skipped++; return null; }

            int tries = req.tries > 0 ? Mathf.Min(req.tries, 16) : Mathf.Min(maxCandidates, 14);
            for (int i = 0; i < tries; i++)
            {
                var c = req.candidate(rng, i);
                if (c == null) continue;
                stats.candidatesTried++;
                var pose = c.Value;
                if (!req.anomaly && !req.wallSupport && !req.ceilingSupport)
                {
                    // settle vertically on whatever is below the footprint (floor, desk, another box)
                    if (!SettleY(p, ref pose, req.supportParent, out var why)) { stats.Reject(why); continue; }
                }
                var fail = Evaluate(req, pose);
                if (fail != Check.None) { stats.Reject(fail); continue; }
                var po = Commit(req, pose);
                if (po != null) return po; // (a failed commit already recorded its reason)
            }
            stats.skipped++;
            return null;
        }

        /// <summary>
        /// Physical settle on commit: the new object becomes the only dynamic body and is simulated briefly.
        /// It ends in a true resting pose (rocking into a lid recess, a bag slumping). If it moves more than a few cm
        /// or rotates noticeably, the pose was not really stable and the placement is undone.
        /// </summary>
        public bool settleOnCommit = true;
        public static readonly System.Diagnostics.Stopwatch TSettle = new(), TReach = new(), TCommit = new();
        bool PhysicalSettle(PlacedObject po)
        {
            TSettle.Start(); try { return PhysicalSettleInner(po); } finally { TSettle.Stop(); }
        }
        bool PhysicalSettleInner(PlacedObject po)
        {
            var p = po.profile;
            var rb = po.gameObject.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.05f, p.mass);
            rb.centerOfMass = p.centerOfMass;
            rb.interpolation = RigidbodyInterpolation.None;
            var mode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            var t = po.transform; var p0 = t.position; var r0 = t.rotation;
            for (int i = 0; i < 22; i++) { Physics.Simulate(1f / 30f); if (i > 3 && rb.IsSleeping()) break; }
            float moved = Vector3.Distance(t.position, p0), turned = Quaternion.Angle(t.rotation, r0);
            bool stillMoving = rb.linearVelocity.magnitude > 0.05f || rb.angularVelocity.magnitude > 0.2f;
            UnityEngine.Object.DestroyImmediate(rb);
            // validator only: the analytic pose is kept so generation stays bit-for-bit deterministic
            // (PhysX results depend on scene history; the accept/reject decision with margins does not)
            t.SetPositionAndRotation(p0, r0);
            Physics.simulationMode = mode;
            Physics.SyncTransforms();
            float allowedTurn = po.state == ObjectState.Normal ? 2f : 6f;
            return moved <= (po.state == ObjectState.Normal ? 0.01f : 0.02f) && turned <= allowedTurn && !stillMoving;
        }

        void Uncommit(PlacedObject po, Rect2? blocked)
        {
            placed.Remove(po);
            stats.placed--;
            if (blocked.HasValue && nav != null) nav.Unblock(blocked.Value);
            for (int i = clearanceZones.Count - 1; i >= 0; i--) if (clearanceZones[i].owner == po) clearanceZones.RemoveAt(i);
            UnityEngine.Object.DestroyImmediate(po.gameObject);
            Physics.SyncTransforms();
        }



        /// <summary>
        /// Player "put down" validation: same physics-based rules as generation (settle on what is below, no
        /// intersection, real support under the centre of mass, upright). Any object may serve as support.
        /// The caller must move the held object's colliders out of the props layer first.
        /// </summary>
        public Check PlayerPlaceCheck(PlacementProfile p, ref Pose pose)
        {
            if (!SettleY(p, ref pose, null, out var why)) return why;
            if (Collides(p, pose, null)) return Check.Collision;
            var s = SupportCheck(p, pose, null, out _, anyOwner: true);
            if (s != Check.None) return s;
            return Check.None;
        }

        // ============================================================== checks

        Check Evaluate(Request req, Pose pose)
        {
            var p = req.profile;
            // 1. Bounds: whole object inside the room volume
            if (room != null && !InsideRoom(p, pose)) return Check.Bounds;
            if (room != null && room.keepOut.Count > 0)
            {
                WorldAabb(p, pose, out var kmn, out var kmx);
                var fr = new Rect(kmn.x - 0.05f, kmn.z - 0.05f, kmx.x - kmn.x + 0.1f, kmx.z - kmn.z + 0.1f);
                foreach (var k in room.keepOut) if (k.Overlaps(fr)) return Check.Bounds;
            }

            // 9. Navigation: floor objects must not sit on reserved walkways (cheap 2D test)
            var foot = FootRect(p, pose);
            if (nav != null && !req.ignoreNavReservation && req.supportParent == null && !req.wallSupport && !req.ceilingSupport
                && LowestY(p, pose) < room?.FloorY + 1.9f && nav.TouchesReserved(foot)) return Check.Navigation;

            // 2. Collision: compound boxes vs architecture + props
            if (Collides(p, pose, null, req.collisionMargin)) return Check.Collision;

            // clearance: we must not stand inside someone else's clearance zone (unless it's our anchor's),
            // and our own clearance zone must be free
            if (InForeignClearance(p, pose, req.anchor)) return Check.Clearance;
            if (p.clearanceFront > 0 && !OwnClearanceFree(p, pose)) return Check.Clearance;

            if (!req.anomaly)
            {
                if (req.wallSupport) { if (!WallContact(p, pose)) return Check.Support; }
                else if (req.ceilingSupport) { if (!CeilingContact(p, pose)) return Check.Support; }
                else
                {
                    // 3. Surface + 4. Support: contact points on the right object, centre of mass over the support polygon
                    var s = SupportCheck(p, pose, req.supportParent, out _);
                    if (s != Check.None) return s;
                }
                // 5. Rotation: upright unless a state/anomaly says otherwise
                if (req.state == ObjectState.Normal)
                {
                    float tilt = Vector3.Angle(pose.rotation * Vector3.up, Vector3.up);
                    if (tilt > p.maxTiltDeg + 0.01f) return Check.Rotation;
                }
            }
            // 6. Semantic rule supplied by the scenario
            if (req.semantic != null && !req.semantic(pose, out _)) return Check.Semantic;
            // 8. Density: keep min spacing from neighbours on the same support level
            if (p.minSpacing > 0 && Crowded(p, pose, req.supportParent)) return Check.Density;
            return Check.None;
        }

        PlacedObject Commit(Request req, Pose pose)
        {
            var go = UnityEngine.Object.Instantiate(req.profile.prefab, pose.position, pose.rotation, root);
            go.transform.localScale = Vector3.one * pose.scale;
            go.name = req.profile.id;
            var po = go.GetComponent<PlacedObject>();
            if (po == null) po = go.AddComponent<PlacedObject>();
            po.profile = req.profile;
            po.supportParent = req.supportParent;
            po.intentionalAnomaly = req.anomaly;
            po.state = req.state;
            po.placedBy = req.tag;
            po.uid = ++uidCounter;
            Physics.SyncTransforms();
            placed.Add(po);
            stats.placed++;
            var p = req.profile;
            bool movable = !req.anomaly && !req.wallSupport && !req.ceilingSupport && p.massClass != MassClass.Fixed;
            // the physics check is only needed where it can fail: things on other things, stacked, tilted or fallen.
            // An upright object standing on the flat floor is already proven stable by the analytic checks.
            bool needsPhysics = req.supportParent != null || req.state != ObjectState.Normal;
            if (settleOnCommit && movable && needsPhysics && !PhysicalSettle(po)) { Uncommit(po, null); stats.Reject(Check.Support); return null; }
            pose = new Pose(po.transform.position, po.transform.rotation, pose.scale);
            if (nav != null && req.supportParent == null && !req.wallSupport && !req.ceilingSupport && LowestY(p, pose) < (room?.FloorY ?? 0) + 1.9f)
            {
                var foot = FootRect(p, pose);
                if (navStart.HasValue && unreachableBaseline < 0) nav.ReachableFraction(navStart.Value, out unreachableBaseline);
                nav.Block(foot);
                if (navStart.HasValue)
                {
                    nav.ReachableFraction(navStart.Value, out int un);
                    if (un > unreachableBaseline) { Uncommit(po, foot); stats.Reject(Check.Navigation); return null; }
                    unreachableBaseline = un;
                }
            }
            if (p.clearanceFront > 0)
            {
                var z = FrontZone(p, pose);
                clearanceZones.Add((z, po, pose.position.y + 0.02f, pose.position.y + 1.6f));
            }
            return po;
        }

        // ------------------------------------------------------------- geometry helpers

        public static Vector3 BoxWorldCenter(LocalBox b, Pose pose) => pose.position + pose.rotation * (b.center * pose.scale);

        public static void WorldAabb(PlacementProfile p, Pose pose, out Vector3 min, out Vector3 max)
        {
            min = Vector3.positiveInfinity; max = Vector3.negativeInfinity;
            foreach (var b in p.boxes)
            {
                var c = BoxWorldCenter(b, pose);
                var h = b.size * (0.5f * pose.scale);
                for (int i = 0; i < 8; i++)
                {
                    var corner = c + pose.rotation * new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                    min = Vector3.Min(min, corner); max = Vector3.Max(max, corner);
                }
            }
        }

        public static float LowestY(PlacementProfile p, Pose pose) { WorldAabb(p, pose, out var mn, out _); return mn.y; }

        bool InsideRoom(PlacementProfile p, Pose pose)
        {
            WorldAabb(p, pose, out var mn, out var mx);
            var b = room.bounds;
            const float t = 0.002f;
            return mn.x >= b.min.x - t && mn.z >= b.min.z - t && mx.x <= b.max.x + t && mx.z <= b.max.z + t && mn.y >= b.min.y - 0.02f && mx.y <= b.max.y + t;
        }

        public static Rect2 FootRect(PlacementProfile p, Pose pose)
        {
            float yaw = pose.rotation.eulerAngles.y * Mathf.Deg2Rad;
            // AABB of boxes in local XZ (for upright objects); fallen objects use world AABB
            if (Vector3.Angle(pose.rotation * Vector3.up, Vector3.up) > 5f)
            {
                WorldAabb(p, pose, out var mn, out var mx);
                return new Rect2(new Vector2((mn.x + mx.x) * 0.5f, (mn.z + mx.z) * 0.5f), new Vector2((mx.x - mn.x) * 0.5f, (mx.z - mn.z) * 0.5f), 0);
            }
            var c = pose.position;
            return new Rect2(new Vector2(c.x, c.z), new Vector2(p.size.x, p.size.z) * (0.5f * pose.scale), yaw);
        }

        /// <summary>Zone in front (+Z) of the object that must stay free (chair space, drawer swing).</summary>
        public static Rect2 FrontZone(PlacementProfile p, Pose pose)
        {
            float yaw = pose.rotation.eulerAngles.y * Mathf.Deg2Rad;
            var fwd = pose.rotation * Vector3.forward;
            var c = pose.position + fwd * (p.size.z * 0.5f * pose.scale + p.clearanceFront * 0.5f);
            return new Rect2(new Vector2(c.x, c.z), new Vector2(p.size.x * 0.5f * pose.scale, p.clearanceFront * 0.5f), yaw);
        }

        bool Collides(PlacementProfile p, Pose pose, Collider ignore, float margin = 0f)
        {
            foreach (var b in p.boxes)
            {
                var half = Shrunk(b.size * (0.5f * pose.scale), OverlapTol) + Vector3.one * margin;
                int n = Physics.OverlapBoxNonAlloc(BoxWorldCenter(b, pose), half, buf, pose.rotation, solidMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) if (buf[i] != ignore) return true;
            }
            return false;
        }

        /// <summary>Shrink half extents by tol but never below 55% of the original (thin paper must still be tested).</summary>
        public static Vector3 Shrunk(Vector3 half, float tol) =>
            new(half.x - Mathf.Min(tol, half.x * 0.45f), half.y - Mathf.Min(tol, half.y * 0.45f), half.z - Mathf.Min(tol, half.z * 0.45f));

        bool InForeignClearance(PlacementProfile p, Pose pose, PlacedObject anchor)
        {
            if (clearanceZones.Count == 0) return false;
            var foot = FootRect(p, pose);
            WorldAabb(p, pose, out var mn, out var mx);
            foreach (var (zone, owner, y0, y1) in clearanceZones)
            {
                if (owner == anchor) continue;
                if (mx.y < y0 || mn.y > y1) continue;
                // objects standing ON the owner (laptop on desk) are not in its clearance zone
                if (mn.y > y1 - 0.05f) continue;
                if (foot.Overlaps(zone, -0.01f)) return true;
            }
            return false;
        }

        bool OwnClearanceFree(PlacementProfile p, Pose pose)
        {
            var z = FrontZone(p, pose);
            var c = new Vector3(z.c.x, pose.position.y + 0.82f, z.c.y);
            var half = new Vector3(z.half.x - 0.01f, 0.78f, z.half.y - 0.01f);
            int n = Physics.OverlapBoxNonAlloc(c, half, buf, pose.rotation, solidMask, QueryTriggerInteraction.Ignore);
            return n == 0;
        }

        bool Crowded(PlacementProfile p, Pose pose, PlacedObject support)
        {
            float s = p.minSpacing;
            foreach (var b in p.boxes)
            {
                // expand horizontally only, shrink vertically so we never "touch" our support
                var half = b.size * (0.5f * pose.scale);
                half = new Vector3(half.x + s, half.y - Mathf.Min(OverlapTol * 2, half.y * 0.45f), half.z + s);
                int n = Physics.OverlapBoxNonAlloc(BoxWorldCenter(b, pose), half, buf, pose.rotation, propsMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var other = buf[i].GetComponentInParent<PlacedObject>();
                    if (other != null && other == support) continue;
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------- vertical settling + support

        static readonly Dictionary<PlacementProfile, Vector3[]> bottomCache = new();

        /// <summary>
        /// Local contact candidates on the underside of the object: corners (inset 3 mm) and centres of every collider box
        /// whose bottom is within 1.5 cm of the pivot. y = local height of the underside at that point.
        /// </summary>
        public static Vector3[] BottomPoints(PlacementProfile p)
        {
            if (bottomCache.TryGetValue(p, out var cached) && cached != null) return cached;
            var pts = new List<Vector3>();
            foreach (var b in p.boxes)
            {
                float by = b.center.y - b.size.y * 0.5f;
                if (by > 0.015f) continue;
                float hx = Mathf.Max(0, b.size.x * 0.5f - 0.003f), hz = Mathf.Max(0, b.size.z * 0.5f - 0.003f);
                pts.Add(new Vector3(b.center.x, by, b.center.z));
                pts.Add(new Vector3(b.center.x - hx, by, b.center.z - hz)); pts.Add(new Vector3(b.center.x + hx, by, b.center.z - hz));
                pts.Add(new Vector3(b.center.x - hx, by, b.center.z + hz)); pts.Add(new Vector3(b.center.x + hx, by, b.center.z + hz));
            }
            if (pts.Count == 0) pts.Add(Vector3.zero);
            var arr = pts.ToArray();
            bottomCache[p] = arr;
            return arr;
        }

        /// <summary>Drop the object straight down until its underside rests on the highest thing below it.</summary>
        bool SettleY(PlacementProfile p, ref Pose pose, PlacedObject expectedParent, out Check why)
        {
            why = Check.Surface;
            if (Vector3.Angle(pose.rotation * Vector3.up, Vector3.up) > 5f)
            {
                // fallen pose: rest the lowest box corner on the floor/support below the object's centre
                WorldAabb(p, pose, out var mn, out var mx);
                var c = new Vector3((mn.x + mx.x) * 0.5f, mx.y + RayLift, (mn.z + mx.z) * 0.5f);
                if (!Physics.Raycast(c, Vector3.down, out var hh, (mx.y - mn.y) + RayLift + 3f, archMask | propsMask, QueryTriggerInteraction.Ignore)) return false;
                pose.position.y += hh.point.y - mn.y;
                return true;
            }
            float best = float.NegativeInfinity;
            float top = pose.position.y + RayLift;
            foreach (var lp in BottomPoints(p))
            {
                var w = pose.position + pose.rotation * (new Vector3(lp.x, 0, lp.z) * pose.scale);
                if (Physics.Raycast(new Vector3(w.x, top, w.z), Vector3.down, out var h, RayLift + 3f, solidMask, QueryTriggerInteraction.Ignore))
                    best = Mathf.Max(best, h.point.y - lp.y * pose.scale);
            }
            if (float.IsNegativeInfinity(best)) return false;
            pose.position.y = best;
            return true;
        }

        /// <summary>
        /// 3. Surface: every point where the underside touches something must touch the expected support
        /// 4. Support: at least 3 real contacts (underside within ContactEps of the support), and the centre of mass must lie
        ///    inside the contact polygon with a safety margin, otherwise physics would tip it over.
        /// </summary>
        public Check SupportCheck(PlacementProfile p, Pose pose, PlacedObject expectedParent, out int contacts, bool anyOwner = false)
        {
            contacts = 0;
            var contactPts = new List<Vector2>();
            foreach (var lp in BottomPoints(p))
            {
                var w = pose.position + pose.rotation * (new Vector3(lp.x, 0, lp.z) * pose.scale);
                float under = pose.position.y + lp.y * pose.scale;
                if (!Physics.Raycast(new Vector3(w.x, under + 0.02f, w.z), Vector3.down, out var h, 0.02f + ContactEps * 2, solidMask, QueryTriggerInteraction.Ignore)) continue;
                if (under - h.point.y > ContactEps) continue;          // not touching here
                if (h.normal.y < 0.94f) return Check.Support;         // resting on a steep face slides
                var owner = h.collider.GetComponentInParent<PlacedObject>();
                bool ok = anyOwner || (expectedParent == null
                    ? owner == null || (owner.profile != null && owner.profile.canStack && p.canStack)
                    : owner == expectedParent);
                if (!ok) return Check.Surface;
                contactPts.Add(new Vector2(w.x, w.z));
            }
            contacts = contactPts.Count;
            if (contactPts.Count < 3) return Check.Support;
            var com = pose.position + pose.rotation * (p.centerOfMass * pose.scale);
            float margin = Mathf.Min(0.012f, 0.12f * Mathf.Min(p.size.x, p.size.z) * pose.scale);
            if (!InsideHull(contactPts, new Vector2(com.x, com.z), margin)) return Check.Support;
            return Check.None;
        }

        bool WallContact(PlacementProfile p, Pose pose)
        {
            // back face (-Z) corners must touch an architecture face within eps
            var back = pose.rotation * Vector3.back;
            var hs = p.size * 0.5f * pose.scale;
            int hits = 0;
            foreach (var (x, yy) in new[] { (-0.8f, 0.15f), (0.8f, 0.15f), (-0.8f, 0.85f), (0.8f, 0.85f) })
            {
                var local = new Vector3(x * hs.x, yy * p.size.y * pose.scale, -hs.z + 0.02f);
                var o = pose.position + pose.rotation * local;
                if (Physics.Raycast(o, back, out var h, 0.02f + 0.012f, archMask, QueryTriggerInteraction.Ignore) && Mathf.Abs(h.normal.y) < 0.2f) hits++;
            }
            return hits >= 3;
        }

        bool CeilingContact(PlacementProfile p, Pose pose)
        {
            var o = pose.position + Vector3.up * (p.size.y * pose.scale - 0.02f);
            return Physics.Raycast(o, Vector3.up, out var h, 0.02f + 0.01f, archMask, QueryTriggerInteraction.Ignore) && h.normal.y < -0.9f;
        }

        /// <summary>Point inside the convex hull of pts, at least 'margin' away from every edge.</summary>
        public static bool InsideHull(List<Vector2> pts, Vector2 q, float margin = 0f)
        {
            var hull = Hull(pts);
            if (hull.Count < 3) return false;
            for (int i = 0; i < hull.Count; i++)
            {
                var a = hull[i]; var b = hull[(i + 1) % hull.Count];
                var e = b - a; float len = e.magnitude; if (len < 1e-6f) continue;
                float signedDist = (e.x * (q.y - a.y) - e.y * (q.x - a.x)) / len;
                if (signedDist < margin) return false;
            }
            return true;
        }

        static List<Vector2> Hull(List<Vector2> p)
        {
            var pts = new List<Vector2>(p);
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 c) => (a.x - o.x) * (c.y - o.y) - (a.y - o.y) * (c.x - o.x);
            foreach (var v in pts) { while (h.Count >= 2 && Cross(h[^2], h[^1], v) <= 0) h.RemoveAt(h.Count - 1); h.Add(v); }
            int lower = h.Count + 1;
            for (int i = pts.Count - 2; i >= 0; i--) { var v = pts[i]; while (h.Count >= lower && Cross(h[^2], h[^1], v) <= 0) h.RemoveAt(h.Count - 1); h.Add(v); }
            if (h.Count > 1) h.RemoveAt(h.Count - 1);
            return h;
        }

        // ============================================================== candidate generators

        public static Quaternion Yaw(float deg) => Quaternion.Euler(0, deg, 0);

        public static float RuleYaw(RotationRule rule, Rng rng)
        {
            switch (rule)
            {
                case RotationRule.Step90: return rng.Range(0, 4) * 90f;
                case RotationRule.Step45: return rng.Range(0, 8) * 45f;
                case RotationRule.Fixed: return 0;
                default: return rng.Range(0f, 360f);
            }
        }

        /// <summary>Random spot on the floor of the room, honouring the profile rotation rule.</summary>
        public Func<Rng, int, Pose?> OnFloor(PlacementProfile p, Bounds area, Quaternion? extraRot = null)
        {
            return (rng, i) =>
            {
                float r = Mathf.Max(p.size.x, p.size.z) * 0.5f;
                if (area.size.x < 2 * r || area.size.z < 2 * r) r = Mathf.Min(p.size.x, p.size.z) * 0.5f;
                float x = rng.Range(area.min.x + r, area.max.x - r), z = rng.Range(area.min.z + r, area.max.z - r);
                var rot = Yaw(RuleYaw(p.rotation, rng));
                if (extraRot.HasValue) rot = rot * extraRot.Value;
                float s = p.scaleJitter > 0 ? 1f + rng.Range(-p.scaleJitter, p.scaleJitter) : 1f;
                return new Pose(new Vector3(x, area.min.y, z), rot, s);
            };
        }

        /// <summary>Back against a wall, front facing into the room.</summary>
        public static float WallGapExtra = 0.02f; // baseboard depth + margin
        public Func<Rng, int, Pose?> AgainstWall(PlacementProfile p, IReadOnlyList<WallSeg> walls, float gap = 0.008f)
        {
            return (rng, i) =>
            {
                if (walls.Count == 0) return null;
                var w = walls[rng.Range(0, walls.Count)];
                float half = p.size.x * 0.5f;
                if (w.Length < p.size.x + 0.02f) return null;
                float t = rng.Range(half + 0.01f, w.Length - half - 0.01f);
                var pos = w.a + w.Dir * t + w.normal * (p.size.z * 0.5f + gap + WallGapExtra);
                var rot = Quaternion.LookRotation(w.normal, Vector3.up);
                return new Pose(pos, rot, 1f);
            };
        }

        /// <summary>Hung on a wall: back touches the wall face, bottom within the profile height band.</summary>
        public Func<Rng, int, Pose?> OnWall(PlacementProfile p, IReadOnlyList<WallSeg> walls)
        {
            return (rng, i) =>
            {
                if (walls.Count == 0) return null;
                var w = walls[rng.Range(0, walls.Count)];
                float half = p.size.x * 0.5f;
                if (w.Length < p.size.x + 0.1f) return null;
                float t = rng.Range(half + 0.05f, w.Length - half - 0.05f);
                float y = rng.Range(p.wallMin, Mathf.Min(p.wallMax, w.height - p.size.y - 0.05f));
                var pos = w.a + w.Dir * t + w.normal * (p.size.z * 0.5f + 0.002f) + Vector3.up * y;
                var fp = new Bounds(pos, new Vector3(Mathf.Abs(w.Dir.x) * p.size.x + Mathf.Abs(w.normal.x) * p.size.z + 0.02f, p.size.y, Mathf.Abs(w.Dir.z) * p.size.x + Mathf.Abs(w.normal.z) * p.size.z + 0.02f));
                foreach (var k in wallKeepOut) if (k.Intersects(fp)) return null;
                return new Pose(pos, Quaternion.LookRotation(w.normal, Vector3.up), 1f);
            };
        }

        public Func<Rng, int, Pose?> OnCeiling(PlacementProfile p, Bounds area)
        {
            return (rng, i) =>
            {
                float x = rng.Range(area.min.x + 0.5f, area.max.x - 0.5f), z = rng.Range(area.min.z + 0.5f, area.max.z - 0.5f);
                return new Pose(new Vector3(x, area.max.y - p.size.y, z), Yaw(RuleYaw(p.rotation, rng)), 1f);
            };
        }

        /// <summary>
        /// On one of the parent's extracted surfaces. Optional local-rect filter (in parent space) lets scenarios
        /// say "back half of the desk", "front-centre" etc. Rotation: free / or facing parent's +Z.
        /// </summary>
        public Func<Rng, int, Pose?> OnSurface(PlacementProfile p, PlacedObject parent, Func<SurfaceRegion, bool> surfaceFilter = null,
            Rect? localArea = null, float? relYaw = null, float yawJitter = 0f)
        {
            return (rng, i) =>
            {
                var prof = parent.profile;
                var regions = new List<SurfaceRegion>();
                foreach (var s in prof.surfaces)
                {
                    if (s.kind == "inside") continue;
                    if (surfaceFilter != null && !surfaceFilter(s)) continue;
                    if (s.clearance < p.size.y + 0.01f) continue;
                    regions.Add(s);
                }
                if (regions.Count == 0) return null;
                // weight by area
                var weights = new List<float>(); foreach (var r in regions) weights.Add(r.area);
                var reg = regions[rng.PickWeighted(weights)];
                float yawLocal = relYaw.HasValue ? relYaw.Value + rng.Range(-yawJitter, yawJitter) : RuleYaw(p.rotation == RotationRule.FaceAnchor ? RotationRule.Free : p.rotation, rng);
                // footprint half extents rotated into parent space (conservative AABB)
                float a = yawLocal * Mathf.Deg2Rad;
                float hx = Mathf.Abs(Mathf.Cos(a)) * p.size.x * 0.5f + Mathf.Abs(Mathf.Sin(a)) * p.size.z * 0.5f;
                float hz = Mathf.Abs(Mathf.Sin(a)) * p.size.x * 0.5f + Mathf.Abs(Mathf.Cos(a)) * p.size.z * 0.5f;
                for (int k = 0; k < 12; k++)
                {
                    var cpos = reg.CellCenter(reg.cells[rng.Range(0, reg.cells.Count)]) + new Vector2(rng.Range(-0.5f, 0.5f), rng.Range(-0.5f, 0.5f)) * reg.cell;
                    if (localArea.HasValue && !localArea.Value.Contains(cpos)) continue;
                    if (!reg.ContainsRect(cpos - new Vector2(hx, hz), cpos + new Vector2(hx, hz))) continue;
                    var t = parent.transform;
                    var world = t.TransformPoint(new Vector3(cpos.x, reg.height, cpos.y));
                    var rot = t.rotation * Yaw(yawLocal);
                    return new Pose(world, rot, 1f);
                }
                return null;
            };
        }

        /// <summary>Stacking: on top of another stackable object (box on box). Support decided by contact raycasts.</summary>
        public Func<Rng, int, Pose?> OnTopOf(PlacementProfile p, PlacedObject below, float maxOffset = 0.06f)
        {
            return (rng, i) =>
            {
                var t = below.transform;
                var off = new Vector3(rng.Range(-maxOffset, maxOffset), below.profile.size.y + 0.002f, rng.Range(-maxOffset, maxOffset));
                var rot = t.rotation * Yaw(rng.Chance(0.5f) ? rng.Range(-12f, 12f) : 90f + rng.Range(-12f, 12f));
                return new Pose(t.TransformPoint(off), rot, 1f);
            };
        }

        /// <summary>Relative to an anchor (chair at a desk): local offset ranges in anchor space, facing the anchor.</summary>
        public Func<Rng, int, Pose?> RelativeTo(PlacedObject anchor, Vector3 localMin, Vector3 localMax, float relYaw, float yawJitter, bool tightFirst = true)
        {
            return (rng, i) =>
            {
                var t = anchor.transform;
                // earlier candidates explore the 'tight' end of the range (chair pushed in), later ones pull back
                float k = tightFirst ? Mathf.Clamp01(i / 10f + rng.Range(0f, 0.15f)) : rng.Value;
                var local = new Vector3(rng.Range(localMin.x, localMax.x), localMin.y, Mathf.Lerp(localMin.z, localMax.z, k));
                var rot = t.rotation * Yaw(relYaw + rng.Range(-yawJitter, yawJitter));
                return new Pose(t.TransformPoint(local), rot, 1f);
            };
        }
    }
}
