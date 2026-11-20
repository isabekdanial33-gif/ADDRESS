using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Address.Core;
using Address.Placement;
using Address.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Address.EditorTools
{
    /// <summary>
    /// Farm for REAL generated levels (LevelBuilder): random Library addresses, independent audit, determinism check,
    /// overview shots from the start room. -executeMethod Address.EditorTools.LevelFarm.Run -levels 30 -overview 2
    /// </summary>
    public static class LevelFarm
    {
        const string Out = "../Docs/reports/levels";

        public static LevelKit Kit()
        {
            var tb = TestFarm.MakeBuilder();
            var db = AssetDatabase.LoadAssetAtPath<ProfileDatabase>(TestFarm.DbPath); db.Reindex();
            return new LevelKit
            {
                db = db, floorMat = tb.floorMat, wallMat = tb.wallMat, ceilingMat = tb.ceilingMat, lightMat = tb.lightMat,
                trofferPrefab = tb.trofferPrefab, baseboardPrefab = tb.baseboardPrefab, doorFramePrefab = tb.doorFramePrefab,
                exitDoorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ADDRESS/Art/Exits/exit_door_handleless.glb"),
                exitElevatorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ADDRESS/Art/Exits/exit_elevator.glb"),
                exitVentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ADDRESS/Art/Exits/exit_vent_hatch.glb"),
                voidMat = VoidMat(),
                poolTileMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Tiles107.mat"),
                basinMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/PR_Basin.mat"),
                waterMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Water.mat"),
                aeroFloorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/FA_Floor.mat"),
                exitPavilionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ADDRESS/Art/Exits/exit_aero_pavilion.glb"),
                aeroHousePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ADDRESS/Art/Exits/aero_house.glb"),
                aeroGrassMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Grass004.mat"),
                aeroWallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/FA_Wall.mat"),
                pgFloorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Asphalt031.mat"),
                pgWallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Concrete034.mat"),
                pgCeilMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Concrete047A.mat"),
                htFloorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Carpet015.mat"),
                htWallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Wallpaper002B.mat"),
                htCeilMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Plaster001.mat"),
                ofFloorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Carpet006.mat"),
                ofWallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/PaintedPlaster017.mat"),
                ofCeilMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/OfficeCeiling005.mat"),
                lfFloorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Carpet012.mat"),
                lfWallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Wallpaper001A.mat"),
                pdWallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Concrete031.mat"),
                pdFloorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Concrete047A.mat"),
                whFloorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Concrete031.mat"),
                whWallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Concrete034.mat"),
                pipeMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Metal032.mat"),
                library = AssetDatabase.FindAssets("t:Material", new[] { "Assets/ADDRESS/Generated/Materials" }).Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).Where(m => m).ToArray(),
                elWallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/MetalPlates006.mat"),
                cvWallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Rock030.mat"),
                cvFloorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Ground054.mat"),
                lbFloorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/WoodFloor041.mat"),
                laFloorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Tiles101.mat"),
                laWallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Plaster001.mat"),
                laCeilMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/OfficeCeiling001.mat"),
                glassMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Generated/Materials/Glass.mat"),
                caustics = Enumerable.Range(0, 16).Select(i => (Texture)AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/ADDRESS/Art/Textures/caustic_{i:00}.png")).Where(t => t).ToArray()
            };
        }

        public static Material VoidMat()
        {
            const string path = "Assets/ADDRESS/Generated/Materials/Void.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", new Color(0.005f, 0.005f, 0.006f)); AssetDatabase.CreateAsset(m, path); }
            return m;
        }

        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Run()
        {
            int levels = int.Parse(Arg("-levels", "20")), overview = int.Parse(Arg("-overview", "2"));
            LevelBuilder.SettleOnCommit = Arg("-nosettle", "0") != "1";
            Directory.CreateDirectory(Out); Directory.CreateDirectory($"{Out}/shots");
            foreach (var f in Directory.GetFiles($"{Out}/shots")) File.Delete(f);
            TestFarm.RefreshDatabase();
            var sb = new StringBuilder();
            var totals = new Dictionary<string, int>();
            int objects = 0, clean = 0, rooms = 0, exitsTotal = 0, exitsReach = 0, levelsWithForward = 0; double ms = 0;
            var lines = new List<string>();
            for (int i = 0; i < levels; i++)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var kit = Kit();
                var addr = new LevelAddress(Arg("-aesthetic", "BR"), Hashing.Combine(0xADD7E55UL, (ulong)i));
                var sw = Stopwatch.StartNew();
                var res = new LevelBuilder(kit).Build(addr);
                ms += sw.Elapsed.TotalMilliseconds;
                var audit = new PlacementAudit(LevelBuilder.ArchLayer, LevelBuilder.PropsLayer);
                audit.Run(res.engine.placed, res.nav, res.start);
                objects += audit.objects; rooms += res.rooms.Count;
                int fwd = res.exits.Count(e => e.role == ExitRole.Forward), fwdOk = res.exits.Count(e => e.role == ExitRole.Forward && e.reachable), allOk = res.exits.Count(e => e.reachable);
                exitsTotal += res.exits.Count; exitsReach += allOk; levelsWithForward += fwdOk > 0 ? 1 : 0;
                if (fwdOk == 0) totals["NoReachableForwardExit"] = totals.GetValueOrDefault("NoReachableForwardExit") + 1;
                foreach (var kv in audit.CountByKind()) totals[kv.Key] = totals.GetValueOrDefault(kv.Key) + kv.Value;
                if (audit.issues.Count == 0) clean++;
                // determinism: hash of all placed poses must be identical on a rebuild
                string h1 = PoseHash(res.engine.placed);
                if (res.rare != "") totals["Rare_" + res.rare] = totals.GetValueOrDefault("Rare_" + res.rare) + 1;
                if (res.attempt > 0) totals["RegeneratedAttempts"] = totals.GetValueOrDefault("RegeneratedAttempts") + res.attempt;
                lines.Add($"| {addr} | {res.rooms.Count} | {res.layout.openings.Count} | {audit.objects} | {audit.issues.Count} | {sw.Elapsed.TotalMilliseconds:F0} | {h1} |");
                foreach (var iss in audit.issues.Take(20)) lines.Add($"|  | issue | {iss} | | | | |");
                if (i < overview) { TestFarm.Lighting(new Address.TestLevel.TestLevelBuilder.Result { root = res.root }); LevelBuilder.ApplyAtmosphere(res.aesthetic); if (res.probe) res.probe.RenderProbe(); Shots(res, $"{Out}/shots/{res.aesthetic}{i}"); }
                string h1s = PoseHash(res.engine.placed, 0.02f); int c1 = res.engine.placed.Count;
                Object.DestroyImmediate(res.root);
                if (i == 0)
                {
                    var again = new LevelBuilder(Kit()).Build(addr);
                    string h2 = PoseHash(again.engine.placed);
                    Debug.Log($"[ADDRESS] determinism {addr}: exact {(h1 == h2 ? "IDENTICAL" : "DIFFERENT")}, structural(2cm) {(h1s == PoseHash(again.engine.placed, 0.02f) ? "IDENTICAL" : "DIFFERENT")}, counts {c1} vs {again.engine.placed.Count}");
                    sb.AppendLine($"Determinism check on {addr}: **{(h1 == h2 ? "identical" : "DIFFERENT")}**\n");
                    Object.DestroyImmediate(again.root);
                }
            }
            var head = new StringBuilder();
            head.AppendLine($"# Generated Backrooms levels — {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            head.AppendLine($"{levels} levels, {rooms} rooms, {objects} objects, clean levels {clean}/{levels}, avg build {ms / levels:F0} ms");
            head.AppendLine($"Issues: {(totals.Count == 0 ? "none" : string.Join(", ", totals.Select(k => k.Key + ":" + k.Value)))}\n");
            head.AppendLine($"Exits: {exitsTotal} placed, {exitsReach} reachable; levels with a reachable FORWARD exit: {levelsWithForward}/{levels}\n");
            head.Append(sb);
            head.AppendLine("| address | rooms | openings | objects | issues | ms | pose hash |\n|---|---|---|---|---|---|---|");
            foreach (var l in lines) head.AppendLine(l);
            File.WriteAllText($"{Out}/report.md", head.ToString());
            Debug.Log($"[ADDRESS] exits {exitsTotal}, reachable {exitsReach}, levels with reachable forward exit {levelsWithForward}/{levels}");
            Debug.Log($"[ADDRESS] levels done: {levels} levels, {rooms} rooms, {objects} objects, issues {totals.Values.Sum()} ({string.Join(", ", totals.Select(k => k.Key + ":" + k.Value))}), clean {clean}/{levels}, avg {ms / levels:F0} ms");
        }

        static string PoseHash(List<PlacedObject> placed, float q = 0.001f)
        {
            ulong h = 1469598103934665603UL;
            foreach (var p in placed)
            {
                if (!p) continue;
                var v = p.transform.position / q;
                var s = $"{p.profile.id}:{Mathf.RoundToInt(v.x)},{Mathf.RoundToInt(v.y)},{Mathf.RoundToInt(v.z)}:{Mathf.RoundToInt(p.transform.eulerAngles.y / (q * 1000f))}";
                h = Hashing.Combine(h, Hashing.Fnv64(s));
            }
            return h.ToString("X16");
        }

        static void Shots(LevelBuilder.Result res, string prefix)
        {
            var go = new GameObject("ShotCam"); var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 72; cam.nearClipPlane = 0.03f; cam.farClipPlane = 80;
            var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true;
            var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt;
            // four directions from the start, then from three furnished rooms
            for (int d = 0; d < 4; d++)
            {
                go.transform.SetPositionAndRotation(res.start + Vector3.up * 1.62f, Quaternion.Euler(4, d * 90, 0));
                Thumbnails.Save(cam, rt, $"{prefix}_start_{d}.png");
            }
            int n = 0;
            foreach (var room in res.layout.rooms.Where(r => r.kind != "empty" && r.kind != "corridor").Take(3))
            {
                var b = room.spec.bounds;
                go.transform.position = new Vector3(b.min.x + 0.45f, 1.62f, b.min.z + 0.45f);
                go.transform.LookAt(new Vector3(b.center.x, 0.7f, b.center.z));
                Thumbnails.Save(cam, rt, $"{prefix}_room{n++}_{room.kind}.png");
            }
            foreach (var e in res.exits)
            {
                // as the player would first notice it: from 3 m away, eye height, slightly off-axis
                var eye = e.position + e.normal * 3.0f + Vector3.Cross(Vector3.up, e.normal) * 0.8f + Vector3.up * 1.62f;
                go.transform.position = eye; go.transform.LookAt(e.position + Vector3.up * (e.form == ExitForm.VentHatch ? 0.25f : 1.1f));
                Thumbnails.Save(cam, rt, $"{prefix}_exit{e.index}_{e.form}_{e.role}_closed.png");
                e.instance.Activate();
                Thumbnails.Save(cam, rt, $"{prefix}_exit{e.index}_{e.form}_{e.role}_open.png");
            }
            Object.DestroyImmediate(go); rt.Release();
        }
    }
}
