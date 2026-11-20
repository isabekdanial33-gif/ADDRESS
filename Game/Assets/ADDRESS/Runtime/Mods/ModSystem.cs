using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Address.Mods
{
    // ------------------------------------------------------------------ data (JsonUtility: public fields, plain types)

    [Serializable] public class ModManifest { public string name, author, version, description; }

    /// <summary>A world (level) described as data: layout preset, surfaces, light, air, colour grade, who lives there.</summary>
    [Serializable]
    public class WorldDef
    {
        public string id, title, level, about;          // id: 2-3 capital letters, used in addresses (MN-xxxx-...)
        public string layout = "rooms";                  // rooms | corridor | hotel | halls | vast
        public float roomMin = 0, roomMax = 0, doorShare = -1; public int size = 0; public float height = 0;
        public string[] kinds;                           // room furnishing mix: office, storage, lounge, breakroom, meeting, server, dump, pillars, wrong, showroom, library, empty, corridor
        public string wall, floor, ceiling;              // a built-in surface name (Wallpaper001A, Carpet006...) or "file:textures/x.png" inside a mod
        public float[] wallTint, floorTint, ceilTint;
        public float[] lightColor; public float lightIntensity = -1, lightRange = -1, deadShare = -1, flickerShare = -1;
        public bool noLamps, pipes, water, lockers, doors;
        public float[] ambient, fogColor; public float fogDensity = -1;
        public float[] gradeFilter; public float gradeSaturation, gradeContrast, gradeTemperature, gradeExposure;
        public string[] entities; public float entityChance = -1;
        public string music = "calm";                    // calm | dark | none
        public float weight = 0.02f;                     // chance per exit of leading here
        [NonSerialized] public string modDir;            // set for mod worlds
    }

    /// <summary>A creature from a mod: a glTF/GLB model with its own animations, and how it behaves.</summary>
    [Serializable]
    public class EntityDef
    {
        public string id, name, about, model;            // model: path inside the mod (models/x.glb)
        public float height = 2f, yaw = 0f, walkSpeed = 0.7f, chaseSpeed = 2.5f, noticeRange = 15f, catchRadius = 0.9f;
        public bool freezeWhenSeen;
        public string idle, walk, run;                   // animation names inside the model (substring match)
        public string[] sounds;                          // wav/ogg inside the mod
        public string[] worlds;                          // world ids where it lives (empty = everywhere)
        public int weight = 2;
        [NonSerialized] public string modDir;
        [NonSerialized] public GameObject template;      // loaded model, hidden
        [NonSerialized] public AudioClip[] clips;
    }

    /// <summary>A "shader pack" (like Minecraft's): a post-processing look plus a few lighting knobs.</summary>
    [Serializable]
    public class ShaderPack
    {
        public string name, author, description;
        public float bloom = -1, bloomThreshold = -1, vignette = -1, grain = -1, chromatic = -1, motionBlur = -1;
        public float saturation = -999, contrast = -999, exposure = -999, temperature = -999, lensDistortion = -999;
        public float dofFocus = -1, dofAperture = -1;
        public float[] colorFilter;
        public float lightMul = -1, fogMul = -1, shadowDistance = -1, ssao = -1;
        [NonSerialized] public string dir;
    }

    public class ModInfo
    {
        public ModManifest manifest; public string dir, error;
        public readonly List<WorldDef> worlds = new(); public readonly List<EntityDef> entities = new();
        public bool Valid => error == null && (worlds.Count > 0 || entities.Count > 0);
        public bool Enabled { get => PlayerPrefs.GetInt("mod_off_" + manifest?.name, 0) == 0; set => PlayerPrefs.SetInt("mod_off_" + manifest?.name, value ? 0 : 1); }
    }

    /// <summary>
    /// Mods and shader packs, next to the game: Mods/&lt;mod&gt;/mod.json (+ worlds/*.json, entities/*.json, models, textures,
    /// sounds) and Shaders/&lt;pack&gt;/pack.json. Built-in worlds use the same format (Resources/Worlds/*.json).
    /// A folder counts only if it parses and has real content; broken ones are listed with the reason.
    /// </summary>
    public static class ModSystem
    {
        public static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string ModsDir => Path.Combine(Root, "Mods");
        public static string ShadersDir => Path.Combine(Root, "Shaders");

        public static readonly List<ModInfo> Mods = new();
        public static readonly List<ShaderPack> Packs = new();
        public static readonly List<string> Broken = new();
        static readonly Dictionary<string, WorldDef> worlds = new();
        public static IEnumerable<ModInfo> ValidMods => Mods.Where(m => m.Valid);
        public static IEnumerable<WorldDef> Worlds => worlds.Values;
        public static IEnumerable<EntityDef> Entities => Mods.Where(m => m.Valid && m.Enabled).SelectMany(m => m.entities).Where(e => e.template);
        public static WorldDef World(string id) => id != null && worlds.TryGetValue(id, out var w) && (w.modDir == null || Mods.Any(m => m.dir == w.modDir && m.Enabled)) ? w : null;
        public static EntityDef Entity(string id) => Entities.FirstOrDefault(e => e.id == id);
        static bool loaded;

        public static void Load()
        {
            if (loaded) return; loaded = true;
            // built-in data worlds
            foreach (var ta in Resources.LoadAll<TextAsset>("Worlds"))
                try { var w = JsonUtility.FromJson<WorldDef>(ta.text); if (w != null && !string.IsNullOrEmpty(w.id)) worlds[w.id] = w; }
                catch (Exception e) { Debug.LogWarning($"[ADDRESS] world {ta.name}: {e.Message}"); }
            // mods
            try
            {
                if (Directory.Exists(ModsDir))
                    foreach (var dir in Directory.GetDirectories(ModsDir)) LoadMod(dir);
            }
            catch (Exception e) { Debug.LogWarning("[ADDRESS] mods: " + e.Message); }
            // shader packs
            try
            {
                if (Directory.Exists(ShadersDir))
                    foreach (var dir in Directory.GetDirectories(ShadersDir))
                    {
                        var f = Path.Combine(dir, "pack.json");
                        try
                        {
                            if (!File.Exists(f)) { Broken.Add($"{Path.GetFileName(dir)}: нет pack.json"); continue; }
                            var p = JsonUtility.FromJson<ShaderPack>(File.ReadAllText(f));
                            if (p == null || string.IsNullOrEmpty(p.name)) { Broken.Add($"{Path.GetFileName(dir)}: нет имени"); continue; }
                            p.dir = dir; Packs.Add(p);
                        }
                        catch (Exception e) { Broken.Add($"{Path.GetFileName(dir)}: {e.Message}"); }
                    }
            }
            catch (Exception e) { Debug.LogWarning("[ADDRESS] shaders: " + e.Message); }
            Debug.Log($"[ADDRESS] mods: {Mods.Count(m => m.Valid)} ok ({string.Join(", ", Mods.Select(m => (m.manifest?.name ?? Path.GetFileName(m.dir)) + (m.Valid ? "" : " BROKEN: " + m.error)))}), worlds {worlds.Count}, shader packs {Packs.Count}");
        }

        static void LoadMod(string dir)
        {
            var info = new ModInfo { dir = dir };
            Mods.Add(info);
            try
            {
                var mf = Path.Combine(dir, "mod.json");
                if (!File.Exists(mf)) { info.error = "нет mod.json"; return; }
                info.manifest = JsonUtility.FromJson<ModManifest>(File.ReadAllText(mf));
                if (info.manifest == null || string.IsNullOrEmpty(info.manifest.name)) { info.error = "в mod.json нет name"; return; }
                var wd = Path.Combine(dir, "worlds");
                if (Directory.Exists(wd))
                    foreach (var f in Directory.GetFiles(wd, "*.json"))
                    {
                        var w = JsonUtility.FromJson<WorldDef>(File.ReadAllText(f));
                        if (w == null || string.IsNullOrEmpty(w.id) || w.id.Length < 2 || w.id.Length > 3 || w.id.Any(ch => ch < 'A' || ch > 'Z')) { info.error = $"{Path.GetFileName(f)}: id должен быть 2-3 заглавные латинские буквы"; return; }
                        w.modDir = dir; info.worlds.Add(w); worlds[w.id] = w;
                    }
                var ed = Path.Combine(dir, "entities");
                if (Directory.Exists(ed))
                    foreach (var f in Directory.GetFiles(ed, "*.json"))
                    {
                        var e = JsonUtility.FromJson<EntityDef>(File.ReadAllText(f));
                        if (e == null || string.IsNullOrEmpty(e.id) || string.IsNullOrEmpty(e.model)) { info.error = $"{Path.GetFileName(f)}: нужны id и model"; return; }
                        if (!File.Exists(Path.Combine(dir, e.model))) { info.error = $"{Path.GetFileName(f)}: нет файла {e.model}"; return; }
                        e.modDir = dir; info.entities.Add(e);
                    }
                if (!info.Valid && info.error == null) info.error = "пустой мод (нет worlds/ и entities/)";
            }
            catch (Exception e) { info.error = e.Message; }
        }

        // ------------------------------------------------------------------ mod creature models (loaded once, async, at start-up)

        public static async void LoadModels()
        {
            foreach (var m in Mods.Where(m => m.Valid))
                foreach (var e in m.entities)
                {
                    try
                    {
                        var gltf = new GLTFast.GltfImport();
                        var settings = new GLTFast.ImportSettings { AnimationMethod = GLTFast.AnimationMethod.Legacy };
                        if (!await gltf.Load(Path.Combine(e.modDir, e.model), settings)) { Debug.LogWarning($"[ADDRESS] mod model {e.model}: load failed"); continue; }
                        var root = new GameObject("ModModel_" + e.id); root.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(root);
                        if (!await gltf.InstantiateMainSceneAsync(root.transform)) { Debug.LogWarning($"[ADDRESS] mod model {e.model}: instantiate failed"); continue; }
                        // feet on the floor, scaled to its height, facing +Z
                        root.SetActive(true);
                        var rs = root.GetComponentsInChildren<Renderer>(true);
                        if (rs.Length > 0)
                        {
                            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                            float s = e.height / Mathf.Max(0.01f, b.size.y);
                            foreach (Transform c in root.transform) { c.localScale *= s; c.localPosition = c.localPosition * s; }
                            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                            foreach (Transform c in root.transform) c.position -= new Vector3(b.center.x, b.min.y, b.center.z);
                            foreach (Transform c in root.transform) c.RotateAround(Vector3.zero, Vector3.up, e.yaw);
                        }
                        foreach (var r in rs) if (r is SkinnedMeshRenderer sk) sk.updateWhenOffscreen = true;
                        root.SetActive(false);
                        e.template = root;
                        var clips = new List<AudioClip>();
                        foreach (var sp in e.sounds ?? new string[0]) { var c = await LoadClip(Path.Combine(e.modDir, sp)); if (c) clips.Add(c); }
                        e.clips = clips.ToArray();
                        Debug.Log($"[ADDRESS] mod creature {e.id} ready ({rs.Length} renderers, {root.GetComponentInChildren<Animation>()?.GetClipCount() ?? 0} clips, {clips.Count} sounds)");
                    }
                    catch (Exception ex) { Debug.LogWarning($"[ADDRESS] mod creature {e.id}: {ex.Message}"); }
                }
        }

        static async System.Threading.Tasks.Task<AudioClip> LoadClip(string path)
        {
            if (!File.Exists(path)) return null;
            var type = path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? AudioType.MPEG : AudioType.WAV;
            using var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), type);
            var op = req.SendWebRequest();
            while (!op.isDone) await System.Threading.Tasks.Task.Yield();
            return req.result == UnityEngine.Networking.UnityWebRequest.Result.Success ? UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req) : null;
        }

        // ------------------------------------------------------------------ surfaces

        static readonly Dictionary<string, Material> matCache = new();
        /// <summary>A built-in surface by name from the kit's library, or a texture from the mod folder ("file:..."), tinted.</summary>
        public static Material Surface(string spec, float[] tint, Material[] library, Material fallback, string modDir)
        {
            if (string.IsNullOrEmpty(spec)) return Tint(fallback, tint, "fallback");
            string key = spec + "|" + (tint == null ? "" : string.Join(",", tint)) + "|" + modDir;
            if (matCache.TryGetValue(key, out var m) && m) return m;
            Material src = null;
            if (spec.StartsWith("file:") && modDir != null)
            {
                var p = Path.Combine(modDir, spec.Substring(5));
                if (File.Exists(p))
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true); tex.LoadImage(File.ReadAllBytes(p)); tex.wrapMode = TextureWrapMode.Repeat; tex.anisoLevel = 4;
                    src = new Material(fallback) { name = "mod_" + Path.GetFileNameWithoutExtension(p) };
                    src.SetTexture("_BaseMap", tex);
                    foreach (var k in new[] { "_BumpMap", "_ParallaxMap", "_OcclusionMap", "_MetallicGlossMap" }) if (src.HasProperty(k)) src.SetTexture(k, null);
                    src.DisableKeyword("_NORMALMAP"); src.DisableKeyword("_PARALLAXMAP"); src.DisableKeyword("_OCCLUSIONMAP"); src.DisableKeyword("_METALLICSPECGLOSSMAP");
                    if (src.HasProperty("_Smoothness")) src.SetFloat("_Smoothness", 0.2f);
                }
            }
            else src = library?.FirstOrDefault(x => x && x.name == spec);
            m = Tint(src ? src : fallback, tint, key);
            matCache[key] = m; return m;
        }

        static Material Tint(Material src, float[] t, string key)
        {
            if (!src || t == null || t.Length < 3) return src;
            var m = new Material(src) { name = src.name + "_t" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(t[0], t[1], t[2], 1));
            return m;
        }

        public static Color C(float[] a, Color def) => a != null && a.Length >= 3 ? new Color(a[0], a[1], a[2]) : def;
    }
}
