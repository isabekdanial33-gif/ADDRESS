using System.Linq;
using Address.Mods;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Address.Game
{
    /// <summary>
    /// The chosen shader pack (Shaders/&lt;pack&gt;/pack.json): its own post-processing volume on top of everything,
    /// plus light / fog multipliers and shadow distance. "" = none.
    /// </summary>
    public static class ShaderPackFx
    {
        public static string Current { get => PlayerPrefs.GetString("shaderpack", ""); set => PlayerPrefs.SetString("shaderpack", value ?? ""); }
        static Volume vol; static Transform host; static float baseShadow = -1; static string appliedFog = "";

        public static void Apply(Transform parent)
        {
            if (parent) host = parent;
            var p = ModSystem.Packs.FirstOrDefault(x => x.name == Current);
            if (!vol && host)
            {
                var go = new GameObject("ShaderPack"); go.transform.SetParent(host, false);
                vol = go.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 20;
            }
            if (!vol) return;
            var urp = QualitySettings.renderPipeline as UniversalRenderPipelineAsset ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (urp && baseShadow < 0) baseShadow = urp.shadowDistance;
            if (p == null) { vol.weight = 0; if (urp && baseShadow > 0) urp.shadowDistance = baseShadow; return; }
            var prof = ScriptableObject.CreateInstance<VolumeProfile>(); vol.sharedProfile = prof; vol.weight = 1;
            if (p.bloom >= 0 || p.bloomThreshold >= 0) { var b = prof.Add<Bloom>(true); if (p.bloom >= 0) b.intensity.Override(p.bloom); if (p.bloomThreshold >= 0) b.threshold.Override(p.bloomThreshold); b.scatter.Override(0.75f); }
            if (p.saturation > -999 || p.contrast > -999 || p.exposure > -999 || p.colorFilter != null)
            {
                var c = prof.Add<ColorAdjustments>(true);
                if (p.saturation > -999) c.saturation.Override(p.saturation); if (p.contrast > -999) c.contrast.Override(p.contrast);
                if (p.exposure > -999) c.postExposure.Override(p.exposure); if (p.colorFilter != null) c.colorFilter.Override(ModSystem.C(p.colorFilter, Color.white));
            }
            if (p.temperature > -999) prof.Add<WhiteBalance>(true).temperature.Override(p.temperature);
            if (p.vignette >= 0) { var v = prof.Add<Vignette>(true); v.intensity.Override(p.vignette); v.smoothness.Override(0.6f); }
            if (p.grain >= 0) { var g = prof.Add<FilmGrain>(true); g.type.Override(FilmGrainLookup.Medium2); g.intensity.Override(p.grain); }
            if (p.chromatic >= 0) prof.Add<ChromaticAberration>(true).intensity.Override(p.chromatic);
            if (p.lensDistortion > -999) prof.Add<LensDistortion>(true).intensity.Override(p.lensDistortion);
            if (p.motionBlur >= 0) { var m = prof.Add<MotionBlur>(true); m.intensity.Override(p.motionBlur); m.quality.Override(MotionBlurQuality.Medium); }
            if (p.dofFocus > 0)
            {
                var d = prof.Add<DepthOfField>(true); d.mode.Override(DepthOfFieldMode.Bokeh);
                d.focusDistance.Override(p.dofFocus); d.aperture.Override(p.dofAperture > 0 ? p.dofAperture : 8f); d.focalLength.Override(40f);
            }
            if (urp && p.shadowDistance > 0) urp.shadowDistance = p.shadowDistance;
            // per place (only when a level is entered, never twice for the same lights)
            if (parent && p.fogMul > 0) RenderSettings.fogDensity *= p.fogMul;
            if (parent && p.lightMul > 0) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type != LightType.Directional) l.intensity *= p.lightMul;
            Debug.Log("[ADDRESS] shader pack: " + p.name);
        }
    }
}
