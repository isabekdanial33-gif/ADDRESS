using System.Collections.Generic;
using Address.Placement;
using Address.World;
using UnityEngine;

namespace Address.Entities
{
    /// <summary>
    /// "Отголосок" (the Echo-walker): a stretched, darkened copy of an explorer. It wanders the place; once it has
    /// noticed someone it follows - but only while nobody is looking at it. Looked at, it freezes mid-step. Lights
    /// near it fail, a low drone grows. If it reaches a player, that player wakes up in the next Echo of the place.
    ///
    /// Movement uses a BFS distance field over the level NavGrid (one field per target refresh, shared by all
    /// entities), so it never walks through walls or furniture.
    /// </summary>
    [DefaultExecutionOrder(200)]   // after ProceduralBody (head tilt is applied on top of the gait)
    public class Stalker : MonoBehaviour
    {
        public enum Mode { Dormant, Wander, Stalk, Frozen }
        public Mode mode = Mode.Dormant;
        public float wanderSpeed = 0.75f, stalkSpeed = 2.0f, catchRadius = 0.85f, noticeRange = 15f;
        public float wakeDelay = 18f;                // grace period after entering a place
        public int generation;
        public System.Func<List<Transform>> targets; // player heads (local camera + remote avatars)
        public System.Func<Transform, bool> isLookedAtBy; // does this viewer currently see the stalker?
        public System.Action<Transform> onCatch;
        public bool puppet;                          // co-op client: pose comes from the host
        public Vector3 netPos; public float netYaw; public bool hasNet;

        NavGrid nav; float floorY; int archPropsMask;
        int[] field; Vector2Int fieldTarget = new(-999, -999); float fieldTimer;
        bool[] roomy;
        Vector3 wanderGoal; float wanderTimer, lostTimer, seenTimer, stingCooldown;
        Transform head; Quaternion head0; AudioSource drone;
        readonly List<(Light l, float i0)> lights = new();
        float flickerT; Transform currentTarget;
        public bool SeenNow { get; private set; }
        public float Speed { get; private set; }

        public void Init(LevelBuilder.Result level, int gen, AudioClip droneClip)
        {
            nav = level.nav; floorY = level.start.y; generation = gen;
            if (Physics.Raycast(transform.position + Vector3.up * 2f, Vector3.down, out var fh, 5f, 1 << LevelBuilder.ArchLayer, QueryTriggerInteraction.Ignore)) floorY = fh.point.y;
            transform.position = new Vector3(transform.position.x, floorY, transform.position.z);
            archPropsMask = (1 << LevelBuilder.ArchLayer) | (1 << LevelBuilder.PropsLayer);
            roomy = new bool[nav.w * nav.h];
            for (int z = 0; z < nav.h; z++) for (int x = 0; x < nav.w; x++) roomy[x + z * nav.w] = nav.Roomy(x, z);
            field = new int[nav.w * nav.h];
            foreach (var l in level.root.GetComponentsInChildren<Light>(true)) if (l.type != LightType.Directional) lights.Add((l, l.intensity));
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == "Head") { head = t; head0 = t.localRotation; }
            // Echo distortion: every copy is a little faster and a little more wrong
            stalkSpeed *= 1f + 0.06f * gen; noticeRange += gen * 1.5f;
            if (droneClip)
            {
                drone = gameObject.AddComponent<AudioSource>(); drone.clip = droneClip; drone.loop = true; drone.spatialBlend = 1; drone.rolloffMode = AudioRolloffMode.Linear;
                drone.minDistance = 1.5f; drone.maxDistance = 22f; drone.volume = 0; drone.dopplerLevel = 0; drone.Play();
            }
            wanderGoal = transform.position;
            body = GetComponentInChildren<Address.Player.ProceduralBody>();
        }
        Address.Player.ProceduralBody body;
        Animator anim; AudioSource voice, steps; AudioClip[] vocals; AudioClip sting; float voiceT = 4f, stingCd;
        public EntityKind kind; public bool freezeWhenSeen = true, passive;
        static AudioClip[] allSfx;
        static AudioClip[] Sfx(params string[] prefixes)
        {
            allSfx ??= Resources.LoadAll<AudioClip>("EntitySfx");
            return System.Array.FindAll(allSfx, c => System.Array.Exists(prefixes, p => c.name.StartsWith(p)));
        }

