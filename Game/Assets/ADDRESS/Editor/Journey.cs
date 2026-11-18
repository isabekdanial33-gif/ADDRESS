using System.IO;
using System.Linq;
using System.Text;
using Address.Core;
using Address.Placement;
using Address.Player;
using Address.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Address.EditorTools
{
    /// <summary>
    /// End-to-end play-test of the core loop with a bot: spawn -> walk to an exit -> open it (E) -> step in ->
    /// next address (forward) or Echo g+1 (loop). Uses the same LevelBuilder / Echo / AddressBook / ExitTrigger as the game.
    /// Output: Docs/reports/journey/*.png + journey.txt
    /// </summary>
    public static class Journey
    {
        const string Out = "../Docs/reports/journey";
        const float Dt = 1f / 30f;
        static FirstPersonController fpc; static ScriptedInput inp; static Camera cam; static RenderTexture rt;
        static LevelBuilder.Result level; static int shot; static readonly StringBuilder log = new();

        public static void Run()
        {
            Directory.CreateDirectory(Out);
            foreach (var f in Directory.GetFiles(Out)) File.Delete(f);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TestFarm.RefreshDatabase();
            Physics.simulationMode = SimulationMode.Script;
            var book = new AddressBook();
            var go = new GameObject("Player") { layer = 11 };
            go.AddComponent<CharacterController>();
            fpc = go.AddComponent<FirstPersonController>(); inp = new ScriptedInput(); fpc.input = inp;
            var head = new GameObject("Head"); head.transform.SetParent(go.transform, false); fpc.head = head.transform;
            cam = head.AddComponent<Camera>(); cam.fieldOfView = 72; cam.nearClipPlane = 0.03f; cam.farClipPlane = 120;
            head.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            rt = new RenderTexture(960, 540, 24); cam.targetTexture = rt;
            fpc.Init();

            var addr = book.NextFree("BR", Hashing.Fnv64("the first room"));
            LevelAddress? from = null;
            string[] plan = { "loop", "loop", "forward", "forward", "forward", "forward" };
            for (int hop = 0; hop < plan.Length; hop++)
            {
                var kit = LevelFarm.Kit();
                level = new LevelBuilder(kit).Build(addr);
                var echo = Echo.Apply(level, kit.wallMat);
                TestFarm.Lighting(new Address.TestLevel.TestLevelBuilder.Result { root = level.root });
                book.Visit(addr);
                if (from.HasValue && addr.Generation > 0) book.LinkEcho(from.Value, addr);
                Teleport(level.start, level.startYaw);
                Log($"HOP {hop}: entered {addr} rooms {level.rooms.Count} objects {level.engine.placed.Count} exits [{string.Join(", ", level.exits.Select(e => $"{e.index}:{e.form}/{e.role}{(e.reachable ? "" : "!")}"))}] echo [{string.Join("; ", echo)}]");
                for (int i = 0; i < 4; i++) { fpc.SetLook(level.startYaw + i * 90, 3); Step(); Shot($"hop{hop}_start{i}"); }

                var want = plan[hop] == "loop" ? ExitRole.Loop : ExitRole.Forward;
                var exit = level.exits.Where(e => e.role == want && e.reachable).OrderBy(e => e.index).FirstOrDefault()
                           ?? level.exits.First(e => e.role == ExitRole.Forward && e.reachable);
                var exRoom = level.layout.rooms[exit.roomId];
                Log($"  going to exit {exit.index} {exit.form}/{exit.role} at {exit.position} normal {exit.normal} room {exRoom.id} {exRoom.kind} bounds {exRoom.spec.bounds.min:F2}-{exRoom.spec.bounds.max:F2} approachCell {exit.approachCell} -> {level.nav.Center(exit.approachCell.x, exit.approachCell.y):F2}");
                bool arrived = WalkTo(exit.Approach, 0.35f);
                Log($"  arrived={arrived} dist={Vector3.Distance(Flat(fpc.transform.position), Flat(exit.Approach)):F2}");
                LookAt(exit.position + Vector3.up * (exit.form == ExitForm.VentHatch ? 0.25f : 1.2f));
                Log($"  player at {fpc.transform.position:F2}, cam {cam.transform.position:F2} fwd {cam.transform.forward:F2}");
                Shot($"hop{hop}_exit_closed");
                // aim + press E (raycast like the game does)
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                bool hitExit = Physics.Raycast(ray, out var hit, 2.2f, 1 << LevelBuilder.ArchLayer) && hit.collider.GetComponentInParent<ExitTrigger>() == exit.instance;
                Log($"  aimed at exit: {hitExit} (hit {(hit.collider ? hit.collider.name + " parent " + hit.collider.transform.root.name : "nothing")} at {hit.distance:F2})");
                exit.instance.Activate();
                Step(); Shot($"hop{hop}_exit_open");
                if (exit.form == ExitForm.VentHatch) inp.crouch = true;
                bool entered = false;
                for (int i = 0; i < 120 && !entered; i++)
                {
                    LookAt(exit.position + Vector3.up * 0.8f, true);
                    inp.move = new Vector2(0, 0.7f); Step();
                    entered = exit.instance.Contains(fpc.transform.position + Vector3.up * 0.5f);
                }
                inp.move = Vector2.zero; inp.crouch = false;
                Shot($"hop{hop}_inside");
                Log($"  stepped into exit: {entered}");
                if (!entered) break;
                var cur = level.address; from = cur;
                addr = exit.role == ExitRole.Loop ? cur.NextEcho() : book.NextFree("BR", Hashing.Combine(cur.Seed, (ulong)(exit.index + 1) * 0x51ED27UL));
                Object.DestroyImmediate(level.root);
            }
            Log($"visited: {string.Join(" | ", book.visited)}");
            Log($"echo links: {string.Join(" | ", book.echoLinks)}");
            File.WriteAllText($"{Out}/journey.txt", log.ToString());
            Debug.Log("[ADDRESS] journey done\n" + log);
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0, v.z);
        static void Log(string s) => log.AppendLine(s);
        static void Step() { fpc.Tick(Dt); Physics.Simulate(Dt); inp.ClearEdges(); }
        static void Shot(string n) => Thumbnails.Save(cam, rt, $"{Out}/{shot++:D2}_{n}.png");

        static void Teleport(Vector3 p, float yaw)
        {
            var cc = fpc.GetComponent<CharacterController>(); cc.enabled = false;
            fpc.transform.position = p + Vector3.up * 0.05f; cc.enabled = true; fpc.SetLook(yaw, 0);
            Physics.SyncTransforms();
        }

        static void LookAt(Vector3 target, bool keepPitch = false)
        {
            var d = target - cam.transform.position;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = keepPitch ? 10 : -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            fpc.SetLook(yaw, pitch);
        }

        /// <summary>Straight line on the nav grid where every sample cell is free with a one-cell margin (or is the last cells).</summary>
        static bool GridLine(NavGrid nav, Vector3 a, Vector3 b)
        {
            var d = Flat(b - a); float len = d.magnitude; if (len < 0.01f) return true;
            for (float t = 0; t <= len; t += 0.1f)
            {
                var c = nav.CellOf(a + d * (t / len));
                if (!nav.Free(c.x, c.y)) return false;
                if (len - t > 0.5f && t > 0.3f && !nav.Roomy(c.x, c.y)) return false;
            }
            return true;
        }

        /// <summary>Path following with string-pulling: steer to the furthest path point the capsule can reach in a straight line.</summary>
        static bool WalkTo(Vector3 target, float stop)
        {
            var nav = level.nav;
            var path = nav.FindPath(nav.CellOf(fpc.transform.position), nav.CellOf(target), false, true) ?? nav.FindPath(nav.CellOf(fpc.transform.position), nav.CellOf(target));
            if (path == null) { Log("  NO PATH"); return false; }
            var pts = path.Select(c => nav.Center(c.x, c.y)).ToList(); pts.Add(Flat(target));
            int mask = (1 << LevelBuilder.ArchLayer) | (1 << LevelBuilder.PropsLayer);
            int idx = 0, frames = 0, n = 0, stuck = 0; Vector3 lastPos = fpc.transform.position;
            while (frames < 3000)
            {
                var pos = fpc.transform.position;
                if (Flat(target - pos).magnitude < stop) { inp.move = Vector2.zero; return true; }
                // furthest visible point ahead
                for (int j = Mathf.Min(pts.Count - 1, idx + 12); j > idx; j--)
                    if (GridLine(nav, pos, pts[j])) { idx = j; break; }
                while (idx < pts.Count - 1 && Flat(pts[idx] - pos).magnitude < 0.25f) idx++;
                var to = Flat(pts[idx] - pos);
                fpc.SetLook(Mathf.MoveTowardsAngle(fpc.transform.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 20f), 3);
                var before = fpc.transform.position;
                inp.move = new Vector2(0, 1); Step(); frames++;
                if (fpc.transform.position.y < -0.3f && before.y >= -0.3f)
                {
                    var under = Physics.RaycastAll(before + Vector3.up * 0.5f, Vector3.down, 3f).Select(h => h.collider.name + "@" + h.point.y.ToString("F2")).ToArray();
                    var near = Physics.OverlapSphere(before + Vector3.up * 0.3f, 0.6f).Select(c => (c.GetComponentInParent<PlacedObject>() ? c.GetComponentInParent<PlacedObject>().name + "[" + c.GetComponentInParent<PlacedObject>().placedBy + "]" : c.name) + "@" + c.bounds.ToString("F2")).Distinct().ToArray();
                    Log($"  FELL at frame {frames}: from {before:F2} to {fpc.transform.position:F2}; below [{string.Join(", ", under)}]; near [{string.Join(", ", near)}]");
                    Shot("fall");
                }
                if (frames % 150 == 0) Shot($"walk{n++}");
                if (frames % 30 == 0) { stuck = (fpc.transform.position - lastPos).magnitude < 0.1f ? stuck + 1 : 0; lastPos = fpc.transform.position; if (stuck > 3)
                {
                    var cs = Physics.OverlapSphere(pos + Vector3.up * 0.9f, 0.45f, mask).Select(c => c.name + "@" + c.bounds.center.ToString("F2")).ToArray();
                    var cc = nav.CellOf(pos);
                    Log($"  stuck at {pos} cell {cc} free={nav.Free(cc.x, cc.y)} next wp {pts[idx]} (idx {idx}/{pts.Count}) touching [{string.Join(", ", cs)}]");
                    break;
                } }
            }
            inp.move = Vector2.zero;
            return Flat(target - fpc.transform.position).magnitude < stop + 0.4f;
        }
    }
}
