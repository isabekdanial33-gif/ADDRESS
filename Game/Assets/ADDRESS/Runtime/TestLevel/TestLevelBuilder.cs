using System.Collections.Generic;
using Address.Core;
using Address.Placement;
using UnityEngine;

namespace Address.TestLevel
{
    /// <summary>
    /// Technical test level (milestone M1): a row of rooms that exercise every placement case -
    /// office workstations, storage shelves & stacks, a lounge, an uneven/sloped floor room and an anomaly room.
    /// Architecture here is a simple box kit (temporary, replaced by the Blender kit in M2).
    /// </summary>
    public class TestLevelBuilder
    {
        public const int ArchLayer = 8, PropsLayer = 9;

        public class Result
        {
            public GameObject root;
            public List<RoomSpec> rooms = new();
            public PlacementEngine engine;
            public NavGrid nav;
            public Vector3 start;
            public List<Vector3> doors = new();
        }

        public Material floorMat, wallMat, ceilingMat, lightMat, unevenMat;
        public GameObject trofferPrefab, baseboardPrefab, doorFramePrefab;
        public const float BaseboardDepth = 0.016f, BaseboardHeight = 0.10f;
        public bool lights = true;

        static readonly (string kind, float w, float d)[] Layout =
        {
            ("office", 11f, 8f), ("storage", 7f, 6.5f), ("lounge", 8f, 6.5f), ("uneven", 7f, 7f), ("anomaly", 9f, 7f)
        };

        public const float Height = 2.75f, WallT = 0.15f, DoorW = 1.1f;