        /// <summary>Per-kind behaviour, animation and voice.</summary>
        public void Configure(EntityKind k)
        {
            kind = k; anim = GetComponentInChildren<Animator>(); body = null;
            string stepSet = "step_carpet_";
            switch (k)
            {
                case EntityKind.Echo: wanderSpeed = 0.75f; stalkSpeed = 2.0f; freezeWhenSeen = true; vocals = Sfx("growl_"); break;
                case EntityKind.Salesman: wanderSpeed = 0.6f; stalkSpeed = 2.7f; freezeWhenSeen = true; noticeRange = 18f; vocals = Sfx("demon_", "eerie_"); break;
                case EntityKind.Crawler: wanderSpeed = 0.9f; stalkSpeed = 2.3f; freezeWhenSeen = false; catchRadius = 0.95f; vocals = Sfx("beast_"); break;
                case EntityKind.Mannequin: wanderSpeed = 0f; stalkSpeed = 3.6f; freezeWhenSeen = true; noticeRange = 22f; vocals = Sfx("spirit_"); stepSet = "step_hard_"; break;
                case EntityKind.Limper: wanderSpeed = 0.45f; stalkSpeed = 1.25f; freezeWhenSeen = false; vocals = Sfx("beast_", "growl_"); break;
                case EntityKind.Smiler: wanderSpeed = 0.35f; stalkSpeed = 1.9f; freezeWhenSeen = true; noticeRange = 12f; vocals = Sfx("eerie_", "deep_"); stepSet = "none"; break;
                case EntityKind.Faceling: wanderSpeed = 0.8f; stalkSpeed = 0.8f; passive = true; vocals = Sfx("spirit_2"); break;
                case EntityKind.Partygoer: wanderSpeed = 0.7f; stalkSpeed = 3.0f; freezeWhenSeen = false; noticeRange = 16f; vocals = Sfx("spirit_1", "spirit_2"); break;
            }
            stalkSpeed *= 1f + 0.06f * generation;
            sting = Resources.Load<AudioClip>("EntitySfx/deep_1");
            AudioSource Src(float max) { var s = gameObject.AddComponent<AudioSource>(); s.spatialBlend = 1; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 1.5f; s.maxDistance = max; s.dopplerLevel = 0; return s; }
            voice = Src(26f);
            var sc = Sfx(stepSet);
            if (sc.Length > 0) { steps = Src(16f); steps.clip = sc[Random.Range(0, sc.Length)]; steps.loop = true; steps.volume = 0; steps.Play(); }
        }

        void Voices(float dt)
        {
            if (anim)
            {
                anim.SetFloat("Speed", Speed, 0.12f, dt);
                anim.speed = mode == Mode.Frozen ? 0f : 1f;   // caught mid-step when looked at
            }
            if (steps)
            {
                float v = Mathf.Clamp01(Speed / 2f) * (kind == EntityKind.Mannequin ? 1f : 0.7f);
                steps.volume = Mathf.MoveTowards(steps.volume, mode == Mode.Frozen ? 0 : v, dt * 4f);
                steps.pitch = Mathf.Clamp(0.75f + Speed * 0.2f, 0.7f, 1.5f);
            }
            stingCd -= dt;
            if (voice && vocals != null && vocals.Length > 0 && mode != Mode.Dormant && mode != Mode.Frozen)
            {
                voiceT -= dt;
                if (voiceT <= 0)
                {
                    voiceT = mode == Mode.Stalk ? Random.Range(3.5f, 7f) : Random.Range(8f, 16f);
                    voice.pitch = Random.Range(0.85f, 1.05f) - generation * 0.04f;
                    voice.PlayOneShot(vocals[Random.Range(0, vocals.Length)], mode == Mode.Stalk ? 1f : 0.6f);
                }
            }
        }

