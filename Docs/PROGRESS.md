# ADDRESS — progress log (read this first when resuming)

## How to run things
- Unity 6000.4.6f1, project `C:\Users\aurel\ADDRESS\Game` (URP 17.4, glTFast 6.21).
- Batch helper: `powershell -File Tools/unity.ps1 -Method <Class.Method> -Extra "<args>"` → log in `Docs/reports/logs/<Method>.log`, prints compile errors + `[ADDRESS]` lines.
- Assets: `python -I Tools/fetch_assets.py` (CC0 Poly Haven/ambientCG from manifest). Semantics: edit `Tools/gen_profiles.py` → `python -I Tools/gen_profiles.py` → `Game/Assets/ADDRESS/Data/profiles.json`.
- Pipeline order after new models: `AssetTools.PrepareAll` → `ProfileBuilder.BatchBuild` → `Thumbnails.BatchRender` (Docs/reports/thumbs) → `TestFarm.Run -seeds N -overview K -failshots M` (Docs/reports/farm/report.md, failures/, overview/).
- Do not open the game as a desktop window for testing; use batch renders.

## Done (M0 + most of M1)
- Project, URP tiers Low/Medium/High/Ultra (`RenderingSetup`), layers (`ProjectSetup`), 49 CC0 models + 20 materials.
- ProfileBuilder: pivot/orientation, surface extraction (multi-hit rays), voxel compound colliders with Y-breaks exactly at surfaces, interior fill, enclosed "inside" surfaces, set splitting by spatial clusters, thin-object fallback.
- PlacementEngine: 9 checks (Style, Bounds, Navigation, Collision, Clearance zones, Surface, Support with real underside contacts + COM margin, Rotation, Semantic, Density) + physical settle on commit (undo if moves >2.5 cm).
- Scenarios: workstation (desk/chair tucked/computer/papers/stationery/drink/clock), shelf units, stacks, lounge (sofa/table/armchairs/side table/TV), wall decor, ceiling, fallen states, FloatingRing intentional anomaly.
- PlacementAudit (independent): exact penetration, underside contacts, tilt, wall/ceiling contact, 1.5 s physics settle of everything, nav reachability.
- TestFarm with report + failure screenshots + overview shots.

## Latest farm result (2026-11-18, 30 seeds)
4224 objects, 5 issues (3 Unstable, 2 Navigation pockets), 26/30 levels clean, 0 intersections, 0 floating.

## Update 2026-11-18 (later)
- 200-seed farm (seeds 1000-1199): 57 522 objects, 0 intersections, 0 floating, 23 "Unstable" (0.04%) -> OverlapTol tightened 3mm->1mm; 20-seed rerun: 6235 objects, 0 issues.
- Nav pockets: engine refuses placements that create unreachable cells (navStart).
- Density: DeskIsland (back-to-back desks), fuller shelves (items picked by what fits), keyboards in front of desktop monitors.
- Blender kit (Tools/blender/kit_backrooms.py): crt_monitor, keyboard, mug_coffee/empty, wall_outlet, light_switch, vent_grille, troffer, baseboard_1m, door_frame. Previews: Tools/blender/preview_glb.py.
- Backrooms look: generated textures (Tools/make_textures.py) BR_Wallpaper/BR_Carpet; recessed troffers with ceiling holes, spot lights down, Trilight ambient tuned (ground colour lights the ceiling).
- M3 player: FirstPersonController, GrabSystem (aim assist, spring hold, ghost green/red using engine.PlayerPlaceCheck, throw, kinematic sweep push of heavy furniture with riders), ScriptedInput.
- Automated play-test `PlayTest.Run` -> Docs/reports/playtest (frames + events.txt). Verified: pick clock from desk & place upright on floor; pick+throw chair; push desk 12 cm until blocked, pull back, 4 riders stayed on it.

## Next
1. M4: real Backrooms layout generator (room graph, openings, open-plan, mostly-empty rooms) + runtime bootstrap scene + LevelAddress use + level cache.
2. M5: exits (handleless door, elevator, vent behind cabinet) + ReachabilitySolver + transitions + AddressBook (unique addresses).
3. Character (yellow hazmat suit) in Blender; hands in first person.
4. Main menu, settings (quality tiers), archive, camera/photo scoring.

