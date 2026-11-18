using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Address.Rendering
{
    /// <summary>
    /// Keeps the frame rate at the target (60 fps by default) by nudging the URP render scale between a floor and the
    /// tier's native scale. Changes are small and rare (no visible pumping); the floor keeps the image sharp.
    /// Also draws an optional FPS counter (average + 1% low over the last second).
    /// </summary>
    public class FrameGovernor : MonoBehaviour
    {
        public static bool Adaptive { get => PlayerPrefs.GetInt("adaptres", 1) == 1; set => PlayerPrefs.SetInt("adaptres", value ? 1 : 0); }
        public static bool ShowFps { get => PlayerPrefs.GetInt("showfps", 0) == 1; set => PlayerPrefs.SetInt("showfps", value ? 1 : 0); }
        public static float TargetFps = 60f;
        public const float MinScale = 0.72f;

        readonly float[] native = new float[8];
        float smooth = 1f / 60f, cooldown, scale = -1f; int lastTier = -1;
        Text label;
        readonly float[] window = new float[240]; int wCount, wHead; float labelTimer;

        void Start()
        {
            for (int i = 0; i < QualitySettings.names.Length && i < native.Length; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset a) native[i] = a.renderScale;
            var c = new GameObject("FpsCanvas").AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 50;
            c.transform.SetParent(transform, false);
            label = new GameObject("fps").AddComponent<Text>(); label.transform.SetParent(c.transform, false);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 18; label.color = new Color(1, 1, 1, 0.85f);
            label.alignment = TextAnchor.UpperRight; label.raycastTarget = false;
            var r = label.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(1, 1); r.anchoredPosition = new Vector2(-14, -10); r.sizeDelta = new Vector2(420, 60);
        }

        UniversalRenderPipelineAsset Asset => QualitySettings.renderPipeline as UniversalRenderPipelineAsset;

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame) { ShowFps = !ShowFps; labelTimer = 0; }
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0 || dt > 0.5f) return; // loading hitches are not performance
            window[wHead] = dt; wHead = (wHead + 1) % window.Length; wCount = Mathf.Min(wCount + 1, window.Length);
            smooth = Mathf.Lerp(smooth, dt, 0.05f);

            int tier = QualitySettings.GetQualityLevel(); var a = Asset;
            if (a == null) return;
            float top = native[Mathf.Clamp(tier, 0, native.Length - 1)]; if (top <= 0) top = 1f;
            if (tier != lastTier) { lastTier = tier; scale = top; a.renderScale = scale; cooldown = 1.5f; }
            if (!Adaptive) { if (Mathf.Abs(a.renderScale - top) > 0.001f) a.renderScale = scale = top; }
            else
            {
                cooldown -= dt;
                float budget = 1f / TargetFps;
                if (cooldown <= 0)
                {
                    if (smooth > budget * 1.04f && scale > MinScale) { scale = Mathf.Max(MinScale, scale - 0.05f); a.renderScale = scale; cooldown = 1.0f; }
                    else if (smooth < budget * 0.80f && scale < top) { scale = Mathf.Min(top, scale + 0.05f); a.renderScale = scale; cooldown = 2.5f; }
                }
            }

            labelTimer -= dt;
            if (label && labelTimer <= 0)
            {
                labelTimer = 0.25f;
                label.enabled = ShowFps;
                if (ShowFps && wCount > 10)
                {
                    float sum = 0, worst = 0; int n = Mathf.Min(wCount, 120);
                    var tmp = new float[n];
                    for (int i = 0; i < n; i++) { float v = window[(wHead - 1 - i + window.Length) % window.Length]; tmp[i] = v; sum += v; }
                    System.Array.Sort(tmp); worst = tmp[Mathf.Max(0, n - 1 - n / 100)];
                    label.text = $"{n / sum:0} fps   1% low {1f / worst:0}   {QualitySettings.names[tier]} x{a.renderScale:0.00}";
                }
            }
        }

        void OnDestroy() { var a = Asset; if (a != null && lastTier >= 0) a.renderScale = native[Mathf.Clamp(lastTier, 0, native.Length - 1)]; }
    }
}
