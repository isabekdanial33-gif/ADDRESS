using System;
using System.Collections.Generic;
using Address.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Address.Interact
{
    /// <summary>Draws a WebGen page (the offline web of the Library) into a scroll view.</summary>
    public static class WebView
    {
        public static void Render(RectTransform view, WebGen.Page page, Font font, Action<string> onLink, Action<string> onSearch, bool keyboard, UiTicker ticker)
        {
            foreach (Transform c in view) UnityEngine.Object.Destroy(c.gameObject);
            var bg = view.GetComponent<RawImage>() ?? view.gameObject.AddComponent<RawImage>();
            bg.texture = page.tile >= 0 ? IconTex.Tile(page.tile, page.tileA, page.tileB) : Texture2D.whiteTexture;
            bg.color = page.tile >= 0 ? Color.white : page.bg; bg.uvRect = page.tile >= 0 ? new Rect(0, 0, view.rect.width / 64f, view.rect.height / 64f) : new Rect(0, 0, 1, 1);
            var sr = view.GetComponent<ScrollRect>() ?? view.gameObject.AddComponent<ScrollRect>();
            if (!view.GetComponent<RectMask2D>()) view.gameObject.AddComponent<RectMask2D>();
            var content = UiKit.Rect(view, "content"); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>(); v.padding = new RectOffset(28, 28, 18, 30); v.spacing = 8; v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = content; sr.horizontal = false; sr.vertical = true; sr.scrollSensitivity = 30; sr.movementType = ScrollRect.MovementType.Clamped;
            bool dark = page.tile >= 0;
            foreach (var b in page.blocks)
            {
                switch (b.kind)
                {
                    case WebGen.Kind.Title: T(content, b.text, font, b.size > 15 ? b.size : 26, b.color, b.bold, b.center); break;
                    case WebGen.Kind.Text: T(content, b.text, font, b.size, b.color == Color.black && dark ? Color.white : b.color, b.bold, b.center); break;
                    case WebGen.Kind.Small: T(content, b.text, font, 12, b.color == Color.black ? (dark ? new Color(1, 1, 1, 0.7f) : new Color(0.35f, 0.35f, 0.35f)) : b.color, false, b.center); break;
                    case WebGen.Kind.Link:
                    {
                        var t = T(content, b.text, font, 16, b.color == Color.black ? new Color(0, 0, 0.8f) : b.color, false, false);
                        t.raycastTarget = true; var btn = t.gameObject.AddComponent<Button>(); btn.transition = Selectable.Transition.None; string u = b.url;
                        btn.onClick.AddListener(() => onLink(u));
                        var line = UiKit.Image(t.transform, "u", t.color); line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(0, 0); line.rectTransform.pivot = new Vector2(0, 0);
                        line.rectTransform.sizeDelta = new Vector2(Mathf.Min(t.preferredWidth, 1050), 1); line.rectTransform.anchoredPosition = new Vector2(0, 2);
                        break;
                    }
                    case WebGen.Kind.Rule: { var r = UiKit.Image(content, "hr", new Color(0.6f, 0.6f, 0.6f)); r.gameObject.AddComponent<LayoutElement>().preferredHeight = 2; break; }
                    case WebGen.Kind.Search:
                    {
                        var row = UiKit.Rect(content, "search"); row.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
                        var box = UiKit.Rect(row, "box"); box.anchorMin = new Vector2(0.15f, 0); box.anchorMax = new Vector2(0.7f, 1); box.offsetMin = box.offsetMax = Vector2.zero;
                        var img = box.gameObject.AddComponent<Image>(); img.color = Color.white;
                        var txt = UiKit.Text(box, "t", font, 18, Color.black); UiKit.Stretch(txt.rectTransform, 8, 0, 8, 0); txt.supportRichText = false;
                        var f = box.gameObject.AddComponent<InputField>(); f.textComponent = txt; f.text = b.text; f.readOnly = !keyboard; f.caretColor = Color.black; f.customCaretColor = true;
                        var go = UiKit.Rect(row, "go"); go.anchorMin = new Vector2(0.71f, 0); go.anchorMax = new Vector2(0.85f, 1); go.offsetMin = go.offsetMax = Vector2.zero;
                        var gi = go.gameObject.AddComponent<Image>(); gi.color = new Color(0.86f, 0.86f, 0.84f);
                        var gt = UiKit.Text(go, "t", font, 16, Color.black, TextAnchor.MiddleCenter); gt.text = "Найти!"; UiKit.Stretch(gt.rectTransform);
                        var gb = go.gameObject.AddComponent<Button>(); gb.targetGraphic = gi; gb.onClick.AddListener(() => { if (!string.IsNullOrWhiteSpace(f.text)) onSearch(f.text); });
                        f.onEndEdit.AddListener(s => { var kb = UnityEngine.InputSystem.Keyboard.current; if (kb != null && kb.enterKey.wasPressedThisFrame && !string.IsNullOrWhiteSpace(s)) onSearch(s); });
                        break;
                    }
                    case WebGen.Kind.Marquee:
                    {
                        var row = UiKit.Rect(content, "marquee"); row.gameObject.AddComponent<LayoutElement>().preferredHeight = 28; row.gameObject.AddComponent<RectMask2D>();
                        var t = UiKit.Text(row, "t", font, 18, b.color); t.text = b.text; t.fontStyle = FontStyle.Bold; t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0, 0.5f); t.rectTransform.pivot = new Vector2(0, 0.5f);
                        float x = 900; ticker.Add(dt => { if (!t) return false; x -= dt * 120; if (x < -t.preferredWidth) x = row.rect.width; t.rectTransform.anchoredPosition = new Vector2(x, 0); return true; });
                        break;
                    }
                    case WebGen.Kind.Banner:
                    {
                        var r = UiKit.Rect(content, "banner"); r.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
                        var img = r.gameObject.AddComponent<RawImage>(); img.texture = IconTex.Gradient(b.color, new Color(1, 0.3f, 0.1f), true);
                        var t = UiKit.Text(r, "t", font, 22, new Color(0.6f, 0, 0), TextAnchor.MiddleCenter); t.text = b.text; t.fontStyle = FontStyle.Bold; UiKit.Stretch(t.rectTransform);
                        float tt = 0; ticker.Add(dt => { if (!t) return false; tt += dt; t.enabled = (int)(tt * 2.5f) % 2 == 0; return true; });
                        var btn = r.gameObject.AddComponent<Button>(); string u = b.url; btn.onClick.AddListener(() => onLink(u));
                        break;
                    }
                    case WebGen.Kind.Counter:
                    {
                        var t = T(content, "Вы посетитель №  " + b.text, font, 16, Color.green, true, true); t.font = UiKit.Mono;
                        break;
                    }
                    case WebGen.Kind.Construction:
                    {
                        var r = UiKit.Rect(content, "uc"); r.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
                        var img = r.gameObject.AddComponent<RawImage>(); img.texture = IconTex.Stripes();
                        float off = 0; ticker.Add(dt => { if (!img) return false; off += dt * 0.6f; img.uvRect = new Rect(off, 0, 12, 1); return true; });
                        var t = UiKit.Text(r, "t", font, 18, Color.black, TextAnchor.MiddleCenter); t.text = b.text; t.fontStyle = FontStyle.Bold; UiKit.Stretch(t.rectTransform);
                        var bgt = t.gameObject.AddComponent<Outline>(); bgt.effectColor = Color.yellow;
                        break;
                    }
                    case WebGen.Kind.Image:
                    {
                        var r = UiKit.Rect(content, "img"); var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 200;
                        var img = UiKit.Rect(r, "i").gameObject.AddComponent<RawImage>(); img.texture = IconTex.Photo(int.Parse(b.extra));
                        img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); img.rectTransform.sizeDelta = new Vector2(266, 200);
                        break;
                    }
                    case WebGen.Kind.Post:
                    {
                        var parts = (b.extra ?? "||").Split('|');
                        var r = UiKit.Rect(content, "post"); var h = r.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 0; h.childControlHeight = true; h.childControlWidth = true; h.childForceExpandWidth = false;
                        var left = UiKit.Rect(r, "who"); var lle = left.gameObject.AddComponent<LayoutElement>(); lle.preferredWidth = 200; lle.minHeight = 110;
                        left.gameObject.AddComponent<Image>().color = new Color(0.82f, 0.86f, 0.93f);
                        var nick = UiKit.Text(left, "n", font, 15, new Color(0.1f, 0.2f, 0.5f), TextAnchor.UpperLeft); nick.text = parts[0] + "\n\n" + parts[2]; nick.fontStyle = FontStyle.Bold; UiKit.Stretch(nick.rectTransform, 8, 6, 6, 6); nick.horizontalOverflow = HorizontalWrapMode.Wrap;
                        var right = UiKit.Rect(r, "msg"); right.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                        right.gameObject.AddComponent<Image>().color = new Color(0.97f, 0.97f, 0.99f);
                        var rv = right.gameObject.AddComponent<VerticalLayoutGroup>(); rv.padding = new RectOffset(10, 10, 6, 10); rv.childControlHeight = true; rv.childControlWidth = true;
                        T(right, "Отправлено: " + parts[1], font, 12, new Color(0.4f, 0.4f, 0.4f), false, false);
                        T(right, b.text, font, 15, Color.black, false, false);
                        break;
                    }
                }
            }
        }

        static Text T(Transform p, string s, Font f, int size, Color c, bool bold, bool center)
        {
            var t = UiKit.Text(p, "t", f, size, c, center ? TextAnchor.UpperCenter : TextAnchor.UpperLeft); t.text = s; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            if (bold) t.fontStyle = FontStyle.Bold;
            return t;
        }
    }

    /// <summary>Tiny procedurally drawn icons and textures for the retro desktop.</summary>
    public static class IconTex
    {
        public enum Kind { Computer, Folder, Globe, Note, Bin, Warn, Info, Floppy, Drive, Cd, Calc, Paint, Mines, Cmd, Player }
        static readonly Dictionary<string, Texture2D> cache = new();

        public static Texture2D Get(Kind k)
        {
            string key = "i" + k; if (cache.TryGetValue(key, out var t)) return t;
            t = New(32, 32);
            Color O = new(0, 0, 0, 1), W = Color.white, G = new(0.75f, 0.75f, 0.72f), Y = new(1f, 0.85f, 0.3f), B = new(0.1f, 0.3f, 0.8f), C = new(0.0f, 0.55f, 0.55f), R = new(0.85f, 0.15f, 0.1f), Gr = new(0.2f, 0.65f, 0.2f);
            switch (k)
            {
                case Kind.Computer: Box(t, 4, 12, 24, 17, G, O); Box(t, 7, 15, 18, 11, C, O); Box(t, 10, 6, 12, 5, G, O); Box(t, 6, 2, 20, 4, G, O); break;
                case Kind.Folder: Box(t, 3, 6, 26, 18, Y, O); Box(t, 3, 22, 11, 4, Y, O); break;
                case Kind.Globe: Disc(t, 16, 16, 12, B, O); Disc(t, 12, 19, 4, Gr, Gr); Disc(t, 20, 12, 5, Gr, Gr); break;
                case Kind.Note: Box(t, 7, 3, 18, 26, W, O); for (int y = 8; y < 26; y += 4) Line(t, 10, y, 22, y, B); break;
                case Kind.Bin: Box(t, 8, 3, 16, 22, G, O); Box(t, 6, 24, 20, 3, G, O); for (int x = 11; x < 23; x += 4) Line(t, x, 6, x, 21, O); break;
                case Kind.Warn: for (int y = 2; y < 28; y++) { int hw = (28 - y) / 2; Line(t, 16 - hw, y, 16 + hw, y, Y); } Line(t, 16, 9, 16, 20, O); t.SetPixel(16, 6, O); break;
                case Kind.Info: Disc(t, 16, 16, 13, B, O); Line(t, 16, 6, 16, 18, W); Disc(t, 16, 23, 2, W, W); break;
                case Kind.Floppy: Box(t, 5, 4, 22, 24, new Color(0.2f, 0.2f, 0.25f), O); Box(t, 9, 18, 14, 9, G, O); Box(t, 8, 4, 16, 9, W, O); break;
                case Kind.Drive: Box(t, 3, 10, 26, 12, G, O); Box(t, 20, 14, 5, 2, Gr, Gr); break;
                case Kind.Cd: Disc(t, 16, 16, 13, new Color(0.85f, 0.85f, 0.9f), O); Disc(t, 16, 16, 3, W, O); break;
                case Kind.Calc: Box(t, 6, 2, 20, 28, G, O); Box(t, 9, 21, 14, 6, new Color(0.7f, 0.85f, 0.7f), O); for (int y = 5; y < 19; y += 5) for (int x = 9; x < 23; x += 5) Box(t, x, y, 4, 4, W, O); break;
                case Kind.Paint: Box(t, 4, 4, 24, 24, W, O); Disc(t, 11, 20, 4, R, R); Disc(t, 20, 18, 4, B, B); Disc(t, 15, 10, 4, Y, Y); break;
                case Kind.Mines: Disc(t, 16, 15, 9, O, O); Line(t, 16, 2, 16, 28, O); Line(t, 3, 15, 29, 15, O); Disc(t, 13, 18, 2, W, W); break;
                case Kind.Cmd: Box(t, 3, 5, 26, 22, O, G); Line(t, 6, 18, 9, 15, W); Line(t, 9, 15, 6, 12, W); Line(t, 11, 11, 16, 11, W); break;
                case Kind.Player: Box(t, 4, 6, 24, 20, new Color(0.2f, 0.25f, 0.35f), O); for (int y = 11; y < 22; y++) { int hw = (y < 16 ? y - 11 : 21 - y); Line(t, 13, y, 13 + hw, y, Gr); } break;
            }
            t.Apply(); cache[key] = t; return t;
        }

        public static Texture2D Gradient(Color a, Color b, bool vertical = false)
        {
            string key = $"g{a}{b}{vertical}"; if (cache.TryGetValue(key, out var t)) return t;
            t = new Texture2D(vertical ? 2 : 128, vertical ? 64 : 2) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < t.height; y++) for (int x = 0; x < t.width; x++) t.SetPixel(x, y, Color.Lerp(vertical ? b : a, vertical ? a : b, vertical ? y / (t.height - 1f) : x / (t.width - 1f)));
            t.Apply(); cache[key] = t; return t;
        }

        public static Texture2D Tile(int kind, Color a, Color b)
        {
            string key = $"t{kind}{a}{b}"; if (cache.TryGetValue(key, out var t)) return t;
            t = New(64, 64); t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Repeat;
            var rnd = new System.Random(kind * 31 + 7);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                bool on = kind switch { 0 => ((x / 8) + (y / 8)) % 2 == 0, 1 => (x * x + y * 3) % 17 < 3, 2 => Mathf.Abs(Mathf.Sin(x * 0.3f) * 10 - (y % 20)) < 2, _ => rnd.NextDouble() < 0.06 };
                t.SetPixel(x, y, on ? a : b);
            }
            t.Apply(); cache[key] = t; return t;
        }

        public static Texture2D Stripes()
        {
            if (cache.TryGetValue("stripes", out var t)) return t;
            t = New(32, 32); t.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) t.SetPixel(x, y, ((x + y) / 8) % 2 == 0 ? new Color(1, 0.85f, 0) : Color.black);
            t.Apply(); cache["stripes"] = t; return t;
        }

        /// <summary>A small "photo" of a strange place: blurry gradient corridor with a lit rectangle.</summary>
        public static Texture2D Photo(int seed)
        {
            string key = "p" + seed; if (cache.TryGetValue(key, out var t)) return t;
            t = New(133, 100); var r = new System.Random(seed);
            Color wall = Color.HSVToRGB((float)r.NextDouble() * 0.2f + 0.1f, 0.5f, 0.6f), floor = wall * 0.5f;
            for (int y = 0; y < 100; y++) for (int x = 0; x < 133; x++)
            {
                float vx = Mathf.Abs(x - 66) / 66f, vy = (y - 50) / 50f;
                bool corridor = vx < 0.25f + Mathf.Abs(vy) * 0.6f == false;
                Color c = vy < -0.2f ? Color.Lerp(floor, wall, (vy + 1) * 0.8f) : vy > 0.4f ? wall * 0.8f : wall;
                if (!corridor && Mathf.Abs(vy) < 0.2f && vx < 0.12f) c = Color.black;
                c *= 0.85f + (float)r.NextDouble() * 0.15f; c.a = 1;
                t.SetPixel(x, y, c);
            }
            t.Apply(); cache[key] = t; return t;
        }

        static Texture2D New(int w, int h) { var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }; var px = new Color[w * h]; t.SetPixels(px); return t; }
        static void Box(Texture2D t, int x, int y, int w, int h, Color fill, Color edge) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) t.SetPixel(i, j, (i == x || j == y || i == x + w - 1 || j == y + h - 1) ? edge : fill); }
        static void Disc(Texture2D t, int cx, int cy, int r, Color fill, Color edge) { for (int j = -r; j <= r; j++) for (int i = -r; i <= r; i++) { float d = Mathf.Sqrt(i * i + j * j); if (d <= r) t.SetPixel(cx + i, cy + j, d > r - 1.2f ? edge : fill); } }
        static void Line(Texture2D t, int x0, int y0, int x1, int y1, Color c) { int n = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)); for (int i = 0; i <= n; i++) { float k = n == 0 ? 0 : i / (float)n; t.SetPixel(Mathf.RoundToInt(Mathf.Lerp(x0, x1, k)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, k)), c); } }
    }
}