## Update 2026-11-18 (M4 + M5 core loop)
- World/LayoutGenerator: BSP rooms on 0.5 m grid, spanning tree + 35% loops, Door (1.0 m, frame) / Wide / Merge openings, wall runs without corner z-fighting, RoomSpecs with inner wall faces.
- World/LevelBuilder: thick floor slab (3 m) + ceiling with troffer holes, walls, lintels, door frames, baseboards (cut at exits), troffers + spot lights (1/4 cast shadows), NavGrid carved by physics (CarveObstacles) so paths can't cross walls, furnishing per room kind (45% empty = liminal).
- Determinism: physical settle is a VALIDATOR only (pose restored) -> rebuild of an address is bit-identical (LevelFarm checks it).
- Exits (World/Exits.cs): ExitPlanner (far/dead-end rooms, exit 0 always Forward), forms HandlelessDoor (wallpaper leaf + dark seam), Elevator (doors sink+slide), VentHatch; roles Forward/Loop/False; approach walkway reserved; reachability verified after furnishing + Repair removes blocking furniture. Blender: Tools/blender/kit_exits.py.
- Echo (World/Echo.cs): g = g-1 + one step of 3 mutations (light off, flicker, remove/shift/rotate prop via engine, darker walls). AddressBook (visited, NextFree, echo links) in persistentDataPath.
- Game/GameRoot + GameConfig + Scenes/Game.unity (GameSetup.Setup): runtime loop with fade, HUD dot + "E" hint + faint address label, noclip fallback.
- LevelFarm 40 levels: 1918 rooms, 31 746 objects, 0 audit issues, 124/124 exits reachable, deterministic.
- Journey (bot end-to-end): 6 hops loop->g1->g2->forward x3 through door/vent/elevator, all entered. Fixed: player falling through thin floor when wedged on furniture (thick floor + floor safety + stepOffset 0.2).

## Next
- Windows build check; main menu (address/phrase entry); settings (quality tiers); character (hazmat) + FP hands; photo camera + archive; door leaf wallpaper UV density; sounds (hum, steps).

## Update 2026-11-18 (build, character, menu, perf)
- Windows build: `GameSetup.BuildWindows` -> Build/ADDRESS.exe (688 MB). Headless runtime check: `Build/ADDRESS.exe -batchmode -nographics -smoketest -logFile ...` -> menu level, new path, exit, travel, pause/resume: "smoke OK", 0 exceptions. (GameConfig must live in GameConfig.cs - ScriptableObject file name rule.)
- Character: Tools/blender/character_hazmat.py (base: CC0 Blender Studio human base meshes in Art_src/downloads/hbm). Suit/hood/mask/gloves/boots/tape/zipper, Humanoid rig valid. CharacterSetup.Run -> Generated/Prefabs/HazmatBody.prefab (wrapped, rotated 180, gamma colours), ProceduralBody (procedural walk, FP offset, hood/mask shadows-only). Previews: Docs/reports/character, character_unity.
- MenuUI (Russian): Продолжить / Новый путь / Ввести адрес или фразу / Архив / Настройки (quality, sensitivity, FOV, invert) / Выход; Esc = pause. Live level background with slow look-around.
- LevelOptimizer: lights within range per tier, shadows only N nearest, props distance-culled time-sliced, architecture static-batched (exits under separate "Exits" root). Perf probe (VisualCheck, Intel Arc 1080p, synced): Low 122 / Med 82 / High 58 / Ultra 50 fps (was 14 on High).

## Next
- Photo camera + score + archive with photos; sounds (fluorescent hum, carpet steps, breathing in mask); door leaf wallpaper UV; hands animation when holding; Poolrooms + Frutiger Aero aesthetics (wave 1 remainder); co-op later.

