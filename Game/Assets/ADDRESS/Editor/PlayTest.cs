using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Address.Placement;
using Address.Player;
using Address.TestLevel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Pose = Address.Placement.Pose;

namespace Address.EditorTools
{
    /// <summary>
    /// Automated play-test: a scripted player walks the test level with the real controller and hands
    /// (pick up, gentle place, throw, push furniture), the world is simulated frame by frame, and frames are saved.
    /// batchmode: -executeMethod Address.EditorTools.PlayTest.Run [-seed 1]
    /// Output: Docs/reports/playtest/frame_###.png + events.txt
    /// </summary>
    public static class PlayTest
    {
        const string Out = "../Docs/reports/playtest";
        const float Dt = 1f / 30f;

        static FirstPersonController fpc; static GrabSystem grab; static ScriptedInput inp; static Camera cam; static RenderTexture rt;
        static TestLevelBuilder.Result level; static int frame, shot; static readonly StringBuilder log = new();

        public static void Run()
        {
            Directory.CreateDirectory(Out);
            foreach (var f in Directory.GetFiles(Out)) File.Delete(f);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var db = TestFarm.RefreshDatabase();
            db = AssetDatabase.LoadAssetAtPath<ProfileDatabase>(TestFarm.DbPath); db.Reindex();
            var builder = TestFarm.MakeBuilder();
            level = builder.Build(0x9E3779B97F4A7C15UL, db);
            TestFarm.Lighting(level);
            Physics.simulationMode = SimulationMode.Script;
            Physics.IgnoreLayerCollision(12, 11, true);

            // player rig
            var go = new GameObject("Player") { layer = 11 };
            go.transform.position = level.start + Vector3.up * 0.05f;
            go.AddComponent<CharacterController>();
            fpc = go.AddComponent<FirstPersonController>();
            inp = new ScriptedInput(); fpc.input = inp;
            var head = new GameObject("Head"); head.transform.SetParent(go.transform, false);
            fpc.head = head.transform;
            cam = head.AddComponent<Camera>(); cam.fieldOfView = 72; cam.nearClipPlane = 0.03f; cam.farClipPlane = 80;
            var data = head.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            rt = new RenderTexture(960, 540, 24); cam.targetTexture = rt;
            fpc.Init(); fpc.SetLook(90, 5);
            grab = go.AddComponent<GrabSystem>();
            grab.cam = cam; grab.fpc = fpc; grab.input = inp; grab.engine = level.engine;
            grab.ghostOk = Ghost(new Color(0.2f, 1f, 0.4f, 0.35f)); grab.ghostBad = Ghost(new Color(1f, 0.2f, 0.2f, 0.35f));
            grab.Init();

            var office = level.rooms[0];
            Log($"start {go.transform.position}, objects in level {level.engine.placed.Count}");

            // 1. look around while walking in
            for (int i = 0; i < 60; i++) { inp.move = new Vector2(0, 1); inp.look = new Vector2(Mathf.Sin(i * 0.1f) * 1.2f, 0); Step(i % 15 == 0); }
            inp.move = Vector2.zero;

            // 2. pick a small item from a desk and put it on the floor
            var small = Nearest(o => o.profile.massClass == MassClass.Small && o.supportParent != null && o.supportParent.profile.group == "desk", office);
            if (small != null)
            {
                Log($"target small: {small.name} on {small.supportParent.name}");
                WalkTo(small.transform.position, 0.9f);
                AimAt(small.transform.position + Vector3.up * small.profile.size.y * 0.5f, 20);
                Shot(); Press(() => inp.grab = true); Log("event: " + grab.LastEvent);
                for (int i = 0; i < 20; i++) { inp.look = new Vector2(3f, 0); Step(i % 5 == 0); }
                inp.look = Vector2.zero;
                AimAtDir(fpc.transform.eulerAngles.y, 50, 15);   // look at the floor ahead
                for (int i = 0; i < 6; i++) Step(i == 5);
                Press(() => inp.grab = true); Log("event: " + grab.LastEvent);
                for (int i = 0; i < 30; i++) Step(i % 10 == 0);
                if (small) Log($"after place: {small.name} at {small.transform.position} up-tilt {Vector3.Angle(small.transform.up, Vector3.up):F1}");
            }

            // 3. pick a chair and throw it
            var chair = Nearest(o => o.profile.group == "chair" && o.state == ObjectState.Normal, office);
            if (chair != null)
            {
                Log($"target chair: {chair.name}");
                WalkTo(chair.transform.position, 1.1f);
                AimAt(chair.transform.position + Vector3.up * 0.5f, 20);
                Press(() => inp.grab = true); Log("event: " + grab.LastEvent);
                for (int i = 0; i < 15; i++) { inp.look = new Vector2(-4f, -0.5f); Step(i % 5 == 0); }
                inp.look = Vector2.zero;
                Press(() => inp.thr = true); Log("event: " + grab.LastEvent);
                for (int i = 0; i < 60; i++) Step(i % 6 == 0);
                Log($"chair rests at {chair.transform.position}, tilt {Vector3.Angle(chair.transform.up, Vector3.up):F0}deg");
            }

            // 4. push a desk with things on it
            var desk = Nearest(o => o.profile.group == "desk" && level.engine.placed.Any(c => c && c.supportParent == o), office);
            if (desk != null)
            {
                var riders = level.engine.placed.Where(c => c && c.supportParent == desk).ToList();
                var before = desk.transform.position;
                Log($"target desk: {desk.name} with {riders.Count} items on it");
                // approach from the desk's free end (the chair occupies the front)
                var side = desk.transform.right * (desk.profile.size.x * 0.5f + 0.9f);
                var front = desk.transform.position + side;
                if (!level.nav.Roomy(level.nav.CellOf(front).x, level.nav.CellOf(front).y)) front = desk.transform.position - side;
                WalkTo(front, 0.25f);
                var toPlayer = (fpc.transform.position - desk.transform.position); toPlayer.y = 0;
                var aimPt = desk.transform.position + Vector3.Project(toPlayer, desk.transform.right).normalized * (desk.profile.size.x * 0.5f - 0.03f) + Vector3.up * 0.55f;
                AimAt(aimPt, 20);
                // step closer until the hands can actually reach the desk front
                for (int i = 0; i < 60 && grab.Aimed != desk; i++) { AimAt(aimPt, 1); inp.move = new Vector2(0, 0.6f); Step(false); }
                inp.move = Vector2.zero;
                Log($"aimed at {(grab.Aimed ? grab.Aimed.name : "nothing")}, player-desk distance {Vector3.Distance(fpc.transform.position, desk.transform.position):F2}");
                Press(() => inp.grab = true); Log("event: " + grab.LastEvent);
                for (int i = 0; i < 60; i++)
                {
                    // first push into it, then pull it back towards us (it may be against a wall on the far side)
                    inp.move = new Vector2(0, i < 25 ? 1 : -1); Step(i % 9 == 0);
                    if (i % 15 == 0) { var b = desk.GetComponent<Rigidbody>(); Log($"  push i={i} wish={fpc.WishVelocity} rb={(b ? b.linearVelocity.ToString() : "none")} kin={(b ? b.isKinematic : false)} pos={desk.transform.position} | {grab.DebugPush}"); }
                }
                inp.move = Vector2.zero;
                Press(() => inp.grab = true); Log("event: " + grab.LastEvent);
                for (int i = 0; i < 30; i++) Step(i % 15 == 0);
                Log($"desk moved {Vector3.Distance(before, desk.transform.position):F2} m");
                foreach (var r in riders) if (r)
                {
                    bool onDesk = Physics.RaycastAll(r.transform.position + Vector3.up * 0.03f, Vector3.down, 0.15f).Any(h => h.collider.GetComponentInParent<PlacedObject>() == desk);
                    Log($"  rider {r.name}: still on desk = {onDesk}, tilt {Vector3.Angle(r.transform.up, Vector3.up):F0}deg");
                }
            }

            // 5. walk to the next room and look around
            if (level.doors.Count > 0)
            {
                WalkTo(level.doors[0] + Vector3.right * 1.2f, 0.4f);
                for (int i = 0; i < 40; i++) { inp.look = new Vector2(2.2f, 0); Step(i % 8 == 0); }
            }
            File.WriteAllText($"{Out}/events.txt", log.ToString());
            Debug.Log("[ADDRESS] playtest done, frames " + shot + "\n" + log);
        }

