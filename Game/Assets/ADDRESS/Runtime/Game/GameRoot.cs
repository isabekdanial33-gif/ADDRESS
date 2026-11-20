using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Address.Core;
using Address.Player;
using Address.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Address.Net;
using Unity.Netcode;

namespace Address.Game
{
    /// <summary>
    /// The running game: builds the level for an address, spawns the player, handles exits and travel through the
    /// Library (forward -> a new unvisited address, loop -> Echo g+1 of this place, false -> a door to nowhere).
    /// </summary>
    public partial class GameRoot : MonoBehaviour
    {
        public GameConfig config;
        public static LevelAddress? RequestedAddress;   // set by the main menu ("enter address / phrase")

        LevelBuilder.Result level;
        AddressBook book;
        GameObject playerBody; float suitTimer; GameObject player; FirstPersonController fpc; GrabSystem grab; Camera cam; FpArms fpArms;
        Image fade; Hud hud;
        bool travelling;
        IPlayerInput input; DeviceInput deviceInput; TouchInput touch;
        MenuUI menu;
        AudioDirector audioDir;
        PhotoAlbum album;
        float flash, lastRequest;
        int remoteMoved = -1;
        enum State { Menu, Playing, Paused }
        State state = State.Menu;
        float menuYaw;

        void Awake()
        {
            // automated test runs (any "-xxxtest", shots, video, verify): silent from the very first frame
            var a0 = System.Environment.GetCommandLineArgs();
            if (Application.isBatchMode || a0.Any(x => x == "-shots" || x == "-playvideo" || x == "-verify" || x == "-roomsonly" || x == "-trailer" || x.EndsWith("test")))
            { AudioListener.volume = 0f; AudioListener.pause = true; }
        }

        void Start()
        {
            if (System.Environment.GetCommandLineArgs().Contains("-touchui")) TouchInput.Force = true;
            Application.targetFrameRate = Application.isMobilePlatform ? 60 : -1;   // phones default to 30
            if (Application.isMobilePlatform) { Screen.sleepTimeout = SleepTimeout.NeverSleep; QualitySettings.vSyncCount = 0; }
            Physics.simulationMode = SimulationMode.FixedUpdate;   // editor test tools switch it to Script; the game needs real physics
            if (PlayerPrefs.HasKey("quality2")) QualitySettings.SetQualityLevel(PlayerPrefs.GetInt("quality2"), true);   // new key: tier list changed
            else if (Application.isMobilePlatform) QualitySettings.SetQualityLevel(System.Array.IndexOf(QualitySettings.names, "Mobile") is int mq && mq >= 0 ? mq : 0, true);
            gameObject.AddComponent<Address.Rendering.FrameGovernor>();
            Address.Mods.ModSystem.Load();
            if (!Application.isBatchMode) Address.Mods.ModSystem.LoadModels();
            book = AddressBook.Load();
            album = PhotoAlbum.Load();
            SetupLighting();
            BuildHud();
            BuildPlayer();
            Address.Net.SteamService.ensureManager = () => NetLauncher.Ensure(config.netAvatarPrefab, config.netSessionPrefab);
            Address.Net.SteamService.onJoined = () => StartCoroutine(WaitSession());
            Address.Net.SteamService.Init();
            Address.Entities.Bestiary.onNew = (section, name) => { if (state == State.Playing) hud.Toast("+ в индекс", section + ": " + name); };
            menu = new MenuUI
            {
                onContinue = Continue,
                onNewPath = () => Play(book.NextFree("BR", Hashing.Combine(Hashing.Fnv64("new path"), (ulong)System.DateTime.Now.Ticks))),
                onAddress = text => Play(LevelAddress.FromPhrase(text, _ => "BR")),
                onQuit = () => { Application.Quit(); },
                onMainMenu = ToMenu,
                onHost = StartHosting,
                onSteamHost = StartSteamHosting,
                onSteamInvite = Address.Net.SteamService.Invite,
                onJoin = ip => JoinSession(ip),
                netStatus = () => NetLauncher.Manager && NetLauncher.Manager.IsServer ? $"Вы хост. Ваш IP: {NetLauncher.LocalIp()} - игроков: {NetLauncher.Manager.ConnectedClientsIds.Count}/2"
                                 : NetLauncher.Manager && NetLauncher.Manager.IsClient ? "Подключено." : "",
                footer = () => $"мест посещено: {book.visited.Count}    фото: {album.photos.Count}    лучший снимок: {(album.photos.Count > 0 ? album.photos.Max(p => p.score) : 0)}",
                here = () => level != null && state != State.Menu ? level.address.ToString() : "",
                archive = () => album.photos.OrderByDescending(p => p.score).Take(8).Select(p => $"{p.score,5}  {p.address}  {string.Join(", ", p.subjects.Take(3))}")
                                    .Concat(book.visited.Select(v => "место: " + v)).Concat(book.echoLinks.Select(e => "эхо: " + e)).ToList()
            };
            // the menu stands inside a real place of the Library: a calm room, slowly looked around
            var bg = RequestedAddress ?? new LevelAddress("BR", Hashing.Combine(Hashing.Fnv64("menu"), (ulong)System.DateTime.Now.DayOfYear));
            StartCoroutine(Enter(bg, null, true));
            ToMenu();
            var args = System.Environment.GetCommandLineArgs();
            // automated test runs: silent, and they always end (even if a test throws)
            if (args.Contains("-smoketest") || args.Contains("-shots") || args.Contains("-nethost") || args.Contains("-netclient") || args.Contains("-playvideo") || args.Contains("-computertest") || args.Contains("-opentest") || args.Contains("-verify") || args.Contains("-gametest"))
            {
                AudioListener.volume = 0f;
                StartCoroutine(Watchdog(args.Contains("-playvideo") ? 600f : args.Contains("-shots") ? 480f : 240f));
            }
            if (args.Contains("-smoketest")) StartCoroutine(Smoke());
            if (args.Contains("-shots")) StartCoroutine(Shots());
            if (args.Contains("-playvideo")) StartCoroutine(PlayVideo());
            if (args.Contains("-computertest")) StartCoroutine(ComputerTest());
            if (args.Contains("-opentest")) StartCoroutine(OpenTest());
            if (args.Contains("-verify")) StartCoroutine(Verify());
            if (args.Contains("-gametest")) StartCoroutine(GameTest());
            if (args.Contains("-trailer")) { AudioListener.volume = 0f; StartCoroutine(Watchdog(900f)); StartCoroutine(Trailer()); }
            if (args.Contains("-nethost")) StartCoroutine(NetHostTest());
            if (args.Contains("-netclient")) StartCoroutine(NetClientTest());
        }