        public Result Build(ulong seed, ProfileDatabase db, string style = "backrooms", string mood = "neutral")
        {
            var res = new Result();
            var rng = new Rng(seed);
            res.root = new GameObject("TestLevel_" + seed.ToString("X"));
            var arch = new GameObject("Architecture"); arch.transform.SetParent(res.root.transform);
            var props = new GameObject("Props"); props.transform.SetParent(res.root.transform);

            // rooms along +X
            float x = 0;
            var all = new Bounds();
            for (int i = 0; i < Layout.Length; i++)
            {
                var (kind, w, d) = Layout[i];
                var b = new Bounds(new Vector3(x + w * 0.5f, Height * 0.5f, 0), new Vector3(w, Height, d));
                var room = new RoomSpec { name = $"{i}_{kind}", kind = kind == "uneven" || kind == "anomaly" ? "office" : kind, bounds = b };
                res.rooms.Add(room);
                if (i == 0) all = b; else all.Encapsulate(b);
                x += w + WallT;
            }
            // doors in shared walls (centre z) + entry door in first room's -X wall
            for (int i = 0; i < res.rooms.Count - 1; i++)
            {
                var a = res.rooms[i].bounds; float dz = rng.Range(-a.extents.z + 1.2f, a.extents.z - 1.2f);
                var door = new Vector3(a.max.x + WallT * 0.5f, 0, dz);
                res.doors.Add(door);
                res.rooms[i].doors.Add(door); res.rooms[i + 1].doors.Add(door);
            }
            var entry = new Vector3(res.rooms[0].bounds.min.x - WallT * 0.5f, 0, 0);
            res.rooms[0].doors.Add(entry);
            res.start = new Vector3(res.rooms[0].bounds.min.x + 0.6f, 0, 0);

            foreach (var room in res.rooms) BuildRoom(room, arch.transform, rng.Fork("arch/" + room.name));
            Physics.SyncTransforms();

            // navigation grid over the whole level
            var navArea = all; navArea.Expand(new Vector3(WallT * 2, 0, WallT * 2));
            res.nav = new NavGrid(navArea, 0.25f);
            foreach (var room in res.rooms) res.nav.SetWalkable(Scenarios.Shrink(room.bounds, 0.0f), true);
            foreach (var d in res.doors) res.nav.SetWalkable(new Bounds(d, new Vector3(WallT * 2 + 0.1f, 1, DoorW - 0.1f)), true);
            // walkways: entry -> door1 -> door2 ... must stay free
            var chain = new List<Vector3> { res.start };
            chain.AddRange(res.doors);
            for (int i = 0; i < chain.Count - 1; i++) res.nav.ReservePath(chain[i], chain[i + 1], 1.0f);
            // approach zones around every door
            foreach (var d in res.doors) foreach (var c in res.nav.CellsIn(new Rect2(new Vector2(d.x, d.z), new Vector2(0.9f, 0.7f), 0))) { }

            // placement
            var eng = new PlacementEngine(ArchLayer, PropsLayer, props.transform) { style = style, mood = mood, nav = res.nav, navStart = res.start };
            res.engine = eng;
            var sc = new Scenarios(eng, db);
            foreach (var room in res.rooms)
            {
                eng.room = room;
                var r = rng.Fork("room/" + room.name);
                // architecture details every room has: outlets near the floor, a switch, return-air vents
                for (int i = 0; i < r.Range(2, 5); i++) sc.WallDecor(room, r.Fork("outlet", i), "outlet");
                sc.WallDecor(room, r.Fork("switch", 0), "switch");
                for (int i = 0; i < r.Range(0, 3); i++) sc.WallDecor(room, r.Fork("vent", i), "vent");
                switch (res.rooms.IndexOf(room))
                {
                    case 0: // office
                        for (int i = 0; i < r.Range(4, 8); i++) sc.Workstation(room, r.Fork("ws", i), true);
                        for (int i = 0; i < r.Range(1, 3); i++) sc.DeskIsland(room, r.Fork("isl", i));
                        sc.ShelfUnit(room, r.Fork("oshelf", 0), 0.7f);
                        sc.AgainstWallSingle(room, r, "cabinet");
                        sc.FloorSingle(room, r, "plant"); sc.FloorSingle(room, r, "trash"); sc.AgainstWallSingle(room, r, "extinguisher");
                        sc.WallDecor(room, r, "wallclock"); sc.WallDecor(room, r, "alarm"); sc.WallDecor(room, r, "seccam");
                        for (int i = 0; i < 2; i++) sc.Stack(room, r.Fork("stack", i), r.Range(1, 4));
                        break;
                    case 1: // storage
                        for (int i = 0; i < r.Range(4, 7); i++) sc.ShelfUnit(room, r.Fork("shelf", i), r.Range(0.6f, 1f));
                        for (int i = 0; i < r.Range(3, 7); i++) sc.Stack(room, r.Fork("stack", i), r.Range(1, 5));
                        sc.FloorSingle(room, r, "cart", false); sc.FloorSingle(room, r, "boots"); sc.FloorSingle(room, r, "sign"); sc.FloorSingle(room, r, "stool");
                        sc.WallDecor(room, r, "alarm");
                        break;
                    case 2: // lounge
                        sc.Lounge(room, r);
                        sc.FloorSingle(room, r, "plant"); sc.FloorSingle(room, r, "plant");
                        sc.WallDecor(room, r, "picture"); sc.WallDecor(room, r, "picture"); sc.WallDecor(room, r, "wallclock");
                        sc.ShelfUnit(room, r, 0.5f);
                        break;
                    case 3: // uneven floor: furniture must refuse unstable spots, small objects may find flat ones
                        for (int i = 0; i < 6; i++) sc.FloorSingle(room, r.Fork("u", i), r.Pick(new[] { "box", "crate", "chair", "plant", "trash", "sign", "stool" }), false);
                        for (int i = 0; i < 3; i++) sc.Workstation(room, r.Fork("uws", i), false);
                        for (int i = 0; i < 3; i++) sc.Stack(room, r.Fork("us", i), 3);
                        break;
                    case 4: // anomaly room: intentional floating ring + normal furniture around it
                        sc.FloatingRing(room, r, r.Range(4, 8));
                        for (int i = 0; i < 3; i++) sc.Workstation(room, r.Fork("aws", i), true);
                        for (int i = 0; i < 4; i++) sc.FloorSingle(room, r.Fork("af", i), r.Pick(new[] { "chair", "box", "sign", "trash" }));
                        sc.WallDecor(room, r, "wallclock");
                        break;
                }
            }
            UnityEngine.Debug.Log($"[ADDRESS] level {seed:X}: {eng.stats}");
            return res;
        }

        // ------------------------------------------------------------------------- architecture (temporary kit)

