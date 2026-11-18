using UnityEngine;
using UnityEngine.InputSystem;

namespace Address.Player
{
    /// <summary>Abstract input so the same controller can be driven by a human or by the automated play-test bot.</summary>
    public interface IPlayerInput
    {
        Vector2 Move { get; }          // x = strafe, y = forward
        Vector2 Look { get; }          // degrees this frame
        bool Run { get; }
        bool Crouch { get; }
        bool GrabPressed { get; }      // pick up / put down (gentle place)
        bool ThrowPressed { get; }
        bool RotateHeld { get; }       // while true, Look rotates the held object
        bool InspectHeld { get; }
        bool InteractPressed { get; }
        bool PhotoPressed { get; }
        bool JumpPressed { get; }
    }

    /// <summary>Keyboard + mouse (and gamepad) through the Input System, actions built in code (no asset needed).</summary>
    public class DeviceInput : IPlayerInput
    {
        readonly InputAction move, look, run, crouch, grab, thr, rotate, inspect, interact, photo, jump;
        public float mouseSensitivity = 0.08f;
        public bool invertY;

        public DeviceInput()
        {
            move = new InputAction("move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");
            look = new InputAction("look", InputActionType.Value, "<Mouse>/delta");
            look.AddBinding("<Gamepad>/rightStick").WithProcessor("scaleVector2(x=12,y=12)");
            run = new InputAction("run", InputActionType.Button, "<Keyboard>/leftShift"); run.AddBinding("<Gamepad>/leftStickPress");
            crouch = new InputAction("crouch", InputActionType.Button, "<Keyboard>/leftCtrl"); crouch.AddBinding("<Keyboard>/c"); crouch.AddBinding("<Gamepad>/buttonEast");
            grab = new InputAction("grab", InputActionType.Button, "<Mouse>/leftButton"); grab.AddBinding("<Gamepad>/rightTrigger");
            thr = new InputAction("throw", InputActionType.Button, "<Mouse>/rightButton"); thr.AddBinding("<Gamepad>/leftTrigger");
            rotate = new InputAction("rotate", InputActionType.Button, "<Keyboard>/r"); rotate.AddBinding("<Gamepad>/rightShoulder");
            inspect = new InputAction("inspect", InputActionType.Button, "<Keyboard>/q"); inspect.AddBinding("<Gamepad>/leftShoulder");
            interact = new InputAction("interact", InputActionType.Button, "<Keyboard>/e"); interact.AddBinding("<Gamepad>/buttonSouth");
            photo = new InputAction("photo", InputActionType.Button, "<Keyboard>/f"); photo.AddBinding("<Gamepad>/buttonNorth");
            jump = new InputAction("jump", InputActionType.Button, "<Keyboard>/space"); jump.AddBinding("<Gamepad>/buttonWest");
            foreach (var a in new[] { move, look, run, crouch, grab, thr, rotate, inspect, interact, photo, jump }) a.Enable();
        }

        public Vector2 Move => Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
        public Vector2 Look { get { var d = look.ReadValue<Vector2>() * mouseSensitivity; if (invertY) d.y = -d.y; return d; } }
        public bool Run => run.IsPressed();
        public bool Crouch => crouch.IsPressed();
        public bool GrabPressed => grab.WasPressedThisFrame();
        public bool ThrowPressed => thr.WasPressedThisFrame();
        public bool RotateHeld => rotate.IsPressed();
        public bool InspectHeld => inspect.IsPressed();
        public bool InteractPressed => interact.WasPressedThisFrame();
        public bool PhotoPressed => photo.WasPressedThisFrame();
        public bool JumpPressed => jump.WasPressedThisFrame();
    }

    /// <summary>Scripted input for automated play-tests.</summary>
    public class ScriptedInput : IPlayerInput
    {
        public Vector2 move, look; public bool jump, run, crouch, grab, thr, rotate, inspect, interact, photo;
        public Vector2 Move => move; public Vector2 Look => look; public bool Run => run; public bool Crouch => crouch;
        public bool GrabPressed => grab; public bool ThrowPressed => thr; public bool RotateHeld => rotate;
        public bool InspectHeld => inspect; public bool InteractPressed => interact; public bool PhotoPressed => photo; public bool JumpPressed => jump;
        public void ClearEdges() { grab = thr = interact = photo = jump = false; look = Vector2.zero; }
    }
}
