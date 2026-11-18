using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Address.Placement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Address.EditorTools
{
    /// <summary>
    /// Turns every source model into a placement-ready prefab + PlacementProfile:
    ///  1. normalises pivot (bottom centre) and orientation (front = +Z, from profiles.json "yaw")
    ///  2. extracts horizontal support surfaces by ray casting the render mesh (desk tops, shelves, seats)
    ///  3. voxelises the mesh on a grid whose Y breakpoints coincide EXACTLY with those surfaces,
    ///     fills the interior, and greedily merges voxels into a compact set of box colliders
    ///     (so a cup standing on a desk touches the desk collider at the real visual height - no floating, no sinking)
    ///  4. computes floor footprint hull and centre of mass.
    /// </summary>
    public static class ProfileBuilder
    {
        const string SrcRoot = "Assets/ThirdParty/PolyHaven";
        const string BlenderRoot = "Assets/ADDRESS/Art/Models";
        const string OutPrefabs = "Assets/ADDRESS/Generated/Prefabs";
        const string OutProfiles = "Assets/ADDRESS/Generated/Profiles";
        public const string SemanticsPath = "Assets/ADDRESS/Data/profiles.json";
        const int MaxBoxes = 120;

        [MenuItem("ADDRESS/Build Placement Profiles")]
        public static void BuildAll() => Build(null);

        /// <summary>batchmode entry: -executeMethod Address.EditorTools.ProfileBuilder.BatchBuild [-only id1,id2]</summary>
        public static void BatchBuild()
        {
            string only = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-only") only = args[i + 1];
            Build(only?.Split(','));
        }

        public static void Build(string[] only)
        {
            ProjectSetup.EnsureLayers();
            Directory.CreateDirectory(OutPrefabs);
            Directory.CreateDirectory(OutProfiles);
            var semantics = LoadSemantics();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bool oldBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            var report = new List<string>();
            try
            {
                foreach (var (id, src) in FindSources())
                {
                    if (only != null && !only.Contains(id)) continue;
                    semantics.TryGetValue(id, out var sem);
                    try
                    {
                        if (sem != null && sem.split)
                        {
                            int n = PartCount(src);
                            for (int k = 0; k < n; k++)
                            {
                                string pid = $"{id}_{(char)('a' + k)}";
                                semantics.TryGetValue(pid, out var psem);
                                psem ??= Clone(sem, pid);
                                report.Add(BuildOne(pid, src, psem, k));
                            }
                        }
                        else report.Add(BuildOne(id, src, sem));
                    }
                    catch (Exception e) { report.Add($"FAIL {id}: {e.Message}"); Debug.LogException(e); }
                }
            }
            finally { Physics.queriesHitBackfaces = oldBack; }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            File.WriteAllLines("../Docs/reports/profile_build.txt", report);
            Debug.Log("[ADDRESS] Profiles built:\n" + string.Join("\n", report));
        }

        static IEnumerable<(string id, GameObject src)> FindSources()
        {
            foreach (var root in new[] { SrcRoot, BlenderRoot })
            {
                if (!AssetDatabase.IsValidFolder(root)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { root }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext != ".gltf" && ext != ".glb" && ext != ".fbx") continue;
                    string id = Path.GetFileName(Path.GetDirectoryName(path));
                    if (root == BlenderRoot) id = Path.GetFileNameWithoutExtension(path);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go) yield return (id, go);
                }
            }
        }

        public static Dictionary<string, ProfileSemantics> LoadSemantics()
        {
            var d = new Dictionary<string, ProfileSemantics>();
            if (!File.Exists(SemanticsPath)) return d;
            var file = JsonUtility.FromJson<ProfileSemanticsFile>(File.ReadAllText(SemanticsPath));
            foreach (var p in file.profiles) d[p.id] = p;
            return d;
        }

        // ------------------------------------------------------------------------------------------------

        static ProfileSemantics Clone(ProfileSemantics s, string id)
        {
            var c = JsonUtility.FromJson<ProfileSemantics>(JsonUtility.ToJson(s)); c.id = id; c.split = false; return c;
        }

        /// <summary>The node whose children are the separate items of a set (first level with more than one renderer child).</summary>
        static Transform PartsParent(Transform t)
        {
            while (true)
            {
                var withR = new List<Transform>();
                foreach (Transform c in t) if (c.GetComponentInChildren<Renderer>(true)) withR.Add(c);
                if (withR.Count == 1 && !withR[0].GetComponent<Renderer>()) { t = withR[0]; continue; }
                return t;
            }
        }

        /// <summary>Items of a set = clusters of renderer children whose bounds touch (handle + body = one suitcase).</summary>
        static List<List<Transform>> Clusters(Transform parentOfParts)
        {
            var kids = new List<Transform>();
            foreach (Transform c in parentOfParts) if (c.GetComponentInChildren<Renderer>(true)) kids.Add(c);
            var bounds = kids.Select(k =>
            {
                var rs = k.GetComponentsInChildren<Renderer>(true); var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                b.Expand(0.01f); return b;
            }).ToList();
            var parent = Enumerable.Range(0, kids.Count).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int i = 0; i < kids.Count; i++) for (int j = i + 1; j < kids.Count; j++) if (bounds[i].Intersects(bounds[j])) parent[Find(i)] = Find(j);
            var groups = new Dictionary<int, List<Transform>>();
            for (int i = 0; i < kids.Count; i++) { int r = Find(i); if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<Transform>(); l.Add(kids[i]); }
            // stable order: by x then z of cluster centre
            return groups.Values.OrderBy(g => g.Average(t => t.position.x)).ThenBy(g => g.Average(t => t.position.z)).ToList();
        }

        static int PartCount(GameObject src)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            int n = Clusters(PartsParent(inst.transform)).Count;
            UnityEngine.Object.DestroyImmediate(inst);
            return Mathf.Max(1, n);
        }

        static string BuildOne(string id, GameObject src, ProfileSemantics sem, int part = -1)
        {
            sem ??= new ProfileSemantics { id = id };
            var root = new GameObject(id);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
            model.name = "Model";
            if (part >= 0)
            {
                var clusters = Clusters(PartsParent(model.transform));
                var kill = new List<GameObject>();
                for (int ci = 0; ci < clusters.Count; ci++) if (ci != part) foreach (var t in clusters[ci]) kill.Add(t.gameObject);
                // keep prefab connection: deactivate instead of destroy (removing children of a prefab instance is not allowed)
                foreach (var k in kill) { k.SetActive(false); foreach (var r in k.GetComponentsInChildren<Renderer>(true)) r.enabled = false; }
            }
            model.transform.SetParent(root.transform, false);
            model.transform.localRotation = Quaternion.Euler(0, sem.yaw, 0);
            model.transform.localScale = Vector3.one * sem.scale;

            // pivot: bottom centre
            var b = RenderBounds(model);
            model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = RenderBounds(model);

            // temporary exact mesh colliders for queries
            var temp = new List<GameObject>();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(false))
            {
                if (!mf.sharedMesh || !mf.gameObject.activeInHierarchy) continue;
                var g = new GameObject("probe"); g.layer = ProjectSetup.LayerProbe;
                g.transform.SetParent(mf.transform, false);
                g.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                temp.Add(g);
            }
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var baked = new Mesh(); smr.BakeMesh(baked, true);
                var g = new GameObject("probe"); g.layer = ProjectSetup.LayerProbe;
                g.transform.SetParent(smr.transform, false);
                g.AddComponent<MeshCollider>().sharedMesh = baked;
                temp.Add(g);
            }
            Physics.SyncTransforms();
            int mask = 1 << ProjectSetup.LayerProbe;

            float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
            var surfaces = ExtractSurfaces(b, mask, maxDim);
            if (sem.surfaces == "none") surfaces.Clear();
            else if (sem.surfaces == "top") surfaces.RemoveAll(s => s.kind != "top");

            float voxel = Mathf.Clamp(maxDim / 60f, 0.006f, 0.03f);
            List<LocalBox> boxes = null; Vector3 com = Vector3.zero; Vector2[] hull = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                boxes = Voxelise(b, surfaces, voxel, mask, out com, out hull);
                if (boxes.Count <= MaxBoxes) break;
                voxel *= 1.35f;
            }

            foreach (var t in temp) UnityEngine.Object.DestroyImmediate(t);

            // colliders on the prefab
            var colRoot = new GameObject("Colliders");
            colRoot.transform.SetParent(root.transform, false);
            foreach (var bx in boxes)
            {
                var bc = colRoot.AddComponent<BoxCollider>();
                bc.center = bx.center; bc.size = bx.size;
            }
            // glTF glass comes in opaque: use the project's transparent glass
            var glassMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Glass.mat");
            if (glassMat)
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    var ms = r.sharedMaterials; bool ch = false;
                    for (int i = 0; i < ms.Length; i++) if (ms[i] && ms[i].name.Contains("glass")) { ms[i] = glassMat; ch = true; }
                    if (ch) r.sharedMaterials = ms;
                }
            SetLayerRecursive(root, ProjectSetup.LayerProps);
            var po = root.AddComponent<PlacedObject>();

            // profile asset
            var profPath = $"{OutProfiles}/{id}.asset";
            var prof = AssetDatabase.LoadAssetAtPath<PlacementProfile>(profPath);
            if (!prof) { prof = ScriptableObject.CreateInstance<PlacementProfile>(); AssetDatabase.CreateAsset(prof, profPath); }
            ApplySemantics(prof, sem);
            prof.id = id;
            prof.size = b.size;
            prof.centerOfMass = com;
            prof.boxes = boxes;
            prof.surfaces = surfaces;
            prof.footprint = hull;

            var prefabPath = $"{OutPrefabs}/{id}.prefab";
            po.profile = prof;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            prof.prefab = prefab;
            EditorUtility.SetDirty(prof);
            UnityEngine.Object.DestroyImmediate(root);

            return $"{id}: size {b.size.x:F2}x{b.size.y:F2}x{b.size.z:F2} voxel {voxel * 100:F1}cm boxes {boxes.Count} surfaces {surfaces.Count} " +
                   string.Join(" ", surfaces.Select(s => $"[{s.kind} y={s.height:F3} a={s.area:F3} c={Mathf.Min(s.clearance, 9):F2}]"));
        }

        public static void ApplySemantics(PlacementProfile p, ProfileSemantics s)
        {
            p.group = string.IsNullOrEmpty(s.group) ? s.id : s.group;
            Enum.TryParse(s.support, out p.support);
            Enum.TryParse(s.rotation, out p.rotation);
            Enum.TryParse(s.massClass, out p.massClass);
            p.mass = s.mass; p.maxTiltDeg = s.maxTilt; p.minSpacing = s.minSpacing; p.clearanceFront = s.clearanceFront;
            p.canStack = s.canStack; p.openable = s.openable; p.scaleJitter = s.scaleJitter;
            p.fallenChance = s.fallenChance; p.fallenEuler = s.fallenEuler; p.wallMin = s.wallMin; p.wallMax = s.wallMax; p.disabled = s.disabled;
            p.styles = new List<string>(s.styles); p.moods = s.moods.Count > 0 ? new List<string>(s.moods) : new List<string> { "neutral" };
            p.rooms = new List<string>(s.rooms);
        }

        static Bounds RenderBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void SetLayerRecursive(GameObject g, int layer)
        {
            g.layer = layer;
            foreach (Transform c in g.transform) SetLayerRecursive(c.gameObject, layer);
        }

        // ---------------------------------------------- surfaces ----------------------------------------

        static List<SurfaceRegion> ExtractSurfaces(Bounds b, int mask, float maxDim)
        {
            float cell = Mathf.Clamp(maxDim / 60f, 0.01f, 0.025f);
            int nx = Mathf.Max(1, Mathf.CeilToInt(b.size.x / cell)), nz = Mathf.Max(1, Mathf.CeilToInt(b.size.z / cell));
            var ox = b.min.x; var oz = b.min.z;
            float top = b.max.y + 0.05f;
            const float Unlimited = 10f;
            // per cell: list of (y, clearance) for upward faces
            var hitsY = new float[nx * nz][];
            var clear = new float[nx * nz][];
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                var o = new Vector3(ox + (x + 0.5f) * cell, top, oz + (z + 0.5f) * cell);
                var hits = RayStack(o, top + 0.01f, mask);
                var ys = new List<float>(); var cs = new List<float>();
                float ceiling = top + Unlimited;
                foreach (var h in hits)
                {
                    float y = h.point.y;
                    if (h.normal.y > 0.97f && y > 0.015f)
                    {
                        ys.Add(y); cs.Add(ceiling >= top + Unlimited - 0.001f ? Unlimited : ceiling - y);
                    }
                    ceiling = y;
                }
                hitsY[x + z * nx] = ys.ToArray(); clear[x + z * nx] = cs.ToArray();
            }

            // connected components of (cell, layer) with |dy| <= 4 mm
            var visited = new HashSet<(int, int)>();
            var result = new List<SurfaceRegion>();
            for (int i = 0; i < nx * nz; i++)
            for (int l = 0; l < hitsY[i].Length; l++)
            {
                if (visited.Contains((i, l))) continue;
                var comp = new List<(int cell, int layer)>();
                var q = new Queue<(int, int)>(); q.Enqueue((i, l)); visited.Add((i, l));
                while (q.Count > 0)
                {
                    var (ci, cl) = q.Dequeue(); comp.Add((ci, cl));
                    float y = hitsY[ci][cl];
                    int cx = ci % nx, cz = ci / nx;
                    foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int xx = cx + dx, zz = cz + dz;
                        if (xx < 0 || zz < 0 || xx >= nx || zz >= nz) continue;
                        int ni = xx + zz * nx;
                        for (int nl = 0; nl < hitsY[ni].Length; nl++)
                            if (!visited.Contains((ni, nl)) && Mathf.Abs(hitsY[ni][nl] - y) <= 0.004f) { visited.Add((ni, nl)); q.Enqueue((ni, nl)); }
                    }
                }
                // drop cells with too little room above
                comp.RemoveAll(c => clear[c.cell][c.layer] < 0.03f);
                if (comp.Count == 0) continue;
                var set = new HashSet<int>(comp.Select(c => c.cell));
                // erode by one cell: edges of rounded/bevelled tops are not safe support
                var eroded = set.Where(c =>
                {
                    int cx = c % nx, cz = c / nx;
                    return set.Contains(c + 1) && set.Contains(c - 1) && set.Contains(c + nx) && set.Contains(c - nx) && cx > 0 && cx < nx - 1 && cz > 0 && cz < nz - 1;
                }).ToList();
                float area = eroded.Count * cell * cell;
                if (area < 0.0036f) continue;
                var ysorted = comp.Select(c => hitsY[c.cell][c.layer]).OrderBy(v => v).ToList();
                float height = ysorted[ysorted.Count / 2];
                float minClear = comp.Where(c => set.Contains(c.cell)).Min(c => clear[c.cell][c.layer]);
                var reg = new SurfaceRegion
                {
                    kind = minClear >= Unlimited - 0.01f ? "top" : "shelf",
                    height = height,
                    clearance = minClear,
                    cell = cell,
                    origin = new Vector2(ox, oz),
                    w = nx, h = nz,
                    cells = eroded,
                    area = area
                };
                result.Add(reg);
            }
            // biggest first
            result.Sort((a, c) => c.area.CompareTo(a.area));
            // seats: a "top" surface between 0.35 and 0.6 m on a chair-like object is a seat (decided later by semantics)
            return result;
        }

        /// <summary>All hits straight down, including several per collider (RaycastAll returns one per collider).</summary>
        static List<RaycastHit> RayStack(Vector3 o, float len, int mask)
        {
            var list = new List<RaycastHit>();
            float travelled = 0;
            var p = o;
            for (int guard = 0; guard < 64 && travelled < len; guard++)
            {
                if (!Physics.Raycast(p, Vector3.down, out var h, len - travelled, mask, QueryTriggerInteraction.Ignore)) break;
                list.Add(h);
                float step = h.distance + 0.0004f;
                travelled += step; p += Vector3.down * step;
            }
            return list;
        }

        // ---------------------------------------------- voxels ------------------------------------------

        static List<float> Breaks(float min, float max, float step, IEnumerable<float> fixedPoints)
        {
            var pts = new List<float> { min, max };
            foreach (var p in fixedPoints) if (p > min + 0.002f && p < max - 0.002f) pts.Add(p);
            pts.Sort();
            var outp = new List<float>();
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float a = pts[i], c = pts[i + 1];
                if (c - a < 0.0015f) continue;
                int n = Mathf.Max(1, Mathf.RoundToInt((c - a) / step));
                for (int k = 0; k < n; k++) outp.Add(a + (c - a) * k / n);
            }
            if (outp.Count == 0 || outp[0] > min + 1e-6f) outp.Insert(0, min);
            outp.Add(max);
            return outp;
        }

        static List<LocalBox> Voxelise(Bounds b, List<SurfaceRegion> surfaces, float voxel, int mask, out Vector3 com, out Vector2[] hull)
        {
            var xs = Breaks(b.min.x, b.max.x, voxel, Array.Empty<float>());
            var zs = Breaks(b.min.z, b.max.z, voxel, Array.Empty<float>());
            var ys = Breaks(0f, b.max.y, voxel, surfaces.Select(s => s.height));
            int nx = xs.Count - 1, ny = ys.Count - 1, nz = zs.Count - 1;
            var solid = new bool[nx, ny, nz];
            const float eps = 0.0008f;
            for (int x = 0; x < nx; x++)
            for (int y = 0; y < ny; y++)
            for (int z = 0; z < nz; z++)
            {
                var c = new Vector3((xs[x] + xs[x + 1]) * 0.5f, (ys[y] + ys[y + 1]) * 0.5f, (zs[z] + zs[z + 1]) * 0.5f);
                var h = new Vector3((xs[x + 1] - xs[x]) * 0.5f, (ys[y + 1] - ys[y]) * 0.5f, (zs[z + 1] - zs[z]) * 0.5f);
                h = new Vector3(nx > 1 ? h.x - Mathf.Min(eps, h.x * 0.3f) : h.x + 0.0005f,
                                ny > 1 ? h.y - Mathf.Min(eps, h.y * 0.3f) : h.y + 0.0005f,
                                nz > 1 ? h.z - Mathf.Min(eps, h.z * 0.3f) : h.z + 0.0005f);
                solid[x, y, z] = Physics.CheckBox(c, h, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
            }

            // flood-fill outside air on a padded grid; anything not reachable is interior => solid
            var outside = new bool[nx + 2, ny + 2, nz + 2];
            var q = new Queue<(int, int, int)>();
            q.Enqueue((0, 0, 0)); outside[0, 0, 0] = true;
            var dirs = new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
            while (q.Count > 0)
            {
                var (x, y, z) = q.Dequeue();
                foreach (var (dx, dy, dz) in dirs)
                {
                    int X = x + dx, Y = y + dy, Z = z + dz;
                    if (X < 0 || Y < 0 || Z < 0 || X > nx + 1 || Y > ny + 1 || Z > nz + 1 || outside[X, Y, Z]) continue;
                    bool inGrid = X >= 1 && Y >= 1 && Z >= 1 && X <= nx && Y <= ny && Z <= nz;
                    if (inGrid && solid[X - 1, Y - 1, Z - 1]) continue;
                    outside[X, Y, Z] = true; q.Enqueue((X, Y, Z));
                }
            }
            var shell = (bool[,,])solid.Clone();
            double vol = 0; Vector3 acc = Vector3.zero;
            for (int x = 0; x < nx; x++) for (int y = 0; y < ny; y++) for (int z = 0; z < nz; z++)
            {
                if (!outside[x + 1, y + 1, z + 1]) solid[x, y, z] = true;
                if (solid[x, y, z])
                {
                    float v = (xs[x + 1] - xs[x]) * (ys[y + 1] - ys[y]) * (zs[z + 1] - zs[z]);
                    vol += v;
                    acc += v * new Vector3((xs[x] + xs[x + 1]) * 0.5f, (ys[y] + ys[y + 1]) * 0.5f, (zs[z] + zs[z + 1]) * 0.5f);
                }
            }
            com = vol > 0 ? acc / (float)vol : new Vector3(0, b.size.y * 0.5f, 0);

            // surfaces enclosed by the (filled) interior are inside closed drawers/cabinets: only reachable by opening
            int Ix(List<float> br, float v) { for (int i = 0; i < br.Count - 1; i++) if (v < br[i + 1]) return i; return br.Count - 2; }
            foreach (var s in surfaces)
            {
                int inner = 0, total = 0;
                int yi = Ix(ys, s.height + 0.01f);
                foreach (var ci in s.cells)
                {
                    var p = s.CellCenter(ci);
                    int xi = Ix(xs, p.x), zi = Ix(zs, p.y);
                    total++;
                    if (solid[xi, yi, zi] && !shell[xi, yi, zi]) inner++;
                }
                if (total > 0 && inner > total / 2) s.kind = "inside";
            }

            // floor footprint: solid voxels in the lowest layer
            var pts = new List<Vector2>();
            for (int x = 0; x < nx; x++) for (int z = 0; z < nz; z++)
                if (solid[x, 0, z])
                {
                    pts.Add(new Vector2(xs[x], zs[z])); pts.Add(new Vector2(xs[x + 1], zs[z]));
                    pts.Add(new Vector2(xs[x], zs[z + 1])); pts.Add(new Vector2(xs[x + 1], zs[z + 1]));
                }
            hull = ConvexHull(pts);

            // greedy merge into boxes
            var used = new bool[nx, ny, nz];
            var boxes = new List<LocalBox>();
            if (vol <= 0)
            {
                // open thin geometry (single sheets of paper): the bounds themselves are the collider
                boxes.Add(new LocalBox { center = new Vector3(b.center.x, b.size.y * 0.5f, b.center.z), size = new Vector3(b.size.x, Mathf.Max(b.size.y, 0.002f), b.size.z) });
                return boxes;
            }
            for (int y = 0; y < ny; y++)
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                if (!solid[x, y, z] || used[x, y, z]) continue;
                int x2 = x; while (x2 + 1 < nx && solid[x2 + 1, y, z] && !used[x2 + 1, y, z]) x2++;
                int z2 = z;
                while (z2 + 1 < nz) { bool ok = true; for (int i = x; i <= x2; i++) if (!solid[i, y, z2 + 1] || used[i, y, z2 + 1]) { ok = false; break; } if (!ok) break; z2++; }
                int y2 = y;
                while (y2 + 1 < ny)
                {
                    bool ok = true;
                    for (int k = z; k <= z2 && ok; k++) for (int i = x; i <= x2; i++) if (!solid[i, y2 + 1, k] || used[i, y2 + 1, k]) { ok = false; break; }
                    if (!ok) break; y2++;
                }
                for (int yy = y; yy <= y2; yy++) for (int k = z; k <= z2; k++) for (int i = x; i <= x2; i++) used[i, yy, k] = true;
                var mn = new Vector3(xs[x], ys[y], zs[z]); var mx = new Vector3(xs[x2 + 1], ys[y2 + 1], zs[z2 + 1]);
                boxes.Add(new LocalBox { center = (mn + mx) * 0.5f, size = mx - mn });
            }
            return boxes;
        }

        static Vector2[] ConvexHull(List<Vector2> p)
        {
            if (p.Count < 3) return p.ToArray();
            var pts = p.Distinct().OrderBy(v => v.x).ThenBy(v => v.y).ToList();
            var h = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 c) => (a.x - o.x) * (c.y - o.y) - (a.y - o.y) * (c.x - o.x);
            foreach (var v in pts) { while (h.Count >= 2 && Cross(h[^2], h[^1], v) <= 0) h.RemoveAt(h.Count - 1); h.Add(v); }
            int lower = h.Count + 1;
            for (int i = pts.Count - 2; i >= 0; i--) { var v = pts[i]; while (h.Count >= lower && Cross(h[^2], h[^1], v) <= 0) h.RemoveAt(h.Count - 1); h.Add(v); }
            h.RemoveAt(h.Count - 1);
            return h.ToArray();
        }
    }
}
