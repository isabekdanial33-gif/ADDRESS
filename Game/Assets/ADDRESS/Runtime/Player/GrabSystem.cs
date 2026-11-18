using System.Collections.Generic;
using Address.Placement;
using UnityEngine;
using Pose = Address.Placement.Pose;

namespace Address.Player
{
    /// <summary>
    /// Hands: pick up small/medium objects with a velocity "spring" (held objects collide with the world and can
    /// never pass through walls), place them gently with the SAME placement checks the generator uses, throw them,
    /// or push/pull heavy furniture. Anything resting on a moved object becomes physical too, so it rides or falls.
    /// </summary>
    public class GrabSystem : MonoBehaviour
    {
        public Camera cam;
        public FirstPersonController fpc;
        public IPlayerInput input;
        public PlacementEngine engine;
        public float reach = 2.2f;
        public int propsLayer = 9, archLayer = 8, heldLayer = 12;
        public Material ghostOk, ghostBad;

        public PlacedObject Held { get; private set; }
        public PlacedObject Pushing { get; private set; }
        public string LastEvent { get; private set; } = "";
        public PlacedObject Aimed { get; private set; }
        public string DebugPush = "";
        /// <summary>Raised ~10x/s for objects the player is moving (held, pushed, recently thrown/placed) - used by co-op sync.</summary>
        public System.Action<PlacedObject> onObjectMoved;
        readonly System.Collections.Generic.Dictionary<PlacedObject, float> settling = new();
        float syncTimer; readonly System.Collections.Generic.List<PlacedObject> doneBuf = new();

        Rigidbody rb;
        Vector3 pushVel;
        float holdDist, strain;
        Quaternion holdRot;                 // held object's rotation relative to the player's yaw
        readonly Dictionary<Collider, int> savedLayers = new();
        GameObject ghost; bool ghostValid; Pose ghostPose; PlacedObject ghostSupport;

        public void Init()
        {
            input ??= fpc ? fpc.input : new DeviceInput();
        }

        void Awake() => Init();
        void Update() { if (Application.isPlaying) Tick(Time.deltaTime); }
        void FixedUpdate() { if (Application.isPlaying) PhysicsStep(Time.fixedDeltaTime); }

        int Solid => (1 << propsLayer) | (1 << archLayer);

        // ------------------------------------------------------------------ per frame

        public void Tick(float dt)
        {
            syncTimer -= dt;
            if (syncTimer <= 0 && onObjectMoved != null)
            {
                syncTimer = 0.1f;
                if (Held) onObjectMoved(Held);
                if (Pushing) onObjectMoved(Pushing);
                var done = doneBuf; done.Clear();
                foreach (var kv in settling) { if (!kv.Key || Time.time > kv.Value) done.Add(kv.Key); else onObjectMoved(kv.Key); }
                foreach (var d in done) { if (d) onObjectMoved(d); settling.Remove(d); }
            }
            Aimed = null;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (Held == null && Pushing == null) Aimed = AimTarget(ray);

            if (Held != null)
            {
                if (input.RotateHeld) { var l = input.Look; holdRot = Quaternion.Euler(0, l.x * 2f, 0) * holdRot; }
                fpc.LookLocked = input.RotateHeld;
                UpdateGhost();
                if (input.GrabPressed) TryPlace();
                else if (input.ThrowPressed) Throw();
            }
            else if (Pushing != null)
            {
                if (input.GrabPressed || Vector3.Distance(Pushing.transform.position, fpc.transform.position) > 2.4f) StopPush();
            }
            else if (input.GrabPressed && Aimed != null) Grab(Aimed);
        }

