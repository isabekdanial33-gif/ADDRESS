using System.Collections;
using System.IO;
using System.Linq;
using Address.Core;
using Address.Placement;
using Address.Player;
using Address.World;
using UnityEngine;

namespace Address.Game
{
    /// <summary>"-verify": targeted checks of the things the user reported, with screenshots (Build/shots/v_*.png).</summary>
    public partial class GameRoot
    {
        IEnumerator Verify()
        {
            yield return new WaitUntil(() => !travelling && level != null);
            string dir = Path.Combine(Application.dataPath, "..", "shots"); Directory.CreateDirectory(dir);
            var rtx = new RenderTexture(1280, 720, 24); var tx = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            void Snap(string n)
            {
                OverlayToCamera();
                foreach (var oc in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (oc != cam && oc.targetTexture && oc.enabled) oc.Render();
                var prev = cam.targetTexture; cam.targetTexture = rtx; cam.Render(); cam.targetTexture = prev;
                RenderTexture.active = rtx; tx.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tx.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, "v_" + n + ".png"), tx.EncodeToPNG());
            }
            void Log(string s) => Debug.Log("[ADDRESS] verify: " + s);
            si = new ScriptedInput(); fpc.input = si; grab.input = si;
            void Tp(Vector3 p) { var cc = player.GetComponent<CharacterController>(); cc.enabled = false; player.transform.position = p; cc.enabled = true; }
            void LookAt(Vector3 p) { var d = p - cam.transform.position; fpc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1, 1)) * Mathf.Rad2Deg); }

            Play(new LevelAddress("BR", Hashing.Fnv64("verify")));
            yield return new WaitForSeconds(0.5f); yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(0.5f);

            // 1) sitting: every kind of seat found, face away from its backrest
            int sat = 0;
            foreach (var s in FindObjectsByType<Address.Interact.Seat>(FindObjectsSortMode.None).Where(x => x.Usable).GroupBy(x => x.po.profile.group).Select(g => g.First()).Take(4))
            {
                Tp(s.SeatPoint + s.Facing() * 1.2f - Vector3.up * (s.SeatPoint.y - s.po.transform.position.y)); yield return null;
                SitOn(s); yield return new WaitForSeconds(0.4f);
                Snap($"sit_{s.po.profile.group}_{sat}"); Log($"sit {s.po.name} facing ok");
                fpc.SetLook(fpc.transform.eulerAngles.y, 60); yield return new WaitForSeconds(0.2f); Snap($"sit_{s.po.profile.group}_{sat}_down");
                StandUp(); yield return new WaitForSeconds(0.2f); sat++;
            }

            // 2) holding a small and a medium object
            foreach (var mc in new[] { MassClass.Small, MassClass.Medium })
            {
                var o = level.engine.placed.Where(x => x && x.gameObject.activeInHierarchy && x.profile.massClass == mc && x.profile.support == SupportType.Floor && x.supportParent == null).FirstOrDefault();
                if (!o) { Log("no object " + mc); continue; }
                var c = o.GetComponentInChildren<Renderer>().bounds.center; var fl = c - o.transform.forward * 1.0f; Tp(new Vector3(fl.x, 0.02f, fl.z)); yield return null;
                LookAt(c); yield return null; yield return null;
                si.grab = true; yield return null; si.grab = false; yield return new WaitForSeconds(1.0f);
                fpc.SetLook(fpc.transform.eulerAngles.y, 10); yield return new WaitForSeconds(0.5f);
                Snap("hold_" + mc); Log($"hold {o.name}: held={(grab.Held ? grab.Held.name : "no")}");
                if (grab.Held) { si.grab = true; yield return null; si.grab = false; yield return new WaitForSeconds(0.3f); if (grab.Held) { si.thr = true; yield return null; si.thr = false; } }
                yield return new WaitForSeconds(0.5f);
            }

            // 3) pushing heavy furniture: both palms on it
            var heavy = level.engine.placed.Where(x => x && x.profile.massClass == MassClass.Heavy && x.profile.support == SupportType.Floor).FirstOrDefault();
            if (heavy)
            {
                var b = heavy.GetComponentInChildren<Renderer>().bounds; var fwd = heavy.transform.forward; var p = b.center + fwd * (Mathf.Max(b.extents.x, b.extents.z) + 0.75f);
                Tp(new Vector3(p.x, 0.02f, p.z)); yield return null; LookAt(b.center); yield return null; yield return null;
                si.grab = true; yield return null; si.grab = false; yield return new WaitForSeconds(0.8f);
                fpc.SetLook(fpc.transform.eulerAngles.y, 15); yield return new WaitForSeconds(0.3f);
                Snap("push"); Log($"push {heavy.name}: pushing={(grab.Pushing ? "yes" : "no")}");
                if (grab.Pushing) { si.grab = true; yield return null; si.grab = false; }
                yield return new WaitForSeconds(0.3f);
            }

            // 4) a whiteboard close up
            var board = level.engine.placed.FirstOrDefault(x => x && x.profile.group == "board");
            if (board) { var bp = board.transform.position + board.transform.forward * 1.6f; Tp(new Vector3(bp.x, 0.02f, bp.z)); yield return null; LookAt(board.GetComponentInChildren<Renderer>().bounds.center); yield return null; yield return null; Snap("board"); Log("board shot"); }
            else Log("no board in this level");

            // 5) the computer: browser starting text
            var fake = new Address.Interact.ComputerStation { laptop = true, seed = 7 };
            UseComputer(fake); yield return new WaitForSeconds(4f); Snap("pc_browser_start");
            yield return new WaitForSeconds(6f); Snap("pc_browser_10s");
            computer?.Close(); yield return new WaitForSeconds(0.5f);
            Snap("after_pc"); Log("computer closed, screen ok");

            // 6) Frutiger Aero: walk 250 m straight with frame timing, then into a house pavilion
            StartCoroutine(Enter(new LevelAddress("FA", Hashing.Fnv64("verify fa")), level.address));
            yield return new WaitForSeconds(0.5f); yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(0.5f);
            Time.captureFramerate = 0;
            var aw = level.root.GetComponentInChildren<AeroWorld>();
            float worst = 0; int frames = 0; var t0 = Time.realtimeSinceStartup; int shot = 0;
            si.run = true; si.move = new Vector2(0, 1); fpc.SetLook(level.startYaw, 3);
            while (Vector3.Distance(player.transform.position, level.start) < 250f && Time.realtimeSinceStartup - t0 < 120f)
            {
                worst = Mathf.Max(worst, Time.unscaledDeltaTime); frames++;
                float dd = Vector3.Distance(player.transform.position, level.start);
                if (dd > (shot + 1) * 55f && shot < 4) { Snap($"fa_walk_{shot}"); Log($"walk {dd:F0} m at {Time.realtimeSinceStartup - t0:F1}s y {player.transform.position.y:F1}"); shot++; }
                if (Time.frameCount % 600 == 0) { var v = new Vector2(fpc.Velocity.x, fpc.Velocity.z).magnitude; if (v < 0.5f) { fpc.SetLook(fpc.transform.eulerAngles.y + 35f, 3); Log($"blocked at {player.transform.position}, turning"); } }
                yield return null;
            }
            si.move = Vector2.zero; si.run = false;
            float secs = Time.realtimeSinceStartup - t0;
            Log($"FA walk: {Vector3.Distance(player.transform.position, level.start):F0} m in {secs:F1}s, avg {frames / secs:F0} fps, worst frame {worst * 1000:F0} ms, chunks {aw.transform.childCount}, y {player.transform.position.y:F2}");
            Snap("fa_after_walk");
            var ex = level.exits.Where(e => e.instance).OrderBy(e => (e.position - player.transform.position).sqrMagnitude).FirstOrDefault();
            if (ex != null)
            {
                var ap = ex.position + ex.normal * 4.4f; Tp(ap + Vector3.up * 0.05f); yield return null;
                LookAt(ex.position + Vector3.up * 0.8f); yield return null; yield return null; Snap("fa_house_inside");
                var from = level.address;
                si.move = new Vector2(0, 1); float tt = 0;
                float logT = 0;
                while (level.address.Equals(from) && tt < 8f)
                {
                    tt += Time.deltaTime; logT += Time.deltaTime; if (tt > 0.6f && tt < 0.7f) Snap("fa_entering");
                    if (logT > 1f) { logT = 0; var lp = ex.instance.transform.InverseTransformPoint(fpc.transform.position + Vector3.up * 0.5f); var hits = Physics.CapsuleCastAll(fpc.transform.position + Vector3.up * 0.35f, fpc.transform.position + Vector3.up * 1.4f, 0.27f, fpc.transform.forward, 0.6f, ~(1 << 11), QueryTriggerInteraction.Ignore);
                        Log($"blockers: {string.Join(",", hits.Select(h => h.collider.name + "@" + h.distance.ToString("F2")))} vel {fpc.Velocity} wish {fpc.WishVelocity} grounded {fpc.Grounded} mult {fpc.speedMultiplier}");
                        Log($"in pavilion local {lp} open {ex.instance.IsOpen} hidden {ex.instance.Hidden} travelling {travelling} state {state} exitsInList {level.exits.Contains(ex)}"); }
                    yield return null;
                }
                si.move = Vector2.zero;
                yield return new WaitForSeconds(0.25f); Snap("fa_transition");
                yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(0.3f); Snap("fa_arrived");
                Log($"FA exit: {from} -> {level.address}");
            }
            Log("done");
            Application.Quit();
        }
    }
}
