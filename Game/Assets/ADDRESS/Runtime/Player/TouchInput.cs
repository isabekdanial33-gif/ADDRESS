using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Address.Player
{
    /// <summary>
    /// Phone controls: left half = floating joystick (push far = run), right half = drag to look,
    /// buttons on the right: E (use), hand (grab/put), throw, jump, crouch, photo, pocket; pause top-left.
    /// Everything is one cheap canvas; no per-frame allocations.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TouchInput : MonoBehaviour, IPlayerInput
    {
        public float lookSensitivity = 0.12f;
        Vector2 move, look; bool run, crouch, grabP, thrP, interactP, photoP, jumpP, rotate;
        public bool PausePressed { get; private set; }
        public bool PocketPressed { get; private set; }
        int moveFinger = -1, lookFinger = -1; Vector2 moveOrigin, lastLook;
        RectTransform stickBase, stickKnob; Canvas canvas;
        readonly System.Collections.Generic.List<(RectTransform r, System.Action down, System.Action up)> buttons = new();
        public static bool Force; public static bool IsPhone => Application.isMobilePlatform || Force;

        public Vector2 Move => move; public Vector2 Look => look; public bool Run => run; public bool Crouch => crouch;
        public bool GrabPressed => grabP; public bool ThrowPressed => thrP; public bool RotateHeld => rotate; public bool InspectHeld => false;
        public bool InteractPressed => interactP; public bool PhotoPressed => photoP; public bool JumpPressed => jumpP;

        void Awake()
        {
            EnhancedTouchSupport.Enable();
            var go = new GameObject("TouchUI"); go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30;
            var sc = go.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 1f;
            stickBase = Circle(go.transform, "stick", 260, new Color(1, 1, 1, 0.10f)); stickBase.gameObject.SetActive(false);
            stickKnob = Circle(stickBase, "knob", 110, new Color(1, 1, 1, 0.35f));
            // right-hand cluster
            Btn(go.transform, "E", new Vector2(-170, 330), 130, () => interactP = true);
            Btn(go.transform, "✋", new Vector2(-330, 200), 150, () => grabP = true);
            Btn(go.transform, "↗", new Vector2(-170, 520), 110, () => thrP = true);
            Btn(go.transform, "▲", new Vector2(-150, 150), 140, () => jumpP = true);
            Btn(go.transform, "▼", new Vector2(-500, 120), 110, () => crouch = !crouch);
            Btn(go.transform, "◉", new Vector2(-480, 300), 100, () => photoP = true);
            Btn(go.transform, "G", new Vector2(-340, 430), 95, () => PocketPressed = true);
            Btn(go.transform, "R", new Vector2(-500, 470), 95, () => rotate = true, () => rotate = false);
            var pause = Btn(go.transform, "II", Vector2.zero, 100, () => PausePressed = true);
            pause.anchorMin = pause.anchorMax = new Vector2(0, 1); pause.anchoredPosition = new Vector2(90, -90);
        }

        RectTransform Circle(Transform p, string n, float size, Color c)
        {
            var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(p, false);
            var img = go.AddComponent<Image>(); img.color = c; img.sprite = Address.Game.UiKit.SoftDot; img.raycastTarget = false;
            var r = (RectTransform)go.transform; r.sizeDelta = Vector2.one * size; return r;
        }

        RectTransform Btn(Transform p, string label, Vector2 fromBottomRight, float size, System.Action down, System.Action up = null)
        {
            var r = Circle(p, "btn_" + label, size, new Color(1, 1, 1, 0.16f));
            r.anchorMin = r.anchorMax = new Vector2(1, 0); r.anchoredPosition = fromBottomRight;
            var t = Address.Game.UiKit.Text(r, "t", Address.Game.UiKit.MonoBold, (int)(size * 0.36f), new Color(1, 1, 1, 0.85f), TextAnchor.MiddleCenter);
            t.text = label; Address.Game.UiKit.Stretch(t.rectTransform);
            buttons.Add((r, down, up));
            return r;
        }

        int ButtonAt(Vector2 screen)
        {
            for (int i = 0; i < buttons.Count; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(buttons[i].r, screen, null)) return i;
            return -1;
        }

        readonly int[] heldButton = new int[16];
        void Update()
        {
            // one-frame presses are cleared by the next Update (consumers read them in their own Update this frame)
            grabP = thrP = interactP = photoP = jumpP = false; PausePressed = PocketPressed = false;
            look = Vector2.zero; bool anyMove = false;
            canvas.enabled = Cursor.lockState != CursorLockMode.None || true;
            foreach (var t in Touch.activeTouches)
            {
                int id = t.touchId & 15; var pos = t.screenPosition;
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    int b = ButtonAt(pos); heldButton[id] = b + 1;
                    if (b >= 0) { buttons[b].down?.Invoke(); continue; }
                    if (pos.x < Screen.width * 0.42f && moveFinger < 0) { moveFinger = t.touchId; moveOrigin = pos; stickBase.gameObject.SetActive(true); stickBase.position = pos; }
                    else if (lookFinger < 0) { lookFinger = t.touchId; lastLook = pos; }
                }
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    if (heldButton[id] > 0) { buttons[heldButton[id] - 1].up?.Invoke(); heldButton[id] = 0; }
                    if (t.touchId == moveFinger) { moveFinger = -1; stickBase.gameObject.SetActive(false); }
                    if (t.touchId == lookFinger) lookFinger = -1;
                    continue;
                }
                if (t.touchId == moveFinger)
                {
                    float radius = 120f * canvas.scaleFactor;
                    var d = Vector2.ClampMagnitude((pos - moveOrigin) / radius, 1.4f);
                    move = Vector2.ClampMagnitude(d, 1f); run = d.magnitude > 1.15f; anyMove = true;
                    stickKnob.position = moveOrigin + Vector2.ClampMagnitude(pos - moveOrigin, radius);
                }
                else if (t.touchId == lookFinger)
                {
                    var dlt = pos - lastLook; lastLook = pos;
                    look += dlt * lookSensitivity * (1920f / Mathf.Max(1, Screen.width)) * Address.Game.Settings.Sensitivity / 0.08f;
                    if (Address.Game.Settings.InvertY) look.y = -look.y;
                }
            }
            if (!anyMove) { move = Vector2.zero; run = false; }
        }
    }
}
