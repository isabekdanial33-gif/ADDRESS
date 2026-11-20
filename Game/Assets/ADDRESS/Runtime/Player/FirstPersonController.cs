using UnityEngine;

namespace Address.Player
{
    /// <summary>
    /// Slow, grounded first-person movement for liminal exploration: walk 1.5 m/s, run 3.4 m/s, crouch to 1.05 m,
    /// subtle head bob and breathing sway. CharacterController keeps it cheap and stable on carpets/steps.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        public Transform head;                // camera pivot
        public float walkSpeed = 1.5f, runSpeed = 3.4f, crouchSpeed = 0.9f;
        public float standHeight = 1.75f, crouchHeight = 1.05f, eyeOffset = 0.12f;
        // crawling (Z toggles; vents force it): on hands and knees, 0.6 m high
        public float crawlHeight = 0.6f, crawlSpeed = 0.75f;
        public bool ForceCrawl;
        bool crawlToggle;
        public bool Crawling => height < (crouchHeight + crawlHeight) * 0.5f;
        public float gravity = 18f, accel = 10f, jumpHeight = 0.55f;
        float coyote; public bool Jumped { get; private set; }
        public float bobAmount = 0.022f, bobFreq = 1.85f;
        public float speedMultiplier = 1f;     // reduced while carrying something heavy
        public IPlayerInput input;

        CharacterController cc;
        float pitch, yaw, bobPhase, vy, height;
        Vector3 vel;
        public bool LookLocked { get; set; }
        public Vector3 Velocity => cc ? cc.velocity : Vector3.zero;
        /// <summary>Where the player is trying to go (input), even when blocked by what they are pushing.</summary>
        public Vector3 WishVelocity { get; private set; }
        public bool Grounded => cc && cc.isGrounded;
        public float VerticalSpeed => vy;
        // ---- sitting
        public bool Seated { get; private set; }
        public float SeatHeight { get; private set; }     // seat surface above the floor
        float seatYaw, sitEye;
        public void Sit(Vector3 floorPos, float facingYaw, float seatHeight)
        {
            Seated = true; SeatHeight = seatHeight; seatYaw = facingYaw; sitEye = seatHeight + 0.72f;
            cc.enabled = false; transform.position = floorPos; yaw = facingYaw; pitch = 8f; vel = Vector3.zero; vy = 0;
            Apply(0);
        }
        public void Stand(Vector3 floorPos)
        {
            Seated = false; transform.position = floorPos; cc.enabled = true; vel = Vector3.zero; vy = 0; Apply(0);
        }
        public float Pitch => pitch;

        void Awake() => Init();

        public void Init()
        {
            cc = GetComponent<CharacterController>();
            cc.height = standHeight; cc.radius = 0.28f; cc.center = new Vector3(0, standHeight / 2, 0);
            cc.stepOffset = 0.3f; cc.slopeLimit = 46f; cc.minMoveDistance = 0f;   // at high fps a frame step is < 1 mm and would be ignored cc.skinWidth = 0.02f;
            height = standHeight;
            yaw = transform.eulerAngles.y;
            input ??= new DeviceInput();
        }

        public void SetLook(float yawDeg, float pitchDeg) { yaw = yawDeg; pitch = pitchDeg; Apply(0); }

        void Update() { if (Application.isPlaying) Tick(Time.deltaTime); }

        public void Tick(float dt)
        {
            if (dt <= 0) return;
            if (!LookLocked)
            {
                var l = input.Look;
                yaw += l.x; pitch = Mathf.Clamp(pitch - l.y, -85f, 85f);
                if (Seated) yaw = seatYaw + Mathf.Clamp(Mathf.DeltaAngle(seatYaw, yaw), -110f, 110f);
            }
            if (Seated) { Apply(dt); return; }
            // crouch (only stand up if there is head room)
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.zKey.wasPressedThisFrame) crawlToggle = !crawlToggle;
            bool wantCrawl = ForceCrawl || crawlToggle;
            bool wantCrouch = input.Crouch && !wantCrawl;
            float target = wantCrawl ? crawlHeight : wantCrouch ? crouchHeight : standHeight;
            if (target > height + 0.01f)
            {
                var top = transform.position + Vector3.up * (height - cc.radius);
                if (Physics.SphereCast(top, cc.radius * 0.95f, Vector3.up, out _, target - height + 0.05f, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
                    target = height;   // no head room: stay down
            }
            height = Mathf.MoveTowards(height, target, dt * 3.5f);
            cc.height = height; cc.center = new Vector3(0, height / 2, 0);

            var mv = input.Move;
            float speed = (Crawling ? crawlSpeed : wantCrouch || height < standHeight - 0.2f ? crouchSpeed : input.Run && mv.y > 0.1f ? runSpeed : walkSpeed) * speedMultiplier;
            var fwd = Quaternion.Euler(0, yaw, 0);
            var wish = fwd * new Vector3(mv.x, 0, mv.y) * speed;
            WishVelocity = wish;
            if (cc.isGrounded) vel = Vector3.Lerp(vel, wish, 1 - Mathf.Exp(-accel * dt));
            if (cc.isGrounded) coyote = 0.12f; else coyote -= dt;
            Jumped = false;
            if (input.JumpPressed && coyote > 0 && !wantCrouch && !wantCrawl && height > standHeight - 0.1f) { vy = Mathf.Sqrt(2f * gravity * jumpHeight); coyote = 0; Jumped = true; }
            else if (cc.isGrounded && vy < 0) vy = -1f; else vy -= gravity * dt;
            // in the air: keep momentum, only slight steering
            if (!cc.isGrounded) vel = Vector3.Lerp(vel, wish, 1 - Mathf.Exp(-accel * 0.25f * dt));
            cc.Move((vel + Vector3.up * vy) * dt);
            // safety: if furniture ever wedges the capsule into the floor, put the feet back on top of it
            if (Physics.Raycast(transform.position + Vector3.up * 1.2f, Vector3.down, out var floorHit, 1.6f, 1 << 8, QueryTriggerInteraction.Ignore)
                && floorHit.normal.y > 0.9f && floorHit.point.y > transform.position.y + 0.04f && floorHit.point.y < transform.position.y + 0.5f)
            {
                cc.enabled = false; transform.position = new Vector3(transform.position.x, floorHit.point.y + 0.01f, transform.position.z); cc.enabled = true; vy = 0;
            }
            Apply(dt);
        }

        void Apply(float dt)
        {
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (!head) return;
            float planar = new Vector2(vel.x, vel.z).magnitude;
            if (cc && cc.isGrounded && planar > 0.1f) bobPhase += dt * bobFreq * Mathf.PI * 2f * Mathf.Lerp(0.8f, 1.35f, planar / runSpeed);
            float bob = Mathf.Sin(bobPhase) * bobAmount * Mathf.Clamp01(planar / walkSpeed);
            float sway = Mathf.Sin(bobPhase * 0.5f) * bobAmount * 0.6f * Mathf.Clamp01(planar / walkSpeed);
            float breathe = Mathf.Sin(Time.time * 1.3f) * 0.004f;
            head.localPosition = Seated ? new Vector3(0, sitEye + breathe, 0.05f) : new Vector3(sway, height - eyeOffset + bob + breathe, 0);
            head.localRotation = Quaternion.Euler(pitch, 0, -sway * 25f);
        }
    }
}
