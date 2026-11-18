using UnityEngine;
using UnityEngine.UI;

namespace Address.Game
{
    /// <summary>
    /// In-game HUD: almost nothing on screen while exploring. A dot that opens into a ring over something usable,
    /// key-cap prompts ("E открыть", "ЛКМ взять"), a title card with the address when a place is entered (typed out,
    /// then it settles in the corner), and short toasts (photo score).
    /// </summary>
    public class Hud
    {
        public GameObject root;
        public Image fade;
        readonly Image dot, ring;
        readonly RectTransform promptRow; readonly Text promptText; readonly Image keyBg; readonly Text keyText;
        readonly Text cardTitle, cardSub, corner, toast, toastSub;
        readonly Image cardLine;
        float ringK, promptK, cardT = 99f, toastT = 99f; string cardFull = "", cardName = "";
        string promptKey, promptLabel;

        public Hud()
        {
            root = new GameObject("HUD");
            var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var sc = root.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            var R = root.transform;
            var vig = UiKit.Image(R, "vignette", new Color(0, 0, 0, 0.35f), UiKit.Vignette); UiKit.Stretch(vig.rectTransform);

            dot = UiKit.Image(R, "dot", new Color(1, 1, 1, 0.5f), UiKit.SoftDot); dot.rectTransform.sizeDelta = new Vector2(7, 7);
            ring = UiKit.Image(R, "ring", new Color(1, 1, 1, 0), UiKit.Ring); ring.rectTransform.sizeDelta = new Vector2(34, 34);

            // prompt: [key] label, under the crosshair
            promptRow = UiKit.Rect(R, "prompt"); promptRow.sizeDelta = new Vector2(600, 40); promptRow.anchoredPosition = new Vector2(0, -60);
            keyBg = UiKit.Image(promptRow, "key", new Color(1, 1, 1, 0.9f)); keyBg.rectTransform.anchorMin = keyBg.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            keyText = UiKit.Text(keyBg.transform, "k", UiKit.MonoBold, 22, new Color(0.08f, 0.07f, 0.04f), TextAnchor.MiddleCenter); UiKit.Stretch(keyText.rectTransform);
            promptText = UiKit.Text(promptRow, "label", UiKit.Mono, 24, UiKit.Ink); promptText.rectTransform.anchorMin = promptText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            promptText.rectTransform.pivot = new Vector2(0, 0.5f); promptText.rectTransform.sizeDelta = new Vector2(500, 40);
            var sh = promptText.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.6f); sh.effectDistance = new Vector2(1, -1);

            // title card (entering a place)
            cardLine = UiKit.Image(R, "cardLine", UiKit.Accent); cardLine.rectTransform.anchorMin = cardLine.rectTransform.anchorMax = new Vector2(0.5f, 0.28f); cardLine.rectTransform.sizeDelta = new Vector2(0, 2);
            cardTitle = UiKit.Text(R, "cardTitle", UiKit.Mono, 42, UiKit.Ink, TextAnchor.MiddleCenter); Anchor(cardTitle.rectTransform, new Vector2(0.5f, 0.28f), new Vector2(0, 36), new Vector2(1200, 50));
            cardSub = UiKit.Text(R, "cardSub", UiKit.Title, 28, UiKit.Accent, TextAnchor.MiddleCenter); Anchor(cardSub.rectTransform, new Vector2(0.5f, 0.28f), new Vector2(0, -26), new Vector2(1200, 34));
            foreach (var t in new[] { cardTitle, cardSub }) { var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, 0.7f); s.effectDistance = new Vector2(2, -2); }

            corner = UiKit.Text(R, "corner", UiKit.Mono, 15, new Color(1, 1, 1, 0.3f), TextAnchor.LowerLeft); Anchor(corner.rectTransform, new Vector2(0, 0), new Vector2(24, 18), new Vector2(700, 24), new Vector2(0, 0));

