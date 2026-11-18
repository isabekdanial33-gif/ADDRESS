using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Address.Placement;
using Address.TestLevel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Address.EditorTools
{
    /// <summary>
    /// Placement test farm. Generates the technical test level for many seeds, audits each with the independent
    /// PlacementAudit, and writes Docs/reports/farm/report.md plus screenshots of failures and overview shots.
    /// batchmode: -executeMethod Address.EditorTools.TestFarm.Run -seeds 100 -overview 2 -style backrooms
    /// </summary>
    public static class TestFarm
    {
        const string Out = "../Docs/reports/farm";
        public const string DbPath = "Assets/ADDRESS/Generated/ProfileDatabase.asset";

        public static ProfileDatabase RefreshDatabase()
        {
            var db = AssetDatabase.LoadAssetAtPath<ProfileDatabase>(DbPath);
            if (!db) { db = ScriptableObject.CreateInstance<ProfileDatabase>(); AssetDatabase.CreateAsset(db, DbPath); }
            db.profiles = AssetDatabase.FindAssets("t:PlacementProfile", new[] { "Assets/ADDRESS/Generated/Profiles" })
                .Select(g => AssetDatabase.LoadAssetAtPath<PlacementProfile>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p).OrderBy(p => p.id).ToList();
            // semantics may have changed since geometry was built: re-apply without rebuilding geometry
            var sem = ProfileBuilder.LoadSemantics();
            foreach (var p in db.profiles)
            {
                string baseId = p.id;
                if (!sem.TryGetValue(baseId, out var s) && baseId.Length > 2 && baseId[^2] == '_') sem.TryGetValue(baseId[..^2], out s);
                if (s != null) { ProfileBuilder.ApplySemantics(p, s); EditorUtility.SetDirty(p); }
            }
            db.Reindex();
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            return db;
        }

        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static TestLevelBuilder MakeBuilder()
        {
            Material M(string id) => AssetDatabase.LoadAssetAtPath<Material>($"Assets/ADDRESS/Generated/Materials/{id}.mat");
            var light = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/LightPanel.mat");
            if (!light)
            {
                light = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                light.SetColor("_BaseColor", Color.white);
                light.EnableKeyword("_EMISSION");
                light.SetColor("_EmissionColor", new Color(1f, 0.96f, 0.86f) * 4f);
                light.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                AssetDatabase.CreateAsset(light, "Assets/ADDRESS/Generated/Materials/LightPanel.mat");
            }
            GameObject A(string n) => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ADDRESS/Art/Architecture/{n}.glb");
            return new TestLevelBuilder { trofferPrefab = A("troffer"), baseboardPrefab = A("baseboard_1m"), doorFramePrefab = A("door_frame"), floorMat = M("BR_Carpet") ?? M("Carpet016"), wallMat = M("BR_Wallpaper") ?? M("Wallpaper001A"), ceilingMat = M("OfficeCeiling001"), unevenMat = M("Concrete031"), lightMat = light };
        }

        public static void Run()
        {
            int seeds = int.Parse(Arg("-seeds", "50"));
            int overview = int.Parse(Arg("-overview", "1"));
            int firstSeed = int.Parse(Arg("-first", "1"));
            int maxFailShots = int.Parse(Arg("-failshots", "30"));
            string style = Arg("-style", "backrooms");
            Directory.CreateDirectory(Out); Directory.CreateDirectory($"{Out}/failures"); Directory.CreateDirectory($"{Out}/overview");
            foreach (var f in Directory.GetFiles($"{Out}/failures")) File.Delete(f);

            var db = RefreshDatabase();
            var builder = MakeBuilder();
            {
                var dbg = new Address.Core.Rng(1);
                var desk = db.Group("desk");
                Debug.Log($"[ADDRESS] db profiles {db.profiles.Count}, desks {desk.Count}, first desk styles [{(desk.Count > 0 ? string.Join(",", desk[0].styles) : "-")}] moods [{(desk.Count > 0 ? string.Join(",", desk[0].moods) : "-")}] prefab {(desk.Count > 0 ? desk[0].prefab : null)} pick {db.Pick("desk", dbg, style, "neutral", "office")}");
            }
            var totals = new Dictionary<string, int>();
            var rejections = new Dictionary<Check, int>();
            var perAlgo = new Dictionary<string, int>();
            var sb = new StringBuilder();
            int totalObjects = 0, totalRequested = 0, failShots = 0, cleanLevels = 0, totalAnom = 0;
            float maxPen = 0, maxDrift = 0; double genMs = 0, auditMs = 0;
            var rows = new List<string>();
            var issueLines = new List<string>();

            for (int s = firstSeed; s < firstSeed + seeds; s++)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // NewScene unloads unused assets: reload the database so cached indices don't point at dead objects
                db = AssetDatabase.LoadAssetAtPath<ProfileDatabase>(DbPath); db.Reindex();
                builder = MakeBuilder();
                var sw = Stopwatch.StartNew();
                var res = builder.Build((ulong)s * 0x9E3779B97F4A7C15UL, db, style);
                genMs += sw.Elapsed.TotalMilliseconds; sw.Restart();
                var audit = new PlacementAudit(TestLevelBuilder.ArchLayer, TestLevelBuilder.PropsLayer);
                audit.Run(res.engine.placed, res.nav, res.start);
                auditMs += sw.Elapsed.TotalMilliseconds;

                totalObjects += audit.objects; totalRequested += res.engine.stats.requested; totalAnom += audit.anomalies;
                maxPen = Mathf.Max(maxPen, audit.maxPenetration); maxDrift = Mathf.Max(maxDrift, audit.maxDrift);
                foreach (var kv in audit.CountByKind()) totals[kv.Key] = totals.GetValueOrDefault(kv.Key) + kv.Value;
                foreach (var kv in res.engine.stats.rejections) rejections[kv.Key] = rejections.GetValueOrDefault(kv.Key) + kv.Value;
                if (audit.issues.Count == 0) cleanLevels++;
                rows.Add($"| {s} | {audit.objects} | {audit.anomalies} | {res.engine.stats.skipped} | {audit.issues.Count} | {string.Join(", ", audit.CountByKind().Select(k => k.Key + ":" + k.Value))} |");
                foreach (var iss in audit.issues)
                {
                    issueLines.Add($"- seed {s}: {iss}");
                    string algo = iss.obj ? iss.obj.placedBy : "nav";
                    perAlgo[algo + " -> " + iss.kind] = perAlgo.GetValueOrDefault(algo + " -> " + iss.kind) + 1;
                    if (iss.obj && failShots < maxFailShots)
                    {
                        Lighting(res);
                        FailureShot(iss, $"{Out}/failures/s{s}_{failShots:D3}_{iss.kind}_{iss.obj.name}.png");
                        failShots++;
                    }
                }
                if (s < firstSeed + overview) { Lighting(res); Overview(res, $"{Out}/overview/seed{s}"); }
                Object.DestroyImmediate(res.root);
            }

            sb.AppendLine($"# Placement test farm — {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine($"Style: **{style}**, seeds {firstSeed}..{firstSeed + seeds - 1} ({seeds} levels × 5 rooms).");
            sb.AppendLine();
            sb.AppendLine("| metric | value |\n|---|---|");
            sb.AppendLine($"| objects placed | {totalObjects} (of {totalRequested} requested; rest refused by checks) |");
            sb.AppendLine($"| intentional anomalies | {totalAnom} |");
            sb.AppendLine($"| levels with zero issues | {cleanLevels}/{seeds} |");
            sb.AppendLine($"| total issues | {totals.Values.Sum()} |");
            foreach (var kv in totals.OrderByDescending(k => k.Value)) sb.AppendLine($"| issues: {kv.Key} | {kv.Value} |");
            sb.AppendLine($"| max penetration | {maxPen * 100:F2} cm |");
            sb.AppendLine($"| max physics drift | {maxDrift * 100:F2} cm |");
            sb.AppendLine($"| avg generation time | {genMs / seeds:F0} ms/level |");
            sb.AppendLine($"| avg audit time | {auditMs / seeds:F0} ms/level |");
            sb.AppendLine();
            sb.AppendLine("## Candidate rejections by check (engine side, expected)\n");
            foreach (var kv in rejections.OrderByDescending(k => k.Value)) sb.AppendLine($"- {kv.Key}: {kv.Value}");
            sb.AppendLine("\n## Issues by placing algorithm\n");
            foreach (var kv in perAlgo.OrderByDescending(k => k.Value)) sb.AppendLine($"- {kv.Key}: {kv.Value}");
            sb.AppendLine("\n## Per seed\n\n| seed | objects | anomalies | refused | issues | kinds |\n|---|---|---|---|---|---|");
            foreach (var r in rows) sb.AppendLine(r);
            sb.AppendLine("\n## Issue list (first 300)\n");
            foreach (var l in issueLines.Take(300)) sb.AppendLine(l);
            File.WriteAllText($"{Out}/report.md", sb.ToString());
            Debug.Log($"[ADDRESS] farm done: objects {totalObjects}, issues {totals.Values.Sum()} ({string.Join(", ", totals.Select(k => k.Key + ":" + k.Value))}), clean {cleanLevels}/{seeds}, gen {genMs / seeds:F0}ms audit {auditMs / seeds:F0}ms");
        }

        // ------------------------------------------------------------------ rendering helpers

        static VolumeProfile _profile;
        public static void Lighting(TestLevelBuilder.Result res)
        {
            // fake bounce light: warm yellow walls reflect onto everything (no realtime GI in procedural levels)
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Trilight: 'sky' lights up-facing normals (floor, desk tops), 'ground' lights DOWN-facing ones (the ceiling):
            // in a lit yellow room the ceiling receives strong warm bounce from carpet and walls.
            RenderSettings.ambientSkyColor = new Color(0.13f, 0.12f, 0.08f);
            RenderSettings.ambientEquatorColor = new Color(0.20f, 0.18f, 0.11f);
            RenderSettings.ambientGroundColor = new Color(0.46f, 0.42f, 0.30f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.022f;
            RenderSettings.fogColor = new Color(0.42f, 0.38f, 0.24f);
            if (Object.FindFirstObjectByType<Volume>()) return;
            if (_profile == null)
            {
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                var tm = _profile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
                var bl = _profile.Add<Bloom>(true); bl.intensity.Override(0.5f); bl.threshold.Override(1.0f); bl.scatter.Override(0.7f);
                var ca = _profile.Add<ColorAdjustments>(true); ca.postExposure.Override(-0.1f); ca.saturation.Override(-4f); ca.contrast.Override(10f);
                var vg = _profile.Add<Vignette>(true); vg.intensity.Override(0.22f);
                var wb = _profile.Add<WhiteBalance>(true); wb.temperature.Override(8f);
            }
            var v = new GameObject("PostFX").AddComponent<Volume>();
            v.isGlobal = true; v.sharedProfile = _profile;
            v.transform.SetParent(res.root.transform);
        }

        static Camera MakeCam(int w, int h, out RenderTexture rt)
        {
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 70; cam.nearClipPlane = 0.03f; cam.farClipPlane = 80;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            return cam;
        }

        static void FailureShot(PlacementAudit.Issue iss, string path)
        {
            var cam = MakeCam(800, 600, out var rt);
            var t = iss.obj.transform;
            var size = iss.obj.profile.size;
            float d = Mathf.Max(1.0f, Mathf.Max(size.x, size.y, size.z) * 1.8f);
            var target = t.position + Vector3.up * size.y * 0.5f;
            cam.transform.position = target + new Vector3(d * 0.7f, d * 0.55f, d * 0.7f);
            cam.transform.LookAt(target);
            // red overlay of the object's colliders
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); mat.SetColor("_BaseColor", new Color(1, 0, 0));
            var overlay = new List<GameObject>();
            foreach (var bc in iss.obj.GetComponentsInChildren<BoxCollider>())
            {
                foreach (var e in Edges(bc))
                {
                    var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(g.GetComponent<Collider>());
                    g.GetComponent<Renderer>().sharedMaterial = mat;
                    g.transform.position = (e.a + e.b) * 0.5f; g.transform.rotation = Quaternion.LookRotation(e.b - e.a + Vector3.one * 1e-6f);
                    g.transform.localScale = new Vector3(0.004f, 0.004f, (e.b - e.a).magnitude);
                    overlay.Add(g);
                }
            }
            Thumbnails.Save(cam, rt, path);
            foreach (var g in overlay) Object.DestroyImmediate(g);
            Object.DestroyImmediate(cam.gameObject); rt.Release();
        }

        static IEnumerable<(Vector3 a, Vector3 b)> Edges(BoxCollider bc)
        {
            var h = bc.size * 0.5f; var c = bc.center; var t = bc.transform;
            Vector3 P(int i) => t.TransformPoint(c + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
            int[,] e = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            for (int i = 0; i < 12; i++) yield return (P(e[i, 0]), P(e[i, 1]));
        }

        public static void Overview(TestLevelBuilder.Result res, string prefix)
        {
            var cam = MakeCam(1280, 720, out var rt);
            foreach (var room in res.rooms)
            {
                var b = room.bounds;
                // two eye-level views from opposite corners + one high view
                var views = new[]
                {
                    (new Vector3(b.min.x + 0.5f, 1.6f, b.min.z + 0.5f), new Vector3(b.center.x, 0.8f, b.center.z)),
                    (new Vector3(b.max.x - 0.5f, 1.6f, b.max.z - 0.5f), new Vector3(b.center.x, 0.8f, b.center.z)),
                    (new Vector3(b.center.x - b.size.x * 0.3f, 2.55f, b.min.z + 0.3f), new Vector3(b.center.x, 0f, b.center.z + 0.5f)),
                };
                for (int i = 0; i < views.Length; i++)
                {
                    cam.transform.position = views[i].Item1; cam.transform.LookAt(views[i].Item2);
                    Thumbnails.Save(cam, rt, $"{prefix}_{room.name}_{i}.png");
                }
            }
            Object.DestroyImmediate(cam.gameObject); rt.Release();
        }
    }
}
