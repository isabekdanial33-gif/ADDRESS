using System.Collections.Generic;
using System.Linq;
using Address.Core;
using Address.Placement;
using Address.TestLevel;
using Address.Rendering;
using UnityEngine;
using Pose = Address.Placement.Pose;

namespace Address.World
{
    /// <summary>Everything a level build needs that comes from assets (set by the editor or by GameConfig at runtime).</summary>
    [System.Serializable]
    public class LevelKit
    {
        public ProfileDatabase db;
        public Material floorMat, wallMat, ceilingMat, lightMat;
        public GameObject trofferPrefab, baseboardPrefab, doorFramePrefab;
        public GameObject exitDoorPrefab, exitElevatorPrefab, exitVentPrefab, exitPavilionPrefab, aeroHousePrefab;
        public Material voidMat;
        public Material poolTileMat, basinMat, waterMat;
        public Material aeroFloorMat, aeroWallMat, glassMat, aeroGrassMat;
        public Material pgFloorMat, pgWallMat, pgCeilMat, htFloorMat, htWallMat, htCeilMat;
        public Material ofFloorMat, ofWallMat, ofCeilMat, lfFloorMat, lfWallMat;
        public Material pdWallMat, pdFloorMat, whFloorMat, whWallMat, pipeMat;
        public Material elWallMat, cvWallMat, cvFloorMat, lbFloorMat, laFloorMat, laWallMat, laCeilMat;
        public Texture[] caustics;
    }

    /// <summary>
    /// Builds a playable Backrooms level for an address: layout -> architecture -> navigation -> furnishing.
    /// Deterministic: the same address always produces the same level.
    /// </summary>
    public class LevelBuilder
    {
        public const int ArchLayer = 8, PropsLayer = 9;

        public class Result
        {
            public LevelAddress address;
            public GameObject root;
            public LayoutGenerator.Layout layout;
            public List<RoomSpec> rooms = new();
            public PlacementEngine engine;
            public NavGrid nav;
            public Vector3 start;
            public float startYaw;
            public List<ExitPlan> exits = new();
            public List<Rect> basins = new();
            public string aesthetic = "BR";
            public int attempt;
            public ReflectionProbe probe;
            public string rare = "";          // "", "silent", "photo", "vast"
        }

        /// <summary>Hash of what stands where (2 cm / 2 deg grid): equal on every machine that generated the same address.</summary>
        public static string StructuralHash(Result r)
        {
            ulong h = 1469598103934665603UL;
            foreach (var p in r.engine.placed)
            {
                if (!p) continue;
                if (p.intentionalAnomaly) { h = Hashing.Combine(h, Hashing.Fnv64("anomaly:" + p.profile.id)); continue; }   // they bob in place
                var v = p.transform.position / 0.02f;
                h = Hashing.Combine(h, Hashing.Fnv64($"{p.profile.id}:{Mathf.RoundToInt(v.x)},{Mathf.RoundToInt(v.y)},{Mathf.RoundToInt(v.z)}:{Mathf.RoundToInt(p.transform.eulerAngles.y / 2f)}"));
            }
            foreach (var e in r.exits) h = Hashing.Combine(h, Hashing.Fnv64($"{e.form}{e.role}{e.position.x:F2}{e.position.z:F2}"));
            return h.ToString("X16");
        }

        static readonly Dictionary<string, Material> tintCache = new Dictionary<string, Material>();
        static Material Tinted(Material src, Color c, string key)
        {
            if (!src) return src;
            if (tintCache.TryGetValue(key, out var m) && m) return m;
            m = new Material(src) { name = key };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            tintCache[key] = m; return m;
        }

        /// <summary>Global lighting/fog per aesthetic (procedural levels have no baked GI: ambient stands in for bounce light).</summary>
        public static void ApplyAtmosphere(string aesthetic)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            var sky = Resources.Load<Material>(aesthetic == "FA" ? "SkyAero" : "SkyDim");
            if (sky) RenderSettings.skybox = sky;
            if (aesthetic == "FA")
            {
                RenderSettings.ambientSkyColor = new Color(0.30f, 0.42f, 0.55f);
                RenderSettings.ambientEquatorColor = new Color(0.52f, 0.60f, 0.66f);   // walls get the bounce of white floors
                RenderSettings.ambientGroundColor = new Color(0.55f, 0.62f, 0.68f);
                RenderSettings.fogDensity = 0.006f; RenderSettings.fogColor = new Color(0.72f, 0.86f, 0.98f);   // open air: clear, a light haze only
            }
            else if (aesthetic == "PG")
            {
                RenderSettings.ambientSkyColor = new Color(0.07f, 0.08f, 0.09f);
                RenderSettings.ambientEquatorColor = new Color(0.12f, 0.13f, 0.14f);
                RenderSettings.ambientGroundColor = new Color(0.22f, 0.23f, 0.24f);
                RenderSettings.fogDensity = 0.035f; RenderSettings.fogColor = new Color(0.2f, 0.22f, 0.24f);   // concrete dust
            }
            else if (aesthetic == "HT")
            {
                RenderSettings.ambientSkyColor = new Color(0.08f, 0.05f, 0.03f);
                RenderSettings.ambientEquatorColor = new Color(0.16f, 0.09f, 0.06f);
                RenderSettings.ambientGroundColor = new Color(0.3f, 0.14f, 0.1f);
                RenderSettings.fogDensity = 0.03f; RenderSettings.fogColor = new Color(0.22f, 0.12f, 0.08f);
            }
            else if (aesthetic == "OF")
            {
                RenderSettings.ambientSkyColor = new Color(0.08f, 0.09f, 0.1f);
                RenderSettings.ambientEquatorColor = new Color(0.14f, 0.15f, 0.16f);
                RenderSettings.ambientGroundColor = new Color(0.24f, 0.25f, 0.26f);
                RenderSettings.fogDensity = 0.03f; RenderSettings.fogColor = new Color(0.25f, 0.27f, 0.29f);
            }
            else if (aesthetic == "PD")
            {
                RenderSettings.ambientSkyColor = new Color(0.1f, 0.06f, 0.03f);
                RenderSettings.ambientEquatorColor = new Color(0.16f, 0.09f, 0.05f);
                RenderSettings.ambientGroundColor = new Color(0.22f, 0.12f, 0.06f);
                RenderSettings.fogDensity = 0.05f; RenderSettings.fogColor = new Color(0.2f, 0.11f, 0.05f);   // steam and heat
            }
            else if (aesthetic == "WH")
            {
                RenderSettings.ambientSkyColor = new Color(0.1f, 0.11f, 0.12f);
                RenderSettings.ambientEquatorColor = new Color(0.15f, 0.16f, 0.17f);
                RenderSettings.ambientGroundColor = new Color(0.26f, 0.27f, 0.28f);
                RenderSettings.fogDensity = 0.028f; RenderSettings.fogColor = new Color(0.3f, 0.32f, 0.34f);
            }
            else if (aesthetic == "LO")
            {
                RenderSettings.ambientSkyColor = new Color(0.008f, 0.008f, 0.01f);
                RenderSettings.ambientEquatorColor = new Color(0.015f, 0.014f, 0.012f);
                RenderSettings.ambientGroundColor = new Color(0.03f, 0.028f, 0.02f);
                RenderSettings.fogDensity = 0.08f; RenderSettings.fogColor = new Color(0.01f, 0.01f, 0.01f);
            }
            else if (aesthetic == "EL")
            {
                RenderSettings.ambientSkyColor = new Color(0.05f, 0.02f, 0.02f);
                RenderSettings.ambientEquatorColor = new Color(0.09f, 0.04f, 0.03f);
                RenderSettings.ambientGroundColor = new Color(0.14f, 0.06f, 0.04f);
                RenderSettings.fogDensity = 0.045f; RenderSettings.fogColor = new Color(0.12f, 0.04f, 0.03f);
            }
            else if (aesthetic == "CV")
            {
                RenderSettings.ambientSkyColor = new Color(0.01f, 0.01f, 0.012f);
                RenderSettings.ambientEquatorColor = new Color(0.02f, 0.018f, 0.016f);
                RenderSettings.ambientGroundColor = new Color(0.04f, 0.035f, 0.03f);
                RenderSettings.fogDensity = 0.07f; RenderSettings.fogColor = new Color(0.015f, 0.013f, 0.012f);
            }
            else if (aesthetic == "RN")
            {
                RenderSettings.ambientSkyColor = new Color(0.12f, 0.01f, 0.01f);
                RenderSettings.ambientEquatorColor = new Color(0.2f, 0.02f, 0.02f);
                RenderSettings.ambientGroundColor = new Color(0.3f, 0.04f, 0.03f);
                RenderSettings.fogDensity = 0.04f; RenderSettings.fogColor = new Color(0.25f, 0.02f, 0.02f);
            }
            else if (aesthetic == "FL")
            {
                RenderSettings.ambientSkyColor = new Color(0.09f, 0.12f, 0.1f);
                RenderSettings.ambientEquatorColor = new Color(0.14f, 0.18f, 0.14f);
                RenderSettings.ambientGroundColor = new Color(0.2f, 0.26f, 0.2f);
                RenderSettings.fogDensity = 0.035f; RenderSettings.fogColor = new Color(0.22f, 0.3f, 0.26f);
            }
            else if (aesthetic == "LB")
            {
                RenderSettings.ambientSkyColor = new Color(0.07f, 0.05f, 0.03f);
                RenderSettings.ambientEquatorColor = new Color(0.12f, 0.08f, 0.05f);
                RenderSettings.ambientGroundColor = new Color(0.2f, 0.13f, 0.08f);
                RenderSettings.fogDensity = 0.03f; RenderSettings.fogColor = new Color(0.16f, 0.11f, 0.07f);
            }
            else if (aesthetic == "LA")
            {
                RenderSettings.ambientSkyColor = new Color(0.2f, 0.22f, 0.24f);
                RenderSettings.ambientEquatorColor = new Color(0.3f, 0.32f, 0.34f);
                RenderSettings.ambientGroundColor = new Color(0.42f, 0.44f, 0.46f);
                RenderSettings.fogDensity = 0.012f; RenderSettings.fogColor = new Color(0.6f, 0.64f, 0.68f);
            }
            else if (aesthetic == "LF")
            {
                RenderSettings.ambientSkyColor = new Color(0.16f, 0.1f, 0.08f);
                RenderSettings.ambientEquatorColor = new Color(0.24f, 0.14f, 0.1f);
                RenderSettings.ambientGroundColor = new Color(0.46f, 0.24f, 0.2f);
                RenderSettings.fogDensity = 0.024f; RenderSettings.fogColor = new Color(0.46f, 0.3f, 0.22f);
            }
            else if (aesthetic == "PR")
            {
                RenderSettings.ambientSkyColor = new Color(0.18f, 0.22f, 0.24f);
                RenderSettings.ambientEquatorColor = new Color(0.22f, 0.28f, 0.30f);
                RenderSettings.ambientGroundColor = new Color(0.34f, 0.40f, 0.42f);
                RenderSettings.fogDensity = 0.026f; RenderSettings.fogColor = new Color(0.72f, 0.86f, 0.88f);
            }
            else
            {
                RenderSettings.ambientSkyColor = new Color(0.13f, 0.12f, 0.08f);
                RenderSettings.ambientEquatorColor = new Color(0.20f, 0.18f, 0.11f);
                RenderSettings.ambientGroundColor = new Color(0.46f, 0.42f, 0.30f);
                RenderSettings.fogDensity = 0.022f; RenderSettings.fogColor = new Color(0.42f, 0.38f, 0.24f);
            }
            // phones draw only 60 m: fog closes the distance so nothing pops in
            if (Application.isMobilePlatform) RenderSettings.fogDensity = Mathf.Max(RenderSettings.fogDensity, 0.03f);
        }