        void BuildRoom(RoomSpec room, Transform parent, Rng rng)
        {
            var b = room.bounds;
            var g = new GameObject(room.name); g.transform.SetParent(parent);
            bool uneven = room.name.EndsWith("uneven");
            if (uneven) UnevenFloor(b, g.transform, rng);
            else Slab(g.transform, "Floor", new Vector3(b.center.x, -0.1f, b.center.z), new Vector3(b.size.x + WallT * 2, 0.2f, b.size.z + WallT * 2), floorMat, 1.6f);
            var ceil = Slab(g.transform, "Ceiling", new Vector3(b.center.x, Height + 0.1f, b.center.z), new Vector3(b.size.x + WallT * 2, 0.2f, b.size.z + WallT * 2), ceilingMat, 1.2f);
            if (lights && trofferPrefab)
            {
                // replace the slab's render mesh by a ceiling plane with rectangular holes where troffers are recessed
                Object.DestroyImmediate(ceil.GetComponent<MeshRenderer>()); Object.DestroyImmediate(ceil.GetComponent<MeshFilter>());
                var plane = new GameObject("CeilingPlane") { layer = ArchLayer };
                plane.transform.SetParent(g.transform);
                var holes = new List<Rect>();
                foreach (var (pos, rotated) in TrofferSpots(room))
                {
                    var hs = rotated ? new Vector2(0.31f, 0.61f) : new Vector2(0.61f, 0.31f);
                    holes.Add(new Rect(pos.x - hs.x, pos.z - hs.y, hs.x * 2, hs.y * 2));
                }
                plane.AddComponent<MeshFilter>().sharedMesh = CeilingWithHoles(new Rect(b.min.x - WallT, b.min.z - WallT, b.size.x + 2 * WallT, b.size.z + 2 * WallT), holes, Height, 1.2f);
                plane.AddComponent<MeshRenderer>().sharedMaterial = ceilingMat;
                plane.isStatic = true;
            }

            // walls: 4 sides, door gaps cut where a door lies on that side
            var corners = new[] { new Vector3(b.min.x, 0, b.min.z), new Vector3(b.max.x, 0, b.min.z), new Vector3(b.max.x, 0, b.max.z), new Vector3(b.min.x, 0, b.max.z) };
            for (int s = 0; s < 4; s++)
            {
                var a = corners[s]; var c = corners[(s + 1) % 4];
                var dir = (c - a).normalized; var nrm = Vector3.Cross(dir, Vector3.up); // into the room
                // collect door gaps on this side
                var gaps = new List<(float t0, float t1)>();
                foreach (var d in room.doors)
                {
                    var rel = d - a; float t = Vector3.Dot(rel, dir); float off = Vector3.Dot(rel, nrm);
                    if (Mathf.Abs(off) < WallT && t > 0 && t < (c - a).magnitude) gaps.Add((t - DoorW * 0.5f, t + DoorW * 0.5f));
                }
                gaps.Sort((p, q) => p.t0.CompareTo(q.t0));
                float len = (c - a).magnitude, cur = 0;
                var pieces = new List<(float, float)>();
                foreach (var gp in gaps) { if (gp.t0 > cur) pieces.Add((cur, gp.t0)); cur = gp.t1; }
                if (cur < len) pieces.Add((cur, len));
                // the -X wall of every room except the first is the +X wall of the previous room: register, don't rebuild
                bool shared = s == 3 && room.bounds.min.x > 0.01f;
                foreach (var (t0, t1) in pieces)
                {
                    if (t1 - t0 < 0.05f) continue;
                    var p0 = a + dir * t0; var p1 = a + dir * t1;
                    room.walls.Add(new WallSeg { a = p0, b = p1, normal = nrm, height = Height });
                    if (shared) continue;
                    float e0 = t0 <= 1e-3f ? -WallT : t0, e1 = t1 >= len - 1e-3f ? len + WallT : t1;
                    var mid = a + dir * ((e0 + e1) * 0.5f) - nrm * (WallT * 0.5f) + Vector3.up * (Height * 0.5f);
                    var w = Slab(g.transform, "Wall", mid, new Vector3(e1 - e0, Height, WallT), wallMat, 1.1f);
                    w.transform.rotation = Quaternion.LookRotation(nrm, Vector3.up);
                }
                if (shared) continue;
                // lintel above door gaps
                foreach (var gp in gaps)
                {
                    var p0 = a + dir * gp.t0; var p1 = a + dir * gp.t1;
                    var mid = (p0 + p1) * 0.5f - nrm * (WallT * 0.5f) + Vector3.up * (2.15f + (Height - 2.15f) * 0.5f);
                    var w = Slab(g.transform, "Lintel", mid, new Vector3(gp.t1 - gp.t0, Height - 2.15f, WallT), wallMat, 0.6f);
                    w.transform.rotation = Quaternion.LookRotation(nrm, Vector3.up);
                }
            }
            if (baseboardPrefab)
                foreach (var w in room.walls)
                {
                    var bb = Object.Instantiate(baseboardPrefab, g.transform);
                    bb.name = "Baseboard"; bb.layer = ArchLayer;
                    var mid = (w.a + w.b) * 0.5f;
                    bb.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(w.normal, Vector3.up));
                    bb.transform.localScale = new Vector3(w.Length, 1, 1);
                    var col = bb.AddComponent<BoxCollider>();
                    col.center = new Vector3(0, BaseboardHeight * 0.5f, BaseboardDepth * 0.5f);
                    col.size = new Vector3(1, BaseboardHeight, BaseboardDepth);
                }
            if (doorFramePrefab)
                foreach (var d in room.doors)
                {
                    if (d.x < room.bounds.min.x - 0.01f && room.bounds.min.x > 0.01f) continue; // shared door: built once by the left room
                    var fr = Object.Instantiate(doorFramePrefab, g.transform);
                    fr.name = "DoorFrame"; fr.layer = ArchLayer;
                    fr.transform.SetPositionAndRotation(new Vector3(d.x, 0, d.z), Quaternion.LookRotation(Vector3.right));
                    foreach (var sx in new[] { -1f, 1f })
                    {
                        var c = fr.AddComponent<BoxCollider>();
                        c.center = new Vector3(sx * (DoorW * 0.5f + 0.045f), 1.11f, 0); c.size = new Vector3(0.09f, 2.22f, 0.22f);
                    }
                    var top = fr.AddComponent<BoxCollider>(); top.center = new Vector3(0, 2.19f, 0); top.size = new Vector3(DoorW + 0.18f, 0.08f, 0.22f);
                }
            if (lights) CeilingLights(room, g.transform, rng);
        }