        /// <summary>
        /// Aim assist for hands: a thin sphere-cast collects everything near the crosshair; a small object lying within
        /// 25 cm behind the first hit wins over the big furniture it stands on (you meant the cup, not the desk).
        /// Blocked by architecture: nothing behind a wall is reachable.
        /// </summary>
        PlacedObject AimTarget(Ray ray)
        {
            float wallDist = reach;
            if (Physics.Raycast(ray, out var wh, reach, 1 << archLayer, QueryTriggerInteraction.Ignore)) wallDist = Mathf.Min(reach, wh.distance + (wh.normal.y > 0.7f ? 0.3f : 0f));   // floors: a dropped thing may sink a bit; walls: never through
            var hits = Physics.SphereCastAll(ray, 0.045f, wallDist, 1 << propsLayer, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return null;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var first = hits[0].collider.GetComponentInParent<PlacedObject>();
            PlacedObject best = first; float bestScore = float.MaxValue;
            foreach (var h in hits)
            {
                var po = h.collider.GetComponentInParent<PlacedObject>();
                if (po == null || h.distance > hits[0].distance + 0.25f) continue;
                var c = po.transform.position + Vector3.up * po.profile.size.y * 0.5f;
                float off = Vector3.Cross(ray.direction, c - ray.origin).magnitude; // distance of its centre from the aim line
                float score = off + (po.profile.massClass == MassClass.Heavy ? 0.6f : po.profile.massClass == MassClass.Medium ? 0.15f : 0f);
                if (score < bestScore) { bestScore = score; best = po; }
            }
            return best;
        }

        // ------------------------------------------------------------------ physics step

        public void PhysicsStep(float dt)
        {
            if (Held != null && rb != null)
            {
                var target = HoldPoint();
                var delta = target - rb.worldCenterOfMass;
                var v = Vector3.ClampMagnitude(delta / dt * 0.5f, 9f) + fpc.Velocity * 0.5f;
                rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, v, 0.7f);
                var wantRot = Quaternion.Euler(0, fpc.transform.eulerAngles.y, 0) * holdRot;
                var dq = wantRot * Quaternion.Inverse(rb.rotation);
                dq.ToAngleAxis(out float ang, out var axis);
                if (ang > 180) ang -= 360;
                if (!float.IsNaN(axis.x) && Mathf.Abs(ang) > 0.01f) rb.angularVelocity = Vector3.ClampMagnitude(axis * (ang * Mathf.Deg2Rad / dt * 0.35f), 12f);
                // blocked by the world: drop it instead of tunnelling or dragging the player
                strain = delta.magnitude > 0.8f ? strain + dt : Mathf.Max(0, strain - dt);
                if (strain > 0.6f) { LastEvent = "dropped (blocked)"; Release(Vector3.zero); }
            }
            if (Pushing != null && rb != null)
            {
                var pv = fpc.WishVelocity; pv.y = 0;
                var toObj = Pushing.transform.position - fpc.transform.position; toObj.y = 0;
                // push when walking into it, pull when walking away (hands on it), sideways moves are ignored
                var dir = toObj.normalized;
                float along = Vector3.Dot(pv, dir);
                // heavy things are heavy: a 15 kg cart rolls, a 90 kg cabinet barely creeps (and takes effort to start)
                float m = Mathf.Max(1f, Pushing.profile.mass);
                float strength = Mathf.Clamp(18f / m, 0.12f, 0.75f);
                var want = dir * along * strength;
                // kinematic slide with a sweep: heavy furniture moves exactly as far as the space in front allows
                pushVel = Vector3.MoveTowards(pushVel, want, Mathf.Lerp(0.4f, 2.5f, strength) * dt);
                var step = pushVel * dt;
                if (step.sqrMagnitude > 1e-8f)
                {
                    bool blocked = false;
                    foreach (var h in rb.SweepTestAll(step.normalized, step.magnitude + 0.004f, QueryTriggerInteraction.Ignore))
                    {
                        if (h.normal.y > 0.7f) continue;                     // the floor it stands on
                        var other = h.collider.GetComponentInParent<PlacedObject>();
                        if (other != null && other.supportParent == Pushing) continue; // its own riders
                        if (h.collider.gameObject.layer == fpc.gameObject.layer) continue;
                        blocked = true; break;
                    }
                    if (blocked) pushVel = Vector3.zero; else rb.MovePosition(rb.position + step);
                }
                DebugPush = $"along={along:F2} vel={pushVel}";
            }
        }