        readonly LevelKit kit;
        Material floorM, wallM, ceilM;
        List<Rect> basinsForArch = new();
        bool glassWalls;
        public bool lights = true;
        public static bool SettleOnCommit = true;
        public LevelBuilder(LevelKit kit) { this.kit = kit; }

        /// <summary>
        /// Build with the hard exit guarantee: at least one FORWARD exit reachable from the start. If not, a reachable
        /// loop/false exit becomes forward; if no exit is reachable at all the level is regenerated with the next
        /// deterministic attempt seed (same address -> same attempt sequence -> same final level).
        /// </summary>
        public Result Build(LevelAddress address, int sizeOverride = 0)
        {
            for (int attempt = 0; ; attempt++)
            {
                var res = BuildOnce(address, sizeOverride, attempt);
                if (res.exits.Any(e => e.role == ExitRole.Forward && e.reachable)) { res.attempt = attempt; HideForRare(res); return res; }
                var promote = res.exits.FirstOrDefault(e => e.reachable);
                if (promote != null)
                {
                    promote.role = ExitRole.Forward;
                    if (promote.instance) { promote.instance.name = $"Exit{promote.index}_{promote.form}_Forward"; foreach (var r in promote.instance.GetComponentsInChildren<Renderer>()) if (r.name == "void" && kit.voidMat) r.sharedMaterial = kit.voidMat; }
                    res.attempt = attempt; HideForRare(res); return res;
                }
                if (attempt >= 6) { Debug.LogError($"[ADDRESS] no reachable exit after {attempt + 1} attempts for {address}"); res.attempt = attempt; return res; }
                Object.DestroyImmediate(res.root);
            }
        }

        /// <summary>Silent/photo places: the forward exit exists and is reachable but cannot be seen or used until revealed.</summary>
        static void HideForRare(Result res)
        {
            if (res.rare != "silent" && res.rare != "photo") return;
            var e = res.exits.Where(x => x.role == ExitRole.Forward && x.reachable && x.instance).OrderBy(x => x.index).FirstOrDefault();
            if (e == null) return;
            e.instance.SetHidden(true);
        }

        Result BuildOnce(LevelAddress address, int sizeOverride, int attempt)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew(); var times = new System.Text.StringBuilder();
            void Mark(string n) { times.Append($"{n} {sw.ElapsedMilliseconds}ms  "); sw.Restart(); }
            var rng = new Rng(attempt == 0 ? address.BaseSeed : Hashing.Combine(address.BaseSeed, (ulong)attempt * 0xA77E3UL));
            var a0 = address.Aesthetic;
            var res = new Result { address = address, aesthetic = a0 == "PR" || a0 == "FA" || a0 == "PG" || a0 == "HT" || a0 == "OF" || a0 == "LF" || a0 == "PD" || a0 == "WH" || a0 == "LO"
                || a0 == "EL" || a0 == "CV" || a0 == "RN" || a0 == "FL" || a0 == "LB" || a0 == "LA" ? a0 : "BR" };
            bool garage = res.aesthetic == "PG" && kit.pgFloorMat, hotel = res.aesthetic == "HT" && kit.htFloorMat;
            bool office = res.aesthetic == "OF" && kit.ofFloorMat, fun = res.aesthetic == "LF" && kit.lfWallMat;
            bool pipes = res.aesthetic == "PD" && kit.pdWallMat, wareh = res.aesthetic == "WH" && kit.whFloorMat, dark = res.aesthetic == "LO";
            bool power = res.aesthetic == "EL" && kit.elWallMat, caves = res.aesthetic == "CV" && kit.cvWallMat, run = res.aesthetic == "RN";
            bool flood = res.aesthetic == "FL" && kit.waterMat, lib = res.aesthetic == "LB" && kit.lbFloorMat, lab = res.aesthetic == "LA" && kit.laWallMat;
            if (res.aesthetic == "OF" && !office || res.aesthetic == "LF" && !fun || res.aesthetic == "PD" && !pipes || res.aesthetic == "WH" && !wareh
                || res.aesthetic == "EL" && !power || res.aesthetic == "CV" && !caves || res.aesthetic == "FL" && !flood || res.aesthetic == "LB" && !lib || res.aesthetic == "LA" && !lab) res.aesthetic = "BR";
            world = garage ? "PG" : hotel ? "HT" : res.aesthetic;
            bool pool = res.aesthetic == "PR" && kit.poolTileMat;
            bool aero = res.aesthetic == "FA" && kit.aeroWallMat;
            floorM = pool ? kit.poolTileMat : aero ? (kit.aeroGrassMat ? LushGrass(kit.aeroGrassMat) : kit.aeroFloorMat) : kit.floorMat;
            wallM = pool ? kit.poolTileMat : aero ? kit.aeroWallMat : kit.wallMat;
            ceilM = pool ? kit.poolTileMat : aero ? kit.aeroWallMat : kit.ceilingMat;
            if (garage) { floorM = kit.pgFloorMat; wallM = kit.pgWallMat ? kit.pgWallMat : kit.wallMat; ceilM = kit.pgCeilMat ? kit.pgCeilMat : kit.ceilingMat; }
            if (hotel) { floorM = kit.htFloorMat; wallM = kit.htWallMat ? kit.htWallMat : kit.wallMat; if (kit.htCeilMat) ceilM = kit.htCeilMat; }
            if (office) { floorM = kit.ofFloorMat; wallM = kit.ofWallMat ? kit.ofWallMat : kit.wallMat; if (kit.ofCeilMat) ceilM = kit.ofCeilMat; }
            if (pipes) { wallM = kit.pdWallMat; floorM = kit.pdFloorMat ? kit.pdFloorMat : kit.pgFloorMat; ceilM = kit.pdWallMat; }
            if (wareh) { floorM = kit.whFloorMat; wallM = kit.whWallMat ? kit.whWallMat : kit.wallMat; if (kit.pgCeilMat) ceilM = kit.pgCeilMat; }
            if (power) { wallM = Tinted(kit.elWallMat, new Color(0.62f, 0.66f, 0.6f), "el_wall"); floorM = kit.pdFloorMat ? kit.pdFloorMat : floorM; ceilM = kit.elWallMat; }
            if (caves) { wallM = kit.cvWallMat; floorM = kit.cvFloorMat ? kit.cvFloorMat : kit.cvWallMat; ceilM = kit.cvWallMat; }
            if (run)
            {
                // Level Run: the same yellow rooms, washed in alarm red
                wallM = Tinted(kit.wallMat, new Color(1f, 0.62f, 0.56f), "rn_wall");
                floorM = Tinted(kit.floorMat, new Color(0.7f, 0.36f, 0.32f), "rn_floor");
            }
            if (lib)
            {
                floorM = kit.lbFloorMat;
                wallM = Tinted(kit.htWallMat ? kit.htWallMat : kit.wallMat, new Color(0.72f, 0.58f, 0.44f), "lb_wall");
                if (kit.htCeilMat) ceilM = Tinted(kit.htCeilMat, new Color(0.86f, 0.8f, 0.7f), "lb_ceil");
            }
            if (lab) { floorM = kit.laFloorMat ? kit.laFloorMat : floorM; wallM = kit.laWallMat; if (kit.laCeilMat) ceilM = kit.laCeilMat; }
            if (fun)
            {
                // Level Fun: the same downloaded wallpaper and carpet, recoloured party-orange and candy red
                wallM = Tinted(kit.lfWallMat, new Color(1f, 0.74f, 0.5f), "lf_wall");
                floorM = Tinted(kit.lfFloorMat ? kit.lfFloorMat : kit.floorMat, new Color(0.95f, 0.42f, 0.45f), "lf_floor");
            }
            glassWalls = aero;
            res.root = new GameObject("Level " + address);
            var arch = new GameObject("Architecture"); arch.transform.SetParent(res.root.transform);
            var props = new GameObject("Props"); props.transform.SetParent(res.root.transform);
            if (aero) return BuildAero(res, rng, arch.transform, props.transform);

            // rare places (~1 in 18): the exit is found differently; the guarantee still holds
            var rr = rng.Fork("rare"); float rv = rr.Value;
            res.rare = rv < 0.022f ? "silent" : rv < 0.044f ? "photo" : rv < 0.056f ? "vast" : "";
            if (garage || hotel || office || fun || pipes || wareh || dark || power || caves || run || flood || lib || lab) res.rare = "";
            int size = sizeOverride > 0 ? sizeOverride : (rng.Fork("size").Chance(0.4f) ? 24 : 36);
            var L = aero ? LayoutGenerator.Park(rng.Fork("size").Range(34, 46))
                  : hotel ? LayoutGenerator.Hotel(rng.Fork("layout"), 44)
                  : garage ? new LayoutGenerator { minRoomM = 9f, maxRoomLo = 13f, maxRoomHi = 19f, doorShare = 0f, forceKind = "parking" }.Generate(rng.Fork("layout"), 44, 44)
                  : office ? new LayoutGenerator { doorShare = 0.55f, kindPool = new[] { "office", "office", "office", "meeting", "breakroom", "server", "storage", "empty", "dump" } }.Generate(rng.Fork("layout"), 36, 36)
                  : pipes ? new LayoutGenerator { minRoomM = 2.5f, maxRoomLo = 3.5f, maxRoomHi = 6f, doorShare = 0f, kindPool = new[] { "corridor", "corridor", "empty", "storage", "dump" } }.Generate(rng.Fork("layout"), 32, 32)
                  : wareh ? new LayoutGenerator { minRoomM = 8f, maxRoomLo = 12f, maxRoomHi = 18f, doorShare = 0.1f, kindPool = new[] { "storage", "storage", "storage", "pillars", "empty", "dump" } }.Generate(rng.Fork("layout"), 44, 44)
                  : power ? new LayoutGenerator { minRoomM = 3f, maxRoomLo = 4.5f, maxRoomHi = 8f, doorShare = 0.2f, kindPool = new[] { "server", "server", "corridor", "storage", "empty", "dump" } }.Generate(rng.Fork("layout"), 34, 34)
                  : caves ? new LayoutGenerator { minRoomM = 3.5f, maxRoomLo = 6f, maxRoomHi = 11f, doorShare = 0f, kindPool = new[] { "empty", "empty", "empty", "corridor", "dump" } }.Generate(rng.Fork("layout"), 38, 38)
                  : run ? new LayoutGenerator { minRoomM = 2.4f, maxRoomLo = 3f, maxRoomHi = 4.5f, doorShare = 0f, kindPool = new[] { "corridor", "corridor", "corridor", "empty" } }.Generate(rng.Fork("layout"), 40, 40)
                  : flood ? new LayoutGenerator { kindPool = new[] { "empty", "empty", "corridor", "wrong", "dump" } }.Generate(rng.Fork("layout"), size, size)
                  : lib ? new LayoutGenerator { minRoomM = 4f, maxRoomLo = 7f, maxRoomHi = 12f, doorShare = 0.3f, kindPool = new[] { "library", "library", "library", "library", "lounge", "empty" } }.Generate(rng.Fork("layout"), 36, 36)
                  : lab ? new LayoutGenerator { doorShare = 0.6f, kindPool = new[] { "office", "meeting", "server", "server", "storage", "empty", "corridor" } }.Generate(rng.Fork("layout"), 36, 36)
                  : fun ? new LayoutGenerator { kindPool = new[] { "lounge", "lounge", "empty", "breakroom", "showroom", "wrong", "dump" } }.Generate(rng.Fork("layout"), size, size)
                  : res.rare == "vast" ? LayoutGenerator.Vast(40) : new LayoutGenerator().Generate(rng.Fork("layout"), size, size);
            if (aero) res.rare = "";
            res.layout = L;
            var runs = LayoutGenerator.Walls(L);
            LayoutGenerator.BuildSpecs(L, runs);
            res.rooms = L.rooms.Select(r => r.spec).ToList();
            if (aero)
            {
                var c = L.WorldBounds.center; c.y = 0;
                var st = c + new Vector3(rng.Fork("st").Range(-6f, 6f), 0, rng.Fork("st2").Range(-6f, 6f));
                res.exits = ExitPlanner.PlanPark(L, rng.Fork("exits"), st);
                PlanPonds(L, res, rng.Fork("ponds"), st);
                // nothing may stand in or right in front of a pavilion
                foreach (var e in res.exits)
                {
                    var c0 = e.position + e.normal * 2.0f; var a2 = Mathf.Abs(e.normal.x) > 0.5f;
                    L.rooms[0].spec.keepOut.Add(a2 ? new Rect(c0.x - 2.6f, c0.z - 1.6f, 5.2f, 3.2f) : new Rect(c0.x - 1.6f, c0.z - 2.6f, 3.2f, 5.2f));
                }
            }
            else
            {
                ExitPlanner.ForceForm = hotel ? ExitForm.Elevator : (ExitForm?)null;
                res.exits = ExitPlanner.Plan(L, rng.Fork("exits"));
                ExitPlanner.ForceForm = null;
            }
            if (pool) PlanBasins(L, res, rng.Fork("basins"), new HashSet<int>(res.exits.Select(e => e.roomId)));