        // ------------------------------------------------------------------ helpers

        static Material Ghost(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1); m.SetColor("_BaseColor", c); m.renderQueue = 3000;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
            return m;
        }

        static void Log(string s) { log.AppendLine($"[{frame:D4}] {s}"); }

        static void Step(bool capture = false)
        {
            fpc.Tick(Dt);
            grab.Tick(Dt);
            grab.PhysicsStep(Dt);
            Physics.Simulate(Dt);
            inp.ClearEdges();
            frame++;
            if (capture) Shot();
        }

        static void Shot() { Thumbnails.Save(cam, rt, $"{Out}/frame_{shot++:D3}.png"); }

        static void Press(System.Action set) { set(); Step(true); }

        static PlacedObject Nearest(System.Func<PlacedObject, bool> f, RoomSpec room)
        {
            var p = fpc.transform.position;
            return level.engine.placed.Where(o => o && f(o) && room.bounds.Contains(new Vector3(o.transform.position.x, room.bounds.center.y, o.transform.position.z)))
                .OrderBy(o => Vector3.Distance(o.transform.position, p)).FirstOrDefault();
        }

        static void AimAt(Vector3 target, int frames)
        {
            var eye = cam.transform.position;
            var d = target - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            AimAtDir(yaw, pitch, frames);
        }

