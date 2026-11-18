using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Address.Core;
using Address.Placement;
using Address.Player;
using Address.World;
using UnityEngine;

namespace Address.Game
{
    /// <summary>
    /// "-playvideo": the game plays itself like a person would (menu, walking, looking around, picking up and
    /// throwing things, jumping, crouching, a photo, opening an exit, travelling, meeting an entity, being caught)
    /// and records every frame (30 fps game time) to Build/video_frames/*.jpg for review.
    /// Runs in batchmode with the GPU and no window; audio is muted for test runs.
    /// </summary>
    public partial class GameRoot
    {
        ScriptedInput si; RenderTexture vrt; Texture2D vtex; string vdir; int vframe; bool recording;
        string caption = "";

        IEnumerator PlayVideo()
        {
            vdir = Path.Combine(Application.dataPath, "..", "video_frames");
            if (Directory.Exists(vdir)) Directory.Delete(vdir, true);
            Directory.CreateDirectory(vdir);
            Time.captureFramerate = 30;
            vrt = new RenderTexture(1280, 720, 24); vtex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            yield return new WaitUntil(() => !travelling && level != null);
            yield return null;
            OverlayToCamera();
            StartCoroutine(Recorder());

            // ---- menu
            Log("menu"); yield return Wait(3.5f);
            typeof(MenuUI).GetMethod("SettingsPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(menu, null);
            yield return Wait(2f);
            typeof(MenuUI).GetMethod("Main", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(menu, null);
            yield return Wait(1f);
            Play(new LevelAddress("BR", Hashing.Fnv64("video run")));
            si = new ScriptedInput(); fpc.input = si; grab.input = si;
            yield return Wait(0.5f); yield return new WaitUntil(() => !travelling);
            Log("entered " + level.address);
            yield return LookAround(4f);

            // ---- pick something up, carry it, throw it
            var obj = NearestGrabbable(14f);
            if (obj)
            {
                Log("going to pick up " + obj.name);
                yield return WalkTo(obj.transform.position, 1.1f, false, 20f);
                yield return AimAt(Center(obj), 0.6f);
                Log($"aimed at grab: {(grab.Aimed ? grab.Aimed.name : "nothing")}");
                Press(() => si.grab = true); yield return Wait(0.6f);
                Log("held: " + (grab.Held ? grab.Held.name : "nothing (" + grab.LastEvent + ")"));
                yield return AimPitch(8f, 0.5f);
                si.move = new Vector2(0, 1); yield return Wait(1.5f); si.move = Vector2.zero;
                yield return Wait(0.8f);
                Press(() => si.thr = true); Log("throw"); yield return Wait(2f);
            }
            // ---- jump, crouch, photo
            si.move = new Vector2(0, 1); yield return Wait(0.6f);
            Press(() => si.jump = true); Log("jump"); yield return Wait(1.0f);
            Press(() => si.jump = true); yield return Wait(1.0f);
            si.move = Vector2.zero; si.crouch = true; Log("crouch"); yield return Wait(1.6f); si.crouch = false; yield return Wait(0.8f);
            yield return LookAround(2.5f);
            TakePhoto(); Log("photo"); yield return Wait(2f);

            // ---- things to use: light switch, a chair, a shelf, a computer
            yield return UseNearest<Address.Interact.LightSwitch>("light switch", 30f, c => true, 1.2f);
            if (lastUsed) { yield return LookAround(1.5f); botE = true; yield return Wait(0.6f); }
            yield return UseNearest<Address.Interact.Seat>("seat", 30f, c => c.Usable, 1.0f);
            if (fpc.Seated) { yield return LookAround(2.5f); yield return AimPitch(60f, 0.8f); yield return Wait(0.8f); botE = true; yield return Wait(0.6f); }
            yield return UseNearest<Address.Interact.Container>("shelf", 30f, c => !c.searched, 1.0f);
            yield return Wait(1.6f);
            yield return UseNearest<Address.Interact.Device>("computer", 45f, d => d.kind == Address.Interact.DeviceKind.Monitor && d.station != null, 0.9f);
            if (lastUsed is Address.Interact.Device dev && computer == null && dev.station != null && !dev.station.On)
            {
                var hub = dev.station.hub; yield return AimAt(hub.transform.position + Vector3.up * 0.3f, 0.6f); botE = true; yield return Wait(0.8f);
                yield return AimAt(dev.transform.position + Vector3.up * 0.25f, 0.6f); botE = true; yield return Wait(0.5f);
            }
            if (computer != null)
            {
                Log("at the computer"); yield return Wait(5f);
                var rb = FindFirstObjectByType<Address.Interact.RealBrowser>();
                if (rb) { yield return Wait(3f); rb.Go("ru.wikipedia.org"); Log("browser -> wikipedia 2007"); yield return Wait(10f); }
                else yield return Wait(4f);
                computer?.Close(); Log("left the computer"); yield return Wait(1f);
            }

            // ---- find a way out
            var exit = level.exits.Where(e => e.reachable && e.instance && e.role == ExitRole.Forward)
                                  .OrderBy(e => (e.position - player.transform.position).sqrMagnitude).FirstOrDefault();
            if (exit != null)
            {
                Log($"walking to exit {exit.form}");
                yield return WalkTo(exit.position + exit.normal * 1.3f, 0.5f, true, 60f);
                yield return AimAt(exit.position + Vector3.up * 1.2f, 0.8f);
                if (!exit.instance.IsOpen) { exit.instance.Activate(); Log("open exit"); }
                yield return Wait(1.6f);
                var from = level.address; float t = 0;
                si.move = new Vector2(0, 1);
                while (level.address.Equals(from) && t < 8f) { t += Time.deltaTime; var d = exit.position - exit.normal * 0.6f - player.transform.position; SteerTowards(d); yield return null; }
                si.move = Vector2.zero;
                yield return new WaitUntil(() => !travelling);
                Log("travelled " + from + " -> " + level.address);
                yield return LookAround(3f);
            }

            // ---- an entity
            foreach (var e in entities) if (e) Destroy(e.gameObject);
            entities.Clear(); forceEntity = true; SpawnEntities(); forceEntity = false;
            var en = entities.FirstOrDefault();
            if (en)
            {
                var spot = VisibleSpot(7f, 11f);
                en.transform.position = spot; en.transform.rotation = Quaternion.LookRotation(Flat(player.transform.position - spot));
                en.wakeDelay = 0;
                yield return AimAt(spot + Vector3.up * 1.6f, 1.0f);
                Log($"entity seen at {Vector3.Distance(spot, player.transform.position):F1} m (mode {en.mode})");
                yield return Wait(3f);
                si.move = new Vector2(0, -0.7f); Log("backing away while watching"); yield return Wait(2.5f); si.move = Vector2.zero;
                Log($"turn away (dist {Dist(en):F1})");
                yield return Turn(180f, 0.6f); yield return Wait(1.5f);
                yield return Turn(180f, 0.5f); Log($"look back (dist {Dist(en):F1}, mode {en.mode})"); yield return Wait(2f);
                yield return Turn(180f, 0.6f); Log("turn away again");
                var from = level.address; float t = 0;
                while (level.address.Equals(from) && t < 15f) { t += Time.deltaTime; yield return null; }
                yield return new WaitUntil(() => !travelling);
                Log("after catch: " + level.address);
                yield return LookAround(4f);
            }
            Log("done");
            recording = false; yield return null;
            Debug.Log($"[ADDRESS] video: {vframe} frames -> {vdir}");
            Application.Quit();
        }

        Component lastUsed;
        IEnumerator UseNearest<T>(string what, float maxDist, System.Func<T, bool> ok, float stand) where T : Address.Interact.Interactable
        {
            lastUsed = null;
            var cands = FindObjectsByType<T>(FindObjectsSortMode.None).Where(c => c && ok(c) && Flat(c.transform.position - player.transform.position).magnitude < maxDist)
                                    .OrderBy(c => (c.transform.position - player.transform.position).sqrMagnitude).ToList();
            if (cands.Count == 0) { Log("no " + what + " nearby"); yield break; }
            var t = cands[0]; Log("going to " + what + " " + t.name);
            var rs = t.GetComponentsInChildren<Renderer>(); var b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.transform.position, Vector3.one * 0.3f); foreach (var r in rs) b.Encapsulate(r.bounds);
            var front = t.transform.forward; front.y = 0; if (front.sqrMagnitude < 0.01f) front = Vector3.forward; front.Normalize();
            var stand0 = new Vector3(b.center.x, player.transform.position.y, b.center.z) + front * (Mathf.Max(b.extents.x, b.extents.z) + stand);
            yield return WalkTo(stand0, 0.35f, false, 25f);
            yield return AimAt(b.center, 0.7f);
            var verb = t.Verb(this); Log($"{what}: E -> '{verb}'");
            if (verb != null) { botE = true; lastUsed = t; }
            yield return Wait(0.8f);
        }

        /// <summary>"-opentest": the openable props in a row, closed and opened.</summary>
        IEnumerator OpenTest()
        {
            yield return new WaitUntil(() => !travelling && level != null);
            menu.Show(false, false); state = State.Playing; hud.SetVisible(false);
            string dir = Path.Combine(Application.dataPath, "..", "shots"); Directory.CreateDirectory(dir);
            var rtx = new RenderTexture(1600, 900, 24); var tx = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            void Snap(string n) { var prev = cam.targetTexture; cam.targetTexture = rtx; cam.Render(); cam.targetTexture = prev; RenderTexture.active = rtx; tx.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tx.Apply(); RenderTexture.active = null; File.WriteAllBytes(Path.Combine(dir, n + ".png"), tx.EncodeToPNG()); }
            var fwd = Quaternion.Euler(0, level.startYaw, 0) * Vector3.forward; var right = Vector3.Cross(Vector3.up, fwd);
            var ids = new[] { "open_box", "storage_tub", "trash_bag_tied", "file_cabinet" };
            var objs = new List<Address.Interact.Container>();
            for (int i = 0; i < ids.Length; i++)
            {
                var p = config.kit.db.Get(ids[i]); if (!p) { Debug.Log("[ADDRESS] opentest missing " + ids[i]); continue; }
                var pos = player.transform.position + fwd * 2.6f + right * (i - 1.5f) * 0.85f;
                var go = Instantiate(p.prefab, pos, Quaternion.LookRotation(-fwd), level.root.transform);
                var po = go.GetComponent<PlacedObject>() ?? go.AddComponent<PlacedObject>(); po.profile = p; po.uid = 777 + i;
                var c = Address.Interact.Container.TryAttach(po, 1); if (c) objs.Add(c);
                Debug.Log($"[ADDRESS] opentest {ids[i]} parts {(c && c.open ? c.open.HasParts : false)} verb {(c ? c.Verb(this) : "-")}");
            }
            fpc.SetLook(level.startYaw, 22); yield return new WaitForSeconds(0.3f); Snap("o0_closed");
            foreach (var c in objs) StartCoroutine(c.open.Open());
            yield return new WaitForSeconds(1.2f); Snap("o1_open");
            Application.Quit();
        }

        /// <summary>"-computertest": the computer screen with everything plugged in, the desktop, the real browser.</summary>
        IEnumerator ComputerTest()
        {
            yield return new WaitUntil(() => !travelling && level != null);
            string dir = Path.Combine(Application.dataPath, "..", "shots"); Directory.CreateDirectory(dir);
            var rt0 = new RenderTexture(1600, 900, 24); var tx = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            void Snap(string n)
            {
                OverlayToCamera();
                foreach (var oc in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (oc != cam && oc.targetTexture && oc.enabled) oc.Render();
                var prev = cam.targetTexture; cam.targetTexture = rt0; cam.Render(); cam.targetTexture = prev;
                RenderTexture.active = rt0; tx.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tx.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, n + ".png"), tx.EncodeToPNG());
            }
            menu.Show(false, false); state = State.Playing;
            var fake = new Address.Interact.ComputerStation { laptop = true, seed = 12345 };
            UseComputer(fake);
            yield return new WaitForSeconds(0.6f); Snap("c0_boot");
            yield return new WaitForSeconds(2.5f); Snap("c1_desktop");
            {
                var crtImg = FindObjectsByType<UnityEngine.UI.RawImage>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == "crt");
                var srt = crtImg ? crtImg.texture as RenderTexture : null;
                if (srt)
                {
                    var uiCam = FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(x => x.targetTexture == srt);
                    Debug.Log($"[ADDRESS] ct rt {srt.width}x{srt.height} uiCam {(uiCam ? uiCam.name + " enabled " + uiCam.enabled + " ortho " + uiCam.orthographic : "none")} crt canvas {crtImg.canvas.name} mode {crtImg.canvas.renderMode} mat {(crtImg.material ? crtImg.material.shader.name : "none")}");
                    if (uiCam) uiCam.Render();
                    var t2 = new Texture2D(srt.width, srt.height, TextureFormat.RGB24, false); RenderTexture.active = srt; t2.ReadPixels(new Rect(0, 0, srt.width, srt.height), 0, 0); t2.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(dir, "c1_rt.png"), t2.EncodeToPNG());
                }
            }
            foreach (var cv in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                foreach (var gr in cv.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                {
                    var rt = gr.rectTransform; var wc = rt.TransformPoint(rt.rect.center);
                    var sp = RectTransformUtility.WorldToScreenPoint(cv.renderMode == RenderMode.ScreenSpaceOverlay ? null : cv.worldCamera, wc);
                    if (Mathf.Abs(sp.x - Screen.width / 2f) < 60 && Mathf.Abs(sp.y - Screen.height / 2f) < 60 && rt.rect.width < 120)
                        Debug.Log($"[ADDRESS] ct center graphic {gr.GetType().Name} '{gr.name}' in canvas '{cv.name}' size {rt.rect.size}");
                }
            foreach (var g in computer.root.GetComponentsInChildren<UnityEngine.UI.RawImage>(true)) Debug.Log($"[ADDRESS] ct rawimage {g.name} tex {(g.texture ? g.texture.name + " " + g.texture.width + "x" + g.texture.height : "null")} parent {g.transform.parent?.name}");
            Debug.Log($"[ADDRESS] ct screen {Screen.width}x{Screen.height} internet {Application.internetReachability} uwb {Address.Interact.RealBrowser.Available}");
            float t = 0; Address.Interact.RealBrowser rb = null;
            while (t < 20f && (rb = FindFirstObjectByType<Address.Interact.RealBrowser>()) == null) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(8f); Snap("c2_browser_home");
            if (rb) { rb.Go("ru.wikipedia.org"); yield return new WaitForSeconds(14f); Snap("c3_wiki2007"); rb.Go("старые компьютеры"); yield return new WaitForSeconds(10f); Snap("c4_search"); }
            computer?.Close();
            Debug.Log("[ADDRESS] computertest done");
            Application.Quit();
        }

        // ------------------------------------------------------------------ recording

        void OverlayToCamera()
        {
            foreach (var cv in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (cv.isRootCanvas && cv.renderMode == RenderMode.ScreenSpaceOverlay) { cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = cam; cv.planeDistance = 0.1f + cv.sortingOrder * 0.001f; }
        }

        IEnumerator Recorder()
        {
            recording = true;
            while (recording)
            {
                yield return null;
                if (Time.frameCount % 60 == 0) OverlayToCamera();
                foreach (var oc in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (oc != cam && oc.targetTexture && oc.enabled) oc.Render();
                var prev = cam.targetTexture; cam.targetTexture = vrt; cam.Render(); cam.targetTexture = prev;
                RenderTexture.active = vrt; vtex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); vtex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(vdir, $"f{vframe++:00000}.jpg"), vtex.EncodeToJPG(88));
            }
        }

        void Log(string s) { caption = s; Debug.Log($"[ADDRESS] video t={vframe / 30f:F1}s: {s}"); }

        // ------------------------------------------------------------------ "hands on the controls"

        static IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; yield return null; } }

        void Press(System.Action set) { set(); StartCoroutine(ClearNext()); }
        IEnumerator ClearNext() { yield return null; yield return null; si.ClearEdges(); }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        float Dist(Address.Entities.Stalker e) => e ? Flat(e.transform.position - player.transform.position).magnitude : -1;
        static Vector3 Center(PlacedObject o) { var r = o.GetComponentsInChildren<Renderer>(); if (r.Length == 0) return o.transform.position; var b = r[0].bounds; foreach (var x in r) b.Encapsulate(x.bounds); return b.center; }

        /// <summary>Turn the view towards a direction like a mouse would (limited degrees per frame).</summary>
        void SteerTowards(Vector3 dir, float targetPitch = 3f)
        {
            dir = Flat(dir); if (dir.sqrMagnitude < 1e-4f) { si.look = Vector2.zero; return; }
            float want = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, cur = fpc.transform.eulerAngles.y;
            float dy = Mathf.DeltaAngle(cur, want);
            si.look = new Vector2(Mathf.Clamp(dy * 0.18f, -5f, 5f), Mathf.Clamp((fpc.Pitch - targetPitch) * 0.12f, -3f, 3f));
        }

        IEnumerator AimAt(Vector3 p, float time)
        {
            float t = 0;
            while (t < time)
            {
                t += Time.deltaTime;
                var d = p - cam.transform.position; float pitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1, 1)) * Mathf.Rad2Deg;
                SteerTowards(d, pitch); yield return null;
            }
            si.look = Vector2.zero;
        }

        IEnumerator AimPitch(float pitch, float time) { float t = 0; while (t < time) { t += Time.deltaTime; si.look = new Vector2(0, Mathf.Clamp((fpc.Pitch - pitch) * 0.15f, -3, 3)); yield return null; } si.look = Vector2.zero; }

        IEnumerator Turn(float deg, float time)
        {
            float t = 0, per = deg / (time * 30f);
            while (t < time) { t += Time.deltaTime; si.look = new Vector2(per, (fpc.Pitch - 2f) * 0.1f); yield return null; }
            si.look = Vector2.zero;
        }

        IEnumerator LookAround(float time)
        {
            float t = 0; float baseYaw = fpc.transform.eulerAngles.y;
            while (t < time)
            {
                t += Time.deltaTime;
                float yaw = baseYaw + Mathf.Sin(t / time * Mathf.PI * 2) * 55f, pitch = Mathf.Sin(t * 1.3f) * 8f;
                SteerTowards(Quaternion.Euler(0, yaw, 0) * Vector3.forward, pitch); yield return null;
            }
            si.look = Vector2.zero;
        }

        IEnumerator WalkTo(Vector3 goal, float stopDist, bool run, float timeout)
        {
            var nav = level.nav;
            var a = NearestFree(nav, player.transform.position); var b = NearestFree(nav, goal);
            var cells = nav.FindPath(a, b, false, true) ?? nav.FindPath(a, b, false, false);
            if (cells == null) { Log("no path"); yield break; }
            var pts = cells.Select(c => nav.Center(c.x, c.y, player.transform.position.y)).ToList();
            int i = 0; float t = 0, stuckT = 0; var lastP = player.transform.position;
            si.run = run;
            while (t < timeout)
            {
                t += Time.deltaTime;
                var p = player.transform.position;
                if (Flat(goal - p).magnitude < stopDist) break;
                while (i < pts.Count - 1 && Flat(pts[i] - p).magnitude < 0.6f) i++;
                // farthest path point (up to 3 m ahead) reachable in a straight line for the capsule
                int look = Mathf.Min(pts.Count - 1, i + 1);
                for (int k = Mathf.Min(pts.Count - 1, i + 12); k > i; k--)
                {
                    var o = p + Vector3.up * 0.9f; var tgt = pts[k] + Vector3.up * 0.9f; var dd = tgt - o;
                    if (!Physics.SphereCast(o, 0.3f, dd.normalized, out _, dd.magnitude, (1 << LevelBuilder.ArchLayer) | (1 << LevelBuilder.PropsLayer), QueryTriggerInteraction.Ignore)) { look = k; break; }
                }
                var dir = pts[look] - p;
                SteerTowards(dir, Mathf.Sin(t * 0.7f) * 4f);
                float off = Mathf.Abs(Mathf.DeltaAngle(fpc.transform.eulerAngles.y, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg));
                si.move = new Vector2(0, off < 50 ? 1 : 0.2f);
                stuckT += Time.deltaTime;
                if (stuckT > 1.5f)
                {
                    if ((p - lastP).magnitude < 0.25f)
                    {
                        // stuck: find a new path from here (furniture may have moved), hop and sidestep
                        var a2 = NearestFree(nav, p); var np2 = nav.FindPath(a2, b, false, true) ?? nav.FindPath(a2, b, false, false);
                        if (np2 != null) { pts = np2.Select(c => nav.Center(c.x, c.y, p.y)).ToList(); i = 0; }
                        Press(() => si.jump = true); si.move = new Vector2(Random.Range(-1f, 1f), 0.3f);
                    }
                    stuckT = 0; lastP = p;
                }
                yield return null;
            }
            si.move = Vector2.zero; si.run = false; si.look = Vector2.zero;
        }

        static Vector2Int NearestFree(NavGrid nav, Vector3 p)
        {
            var c = nav.CellOf(p);
            for (int r = 0; r < 12; r++)
                for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
                    if (nav.Roomy(c.x + dx, c.y + dz)) return new Vector2Int(c.x + dx, c.y + dz);
            return c;
        }

        PlacedObject NearestGrabbable(float maxDist) =>
            level.engine.placed.Where(o => o && o.profile && o.profile.massClass == MassClass.Small && o.supportParent == null && !o.intentionalAnomaly
                                         && o.profile.support != SupportType.Wall && o.profile.support != SupportType.Ceiling
                                         && Flat(o.transform.position - player.transform.position).magnitude < maxDist)
                               .OrderBy(o => (o.transform.position - player.transform.position).sqrMagnitude).FirstOrDefault();

        /// <summary>A roomy floor point at the given distance that the camera can see.</summary>
        Vector3 VisibleSpot(float minD, float maxD)
        {
            var nav = level.nav; Vector3 best = player.transform.position + player.transform.forward * minD; float bestScore = -1;
            for (int k = 0; k < 400; k++)
            {
                float ang = k * 137.5f, d = Mathf.Lerp(minD, maxD, (k % 7) / 6f);
                var p = player.transform.position + Quaternion.Euler(0, ang, 0) * Vector3.forward * d;
                var c = nav.CellOf(p); if (!nav.Roomy(c.x, c.y)) continue;
                p = nav.Center(c.x, c.y, level.start.y);
                var from = cam.transform.position; var to = p + Vector3.up * 1.5f;
                if (Physics.Raycast(from, (to - from).normalized, (to - from).magnitude, (1 << LevelBuilder.ArchLayer) | (1 << LevelBuilder.PropsLayer))) continue;
                float score = d;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }
    }
}