            basinsForArch = res.basins;
            BuildArchitecture(L, runs, arch.transform, rng.Fork("arch"), res.exits);
            if (pool || (aero && res.basins.Count > 0)) BuildBasins(res, arch.transform);
            if (aero) foreach (var b in res.basins) Bubbles(b, arch.transform);
            if (aero) BuildSky(L, arch.transform, rng.Fork("sky"));
            Mark("arch");
            var exitsRoot = new GameObject("Exits"); exitsRoot.transform.SetParent(res.root.transform);   // animated: never static-batched
            BuildExits(res.exits, exitsRoot.transform);
            Physics.SyncTransforms();

            // navigation: room interiors + openings walkable; walkways between all openings of a room reserved
            var area = L.WorldBounds; area.Expand(new Vector3(1, 0, 1));
            res.nav = new NavGrid(area, 0.25f);
            foreach (var r in res.rooms) res.nav.SetWalkable(r.bounds, true);
            foreach (var o in L.openings)
            {
                var c = o.Center; float len = o.to - o.from;
                var b = o.vertical ? new Bounds(c, new Vector3(LayoutGenerator.WallT + 0.6f, 1, len - 0.1f)) : new Bounds(c, new Vector3(len - 0.1f, 1, LayoutGenerator.WallT + 0.6f));
                res.nav.SetWalkable(b, true);
            }
            res.nav.CarveObstacles(1 << ArchLayer);
            foreach (var b in res.basins) res.nav.SetWalkable(new Bounds(new Vector3(b.center.x, 0, b.center.y), new Vector3(b.width + 0.3f, 1, b.height + 0.3f)), false);
            var startRoom = L.rooms[L.startRoom].spec;
            res.start = startRoom.bounds.center; res.start.y = 0;
            if (aero) { var c = L.WorldBounds.center; res.start = new Vector3(c.x + rng.Fork("st").Range(-6f, 6f), 0, c.z + rng.Fork("st2").Range(-6f, 6f)); }
            res.startYaw = rng.Fork("yaw").Range(0, 4) * 90f;
            foreach (var r in res.rooms)
            {
                var pts = new List<Vector3>(r.doors);
                if (r == startRoom) pts.Insert(0, res.start);
                for (int i = 0; i < pts.Count - 1; i++) res.nav.ReservePath(pts[i], pts[i + 1], 1.0f);
            }
            // every exit keeps a free walkway from its room's first opening to its approach zone
            foreach (var e in res.exits)
            {
                var room = L.rooms[e.roomId].spec;
                var from = room.doors.Count > 0 ? room.doors[0] : room.bounds.center;
                from.y = 0;
                e.approachCell = NearestWalkable(res.nav, e.Approach);
                res.nav.ReservePath(from, res.nav.Center(e.approachCell.x, e.approachCell.y), 1.0f);
                foreach (var c in res.nav.CellsIn(new Rect2(new Vector2(e.Approach.x, e.Approach.z), new Vector2(e.width * 0.5f + 0.1f, 0.6f), Mathf.Atan2(e.normal.x, e.normal.z))))
                    res.nav.ReservePath(res.nav.Center(c.x, c.y), res.nav.Center(c.x, c.y), 0.25f);
            }

            Mark("nav");
            // furnishing
            var eng = new PlacementEngine(ArchLayer, PropsLayer, props.transform) { style = pool ? "poolrooms" : aero ? "frutiger" : "backrooms", mood = pool || aero ? "light" : "neutral", nav = res.nav, navStart = res.start, settleOnCommit = SettleOnCommit && !lib };   // library: thousands of books stand on their boards, no physics pass
            res.engine = eng;
            var sc = new Scenarios(eng, kit.db);
            foreach (var room in L.rooms)
            {
                var spec = room.spec;
                eng.room = spec;
                var r = rng.Fork("room", room.id);
                float areaM2 = spec.bounds.size.x * spec.bounds.size.z;
                for (int i = 0; i < r.Range(1, 4); i++) sc.WallDecor(spec, r.Fork("outlet", i), "outlet");
                if (r.Chance(0.5f)) sc.WallDecor(spec, r, "switch");
                if (r.Chance(0.5f)) sc.WallDecor(spec, r, "vent");
                if (res.rare == "vast") { for (int i = 0; i < 3; i++) sc.FloorSingle(spec, r.Fork("stray", i), r.Pick(new[] { "chair", "sign", "box" }), false); continue; }
                if (aero)
                {
                    float a2 = spec.bounds.size.x * spec.bounds.size.z;
                    if (room.kind == "corridor") { if (r.Chance(0.4f)) sc.FloorSingle(spec, r, "plant"); continue; }
                    // the park: water columns, planters, benches, orbs, lots of green, lounges under the sky
                    int n = Mathf.Clamp(Mathf.RoundToInt(a2 / 11f), 1, 160);
                    for (int i = 0; i < Mathf.Clamp(Mathf.RoundToInt(a2 / 160f), 1, 10); i++) { sc.AgainstWallSingle(spec, r.Fork("bench", i), "bench"); sc.Lounge(spec, r.Fork("lounge", i + 1)); }
                    for (int i = 0; i < n; i++) sc.FloorSingle(spec, r.Fork("aero", i), r.Pick(new[] { "plant", "plant", "planter", "tube", "orb" }), r.Chance(0.5f));
                    if (r.Chance(0.6f)) sc.AgainstWallSingle(spec, r, "bench");
                    if (r.Chance(0.35f)) sc.Lounge(spec, r.Fork("lounge", 0));
                    if (room.kind == "empty" && r.Chance(0.008f)) sc.FloatingRing(spec, r.Fork("anomaly", 0), r.Range(3, 6));
                    continue;
                }
                if (pool)
                {
                    if (room.kind == "pool" || room.kind == "lounge")
                    {
                        for (int i = 0; i < r.Range(0, 5); i++) sc.FloorSingle(spec, r.Fork("chair", i), "chair");
                        if (r.Chance(0.35f)) sc.FloorSingle(spec, r, "sign", false);
                        if (r.Chance(0.4f)) sc.FloorSingle(spec, r, "plant");
                        if (r.Chance(0.2f)) sc.FloorSingle(spec, r, "crate");
                    }
                    else if (room.kind == "empty" && r.Chance(0.008f)) sc.FloatingRing(spec, r.Fork("anomaly", 0), r.Range(3, 6));
                    continue;
                }
                switch (room.kind)
                {
                    case "office":
                        int ws = Mathf.Clamp(Mathf.RoundToInt(areaM2 / 11f), 1, 7);
                        for (int i = 0; i < ws; i++) sc.Workstation(spec, r.Fork("ws", i), true);
                        if (areaM2 > 35) sc.DeskIsland(spec, r.Fork("isl", 0));
                        if (r.Chance(0.5f)) sc.AgainstWallSingle(spec, r, "cabinet");
                        if (r.Chance(0.4f)) sc.FloorSingle(spec, r, "plant");
                        if (r.Chance(0.5f)) sc.FloorSingle(spec, r, "trash");
                        if (r.Chance(0.6f)) sc.WallDecor(spec, r, "wallclock");
                        if (r.Chance(0.3f)) sc.AgainstWallSingle(spec, r, "extinguisher");
                        if (r.Chance(0.4f)) sc.Stack(spec, r.Fork("stack", 0), r.Range(1, 4));
                        break;
                    case "storage":
                        int sh = Mathf.Clamp(Mathf.RoundToInt(areaM2 / 9f), 1, 6);
                        for (int i = 0; i < sh; i++) sc.ShelfUnit(spec, r.Fork("shelf", i), r.Range(0.5f, 1f));
                        for (int i = 0; i < r.Range(1, 4); i++) sc.Stack(spec, r.Fork("stack", i), r.Range(1, 4));
                        if (r.Chance(0.4f)) sc.FloorSingle(spec, r, "cart", false);
                        if (r.Chance(0.3f)) sc.FloorSingle(spec, r, "sign");
                        break;
                    case "breakroom":   // vending machines, a water cooler, a table with chairs, a bin
                        for (int i = 0; i < r.Range(1, 3); i++) sc.AgainstWallSingle(spec, r.Fork("vend", i), "vending");
                        sc.AgainstWallSingle(spec, r.Fork("cool", 0), "cooler");
                        if (r.Chance(0.7f)) sc.DeskIsland(spec, r.Fork("tbl", 0));
                        if (r.Chance(0.6f)) sc.FloorSingle(spec, r, "trash");
                        if (r.Chance(0.5f)) sc.WallDecor(spec, r, "wallclock");
                        break;
                    case "meeting":     // one long table, whiteboard, chairs around
                        sc.FloorSingle(spec, r.Fork("conf", 0), "conftable", false);
                        for (int i = 0; i < r.Range(3, 9); i++) sc.FloorSingle(spec, r.Fork("chair", i), "chair");
                        sc.WallDecor(spec, r.Fork("wb", 0), "board");
                        if (r.Chance(0.5f)) sc.FloorSingle(spec, r, "plant");
                        break;
                    case "server":      // rows of humming racks
                        int racks = Mathf.Clamp(Mathf.RoundToInt(areaM2 / 4f), 2, 10);
                        for (int i = 0; i < racks; i++) sc.AgainstWallSingle(spec, r.Fork("rack", i), "rack");
                        if (r.Chance(0.5f)) sc.Stack(spec, r.Fork("stack", 0), r.Range(1, 3));
                        break;
                    case "parking":     // a few things left behind between the columns
                        if (r.Chance(0.5f)) sc.FloorSingle(spec, r, "cart", false);
                        for (int i = 0; i < r.Range(1, 4); i++) sc.FloorSingle(spec, r.Fork("junk", i), r.Pick(new[] { "box", "trash", "sign", "crate", "box" }), r.Chance(0.7f));
                        if (r.Chance(0.4f)) sc.Stack(spec, r.Fork("stack", 0), r.Range(1, 3));
                        if (r.Chance(0.5f)) sc.AgainstWallSingle(spec, r, "extinguisher");
                        break;
                    case "hall":        // hotel corridor: a picture, a side table with a lamp, a plant, a housekeeping trolley
                        if (r.Chance(0.6f)) sc.WallDecor(spec, r, "picture");
                        if (r.Chance(0.25f)) sc.AgainstWallSingle(spec, r, "smalltable");
                        if (r.Chance(0.2f)) sc.FloorSingle(spec, r, "plant");
                        if (r.Chance(0.12f)) sc.FloorSingle(spec, r, "cart");
                        if (r.Chance(0.15f)) sc.FloorSingle(spec, r, r.Pick(new[] { "suitcase", "trash", "chair" }));
                        break;
                    case "showroom":    // a furniture showroom: display sets spread out in rows, all facing the same way
                        sc.Showroom(spec, r);
                        break;
                    case "dump":        // abandoned furniture and clothes piled and strewn about
                        sc.Dump(spec, r);
                        break;
                    case "pillars":     // a hall of columns, nearly empty
                        if (r.Chance(0.4f)) sc.FloorSingle(spec, r, r.Pick(new[] { "chair", "box", "sign" }), false);
                        break;
                    case "wrong":       // furnished, but placed wrong: a chair facing the wall
                        sc.Wrong(spec, r);
                        break;
                    case "library":     // bookshelves along every wall, a reading chair or two
                        int bs = Mathf.Clamp(Mathf.RoundToInt(areaM2 / 7f), 1, 5);
                        for (int i = 0; i < bs; i++) sc.ShelfUnit(spec, r.Fork("book", i), r.Range(0.35f, 0.5f), "wooden_bookshelf_worn", "book");
                        if (r.Chance(0.5f)) sc.FloorSingle(spec, r, "armchair");
                        if (r.Chance(0.4f)) sc.FloorSingle(spec, r.Fork("tbl", 0), "smalltable");
                        if (r.Chance(0.3f)) sc.WallDecor(spec, r, "wallclock");
                        break;
                    case "lounge":
                        sc.Lounge(spec, r);
                        if (r.Chance(0.6f)) sc.FloorSingle(spec, r, "plant");
                        if (r.Chance(0.5f)) sc.WallDecor(spec, r, "picture");
                        break;
                    default: // empty / corridor: the liminal norm; sometimes a single stray object
                        if (room.kind == "empty" && r.Chance(0.008f)) { sc.FloatingRing(spec, r.Fork("anomaly", 0), r.Range(3, 7)); break; }  // rare find
                        if (r.Chance(0.22f)) sc.FloorSingle(spec, r, r.Pick(new[] { "chair", "box", "sign", "trash", "stool" }), r.Chance(0.6f));
                        if (r.Chance(0.12f)) sc.WallDecor(spec, r, "wallclock");
                        break;
                }
            }
            Mark("furnish");
            // one realtime reflection probe rendered once: glossy tiles, water and glass reflect the actual place
            var probeGo = new GameObject("ReflectionProbe"); probeGo.transform.SetParent(res.root.transform);
            probeGo.transform.position = res.start + Vector3.up * 1.4f;
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime; probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(L.w * LayoutGenerator.Cell + 2, 6, L.h * LayoutGenerator.Cell + 2); probe.center = L.WorldBounds.center - probeGo.transform.position + Vector3.up * 1.4f;
            probe.resolution = Application.isMobilePlatform ? 32 : 128; probe.hdr = !Application.isMobilePlatform; probe.intensity = 1f;
            res.probe = probe;