        /// <summary>
        /// Where the held object should be: in front of the chest, following the view but with the pitch clamped, so
        /// looking at your feet never pulls it into your own body and looking up never lifts it over your head.
        /// </summary>
        public Vector3 HoldPoint()
        {
            float yaw = fpc.transform.eulerAngles.y, pitch = Mathf.Clamp(fpc.Pitch, -45f, 28f);
            var dir = Quaternion.Euler(pitch, yaw, 0) * Vector3.forward;
            var p = cam.transform.position + dir * holdDist - Vector3.up * 0.2f;
            float radius = Held ? HeldRadius : 0.15f;
            var flat = p - fpc.transform.position; flat.y = 0;
            float minH = 0.42f + radius;                       // never inside the capsule / body
            var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            if (Vector3.Dot(flat, fwd) < minH) p += fwd * (minH - Vector3.Dot(flat, fwd));
            p.y = Mathf.Max(p.y, fpc.transform.position.y + 0.35f + radius * 0.5f);
            return p;
        }
        float HeldRadius;

        // ------------------------------------------------------------------ actions

        void Grab(PlacedObject po)
        {
            var p = po.profile;
            if (p.massClass == MassClass.Fixed || p.support == SupportType.Wall || p.support == SupportType.Ceiling) { LastEvent = "fixed: " + po.name; return; }
            rb = MakePhysical(po);
            WakeSupported(po);
            if (p.massClass == MassClass.Heavy)
            {
                Pushing = po;
                rb.isKinematic = true; pushVel = Vector3.zero;
                fpc.speedMultiplier = Mathf.Clamp(14f / Mathf.Max(1f, p.mass), 0.12f, 0.5f);   // you lean into it, you don't walk
                LastEvent = "pushing " + po.name;
                return;
            }
            Held = po;
            rb.useGravity = false;
            rb.linearDamping = 4f; rb.angularDamping = 6f;
            holdDist = p.massClass == MassClass.Medium ? 0.95f : 0.75f;
            { var rs = po.GetComponentsInChildren<Renderer>(); var b = rs.Length > 0 ? rs[0].bounds : new Bounds(po.transform.position, Vector3.one * 0.2f); foreach (var r in rs) b.Encapsulate(r.bounds); HeldRadius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), 0.05f, 0.45f); }
            po.intentionalAnomaly = false;   // a floating thing you took in your hands is just a thing now
            foreach (var m in po.GetComponentsInChildren<MonoBehaviour>()) if (m && m.GetType().Name == "AnomalyMotion") m.enabled = false;
            holdRot = Quaternion.Inverse(Quaternion.Euler(0, fpc.transform.eulerAngles.y, 0)) * po.transform.rotation;
            fpc.speedMultiplier = p.massClass == MassClass.Medium ? 0.75f : 1f;
            savedLayers.Clear();
            foreach (var c in po.GetComponentsInChildren<Collider>()) { savedLayers[c] = c.gameObject.layer; c.gameObject.layer = heldLayer; }
            po.supportParent = null;
            LastEvent = "picked " + po.name;
        }

        Rigidbody MakePhysical(PlacedObject po)
        {
            var r = po.GetComponent<Rigidbody>();
            if (r == null) r = po.gameObject.AddComponent<Rigidbody>();
            r.isKinematic = false; r.useGravity = true;
            r.mass = Mathf.Max(0.05f, po.profile.mass);
            r.centerOfMass = po.profile.centerOfMass;
            r.interpolation = RigidbodyInterpolation.Interpolate;
            r.collisionDetectionMode = po.profile.massClass == MassClass.Small ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;
            r.linearDamping = 0.05f; r.angularDamping = 0.1f;
            return r;
        }

        /// <summary>Objects standing on a moved object stop being static: they ride along on friction or fall off.</summary>
        void WakeSupported(PlacedObject po)
        {
            if (engine == null) return;
            foreach (var other in engine.placed)
                if (other && other.supportParent == po && other.GetComponent<Rigidbody>() == null) { MakePhysical(other); WakeSupported(other); }
        }

        void Release(Vector3 impulse)
        {
            if (Held == null) return;
            foreach (var kv in savedLayers) if (kv.Key) kv.Key.gameObject.layer = kv.Value;
            savedLayers.Clear();
            rb.useGravity = true; rb.linearDamping = 0.05f; rb.angularDamping = 0.1f;
            if (impulse != Vector3.zero) rb.AddForce(impulse, ForceMode.Impulse);
            if (Held) settling[Held] = Time.time + 2.5f;   // keep reporting while it falls/slides
            Held = null; rb = null;
            fpc.speedMultiplier = 1f; fpc.LookLocked = false;
            HideGhost();
        }

        /// <summary>Put the held thing into a pocket: it disappears from the world until taken out.</summary>
        public PlacedObject PocketHeld()
        {
            if (Held == null || Held.profile.massClass != MassClass.Small) return null;
            var po = Held; var body = rb;
            Release(Vector3.zero);
            if (body) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            settling.Remove(po);
            po.gameObject.SetActive(false);
            LastEvent = "pocketed " + po.name;
            return po;
        }

        /// <summary>Take something out of a pocket straight into the hands.</summary>
        public void TakeOut(PlacedObject po)
        {
            if (!po || Held != null || Pushing != null) return;
            po.gameObject.SetActive(true);
            po.transform.SetPositionAndRotation(cam.transform.position + cam.transform.forward * 0.6f - Vector3.up * 0.2f, Quaternion.Euler(0, fpc.transform.eulerAngles.y, 0));
            Physics.SyncTransforms();
            Grab(po);
        }

        void Throw()
        {
            float m = Mathf.Max(0.2f, Held.profile.mass);
            var name = Held.name;
            Release(cam.transform.forward * Mathf.Clamp(5.5f * Mathf.Sqrt(m), 0.5f, 14f));
            LastEvent = "threw " + name;
        }

        void StopPush()
        {
            if (rb) { rb.isKinematic = false; rb.linearVelocity = Vector3.zero; }
            LastEvent = "stopped pushing " + Pushing.name;
            settling[Pushing] = Time.time + 1.5f;
            Pushing = null; rb = null; fpc.speedMultiplier = 1f;
        }

        // ------------------------------------------------------------------ gentle placement

        void UpdateGhost()
        {
            ghostValid = ComputePlacement(out ghostPose, out ghostSupport, out _);
            if (!ghostOk) return;
            if (ghost == null)
            {
                ghost = Instantiate(Held.profile.prefab);
                foreach (var c in ghost.GetComponentsInChildren<Collider>()) Destroy(c);
                foreach (var po in ghost.GetComponentsInChildren<PlacedObject>()) Destroy(po);
            }
            ghost.transform.SetPositionAndRotation(ghostPose.position, ghostPose.rotation);
            var m = ghostValid ? ghostOk : ghostBad;
            foreach (var r in ghost.GetComponentsInChildren<Renderer>()) { var arr = r.sharedMaterials; for (int i = 0; i < arr.Length; i++) arr[i] = m; r.sharedMaterials = arr; }
        }

        void HideGhost() { if (ghost) { if (Application.isPlaying) Destroy(ghost); else DestroyImmediate(ghost); } ghost = null; }

        /// <summary>Where would the held object rest if put down where the player looks? Uses the generator's checks.</summary>
        public bool ComputePlacement(out Pose pose, out PlacedObject support, out Check fail)
        {
            pose = default; support = null; fail = Check.Surface;
            if (Held == null || engine == null) return false;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out var hit, reach + 0.6f, Solid, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.85f) return false;
            support = hit.collider.GetComponentInParent<PlacedObject>();
            var rot = Quaternion.Euler(0, (Quaternion.Euler(0, fpc.transform.eulerAngles.y, 0) * holdRot).eulerAngles.y, 0);
            pose = new Pose(hit.point, rot, 1f);
            fail = engine.PlayerPlaceCheck(Held.profile, ref pose);
            return fail == Check.None;
        }

        void TryPlace()
        {
            if (!ComputePlacement(out var pose, out var support, out var fail))
            {
                // no valid spot under the crosshair: just let go (it drops where it is), never "nothing happens"
                var n = Held.name; Release(fpc.Velocity * 0.5f * Mathf.Max(0.2f, Held ? Held.profile.mass : 1f)); LastEvent = $"dropped {n} ({fail})"; return;
            }
            var po = Held; var body = rb;
            Release(Vector3.zero);
            body.position = pose.position; body.rotation = pose.rotation;
            po.transform.SetPositionAndRotation(pose.position, pose.rotation);
            body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            po.supportParent = support;
            Physics.SyncTransforms();
            LastEvent = $"placed {po.name} on {(support ? support.name : "floor")}";
        }
    }
}
