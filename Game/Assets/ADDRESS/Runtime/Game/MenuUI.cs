using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Address.Game
{
    /// <summary>
    /// Main / pause menu built in code (uGUI) over the live level: dark gradient, scanlines and vignette, a title that
    /// flickers like a failing fluorescent tube, a scrambling "address" ticker (the Library is endless), animated buttons
    /// with index numbers, and a status footer.
    /// </summary>
    public class MenuUI
    {
        public GameObject root;
        Transform page; RectTransform pageRt;
        public Action onContinue, onNewPath, onQuit, onMainMenu;
        public Action<string> onAddress;
        public Func<List<string>> archive;
        public Action onHost; public Action<string> onJoin; public Func<string> netStatus;
        public Func<string> footer, here;
        public bool paused;
        readonly UiTicker ticker;
        Text titleText, ghostA, ghostB, ticker1, footerText, hereText, crumb;
        int buttonIndex; float pageTime;
        Action back;

        public MenuUI()
        {
            root = new GameObject("Menu");
            var c = root.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 20;
            var sc = root.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            ticker = root.AddComponent<UiTicker>();
            if (!UnityEngine.Object.FindFirstObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<InputSystemUIInputModule>();
            }
            var R = root.transform;
            // backdrop: vignette, left gradient, scanlines (slowly drifting), thin accent rule
            var vig = UiKit.Image(R, "vignette", new Color(0, 0, 0, 0.75f), UiKit.Vignette); UiKit.Stretch(vig.rectTransform);
            var grad = UiKit.Image(R, "gradient", new Color(0.02f, 0.018f, 0.01f, 0.88f), UiKit.HGradient);
            grad.rectTransform.anchorMin = Vector2.zero; grad.rectTransform.anchorMax = new Vector2(0.62f, 1); grad.rectTransform.offsetMin = grad.rectTransform.offsetMax = Vector2.zero;
            var scan = UiKit.Image(R, "scanlines", new Color(0, 0, 0, 0.16f), UiKit.Scanlines); scan.type = Image.Type.Tiled; UiKit.Stretch(scan.rectTransform);
            scan.pixelsPerUnitMultiplier = 0.33f;
            var rule = UiKit.Image(R, "rule", UiKit.AccentDim); rule.rectTransform.anchorMin = new Vector2(0, 0); rule.rectTransform.anchorMax = new Vector2(0, 1);
            rule.rectTransform.offsetMin = new Vector2(96, 110); rule.rectTransform.offsetMax = new Vector2(98, -110);

            // title block
            var head = UiKit.Rect(R, "head"); head.anchorMin = head.anchorMax = head.pivot = new Vector2(0, 1); head.anchoredPosition = new Vector2(130, -96); head.sizeDelta = new Vector2(900, 200);
            ghostA = UiKit.Text(head, "ghostA", UiKit.Title, 112, new Color(1f, 0.2f, 0.2f, 0f)); Place(ghostA.rectTransform, 0, 0, 900, 130);
            ghostB = UiKit.Text(head, "ghostB", UiKit.Title, 112, new Color(0.2f, 0.9f, 1f, 0f)); Place(ghostB.rectTransform, 0, 0, 900, 130);
            titleText = UiKit.Text(head, "title", UiKit.Title, 112, UiKit.Ink); titleText.text = ghostA.text = ghostB.text = "ADDRESS"; Place(titleText.rectTransform, 0, 0, 900, 130);
            ticker1 = UiKit.Text(head, "ticker", UiKit.Mono, 22, UiKit.AccentDim); Place(ticker1.rectTransform, 6, -128, 900, 30);
            crumb = UiKit.Text(head, "crumb", UiKit.Mono, 18, UiKit.InkDim); Place(crumb.rectTransform, 6, -162, 900, 26);

            // footer + "you are here"
            footerText = UiKit.Text(R, "footer", UiKit.Mono, 17, UiKit.InkDim);
            footerText.rectTransform.anchorMin = footerText.rectTransform.anchorMax = footerText.rectTransform.pivot = new Vector2(0, 0); footerText.rectTransform.anchoredPosition = new Vector2(130, 46); footerText.rectTransform.sizeDelta = new Vector2(1000, 24);
            hereText = UiKit.Text(R, "here", UiKit.Mono, 17, UiKit.InkDim, TextAnchor.LowerRight);
            hereText.rectTransform.anchorMin = hereText.rectTransform.anchorMax = hereText.rectTransform.pivot = new Vector2(1, 0); hereText.rectTransform.anchoredPosition = new Vector2(-60, 46); hereText.rectTransform.sizeDelta = new Vector2(900, 24);

            // living backdrop: flicker, glitch, scramble ticker, drifting scanlines
            float t = 0, glitch = 0, nextGlitch = 2f, scramble = 0; string target = RandomAddress(); var rng = new System.Random();
            ticker.Add(dt =>
            {
                t += dt; pageTime += dt;
                // fluorescent flicker: mostly steady, rare double blinks
                float f = 1f; float ph = t % 7.3f; if (ph > 6.9f && ph < 7.0f || ph > 7.08f && ph < 7.14f) f = 0.35f;
                titleText.color = new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.95f * f);
                // chromatic glitch every few seconds
                nextGlitch -= dt; if (nextGlitch <= 0) { glitch = 0.18f; nextGlitch = 2.5f + (float)rng.NextDouble() * 4f; }
                glitch = Mathf.Max(0, glitch - dt); float g = glitch > 0 ? 1 : 0;
                ghostA.rectTransform.anchoredPosition = new Vector2(-6 * g + (float)rng.NextDouble() * 3 * g, 0); ghostA.color = new Color(1, 0.25f, 0.2f, 0.55f * g);
                ghostB.rectTransform.anchoredPosition = new Vector2(6 * g, 2 * g); ghostB.color = new Color(0.2f, 0.85f, 1f, 0.55f * g);
                // address ticker: scramble into a new random address every few seconds
                scramble += dt; if (scramble > 3.2f) { scramble = 0; target = RandomAddress(); }
                int fixedChars = Mathf.Clamp((int)(scramble * 40), 0, target.Length);
                var chars = target.ToCharArray(); const string hex = "0123456789ABCDEF";
                for (int i = fixedChars; i < chars.Length; i++) if (chars[i] != '-') chars[i] = hex[rng.Next(16)];
                ticker1.text = "→ " + new string(chars) + ((int)(t * 2) % 2 == 0 ? " _" : "  ");
                scan.rectTransform.offsetMin = new Vector2(0, -(t * 12f % 12f)); scan.rectTransform.offsetMax = new Vector2(0, 0);
                footerText.text = footer?.Invoke() ?? ""; var h = here?.Invoke(); hereText.text = string.IsNullOrEmpty(h) ? "" : "вы здесь  " + h;
                if (pageRt) { float a = Mathf.Clamp01(pageTime * 4f); pageRt.anchoredPosition = new Vector2(Mathf.Lerp(-24, 0, 1 - Mathf.Pow(1 - a, 3)), pageRt.anchoredPosition.y); }
                // Esc on a sub-page: back to the main page (Esc on the main page is handled by the game: resume)
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (root.activeSelf && OnSubPage && kb != null && kb.escapeKey.wasPressedThisFrame && pageTime > 0.1f) { UiKit.Click(); backFrame = Time.frameCount; var b = back; b(); }
                return true;
            });
        }

        static string RandomAddress()
        {
            var r = new System.Random(); string[] a = { "BR", "PR", "FA", "PG", "HT" }; const string hex = "0123456789ABCDEF";
            string G() { var c = new char[4]; for (int i = 0; i < 4; i++) c[i] = hex[r.Next(16)]; return new string(c); }
            return $"{a[r.Next(3)]}-{G()}-{G()}-{G()}-{G()}";
        }

        static void Place(RectTransform r, float x, float y, float w, float h) { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h); }

        public void Show(bool show, bool pausedMenu)
        {
            paused = pausedMenu; root.SetActive(show);
            if (show) Main();
        }

        /// <summary>A sub-page (settings, archive...) is open: Esc goes back inside the menu, not to the game.</summary>
        public bool OnSubPage => back != null;
        /// <summary>Frame in which Esc was used to go back inside the menu (the game must not also react to it).</summary>
        public int backFrame = -1;
        public bool EscConsumed => OnSubPage || backFrame == Time.frameCount;

        // ------------------------------------------------------------------ pages

        void NewPage(string title, Action backTo)
        {
            if (page) UnityEngine.Object.Destroy(page.gameObject);
            pageRt = UiKit.Rect(root.transform, "page"); page = pageRt;
            pageRt.anchorMin = pageRt.anchorMax = pageRt.pivot = new Vector2(0, 1); pageRt.anchoredPosition = new Vector2(0, -330); pageRt.sizeDelta = new Vector2(820, 640);
            var v = page.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 6; v.childAlignment = TextAnchor.UpperLeft; v.childControlHeight = false; v.childControlWidth = true; v.childForceExpandHeight = false;
            v.padding = new RectOffset(130, 0, 0, 0);
            crumb.text = title == null ? (paused ? "пауза" : "библиотека мест · бесконечна") : "/ " + title;
            buttonIndex = 0; pageTime = 0; back = backTo;
        }

        void Main()
        {
            NewPage(null, null);
            if (paused) Button("Продолжить", () => onContinue?.Invoke());
            else Button("Продолжить путь", () => onContinue?.Invoke());
            Button("Новый путь", () => onNewPath?.Invoke());
            Button("Ввести адрес или фразу", AddressPage);
            Button("Кооператив", CoopPage);
            Button("Архив", ArchivePage);
            Button("Настройки", SettingsPage);
            if (paused) Button("Главное меню", () => onMainMenu?.Invoke());
            Button("Выход", () => onQuit?.Invoke());
        }

        void AddressPage()
        {
            NewPage("адрес", Main);
            Label("Каждое место в Библиотеке уже существует.\nВведите адрес  BR-XXXX-XXXX-XXXX-XXXX  или любую фразу -\nиз неё получится свой адрес.", 20, UiKit.InkDim, 92);
            var field = Input("например: дом, где я вырос");
            Button("Перейти", () => { if (!string.IsNullOrWhiteSpace(field.text)) onAddress?.Invoke(field.text); });
            Button("Назад", Main);
        }

        void CoopPage()
        {
            NewPage("кооператив", Main);
            Label("До двух исследователей. Один создаёт игру, второй подключается\nпо IP в локальной сети (Wi-Fi / кабель).", 20, UiKit.InkDim, 64);
            var status = Label(netStatus?.Invoke() ?? "", 20, UiKit.Accent, 34);
            ticker.Add(_ => { if (!status) return false; status.text = netStatus?.Invoke() ?? ""; return true; });
            Button("Создать игру", () => { onHost?.Invoke(); CoopPage(); });
            var field = Input("IP друга, например 192.168.1.5");
            Button("Подключиться к IP", () => onJoin?.Invoke(string.IsNullOrWhiteSpace(field.text) ? "127.0.0.1" : field.text));
            Button("Назад", Main);
        }

        void ArchivePage()
        {
            NewPage("архив", Main);
            var list = archive?.Invoke() ?? new List<string>();
            if (list.Count == 0) Label("Пусто. Вы ещё нигде не были.", 20, UiKit.InkDim, 40);
            for (int i = 0; i < Mathf.Min(list.Count, 12); i++)
            {
                var l = Label(list[i], 18, Color.clear, 30);
                float d = i * 0.04f, a = 0; ticker.Add(dt => { if (!l) return false; d -= dt; if (d > 0) return true; a = Mathf.MoveTowards(a, 1, dt * 4); l.color = new Color(1, 1, 1, 0.8f * a); return a < 1; });
            }
            Button("Назад", Main);
        }

        void SettingsPage()
        {
            NewPage("настройки", Main);
            var names = QualitySettings.names;
            Cycle("Графика", () => names[QualitySettings.GetQualityLevel()], () =>
            {
                int q = (QualitySettings.GetQualityLevel() + 1) % names.Length; QualitySettings.SetQualityLevel(q, true); PlayerPrefs.SetInt("quality2", q);
            });
            Cycle("Авто-разрешение (60 fps)", () => Address.Rendering.FrameGovernor.Adaptive ? "вкл" : "выкл", () => Address.Rendering.FrameGovernor.Adaptive = !Address.Rendering.FrameGovernor.Adaptive);
            Cycle("Счётчик FPS  [F3]", () => Address.Rendering.FrameGovernor.ShowFps ? "вкл" : "выкл", () => Address.Rendering.FrameGovernor.ShowFps = !Address.Rendering.FrameGovernor.ShowFps);
            Cycle("Чувствительность мыши", () => Settings.Sensitivity.ToString("0.00"), () => { Settings.Sensitivity = Settings.Sensitivity >= 0.2f ? 0.03f : Settings.Sensitivity + 0.02f; });
            Cycle("Поле зрения", () => Settings.Fov.ToString("0") + "°", () => { Settings.Fov = Settings.Fov >= 95 ? 60 : Settings.Fov + 5; });
            Cycle("Инверсия по вертикали", () => Settings.InvertY ? "вкл" : "выкл", () => Settings.InvertY = !Settings.InvertY);
            Button("Назад", Main);
        }

        // ------------------------------------------------------------------ widgets

        Text Label(string s, int size, Color c, float h)
        {
            var t = UiKit.Text(page, "label", UiKit.Mono, size, c); t.text = s; t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.rectTransform.sizeDelta = new Vector2(0, h);
            return t;
        }

        HoverFx Button(string s, Action a, string value = null)
        {
            var row = UiKit.Rect(page, s); row.sizeDelta = new Vector2(0, 54);
            var hit = row.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0); // raycast target
            var b = row.gameObject.AddComponent<Button>(); b.targetGraphic = hit; b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { UiKit.Click(); a(); });
            var content = UiKit.Rect(row, "content"); content.anchorMin = new Vector2(0, 0); content.anchorMax = new Vector2(1, 1); content.offsetMin = content.offsetMax = Vector2.zero;
            var bar = UiKit.Image(content, "bar", UiKit.AccentDim); bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = bar.rectTransform.pivot = new Vector2(0, 0.5f); bar.rectTransform.anchoredPosition = new Vector2(-34, 0);
            var idx = UiKit.Text(content, "index", UiKit.Mono, 16, UiKit.InkDim); idx.text = (++buttonIndex).ToString("00"); Place2(idx.rectTransform, 0, 60);
            var label = UiKit.Text(content, "label", UiKit.Title, 32, UiKit.Ink); label.text = s; Place2(label.rectTransform, 46, 640);
            var arrow = UiKit.Text(content, "arrow", UiKit.Mono, 26, Color.clear); arrow.text = "›"; Place2(arrow.rectTransform, -22, 30);
            var fx = row.gameObject.AddComponent<HoverFx>(); fx.content = content; fx.bar = bar; fx.label = label; fx.index = idx; fx.arrow = arrow;
            fx.Appear(0.05f + buttonIndex * 0.045f);
            if (value != null)
            {
                var v = UiKit.Text(content, "value", UiKit.MonoBold, 24, UiKit.Accent, TextAnchor.MiddleRight); v.text = value;
                v.rectTransform.anchorMin = new Vector2(0, 0); v.rectTransform.anchorMax = new Vector2(1, 1); v.rectTransform.offsetMin = Vector2.zero; v.rectTransform.offsetMax = new Vector2(-40, 0);
            }
            return fx;
        }

        static void Place2(RectTransform r, float x, float w) { r.anchorMin = new Vector2(0, 0); r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 0.5f); r.anchoredPosition = new Vector2(x, 0); r.sizeDelta = new Vector2(w, 0); }

        void Cycle(string name, Func<string> value, Action next)
        {
            Text v = null;
            var fx = Button(name, () => { next(); if (v) v.text = value(); }, value());
            v = fx.transform.Find("content/value").GetComponent<Text>();
        }

        InputField Input(string placeholder)
        {
            var row = UiKit.Rect(page, "input"); row.sizeDelta = new Vector2(0, 62);
            var bg = row.gameObject.AddComponent<Image>(); bg.color = new Color(1, 1, 1, 0.06f);
            var line = UiKit.Image(row, "line", UiKit.Accent); line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0); line.rectTransform.pivot = new Vector2(0.5f, 0); line.rectTransform.sizeDelta = new Vector2(0, 2);
            var t = UiKit.Text(row, "text", UiKit.Mono, 26, UiKit.Ink); t.supportRichText = false;
            UiKit.Stretch(t.rectTransform, 18, 0, 18, 0);
            var ph = UiKit.Text(row, "placeholder", UiKit.Mono, 22, new Color(1, 1, 1, 0.28f)); ph.text = placeholder; ph.fontStyle = FontStyle.Italic;
            UiKit.Stretch(ph.rectTransform, 18, 0, 18, 0);
            var f = row.gameObject.AddComponent<InputField>(); f.textComponent = t; f.placeholder = ph; f.targetGraphic = bg; f.characterLimit = 120;
            f.caretColor = UiKit.Accent; f.customCaretColor = true; f.selectionColor = new Color(1, 0.84f, 0.32f, 0.3f);
            return f;
        }
    }

    /// <summary>Persistent player settings.</summary>
    public static class Settings
    {
        public static float Sensitivity { get => PlayerPrefs.GetFloat("sens", 0.08f); set => PlayerPrefs.SetFloat("sens", value); }
        public static float Fov { get => PlayerPrefs.GetFloat("fov", 72f); set => PlayerPrefs.SetFloat("fov", value); }
        public static bool InvertY { get => PlayerPrefs.GetInt("invy", 0) == 1; set => PlayerPrefs.SetInt("invy", value ? 1 : 0); }
    }
}
