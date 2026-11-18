using System.Diagnostics;
using System.IO;
using Address.Core;
using Address.Game;
using Address.World;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace Address.EditorTools
{
    /// <summary>Menu screenshot + GPU frame-time probe per quality tier (render 1080p frames, sync with a readback).</summary>
    public static class VisualCheck
    {
        public static void Run()
        {
            Directory.CreateDirectory("../Docs/reports/visual");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string aest = "BR"; var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-aesthetic") aest = args[i + 1];
            var level = new LevelBuilder(LevelFarm.Kit()).Build(new LevelAddress(aest, Hashing.Combine(Hashing.Fnv64("menu"), 322UL)));
            TestFarm.Lighting(new Address.TestLevel.TestLevelBuilder.Result { root = level.root });
            LevelBuilder.ApplyAtmosphere(level.aesthetic); if (level.probe) level.probe.RenderProbe();
            var go = new GameObject("cam"); var cam = go.AddComponent<Camera>(); cam.fieldOfView = 72; cam.nearClipPlane = 0.03f; cam.farClipPlane = 120;
            var data = go.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            go.transform.SetPositionAndRotation(level.start + Vector3.up * 1.62f, Quaternion.Euler(3, level.startYaw + 30, 0));
            var rt = new RenderTexture(1920, 1080, 24); cam.targetTexture = rt;
            var opt = level.root.AddComponent<Address.Rendering.LevelOptimizer>(); opt.Init(level.root, go.transform);

            // menu over the live room
            var menu = new MenuUI(); menu.Show(true, false);
            var canvas = menu.root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 0.5f;
            Canvas.ForceUpdateCanvases();
            Thumbnails.Save(cam, rt, "../Docs/reports/visual/main_menu.png");
            Object.DestroyImmediate(menu.root);

            // frame-time probe: 90 frames rotating the view, per tier
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var report = "";
            for (int q = 0; q < QualitySettings.names.Length; q++)
            {
                QualitySettings.SetQualityLevel(q, true);
                opt.Tick(true);
                for (int i = 0; i < 5; i++) Render(cam, rt, tex);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 90; i++) { go.transform.rotation = Quaternion.Euler(3, level.startYaw + i * 4, 0); opt.Tick(false); Render(cam, rt, tex); }
                double ms = sw.Elapsed.TotalMilliseconds / 90;
                report += $"{QualitySettings.names[q]}: {ms:F1} ms/frame (~{1000 / ms:F0} fps) at 1920x1080\n";
                go.transform.rotation = Quaternion.Euler(3, level.startYaw + 30, 0);
                Thumbnails.Save(cam, rt, $"../Docs/reports/visual/{aest}_tier_{QualitySettings.names[q]}.png");
            }
            // High-tier cost breakdown: toggle one feature at a time
            QualitySettings.SetQualityLevel(2, true); opt.Tick(true);
            var urp = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
            var rd = UnityEditor.AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/ADDRESS/Settings/Rendering/ADDRESS_Renderer.asset");
            double Measure()
            {
                for (int i = 0; i < 5; i++) Render(cam, rt, tex);
                var s2 = Stopwatch.StartNew();
                for (int i = 0; i < 90; i++) { go.transform.rotation = Quaternion.Euler(3, level.startYaw + i * 4, 0); opt.Tick(false); Render(cam, rt, tex); }
                return s2.Elapsed.TotalMilliseconds / 90;
            }
            string NL = System.Environment.NewLine;
            report += $"[High base] {Measure():F1} ms" + NL;
            int msaa = urp.msaaSampleCount; urp.msaaSampleCount = 1; report += $"[High msaa1] {Measure():F1} ms" + NL; urp.msaaSampleCount = msaa;
            foreach (var f in rd.rendererFeatures) f.SetActive(false); report += $"[High noSSAO] {Measure():F1} ms" + NL; foreach (var f in rd.rendererFeatures) f.SetActive(true);
            var ls = Object.FindObjectsByType<Light>(FindObjectsSortMode.None); var sh = new LightShadows[ls.Length];
            for (int i = 0; i < ls.Length; i++) { sh[i] = ls[i].shadows; ls[i].shadows = LightShadows.None; }
            report += $"[High noShadows] {Measure2():F1} ms" + NL;
            for (int i = 0; i < ls.Length; i++) ls[i].shadows = sh[i];
            float rs = urp.renderScale; urp.renderScale = 0.8f; report += $"[High scale0.8] {Measure():F1} ms" + NL; urp.renderScale = rs;
            bool op = urp.supportsCameraOpaqueTexture; urp.supportsCameraOpaqueTexture = false; report += $"[High noOpaqueTex] {Measure():F1} ms" + NL; urp.supportsCameraOpaqueTexture = op;
            data.renderPostProcessing = false; report += $"[High noPost] {Measure():F1} ms" + NL; data.renderPostProcessing = true;
            double Measure2()
            {
                for (int i = 0; i < 5; i++) Render(cam, rt, tex);
                var s2 = Stopwatch.StartNew();
                for (int i = 0; i < 90; i++) { go.transform.rotation = Quaternion.Euler(3, level.startYaw + i * 4, 0); Render(cam, rt, tex); }
                return s2.Elapsed.TotalMilliseconds / 90;
            }
            File.WriteAllText($"../Docs/reports/visual/perf_{aest}.txt", report + $"GPU: {SystemInfo.graphicsDeviceName}, objects {level.engine.placed.Count}, lights {Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length} (enabled {System.Linq.Enumerable.Count(Object.FindObjectsByType<Light>(FindObjectsSortMode.None), l => l.enabled)})\n");
            Debug.Log("[ADDRESS] visual check\n" + report);
        }

        static void Render(Camera cam, RenderTexture rt, Texture2D tex)
        {
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); tex.Apply(); // forces the GPU to finish the frame
            RenderTexture.active = prev;
        }
    }
}