## Update 2026-11-18 (audio + photo)
- Tools/make_sounds.py synthesises: fluorescent hum (120 Hz series + ballast buzz), room tone, 6 carpet steps, mask breathing (inhale/valve/exhale flutter), door creak, elevator chime+motor, vent clank, shutter, travel whoosh. Spectrograms checked (Docs/reports/sound_spectrograms.png).
- AudioDirector: 6 hum sources follow nearest lit fixtures (3D, detuned), room tone, steps by distance, breathing faster after running; exit sounds; travel sound.
- PhotoCamera (F): score = rarity x frame fill x framing (+exits/anomalies/echo changes), saved to persistentDataPath/photos + album.json; Archive page lists top photos + places + echo links.
- NOTE: smoke tests write to the real save (LocalLow/ADDRESS/ADDRESS) - delete address_book.json and photos/ after running them.

## Update 2026-11-18 (Poolrooms)
- Poolrooms (address aesthetic "PR"): white glazed tiles (Tiles107), basins 1.2 m deep with aqua tiles (pool_tile_aqua), 6 steps of 0.2 m, water at -0.12 (Shaders/Water.shader: refraction via opaque texture, depth absorption, Fresnel probe reflection, Forward+ light-loop speculars, edge fade), caustics = spot light cookie flipbook (Tools/make_water_textures.py, Worley F2-F1 network, CausticAnimator). Floor = holed plane + grid BoxColliders (FloorWithBasins). keepOut zones stop furniture standing in pools; nav excludes basins; no basins in exit/start rooms.
- URP assets: m_RequireDepthTexture / m_RequireOpaqueTexture = 1 (were silently off - wrong field names).
- Hard exit guarantee in LevelBuilder.Build: promote a reachable exit to Forward, else deterministic regeneration (attempt seeds). PR farm 30 levels: 84/84 exits reachable, 30/30 forward, 3 regenerations; BR farm 30 levels unchanged (0 issues, deterministic).
- GameRoot: ApplyAtmosphere per aesthetic; NextAesthetic by exit form (vent 55% PR, lift 40%, door 20%, +15% when already in PR).

## Update 2026-11-18 (Frutiger Aero + Director)
- Frutiger Aero ("FA"): Tools/blender/kit_aero.py (aero_tube water column with bubbles, aero_planter, aero_bench, aero_orb); glass swap in ProfileBuilder (materials named *glass* -> Generated/Materials/Glass.mat); FA_Floor/FA_Wall; 35% interior walls become glass partitions; atrium furnishing; cool atmosphere. FA farm 20 levels: 0 issues, 59/59 exits reachable, deterministic.
- Per-level realtime ReflectionProbe rendered once (all aesthetics).
- Perf (Arc 1080p): PR High ~84 fps, FA High ~54 fps (glass/transparency), BR High ~58.
- Director: quiet escalation at 8/13/18 min: dust draft into the forward exit, failing lamp next to it, moving-air sound.
- NextAesthetic picks BR/PR/FA by exit form.

## Update 2026-11-18 (rare places)
- Rare places (~1 in 20, rolled from the address): "silent" (forward exit hidden until the player stands still 60 s), "photo" (hidden exit is revealed by photographing its wall, +500 score), "vast" (LayoutGenerator.Vast: one enormous hall + side room). ExitTrigger.SetHidden.
- 120-level BR farm: 85 402 objects, 1 Unstable, 365/365 exits reachable, 120/120 forward; rare 2/2/2.
- Director needs config.dustMat (Shader.Find is stripped in builds - always reference materials via assets).

## Update 2026-11-18 (co-op, partially verified) - USAGE LIMIT REACHED HERE
- Netcode for GameObjects 2.11.1 + Transport 2.7.2; Net/NetLauncher (host/join port 7777, max 2), NetSession (Address + LevelHash vars, RequestTravelRpc), NetAvatar (owner pos/yaw, remote hazmat body). Menu page "Кооператив". Test: Build/ADDRESS.exe -batchmode -nographics -nethost / -netclient.
- VERIFIED (2 real processes): client connects, receives address, generates the SAME level (hash equal at entry, twice: BR then FA after host travelled through an exit).
- OPEN: (1) client's later hash recheck differs (something changes after entry - check Director/Echo/LevelOptimizer or physics on props); (2) remote avatar reported moved 0 m - host walks before client finds avatar, or NetworkVariable writes from teleport not sent; (3) moved objects are not synced yet.
- Clean LocalLow/ADDRESS saves after net tests.