        /// <summary>Headless runtime check of the real player build: menu -> new path -> exit -> travel.</summary>
        // renders what the player really sees in the BUILD (batchmode with a GPU, no window) into PNGs
        IEnumerator Shots()
        {
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "shots"); System.IO.Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1280, 720, 24); var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            void Shot(Camera c, string name)
            {
                OverlayToCamera();
                var prev = c.targetTexture; c.targetTexture = rt; c.Render(); c.targetTexture = prev;
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            }
            yield return new WaitUntil(() => !travelling && level != null);
            // overlay canvases are not drawn into a render texture: show them through the camera for the shots
            foreach (var cv in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) if (cv.isRootCanvas) { cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = cam; cv.planeDistance = 0.1f + cv.sortingOrder * 0.001f; }
            cam.nearClipPlane = 0.03f;
            yield return new WaitForSeconds(1.5f);
            Shot(cam, "00_menu");
            typeof(MenuUI).GetMethod("SettingsPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(menu, null);
            yield return new WaitForSeconds(1f); Shot(cam, "01_settings");
            Address.Entities.Bestiary.SeeCreature("Verity"); Address.Entities.Bestiary.SeeCreature("Clark"); Address.Entities.Bestiary.VisitWorld("BR");
            typeof(MenuUI).GetMethod("IndexList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(menu, new object[] { "существа", Address.Entities.Bestiary.Creatures, 0 });
            yield return new WaitForSeconds(1f); Shot(cam, "02_index");
            typeof(MenuUI).GetMethod("AddressPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(menu, null);
            yield return new WaitForSeconds(1f); Shot(cam, "02_address");
            Play(new LevelAddress("BR", Hashing.Fnv64("shots")));
            yield return new WaitForSeconds(0.5f); yield return new WaitUntil(() => !travelling);
            yield return new WaitForSeconds(1.2f); Shot(cam, "05_titlecard");
            yield return new WaitForSeconds(0.3f);
            Debug.Log($"[ADDRESS] shots: {level.address} rooms {level.rooms.Count} props {level.engine.placed.Count} renderers {level.root.GetComponentsInChildren<Renderer>().Length} enabled {level.root.GetComponentsInChildren<Renderer>().Count(r => r.enabled)} player {player.transform.position} cam {cam.transform.position}");
            {
                var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                var rs = level.root.GetComponentsInChildren<Renderer>(true);
                int inF = 0, act = 0, nullMat = 0; var shaders = new System.Collections.Generic.HashSet<string>();
                foreach (var r in rs) { if (r.gameObject.activeInHierarchy) act++; if (GeometryUtility.TestPlanesAABB(planes, r.bounds)) inF++; foreach (var m in r.sharedMaterials) { if (!m) nullMat++; else shaders.Add(m.shader.name + (m.shader.isSupported ? "" : "(UNSUPPORTED)")); } }
                var near = rs.OrderBy(r => (r.bounds.center - cam.transform.position).sqrMagnitude).Take(6).Select(r => $"{r.name}@{r.bounds.center} en={r.enabled} act={r.gameObject.activeInHierarchy} layer={r.gameObject.layer}");
                Debug.Log($"[ADDRESS] shots diag: rootActive {level.root.activeInHierarchy} rootPos {level.root.transform.position} activeRenderers {act} inFrustum {inF} nullMats {nullMat} camMask {cam.cullingMask} far {cam.farClipPlane} fog {RenderSettings.fog} {RenderSettings.fogMode} {RenderSettings.fogDensity} {RenderSettings.fogStartDistance}-{RenderSettings.fogEndDistance} shaders [{string.Join(", ", shaders)}] near: {string.Join(" | ", near)}");
            }
            foreach (var kb in level.engine.placed.Where(o => o && o.profile && (o.profile.group == "keyboard" || o.profile.group == "mouse")).Take(12))
            {
                var sp = kb.supportParent; float top = sp ? sp.transform.position.y + sp.profile.surfaces.Where(q => q.kind == "top").Select(q => q.height).DefaultIfEmpty(-9).Max() : -9;
                Debug.Log($"[ADDRESS] kbcheck {kb.name} y {kb.transform.position.y:F3} on {(sp ? sp.name : "-")} top {top:F3} diff {kb.transform.position.y - top:F3} rb {(kb.GetComponent<Rigidbody>() != null)} by {kb.placedBy}");
            }
            for (int i = 0; i < 4; i++) { fpc.SetLook(level.startYaw + i * 90, 5); yield return null; yield return null; Shot(cam, $"1{i}_look{i * 90}"); }
            fpc.SetLook(level.startYaw, 75); yield return null; yield return null; Shot(cam, "20_down");
            // third person views of the body
            var tp = new GameObject("tpcam").AddComponent<Camera>(); tp.fieldOfView = 50; tp.nearClipPlane = 0.05f;
            tp.cullingMask = ~(1 << LevelBuilder.ArchLayer); tp.clearFlags = CameraClearFlags.SolidColor; tp.backgroundColor = new Color(0.3f, 0.32f, 0.36f);
            var f = Quaternion.Euler(0, level.startYaw, 0) * Vector3.forward;
            tp.transform.position = player.transform.position + f * 2.4f + Vector3.up * 1.3f; tp.transform.LookAt(player.transform.position + Vector3.up * 0.9f);
            Shot(tp, "30_body_front");
            tp.transform.position = player.transform.position + Quaternion.Euler(0, 90, 0) * f * 2.4f + Vector3.up * 1.3f; tp.transform.LookAt(player.transform.position + Vector3.up * 0.9f);
            Shot(tp, "31_body_side");
            // walk forward and catch several stride phases from the side + first person looking down
            var si = new ScriptedInput(); var old = fpc.input; fpc.input = si; grab.input = si;
            void Side(string n)
            {
                var fw = Quaternion.Euler(0, fpc.transform.eulerAngles.y, 0) * Vector3.forward;
                tp.transform.position = player.transform.position + Quaternion.Euler(0, 90, 0) * fw * 2.6f + Vector3.up * 1.0f; tp.transform.LookAt(player.transform.position + Vector3.up * 0.85f);
                Shot(tp, n);
            }
            fpc.SetLook(level.startYaw, 0);
            si.move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.8f);
            for (int k = 0; k < 4; k++) { yield return new WaitForSeconds(0.17f); Side($"32_walk{k}"); }
            si.run = true; yield return new WaitForSeconds(0.8f);
            for (int k = 0; k < 2; k++) { yield return new WaitForSeconds(0.12f); Side($"34_run{k}"); }
            fpc.SetLook(fpc.transform.eulerAngles.y, 70); yield return null; Shot(cam, "35_fp_down_run");
            si.run = false; si.move = Vector2.zero; yield return new WaitForSeconds(0.6f);
            Shot(cam, "36_fp_down_still");
            si.jump = true; yield return null; si.jump = false; yield return new WaitForSeconds(0.18f);
            Side("37_jump"); fpc.SetLook(fpc.transform.eulerAngles.y, 0);
            Debug.Log($"[ADDRESS] shots jump: y {player.transform.position.y:F2}");
            yield return new WaitForSeconds(1f);
            si.crouch = true; yield return new WaitForSeconds(0.8f); Side("38_crouch"); si.crouch = false; yield return new WaitForSeconds(0.6f);
            // pick up a nearby small object and look at the hands (try several until one is in reach)
            var cands = level.engine.placed.Where(o => o && o.profile && o.profile.massClass == Address.Placement.MassClass.Small && o.supportParent == null && o.profile.support != Address.Placement.SupportType.Wall && o.profile.support != Address.Placement.SupportType.Ceiling)
                           .OrderBy(o => (o.transform.position - player.transform.position).sqrMagnitude).Take(12).ToList();
            foreach (var cand in cands)
            {
                var cc2 = player.GetComponent<CharacterController>(); var tgt = cand.GetComponentInChildren<Renderer>().bounds.center;
                var flat = tgt - player.transform.position; flat.y = 0; if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
                cc2.enabled = false; player.transform.position = new Vector3(tgt.x, player.transform.position.y, tgt.z) - flat.normalized * 1.0f; cc2.enabled = true;
                yield return null;
                var look = tgt - cam.transform.position;
                fpc.SetLook(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, -Mathf.Asin(look.normalized.y) * Mathf.Rad2Deg);
                yield return null; yield return null;
                yield return new WaitForSeconds(0.3f); Shot(cam, "39a_prompt");
                si.grab = true; yield return null; si.grab = false; yield return new WaitForSeconds(0.2f);
                if (!grab.Held) continue;
                fpc.SetLook(fpc.transform.eulerAngles.y, 12);
                for (int k = 0; k < 6 && grab.Held; k++)
                {
                    var hrb = grab.Held.GetComponent<Rigidbody>(); var tg = cam.transform.position + cam.transform.forward * 0.58f;
                    var ov = Physics.OverlapBox(grab.Held.GetComponentInChildren<Renderer>().bounds.center, grab.Held.GetComponentInChildren<Renderer>().bounds.extents * 0.9f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Where(c => !c.transform.IsChildOf(grab.Held.transform)).Select(c => c.name + "/L" + c.gameObject.layer);
                    Debug.Log($"[ADDRESS] shots grab diag {grab.Held.name}: com {hrb.worldCenterOfMass} target {tg} kin {hrb.isKinematic} vel {hrb.linearVelocity} overlaps [{string.Join(",", ov)}]");
                    yield return new WaitForSeconds(0.1f);
                }
                yield return new WaitForSeconds(0.2f); if (!grab.Held) { Debug.Log("[ADDRESS] shots grab lost: " + grab.LastEvent); continue; }
                yield return new WaitForSeconds(0.8f);
                Debug.Log($"[ADDRESS] shots grab: {cand.name} held={(grab.Held ? grab.Held.name : "none")}");
                fpc.SetLook(fpc.transform.eulerAngles.y, 12); yield return new WaitForSeconds(0.4f);
                Shot(cam, "39_fp_hold"); Side("39_side_hold");
                TakePhoto(); yield return new WaitForSeconds(0.25f); Shot(cam, "39b_photo_toast");
                si.move = new Vector2(0, 1); yield return new WaitForSeconds(0.5f); Shot(cam, "39_fp_hold_walk"); si.move = Vector2.zero;
                break;
            }
            grab.input = old;
            // ---- entity: spawn one in front, look at it (frozen), turn away (it comes), get caught -> Echo
            {
                if (grab.Held) { si.grab = true; yield return null; si.grab = false; yield return new WaitForSeconds(0.5f); }
                foreach (var old2 in entities) if (old2) Destroy(old2.gameObject);
                entities.Clear(); forceEntity = true; SpawnEntities(); forceEntity = false;
                var en = entities.FirstOrDefault();
                if (en)
                {
                    var spot = VisibleSpot(4.5f, 7f);
                    en.transform.position = spot; en.transform.rotation = Quaternion.LookRotation(Flat(player.transform.position - spot));
                    var lk = spot + Vector3.up * 1.5f - cam.transform.position;
                    fpc.SetLook(Mathf.Atan2(lk.x, lk.z) * Mathf.Rad2Deg, -Mathf.Asin(lk.normalized.y) * Mathf.Rad2Deg);
                    en.wakeDelay = 0; en.catchRadius = -1;   // no catching while photographing it
                    yield return new WaitForSeconds(1.2f);
                    Shot(cam, "50_entity_fp"); Debug.Log($"[ADDRESS] shots entity: mode {en.mode} seen {en.SeenNow} dist {Vector3.Distance(en.transform.position, player.transform.position):F2}");
                    var ecam = tp; ecam.cullingMask = ~(1 << LevelBuilder.ArchLayer);
                    ecam.transform.position = en.transform.position + en.transform.forward * 2.6f + Vector3.up * 1.4f; ecam.transform.LookAt(en.transform.position + Vector3.up * 1.2f); Shot(ecam, "51_entity_front");
                    ecam.transform.position = en.transform.position + en.transform.right * 2.8f + Vector3.up * 1.3f; ecam.transform.LookAt(en.transform.position + Vector3.up * 1.1f); Shot(ecam, "52_entity_side");
                    ecam.transform.position = en.transform.position + en.transform.forward * 0.9f + Vector3.up * 2.0f; ecam.transform.LookAt(en.transform.position + Vector3.up * 1.95f); ecam.cullingMask = ~0; Shot(ecam, "53_entity_face");
                    // turn away
                    fpc.SetLook(fpc.transform.eulerAngles.y + 180f, 0); float d0 = Vector3.Distance(en.transform.position, player.transform.position);
                    yield return new WaitForSeconds(1.0f);
                    Debug.Log($"[ADDRESS] shots entity after turning away 1s: mode {en.mode} dist {d0:F2} -> {Vector3.Distance(en.transform.position, player.transform.position):F2}");
                    fpc.SetLook(fpc.transform.eulerAngles.y + 180f, 0); yield return null; yield return null;
                    Shot(cam, "54_entity_closer"); Debug.Log($"[ADDRESS] shots entity looked back: mode {en.mode} seen {en.SeenNow}");
                    fpc.SetLook(fpc.transform.eulerAngles.y + 180f, 0); en.catchRadius = 0.85f;
                    var from = level.address; float tw = 0;
                    while (level.address.Equals(from) && tw < 12f) { tw += Time.deltaTime; if (travelling && tw > 0.1f && tw < 0.6f) { } yield return null; }
                    yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(0.4f);
                    Shot(cam, "55_after_catch"); Debug.Log($"[ADDRESS] shots entity catch: {from} -> {level.address}");
                }
                else Debug.Log("[ADDRESS] shots entity: spawn failed");
            }
            fpc.input = old;
            // same pipeline, menu address again: is it "second level" or "this level"?
            StartCoroutine(Enter(new LevelAddress("BR", Hashing.Combine(Hashing.Fnv64("menu"), (ulong)System.DateTime.Now.DayOfYear)), null, true));
            yield return new WaitForSeconds(0.5f); yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(1f);
            Shot(cam, "40_menu_again");
            // ---- the new room kinds
            foreach (var addr in new[] { "rooms a", "rooms b", "rooms c", "rooms d", "rooms e" })
            {
                StartCoroutine(Enter(new LevelAddress("BR", Hashing.Fnv64(addr)), null));
                yield return new WaitForSeconds(0.5f); yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(0.5f);
                foreach (var kind in new[] { "showroom", "dump", "pillars", "wrong", "dark" })
                {
                    var lroom = level.layout.rooms.OrderByDescending(rm => rm.World.width * rm.World.height).FirstOrDefault(rm => kind == "dark" ? rm.dark && rm.World.width * rm.World.height > 20 : rm.kind == kind);
                    var room = lroom?.spec; if (room == null) continue;
                    var cc4 = player.GetComponent<CharacterController>(); var b = room.bounds;
                    var corner = new Vector3(b.min.x + 0.6f, 0.02f, b.min.z + 0.6f);
                    cc4.enabled = false; player.transform.position = corner; cc4.enabled = true;
                    var lk = b.center - corner; fpc.SetLook(Mathf.Atan2(lk.x, lk.z) * Mathf.Rad2Deg, 8); yield return null; yield return null;
                    yield return new WaitForSeconds(0.45f);
                    Shot(cam, $"80_{kind}_{addr.Replace(' ', '_')}");
                    Debug.Log($"[ADDRESS] tour {kind} {addr}: {b.size.x:F1}x{b.size.z:F1} props {level.engine.placed.Count(o => o && b.Contains(new Vector3(o.transform.position.x, b.center.y, o.transform.position.z)))} pillars {level.root.GetComponentsInChildren<Transform>().Count(t => t.name == "Pillar" && b.Contains(new Vector3(t.position.x, b.center.y, t.position.z)))}");
                }
            }
            // ---- every entity kind, walking in place 3 m in front of the camera
            {
                var cc5 = player.GetComponent<CharacterController>(); cc5.enabled = false; player.transform.position = level.start; cc5.enabled = true;
                // the most open of 8 directions from the start
                float bestFree = 0; float yawE = level.startYaw;
                for (int i = 0; i < 8; i++)
                {
                    var d = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;
                    float free = Physics.Raycast(level.start + Vector3.up * 1.0f, d, out var hh, 20f, ~0, QueryTriggerInteraction.Ignore) ? hh.distance : 20f;
                    if (free > bestFree) { bestFree = free; yawE = i * 45f; }
                }
                var fwd = Quaternion.Euler(0, yawE, 0) * Vector3.forward; float distE = Mathf.Clamp(bestFree - 1.0f, 1.8f, 3.4f);
                foreach (Address.Entities.EntityKind kind in System.Enum.GetValues(typeof(Address.Entities.EntityKind)))
                {
                    var e = Address.Entities.EntitySpawner.SpawnKind(level, config.bodyPrefab, kind, level.start + fwd * distE, Quaternion.LookRotation(-fwd), null);
                    e.testSpeed = 0f;
                    var an = e.GetComponentInChildren<Animator>();
                    foreach (var sp in new[] { 0f, 1.2f })
                    {
                        e.testSpeed = sp;
                        fpc.SetLook(yawE, 2); yield return new WaitForSeconds(0.9f);
                        Shot(cam, $"85_ent_{kind}_{(sp > 0 ? "walk" : "idle")}");
                    }
                    Debug.Log($"[ADDRESS] entity {kind}: animator {(an ? an.runtimeAnimatorController?.name : "none")} human {(an && an.isHuman)}");
                    Destroy(e.gameObject); yield return null;
                }
                // the Shy one: sits and weeps until looked at, then screams and runs at you
                {
                    var shy = Address.Entities.EntitySpawner.SpawnKind(level, config.bodyPrefab, Address.Entities.EntityKind.Shy, level.start + fwd * distE, Quaternion.LookRotation(-fwd), null);
                    shy.wakeDelay = 0; shy.targets = EntityTargets; shy.isLookedAtBy = LooksAt(shy); shy.onCatch = _ => Debug.Log("[ADDRESS] shy: caught");
                    fpc.SetLook(yawE + 180f, 0); yield return new WaitForSeconds(1.0f);
                    Debug.Log($"[ADDRESS] shy unseen: enraged {shy.Enraged} mode {shy.mode}");
                    fpc.SetLook(yawE, 0); yield return new WaitForSeconds(0.6f); Shot(cam, "85b_shy_seen");
                    var d0 = Vector3.Distance(shy.transform.position, player.transform.position);
                    yield return new WaitForSeconds(4.2f); Shot(cam, "85b_shy_rage");
                    Debug.Log($"[ADDRESS] shy seen: enraged {shy.Enraged} mode {shy.mode} dist {d0:F2} -> {Vector3.Distance(shy.transform.position, player.transform.position):F2}");
                    Destroy(shy.gameObject); yield return null;
                }
                // Verity and the fake one: hanging in front, then talking (the subtitle must show)
                foreach (var fk in new[] { false, true })
                {
                    var v = Address.Entities.Verity.Spawn(level, level.start + fwd * 2.2f + Vector3.up * 1.2f, fk);
                    if (!v) { Debug.LogError("[ADDRESS] verity: no model"); break; }
                    v.viewer = cam.transform; v.monsters = () => entities; v.say = (who, line, sec) => hud.Say(who, line, sec);
                    verities.Clear(); verities.Add(v);
                    hud.SetVisible(true); state = State.Playing;
                    fpc.SetLook(yawE, 0); yield return new WaitForSeconds(1.2f); Shot(cam, $"87_{v.Key}_idle");
                    v.Talk(); yield return new WaitForSeconds(2.2f); Shot(cam, $"87_{v.Key}_talk1");
                    v.Talk(); yield return new WaitForSeconds(1.8f); Shot(cam, $"87_{v.Key}_talk2");
                    Destroy(v.gameObject); verities.Clear(); yield return null;
                    state = State.Menu; hud.SetVisible(false);
                }
                // first person: legs when looking down, arms at rest; crawling seen from the side; the vent duct from inside
                {
                    var cc6 = player.GetComponent<CharacterController>();
                    hud.SetVisible(true); state = State.Playing; fpc.enabled = false;
                    fpc.SetLook(yawE, 70); yield return new WaitForSeconds(0.6f); Shot(cam, "88_fp_legs");
                    fpc.SetLook(yawE, 0); yield return new WaitForSeconds(0.4f); Shot(cam, "88_fp_arms_idle");
                    fpc.ForceCrawl = true; for (int i = 0; i < 20; i++) fpc.Tick(0.05f);
                    var side = Vector3.Cross(Vector3.up, fwd);
                    var dummy = Instantiate(config.bodyPrefab, level.start + fwd * 2.4f, Quaternion.LookRotation(side));
                    foreach (var c in dummy.GetComponentsInChildren<Collider>()) Destroy(c);
                    var dpb = dummy.GetComponent<ProceduralBody>(); if (dpb) { dpb.firstPerson = false; dpb.fpc = fpc; }
                    fpc.SetLook(yawE, 12); yield return new WaitForSeconds(1.2f); Shot(cam, "88_crawl_side");
                    Destroy(dummy);
                    var droot = BuildDuct(out var ds, out var de, out var dyaw);
                    cc6.enabled = false; player.transform.position = ds; cc6.enabled = true; for (int i = 0; i < 5; i++) fpc.Tick(0.05f);
                    fpc.SetLook(dyaw, 0); yield return new WaitForSeconds(0.6f); Shot(cam, "88_duct_start");
                    cc6.enabled = false; player.transform.position = ds + new Vector3(0, 0, 5.6f); cc6.enabled = true; for (int i = 0; i < 5; i++) fpc.Tick(0.05f);
                    fpc.SetLook(90, 0); yield return new WaitForSeconds(0.6f); Shot(cam, "88_duct_bend");
                    Debug.Log($"[ADDRESS] duct: start {ds} end {de} crawling {fpc.Crawling} cam y {cam.transform.position.y - ds.y:F2}");
                    fpc.ForceCrawl = false; Destroy(droot);
                    cc6.enabled = false; player.transform.position = level.start; cc6.enabled = true; for (int i = 0; i < 20; i++) fpc.Tick(0.05f);
                    state = State.Menu; hud.SetVisible(false);
                }
            }
            // ---- the new worlds: garage and hotel, four looks each from the start
            foreach (var w in new[] { "GR", "PK", "DM", "TE", "TH", "BS", "BK" })
                for (int v = 0; v < 1; v++)
                {
                    StartCoroutine(Enter(new LevelAddress(w, Hashing.Fnv64("world " + w + v)), null));
                    yield return new WaitForSeconds(0.5f); yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(1.0f);
                    Debug.Log($"[ADDRESS] world {w}: {level.address} rooms {level.rooms.Count} exits {level.exits.Count(e => e.reachable)}/{level.exits.Count} {string.Join(",", level.exits.Select(e => e.form))} props {level.engine.placed.Count}");
                    for (int i = 0; i < 4; i += 2) { fpc.SetLook(level.startYaw + i * 90, 3); yield return new WaitForSeconds(0.35f); Shot(cam, $"86_{w}{v}_look{i}"); }
                }
            if (System.Environment.GetCommandLineArgs().Contains("-roomsonly")) { Application.Quit(); yield break; }
            // ---- Frutiger Aero park + the archway into the Poolrooms
            StartCoroutine(Enter(new LevelAddress("FA", Hashing.Fnv64("shots fa")), null));
            yield return new WaitForSeconds(0.5f); yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(1f);
            for (int i = 0; i < 3; i++) { fpc.SetLook(level.startYaw + i * 120, -8); yield return null; yield return null; Shot(cam, $"60_fa_{i}"); }
            var arch = level.exits.FirstOrDefault(x => x.form == ExitForm.Archway && x.instance);
            Debug.Log($"[ADDRESS] shots fa: exits {string.Join(",", level.exits.Select(x => x.form + "/" + x.role + (x.reachable ? "" : "!")))} basins {level.basins.Count}");
            if (arch != null)
            {
                var cc3 = player.GetComponent<CharacterController>(); var ap = arch.position + arch.normal * 4.6f;
                cc3.enabled = false; player.transform.position = new Vector3(ap.x, arch.position.y + 0.02f, ap.z); cc3.enabled = true;
                fpc.SetLook(Mathf.Atan2(-arch.normal.x, -arch.normal.z) * Mathf.Rad2Deg, 5); yield return null; yield return null;
                Shot(cam, "61_fa_archway");
                Travel(arch.instance); yield return new WaitForSeconds(0.9f); Shot(cam, "62_dissolve");
                yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(0.3f); Shot(cam, "63_poolrooms");
                Debug.Log($"[ADDRESS] shots fa -> {level.address}");
            }
            Debug.Log("[ADDRESS] shots done -> " + dir);
            Application.Quit();
        }

        IEnumerator Watchdog(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.Log("[ADDRESS] watchdog: test run took too long, quitting");
            Application.Quit();
            yield return new WaitForSecondsRealtime(3f);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }

        IEnumerator Smoke()
        {
            yield return new WaitUntil(() => !travelling && level != null);
            Debug.Log($"[ADDRESS] smoke: menu level ok {level.address}");
            menu.onNewPath();
            yield return new WaitForSeconds(0.5f);
            yield return new WaitUntil(() => !travelling);
            var first = level.address;
            var exit = level.exits.First(e => e.role == ExitRole.Forward && e.reachable);
            exit.instance.Activate();
            yield return new WaitForSeconds(2.5f);
            var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
            player.transform.position = exit.position + exit.normal * 0.2f; cc.enabled = true;
            yield return new WaitForSeconds(0.3f);
            yield return new WaitUntil(() => !travelling);
            Debug.Log($"[ADDRESS] smoke: travelled {first} -> {level.address} via {exit.form}; fps {1f / Time.smoothDeltaTime:F0}; state {state}");
            TakePhoto();
            var dir = level.root.GetComponent<Director>();
            if (dir) { dir.stage1 = 0.1f; dir.stage2 = 0.2f; dir.stage3 = 0.3f; }
            yield return new WaitForSeconds(0.6f);
            foreach (var aest in new[] { "PR", "FA", "PG", "HT", "OF", "LF", "PD", "WH", "LO", "EL", "CV", "RN", "FL", "LB", "LA" })
            {
                StartCoroutine(Enter(new LevelAddress(aest, 777UL), level.address));
                yield return new WaitForSeconds(0.5f); yield return new WaitUntil(() => !travelling);
                Debug.Log($"[ADDRESS] smoke: {aest} level {level.address} rooms {level.rooms.Count} exits {level.exits.Count(e => e.reachable)}/{level.exits.Count} reachable");
            }
            Pause(); yield return new WaitForSecondsRealtime(0.2f); Resume();
            Debug.Log("[ADDRESS] smoke OK");
            Application.Quit();
        }

        void Continue()
        {
            if (state == State.Paused || (state == State.Menu && level != null && book.last == level.address.ToString())) { Resume(); return; }
            if (!string.IsNullOrEmpty(book.last) && LevelAddress.TryParse(book.last, out var last)) Play(last);
            else Play(book.NextFree("BR", Hashing.Fnv64("the first room")));
        }

        void Play(LevelAddress a)
        {
            menu.Show(false, false);
            state = State.Playing; Time.timeScale = 1;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            fpc.enabled = true; grab.enabled = true;
            hud.SetVisible(true);
            if (level == null || !level.address.Equals(a)) StartCoroutine(Enter(a, null));
            else hud.EnterPlace(a.ToString(), PlaceName(level.aesthetic, a.Generation));
        }

        void Resume()
        {
            menu.Show(false, false); state = State.Playing; Time.timeScale = 1; hud.SetVisible(true);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; fpc.enabled = true; grab.enabled = true;
        }

        void Pause()
        {
            state = State.Paused; Time.timeScale = 0; menu.Show(true, true); hud.SetVisible(false);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true; fpc.enabled = false; grab.enabled = false;
        }

        void ToMenu()
        {
            state = State.Menu; Time.timeScale = 1; menu.Show(true, false); hud.SetVisible(false);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true; fpc.enabled = false; grab.enabled = false;
        }

        // ------------------------------------------------------------------ world

        IEnumerator Enter(LevelAddress address, LevelAddress? cameFrom, bool menuBackground = false, bool crossfade = false)
        {
            travelling = true;
            Texture2D snap = null;
            if (crossfade)
            {
                // keep the last frame on screen while the next place is built, then dissolve into it
                yield return new WaitForEndOfFrame();
                try { snap = ScreenCapture.CaptureScreenshotAsTexture(); } catch { snap = null; }
                if (snap) hud.ShowSnapshot(snap); else yield return Fade(1f, 0.35f);
            }
            else yield return Fade(1f, 0.6f);
            if (level != null && level.root) Destroy(level.root);
            yield return null;
            // generation must not see anything peer-specific (the player's capsule at its old position would
            // touch objects during the physical stability check and change the result between machines)
            var pcc = player ? player.GetComponent<CharacterController>() : null;
            if (pcc) pcc.enabled = false;
            if (!menuBackground) { hud.Loading("→ " + address + "\nищу это место..."); yield return null; yield return null; }
            level = new LevelBuilder(config.kit).Build(address);
            hud.Loading(null);
            if (pcc) pcc.enabled = true;
            LevelBuilder.ApplyAtmosphere(level.aesthetic);
            Grade(level.aesthetic);
            if (level.probe) level.probe.RenderProbe();
            var echoLog = Echo.Apply(level, config.kit.wallMat);
            level.root.AddComponent<Address.Rendering.LevelOptimizer>().Init(level.root, cam.transform);
            if (audioDir) audioDir.SetLevel(level.root);
            if (!menuBackground && NetSession.Instance != null)
            {
                var hash = LevelBuilder.StructuralHash(level);
                if (NetLauncher.Manager.IsServer) { NetSession.Instance.Address.Value = address.ToString(); NetSession.Instance.LevelHash.Value = hash; }
                else Debug.Log($"[ADDRESS] net: level {address} hash {hash} host {NetSession.Instance.LevelHash.Value} -> {(hash == NetSession.Instance.LevelHash.Value.ToString() ? "SAME PLACE" : "MISMATCH (waiting for host hash)")}");
            }
            if (!menuBackground && level.aesthetic != "FA") level.root.AddComponent<Address.World.BrownOut>().viewer = player.transform;
            if (!menuBackground) { var d = level.root.AddComponent<Director>(); d.Begin(level, config.audio?.roomTone, config.dustMat); d.player = player.transform; }
            foreach (var e in level.exits.Where(e => e.role == ExitRole.False && e.instance))
                foreach (var r in e.instance.GetComponentsInChildren<Renderer>()) if (r.name == "void") r.sharedMaterial = config.kit.wallMat; // door to nowhere
            if (!menuBackground) { book.Visit(address); Address.Entities.Bestiary.VisitWorld(level.aesthetic); }
            if (cameFrom.HasValue && address.Generation > 0) book.LinkEcho(cameFrom.Value, address);
            grab.engine = level.engine;
            var aeroWorld = level.root.GetComponentInChildren<AeroWorld>(); if (aeroWorld) aeroWorld.viewer = player.transform;
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = level.start + Vector3.up * 0.05f;
            cc.enabled = true;
            fpc.SetLook(level.startYaw, 0);
            if (!menuBackground) hud.EnterPlace(address.ToString(), PlaceName(level.aesthetic, address.Generation));
            entities.Clear();
            if (!menuBackground) SpawnEntities();
            Address.Interact.InteractBuilder.Attach(level);
            Debug.Log($"[ADDRESS] entered {address}{(level.rare != "" ? " RARE:" + level.rare : "")}: rooms {level.rooms.Count}, objects {level.engine.placed.Count}, exits {level.exits.Count} ({string.Join(", ", level.exits.Select(e => e.form + "/" + e.role + (e.reachable ? "" : "!UNREACHABLE")))}), echo [{string.Join("; ", echoLog)}]");
            if (snap != null) { fade.color = new Color(0, 0, 0, 0); yield return hud.DissolveSnapshot(1.8f); Destroy(snap); }
            else yield return Fade(0f, 1.2f);
            travelling = false;
        }

        // ------------------------------------------------------------------ co-op

        void StartHosting()
        {
            NetLauncher.Ensure(config.netAvatarPrefab, config.netSessionPrefab);
            if (!NetLauncher.Host(config.netSessionPrefab)) { Debug.LogWarning("[ADDRESS] host failed"); return; }
            WireSession();
            if (state == State.Menu) Continue(); else if (level != null) StartCoroutine(Enter(level.address, null));
        }

        /// <summary>Co-op over Steam: a friends-only lobby, the friend joins through the overlay invite (Steam relay, no IP).</summary>
        void StartSteamHosting()
        {
            NetLauncher.Ensure(config.netAvatarPrefab, config.netSessionPrefab);
            Address.Net.SteamService.HostLobby(() =>
            {
                if (!NetLauncher.Host(config.netSessionPrefab)) return false;
                WireSession();
                if (state == State.Menu) Continue(); else if (level != null) StartCoroutine(Enter(level.address, null));
                return true;
            });
        }

        void JoinSession(string ip)
        {
            NetLauncher.Ensure(config.netAvatarPrefab, config.netSessionPrefab);
            if (!NetLauncher.Join(ip)) { Debug.LogWarning("[ADDRESS] join failed"); return; }
            StartCoroutine(WaitSession());
        }

        IEnumerator WaitSession()
        {
            float t = 0;
            while (NetSession.Instance == null && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
            if (NetSession.Instance == null) { Debug.LogWarning("[ADDRESS] no session from host"); yield break; }
            WireSession();
            menu.Show(false, false); state = State.Playing; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; fpc.enabled = true; grab.enabled = true;
            var a = NetSession.Instance.Address.Value.ToString();
            if (a.Length > 0 && LevelAddress.TryParse(a, out var la)) StartCoroutine(Enter(la, null));
        }

        void WireSession()
        {
            var sess = NetSession.Instance;
            if (sess == null) return;
            sess.onAddress = a => { if (!NetLauncher.Manager.IsServer && LevelAddress.TryParse(a, out var la) && (level == null || !level.address.Equals(la))) StartCoroutine(Enter(la, level?.address)); };
            grab.onObjectMoved = po => { if (NetSession.Instance != null && po) NetSession.Instance.ObjectPoseRpc(po.uid, po.transform.position, po.transform.rotation); };
            sess.onObjectPose = (uid, pos, rot) =>
            {
                var po = level?.engine.placed.FirstOrDefault(x => x && x.uid == uid);
                if (!po) return;
                var rb = po.GetComponent<Rigidbody>();
                if (rb) { rb.isKinematic = true; rb.position = pos; rb.rotation = rot; }
                po.transform.SetPositionAndRotation(pos, rot);
                remoteMoved = uid;
            };
            sess.onEntityPose = (i, pos, yaw, mode) =>
            {
                if (i < 0 || i >= entities.Count || !entities[i]) return;
                var e = entities[i]; e.puppet = true; e.mode = (Address.Entities.Stalker.Mode)mode; e.netPos = pos; e.netYaw = yaw; e.hasNet = true;
            };
            sess.onTravelRequest = (idx, client) => { var e = level?.exits.FirstOrDefault(x => x.index == idx); if (e?.instance) { travelling = false; Travel(e.instance); } };
            StartCoroutine(BindAvatar());
        }

        IEnumerator BindAvatar()
        {
            while (NetAvatar.Local == null) yield return null;
            NetAvatar.Local.follow = player.transform;
        }

        IEnumerator NetHostTest()
        {
            yield return new WaitUntil(() => !travelling && level != null);
            StartHosting();
            yield return new WaitForSeconds(1f); yield return new WaitUntil(() => !travelling);
            float t = 0; while (NetLauncher.Manager.ConnectedClientsIds.Count < 2 && t < 90f) { t += Time.deltaTime; yield return null; }
            Debug.Log($"[ADDRESS] net host: clients {NetLauncher.Manager.ConnectedClientsIds.Count}, level {level.address} hash {LevelBuilder.StructuralHash(level)}");
            // give the client time to build the level, then walk a circle so it can see the avatar move
            yield return new WaitForSeconds(6f);
            var c = player.transform.position;
            for (float a = 0; a < 12f; a += Time.deltaTime) { var cc = player.GetComponent<CharacterController>(); cc.enabled = false; player.transform.position = c + new Vector3(Mathf.Cos(a) * 1.2f, 0, Mathf.Sin(a) * 1.2f); cc.enabled = true; yield return null; }
            // move a world object the way the hands do and report it
            var box = level.engine.placed.FirstOrDefault(p => p && !p.intentionalAnomaly && p.profile.massClass != Address.Placement.MassClass.Fixed);
            if (box)
            {
                box.transform.position += new Vector3(0.5f, 0, 0.3f);
                for (int i = 0; i < 5; i++) { grab.onObjectMoved?.Invoke(box); yield return new WaitForSeconds(0.1f); }
                Debug.Log($"[ADDRESS] net host: moved object uid {box.uid} {box.name} to {box.transform.position}");
            }
            yield return new WaitForSeconds(2f);
            var exit = level.exits.First(e => e.role == ExitRole.Forward && e.reachable);
            exit.instance.Activate(); yield return new WaitForSeconds(2.5f);
            travelling = false; Travel(exit.instance);
            yield return new WaitForSeconds(1f); yield return new WaitUntil(() => !travelling);
            Debug.Log($"[ADDRESS] net host: travelled to {level.address} hash {LevelBuilder.StructuralHash(level)}");
            yield return new WaitForSeconds(12f);
            Debug.Log("[ADDRESS] net host OK"); NetLauncher.Stop(); Application.Quit();
        }

        IEnumerator NetClientTest()
        {
            yield return new WaitUntil(() => !travelling && level != null);
            yield return new WaitForSeconds(3f);
            JoinSession("127.0.0.1");
            float t = 0; while ((NetSession.Instance == null || travelling) && t < 60f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(2f); yield return new WaitUntil(() => !travelling);
            Debug.Log($"[ADDRESS] net client: in {level?.address}, host hash {NetSession.Instance?.LevelHash.Value}, mine {LevelBuilder.StructuralHash(level)}");
            NetAvatar remote = null; Vector3 p0 = default; float moved = 0;
            for (float s = 0; s < 14f; s += Time.deltaTime)
            {
                remote ??= FindObjectsByType<NetAvatar>(FindObjectsSortMode.None).FirstOrDefault(a => !a.IsOwner);
                if (remote) { if (p0 == default) p0 = remote.transform.position; moved = Mathf.Max(moved, (remote.transform.position - p0).magnitude); }
                yield return null;
            }
            Debug.Log($"[ADDRESS] net client: remote avatar {(remote ? "seen" : "MISSING")}, moved {moved:F2} m");
            t = 0; while (remoteMoved < 0 && t < 10f) { t += Time.deltaTime; yield return null; }
            var mv = level.engine.placed.FirstOrDefault(p => p && p.uid == remoteMoved);
            Debug.Log($"[ADDRESS] net client: object moved by host: {(mv ? $"uid {mv.uid} {mv.name} now at {mv.transform.position}" : "NOT RECEIVED")}");
            var before = level.address;
            t = 0; while ((level.address.Equals(before) || travelling) && t < 40f) { t += Time.deltaTime; yield return null; }
            var snap = level.engine.placed.Where(p => p).ToDictionary(p => p, p => (p.transform.position, p.transform.rotation));
            yield return new WaitForSeconds(1f);
            foreach (var kv in snap.Where(kv => kv.Key && ((kv.Key.transform.position - kv.Value.position).sqrMagnitude > 1e-6f || Quaternion.Angle(kv.Key.transform.rotation, kv.Value.rotation) > 0.1f)).Take(6))
                Debug.Log($"[ADDRESS] net client: {kv.Key.name} [{kv.Key.placedBy}] moved {kv.Value.position} -> {kv.Key.transform.position} rb={(kv.Key.GetComponent<Rigidbody>() != null)}");
            Debug.Log($"[ADDRESS] net client: missing after 1s: {snap.Count(kv => !kv.Key)}, count now {level.engine.placed.Count(p => p)} vs {snap.Count}");
            Debug.Log($"[ADDRESS] net client: followed host to {level.address}: hash {(LevelBuilder.StructuralHash(level) == NetSession.Instance?.LevelHash.Value.ToString() ? "SAME PLACE" : "MISMATCH")}");
            Debug.Log("[ADDRESS] net client OK"); Application.Quit();
        }

        /// <summary>The kind of exit hints at the next place: vents and lifts lead to water more often, plain doors stay dry.</summary>
        static string NextAesthetic(ExitForm form, string current, ulong seed)
        {
            float pr = form == ExitForm.VentHatch ? 0.5f : form == ExitForm.Elevator ? 0.3f : 0.15f;
            float fa = form == ExitForm.Elevator ? 0.35f : form == ExitForm.HandlelessDoor ? 0.15f : 0.1f;
            if (current == "PR") pr += 0.15f;   // the water pulls you in
            if (current == "FA") fa += 0.15f;
            // lifts go down into the garage, plain doors open onto hotel corridors
            float pg = form == ExitForm.Elevator ? 0.2f : form == ExitForm.VentHatch ? 0.1f : 0.08f;
            float ht = form == ExitForm.HandlelessDoor ? 0.18f : form == ExitForm.Elevator ? 0.12f : 0.05f;
            if (current == "PG") pg += 0.1f; if (current == "HT") ht += 0.1f;
            // handleless doors open onto office floors; vents drop you into Level Fun
            float of = form == ExitForm.HandlelessDoor ? 0.12f : 0.06f, lf = form == ExitForm.VentHatch ? 0.08f : 0.04f;
            if (current == "OF") of += 0.08f;
            // vents lead down into the pipes, lifts to the warehouse; any door may open onto pitch black
            float pd = form == ExitForm.VentHatch ? 0.1f : 0.04f, wh = form == ExitForm.Elevator ? 0.1f : 0.05f, lo = 0.02f;
            // vents into the power station and the caves, lifts to the Async lab, plain doors into the library; Level Run can open anywhere
            // the frightening ones are rare finds
            float el = form == ExitForm.VentHatch ? 0.03f : 0.015f, cv = form == ExitForm.VentHatch ? 0.02f : 0.008f, rn = 0.008f;
            float fl = current == "PR" ? 0.08f : 0.04f, lb = form == ExitForm.HandlelessDoor ? 0.06f : 0.03f, la = form == ExitForm.Elevator ? 0.08f : 0.03f;
            if (current == "LA") la += 0.06f; if (current == "LB") lb += 0.06f;
            // the older odds already fill most of the range: scale them so the new worlds fit and plain yellow rooms stay common
            float sc = 0.78f; pr *= sc; fa *= sc; pg *= sc; ht *= sc; of *= sc; lf *= sc; pd *= sc; wh *= sc; lo *= sc;
            // worlds described as data (built-in Resources/Worlds and mods) each take their own small share first
            float xd = ((seed >> 20) % 1000) / 1000f, acc = 0;
            foreach (var wd in Address.Mods.ModSystem.Worlds.OrderBy(w => w.id))
            {
                if (Address.Mods.ModSystem.World(wd.id) == null) continue;   // its mod is switched off
                acc += Mathf.Clamp(wd.weight, 0f, 0.2f);
                if (xd < acc) return wd.id;
            }
            float x = (seed % 1000) / 1000f;
            float a = pr, b = a + fa, c = b + pg, d = c + ht, e = d + of, f = e + lf, g = f + pd, h = g + wh, i = h + lo;
            float j = i + el, k2 = j + cv, l2 = k2 + rn, m2 = l2 + fl, n2 = m2 + lb, o2 = n2 + la;
            return x < a ? "PR" : x < b ? "FA" : x < c ? "PG" : x < d ? "HT" : x < e ? "OF" : x < f ? "LF" : x < g ? "PD" : x < h ? "WH" : x < i ? "LO"
                 : x < j ? "EL" : x < k2 ? "CV" : x < l2 ? "RN" : x < m2 ? "FL" : x < n2 ? "LB" : x < o2 ? "LA" : "BR";
        }

        void TakePhoto()
        {
            var (score, subjects) = PhotoScoring.Score(cam, level);
            // a photo of the right wall in a "photo" place develops the exit that the eye cannot see
            if (level.rare == "photo")
                foreach (var e in level.exits)
                    if (e.instance && e.instance.Hidden)
                    {
                        var vp = cam.WorldToViewportPoint(e.position + Vector3.up);
                        bool inFrame = vp.z > 0 && vp.z < 9f && vp.x > 0.2f && vp.x < 0.8f && vp.y > 0.15f && vp.y < 0.85f;
                        bool facing = Vector3.Dot(cam.transform.forward, -e.normal) > 0.5f;
                        if (inFrame && facing) { e.instance.SetHidden(false); score += 500; subjects.Add("exit:revealed"); Debug.Log("[ADDRESS] photo revealed the hidden exit"); }
                    }
            var file = PhotoAlbum.NewFile();
            ScreenCapture.CaptureScreenshot(file);
            album.photos.Add(new PhotoRecord { file = file, address = level.address.ToString(), date = System.DateTime.Now.ToString("s"), score = score, subjects = subjects });
            album.Save();
            audioDir?.Play(config.audio?.shutterReal ? config.audio.shutterReal : config.audio?.shutter, 0.8f);
            flash = 1f; hud.Toast(score > 0 ? $"+{score}" : "снимок", score >= 300 ? "редкий кадр · сохранён в архив" : "сохранён в архив");
            Debug.Log($"[ADDRESS] photo {score} [{string.Join(", ", subjects)}] -> {file}");
        }

        void Travel(ExitTrigger exit)
        {
            if (travelling) return;
            if (NetSession.Instance != null && NetLauncher.Manager.IsClient && !NetLauncher.Manager.IsServer)
            {
                if (Time.time - lastRequest > 2f) { lastRequest = Time.time; NetSession.Instance.RequestTravelRpc(exit.plan.index); }
                return;
            }
            var cur = level.address;
            // a vent is a real shaft: crawl through it first (tests that jump through exits skip it)
            if (exit.plan.form == ExitForm.VentHatch && exit.plan.role != ExitRole.False && !ductDone && state == State.Playing && !Application.isBatchMode)
            { StartCoroutine(DuctRoutine(exit)); return; }
            switch (exit.plan.role)
            {
                case ExitRole.Forward:
                    audioDir?.Play(config.audio?.travel, 0.3f);
                    ulong ns = Hashing.Combine(cur.Seed, (ulong)(exit.plan.index + 1) * 0x51ED27UL);
                    // the park's tiled archways lead straight down into the Poolrooms, without a door or a black screen
                    bool arch = exit.plan.form == ExitForm.Archway;
                    var next = book.NextFree(arch ? "PR" : NextAesthetic(exit.plan.form, cur.Aesthetic, ns), ns);
                    StartCoroutine(Enter(next, cur, false, arch)); break;
                case ExitRole.Loop:
                    audioDir?.Play(config.audio?.travel, 0.3f);
                    StartCoroutine(Enter(cur.NextEcho(), cur)); break;
                case ExitRole.False:
                    break; // nothing behind it - the player walks into a wall
            }
        }

        /// <summary>Co-op colours: the host is yellow, the second explorer blue (own body and arms, and as seen by the other).</summary>
        void Suit(GameObject go, bool blue)
        {
            if (!go || !config.suitYellow || !config.suitBlue) return;
            var want = blue ? config.suitBlue : config.suitYellow;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterials; bool ch = false;
                for (int i = 0; i < m.Length; i++) if ((m[i] == config.suitYellow || m[i] == config.suitBlue) && m[i] != want) { m[i] = want; ch = true; }
                if (ch) r.sharedMaterials = m;
            }
        }

        void TickSuits()
        {
            suitTimer -= Time.deltaTime; if (suitTimer > 0) return; suitTimer = 0.5f;
            bool meBlue = NetLauncher.Manager && NetLauncher.Manager.IsClient && !NetLauncher.Manager.IsServer;
            Suit(playerBody, meBlue); if (fpArms) Suit(fpArms.Rig, meBlue);
            avatars = NetLauncher.Manager ? FindObjectsByType<NetAvatar>(FindObjectsSortMode.None) : System.Array.Empty<NetAvatar>();
            foreach (var a in avatars) if (a && !a.IsOwner && a.visual) Suit(a.visual, a.OwnerClientId != NetworkManager.ServerClientId);
        }

        void Update()
        {
            TickEntitiesNet();
            TickSuits();
            TickFear();
            if (deviceInput != null) { deviceInput.mouseSensitivity = Settings.Sensitivity; deviceInput.invertY = Settings.InvertY; }
            cam.fieldOfView = Settings.Fov;
            if (!AudioListener.pause) AudioListener.volume = Settings.Sound ? 1f : 0f;   // test runs keep the listener paused and silent
            Address.World.Flicker.Enabled = Settings.FlickerOn;
            if (touch) touch.gameObject.SetActive(state == State.Playing && computer == null);
            if (state == State.Menu)
            {
                // slow look-around of the menu room
                menuYaw += Time.deltaTime * 3f;
                fpc.SetLook(menuYaw, 2f + Mathf.Sin(Time.time * 0.2f) * 3f);
                hud.Aim(false);
                return;
            }
            if (state == State.Paused) { if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !menu.EscConsumed) Resume(); return; }
            if (touch && touch.PausePressed) { Pause(); return; }
            if (computer != null) return;   // the computer screen has the input (Esc closes it)
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Pause(); return; }
            if (travelling || level == null) { hud.Aim(false); return; }
            TickRitual(); TickCheats();
            // interaction with exits (E): only when aiming at one within reach
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            ExitTrigger aimed = null;
            if (Physics.Raycast(ray, out var hit, 2.0f, 1 << LevelBuilder.ArchLayer, QueryTriggerInteraction.Ignore))
                aimed = hit.collider.GetComponentInParent<ExitTrigger>();
            TickPockets();
            bool exitAim = aimed && !aimed.IsOpen;
            if (TickVerity()) { }
            else if (TickInteract(ray, exitAim)) { }
            else if (grab.Held) hud.Aim(false, "ЛКМ", "положить     ПКМ — бросить     R — вращать");
            else if (grab.Pushing) hud.Aim(false, "ЛКМ", "отпустить");
            else if (aimed && !aimed.IsOpen) hud.Aim(true, "E", "открыть");
            else if (grab.Aimed && grab.Aimed.profile)
            {
                var mc = grab.Aimed.profile.massClass;
                if (mc == Address.Placement.MassClass.Fixed || grab.Aimed.profile.support == Address.Placement.SupportType.Wall || grab.Aimed.profile.support == Address.Placement.SupportType.Ceiling) hud.Aim(false);
                else hud.Aim(true, "ЛКМ", mc == Address.Placement.MassClass.Heavy ? "толкать" : "взять");
            }
            else hud.Aim(false);
            if (exitAim && EPressed())
            {
                aimed.Activate();
                var clip = aimed.plan.form == ExitForm.Elevator ? config.audio?.elevator : aimed.plan.form == ExitForm.VentHatch ? config.audio?.vent : (config.audio?.doorReal ? config.audio.doorReal : config.audio?.door);
                audioDir?.PlayAt(clip, aimed.transform.position + Vector3.up, 0.9f);
            }
            if (input.PhotoPressed) TakePhoto();
            if (flash > 0) { flash -= Time.deltaTime * 2.5f; fade.color = new Color(1, 1, 1, Mathf.Max(0, flash) * 0.5f); }
            TickIndex();
            foreach (var e in level.exits)
                if (e.instance && e.instance.Contains(fpc.transform.position + Vector3.up * 0.5f)) { Travel(e.instance); break; }
            if (fpc.transform.position.y < -5f)   // fell out of the world: that is cheating too
            {
                Strike("выпал из мира");
                if (CheatStrikes > 0 && CheatStrikes < 3) StartCoroutine(Enter(book.NextFree("BR", Hashing.Combine(level.address.Seed, 0xC0FFEEUL)), level.address)); // noclip
            }
        }

        // ------------------------------------------------------------------ player, HUD, lighting

        void BuildPlayer()
        {
            player = new GameObject("Player") { layer = 11 };
            player.AddComponent<CharacterController>();
            fpc = player.AddComponent<FirstPersonController>();
            if (TouchInput.IsPhone) { var tgo = new GameObject("Touch"); tgo.transform.SetParent(transform, false); touch = tgo.AddComponent<TouchInput>(); input = touch; }
            else { deviceInput = new DeviceInput(); input = deviceInput; }
            fpc.input = input;
            var head = new GameObject("Head"); head.transform.SetParent(player.transform, false);
            fpc.head = head.transform;
            head.tag = "MainCamera";
            cam = head.AddComponent<Camera>(); cam.fieldOfView = 72; cam.nearClipPlane = 0.03f; cam.farClipPlane = Application.isMobilePlatform ? 60f : 120f;
            var data = head.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            head.AddComponent<AudioListener>();
            fpc.Init();
            grab = player.AddComponent<GrabSystem>();
            grab.cam = cam; grab.fpc = fpc; grab.input = input; grab.ghostOk = config.ghostOk; grab.ghostBad = config.ghostBad;
            grab.Init();
            fpArms = head.AddComponent<FpArms>(); fpArms.cam = cam; fpArms.grab = grab; fpArms.fpc = fpc;
            fpArms.Init(config.bodyPrefab);
            Physics.IgnoreLayerCollision(12, 11, true);
            fearFx = player.AddComponent<FearFx>(); fearFx.head = head.transform; fearFx.Init();
            audioDir = player.AddComponent<AudioDirector>();
            if (config.audio != null) audioDir.Init(config.audio, fpc);
            if (config.bodyPrefab)
            {
                var body = Instantiate(config.bodyPrefab, player.transform); playerBody = body;
                body.transform.localPosition = Vector3.zero; body.transform.localRotation = Quaternion.identity;
                foreach (var t in body.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 11;
                foreach (var c in body.GetComponentsInChildren<Collider>(true)) Destroy(c);
                var pb = body.GetComponent<ProceduralBody>(); if (pb) { pb.fpc = fpc; pb.grab = grab; pb.firstPerson = true; }
            }
        }

        // per-world colour grade on top of the base post profile
        Volume gradeVol; ColorAdjustments gradeCol; WhiteBalance gradeWb;
        void Grade(string a)
        {
            if (!gradeVol)
            {
                var go = new GameObject("WorldGrade"); go.transform.SetParent(transform, false);
                gradeVol = go.AddComponent<Volume>(); gradeVol.isGlobal = true; gradeVol.priority = 5;
                var p = ScriptableObject.CreateInstance<VolumeProfile>(); gradeVol.sharedProfile = p;
                gradeCol = p.Add<ColorAdjustments>(true); gradeWb = p.Add<WhiteBalance>(true);
            }
            var wd = Address.Mods.ModSystem.World(a);
            if (wd != null)
            {
                gradeCol.colorFilter.Override(Address.Mods.ModSystem.C(wd.gradeFilter, Color.white)); gradeCol.saturation.Override(wd.gradeSaturation);
                gradeCol.contrast.Override(wd.gradeContrast); gradeCol.postExposure.Override(wd.gradeExposure); gradeWb.temperature.Override(wd.gradeTemperature);
                ShaderPackFx.Apply(transform);
                return;
            }
            (Color f, float sat, float con, float exp, float temp) g = a switch
            {
                "BR" => (new Color(1f, 0.95f, 0.8f), -10f, 14f, 0f, 14f),      // sickly yellow
                "PR" => (new Color(0.92f, 1f, 1f), -6f, 8f, 0.1f, -10f),
                "LO" => (new Color(0.9f, 0.92f, 1f), -35f, 28f, -0.2f, -6f),
                "CV" => (new Color(1f, 0.92f, 0.85f), -30f, 26f, -0.15f, 4f),
                "RN" => (new Color(1f, 0.86f, 0.86f), -12f, 24f, 0f, 0f),
                "FL" => (new Color(0.88f, 1f, 0.94f), -18f, 14f, 0f, -8f),
                "LA" => (new Color(0.95f, 0.98f, 1f), -22f, 16f, 0.1f, -16f),
                "LB" => (new Color(1f, 0.9f, 0.78f), -12f, 14f, -0.05f, 12f),
                "EL" => (new Color(1f, 0.9f, 0.88f), -20f, 22f, -0.1f, 0f),
                "PD" => (new Color(1f, 0.9f, 0.78f), -10f, 18f, 0f, 10f),
                "HT" => (new Color(1f, 0.9f, 0.8f), -8f, 14f, 0f, 10f),
                "OF" => (new Color(0.94f, 0.98f, 1f), -20f, 12f, 0f, -8f),
                "WH" => (new Color(0.95f, 0.98f, 1f), -18f, 14f, 0f, -6f),
                "PG" => (new Color(0.92f, 0.98f, 1f), -18f, 16f, -0.05f, -8f),
                _ => (Color.white, 0f, 0f, 0f, 0f),
            };
            gradeCol.colorFilter.Override(g.f); gradeCol.saturation.Override(g.sat); gradeCol.contrast.Override(g.con); gradeCol.postExposure.Override(g.exp);
            gradeWb.temperature.Override(g.temp);
            ShaderPackFx.Apply(transform);
        }

        void SetupLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.13f, 0.12f, 0.08f);
            RenderSettings.ambientEquatorColor = new Color(0.20f, 0.18f, 0.11f);
            RenderSettings.ambientGroundColor = new Color(0.46f, 0.42f, 0.30f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.022f;
            RenderSettings.fogColor = new Color(0.42f, 0.38f, 0.24f);
            if (config.post) { var v = new GameObject("PostFX").AddComponent<Volume>(); v.isGlobal = true; v.sharedProfile = config.post; }
        }

        // ------------------------------------------------------------------ entities

        readonly List<Address.Entities.Stalker> entities = new();
        float entitySync;
        public bool forceEntity;   // tests

        // ------------------------------------------------------------------ Verity and the index

        readonly List<Address.Entities.Verity> verities = new();
        FearFx fearFx;

        /// <summary>How afraid the explorer should be: the nearest thing that hunts them, closer = worse; looking at it, worse still.</summary>
        void TickFear()
        {
            if (!fearFx) return;
            float f = 0;
            if (state == State.Playing && !travelling && level != null)
            {
                foreach (var e in entities)
                {
                    if (!e || e.passive) continue;
                    float d = (e.transform.position - cam.transform.position).magnitude;
                    bool hunting = e.mode == Address.Entities.Stalker.Mode.Stalk || e.mode == Address.Entities.Stalker.Mode.Frozen;
                    float k = Mathf.Clamp01(1f - (d - 2.5f) / (hunting ? 16f : 7f)) * (hunting ? 1f : 0.35f);
                    if (e.SeenNow) k *= 1.15f;
                    f = Mathf.Max(f, k);
                }
                var a = level.aesthetic; if (a == "LO" || a == "CV" || a == "RN") f = Mathf.Max(f, 0.12f);   // never quite at ease there
            }
            fearFx.target = f;
        }
        public bool forceVerity;   // tests
        float indexT;

        /// <summary>Verity close and in view: "E поговорить". Returns true when it took the prompt this frame.</summary>
        bool TickVerity()
        {
            Address.Entities.Verity near = null; float bd = 3.6f;
            foreach (var v in verities)
            {
                if (!v) continue;
                var d = v.transform.position + Vector3.up * 0.35f - cam.transform.position;
                if (d.magnitude < bd && Vector3.Angle(cam.transform.forward, d) < 32f) { bd = d.magnitude; near = v; }
            }
            if (!near) return false;
            hud.Aim(true, "E", near.Talking ? "дальше" : "поговорить с Verity");
            if (EPressed()) near.Talk();
            return true;
        }

        /// <summary>Whatever the explorer actually sees goes into the index.</summary>
        void TickIndex()
        {
            indexT -= Time.deltaTime; if (indexT > 0) return; indexT = 0.3f;
            foreach (var e in entities)
                if (e && (e.transform.position - cam.transform.position).sqrMagnitude < 24f * 24f && LooksAt(e)(cam.transform)) Address.Entities.Bestiary.SeeCreature(e.IndexKey);
            int mask = (1 << LevelBuilder.ArchLayer) | (1 << LevelBuilder.PropsLayer);
            foreach (var v in verities)
            {
                if (!v) continue;
                var p = v.transform.position + Vector3.up * 0.35f; var d = p - cam.transform.position; var vp = cam.WorldToViewportPoint(p);
                if (d.magnitude < 14f && vp.z > 0 && vp.x > 0.05f && vp.x < 0.95f && vp.y > 0.05f && vp.y < 0.95f && !Physics.Raycast(cam.transform.position, d.normalized, d.magnitude - 0.3f, mask, QueryTriggerInteraction.Ignore))
                    Address.Entities.Bestiary.SeeCreature(v.Key);
            }
        }

        void SpawnEntities()
        {
            entities.AddRange(Address.Entities.EntitySpawner.Spawn(level, config.bodyPrefab, Address.Entities.EntitySpawner.Drone, forceEntity));
            verities.Clear();
            verities.AddRange(Address.Entities.Verity.SpawnFor(level, forceVerity));
            foreach (var v in verities) { v.viewer = cam.transform; v.monsters = () => entities; v.say = (who, line, sec) => hud.Say(who, line, sec); }
            if (verities.Count > 0) Debug.Log($"[ADDRESS] verity: {string.Join(", ", verities.Select(v => v.Key + " " + v.transform.position.ToString("F1")))}");
            bool client = NetLauncher.Manager && NetLauncher.Manager.IsClient && !NetLauncher.Manager.IsServer;
            foreach (var e in entities)
            {
                e.puppet = client;
                e.targets = EntityTargets;
                e.isLookedAtBy = LooksAt(e);
                e.onCatch = _ => Caught(e);
            }
            if (entities.Count > 0) Debug.Log($"[ADDRESS] entities: {entities.Count} at {string.Join(", ", entities.Select(x => x.transform.position.ToString("F1")))}");
        }

        readonly List<Transform> targetBuf = new(); NetAvatar[] avatars = System.Array.Empty<NetAvatar>();
        List<Transform> EntityTargets()
        {
            var l = targetBuf; l.Clear();
            if (state == State.Playing && !travelling) l.Add(cam.transform);
            foreach (var a in avatars) if (a && !a.IsOwner && a.IsSpawned) l.Add(a.transform);   // cached list (scene scans every frame cost hitches)
            return l;
        }

        System.Func<Transform, bool> LooksAt(Address.Entities.Stalker e) => viewer =>
        {
            int mask = (1 << LevelBuilder.ArchLayer) | (1 << LevelBuilder.PropsLayer);
            Vector3 eye, fwd; bool local = viewer == cam.transform;
            if (local) { eye = cam.transform.position; fwd = cam.transform.forward; }
            else { eye = viewer.position + Vector3.up * 1.6f; fwd = viewer.forward; }
            foreach (var h in new[] { 1.85f, 1.2f, 0.5f })
            {
                var p = e.transform.position + Vector3.up * h; var d = p - eye;
                if (d.magnitude > 40f) return false;
                if (local) { var vp = cam.WorldToViewportPoint(p); if (vp.z <= 0 || vp.x < -0.02f || vp.x > 1.02f || vp.y < -0.02f || vp.y > 1.02f) continue; }
                else if (Vector3.Angle(new Vector3(fwd.x, 0, fwd.z), new Vector3(d.x, 0, d.z)) > 50f) continue;
                if (!Physics.Raycast(eye, d.normalized, d.magnitude - 0.25f, mask, QueryTriggerInteraction.Ignore)) return true;
            }
            return false;
        };

        void Caught(Address.Entities.Stalker e)
        {
            if (GodMode) return;
            if (travelling || level == null) return;
            Debug.Log($"[ADDRESS] caught by entity at {e.transform.position:F1} -> echo of {level.address}");
            StartCoroutine(CaughtSequence(e));
        }

        IEnumerator CaughtSequence(Address.Entities.Stalker e)
        {
            travelling = true; fpc.enabled = false; grab.enabled = false;
            audioDir?.Play(Address.Entities.EntitySpawner.Hit, 1f);
            // turn to face it, the screen tears, then black
            var headPos = e.transform.position + Vector3.up * 1.9f;
            var look = headPos - cam.transform.position;
            float yaw0 = fpc.transform.eulerAngles.y, yaw1 = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, pitch1 = -Mathf.Asin(Mathf.Clamp(look.normalized.y, -1, 1)) * Mathf.Rad2Deg;
            for (float t = 0; t < 0.35f; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0, 1, t / 0.35f);
                fpc.SetLook(Mathf.LerpAngle(yaw0, yaw1, k), Mathf.Lerp(0, pitch1, k));
                fade.color = new Color(0.25f, 0, 0, Random.value * 0.35f * k);
                yield return null;
            }
            for (float t = 0; t < 0.6f; t += Time.deltaTime) { fade.color = Random.value < 0.5f ? new Color(0, 0, 0, 0.9f) : new Color(0.35f, 0.02f, 0.02f, 0.4f); yield return null; }
            fade.color = Color.black;
            fpc.enabled = state == State.Playing; grab.enabled = state == State.Playing;
            var from = level.address;
            yield return Enter(from.NextEcho(), from);
            hud.Toast("ты проснулся", $"эхо {from.Generation + 1} этого места");
        }