        GameObject Slab(Transform parent, string name, Vector3 center, Vector3 size, Material mat, float uvScale)
        {
            var go = new GameObject(name);
            go.layer = ArchLayer;
            go.transform.SetParent(parent);
            go.transform.position = center;
            var mesh = BoxMesh(size, uvScale);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            go.AddComponent<BoxCollider>().size = size;
            go.isStatic = true;
            return go;
        }

        /// <summary>Box mesh with world-scaled UVs (1 UV unit = 1/uvScale metres) so textures never stretch.</summary>
        public static Mesh BoxMesh(Vector3 s, float uvScale)
        {
            var h = s * 0.5f;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>(); var nrms = new List<Vector3>();
            void Face(Vector3 n, Vector3 u, Vector3 v, float su, float sv)
            {
                int i = verts.Count;
                var c = Vector3.Scale(n, h);
                var hu = u * 0.5f * su; var hv = v * 0.5f * sv;
                verts.Add(c - hu - hv); verts.Add(c + hu - hv); verts.Add(c + hu + hv); verts.Add(c - hu + hv);
                // UVs in world metres projected on the face (continuous across slabs that share planes)
                for (int k = 0; k < 4; k++) { var p = verts[i + k]; uvs.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)) / uvScale); nrms.Add(n); }
                tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            Face(Vector3.up, Vector3.right, Vector3.forward, s.x, s.z);
            Face(Vector3.down, Vector3.right, Vector3.back, s.x, s.z);
            Face(Vector3.forward, Vector3.left, Vector3.up, s.x, s.y);
            Face(Vector3.back, Vector3.right, Vector3.up, s.x, s.y);
            Face(Vector3.right, Vector3.forward, Vector3.up, s.z, s.y);
            Face(Vector3.left, Vector3.back, Vector3.up, s.z, s.y);
            var m = new Mesh(); m.SetVertices(verts); m.SetUVs(0, uvs); m.SetNormals(nrms); m.SetTriangles(tris, 0);
            m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        void UnevenFloor(Bounds b, Transform parent, Rng rng)
        {
            // heightfield: gentle bumps up to ~6 cm + one 8-degree ramp strip; furniture must refuse unstable spots
            float cell = 0.1f;
            int nx = Mathf.CeilToInt((b.size.x + WallT * 2) / cell) + 1, nz = Mathf.CeilToInt((b.size.z + WallT * 2) / cell) + 1;
            var o = new Vector3(b.min.x - WallT, 0, b.min.z - WallT);
            float ox = rng.Range(0f, 100f), oz = rng.Range(0f, 100f);
            var verts = new Vector3[nx * nz]; var uvs = new Vector2[nx * nz];
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    float wx = o.x + x * cell, wz = o.z + z * cell;
                    float hgt = (Mathf.PerlinNoise(ox + wx * 0.45f, oz + wz * 0.45f) - 0.5f) * 0.12f;
                    float ramp = Mathf.Clamp01((wx - b.center.x) / 1.5f) * Mathf.Clamp01((b.max.x - 0.5f - wx) / 0.5f);
                    if (wz > b.center.z + 1f) hgt += Mathf.Clamp(wx - b.center.x, 0, 1.5f) * Mathf.Tan(8 * Mathf.Deg2Rad);
                    float edge = Mathf.Min(Mathf.Min(wx - b.min.x, b.max.x - wx), Mathf.Min(wz - b.min.z, b.max.z - wz));
                    hgt *= Mathf.Clamp01(edge / 0.4f); // walls meet the floor at y=0
                    verts[x + z * nx] = new Vector3(wx, Mathf.Max(0, hgt + 0.03f), wz);
                    uvs[x + z * nx] = new Vector2(wx, wz) * 0.5f;
                }
            var tris = new List<int>();
            for (int z = 0; z < nz - 1; z++)
                for (int x = 0; x < nx - 1; x++)
                {
                    int i = x + z * nx;
                    tris.AddRange(new[] { i, i + nx, i + 1, i + 1, i + nx, i + nx + 1 });
                }
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = verts; m.uv = uvs; m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            var go = new GameObject("UnevenFloor") { layer = ArchLayer };
            go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = unevenMat ? unevenMat : floorMat;
            go.AddComponent<MeshCollider>().sharedMesh = m;
            // base slab under it so nothing falls through gaps
            Slab(parent, "FloorBase", new Vector3(b.center.x, -0.1f, b.center.z), new Vector3(b.size.x + WallT * 2, 0.2f, b.size.z + WallT * 2), floorMat, 0.5f);
        }

        static List<(Vector3 pos, bool rotated)> TrofferSpots(RoomSpec room)
        {
            var b = room.bounds; var list = new List<(Vector3, bool)>();
            float sx = 2.4f, sz = 2.4f;
            int nx = Mathf.Max(1, Mathf.RoundToInt(b.size.x / sx)), nz = Mathf.Max(1, Mathf.RoundToInt(b.size.z / sz));
            bool rotated = b.size.x <= b.size.z;
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                    list.Add((new Vector3(b.min.x + (ix + 0.5f) * b.size.x / nx, Height, b.min.z + (iz + 0.5f) * b.size.z / nz), rotated));
            return list;
        }

        /// <summary>Downward-facing ceiling plane at height y, cut into rectangles that avoid the holes. UVs in world metres.</summary>
        public static Mesh CeilingWithHoles(Rect area, List<Rect> holes, float y, float uvScale)
        {
            var xs = new List<float> { area.xMin, area.xMax }; var zs = new List<float> { area.yMin, area.yMax };
            foreach (var h in holes) { xs.Add(h.xMin); xs.Add(h.xMax); zs.Add(h.yMin); zs.Add(h.yMax); }
            xs.Sort(); zs.Sort();
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); var n = new List<Vector3>();
            for (int i = 0; i < xs.Count - 1; i++)
                for (int j = 0; j < zs.Count - 1; j++)
                {
                    float x0 = xs[i], x1 = xs[i + 1], z0 = zs[j], z1 = zs[j + 1];
                    if (x1 - x0 < 1e-4f || z1 - z0 < 1e-4f) continue;
                    var c = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
                    bool inHole = false; foreach (var h in holes) if (h.Contains(c)) { inHole = true; break; }
                    if (inHole) continue;
                    int k = v.Count;
                    v.Add(new Vector3(x0, y, z0)); v.Add(new Vector3(x1, y, z0)); v.Add(new Vector3(x1, y, z1)); v.Add(new Vector3(x0, y, z1));
                    foreach (var p in v.GetRange(k, 4)) { uv.Add(new Vector2(p.x, p.z) / uvScale); n.Add(Vector3.down); }
                    t.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 }); // wound to face down
                }
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        void CeilingLights(RoomSpec room, Transform parent, Rng rng)
        {
            if (trofferPrefab)
            {
                int k = 0;
                foreach (var (pos, rotated) in TrofferSpots(room))
                {
                    var tr = Object.Instantiate(trofferPrefab, parent);
                    tr.name = "Troffer"; tr.layer = ArchLayer;
                    tr.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, rotated ? 90 : 0, 0)); // recessed: lip flush with the ceiling plane
                    // glTF emissive materials don't glow reliably in URP: swap the diffuser for our HDR emissive panel material
                    foreach (var r in tr.GetComponentsInChildren<Renderer>())
                    {
                        var mats = r.sharedMaterials;
                        for (int mi = 0; mi < mats.Length; mi++) if (mats[mi] && mats[mi].name.StartsWith("troffer_diffuser") && lightMat) mats[mi] = lightMat;
                        r.sharedMaterials = mats;
                    }
                    var lg = new GameObject("Light"); lg.transform.SetParent(tr.transform);
                    lg.transform.SetPositionAndRotation(pos + Vector3.down * 0.02f, Quaternion.Euler(90, 0, 0));
                    var l = lg.AddComponent<Light>();
                    l.type = LightType.Spot; l.spotAngle = 170f; l.innerSpotAngle = 100f; l.range = 7.5f; l.intensity = 3.0f;
                    l.color = new Color(1f, 0.95f, 0.82f);
                    l.shadows = (k++ % 2 == 0) ? LightShadows.Soft : LightShadows.None; l.shadowStrength = 0.7f;
                }
                return;
            }
            var b = room.bounds;
            float sx = 2.4f, sz = 2.4f;
            int nx = Mathf.Max(1, Mathf.RoundToInt(b.size.x / sx)), nz = Mathf.Max(1, Mathf.RoundToInt(b.size.z / sz));
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    var p = new Vector3(b.min.x + (ix + 0.5f) * b.size.x / nx, Height - 0.005f, b.min.z + (iz + 0.5f) * b.size.z / nz);
                    GameObject panel;
                    if (trofferPrefab)
                    {
                        panel = Object.Instantiate(trofferPrefab, parent);
                        panel.transform.position = new Vector3(p.x, Height - 0.09f, p.z);
                        panel.transform.rotation = Quaternion.Euler(0, (b.size.x > b.size.z) ? 0 : 90, 0);
                    }
                    else
                    {
                        panel = new GameObject("LightPanel");
                        panel.transform.SetParent(parent);
                        panel.transform.position = p;
                        panel.AddComponent<MeshFilter>().sharedMesh = BoxMesh(new Vector3(1.2f, 0.01f, 0.6f), 1f);
                        panel.AddComponent<MeshRenderer>().sharedMaterial = lightMat;
                    }
                    panel.name = "Troffer"; panel.layer = ArchLayer;
                    var lg = new GameObject("Light"); lg.transform.SetParent(panel.transform); lg.transform.position = new Vector3(p.x, Height - 0.1f, p.z);
                    lg.transform.rotation = Quaternion.Euler(90, 0, 0);
                    var l = lg.AddComponent<Light>();
                    l.type = LightType.Spot; l.spotAngle = 165f; l.innerSpotAngle = 90f; l.range = 7.5f; l.intensity = 3.2f;
                    l.color = new Color(1f, 0.95f, 0.82f);
                    l.shadows = (ix + iz) % 2 == 0 ? LightShadows.Soft : LightShadows.None;
                    l.shadowStrength = 0.75f;
                }
        }
    }
}