            toast = UiKit.Text(R, "toast", UiKit.Title, 40, UiKit.Accent, TextAnchor.MiddleCenter); Anchor(toast.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(800, 60));
            toastSub = UiKit.Text(R, "toastSub", UiKit.Mono, 18, UiKit.Ink, TextAnchor.MiddleCenter); Anchor(toastSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 82), new Vector2(800, 30));
            foreach (var t in new[] { toast, toastSub }) { var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, 0.7f); s.effectDistance = new Vector2(2, -2); }

            fade = UiKit.Image(R, "fade", Color.black); UiKit.Stretch(fade.rectTransform);
            root.AddComponent<UiTicker>().Add(Tick);
        }

        static void Anchor(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        { r.anchorMin = r.anchorMax = anchor; r.pivot = pivot ?? new Vector2(0.5f, 0.5f); r.anchoredPosition = pos; r.sizeDelta = size; }

        RawImage snapshot;
        public void ShowSnapshot(Texture2D t)
        {
            if (!snapshot) { snapshot = UiKit.Rect(root.transform, "snapshot").gameObject.AddComponent<RawImage>(); UiKit.Stretch(snapshot.rectTransform); snapshot.raycastTarget = false; }
            snapshot.transform.SetAsLastSibling(); snapshot.texture = t; snapshot.color = Color.white; snapshot.gameObject.SetActive(true);
        }
        public System.Collections.IEnumerator DissolveSnapshot(float time)
        {
            for (float t = 0; t < time && snapshot; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0, 1, t / time);
                snapshot.color = new Color(1, 1, 1, 1 - k);
                snapshot.rectTransform.localScale = Vector3.one * (1 + k * 0.06f);   // a slight drift forward, like walking through
                yield return null;
            }
            if (snapshot) { snapshot.gameObject.SetActive(false); snapshot.rectTransform.localScale = Vector3.one; }
        }

        Text loading;
        /// <summary>While a place is being built the screen is not just black: the address is shown on it.</summary>
        public void Loading(string text)
        {
            if (!loading)
            {
                loading = UiKit.Text(root.transform, "loading", UiKit.Mono, 26, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleCenter);
                Anchor(loading.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400, 80));
            }
            loading.transform.SetAsLastSibling(); loading.text = text ?? ""; loading.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        public void SetVisible(bool v)
        {
            foreach (Transform c in root.transform)
                if (c != fade.transform && (!snapshot || c != snapshot.transform)) c.gameObject.SetActive(v);
        }

        /// <summary>Crosshair state + prompt. key null = no prompt.</summary>
        public void Aim(bool usable, string key = null, string label = null) { aimUsable = usable; promptKey = key; promptLabel = label; }
        bool aimUsable;

        public void EnterPlace(string address, string placeName)
        {
            cardFull = address; cardName = placeName; cardT = 0; corner.text = "";
        }

        Text pocketsText; string lastPockets = null;
        /// <summary>The four pockets, always visible at the bottom: [1] кабель  [2] мышь  [3] —  [4] —</summary>
        public void SetPockets(System.Collections.Generic.List<string> names)
        {
            if (!pocketsText)
            {
                pocketsText = UiKit.Text(root.transform, "pockets", UiKit.Mono, 18, new Color(1, 1, 1, 0.75f), TextAnchor.LowerCenter);
                Anchor(pocketsText.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(1200, 30), new Vector2(0.5f, 0));
                var sh = pocketsText.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.7f); sh.effectDistance = new Vector2(1, -1);
            }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 4; i++) sb.Append($"[{i + 1}] {(i < names.Count ? names[i] : "—")}     ");
            var s = sb.ToString() + "  G — в карман";
            if (s != lastPockets) { lastPockets = s; pocketsText.text = s; }
        }

        public void Toast(string big, string small) { toast.text = big; toastSub.text = small; toastT = 0; }

        bool Tick(float dt)
        {
            // crosshair
            ringK = Mathf.MoveTowards(ringK, aimUsable ? 1 : 0, dt * 8f);
            float e = 1 - Mathf.Pow(1 - ringK, 3);
            ring.color = new Color(1, 1, 1, 0.75f * e); ring.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(16, 30, e);
            dot.color = new Color(1, 1, 1, Mathf.Lerp(0.45f, 0.95f, e));

            // prompt
            bool show = !string.IsNullOrEmpty(promptKey);
            promptK = Mathf.MoveTowards(promptK, show ? 1 : 0, dt * 7f);
            if (show)
            {
                keyText.text = promptKey; promptText.text = promptLabel;
                float kw = Mathf.Max(36, promptKey.Length * 14 + 18), lw = promptText.preferredWidth, total = kw + 10 + lw;
                keyBg.rectTransform.sizeDelta = new Vector2(kw, 36); keyBg.rectTransform.anchoredPosition = new Vector2(-total / 2 + kw / 2, 0);
                promptText.rectTransform.anchoredPosition = new Vector2(-total / 2 + kw + 10, 0);
            }
            float pa = promptK;
            keyBg.color = new Color(1, 0.96f, 0.86f, 0.9f * pa); keyText.color = new Color(0.08f, 0.07f, 0.04f, pa); promptText.color = new Color(1, 1, 1, 0.9f * pa);
            promptRow.anchoredPosition = new Vector2(0, -56 - (1 - pa) * 8);

            // title card: type the address, show the place name, hold, fade; then leave it in the corner
            cardT += dt;
            int chars = Mathf.Clamp((int)((cardT - 0.3f) * 34), 0, cardFull.Length);
            float cardA = cardT < 0.3f ? 0 : cardT < 4.2f ? 1 : Mathf.Clamp01(1 - (cardT - 4.2f) / 1.2f);
            cardTitle.text = cardT < 6f ? cardFull.Substring(0, chars) + (chars < cardFull.Length || (int)(cardT * 3) % 2 == 0 ? "_" : " ") : "";
            cardTitle.color = new Color(1, 1, 1, 0.92f * cardA);
            float nameA = Mathf.Clamp01((cardT - 0.3f - cardFull.Length / 34f) * 2f) * cardA;
            cardSub.text = cardName.ToUpperInvariant(); cardSub.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, nameA);
            float lineW = Mathf.Lerp(0, 520, Mathf.Clamp01((cardT - 0.2f) * 2.5f)) * cardA;
            cardLine.rectTransform.sizeDelta = new Vector2(lineW, 2); cardLine.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.8f * cardA);
            if (cardT > 5.2f && corner.text == "" && cardFull != "") corner.text = cardFull;

            // toast: pops, rises a little, fades
            toastT += dt;
            float ta = toastT < 0.12f ? toastT / 0.12f : Mathf.Clamp01(1 - (toastT - 1.4f) / 0.6f);
            float pop = 1 + Mathf.Max(0, 0.25f - toastT) * 1.2f;
            toast.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, ta); toastSub.color = new Color(1, 1, 1, 0.85f * ta);
            toast.rectTransform.localScale = Vector3.one * pop; toast.rectTransform.anchoredPosition = new Vector2(0, 120 + toastT * 12f);
            return true;
        }
    }
}