        void TickEntitiesNet()
        {
            if (NetSession.Instance == null || !NetLauncher.Manager || !NetLauncher.Manager.IsServer || entities.Count == 0) return;
            entitySync -= Time.deltaTime; if (entitySync > 0) return; entitySync = 0.1f;
            for (int i = 0; i < entities.Count; i++)
                if (entities[i]) NetSession.Instance.EntityPoseRpc(i, entities[i].transform.position, entities[i].transform.eulerAngles.y, (byte)entities[i].mode);
        }

        void BuildHud()
        {
            hud = new Hud(); fade = hud.fade; hud.SetVisible(false);
        }

        static string PlaceName(string aesthetic, int generation)
        {
            var wd = Address.Mods.ModSystem.World(aesthetic);
            if (wd != null) return generation > 0 ? $"{wd.title} · эхо {generation}" : wd.title;
            string n = aesthetic switch { "BR" => "Жёлтые комнаты", "PR" => "Бассейны", "FA" => "Frutiger Aero", "PG" => "Подземная парковка", "HT" => "Бесконечный отель", "OF" => "Заброшенный офис", "LF" => "Уровень Веселья =)", "PD" => "Трубы", "WH" => "Склад", "LO" => "Без света", "EL" => "Электростанция", "CV" => "Пещеры", "RN" => "Беги!", "FL" => "Затопленные комнаты", "LB" => "Библиотека", "LA" => "Лаборатория Async", _ => aesthetic };
            return generation > 0 ? $"{n} · эхо {generation}" : n;
        }

        static Image NewImage(Transform p, string n, Vector2 size, Color c)
        {
            var go = new GameObject(n); go.transform.SetParent(p, false);
            var img = go.AddComponent<Image>(); img.color = c; img.rectTransform.sizeDelta = size; img.raycastTarget = false;
            return img;
        }

        static Text NewText(Transform p, string n, Font f, int size, Vector2 pos, TextAnchor anchor)
        {
            var go = new GameObject(n); go.transform.SetParent(p, false);
            var t = go.AddComponent<Text>(); t.font = f; t.fontSize = size; t.alignment = anchor; t.color = new Color(1, 1, 1, 0.8f);
            t.rectTransform.anchoredPosition = pos; t.rectTransform.sizeDelta = new Vector2(300, 30); t.raycastTarget = false;
            return t;
        }

        IEnumerator Fade(float to, float time)
        {
            float from = fade.color.a;
            for (float t = 0; t < time; t += Time.unscaledDeltaTime) { fade.color = new Color(0, 0, 0, Mathf.Lerp(from, to, t / time)); yield return null; }
            fade.color = new Color(0, 0, 0, to);
        }
    }
}
