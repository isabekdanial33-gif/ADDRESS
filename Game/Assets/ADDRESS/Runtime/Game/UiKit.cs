using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Address.Game
{
    /// <summary>Shared look of the UI: fonts, palette, generated textures, a tiny tween ticker and a UI click sound.</summary>
    public static class UiKit
    {
        public static readonly Color Accent = new Color(1f, 0.84f, 0.32f);        // warm fluorescent yellow
        public static readonly Color AccentDim = new Color(1f, 0.84f, 0.32f, 0.35f);
        public static readonly Color Ink = new Color(1f, 0.98f, 0.92f, 0.92f);
        public static readonly Color InkDim = new Color(1f, 0.98f, 0.92f, 0.45f);

        static Font title, mono, monoBold;
        public static Font Title => title ? title : title = Load("Fonts/RussoOne-Regular");
        public static Font Mono => mono ? mono : mono = Load("Fonts/IBMPlexMono-Regular");
        public static Font MonoBold => monoBold ? monoBold : monoBold = Load("Fonts/IBMPlexMono-Medium");
        static Font Load(string path) { var f = Resources.Load<Font>(path); return f ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }

        // ------------------------------------------------------------------ generated textures

        static Sprite hGrad, vignette, scan, ring, soft;
        /// <summary>Opaque on the left, transparent on the right.</summary>
        public static Sprite HGradient => hGrad ? hGrad : hGrad = Make(256, 4, (x, y) => new Color(1, 1, 1, Mathf.Pow(1 - x, 1.6f)));
        public static Sprite Vignette => vignette ? vignette : vignette = Make(128, 128, (x, y) =>
        {
            float dx = x - 0.5f, dy = y - 0.5f; float d = Mathf.Sqrt(dx * dx + dy * dy) * 1.414f;
            return new Color(1, 1, 1, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.45f, 1.05f, d)));
        });
        public static Sprite Scanlines => scan ? scan : scan = Make(4, 4, (x, y) => new Color(1, 1, 1, y < 0.5f ? 0.0f : 1f), FilterMode.Point, TextureWrapMode.Repeat);
        public static Sprite Ring => ring ? ring : ring = Make(64, 64, (x, y) =>
        {
            float d = Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) * 2f;
            return new Color(1, 1, 1, Mathf.Clamp01(1 - Mathf.Abs(d - 0.82f) * 14f));
        });
        public static Sprite SoftDot => soft ? soft : soft = Make(32, 32, (x, y) =>
        {
            float d = Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) * 2f;
            return new Color(1, 1, 1, Mathf.Clamp01((1 - d) * 3f));
        });

        static Sprite Make(int w, int h, Func<float, float, Color> f, FilterMode fm = FilterMode.Bilinear, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = fm, wrapMode = wrap };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.SetPixel(x, y, f((x + 0.5f) / w, (y + 0.5f) / h));
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
        }

        // ------------------------------------------------------------------ builders

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform r, float l = 0, float b = 0, float rt = 0, float t = 0)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(-rt, -t); }

        public static Image Image(Transform parent, string name, Color c, Sprite s = null)
        {
            var i = Rect(parent, name).gameObject.AddComponent<Image>(); i.color = c; i.sprite = s; i.raycastTarget = false; return i;
        }

        public static Text Text(Transform parent, string name, Font f, int size, Color c, TextAnchor a = TextAnchor.MiddleLeft)
        {
            var t = Rect(parent, name).gameObject.AddComponent<Text>();
            t.font = f; t.fontSize = size; t.color = c; t.alignment = a; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.supportRichText = true;
            return t;
        }

        // ------------------------------------------------------------------ click sound (synthesised, no asset)

        static AudioClip click, hover;
        static AudioSource src;
        public static void Click() => Play(click ? click : click = Blip("ui_click", 0.05f, 1400f, 700f, 0.35f));
        public static void Hover() => Play(hover ? hover : hover = Blip("ui_hover", 0.03f, 2400f, 2200f, 0.12f));
        static void Play(AudioClip c)
        {
            if (!src) { var go = new GameObject("UiAudio"); UnityEngine.Object.DontDestroyOnLoad(go); src = go.AddComponent<AudioSource>(); src.spatialBlend = 0; src.ignoreListenerPause = true; }
            src.PlayOneShot(c);
        }
        static AudioClip Blip(string n, float len, float f0, float f1, float vol)
        {
            int sr = 44100, count = (int)(sr * len); var d = new float[count]; float ph = 0;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / count; float f = Mathf.Lerp(f0, f1, t); ph += f / sr * Mathf.PI * 2;
                d[i] = Mathf.Sin(ph) * vol * Mathf.Pow(1 - t, 2.5f) * Mathf.Min(1, i / 40f);
            }
            var c = AudioClip.Create(n, count, 1, sr, false); c.SetData(d, 0); return c;
        }
    }

    /// <summary>Per-frame animation hooks for UI built in code (works while the game is paused: unscaled time).</summary>
    public class UiTicker : MonoBehaviour
    {
        readonly List<Func<float, bool>> jobs = new();
        public void Add(Func<float, bool> job) => jobs.Add(job);   // return false to stop
        public void Clear() => jobs.Clear();
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = jobs.Count - 1; i >= 0; i--) { bool keep; try { keep = jobs[i](dt); } catch { keep = false; } if (!keep) jobs.RemoveAt(i); }
        }
    }

    /// <summary>Hover/selection animation of a menu button: slides right, accent bar grows, label warms up.</summary>
    public class HoverFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public RectTransform content; public Image bar; public Text label, index, arrow;
        public Color normal = UiKit.Ink, hot = UiKit.Accent;
        float k, target; public float appear; float delay;
        public void Appear(float d) { delay = d; appear = 0; }
        public void OnPointerEnter(PointerEventData e) { target = 1; UiKit.Hover(); }
        public void OnPointerExit(PointerEventData e) => target = 0;
        public void OnSelect(BaseEventData e) => target = 1;
        public void OnDeselect(BaseEventData e) => target = 0;
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (delay > 0) delay -= dt; else appear = Mathf.MoveTowards(appear, 1, dt * 3.2f);
            k = Mathf.MoveTowards(k, target, dt * 7f);
            float e = 1 - Mathf.Pow(1 - k, 3), a = 1 - Mathf.Pow(1 - appear, 3);
            if (content) content.anchoredPosition = new Vector2(Mathf.Lerp(-40, 0, a) + e * 16f, 0);
            if (bar) { bar.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(2, 6, e), Mathf.Lerp(10, 34, e)); bar.color = Color.Lerp(UiKit.AccentDim, UiKit.Accent, e) * new Color(1, 1, 1, a); }
            if (label) label.color = Color.Lerp(normal, hot, e) * new Color(1, 1, 1, a);
            if (index) index.color = Color.Lerp(UiKit.InkDim, UiKit.Accent, e) * new Color(1, 1, 1, a * 0.9f);
            if (arrow) arrow.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, e * a);
        }
    }
}