## Update 2026-11-18 (co-op verified)
- Fixed: (1) StructuralHash ignores bobbing intentional anomalies; (2) player CharacterController disabled during generation - its capsule (peer-specific position) changed physical-stability results between machines -> cross-process determinism now holds; (3) test timing.
- Object sync: GrabSystem.onObjectMoved (10 Hz while held/pushed, 2.5 s after release) -> NetSession.ObjectPoseRpc(uid,...) (SendTo.NotMe, RequireOwnership=false); uid is identical on all peers.
- Two-process test (host+client, x2 runs): every level SAME PLACE, remote avatar moves, object moved by host arrives at the exact position, client follows host through exits (BR and FA).
- Still open: real LAN between two PCs not tested; hands pose while holding; entities; wave 2 aesthetics; FA perf (High ~54 fps on Arc).

## Update 2026-11-18 (60 fps on Arc)
- Breakdown probe (VisualCheck, High): shadowed spot lights were the main cost (~6 ms for 4); MSAA/post/opaque copy ~0.
- LevelOptimizer shadow casters: Low 0 / Med 1 / High 2 / Ultra 4 (was 0/2/4/6). Visually indistinguishable in BR.
- FrameGovernor: adaptive render scale (tier native -> 0.72 floor, steps 0.05) holding 60 fps; FPS counter (F3 or settings), settings toggles "Авто-разрешение", "Показывать FPS".
- Probe Arc 1080p High: BR 66, FA 66, PR 88 fps (was 58/54/84). Build rebuilt, smoke OK, save cleaned.

