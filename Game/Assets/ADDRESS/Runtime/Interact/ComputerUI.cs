using System;
using System.Collections.Generic;
using Address.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Address.Interact
{
    /// <summary>
    /// The screen of an old computer: BIOS boot, a 2000-era desktop ("КомпОС 2000") with icons, draggable windows, a
    /// taskbar with a start button and a clock, and apps: a 2007-style browser over the generated web (WebGen),
    /// Notepad, My Computer, Recycle Bin. No mouse plugged in = no pointer; no keyboard = nothing can be typed.
    /// </summary>
    public class ComputerUI
    {
        public GameObject root; public Action onClose;
        RectTransform screen, desk, windows, taskButtons; Text clock;
        ComputerStation st; UiTicker ticker; Font sys, mono;
        static readonly Color Gray = new(0.83f, 0.82f, 0.78f), Dark = new(0.5f, 0.5f, 0.48f), Navy = new(0.04f, 0.14f, 0.45f), Teal = new(0.0f, 0.42f, 0.44f);
        readonly List<(RectTransform w, Button task)> open = new();

        public ComputerUI(ComputerStation station)
        {
            st = station; sys = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); mono = UiKit.Mono;
            root = new GameObject("Computer");
            // the desktop is drawn by its own camera into a texture, and that texture is shown through an old,
            // dirty CRT (curvature, scanlines, bleed, smudges). Same size as the screen, so the pointer still lines up.
            camGo = new GameObject("ComputerCam"); camGo.transform.position = new Vector3(0, -2000, 0);   // NOT under the canvas (the canvas follows its camera)
            var uiCam = camGo.AddComponent<Camera>(); uiCam.clearFlags = CameraClearFlags.SolidColor; uiCam.backgroundColor = Color.black; uiCam.cullingMask = ~0;   // it hangs 2 km below the world, only the desktop is in front of it uiCam.orthographic = true; uiCam.nearClipPlane = 0.01f; uiCam.farClipPlane = 10f;
            rt = new RenderTexture(Mathf.Max(640, Screen.width), Mathf.Max(480, Screen.height), 16) { name = "ComputerScreen" };
            uiCam.targetTexture = rt;
            var c = root.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = uiCam; c.planeDistance = 1f; root.layer = 5;
            var sc = root.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            ticker = root.AddComponent<UiTicker>();
            // bezel around a 4:3 picture
            var bez = UiKit.Image(root.transform, "bezel", new Color(0.05f, 0.05f, 0.045f)); UiKit.Stretch(bez.rectTransform); bez.raycastTarget = true;
            screen = UiKit.Rect(root.transform, "screen"); screen.anchorMin = screen.anchorMax = new Vector2(0.5f, 0.5f); screen.sizeDelta = new Vector2(1360, 1020);
            var bg = screen.gameObject.AddComponent<Image>(); bg.color = Color.black; screen.gameObject.AddComponent<RectMask2D>();
            var hint = UiKit.Text(root.transform, "hint", UiKit.Mono, 18, new Color(1, 1, 1, 0.35f), TextAnchor.LowerCenter);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0); hint.rectTransform.anchoredPosition = new Vector2(0, 18); hint.text = "Esc — встать из-за компьютера";
            ticker.Add(_ => { var kb = UnityEngine.InputSystem.Keyboard.current; if (kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return false; } return true; });
            // display: full-screen CRT on top of everything
            disp = new GameObject("ComputerDisplay");   // a separate root canvas (nested it would render INTO the screen texture)
            var dc = disp.AddComponent<Canvas>(); dc.renderMode = RenderMode.ScreenSpaceOverlay; dc.sortingOrder = 45;
            var ri = UiKit.Rect(disp.transform, "crt").gameObject.AddComponent<RawImage>(); UiKit.Stretch(ri.rectTransform); ri.texture = rt; ri.raycastTarget = false;
            var crt = Resources.Load<Material>("CRT"); if (crt)
            {
                ri.material = new Material(crt);
                float k = rt.height / 600f;   // the 4:3 picture is ~600 old pixels tall
                ri.material.SetVector("_Grid", new Vector4(rt.width / k, rt.height / k, 0, 0));
                ri.material.SetFloat("_Lines", 600f); ri.material.SetFloat("_Curvature", 0.08f);
            }
            Boot();
        }
        RenderTexture rt; GameObject disp, camGo;
        public void SetLayerRecursive(GameObject g) { foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5; }

        public void Close() { if (camGo) UnityEngine.Object.Destroy(camGo); if (disp) UnityEngine.Object.Destroy(disp); if (rt) rt.Release(); if (root) UnityEngine.Object.Destroy(root); onClose?.Invoke(); }

        // ------------------------------------------------------------------ boot

        void Boot()
        {
            var t = UiKit.Text(screen, "bios", mono, 22, new Color(0.8f, 0.8f, 0.8f), TextAnchor.UpperLeft); UiKit.Stretch(t.rectTransform, 30, 30, 30, 30);
            string[] lines = { "Award-совместимая BIOS v4.51PG, (C) 1984-2001", "", "Процессор: 733 МГц", "Проверка памяти: 131072K OK", "", "Обнаружены устройства IDE:", "  Первичный ведущий:  ДИСК 20 ГБ", "  Вторичный ведущий: CD-ROM 52x", "",
                st.HasKeyboard ? "Клавиатура ........ OK" : "Клавиатура ........ НЕ ОБНАРУЖЕНА",
                st.HasMouse ? "Мышь PS/2 ......... OK" : "Мышь PS/2 ......... НЕ ОБНАРУЖЕНА", "", "Загрузка КомпОС 2000..." };
            float time = 0; int shown = 0;
            ticker.Add(dt =>
            {
                if (!t) return false;
                time += dt; int want = Mathf.Min(lines.Length, (int)(time * 9));
                if (want != shown) { shown = want; t.text = string.Join("\n", lines, 0, shown); }
                if (time > lines.Length / 9f + 0.9f) { UnityEngine.Object.Destroy(t.gameObject); Desktop(); return false; }
                return true;
            });
        }

        // ------------------------------------------------------------------ desktop

        void Desktop()
        {
            desk = UiKit.Rect(screen, "desktop"); UiKit.Stretch(desk);
            desk.gameObject.AddComponent<Image>().color = Teal;
            Icon(0, "Мой компьютер", IconTex.Kind.Computer, MyComputer);
            Icon(1, "Мои документы", IconTex.Kind.Folder, () => Notepad("readme.txt", ReadmeText()));
            Icon(2, "Обозреватель", IconTex.Kind.Globe, () => Browser(WebGen.Home));
            Icon(3, "Блокнот", IconTex.Kind.Note, () => Notepad("Без имени", ""));
            Icon(4, "Корзина", IconTex.Kind.Bin, () => Dialog("Корзина", "Корзина пуста."));
            windows = UiKit.Rect(desk, "windows"); UiKit.Stretch(windows);
            Taskbar();
            if (!st.HasMouse) Dialog("Устройство не найдено", "Мышь не обнаружена.\nПодключите мышь к системному блоку кабелем.", true);
            else Browser(WebGen.Home);
        }

        string ReadmeText()
        {
            var r = new Address.Core.Rng(st.seed);
            return "прочти меня\n\nэтот компьютер работает. не выключайте его.\n\nкабели лежат в шкафах. мышь была где-то на складе.\n\nадреса, которые я проверил:\n" +
                   $"  {Addr(r)}  - тихо\n  {Addr(r)}  - бассейн\n  {Addr(r)}  - не ходите туда\n\nесли свет мигает - не оборачивайтесь.";
        }
        static string Addr(Address.Core.Rng r) { const string h = "0123456789ABCDEF"; string G() { var c = new char[4]; for (int i = 0; i < 4; i++) c[i] = h[r.Range(0, 16)]; return new string(c); } return $"{r.Pick(new[] { "BR", "PR", "FA" })}-{G()}-{G()}-{G()}-{G()}"; }

        void Icon(int i, string label, IconTex.Kind k, Action open)
        {
            var b = UiKit.Rect(desk, label); b.anchorMin = b.anchorMax = b.pivot = new Vector2(0, 1); b.anchoredPosition = new Vector2(22, -22 - i * 108); b.sizeDelta = new Vector2(110, 100);
            var hit = b.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
            var btn = b.gameObject.AddComponent<Button>(); btn.transition = Selectable.Transition.None; btn.onClick.AddListener(() => { if (st.HasMouse) open(); });
            var img = UiKit.Rect(b, "img").gameObject.AddComponent<RawImage>(); img.texture = IconTex.Get(k); img.raycastTarget = false;
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 1); img.rectTransform.pivot = new Vector2(0.5f, 1); img.rectTransform.sizeDelta = new Vector2(56, 56); img.rectTransform.anchoredPosition = new Vector2(0, -2);
            var t = UiKit.Text(b, "t", sys, 16, Color.white, TextAnchor.UpperCenter); t.text = label;
            t.rectTransform.anchorMin = new Vector2(0, 0); t.rectTransform.anchorMax = new Vector2(1, 0); t.rectTransform.pivot = new Vector2(0.5f, 0); t.rectTransform.sizeDelta = new Vector2(0, 38);
            var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = Color.black; sh.effectDistance = new Vector2(1, -1);
        }

        void Taskbar()
        {
            var bar = UiKit.Rect(desk, "taskbar"); bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(0.5f, 0); bar.sizeDelta = new Vector2(0, 44);
            bar.gameObject.AddComponent<Image>().color = Gray;
            var top = UiKit.Image(bar, "hl", Color.white); top.rectTransform.anchorMin = new Vector2(0, 1); top.rectTransform.anchorMax = new Vector2(1, 1); top.rectTransform.sizeDelta = new Vector2(0, 2);
            var start = Btn(bar, "Пуск", new Vector2(6, 5), new Vector2(96, 34), () => StartMenu(), true);
            taskButtons = UiKit.Rect(bar, "tasks"); taskButtons.anchorMin = new Vector2(0, 0); taskButtons.anchorMax = new Vector2(1, 1); taskButtons.offsetMin = new Vector2(112, 5); taskButtons.offsetMax = new Vector2(-120, -5);
            var h = taskButtons.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 4; h.childForceExpandWidth = false; h.childControlWidth = false; h.childControlHeight = true;
            var tray = UiKit.Image(bar, "tray", new Color(0.78f, 0.77f, 0.73f)); tray.rectTransform.anchorMin = tray.rectTransform.anchorMax = new Vector2(1, 0); tray.rectTransform.pivot = new Vector2(1, 0); tray.rectTransform.anchoredPosition = new Vector2(-6, 5); tray.rectTransform.sizeDelta = new Vector2(104, 34);
            Bevel(tray.rectTransform, true);
            clock = UiKit.Text(tray.transform, "clock", sys, 18, Color.black, TextAnchor.MiddleCenter); UiKit.Stretch(clock.rectTransform);
            ticker.Add(_ => { if (!clock) return false; clock.text = DateTime.Now.ToString("HH:mm"); return true; });
        }

        GameObject startMenu;
        void StartMenu()
        {
            if (startMenu) { UnityEngine.Object.Destroy(startMenu); return; }
            var m = UiKit.Rect(desk, "start"); startMenu = m.gameObject; m.anchorMin = m.anchorMax = m.pivot = new Vector2(0, 0); m.anchoredPosition = new Vector2(4, 46); m.sizeDelta = new Vector2(300, 250);
            m.gameObject.AddComponent<Image>().color = Gray;
            var side = UiKit.Image(m, "side", Navy); side.rectTransform.anchorMin = new Vector2(0, 0); side.rectTransform.anchorMax = new Vector2(0, 1); side.rectTransform.pivot = new Vector2(0, 0.5f); side.rectTransform.sizeDelta = new Vector2(34, 0);
            var st2 = UiKit.Text(side.transform, "t", sys, 18, Color.white, TextAnchor.LowerCenter); st2.text = "КомпОС"; st2.rectTransform.localEulerAngles = new Vector3(0, 0, 90); UiKit.Stretch(st2.rectTransform);
            (string, Action)[] items = { ("Обозреватель", () => Browser(WebGen.Home)), ("Блокнот", () => Notepad("Без имени", "")), ("Мои документы", () => Notepad("readme.txt", ReadmeText())), ("Мой компьютер", MyComputer), ("Завершение работы...", Close) };
            for (int i = 0; i < items.Length; i++) { var (n, a) = items[i]; Btn(m, n, new Vector2(40, 250 - 48 - i * 46), new Vector2(252, 40), () => { UnityEngine.Object.Destroy(startMenu); a(); }, false); }
        }

        // ------------------------------------------------------------------ windows

        RectTransform Window(string title, Vector2 size, out RectTransform body)
        {
            if (startMenu) UnityEngine.Object.Destroy(startMenu);
            var w = UiKit.Rect(windows, title); w.anchorMin = w.anchorMax = w.pivot = new Vector2(0.5f, 0.5f);
            w.sizeDelta = size; w.anchoredPosition = new Vector2(30 * (open.Count % 5) - 40, 20 - 26 * (open.Count % 5));
            w.gameObject.AddComponent<Image>().color = Gray;
            Bevel(w);
            var tb = UiKit.Rect(w, "title"); tb.anchorMin = new Vector2(0, 1); tb.anchorMax = new Vector2(1, 1); tb.pivot = new Vector2(0.5f, 1); tb.offsetMin = new Vector2(4, -34); tb.offsetMax = new Vector2(-4, -4);
            var tbi = tb.gameObject.AddComponent<RawImage>(); tbi.texture = IconTex.Gradient(Navy, new Color(0.25f, 0.5f, 0.85f));
            var tt = UiKit.Text(tb, "t", sys, 18, Color.white); tt.text = "  " + title; tt.fontStyle = FontStyle.Bold; UiKit.Stretch(tt.rectTransform);
            var drag = tb.gameObject.AddComponent<WindowDrag>(); drag.target = w;
            var cl = Btn(tb, "×", Vector2.zero, new Vector2(28, 24), () => CloseWindow(w), true); var cr = (RectTransform)cl.transform; cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1, 0.5f); cr.anchoredPosition = new Vector2(-3, 0);
            body = UiKit.Rect(w, "body"); body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one; body.offsetMin = new Vector2(6, 6); body.offsetMax = new Vector2(-6, -38);
            var task = Btn(taskButtons, title, Vector2.zero, new Vector2(190, 34), () => w.SetAsLastSibling(), false);
            open.Add((w, task));
            w.gameObject.AddComponent<BringToFront>();
            return w;
        }

        void CloseWindow(RectTransform w)
        {
            for (int i = open.Count - 1; i >= 0; i--) if (open[i].w == w) { if (open[i].task) UnityEngine.Object.Destroy(open[i].task.gameObject); open.RemoveAt(i); }
            UnityEngine.Object.Destroy(w.gameObject);
        }

        void Dialog(string title, string text, bool warn = false)
        {
            var w = Window(title, new Vector2(520, 220), out var body);
            var ic = UiKit.Rect(body, "i").gameObject.AddComponent<RawImage>(); ic.texture = IconTex.Get(warn ? IconTex.Kind.Warn : IconTex.Kind.Info);
            ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0, 1); ic.rectTransform.pivot = new Vector2(0, 1); ic.rectTransform.anchoredPosition = new Vector2(16, -16); ic.rectTransform.sizeDelta = new Vector2(48, 48);
            var t = UiKit.Text(body, "t", sys, 18, Color.black, TextAnchor.UpperLeft); t.text = text; t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one; t.rectTransform.offsetMin = new Vector2(80, 50); t.rectTransform.offsetMax = new Vector2(-12, -16);
            var ok = Btn(body, "OK", Vector2.zero, new Vector2(110, 34), () => CloseWindow(w), false); var r = (RectTransform)ok.transform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0); r.anchoredPosition = new Vector2(0, 8);
        }

        void MyComputer()
        {
            Window("Мой компьютер", new Vector2(560, 360), out var body);
            body.gameObject.AddComponent<Image>().color = Color.white;
            (string, IconTex.Kind, string)[] drives = { ("Диск 3,5 (A:)", IconTex.Kind.Floppy, "Нет диска в дисководе A:.\nВставьте диск и повторите попытку."), ("Локальный диск (C:)", IconTex.Kind.Drive, "Свободно 3,1 ГБ из 20 ГБ.\nПапка «Мои документы» на рабочем столе."), ("CD-дисковод (D:)", IconTex.Kind.Cd, "Устройство не готово.") };
            for (int i = 0; i < drives.Length; i++)
            {
                var (n, k, msg) = drives[i];
                var b = UiKit.Rect(body, n); b.anchorMin = b.anchorMax = b.pivot = new Vector2(0, 1); b.anchoredPosition = new Vector2(20 + i * 170, -20); b.sizeDelta = new Vector2(150, 120);
                b.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0); var bt = b.gameObject.AddComponent<Button>(); bt.onClick.AddListener(() => Dialog(n, msg, k != IconTex.Kind.Drive));
                var img = UiKit.Rect(b, "img").gameObject.AddComponent<RawImage>(); img.texture = IconTex.Get(k); img.raycastTarget = false; img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 1); img.rectTransform.pivot = new Vector2(0.5f, 1); img.rectTransform.sizeDelta = new Vector2(56, 56);
                var t = UiKit.Text(b, "t", sys, 16, Color.black, TextAnchor.UpperCenter); t.text = n; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.rectTransform.anchorMin = new Vector2(0, 0); t.rectTransform.anchorMax = new Vector2(1, 0); t.rectTransform.pivot = new Vector2(0.5f, 0); t.rectTransform.sizeDelta = new Vector2(0, 52);
            }
        }

        void Notepad(string name, string text)
        {
            Window(name + " — Блокнот", new Vector2(720, 520), out var body);
            var f = InputBox(body, text, true, 18, sys);
            UiKit.Stretch((RectTransform)f.transform);
        }

        InputField InputBox(Transform parent, string text, bool multiline, int size, Font font)
        {
            var r = UiKit.Rect(parent, "input"); var bg = r.gameObject.AddComponent<Image>(); bg.color = Color.white;
            var t = UiKit.Text(r, "text", font, size, Color.black, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft); t.supportRichText = false; t.horizontalOverflow = multiline ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.Stretch(t.rectTransform, 8, 4, 8, 4);
            Bevel(r, true);
            var f = r.gameObject.AddComponent<InputField>(); f.textComponent = t; f.targetGraphic = bg; f.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine; f.text = text;
            f.readOnly = !st.HasKeyboard; f.caretColor = Color.black; f.customCaretColor = true;
            if (!st.HasKeyboard) r.gameObject.AddComponent<OnSelectHook>().action = () => { if (!kbWarned) { kbWarned = true; Dialog("Клавиатура", "Клавиатура не обнаружена.\nПодключите клавиатуру к системному блоку.", true); } };
            return f;
        }
        bool kbWarned;

        // ------------------------------------------------------------------ browser

        void Browser(string url)
        {
            if (Application.internetReachability != NetworkReachability.NotReachable && RealBrowser.Available) { RealBrowserWindow(); return; }
            var w = Window("Обозреватель", new Vector2(1180, 860), out var body);
            var tool = UiKit.Rect(body, "toolbar"); tool.anchorMin = new Vector2(0, 1); tool.anchorMax = new Vector2(1, 1); tool.pivot = new Vector2(0.5f, 1); tool.sizeDelta = new Vector2(0, 46);
            var ti = tool.gameObject.AddComponent<RawImage>(); ti.texture = IconTex.Gradient(new Color(0.95f, 0.96f, 0.98f), new Color(0.78f, 0.82f, 0.9f), true);
            var view = UiKit.Rect(body, "view"); view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one; view.offsetMin = new Vector2(0, 28); view.offsetMax = new Vector2(0, -48);
            var statusBar = UiKit.Text(body, "status", sys, 15, Color.black); statusBar.rectTransform.anchorMin = new Vector2(0, 0); statusBar.rectTransform.anchorMax = new Vector2(1, 0); statusBar.rectTransform.pivot = new Vector2(0, 0); statusBar.rectTransform.sizeDelta = new Vector2(0, 26); statusBar.text = "  Готово";
            var history = new List<string>(); int hpos = -1;
            InputField addr = null; Text titleText = w.Find("title/t").GetComponent<Text>();
            Action<string, bool> go = null;
            go = (u, push) =>
            {
                var page = WebGen.Get(u);
                if (push) { if (hpos < history.Count - 1) history.RemoveRange(hpos + 1, history.Count - hpos - 1); history.Add(page.url); hpos = history.Count - 1; }
                addr.text = page.url; titleText.text = "  " + page.title + " — Обозреватель";
                statusBar.text = "  Загрузка...";
                float t0 = 0; ticker.Add(dt => { t0 += dt; if (!statusBar) return false; if (t0 > 0.35f) { statusBar.text = "  Готово       Интернет"; return false; } return true; });
                WebView.Render(view, page, sys, (link) => go(link, true), (q) => go(WebGen.Home + "search?q=" + Uri.EscapeDataString(q), true), st.HasKeyboard, ticker);
            };
            Btn(tool, "◄ Назад", new Vector2(6, -6), new Vector2(110, 34), () => { if (hpos > 0) { hpos--; go(history[hpos], false); } }, false).transform.SetAsLastSibling();
            Btn(tool, "Вперёд ►", new Vector2(120, -6), new Vector2(110, 34), () => { if (hpos < history.Count - 1) { hpos++; go(history[hpos], false); } }, false);
            Btn(tool, "Обновить", new Vector2(234, -6), new Vector2(100, 34), () => go(history[hpos], false), false);
            Btn(tool, "Домой", new Vector2(338, -6), new Vector2(84, 34), () => go(WebGen.Home, true), false);
            foreach (Transform b in tool) { var r = (RectTransform)b; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); }
            var ar = UiKit.Rect(tool, "addr"); ar.anchorMin = new Vector2(0, 1); ar.anchorMax = new Vector2(1, 1); ar.pivot = new Vector2(0, 1); ar.offsetMin = new Vector2(430, -40); ar.offsetMax = new Vector2(-112, -6);
            addr = InputBox(ar, url, false, 17, sys); UiKit.Stretch((RectTransform)addr.transform);
            addr.onEndEdit.AddListener(v => { var kb = UnityEngine.InputSystem.Keyboard.current; if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) go(v, true); });
            var gob = Btn(tool, "Переход", Vector2.zero, new Vector2(100, 34), () => go(addr.text, true), false); var gr = (RectTransform)gob.transform; gr.anchorMin = gr.anchorMax = gr.pivot = new Vector2(1, 1); gr.anchoredPosition = new Vector2(-6, -6);
            go(url, true);
        }

        // ------------------------------------------------------------------ real browser (Chromium via UnityWebBrowser)

        void RealBrowserWindow()
        {
            var w = Window("Обозреватель", new Vector2(1240, 900), out var body);
            var tool = UiKit.Rect(body, "toolbar"); tool.anchorMin = new Vector2(0, 1); tool.anchorMax = new Vector2(1, 1); tool.pivot = new Vector2(0.5f, 1); tool.sizeDelta = new Vector2(0, 46);
            var ti = tool.gameObject.AddComponent<RawImage>(); ti.texture = IconTex.Gradient(new Color(0.95f, 0.96f, 0.98f), new Color(0.78f, 0.82f, 0.9f), true);
            var view = UiKit.Rect(body, "view"); view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one; view.offsetMin = new Vector2(0, 28); view.offsetMax = new Vector2(0, -48);
            var statusBar = UiKit.Text(body, "status", sys, 15, Color.black); statusBar.rectTransform.anchorMin = new Vector2(0, 0); statusBar.rectTransform.anchorMax = new Vector2(1, 0); statusBar.rectTransform.pivot = new Vector2(0, 0); statusBar.rectTransform.sizeDelta = new Vector2(0, 26); statusBar.text = "  Соединение...";
            Text titleText = w.Find("title/t").GetComponent<Text>();
            var rb = RealBrowser.Create(view, st.HasMouse, st.HasKeyboard);
            InputField addr = null; bool era2007 = true; Text eraLabel = null;
            rb.onUrl = u => { if (addr && !addr.isFocused) addr.text = RealBrowser.Pretty(u); };
            rb.onTitle = t => { titleText.text = "  " + t + " — Обозреватель"; };
            rb.onStatus = s2 => { if (statusBar) statusBar.text = "  " + s2; };
            Btn(tool, "◄ Назад", new Vector2(6, -6), new Vector2(104, 34), rb.Back, false);
            Btn(tool, "Вперёд ►", new Vector2(114, -6), new Vector2(104, 34), rb.Forward, false);
            Btn(tool, "Обновить", new Vector2(222, -6), new Vector2(96, 34), rb.Refresh, false);
            Btn(tool, "Домой", new Vector2(322, -6), new Vector2(80, 34), rb.Home, false);
            var era = Btn(tool, "2007", new Vector2(406, -6), new Vector2(72, 34), () => { era2007 = !era2007; rb.era2007 = era2007; eraLabel.text = era2007 ? "2007" : "Сейчас"; rb.Refresh(); }, true);
            eraLabel = era.GetComponentInChildren<Text>();
            foreach (Transform b in tool) { var r = (RectTransform)b; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); }
            var ar = UiKit.Rect(tool, "addr"); ar.anchorMin = new Vector2(0, 1); ar.anchorMax = new Vector2(1, 1); ar.pivot = new Vector2(0, 1); ar.offsetMin = new Vector2(486, -40); ar.offsetMax = new Vector2(-112, -6);
            addr = InputBox(ar, "", false, 17, sys); UiKit.Stretch((RectTransform)addr.transform);
            addr.onEndEdit.AddListener(v => { var kb = UnityEngine.InputSystem.Keyboard.current; if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) rb.Go(v); });
            var gob = Btn(tool, "Переход", Vector2.zero, new Vector2(100, 34), () => rb.Go(addr.text), false); var gr = (RectTransform)gob.transform; gr.anchorMin = gr.anchorMax = gr.pivot = new Vector2(1, 1); gr.anchoredPosition = new Vector2(-6, -6);
            SetLayerRecursive(w.gameObject);
        }

        // ------------------------------------------------------------------ widgets

        Button Btn(Transform parent, string label, Vector2 pos, Vector2 size, Action a, bool bold)
        {
            var r = UiKit.Rect(parent, label); r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 0); r.anchoredPosition = pos; r.sizeDelta = size;
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = size.x;
            var img = r.gameObject.AddComponent<Image>(); img.color = Gray; Bevel(r);
            var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = img; var cb = b.colors; cb.pressedColor = new Color(0.7f, 0.7f, 0.68f); cb.highlightedColor = new Color(0.9f, 0.9f, 0.87f); b.colors = cb;
            b.onClick.AddListener(() => { if (st.HasMouse) { UiKit.Click(); a(); } });
            var t = UiKit.Text(r, "t", sys, 17, Color.black, TextAnchor.MiddleCenter); t.text = label; if (bold) t.fontStyle = FontStyle.Bold; UiKit.Stretch(t.rectTransform);
            return b;
        }

        /// <summary>The classic raised 3D frame: white + light grey on top/left, black + dark grey on bottom/right.</summary>
        static void Bevel(RectTransform r, bool sunken = false)
        {
            Color hi = sunken ? Dark : Color.white, hi2 = sunken ? Color.black : new Color(0.87f, 0.87f, 0.87f), lo = sunken ? Color.white : Color.black, lo2 = sunken ? new Color(0.87f, 0.87f, 0.87f) : Dark;
            void Edge(string n, Color c, Vector2 amin, Vector2 amax, Vector2 size, Vector2 off) { var i = UiKit.Image(r, n, c); i.rectTransform.anchorMin = amin; i.rectTransform.anchorMax = amax; i.rectTransform.sizeDelta = size; i.rectTransform.pivot = new Vector2(amin.x, amin.y); i.rectTransform.anchoredPosition = off; }
            Edge("lo_r", lo, new Vector2(1, 0), new Vector2(1, 1), new Vector2(2, 0), new Vector2(-2, 0));
            Edge("lo_b", lo, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 2), Vector2.zero);
            Edge("lo2_r", lo2, new Vector2(1, 0), new Vector2(1, 1), new Vector2(2, -4), new Vector2(-4, 2));
            Edge("lo2_b", lo2, new Vector2(0, 0), new Vector2(1, 0), new Vector2(-4, 2), new Vector2(2, 2));
            Edge("hi_l", hi, new Vector2(0, 0), new Vector2(0, 1), new Vector2(2, -2), new Vector2(0, 2));
            Edge("hi_t", hi, new Vector2(0, 1), new Vector2(1, 1), new Vector2(-2, 2), new Vector2(0, -2));
            Edge("hi2_l", hi2, new Vector2(0, 0), new Vector2(0, 1), new Vector2(2, -6), new Vector2(2, 4));
            Edge("hi2_t", hi2, new Vector2(0, 1), new Vector2(1, 1), new Vector2(-6, 2), new Vector2(2, -4));
        }
    }

    /// <summary>Drag a window by its title bar.</summary>
    public class WindowDrag : MonoBehaviour, IDragHandler, IBeginDragHandler
    {
        public RectTransform target;
        public void OnBeginDrag(PointerEventData e) => target.SetAsLastSibling();
        public void OnDrag(PointerEventData e) { var c = GetComponentInParent<Canvas>(); target.anchoredPosition += e.delta / (c ? c.scaleFactor : 1f); }
    }

    public class OnSelectHook : MonoBehaviour, ISelectHandler { public System.Action action; public void OnSelect(BaseEventData e) => action?.Invoke(); }

    public class BringToFront : MonoBehaviour, IPointerDownHandler { public void OnPointerDown(PointerEventData e) => transform.SetAsLastSibling(); }
}