        void Update()
        {
            float dt = Time.deltaTime; if (dt <= 0 || nav == null) return;
            UpdateAtmosphere(dt);
            Voices(dt);
            if (body) body.freeze = mode == Mode.Frozen;
            if (puppet)
            {
                if (hasNet)
                {
                    Speed = Flat(netPos - transform.position).magnitude * 10f;
                    transform.position = Vector3.Lerp(transform.position, netPos, 1 - Mathf.Exp(-10f * dt));
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, netYaw, 0), 1 - Mathf.Exp(-10f * dt));
                }
                return;
            }

            wakeDelay -= dt;
            if (wakeDelay > 0) { mode = Mode.Dormant; Speed = 0; return; }

            // who is closest, does anyone see me?
            var ts = targets?.Invoke(); Transform best = null; float bestD = float.MaxValue;
            SeenNow = false;
            if (ts != null)
                foreach (var t in ts)
                {
                    if (!t) continue;
                    float d = Flat(t.position - transform.position).magnitude;
                    if (d < bestD) { bestD = d; best = t; }
                    if (isLookedAtBy != null && isLookedAtBy(t)) SeenNow = true;
                }
            currentTarget = best;
            if (best == null) { mode = Mode.Wander; Wander(dt); return; }

            if (passive) { mode = Mode.Wander; Wander(dt); return; }   // facelings just wander about their business
            bool los = HasLineOfSight(best.position);
            if (mode == Mode.Dormant || mode == Mode.Wander)
            {
                if ((los && bestD < noticeRange) || bestD < 4f)
                {
                    mode = Mode.Stalk; lostTimer = 0;
                    if (stingCd <= 0 && voice) { stingCd = 25f; if (sting) voice.PlayOneShot(sting, 1f); if (vocals != null && vocals.Length > 0) voice.PlayOneShot(vocals[Random.Range(0, vocals.Length)], 1f); }
                }
            }
            if (mode == Mode.Stalk || mode == Mode.Frozen)
            {
                lostTimer = los ? 0 : lostTimer + dt;
                if (lostTimer > 25f) { mode = Mode.Wander; wanderTimer = 0; }
            }

            if (mode == Mode.Stalk || mode == Mode.Frozen)
            {
                if (SeenNow && freezeWhenSeen) { mode = Mode.Frozen; Speed = 0; seenTimer += dt; return; }
                mode = Mode.Stalk; seenTimer = 0;
                if (bestD < catchRadius) { onCatch?.Invoke(best); enabled = false; return; }
                // close in: faster when far, careful when near
                float sp = stalkSpeed * Mathf.Lerp(0.75f, 1.25f, Mathf.InverseLerp(3f, 14f, bestD));
                if (SeenNow) sp *= 0.6f;   // the ones that do not freeze still slow down under your gaze
                MoveAlongField(best.position, sp, dt);
            }
            else Wander(dt);
        }

        void Wander(float dt)
        {
            wanderTimer -= dt;
            if (wanderTimer <= 0 || Flat(wanderGoal - transform.position).sqrMagnitude < 0.5f)
            {
                wanderTimer = Random.Range(8f, 16f);
                for (int i = 0; i < 30; i++)
                {
                    var c = nav.CellOf(transform.position) + new Vector2Int(Random.Range(-40, 41), Random.Range(-40, 41));
                    if (nav.InGrid(c.x, c.y) && roomy[c.x + c.y * nav.w]) { wanderGoal = nav.Center(c.x, c.y, floorY); break; }
                }
            }
            MoveAlongField(wanderGoal, wanderSpeed, dt);
        }

        // ------------------------------------------------------------------ navigation

        void MoveAlongField(Vector3 goal, float speed, float dt)
        {
            var gc = nav.CellOf(goal);
            fieldTimer -= dt;
            if ((gc - fieldTarget).sqrMagnitude > 4 && fieldTimer <= 0) { BuildField(gc); fieldTimer = 0.35f; }
            var me = nav.CellOf(transform.position);
            if (!nav.InGrid(me.x, me.y)) { Speed = 0; return; }
            // look a few cells ahead down the gradient (smooths the 4-connected grid path)
            var cur = me; int curD = Dist(cur);
            if (curD < 0) { Snap(); Speed = 0; return; }
            Vector3 aim = goal;
            for (int step = 0; step < 6 && curD > 0; step++)
            {
                Vector2Int next = cur; int nd = curD;
                foreach (var d in Dirs8)
                {
                    var n = cur + d; int dd = Dist(n);
                    if (dd >= 0 && dd < nd) { nd = dd; next = n; }
                }
                if (next == cur) break;
                cur = next; curD = nd; aim = nav.Center(cur.x, cur.y, floorY);
            }
            if (curD == 0) aim = new Vector3(goal.x, floorY, goal.z);
            var to = Flat(aim - transform.position);
            float stepLen = Mathf.Min(speed * dt, to.magnitude);
            Speed = stepLen / dt;
            if (to.sqrMagnitude > 1e-4f)
            {
                var np = transform.position + to.normalized * stepLen; np.y = floorY;
                var nc = nav.CellOf(np);
                bool ok = nav.InGrid(nc.x, nc.y) && (roomy[nc.x + nc.y * nav.w] || Dist(nc) >= 0 || (curD == 0 && nav.Free(nc.x, nc.y)));
                if (ok) transform.position = np;
                var want = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 240f * dt);
            }
        }

        static readonly Vector2Int[] Dirs8 = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1) };
        int Dist(Vector2Int c) => nav.InGrid(c.x, c.y) ? field[c.x + c.y * nav.w] : -1;

        void BuildField(Vector2Int goal)
        {
            fieldTarget = goal;
            for (int i = 0; i < field.Length; i++) field[i] = -1;
            // the goal itself may stand next to furniture: start from the nearest roomy cell
            if (!nav.InGrid(goal.x, goal.y)) return;
            var q = new Queue<Vector2Int>();
            for (int r = 0; r < 8 && q.Count == 0; r++)
                for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
                {
                    var c = goal + new Vector2Int(dx, dz);
                    if (nav.InGrid(c.x, c.y) && roomy[c.x + c.y * nav.w] && field[c.x + c.y * nav.w] < 0) { field[c.x + c.y * nav.w] = 0; q.Enqueue(c); }
                }
            while (q.Count > 0)
            {
                var c = q.Dequeue(); int d = field[c.x + c.y * nav.w] + 1;
                for (int k = 0; k < 4; k++)
                {
                    var n = c + Dirs8[k];
                    if (!nav.InGrid(n.x, n.y)) continue;
                    int i = n.x + n.y * nav.w;
                    if (field[i] >= 0 || !roomy[i]) continue;
                    field[i] = d; q.Enqueue(n);
                }
            }
        }

        void Snap()
        {
            var me = nav.CellOf(transform.position);
            for (int r = 1; r < 12; r++)
                for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
                {
                    var c = me + new Vector2Int(dx, dz);
                    if (Dist(c) >= 0) { transform.position = nav.Center(c.x, c.y, floorY); return; }
                }
        }

        bool HasLineOfSight(Vector3 eye)
        {
            var from = transform.position + Vector3.up * 1.7f;
            var d = eye - from;
            return !Physics.Raycast(from, d.normalized, d.magnitude - 0.3f, archPropsMask, QueryTriggerInteraction.Ignore);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        // ------------------------------------------------------------------ presence: lights, drone, posture

        void UpdateAtmosphere(float dt)
        {
            flickerT += dt;
            bool active = mode == Mode.Stalk || mode == Mode.Frozen;
            float range = active ? 7f : 4f;
            foreach (var (l, i0) in lights)
            {
                if (!l) continue;
                float d = (l.transform.position - transform.position).magnitude;
                if (d > range + 2f) { if (l.intensity != i0) l.intensity = Mathf.MoveTowards(l.intensity, i0, i0 * dt * 2f); continue; }
                float k = Mathf.Clamp01(1 - d / range);
                float n = Mathf.PerlinNoise(flickerT * 9f, d * 3.1f);
                float drop = n > 0.62f ? 0.05f : 1f - k * 0.55f;
                l.intensity = i0 * Mathf.Lerp(1f, drop, k + (active ? 0.25f : 0));
            }
            if (drone)
            {
                float want = mode == Mode.Dormant ? 0.15f : active ? 0.9f : 0.4f;
                drone.volume = Mathf.MoveTowards(drone.volume, want, dt * 0.6f);
                drone.pitch = SeenNow ? 0.82f : 1f + 0.04f * generation;
            }
        }

        void LateUpdate()
        {
            // wrong posture: the head hangs to one side, more with every Echo; snaps upright when looked at
            if (head)
            {
                float tilt = (SeenNow ? 8f : 28f) + generation * 6f;
                if (kind == EntityKind.Mannequin) return;          // a mannequin never moves its head on its own
                if (!anim) head.localRotation = head0;             // animated bodies: tilt on top of the animation
                head.rotation = Quaternion.AngleAxis(tilt, transform.forward) * Quaternion.AngleAxis(SeenNow ? -12f : 6f, transform.right) * head.rotation;
            }
        }

        void OnDestroy()
        {
            foreach (var (l, i0) in lights) if (l) l.intensity = i0;
        }
    }
}