        static void AimAtDir(float yaw, float pitch, int frames)
        {
            float y0 = fpc.transform.eulerAngles.y, p0 = cam.transform.localEulerAngles.x; if (p0 > 180) p0 -= 360;
            for (int i = 1; i <= frames; i++)
            {
                float t = Mathf.SmoothStep(0, 1, i / (float)frames);
                fpc.SetLook(Mathf.LerpAngle(y0, yaw, t), Mathf.Lerp(p0, pitch, t));
                Step(false);
            }
        }

        /// <summary>Walk along a grid path (NavGrid avoids furniture) until within 'stop' metres of the target.</summary>
        static void WalkTo(Vector3 target, float stop)
        {
            var nav = level.nav;
            var goal = nav.CellOf(target);
            // nearest free cell to the target
            Vector2Int best = goal; float bd = float.MaxValue;
            for (int dz = -6; dz <= 6; dz++) for (int dx = -6; dx <= 6; dx++)
            {
                var c = new Vector2Int(goal.x + dx, goal.y + dz);
                if (!nav.Roomy(c.x, c.y)) continue;
                float d = Vector3.Distance(nav.Center(c.x, c.y), new Vector3(target.x, 0, target.z));
                if (d < bd) { bd = d; best = c; }
            }
            var path = nav.FindPath(nav.CellOf(fpc.transform.position), best, false, true) ?? nav.FindPath(nav.CellOf(fpc.transform.position), best);
            if (path == null) { Log("no path to " + target); return; }
            int guard = 0;
            foreach (var c in path)
            {
                var wp = nav.Center(c.x, c.y);
                while (guard++ < 900)
                {
                    var pos = fpc.transform.position; var to = wp - pos; to.y = 0;
                    if (to.magnitude < 0.2f) break;
                    var goalFlat = target - pos; goalFlat.y = 0;
                    if (goalFlat.magnitude < stop) { inp.move = Vector2.zero; return; }
                    float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    fpc.SetLook(Mathf.MoveTowardsAngle(fpc.transform.eulerAngles.y, yaw, 12f), 4);
                    inp.move = new Vector2(0, 1);
                    Step(guard % 20 == 0);
                }
            }
            inp.move = Vector2.zero;
        }
    }
}