## Update 2026-11-18 (user played the build: fixes)
- CRITICAL: in the player build every level after the first rendered NOTHING (sky only). Cause: URP GPU Resident Drawer with runtime-built/destroyed levels. GRD off in all tiers (RenderingSetup + assets). Perf High after: BR 64 / FA 66 fps.
- CRITICAL: physics never ran in the game (DynamicsManager SimulationMode = Script, left by editor test tools). Project setting back to FixedUpdate + GameRoot forces FixedUpdate at start.
- New build-side visual test: `Build/ADDRESS.exe -batchmode -shots` (GPU, no window) -> Build/shots/*.png: menu pages, title card, 4 directions, body side views (walk/run/jump/crouch), FP look-down, grab + hands, photo toast. ALWAYS run it before handing a build over.
- Body: ProceduralBody rewritten - swings in body space (axis-safe), walk/run/jump/crouch poses, two-bone arm reach + palm twist when holding; FP body slides back when looking down (boots visible, no chest blob); neck/head/hood/mask shadow-only. Jump (Space, coyote time). Held objects carried at chest height (hold 0.68/0.9 m).
- UI: UiKit (Russo One + IBM Plex Mono, OFL, Resources/Fonts), new MenuUI (flicker+glitch title, scrambling address ticker, animated indexed buttons with sound, footer stats, Esc back), Hud (dot->ring crosshair, key-cap prompts, typed address title card, photo toast, vignette).

## Backlog from the user (2026-11-18, after playing) - work through in order
1. Body/anims: no distortion, no wrist wringing when holding, held object never enters the body when looking down (DONE: HoldPoint pitch clamp + min distance), camera never sees inside the body.
2. Grab rework: smooth/responsive, LMB always does something (DONE: drop when no valid spot), thrown items re-pickable (verify), nothing hangs in the air (floating anomalies 0.04 -> 0.008 per empty room; grabbing an anomaly makes it normal).
3. Interactions: working light switches, sit on chairs/sofas (only grounded upright ones), inspectable shelves/cabinets (search -> find items), peripherals + cables to connect to computers.
4. Computer terminal: retro desktop OS + 2007-style web browser (original names, no real brands).
5. Environment bugs: props sinking into walls (e.g. cages), wrong glass rendering.
6. Variety: more objects, new worlds (Dreamcore, Weirdcore, Bluehour), redesigned door models, entities in all worlds (DONE v1: Stalker "Отголосок").
7. Sound: replace annoying/repetitive SFX (door open, music/ambience loops).
Testing: `Tools/run_build.ps1 -Mode -playvideo` -> Build/video_frames -> Docs/videos/*.mp4 (ffmpeg from imageio-ffmpeg); review contact sheets + local qwen2.5vl (Ollama).
8. Character rework (user: "переработать персонажа").
9. Frutiger Aero: NO ceiling (open sky) and NO rooms - one open space (atrium/park/glass, no partitions); smooth door-less transition FA -> Poolrooms (open archway/water passage + crossfade from the last frame, no black fade). Make Poolrooms easy to reach.
10. Computer: REAL browser (UnityWebBrowser 2.2.8, CEF, MIT) styled 2007: default via Wayback Machine 2007 snapshots, toggle for modern web, offline fallback = WebGen Library web; normal apps (notepad, calculator, paint, mines, cmd, player, explorer); dirty CRT screen (curvature, scanlines, noise, smudges, flicker).

## Update 2026-11-18 (late morning) - done from the backlog
- FP view: FpArms (Tools/blender/fp_arms.py cuts sleeve+glove from the character -> Art/Character/FP/fp_arm_*.glb), under the camera, hands grip the held object's sides, E = reach. FP body shows only hips/legs/boots; torso/arms/mask shadow-only. Hold point pitch-clamped, never inside the body.
- Interactions (Runtime/Interact): LightSwitch (room lights), Seat (sit/stand, only upright grounded seats), Container (search -> cable/mouse/keyboard, deterministic), CablePickup, Device/ComputerStation/CableLink (tower hub, monitor/keyboard/mouse need a cable; sagging LineRenderer cable, breaks when stretched), laptop = self-contained.
- Computer: ComputerUI = BIOS boot + "КомпОС 2000" desktop (Win9x bevels, taskbar, start menu, windows, notepad, my computer, bin) rendered to RT by its own camera (must NOT be a child of the canvas) and shown through Shaders/CRT.shader (Resources/CRT.mat; curvature, 600-line pixel grid, scanlines, bleed, smudges, noise). REAL browser: UnityWebBrowser 2.2.8 (CEF) - events marshalled to main thread (else crash); "2007" mode = web.archive.org/web/2007if_/; search = html.duckduckgo.com; own 2007 start page. Offline -> WebGen Library web.
- New models: kit_tech.py (pc_tower, ball_mouse, cable_coil) + profiles; kit_exits.py v2 (panel door with lever handle; half the doors hidden-wallpaper without handle; richer elevator).
- Frutiger Aero = open park: LayoutGenerator.Park, no ceiling, sun + procedural sky (Resources/SkyAero.mat), low glass balustrade, endless plaza, ponds, only Archway exits = tiled pavilions -> Poolrooms with a crossfade from the last frame (no black fade).
- Bugs fixed: physics Script mode, GPU Resident Drawer, floating anomalies 0.04 -> 0.008, LMB always does something (drop), entity final approach.
- Tests: -shots, -playvideo (bot plays + records Build/video_frames), -computertest; Tools/run_build.ps1 kills the game after.
- STILL TODO: sound overhaul (#7), more object variety + wave-2 worlds, props sinking into walls / glass rendering check (#5), full third-person body rework, palm orientation polish, bot pathing.

## Update 2026-11-18 evening - fixes from the user's 12-min playtest video
- White screen after leaving the computer: Hud.SetVisible re-enabled the dissolve snapshot (destroyed texture = white). Fixed.
- Perf/hitches: world camera off while the computer is open (was 11 fps); FP arms rig skins only its arm split (GPU skinning on); LevelOptimizer cached fixture renderers (was GetComponentsInChildren x200 every 0.2 s); no per-frame FindObjectsByType/allocations (entities, grab, arms); Ultra tier lighter (MSAA 2, 2K shadows, 3 casters).
- FP legs: only shins + boots drawn (no torn thighs).
- Inventory: 4 pockets, G = put held small thing away, 1-4 = take out; HUD strip at the bottom; cables use a slot.
- Frutiger Aero: grass lawn (ambientCG Grass004, lush tint), billboard cumulus clouds within 120 m, clear air (fog 0.006), bubbles over ponds.
- Sound: procedural non-repeating ballast hum / air / wind (ProcVoice), real PD recordings (door creak, shutter, empty pool hall), random distant events, footsteps without repeats, breathing only after running. Wikimedia rate-limited further downloads.
- New rooms: breakroom (vending, cooler), meeting (conference table, whiteboard), server (racks) - kit_office2.py.
- Release v0.5 on GitHub.
- Known left: level generation still blocks ~5-8 s behind a black fade; archway pavilions plain; vending glass not transparent; storage tubs recoloured grey.

## Update 2026-11-18 late - video 2 fixes
- Frutiger Aero = endless open world (World/AeroWorld.cs): 48 m terrain chunks streamed around the player (one per frame), hills + rivers + lakes (water plane at -0.8), Blender aero_house.glb (kit_aerohouse.py) with the exit pavilion (kit_pavilion.py) inside; clouds follow the player. No entities in FA.
- FP: body pieces no longer drawn in first person at all (only its shadow) - torn cloth gone. FpArms: wrist target behind the grip point, shoulders lower.
- Seats face away from the backrest (probe), whiteboards don't self-shadow, browser shows "запускается" and falls back to the offline web after 14 s.
- Loading: address shown while building; physics stability check only for stacked/tilted objects, fewer hopeless candidates (BR big level 5.6 -> ~3.2 s).

## Update 2026-11-18 night - mobile (Android) in progress
- TouchInput (Player/TouchInput.cs): floating left joystick (push far = run), right-half drag look, buttons E / hand / throw / jump / crouch / photo / G / R, pause. `-touchui` forces it on PC for screenshots.
- Quality tier "Mobile" (index 3, Ultra -> 4; PlayerPrefs key now "quality2"): own renderer without SSAO / depth priming, scale 0.7 (governor down to 0.5), no MSAA, HDR R11G11B10, 1 cascade 512 sun shadow, no lamp shadows, half-res textures, no aniso, probe 32 px, far clip 60 + fog 0.03, LevelOptimizer lights 9 m / props 13 m, 3 hum voices, 14 clouds, Aero chunks 16x16. Phones default to this tier; 60 fps target.
- RealBrowser compiled only on Windows (stub elsewhere).
- GameSetup.BuildAndroid: IL2CPP ARM64, API 26+, Vulkan+GLES3, ASTC, landscape -> Build/Android/ADDRESS.apk. Android module install via Unity Hub CLI was slow/aborted - retry: "Unity Hub.exe" -- --headless install-modules --version 6000.4.6f1 -m android android-sdk-ndk-tools android-open-jdk --childModules

## 2026-11-19 — room variety & dark zones (v0.9)
Reference: Backrooms (A24, 2026, Kane Parsons) — failing furniture showroom (Cap'n Clark's), sickly-yellow offices, furniture & clothes piled about, shadowed zones; "wrong placement" liminal trick.
- LayoutGenerator: 1–2 big halls (9–15 m) per level -> "showroom" (display sets on a grid, all facing one way, sofa+coffee table) or "pillars" (0.5 m columns on a 3 m grid, off doors/exits/troffers).
- New kinds: dump (stacks, fallen chairs, boots/lifejackets/suitcases strewn, shoved sofa), wrong (one chair facing the wall).
- Dark zones: 1–2 clusters (14–26% of rooms, never the start room or its neighbours). 72% dead tubes (grey "troffer_dead" diffuser, no light), 18% flicker, rest dim amber; lit tubes near a dark zone get a shorter range (no leak through walls). Normal rooms: 3.5% flicker, 1.5% dead.
- Test: `ADDRESS.exe -batchmode -shots -roomsonly` -> shots/80_<kind>_rooms_<a..e>.png + "[ADDRESS] tour" lines.
- Test runs mute audio in GameRoot.Awake (AudioListener.volume=0 + pause) — but the user still heard sound once: do NOT run builds while the user is in class.
- FP arms: real palm normal from finger roots (fingers curl into the palm, no twisted thumb); shoulders FP-cheated forward (0,-0.30,0.04); held objects close to the chest (holdDist 0.36/0.42 + 0.35 r, -0.29 m), hands grip the near half of big objects. Arm length of the hazmat rig: 0.27 + 0.225 m.
- Entities (5 kinds, EntityKind): Echo (dark, stretched, freezes when seen), Salesman (towering tan suit, lives in showrooms, gestures, sprints), Crawler (crouch-walk, dark zones, does not freeze), Mannequin (Quaternius female mannequin, moves only unseen, fast), Limper (green-grey zombie gait, slow, never stops). Animations: Quaternius UAL1+UAL2 (CC0) imported as Humanoid (Editor: EntityAssets.Build -> Resources/Entities/*.overrideController, Mannequin.prefab); body rotated 180 deg (UAL clips face -Z); frozen = Animator.speed 0.
- Entity sounds (CC0, Resources/EntitySfx, CREDITS.txt): growls/beasts/demon/spirit voices, looped carpet/hard footsteps by speed, deep sting on notice, jump scare on catch.
- Downloads: Tools/itch_dl.sh <itch page> <dir> (free itch.io packs; empty key works). Test tour: shots/85_ent_<kind>_{idle,walk}.png.
- New worlds (not yet seen in-game, user was in class): PG "Подземная парковка" (LayoutGenerator min 9 m sections, all open, kind "parking": pillar grid, painted stall lines + yellow kerb band, Asphalt031/Concrete034/Concrete047A, cold half-dead tubes, dust fog) and HT "Бесконечный отель" (LayoutGenerator.Hotel: 2 m corridor grid, Carpet015 + Wallpaper002B, wooden room doors as decor every 3-4 m, warm dim lights every other off, exits = lifts only via ExitPlanner.ForceForm). NextAesthetic: lifts -> garage, plain doors -> hotel. Test: shots/86_{PG,HT}{0,1}_look*.png.
- Wiki-archetype entities (our own models): Smiler (billboard grin, additive Resources/Entities/SmilerMat, dark zones/garage, freezes when seen), Faceling (skin mannequin, passive wanderer), Partygoer (yellow mannequin with "=)" cutout face, Dance_Loop idle, sprints).

## 2026-11-20 — v0.11
- Downloaded CC-BY Sketchfab entities (Art_src/dl/sketchfab → Tools/blender/clean_export.py → Game/Assets/ThirdParty/Sketchfab, credits there):
  Smiler, Skin Stealer, Captain Clark (Mixamo → Humanoid + UAL clips), Partygoer, Growlers, Needlelimbs (own clips, Generic).
  Editor: `Address.EditorTools.SketchfabEntities.Build` → Resources/Entities/Model_<Kind>.prefab + override controllers.
  Generic rigs are measured in a sampled pose (their clips move the root). Scramby downloaded but unused (ceiling entity, only Hide/Kill clips). Kitty rejected (960k tris, no rig).
- New worlds OF (abandoned office) and LF (Level Fun) — kit materials recoloured, kindPool in LayoutGenerator.
- Silent test builds: set `m_DisableAudio: 1` in ProjectSettings/AudioManager.asset for test runs, back to 0 before release builds.
- Not verified: gameplay chase with new kinds, sounds, Android build.

## 2026-11-20 — v0.12
- Entities: Howler (= "Backrooms Pitfalls Bacteria", BezPixelu) and Lifeform (bac). Tried and dropped: "Accurate Bacteria v2" (clips are film scenes with root travel — body leaves the frame), "Still Life improved" (CC rig mapped by SketchfabEntities.MapCC, but head is a bone-parented mesh dropped by clean_export and the arms stayed in T-pose — retry with keep_unskinned=1).
- Worlds PD (Pipe Dreams: small concrete rooms, LevelBuilder.Pipes), WH (warehouse: storage/pillars, concrete), LO (Lights Out: 86% tubes dead, near-black ambient).

## v0.13 (2026-11-20)
- Verity (downloaded Blockbench model): flies through walls, comes to the explorer, E = talk. Tells the real way out
  (form, distance, direction, how to open it), warns about false/loop exits and the entity here, then leads along the
  real corridors (BFS on the nav grid). Five faces switched by scale: the mouth flaps while it talks; Kenney CC0 blips.
- Fake Verity: dimmer, greyer, flickering glow, "sus" face; same cheerful talk, but leads to the monster (or a door
  to nowhere) and goes out with a giggle.
- Index (menu -> Индекс): creatures actually seen and worlds entered, kept in PlayerPrefs; toast on new entries.
- Captain Clark limps on the right leg (stiff knee, hip drop and lean, slows on the bad step) on top of the UAL walk.
- New entities: Hound, Rake, Librarian (moves only unseen), Deathmoth (flies), Hazmat (lost explorer).
  FitHeight: bodies are re-measured in play and scaled/feet-planted if a rig shows a different size than in the editor.
- New worlds: Электростанция (EL), Пещеры (CV, ambientCG Rock030/Ground054, glows instead of lamps), Беги! (RN, red,
  entity wakes after 5 s), Затопленные комнаты (FL), Библиотека (LB, bookshelves + Poly Haven encyclopedia sets, no
  physics settle), Лаборатория Async (LA).
- Dropped: "Wooden Horror Stalker" (Tripo AI rig lies sideways: invisible in play), cooklittlelambs hazmat (mesh far
  from its skeleton). Not verified: live chases with the new kinds, their sounds, Android. Library takes ~13-16 s to build.

## v0.16 (2026-11-20)
- New creatures (Sketchfab CC BY, Tools/blender/inspect_anim.py renders every take before choosing):
  Spider (rakshaan, yaw 180), Thinman (ManThing by Shedmon, 3 m, freezes when seen), Grinner (backrooms monster 01 by PhillWP),
  Shy (SCP-096 Reanimated by SealifeFan3: sits and weeps; looked at -> 3.2 s scream -> hunts that player and never gives up),
  Ember (Infernal Magma Hound by Tim0). Rejected: Finger Maiden (an unreadable lump in play), Wretch (one 754-frame take that
  turns the body around), Maw/Flesh Blob/Black Monster (no walk), Phantom ghoul, Still Life FF3, Attic ghost, One-eyed (cute).
  Sketchfab download: the API's archive URL only downloads via a real click on the dialog's GLB button (JS/iframe downloads are blocked).
- New data worlds: GR Grassrooms, PK Pinkrooms, DM Dead Mall, TE Level 3999 (white, no entities, rare), TH Level 7
  Thalassophobia, BS Level -1 basement (spiders), BK stone corridors. New ambientCG: Bricks075A, Marble006 (unused: too blue),
  WoodFloor051, Tiles040, Wallpaper001C.
- `-gametest` (silent): ritual (music + 2 servants -> survive -> other world), NOCLIP/GODMODE/MOTHERLODE, third strike ->
  YOU CHEATED, a Spider catches you -> next Echo. All pass. Fixed: cheat effects did nothing without a keyboard (touch);
  SKIPLEVEL + third strike could start two trips at once; ritual could "end" instantly when the music source never plays.

## v0.17 (2026-11-21)
- Creatures (Sketchfab CC BY): Upir (Ape-like Goblin, Livagof), Wendigo (PSX Wendigo, ultaragaultraultronus; freezes when
  seen, 4 m/s unseen), Hellhound (Infernal Dog, Rodolfoisreal1423; fastest), Decayed and Medusa (Terror Engine, jazbattgl).
  Textures that the FBX lost: copy the base colour to <stem>_base.png (goblin, medusa). Rejected: Creature Mimic (barrel +
  spikes), Ditch Dog (shards), Found Footage (rigid parts), Skawg (no walk), Lurker (swims), Grandpa / Animal Company (other games' characters).
- Worlds: MG Morgue, SW Subway (140 m corridor), AT Attic (2.3 m ceiling), CS Cold Storage, CH Chapel, AR Dark Arcade.
  New ambientCG: Planks021, Tiles046.
- Silent checks all pass: shots tour (30 kinds), -gametest, -smoketest.