            // guarantee: verify every exit is reachable from the start through the furnished level
            var sc0 = res.nav.CellOf(res.start);
            foreach (var e in res.exits)
            {
                e.reachable = res.nav.FindPath(sc0, e.approachCell) != null;
                if (!e.reachable) e.reachable = Repair(res, sc0, e);
            }
            Mark("verify");
            Debug.Log($"[ADDRESS] build timing {address}: {times} objects {res.engine.placed.Count} settle {PlacementEngine.TSettle.ElapsedMilliseconds}ms tries {res.engine.stats.candidatesTried} req {res.engine.stats.requested}");
            PlacementEngine.TSettle.Reset();
            return res;
        }

        static Vector2Int NearestWalkable(NavGrid nav, Vector3 p)
        {
            var c = nav.CellOf(p);
            for (int r = 0; r <= 6; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) == r && nav.Walkable(c.x + dx, c.y + dz)) return new Vector2Int(c.x + dx, c.y + dz);
            return c;
        }

        /// <summary>
        /// Exit cut off by furniture: find the route ignoring furniture and remove whatever stands on it
        /// (with everything resting on it). Architecture is never touched, so this cannot open holes in walls.
        /// </summary>
        static bool Repair(Result res, Vector2Int start, ExitPlan e)
        {
            var path = res.nav.FindPath(start, e.approachCell, ignoreBlocked: true);
            if (path == null) return false;
            var onPath = new HashSet<Vector2Int>(path);
            foreach (var po in res.engine.placed.ToList())
            {
                if (!po || po.supportParent != null) continue;
                var foot = PlacementEngine.FootRect(po.profile, new Pose(po.transform.position, po.transform.rotation, po.transform.localScale.x));
                if (!res.nav.CellsIn(foot).Any(onPath.Contains)) continue;
                res.nav.Unblock(foot);
                RemoveTree(res, po);
            }
            Physics.SyncTransforms();
            return res.nav.FindPath(start, e.approachCell) != null;
        }

        static void RemoveTree(Result res, PlacedObject po)
        {
            foreach (var c in res.engine.placed.Where(c => c && c.supportParent == po).ToList()) RemoveTree(res, c);
            res.engine.placed.Remove(po);
            Object.DestroyImmediate(po.gameObject);
        }

        void BuildExits(List<ExitPlan> exits, Transform parent)
        {
            foreach (var e in exits)
            {
                if (e.form == ExitForm.Archway) { BuildArchway(e, parent); continue; }
                var prefab = e.form switch { ExitForm.Elevator => kit.exitElevatorPrefab, ExitForm.VentHatch => kit.exitVentPrefab, _ => kit.exitDoorPrefab };
                if (!prefab) continue;
                var go = Object.Instantiate(prefab, parent);
                go.name = $"Exit{e.index}_{e.form}_{e.role}";
                // back face on the wall plane (prefab back is its local -Z at z=0)
                go.transform.SetPositionAndRotation(e.position, Quaternion.LookRotation(e.normal, Vector3.up));
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ArchLayer;
                if (e.form == ExitForm.Elevator)
                {
                    // dark shaft behind the sliding doors (the model's own back plate sits inside the wall)
                    var v = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.DestroyImmediate(v.GetComponent<Collider>());
                    v.name = "void"; v.layer = ArchLayer;
                    v.transform.SetParent(go.transform, false);
                    v.transform.localPosition = new Vector3(0, 1.05f, 0.002f);
                    v.transform.localRotation = Quaternion.Euler(0, 180, 0);
                    v.transform.localScale = new Vector3(1.0f, 2.1f, 1);
                    if (kit.voidMat) v.GetComponent<Renderer>().sharedMaterial = kit.voidMat;
                }
                if (e.form == ExitForm.HandlelessDoor)
                {
                    // half of the doors are ordinary painted doors with a handle; the other half wear the wallpaper and have
                    // no handle - only the dark seam gives them away (decided per exit, deterministic)
                    bool hiddenDoor = ((e.index * 7919 + Mathf.RoundToInt(e.position.x * 31 + e.position.z * 17)) & 1) == 0;
                    var handle = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "door_handle");
                    var leafT = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "leaf");
                    if (handle) { if (hiddenDoor || !leafT) Object.DestroyImmediate(handle.gameObject); else handle.SetParent(leafT, true); }
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                        if (r.name == "leaf" && wallM && hiddenDoor)
                        {
                            var m = r.sharedMaterials; for (int i = 0; i < m.Length; i++) m[i] = wallM; r.sharedMaterials = m;
                            // same texel density as the walls (1.1 m per tile), projected in the leaf's own plane
                            var mf = r.GetComponent<MeshFilter>();
                            if (mf && mf.sharedMesh)
                            {
                                var mesh = Object.Instantiate(mf.sharedMesh);
                                var verts = mesh.vertices; var uv = new Vector2[verts.Length];
                                var off = (e.index * 0.37f + e.position.x * 0.13f) % 1f;   // deterministic per exit
                                for (int i = 0; i < verts.Length; i++) uv[i] = new Vector2(verts[i].x / 1.1f + off, verts[i].y / 1.1f);
                                mesh.uv = uv; mesh.RecalculateTangents(); mf.sharedMesh = mesh;
                            }
                        }
                    var v = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.DestroyImmediate(v.GetComponent<Collider>());
                    v.name = "void"; v.layer = ArchLayer;
                    v.transform.SetParent(go.transform, false);
                    v.transform.localPosition = new Vector3(0, 1.01f, 0.0015f);
                    v.transform.localRotation = Quaternion.Euler(0, 180, 0);
                    v.transform.localScale = new Vector3(0.86f, 2.02f, 1);
                    if (kit.voidMat) v.GetComponent<Renderer>().sharedMaterial = kit.voidMat;
                }
                var col = go.AddComponent<BoxCollider>();
                float h = e.form == ExitForm.VentHatch ? 0.48f : e.form == ExitForm.Elevator ? 2.35f : 2.05f;
                col.center = new Vector3(0, h * 0.5f, 0.03f); col.size = new Vector3(e.width, h, 0.06f);
                var trig = go.AddComponent<ExitTrigger>();
                trig.Bind(e);
                e.instance = trig;
            }
        }

        // ------------------------------------------------------------------ architecture

        void BuildArchitecture(LayoutGenerator.Layout L, List<LayoutGenerator.WallRun> runs, Transform parent, Rng rng, List<ExitPlan> exits)
        {
            float T = LayoutGenerator.WallT, H = L.height;
            var wb = L.WorldBounds;
            // one floor slab, one ceiling collider; visual ceiling with holes for recessed troffers
            // thick slab (top at y=0): anything pressed into the floor is pushed back UP, never through it
            if (basinsForArch.Count == 0)
                Slab(parent, "Floor", new Vector3(wb.center.x, -1.5f, wb.center.z), new Vector3(wb.size.x + 1, 3f, wb.size.z + 1), floorM, 1.6f);
            else
                FloorWithBasins(parent, new Rect(wb.min.x - 0.5f, wb.min.z - 0.5f, wb.size.x + 1, wb.size.z + 1), basinsForArch);
            var spots = new List<(Vector3 pos, bool rotated)>();
            bool openSky = glassWalls;   // Frutiger Aero: no ceiling at all, the sky is the light
            var ceil = openSky ? null : Slab(parent, "Ceiling", new Vector3(wb.center.x, H + 0.1f, wb.center.z), new Vector3(wb.size.x + 1, 0.2f, wb.size.z + 1), ceilM, 1.2f);
            if (openSky) goto Walls;
            spotMode.Clear(); darkRects.Clear();
            foreach (var room in L.rooms) if (room.dark) darkRects.Add(room.World);
            var lr = rng.Fork("tubes");
            foreach (var room in L.rooms)
            {
                foreach (var s in TrofferSpots(room.World, H))
                {
                    if (world == "CV") continue;   // caves: no fixtures in the rock, a few glows instead (CaveLights)
                    spots.Add(s);
                    // 0 lit, 1 dead, 2 failing (flickers), 3 dim
                    float v = lr.Value;
                    int mode = room.dark ? (v < 0.72f ? 1 : v < 0.9f ? 2 : 3) : (v < 0.035f ? 2 : v < 0.05f ? 1 : 0);
                    if (world == "PG" && mode == 0) mode = v < 0.12f ? 1 : v < 0.2f ? 2 : 0;          // half the garage tubes are dead or dying
                    if (world == "HT" && mode == 0 && ((int)(s.Item1.x * 2 + s.Item1.z * 3) & 1) == 0) mode = 1;   // hotel: every other light off
                    if (world == "OF" && mode == 0) mode = v < 0.2f ? 1 : v < 0.3f ? 2 : 0;           // the abandoned office: a fifth of the tubes dead
                    if (world == "LO") mode = v < 0.86f ? 1 : 2;                                          // lights out: only a few tubes still flicker
                    if (world == "WH" && mode == 0) mode = v < 0.25f ? 1 : 0;
                    if (world == "EL" && mode == 0) mode = v < 0.3f ? 1 : v < 0.45f ? 2 : 0;          // power station: emergency lighting only, half of it failing
                    if (world == "RN" && mode == 0) mode = v < 0.3f ? 2 : 0;                          // Level Run: the alarm lights pulse
                    if (world == "LB" && mode == 0) mode = v < 0.18f ? 1 : v < 0.4f ? 3 : 0;
                    if (world == "FL" && mode == 0) mode = v < 0.1f ? 1 : v < 0.18f ? 2 : 0;
                    if (mode != 0) spotMode[SpotKey(s.Item1)] = mode;
                }
            }
            if (lights && kit.trofferPrefab)
            {
                Object.DestroyImmediate(ceil.GetComponent<MeshRenderer>()); Object.DestroyImmediate(ceil.GetComponent<MeshFilter>());
                var plane = new GameObject("CeilingPlane") { layer = ArchLayer, isStatic = true };
                plane.transform.SetParent(parent);
                var holes = spots.Select(s => { var hs = s.rotated ? new Vector2(0.31f, 0.61f) : new Vector2(0.61f, 0.31f); return new Rect(s.pos.x - hs.x, s.pos.z - hs.y, hs.x * 2, hs.y * 2); }).ToList();
                plane.AddComponent<MeshFilter>().sharedMesh = TestLevelBuilder.CeilingWithHoles(new Rect(wb.min.x - 0.5f, wb.min.z - 0.5f, wb.size.x + 1, wb.size.z + 1), holes, H, 1.2f);
                plane.AddComponent<MeshRenderer>().sharedMaterial = ceilM;
            }
            Walls:
            // walls: horizontal runs extended by T/2 at both ends close the corners; vertical runs stop at them
            foreach (var r in runs)
            {
                if (r.vertical)
                {
                    float len = r.to - r.from - T; if (len <= 0.01f) continue;
                    var c = new Vector3(r.line, H * 0.5f, (r.from + r.to) * 0.5f);
                    bool glassV = glassWalls && kit.glassMat && r.line > 0.01f && r.line < L.w * LayoutGenerator.Cell - 0.01f && rng.Chance(0.35f);
                    var wv = Slab(parent, glassV ? "Glass" : "Wall", c, new Vector3(T * (glassV ? 0.25f : 1f), H, len), glassV ? kit.glassMat : wallM, 1.1f);
                    if (glassV) wv.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                else
                {
                    var c = new Vector3((r.from + r.to) * 0.5f, H * 0.5f, r.line);
                    bool glassH = glassWalls && kit.glassMat && r.line > 0.01f && r.line < L.h * LayoutGenerator.Cell - 0.01f && rng.Chance(0.35f);
                    var wh = Slab(parent, glassH ? "Glass" : "Wall", c, new Vector3(r.to - r.from + (glassH ? 0 : T), H, T * (glassH ? 0.25f : 1f)), glassH ? kit.glassMat : wallM, 1.1f);
                    if (glassH) wh.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            // lintels over doors + frames
            foreach (var o in L.openings)
            {
                if (o.kind != LayoutGenerator.OpeningKind.Door) continue;
                var c = o.Center; float len = o.to - o.from;
                var size = o.vertical ? new Vector3(T, H - 2.15f, len) : new Vector3(len, H - 2.15f, T);
                Slab(parent, "Lintel", c + Vector3.up * (2.15f + (H - 2.15f) * 0.5f), size, wallM, 1.1f);
                if (kit.doorFramePrefab)
                {
                    var fr = Object.Instantiate(kit.doorFramePrefab, parent);
                    fr.name = "DoorFrame"; fr.layer = ArchLayer;
                    fr.transform.SetPositionAndRotation(c, Quaternion.LookRotation(o.vertical ? Vector3.right : Vector3.forward));
                    fr.transform.localScale = new Vector3(LayoutGenerator.DoorW / 1.1f, 1, 1); // model is made for 1.1 m
                    foreach (var sx in new[] { -1f, 1f })
                    {
                        var bc = fr.AddComponent<BoxCollider>();
                        bc.center = new Vector3(sx * (LayoutGenerator.DoorW * 0.5f + 0.045f), 1.11f, 0); bc.size = new Vector3(0.09f, 2.22f, 0.22f);
                    }
                }
            }
            // baseboards on every inner wall face
            if (kit.baseboardPrefab && wallM == kit.wallMat)
                foreach (var room in L.rooms)
                    foreach (var w in room.spec.walls)
                    {
                        // run the baseboard into the corners (walls specs are trimmed by T/2 for placement)
                        var a0 = w.a - w.Dir * (T * 0.5f); var b0 = w.b + w.Dir * (T * 0.5f);
                        // cut gaps where an exit sits on this wall face
                        var cuts = new List<(float, float)>();
                        foreach (var e in exits)
                            if (Vector3.Dot(e.normal, w.normal) > 0.99f && Mathf.Abs(Vector3.Dot(e.position - w.a, w.normal)) < 0.01f)
                            {
                                float t = Vector3.Dot(e.position - a0, w.Dir);
                                cuts.Add((t - e.width * 0.5f, t + e.width * 0.5f));
                            }
                        cuts.Sort((x, y) => x.Item1.CompareTo(y.Item1));
                        float total = (b0 - a0).magnitude, cur = 0;
                        var pieces = new List<(float, float)>();
                        foreach (var (c0, c1) in cuts) { if (c0 > cur) pieces.Add((cur, c0)); cur = Mathf.Max(cur, c1); }
                        if (cur < total) pieces.Add((cur, total));
                        foreach (var (t0, t1) in pieces)
                        {
                        if (t1 - t0 < 0.05f) continue;
                        var a = a0 + w.Dir * t0; var b = a0 + w.Dir * t1;
                        var bb = Object.Instantiate(kit.baseboardPrefab, parent);
                        bb.name = "Baseboard"; bb.layer = ArchLayer;
                        bb.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(w.normal, Vector3.up));
                        bb.transform.localScale = new Vector3((b - a).magnitude, 1, 1);
                        var col = bb.AddComponent<BoxCollider>();
                        col.center = new Vector3(0, TestLevelBuilder.BaseboardHeight * 0.5f, TestLevelBuilder.BaseboardDepth * 0.5f);
                        col.size = new Vector3(1, TestLevelBuilder.BaseboardHeight, TestLevelBuilder.BaseboardDepth);
                        }
                    }
            // columns: a grid of square pillars in big halls (kept off doors, exits and troffers)
            foreach (var room in L.rooms)
            {
                if ((room.kind != "pillars" && room.kind != "parking") || openSky) continue;
                var b = room.spec.bounds; const float P = 3.0f, S = 0.5f;
                int nx = Mathf.FloorToInt((b.size.x - 2.4f) / P) + 1, nz = Mathf.FloorToInt((b.size.z - 2.4f) / P) + 1;
                if (nx < 2 || nz < 2) continue;
                float x0 = b.center.x - (nx - 1) * P * 0.5f, z0 = b.center.z - (nz - 1) * P * 0.5f;
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                    {
                        var p = new Vector3(x0 + ix * P, 0, z0 + iz * P);
                        if (room.spec.doors.Any(d => Vector2.Distance(new Vector2(d.x, d.z), new Vector2(p.x, p.z)) < 1.6f)) continue;
                        if (exits.Any(e => Vector2.Distance(new Vector2(e.position.x, e.position.z), new Vector2(p.x, p.z)) < 2.4f)) continue;
                        if (spots.Any(s => Mathf.Abs(s.pos.x - p.x) < 0.75f && Mathf.Abs(s.pos.z - p.z) < 0.75f)) continue;
                        Slab(parent, "Pillar", new Vector3(p.x, H * 0.5f, p.z), new Vector3(S, H, S), wallM, 1.1f);
                    }
            }
            if (world == "PG") ParkingLines(L, parent, rng.Fork("lines"));
            if (world == "HT") HotelDoors(L, parent, rng.Fork("doors"), exits);
            if (world == "PD" || world == "EL") Pipes(L, parent, rng.Fork("pipes"));
            if (world == "CV") CaveLights(L, parent, rng.Fork("glow"));
            if (world == "FL") FloodWater(L, parent);
            if (lights && !openSky) Lights(spots, parent);
        }

        string world = "BR";

        /// <summary>Painted parking stalls along the walls of every section: white lines, a yellow kerb band, a few numbers worn off.</summary>
        /// <summary>Level 2: bundles of pipes run along every wall under the ceiling.</summary>
        void Pipes(LayoutGenerator.Layout L, Transform parent, Rng rng)
        {
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder); var cm = cyl.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(cyl);
            var mat = kit.pipeMat ? kit.pipeMat : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            foreach (var room in L.rooms)
                foreach (var w in room.spec.walls)
                {
                    var along = w.b - w.a; float len = along.magnitude; if (len < 0.8f) continue; along /= len;
                    int n = rng.Range(1, 4);
                    for (int i = 0; i < n; i++)
                    {
                        float r = rng.Range(0.035f, 0.09f), y = w.height - 0.12f - i * 0.22f, off = 0.1f + r + (i % 2) * 0.07f;
                        var g = new GameObject("Pipe") { layer = ArchLayer, isStatic = true }; g.transform.SetParent(parent);
                        g.transform.position = (w.a + w.b) * 0.5f + w.normal * off + Vector3.up * y;
                        g.transform.rotation = Quaternion.FromToRotation(Vector3.up, along);
                        g.transform.localScale = new Vector3(r * 2, len * 0.5f, r * 2);
                        g.AddComponent<MeshFilter>().sharedMesh = cm; var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                }
        }

        /// <summary>Level 8: no lamps in the rock. Here and there a warm glow low over the floor (a dropped flare, a lantern).</summary>
        void CaveLights(LayoutGenerator.Layout L, Transform parent, Rng rng)
        {
            foreach (var room in L.rooms)
            {
                if (!rng.Chance(room.id == L.startRoom ? 1f : 0.45f)) continue;
                var w = room.World;
                var lg = new GameObject("CaveGlow"); lg.transform.SetParent(parent);
                lg.transform.position = new Vector3(rng.Range(w.xMin + 0.6f, w.xMax - 0.6f), 0.5f, rng.Range(w.yMin + 0.6f, w.yMax - 0.6f));
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = rng.Range(4.5f, 7f); l.intensity = rng.Range(1.4f, 2.4f);
                l.color = rng.Chance(0.75f) ? new Color(1f, 0.52f, 0.22f) : new Color(0.55f, 1f, 0.6f);   // flares and glow sticks
                l.shadows = LightShadows.None;
                if (rng.Chance(0.5f)) lg.AddComponent<Flicker>().seed = lg.transform.position.x * 3.1f;
            }
        }

        /// <summary>Flooded rooms: still water a hand deep over every floor.</summary>
        void FloodWater(LayoutGenerator.Layout L, Transform parent)
        {
            var b = L.WorldBounds;
            var w = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(w.GetComponent<Collider>());
            w.name = "FloodWater"; w.transform.SetParent(parent);
            w.transform.position = new Vector3(b.center.x, 0.14f, b.center.z); w.transform.rotation = Quaternion.Euler(90, 0, 0);
            w.transform.localScale = new Vector3(b.size.x + 2, b.size.z + 2, 1);
            var r = w.GetComponent<Renderer>(); r.sharedMaterial = kit.waterMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void ParkingLines(LayoutGenerator.Layout L, Transform parent, Rng rng)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var white = new Material(lit) { name = "paint_white" }; white.SetColor("_BaseColor", new Color(0.78f, 0.78f, 0.74f)); white.SetFloat("_Smoothness", 0.15f);
            var yellow = new Material(lit) { name = "paint_yellow" }; yellow.SetColor("_BaseColor", new Color(0.75f, 0.6f, 0.12f)); yellow.SetFloat("_Smoothness", 0.15f);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); var qm = quad.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(quad);
            void Stripe(Vector3 c, float yaw, float len, float wid, Material m)
            {
                var g = new GameObject("Stripe") { layer = ArchLayer, isStatic = true }; g.transform.SetParent(parent);
                g.transform.SetPositionAndRotation(c + Vector3.up * 0.004f, Quaternion.Euler(90, yaw, 0)); g.transform.localScale = new Vector3(wid, len, 1);
                g.AddComponent<MeshFilter>().sharedMesh = qm; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (var room in L.rooms)
                foreach (var w in room.spec.walls)
                {
                    var along = (w.b - w.a); float len = along.magnitude; if (len < 3f) continue; along /= len;
                    float yaw = Mathf.Atan2(w.normal.x, w.normal.z) * Mathf.Rad2Deg;
                    // stall dividers every 2.5 m, 4.6 m long, perpendicular to the wall
                    for (float t = 1.25f; t < len - 0.5f; t += 2.5f)
                    {
                        if (rng.Chance(0.12f)) continue;   // worn away
                        Stripe(w.a + along * t + w.normal * 2.4f, yaw, 4.6f, 0.11f, white);
                    }
                    Stripe((w.a + w.b) * 0.5f + w.normal * 0.12f, yaw + 90f, len - 0.3f, 0.16f, yellow);   // kerb band
                }
        }

        /// <summary>Room doors along the hotel corridors: dark wood, a handle, a number plate; locked (decor only).</summary>
        void HotelDoors(LayoutGenerator.Layout L, Transform parent, Rng rng, List<ExitPlan> exits)
        {
            if (!kit.exitDoorPrefab) return;
            int number = 100 + rng.Range(0, 800);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var wood = new Material(lit) { name = "hotel_door" }; wood.SetColor("_BaseColor", new Color(0.26f, 0.15f, 0.09f)); wood.SetFloat("_Smoothness", 0.35f);
            var placedDoors = new List<Vector3>();
            foreach (var room in L.rooms)
                foreach (var w in room.spec.walls)
                {
                    var along = (w.b - w.a); float len = along.magnitude; if (len < 2.4f) continue; along /= len;
                    for (float t = 1.4f; t < len - 1.0f; t += rng.Range(3.0f, 4.2f))
                    {
                        var p = w.a + along * t;
                        bool nearExit = false; foreach (var e in exits) if (Vector3.Distance(new Vector3(e.position.x, 0, e.position.z), new Vector3(p.x, 0, p.z)) < 2.6f) nearExit = true;
                        if (nearExit) continue;
                        bool crowded = false; foreach (var q in placedDoors) if ((q - p).sqrMagnitude < 2.1f * 2.1f) crowded = true;
                        if (crowded) continue;
                        placedDoors.Add(p);
                        var go = Object.Instantiate(kit.exitDoorPrefab, parent); go.name = "HotelDoor" + (number++);
                        go.transform.SetPositionAndRotation(new Vector3(p.x, 0, p.z), Quaternion.LookRotation(w.normal, Vector3.up));   // back on the wall face, like the exits
                        foreach (var t2 in go.GetComponentsInChildren<Transform>(true)) { t2.gameObject.layer = ArchLayer; t2.gameObject.isStatic = true; }
                        Renderer leafR = null;
                        foreach (var r in go.GetComponentsInChildren<Renderer>())
                            if (r.name == "leaf") { leafR = r; var m = r.sharedMaterials; for (int i = 0; i < m.Length; i++) m[i] = wood; r.sharedMaterials = m; }
                        // the handle sits low in the model: lift it (only vertically) to 1 m
                        foreach (var hr in go.GetComponentsInChildren<Renderer>(true))
                            if (hr.transform.name == "door_handle" || (hr.transform.parent && hr.transform.parent.name == "door_handle"))
                            { var ht = hr.transform.name == "door_handle" ? hr.transform : hr.transform.parent; ht.position += Vector3.up * (1.0f - hr.bounds.center.y); break; }
                    }
                }
        }

        readonly Dictionary<Vector2Int, int> spotMode = new();
        readonly List<Rect> darkRects = new();
        Material deadTubeM;
        float DistToDark(Vector3 p)
        {
            float best = 99f;
            foreach (var r in darkRects)
            {
                float dx = Mathf.Max(r.xMin - p.x, 0, p.x - r.xMax), dz = Mathf.Max(r.yMin - p.z, 0, p.z - r.yMax);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }
        static Vector2Int SpotKey(Vector3 p) => new(Mathf.RoundToInt(p.x * 20), Mathf.RoundToInt(p.z * 20));

        // ------------------------------------------------------------------ Poolrooms

        const float BasinDepth = 1.2f, Deck = 1.1f, StepH = 0.2f, StepD = 0.3f, WaterY = -0.12f;

        void PlanBasins(LayoutGenerator.Layout L, Result res, Rng rng, HashSet<int> exitRooms)
        {
            foreach (var room in L.rooms)
            {
                if (room.kind == "corridor") continue;
                if (exitRooms.Contains(room.id) || room.id == L.startRoom) { room.kind = "empty"; continue; }   // exits & spawn keep a dry floor
                float k = rng.Value;
                room.kind = k < 0.55f ? "pool" : k < 0.85f ? "empty" : "lounge";
                if (room.kind != "pool") continue;
                var b = room.spec.bounds;
                float w = b.size.x - 2 * Deck, d = b.size.z - 2 * Deck;
                if (w < 2.5f || d < 2.5f) { room.kind = "empty"; continue; }
                var rect = new Rect(b.min.x + Deck, b.min.z + Deck, w, d);
                res.basins.Add(rect);
                room.spec.keepOut.Add(rect);
                room.spec.kind = "pool";
            }
        }

        /// <summary>Floor slab with rectangular holes for basins: colliders from the grid decomposition, visual from the holed plane.</summary>
        void FloorWithBasins(Transform parent, Rect area, List<Rect> holes)
        {
            var go = new GameObject("Floor") { layer = ArchLayer, isStatic = true };
            go.transform.SetParent(parent);
            var mesh = TestLevelBuilder.CeilingWithHoles(area, holes, 0f, 1.6f);
            // flip to face up
            var tris = mesh.triangles; for (int i = 0; i < tris.Length; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            mesh.triangles = tris; var n = mesh.normals; for (int i = 0; i < n.Length; i++) n[i] = Vector3.up; mesh.normals = n; mesh.RecalculateTangents();
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = floorM;
            var xs = new List<float> { area.xMin, area.xMax }; var zs = new List<float> { area.yMin, area.yMax };
            foreach (var h in holes) { xs.Add(h.xMin); xs.Add(h.xMax); zs.Add(h.yMin); zs.Add(h.yMax); }
            xs.Sort(); zs.Sort();
            for (int i = 0; i < xs.Count - 1; i++)
                for (int j = 0; j < zs.Count - 1; j++)
                {
                    float x0 = xs[i], x1 = xs[i + 1], z0 = zs[j], z1 = zs[j + 1];
                    if (x1 - x0 < 1e-4f || z1 - z0 < 1e-4f) continue;
                    var c = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
                    if (holes.Any(h => h.Contains(c))) continue;
                    var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(c.x, -1.5f, c.y); bc.size = new Vector3(x1 - x0, 3f, z1 - z0);
                }
        }

        /// <summary>Frutiger Aero ponds: 2-4 shallow pools in the park, never at the start or in front of an exit.</summary>
        void PlanPonds(LayoutGenerator.Layout L, Result res, Rng rng, Vector3 start)
        {
            var wb = L.WorldBounds; int n = rng.Range(2, 5);
            for (int t = 0; t < 60 && res.basins.Count < n; t++)
            {
                float w = rng.Range(3f, 7f), d = rng.Range(3f, 7f);
                var r = new Rect(rng.Range(wb.min.x + 2.5f, wb.max.x - 2.5f - w), rng.Range(wb.min.z + 2.5f, wb.max.z - 2.5f - d), w, d);
                var grow = new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3f, r.height + 3f);
                if (grow.Contains(new Vector2(start.x, start.z)) || Vector2.Distance(r.center, new Vector2(start.x, start.z)) < 7f) continue;
                if (res.exits.Any(e => Vector2.Distance(r.center, new Vector2(e.position.x, e.position.z)) < Mathf.Max(w, d) * 0.5f + 5f)) continue;
                if (res.basins.Any(b => new Rect(b.x - 2, b.y - 2, b.width + 4, b.height + 4).Overlaps(r))) continue;
                res.basins.Add(r); L.rooms[0].spec.keepOut.Add(r);
            }
        }

        /// <summary>Open sky for Frutiger Aero: warm sun with soft shadows, a bright procedural sky.</summary>
        void BuildSky(LayoutGenerator.Layout L, Transform parent, Rng rng)
        {
            // the park's edge is a low glass balustrade; beyond it the white plaza goes on into the haze
            var wb = L.WorldBounds;
            foreach (Transform t in parent) if (t.name == "Wall") { var s = t.localScale; }
            foreach (var mr in parent.GetComponentsInChildren<MeshRenderer>())
                if (mr.name == "Wall")
                {
                    var tr = mr.transform; var mf = mr.GetComponent<MeshFilter>(); var b = mr.bounds;
                    var go = Slab(parent, "Glass", new Vector3(b.center.x, 0.6f, b.center.z), new Vector3(Mathf.Max(0.06f, b.size.x < 0.5f ? 0.05f : b.size.x), 1.2f, Mathf.Max(0.06f, b.size.z < 0.5f ? 0.05f : b.size.z)), kit.glassMat ? kit.glassMat : wallM, 1.1f);
                    go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var rail = Slab(parent, "Rail", new Vector3(b.center.x, 1.22f, b.center.z), new Vector3(b.size.x < 0.5f ? 0.07f : b.size.x, 0.05f, b.size.z < 0.5f ? 0.07f : b.size.z), kit.aeroWallMat ? kit.aeroWallMat : wallM, 1.1f);
                    Object.DestroyImmediate(mr.gameObject);
                }
            var plaza = new GameObject("Plaza") { layer = ArchLayer, isStatic = true }; plaza.transform.SetParent(parent);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(plaza.transform); q.name = "PlazaFloor";
            q.transform.SetPositionAndRotation(new Vector3(wb.center.x, -0.01f, wb.center.z), Quaternion.Euler(90, 0, 0)); q.transform.localScale = new Vector3(600, 600, 1);
            var pm = new Material(floorM); pm.mainTextureScale = new Vector2(600 / 1.6f, 600 / 1.6f); if (pm.HasProperty("_BaseMap")) pm.SetTextureScale("_BaseMap", new Vector2(600 / 1.6f, 600 / 1.6f));
            q.GetComponent<Renderer>().sharedMaterial = pm;

            var sun = new GameObject("Sun"); sun.transform.SetParent(parent);
            sun.transform.rotation = Quaternion.Euler(rng.Range(48f, 62f), rng.Range(0f, 360f), 0);
            var l = sun.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.25f; l.color = new Color(1f, 0.97f, 0.9f);
            l.shadows = LightShadows.Soft; l.shadowStrength = 0.55f;
            RenderSettings.sun = l;
            Clouds(L, parent, rng);
        }

        /// <summary>Frutiger Aero sky: big soft cumulus billboards all around, high up.</summary>
        /// <summary>Frutiger Aero: the endless open world (hills, lakes, rivers, houses with the exits inside).</summary>
        Result BuildAero(Result res, Rng rng, Transform arch, Transform props)
        {
            res.rare = ""; res.rooms = new List<RoomSpec>();
            res.engine = new PlacementEngine(ArchLayer, PropsLayer, props);
            var world = arch.gameObject.AddComponent<AeroWorld>();
            world.buildExit = (plan, parent) => { BuildArchway(plan, parent); return plan.instance; };
            world.Init(res, kit, kit.aeroGrassMat ? LushGrass(kit.aeroGrassMat) : floorM, props);
            var sun = new GameObject("Sun"); sun.transform.SetParent(arch);
            sun.transform.rotation = Quaternion.Euler(rng.Range(45f, 60f), rng.Range(0f, 360f), 0);
            var l = sun.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.3f; l.color = new Color(1f, 0.97f, 0.9f); l.shadows = LightShadows.Soft; l.shadowStrength = 0.6f;
            RenderSettings.sun = l;
            Clouds(Vector3.zero, arch, rng);
            res.start = new Vector3(0, world.Height(0, 0) + 0.05f, 0); res.startYaw = rng.Range(0, 4) * 90f;
            res.nav = new NavGrid(new Bounds(res.start, new Vector3(24, 2, 24)), 0.5f);
            res.nav.SetWalkable(new Bounds(res.start, new Vector3(20, 2, 20)), true);
            return res;
        }

        void Clouds(LayoutGenerator.Layout L, Transform parent, Rng rng) => Clouds(L.WorldBounds.center, parent, rng);
        void Clouds(Vector3 c, Transform parent, Rng rng)
        {
            var mat = Resources.Load<Material>("CloudMat"); if (!mat) return;
            mat = new Material(mat); mat.SetTexture("_BaseMap", CloudTex());
            var root = new GameObject("Clouds"); root.transform.SetParent(parent); root.AddComponent<Address.Rendering.FollowViewerXZ>();
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Quad); var qm = mesh.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(mesh);
            for (int i = 0; i < (Application.isMobilePlatform ? 14 : 34); i++)
            {
                float a = rng.Range(0f, 360f), d = rng.Range(85f, 112f), h = rng.Range(32f, 62f);   // inside the 120 m far plane
                var p = c + Quaternion.Euler(0, a, 0) * Vector3.forward * d + Vector3.up * h;
                var go = new GameObject("cloud"); go.transform.SetParent(root.transform);
                go.transform.position = p; go.transform.rotation = Quaternion.LookRotation(p - new Vector3(c.x, h * 0.6f, c.z));
                float w = rng.Range(40f, 85f); go.transform.localScale = new Vector3(w, w * rng.Range(0.4f, 0.55f), 1);
                go.AddComponent<Address.Rendering.Billboard>();
                go.AddComponent<MeshFilter>().sharedMesh = qm; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
        }

        static Material lush;
        /// <summary>The Aero lawn is a saturated, sunny green (the scanned grass alone reads dull under the sky).</summary>
        static Material LushGrass(Material src) { if (!lush) { lush = new Material(src); lush.SetColor("_BaseColor", new Color(1.05f, 1.35f, 0.75f)); } return lush; }

        static Texture2D cloudTex;
        static Texture2D CloudTex()
        {
            if (cloudTex) return cloudTex;
            int W = 256, H = 128; cloudTex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var rnd = new System.Random(5); var blobs = new System.Collections.Generic.List<(float x, float y, float r)>();
            for (int i = 0; i < 14; i++) blobs.Add(((float)(0.18 + rnd.NextDouble() * 0.64), (float)(0.3 + rnd.NextDouble() * 0.25), (float)(0.09 + rnd.NextDouble() * 0.13)));
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H, d = 0, shade = 0;
                foreach (var b in blobs) { float dx = (u - b.x) / b.r, dy = (v - b.y) / (b.r * 2f); float k = Mathf.Clamp01(1 - (dx * dx + dy * dy)); d = Mathf.Max(d, k); shade += k * (v - b.y); }
                float n = Mathf.PerlinNoise(u * 9f, v * 9f) * 0.35f + 0.65f;
                // soft edges everywhere (fade to 0 well inside the card), puffy top, flat-ish bottom
                float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v)) * 6f);
                float a = Mathf.SmoothStep(0, 1, Mathf.Clamp01(d * 1.3f * n - 0.15f)) * edge * 0.92f;
                float lit = Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(v * 1.6f));    // flat, slightly grey underside
                cloudTex.SetPixel(x, y, new Color(lit, lit, Mathf.Min(1, lit + 0.03f), a));
            }
            cloudTex.Apply(); return cloudTex;
        }

        /// <summary>Glossy bubbles rising out of the water (the Aero signature).</summary>
        void Bubbles(Rect basin, Transform parent)
        {
            var mat = Resources.Load<Material>("BubbleMat"); if (!mat) return;
            var go = new GameObject("Bubbles"); go.transform.SetParent(parent);
            go.transform.position = new Vector3(basin.center.x, 0.1f, basin.center.y);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.28f); main.maxParticles = 90; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.01f;
            var em = ps.emission; em.rateOverTime = Mathf.Clamp(basin.width * basin.height * 0.5f, 3f, 14f);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(basin.width * 0.8f, 0.05f, basin.height * 0.8f);
            var nz = ps.noise; nz.enabled = true; nz.strength = 0.12f; nz.frequency = 0.4f;
            var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.9f, 0.8f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); var m = new Material(mat); m.SetTexture("_BaseMap", BubbleTex()); r.sharedMaterial = m;
            ps.Play();
        }

        static Texture2D bubbleTex;
        static Texture2D BubbleTex()
        {
            if (bubbleTex) return bubbleTex;
            int N = 64; bubbleTex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1, d = Mathf.Sqrt(u * u + v * v);
                float rim = Mathf.Clamp01(1 - Mathf.Abs(d - 0.88f) * 9f) * 0.9f;
                float body = d < 0.9f ? 0.08f + 0.18f * d * d : 0;
                float hi = Mathf.Clamp01(1 - Mathf.Sqrt((u + 0.35f) * (u + 0.35f) + (v - 0.4f) * (v - 0.4f)) * 4.5f);
                float a = Mathf.Clamp01(rim + body + hi) * (d < 1 ? 1 : 0);
                bubbleTex.SetPixel(x, y, new Color(0.85f + hi * 0.15f, 0.97f, 1f, a));
            }
            bubbleTex.Apply(); return bubbleTex;
        }

        void BuildArchway(ExitPlan e, Transform parent)
        {
            // a small tiled pavilion standing against the park wall: inside it smells of chlorine - a water channel,
            // cyan light and a glowing end where the Poolrooms begin. No door: you just walk in.
            var go = new GameObject($"Exit{e.index}_Archway_{e.role}") { layer = ArchLayer };
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(e.position, Quaternion.LookRotation(e.normal, Vector3.up));
            var tile = kit.poolTileMat ? kit.poolTileMat : wallM;
            float W = 1.7f, D = 3.2f, Hh = 2.6f, t = 0.18f;
            if (kit.exitPavilionPrefab)
            {
                // the Blender pavilion: glass barrel vault on white ribs; invisible walls keep you inside the tunnel
                var pv = Object.Instantiate(kit.exitPavilionPrefab, go.transform); pv.name = "pavilion";
                pv.transform.localPosition = Vector3.zero; pv.transform.localRotation = Quaternion.identity;
                pv.transform.localScale = new Vector3(1.3f, 1.85f, 1f);              // 2.4 m high, 3.2 m wide: you walk in upright
                foreach (var r in pv.GetComponentsInChildren<Renderer>())
                {
                    var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) if (ms[i] && ms[i].name.Contains("glass") && kit.glassMat) ms[i] = kit.glassMat;
                    r.sharedMaterials = ms;
                }
                foreach (var sx in new[] { -1f, 1f }) { var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(sx * 1.66f, 1.2f, 1.7f); bc.size = new Vector3(0.12f, 2.4f, 3.4f); }
                var roof = go.AddComponent<BoxCollider>(); roof.center = new Vector3(0, 2.5f, 1.7f); roof.size = new Vector3(3.4f, 0.08f, 3.4f);
                W = 2.6f; D = 3.4f; Hh = 2.2f;
                goto Inside;
            }
            GameObject Part(string n, Vector3 c, Vector3 size, Material m)
            {
                var p = Slab(go.transform, n, Vector3.zero, size, m, 0.6f); p.transform.localPosition = c; p.transform.localRotation = Quaternion.identity; p.isStatic = false; return p;
            }
            Part("side_l", new Vector3(-W * 0.5f - t * 0.5f, Hh * 0.5f, D * 0.5f), new Vector3(t, Hh, D), tile);
            Part("side_r", new Vector3(W * 0.5f + t * 0.5f, Hh * 0.5f, D * 0.5f), new Vector3(t, Hh, D), tile);
            Part("roof", new Vector3(0, Hh + 0.09f, D * 0.5f), new Vector3(W + 2 * t + 0.2f, 0.18f, D + 0.2f), tile);
            Part("lintel", new Vector3(0, Hh - 0.3f, D - 0.09f), new Vector3(W, 0.6f, 0.18f), tile);
            Part("step", new Vector3(0, 0.02f, D * 0.5f), new Vector3(W, 0.04f, D), tile);
            Inside:
            if (kit.waterMat)
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(w.GetComponent<Collider>());
                w.name = "channel"; w.transform.SetParent(go.transform, false);
                w.transform.localPosition = new Vector3(0, 0.045f, D * 0.5f - 0.2f); w.transform.localRotation = Quaternion.Euler(90, 0, 0); w.transform.localScale = new Vector3(0.6f, D - 0.6f, 1);
                w.GetComponent<Renderer>().sharedMaterial = kit.waterMat;
            }
            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(glow.GetComponent<Collider>());
            glow.name = "glow"; glow.transform.SetParent(go.transform, false);
            glow.transform.localPosition = new Vector3(0, Hh * 0.45f, 0.03f); glow.transform.localRotation = Quaternion.Euler(0, 180, 0); glow.transform.localScale = new Vector3(W * 0.55f, Hh * 0.55f, 1);
            if (kit.lightMat) glow.GetComponent<Renderer>().sharedMaterial = kit.lightMat;
            var lg = new GameObject("Light"); lg.transform.SetParent(go.transform, false); lg.transform.localPosition = new Vector3(0, 2.0f, 1.2f);
            var li = lg.AddComponent<Light>(); li.type = LightType.Point; li.range = 4.5f; li.intensity = 2.2f; li.color = new Color(0.6f, 0.95f, 1f); li.shadows = LightShadows.None;
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = ArchLayer;
            var trig = go.AddComponent<ExitTrigger>(); trig.Bind(e); e.instance = trig; trig.Activate();
        }

        void BuildBasins(Result res, Transform parent)
        {
            float T = 0.15f;
            int k = 0;
            foreach (var r in res.basins)
            {
                var c = new Vector3(r.center.x, 0, r.center.y);
                // basin floor + four inner walls (aqua tiles), all below the deck
                Slab(parent, "BasinFloor", c + Vector3.down * (BasinDepth + 1.0f), new Vector3(r.width + 2 * T, 2f, r.height + 2 * T), kit.basinMat, 1.0f);
                Slab(parent, "BasinWall", new Vector3(r.xMin - T * 0.5f, -BasinDepth * 0.5f, c.z), new Vector3(T, BasinDepth, r.height + 2 * T), kit.basinMat, 1.0f);
                Slab(parent, "BasinWall", new Vector3(r.xMax + T * 0.5f, -BasinDepth * 0.5f, c.z), new Vector3(T, BasinDepth, r.height + 2 * T), kit.basinMat, 1.0f);
                Slab(parent, "BasinWall", new Vector3(c.x, -BasinDepth * 0.5f, r.yMin - T * 0.5f), new Vector3(r.width, BasinDepth, T), kit.basinMat, 1.0f);
                Slab(parent, "BasinWall", new Vector3(c.x, -BasinDepth * 0.5f, r.yMax + T * 0.5f), new Vector3(r.width, BasinDepth, T), kit.basinMat, 1.0f);
                // entry steps along the shorter side
                int n = Mathf.RoundToInt(BasinDepth / StepH);
                bool alongX = r.width >= r.height;
                for (int i = 0; i < n - 1; i++)
                {
                    float top = -StepH * (i + 1);
                    float run = StepD * (i + 1);
                    var size = alongX ? new Vector3(run, -top - 0f, r.height) : new Vector3(r.width, -top, run);
                    size = alongX ? new Vector3(StepD, BasinDepth + top, Mathf.Min(1.6f, r.height)) : new Vector3(Mathf.Min(1.6f, r.width), BasinDepth + top, StepD);
                    var pos = alongX ? new Vector3(r.xMin + StepD * (i + 0.5f), -BasinDepth + (BasinDepth + top) * 0.5f, c.z) : new Vector3(c.x, -BasinDepth + (BasinDepth + top) * 0.5f, r.yMin + StepD * (i + 0.5f));
                    Slab(parent, "Step", pos, size, kit.basinMat, 1.0f);
                }
                // water surface
                if (kit.waterMat)
                {
                    var w = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(w.GetComponent<Collider>());
                    w.name = "Water"; w.transform.SetParent(parent);
                    w.transform.SetPositionAndRotation(new Vector3(c.x, WaterY, c.z), Quaternion.Euler(90, 0, 0));
                    w.transform.localScale = new Vector3(r.width, r.height, 1);
                    w.GetComponent<Renderer>().sharedMaterial = kit.waterMat;
                    w.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                // caustics: a spot above the basin projecting an animated light pattern
                if (lights && kit.caustics != null && kit.caustics.Length > 0)
                {
                    var lg = new GameObject("Caustics"); lg.transform.SetParent(parent);
                    float h = res.layout.height + BasinDepth;
                    lg.transform.SetPositionAndRotation(new Vector3(c.x, res.layout.height - 0.05f, c.z), Quaternion.Euler(90, 0, 0));
                    var l = lg.AddComponent<Light>(); l.type = LightType.Spot; l.range = h + 0.4f;   // must not reach through walls
                    l.spotAngle = Mathf.Clamp(2f * Mathf.Atan(Mathf.Max(r.width, r.height) * 0.5f / h) * Mathf.Rad2Deg, 30f, 100f); l.innerSpotAngle = l.spotAngle * 0.7f;
                    l.intensity = 1.6f; l.color = new Color(0.8f, 1f, 1f); l.cookie = kit.caustics[0]; l.shadows = LightShadows.None;
                    lg.AddComponent<CausticAnimator>().Setup(kit.caustics, k++ * 0.37f);
                }
            }
        }

        static IEnumerable<(Vector3, bool)> TrofferSpots(Rect wr, float H)
        {
            int nx = Mathf.Max(1, Mathf.RoundToInt(wr.width / 2.4f)), nz = Mathf.Max(1, Mathf.RoundToInt(wr.height / 2.4f));
            bool rotated = wr.width <= wr.height;
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                    yield return (new Vector3(wr.xMin + (ix + 0.5f) * wr.width / nx, H, wr.yMin + (iz + 0.5f) * wr.height / nz), rotated);
        }

        void Lights(List<(Vector3 pos, bool rotated)> spots, Transform parent)
        {
            int k = 0;
            foreach (var (pos, rotated) in spots)
            {
                if (!kit.trofferPrefab) break;
                var tr = Object.Instantiate(kit.trofferPrefab, parent);
                tr.name = "Troffer"; tr.layer = ArchLayer;
                tr.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, rotated ? 90 : 0, 0));
                spotMode.TryGetValue(SpotKey(pos), out int mode);
                if (mode == 1)   // dead tube: the fixture stays, grey and cold
                {
                    if (!deadTubeM && kit.lightMat)
                    {
                        deadTubeM = new Material(kit.lightMat) { name = "troffer_dead" };
                        deadTubeM.SetColor("_BaseColor", new Color(0.32f, 0.31f, 0.28f)); deadTubeM.SetColor("_EmissionColor", Color.black); deadTubeM.DisableKeyword("_EMISSION");
                    }
                    foreach (var r in tr.GetComponentsInChildren<Renderer>())
                    {
                        var mats = r.sharedMaterials;
                        for (int mi = 0; mi < mats.Length; mi++) if (mats[mi] && mats[mi].name.StartsWith("troffer_diffuser") && deadTubeM) mats[mi] = deadTubeM;
                        r.sharedMaterials = mats;
                    }
                    continue;
                }
                foreach (var r in tr.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int mi = 0; mi < mats.Length; mi++) if (mats[mi] && mats[mi].name.StartsWith("troffer_diffuser") && kit.lightMat) mats[mi] = kit.lightMat;
                    r.sharedMaterials = mats;
                }
                var lg = new GameObject("Light"); lg.transform.SetParent(tr.transform);
                lg.transform.SetPositionAndRotation(pos + Vector3.down * 0.02f, Quaternion.Euler(90, 0, 0));
                var l = lg.AddComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = 170f; l.innerSpotAngle = 100f; l.range = 7.5f;
                bool tiles = wallM != kit.wallMat;   // glazed tiles / white plaster bounce far more light than wallpaper
                l.intensity = tiles ? (glassWalls ? 1.7f : 1.5f) : 3.0f;
                l.color = tiles ? new Color(0.95f, 0.98f, 1f) : new Color(1f, 0.95f, 0.82f);
                if (world == "PG") { l.intensity = 2.4f; l.color = new Color(0.86f, 0.95f, 1f); }
                if (world == "HT") { l.intensity = 1.7f; l.color = new Color(1f, 0.78f, 0.5f); l.range = 5.5f; }
                if (world == "OF") { l.intensity = 1.6f; l.color = new Color(0.88f, 0.96f, 1f); }
                if (world == "PD") { l.intensity = 2.2f; l.color = new Color(1f, 0.62f, 0.32f); l.range = 5.5f; }
                if (world == "WH") { l.intensity = 2.6f; l.color = new Color(0.9f, 0.95f, 1f); l.range = 9f; }
                if (world == "LO") { l.intensity = 1.4f; }
                if (world == "LF") { l.intensity = 2.6f; l.color = new Color(1f, 0.8f, 0.66f); }
                if (world == "EL") { l.intensity = 2.0f; l.color = new Color(1f, 0.28f, 0.16f); l.range = 6f; }
                if (world == "RN") { l.intensity = 2.8f; l.color = new Color(1f, 0.1f, 0.08f); }
                if (world == "FL") { l.intensity = 2.4f; l.color = new Color(0.85f, 1f, 0.92f); }
                if (world == "LB") { l.intensity = 1.7f; l.color = new Color(1f, 0.78f, 0.52f); l.range = 6f; }
                if (world == "LA") { l.intensity = 1.9f; l.color = new Color(0.93f, 0.97f, 1f); }
                // shadows are expensive: only every 4th fixture casts them (enough for grounding furniture)
                l.shadows = (k++ % 4 == 0) ? LightShadows.Soft : LightShadows.None; l.shadowStrength = 0.7f;
                // lit tubes next to a dark zone reach less far: light must not leak through the walls into the dark
                float dd = DistToDark(pos);
                if (mode == 0 && dd < 6f) l.range = Mathf.Clamp(dd + 2.6f, 3.4f, 7.5f);
                if (mode != 0) l.range = 4.2f;
                if (mode == 3) { l.intensity *= 0.35f; l.color = new Color(1f, 0.86f, 0.62f); }
                if (mode == 2) { l.intensity *= 0.7f; lg.AddComponent<Flicker>().seed = pos.x * 3.1f + pos.z * 7.7f; }
            }
        }

        GameObject Slab(Transform parent, string name, Vector3 center, Vector3 size, Material mat, float uvScale)
        {
            var go = new GameObject(name) { layer = ArchLayer, isStatic = true };
            go.transform.SetParent(parent);
            go.transform.position = center;
            go.AddComponent<MeshFilter>().sharedMesh = TestLevelBuilder.BoxMesh(size, uvScale);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }
    }
}
