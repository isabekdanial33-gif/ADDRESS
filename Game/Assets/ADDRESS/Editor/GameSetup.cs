using System.IO;
using System.Linq;
using Address.Game;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Address.EditorTools
{
    /// <summary>Creates the GameConfig asset and the Game scene, registers it in Build Settings, builds the player.</summary>
    public static class GameSetup
    {
        const string ConfigPath = "Assets/ADDRESS/Generated/GameConfig.asset";
        const string PostPath = "Assets/ADDRESS/Generated/PostFX.asset";
        const string ScenePath = "Assets/ADDRESS/Scenes/Game.unity";

        [MenuItem("ADDRESS/Setup Game Scene")]
        public static void Setup()
        {
            // runtime-loaded materials whose shaders must ship with the build
            System.IO.Directory.CreateDirectory("Assets/ADDRESS/Resources");
            foreach (var (n, tint, ground, exp, thick) in new[] { ("SkyAero", new Color(0.42f, 0.66f, 1f), new Color(0.85f, 0.9f, 0.95f), 1.35f, 0.75f), ("SkyDim", new Color(0.5f, 0.5f, 0.45f), new Color(0.3f, 0.28f, 0.22f), 0.6f, 1.0f) })
            {
                var path = $"Assets/ADDRESS/Resources/{n}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { var sh = Shader.Find("Skybox/Procedural"); if (!sh) continue; m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
                m.SetColor("_SkyTint", tint); m.SetColor("_GroundColor", ground); m.SetFloat("_Exposure", exp); m.SetFloat("_AtmosphereThickness", thick); m.SetFloat("_SunSize", 0.035f);
                EditorUtility.SetDirty(m);
            }
            // transparent materials used at runtime (clouds, bubbles): created here so their shader variants ship
            void Transparent(string n, string shader, Color c)
            {
                var path = $"Assets/ADDRESS/Resources/{n}.mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                var sh = Shader.Find(shader); if (!sh) return;
                if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
                m.shader = sh; m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetColor("_BaseColor", c);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000;
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
                EditorUtility.SetDirty(m);
            }
            Transparent("CloudMat", "Universal Render Pipeline/Unlit", Color.white);
            Transparent("BubbleMat", "Universal Render Pipeline/Particles/Unlit", Color.white);
            if (!AssetDatabase.LoadAssetAtPath<Material>("Assets/ADDRESS/Resources/CRT.mat"))
            {
                var sh = Shader.Find("ADDRESS/CRT");
                if (sh) AssetDatabase.CreateAsset(new Material(sh), "Assets/ADDRESS/Resources/CRT.mat");
            }

            TestFarm.RefreshDatabase();
            var cfg = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (!cfg) { cfg = ScriptableObject.CreateInstance<GameConfig>(); AssetDatabase.CreateAsset(cfg, ConfigPath); }
            cfg.kit = LevelFarm.Kit();
            PlayerSettings.gpuSkinning = true;
            cfg.ghostOk = Ghost("GhostOk", new Color(0.3f, 1f, 0.5f, 0.32f));
            cfg.ghostBad = Ghost("GhostBad", new Color(1f, 0.25f, 0.2f, 0.32f));
            cfg.post = Post();
            // the downloaded character (if imported) replaces my own model
            cfg.bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ADDRESS/Generated/Prefabs/HazmatBody2.prefab") ?? AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.PrefabPath);
            cfg.suitYellow = AssetDatabase.LoadAssetAtPath<Material>("Assets/ThirdParty/HazmatSuit/M_Hazmat_URP.mat");
            cfg.suitBlue = AssetDatabase.LoadAssetAtPath<Material>("Assets/ThirdParty/HazmatSuit/M_Hazmat_Blue.mat");
            cfg.fpArmLeft = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ADDRESS/Art/Character/FP/fp_arm_Left.glb");
            cfg.fpArmRight = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ADDRESS/Art/Character/FP/fp_arm_Right.glb");
            var dp = "Assets/ADDRESS/Generated/Materials/Dust.mat"; var dm = AssetDatabase.LoadAssetAtPath<Material>(dp);
            if (!dm) { dm = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(dm, dp); }
            dm.SetColor("_BaseColor", new Color(1, 0.97f, 0.9f, 0.55f)); dm.SetFloat("_Surface", 1); dm.renderQueue = 3000; EditorUtility.SetDirty(dm);
            cfg.dustMat = dm;
            cfg.netSessionPrefab = NetPrefab("NetSession", go => go.AddComponent<Address.Net.NetSession>());
            cfg.netAvatarPrefab = NetPrefab("NetAvatar", go =>
            {
                var av = go.AddComponent<Address.Net.NetAvatar>();
                var bodyPrefab = cfg.bodyPrefab;
                if (bodyPrefab)
                {
                    var b = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab); b.transform.SetParent(go.transform, false);
                    b.GetComponent<Address.Player.ProceduralBody>().firstPerson = false; av.visual = b;
                }
            });
            AudioClip A(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/ADDRESS/Audio/{n}.wav");
            cfg.audio = new AudioKit
            {
                hum = A("hum_fluorescent"), roomTone = A("room_tone"), breath = A("breath_mask"), door = A("door_open"), elevator = A("elevator_open"),
                vent = A("vent_open"), shutter = A("camera_shutter"), travel = A("travel"),
                steps = Enumerable.Range(0, 6).Select(i => A($"step_carpet_{i}")).Where(c => c).ToArray(),
                // real recordings (Wikimedia Commons, public domain) - Audio/Real/SOURCES.md
                doorReal = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ADDRESS/Audio/Real/Door_handle_creaking.wav"),
                shutterReal = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ADDRESS/Audio/Real/Holga_shuttersound.wav"),
                poolHall = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ADDRESS/Audio/Real/Indoor_swimming_pool_hall.wav"),
            };
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("GameRoot").AddComponent<GameRoot>();
            root.config = cfg;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "ADDRESS";
            PlayerSettings.companyName = "ADDRESS";
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            // the Input System drives the player; legacy input stays enabled for menu fallbacks
            var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var aih = ps.FindProperty("activeInputHandler"); if (aih != null) { aih.intValue = 2; ps.ApplyModifiedPropertiesWithoutUndo(); }
            AssetDatabase.SaveAssets();
            Debug.Log("[ADDRESS] game scene ready");
        }

        static GameObject NetPrefab(string name, System.Action<GameObject> build)
        {
            var path = $"Assets/ADDRESS/Generated/Prefabs/{name}.prefab";
            var go = new GameObject(name); go.AddComponent<Unity.Netcode.NetworkObject>(); build(go);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path); Object.DestroyImmediate(go);
            return prefab;
        }

        static Material Ghost(string name, Color c)
        {
            var path = $"Assets/ADDRESS/Generated/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetFloat("_Surface", 1); m.SetColor("_BaseColor", c); m.renderQueue = 3000;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
            EditorUtility.SetDirty(m);
            return m;
        }

        static VolumeProfile Post()
        {
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
            if (p) return p;
            p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, PostPath);
            void Add<T>(System.Action<T> set) where T : VolumeComponent { var c = p.Add<T>(true); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, p); set(c); }
            Add<Tonemapping>(c => c.mode.Override(TonemappingMode.ACES));
            Add<Bloom>(c => { c.intensity.Override(0.5f); c.threshold.Override(1.0f); c.scatter.Override(0.7f); });
            Add<ColorAdjustments>(c => { c.postExposure.Override(-0.1f); c.saturation.Override(-4f); c.contrast.Override(10f); });
            Add<Vignette>(c => c.intensity.Override(0.22f));
            Add<WhiteBalance>(c => c.temperature.Override(8f));
            Add<FilmGrain>(c => { c.type.Override(FilmGrainLookup.Thin1); c.intensity.Override(0.12f); });
            EditorUtility.SetDirty(p);
            return p;
        }

        /// <summary>-executeMethod Address.EditorTools.GameSetup.BuildAndroid (run Unity with -buildTarget Android).</summary>
        public static void BuildAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            RenderingSetup.Run();
            Setup();
            PlayerSettings.companyName = "ADDRESS"; PlayerSettings.productName = "ADDRESS";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.address.game");
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan, UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            EditorUserBuildSettings.buildAppBundle = false;
            System.IO.Directory.CreateDirectory("../Build/Android");
            var opts = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = "../Build/Android/ADDRESS.apk", target = BuildTarget.Android, options = BuildOptions.None };
            var r = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[ADDRESS] android build {r.summary.result}: {r.summary.totalSize / (1024 * 1024)} MB, {r.summary.totalErrors} errors, {r.summary.totalTime}");
        }

        public static void BuildWindows()
        {
            Setup();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutPath(),   // -out <dir>: silent test builds go elsewhere so the player never starts a muted copy
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var rep = BuildPipeline.BuildPlayer(opts);
            // ship the example mods and shader packs next to the exe (players add their own there)
            try
            {
                var outDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(opts.locationPathName));
                foreach (var d in new[] { "Mods", "Shaders" })
                {
                    var src = System.IO.Path.GetFullPath(System.IO.Path.Combine("..", d)); if (!System.IO.Directory.Exists(src)) continue;
                    foreach (var f in System.IO.Directory.GetFiles(src, "*", System.IO.SearchOption.AllDirectories))
                    {
                        var to = System.IO.Path.Combine(outDir, d, f.Substring(src.Length + 1));
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)); System.IO.File.Copy(f, to, true);
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[ADDRESS] mods copy: " + e.Message); }
            // Steam reads the app id from this file when the game is not started by the Steam client (480 = test app)
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(opts.locationPathName)), "steam_appid.txt"), Address.Net.SteamService.AppId.ToString()); } catch (System.Exception e) { Debug.LogWarning(e.Message); }
            Debug.Log($"[ADDRESS] build {rep.summary.result}: {rep.summary.totalSize / (1024 * 1024)} MB, {rep.summary.totalErrors} errors, {rep.summary.totalTime}");
        }
    
        static string OutPath()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-out") return System.IO.Path.Combine(a[i + 1], "ADDRESS.exe");
            return "../Build/ADDRESS.exe";
        }
}
}
