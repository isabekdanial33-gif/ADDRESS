using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Address.EditorTools
{
    /// <summary>
    /// Creates URP assets for the four quality profiles (Low/Medium/High/Ultra) and wires them into Quality/Graphics settings.
    /// Safe to run repeatedly: existing assets are updated in place.
    /// </summary>
    public static class RenderingSetup
    {
        const string Dir = "Assets/ADDRESS/Settings/Rendering";

        struct Tier
        {
            public string name; public float renderScale; public int msaa; public float shadowDist; public int cascades;
            public int shadowRes; public int addLights; public bool softShadows; public int addShadowRes; public bool hdr;
        }

        static readonly Tier[] Tiers =
        {
            new Tier{ name="Low",    renderScale=0.67f, msaa=1, shadowDist=18, cascades=1, shadowRes=1024, addLights=4,  softShadows=false, addShadowRes=512,  hdr=true },
            new Tier{ name="Medium", renderScale=0.85f, msaa=1, shadowDist=30, cascades=2, shadowRes=2048, addLights=8,  softShadows=true,  addShadowRes=1024, hdr=true },
            new Tier{ name="High",   renderScale=1.0f,  msaa=2, shadowDist=45, cascades=3, shadowRes=2048, addLights=16, softShadows=true,  addShadowRes=2048, hdr=true },
            new Tier{ name="Mobile", renderScale=0.7f, msaa=1, shadowDist=22, cascades=1, shadowRes=512,  addLights=4,  softShadows=false, addShadowRes=256,  hdr=true },
            new Tier{ name="Ultra",  renderScale=1.0f,  msaa=2, shadowDist=60, cascades=3, shadowRes=2048, addLights=16, softShadows=true,  addShadowRes=2048, hdr=true },
        };

        [MenuItem("ADDRESS/Setup Rendering (URP tiers)")]
        public static void Run()
        {
            Directory.CreateDirectory(Dir);
            var rendererPath = $"{Dir}/ADDRESS_Renderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            // Forward+ : many small lights (fluorescent panels) without per-object light limits
            renderer.renderingMode = RenderingMode.ForwardPlus;
            renderer.depthPrimingMode = DepthPrimingMode.Auto;
            EnsureSsao(renderer);
            EditorUtility.SetDirty(renderer);

            var assets = new UniversalRenderPipelineAsset[Tiers.Length];
            for (int i = 0; i < Tiers.Length; i++)
            {
                var t = Tiers[i];
                var path = $"{Dir}/URP_{t.name}.asset";
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (a == null)
                {
                    a = UniversalRenderPipelineAsset.Create(renderer);
                    AssetDatabase.CreateAsset(a, path);
                }
                a.renderScale = t.renderScale;
                a.msaaSampleCount = t.msaa;
                a.shadowDistance = t.shadowDist;
                a.shadowCascadeCount = t.cascades;
                a.mainLightShadowmapResolution = t.shadowRes;
                a.maxAdditionalLightsCount = Mathf.Min(t.addLights, 8);
                a.supportsHDR = t.hdr;
                a.additionalLightsShadowmapResolution = t.addShadowRes;
                a.supportsCameraDepthTexture = true;    // water: depth absorption + soft edges
                a.supportsCameraOpaqueTexture = true;   // water: refraction
                var so = new SerializedObject(a);
                SetBool(so, "m_SoftShadowsSupported", t.softShadows);
                SetBool(so, "m_AdditionalLightShadowsSupported", true);
                SetBool(so, "m_RequireDepthTexture", true);
                SetBool(so, "m_RequireOpaqueTexture", true); SetInt(so, "m_OpaqueDownsampling", 0);
                SetInt(so, "m_AdditionalLightsRenderingMode", 1); // per pixel
                SetBool(so, "m_UseSRPBatcher", true);
                SetInt(so, "m_GPUResidentDrawerMode", 0);        // instanced drawing (Unity 6)
                SetBool(so, "m_GPUResidentDrawerEnableOcclusionCullingInCameras", false);
                SetInt(so, "m_LightProbeSystem", 1);              // Adaptive Probe Volumes
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(a);
                assets[i] = a;
            }

            // the phone tier gets its own renderer: no SSAO, no depth priming, cheap opaque copy
            var mobPath = $"{Dir}/ADDRESS_Renderer_Mobile.asset";
            var mob = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(mobPath);
            if (mob == null) { mob = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(mob, mobPath); }
            mob.renderingMode = RenderingMode.ForwardPlus; mob.depthPrimingMode = DepthPrimingMode.Disabled; EditorUtility.SetDirty(mob);
            int mi = System.Array.FindIndex(Tiers, t => t.name == "Mobile");
            {
                var so = new SerializedObject(assets[mi]);
                var list = so.FindProperty("m_RendererDataList"); list.arraySize = 1; list.GetArrayElementAtIndex(0).objectReferenceValue = mob;
                SetBool(so, "m_AdditionalLightShadowsSupported", false); SetInt(so, "m_OpaqueDownsampling", 2);
                SetInt(so, "m_GPUResidentDrawerMode", 0); SetBool(so, "m_SupportsLightLayers", false);
                SetInt(so, "m_ColorGradingMode", 0); SetInt(so, "m_ColorGradingLutSize", 16);
                SetBool(so, "m_UseFastSRGBLinearConversion", true);
                SetInt(so, "m_HDRColorBufferPrecision", 0);   // 32-bit R11G11B10: HDR without the bandwidth of 64-bit
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            GraphicsSettings.defaultRenderPipeline = assets[2];
            // Quality levels: rebuild names + per-level pipeline asset
            var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = qs.FindProperty("m_QualitySettings");
            while (levels.arraySize < Tiers.Length) levels.InsertArrayElementAtIndex(levels.arraySize);
            while (levels.arraySize > Tiers.Length) levels.DeleteArrayElementAtIndex(levels.arraySize - 1);
            for (int i = 0; i < Tiers.Length; i++)
            {
                var l = levels.GetArrayElementAtIndex(i);
                l.FindPropertyRelative("name").stringValue = Tiers[i].name;
                l.FindPropertyRelative("customRenderPipeline").objectReferenceValue = assets[i];
                l.FindPropertyRelative("vSyncCount").intValue = 0;
                bool mobile = Tiers[i].name == "Mobile";
                l.FindPropertyRelative("anisotropicTextures").intValue = mobile ? 0 : i >= 1 ? 2 : 1;
                l.FindPropertyRelative("globalTextureMipmapLimit").intValue = i == 0 || mobile ? 1 : 0;   // half-res textures on phones
                l.FindPropertyRelative("lodBias").floatValue = new[] { 0.6f, 1f, 1.5f, 0.5f, 2f }[i];
                l.FindPropertyRelative("skinWeights").intValue = mobile ? 2 : 4;
                l.FindPropertyRelative("realtimeReflectionProbes").boolValue = !mobile;
                l.FindPropertyRelative("softParticles").boolValue = !mobile;
                l.FindPropertyRelative("streamingMipmapsActive").boolValue = true;
                l.FindPropertyRelative("streamingMipmapsMemoryBudget").floatValue = new[] { 512f, 1024f, 2048f, 256f, 4096f }[i];
            }
            qs.FindProperty("m_CurrentQuality").intValue = 2;
            // default tier per platform: phones start on "Mobile"
            var per = qs.FindProperty("m_PerPlatformDefaultQuality");
            if (per != null) for (int k = 0; k < per.arraySize; k++) { var e = per.GetArrayElementAtIndex(k); var key = e.FindPropertyRelative("first"); var val = e.FindPropertyRelative("second");
                if (key != null && val != null) val.intValue = key.stringValue == "Android" || key.stringValue == "iPhone" ? mi : 2; }
            qs.ApplyModifiedPropertiesWithoutUndo();

            // Linear colour space is mandatory for PBR
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
            Debug.Log("[ADDRESS] URP tiers configured");
        }

        static void EnsureSsao(UniversalRendererData r)
        {
            foreach (var f in r.rendererFeatures) if (f is ScreenSpaceAmbientOcclusion) return;
            var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = "SSAO";
            AssetDatabase.AddObjectToAsset(ssao, r);
            r.rendererFeatures.Add(ssao);
            var so = new SerializedObject(r);
            var map = so.FindProperty("m_RendererFeatureMap");
            map.InsertArrayElementAtIndex(map.arraySize);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out _, out long id);
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetBool(SerializedObject so, string prop, bool v) { var p = so.FindProperty(prop); if (p != null) p.boolValue = v; }
        static void SetInt(SerializedObject so, string prop, int v) { var p = so.FindProperty(prop); if (p != null) p.intValue = v; }
    }
}
