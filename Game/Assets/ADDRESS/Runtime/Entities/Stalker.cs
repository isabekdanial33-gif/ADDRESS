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
        public static float GlobalSpeedMul = 1f;     // the Mountain King's music speeds everything up
        public float testSpeed = -1;                 // test tour: walk in place at this speed (animation, limp, flight) without moving
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
        // the Shy one sits and weeps; whoever looks at it is hunted down, wherever they run
        public bool shy; public bool Enraged { get; private set; } float rageT;
        static AudioClip[] allSfx;
        static AudioClip[] Sfx(params string[] prefixes)
        {
            allSfx ??= Resources.LoadAll<AudioClip>("EntitySfx");
            return System.Array.FindAll(allSfx, c => System.Array.Exists(prefixes, p => c.name.StartsWith(p)));
        }

        // ------------------------------------------------------------------ creatures from mods
        public string modId; Animation legacy; string clipIdle, clipWalk, clipRun;
        public string IndexKey => modId ?? kind.ToString();

        /// <summary>A mod creature: speeds and voice from its JSON, its own glTF animations (legacy Animation component).</summary>
        public void ConfigureMod(Address.Mods.EntityDef e)
        {
            modId = e.id; kind = EntityKind.Echo; anim = null; body = null;
            wanderSpeed = e.walkSpeed; stalkSpeed = e.chaseSpeed * (1f + 0.06f * generation); noticeRange = e.noticeRange; catchRadius = e.catchRadius; freezeWhenSeen = e.freezeWhenSeen;
            vocals = e.clips != null && e.clips.Length > 0 ? e.clips : Sfx("growl_", "eerie_");
            legacy = GetComponentInChildren<Animation>();
            if (legacy)
            {
                string Find(string want, string fallback)
                {
                    string first = null;
                    foreach (AnimationState s in legacy) { first ??= s.name; if (!string.IsNullOrEmpty(want) && s.name.ToLowerInvariant().Contains(want.ToLowerInvariant())) return s.name; }
                    return fallback ?? first;
                }
                clipIdle = Find(e.idle, null); clipWalk = Find(e.walk, clipIdle); clipRun = Find(e.run, clipWalk);
                foreach (AnimationState s in legacy) s.wrapMode = WrapMode.Loop;
            }
            sting = Resources.Load<AudioClip>("EntitySfx/deep_1");
            var s1 = gameObject.AddComponent<AudioSource>(); s1.spatialBlend = 1; s1.rolloffMode = AudioRolloffMode.Linear; s1.minDistance = 1.5f; s1.maxDistance = 26f; s1.dopplerLevel = 0; voice = s1;
        }

        void LegacyAnim()
        {
            if (!legacy) return;
            var want = Speed < 0.08f ? clipIdle : Speed < 1.8f ? clipWalk : clipRun;
            if (want != null && !legacy.IsPlaying(want)) legacy.CrossFade(want, 0.25f);
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
                case EntityKind.SkinStealer: wanderSpeed = 0.8f; stalkSpeed = 2.9f; freezeWhenSeen = false; catchRadius = 0.95f; noticeRange = 15f; vocals = Sfx("beast_", "demon_"); break;
                case EntityKind.Clark: wanderSpeed = 0.5f; stalkSpeed = 1.6f; freezeWhenSeen = true; noticeRange = 20f; vocals = Sfx("eerie_", "spirit_"); break;
                case EntityKind.Growler: wanderSpeed = 0.9f; stalkSpeed = 3.2f; freezeWhenSeen = false; noticeRange = 14f; vocals = Sfx("growl_"); stepSet = "step_hard_"; break;
                case EntityKind.Howler: wanderSpeed = 0.8f; stalkSpeed = 2.6f; freezeWhenSeen = false; noticeRange = 15f; vocals = Sfx("growl_", "demon_"); break;
                case EntityKind.Lifeform: wanderSpeed = 0.9f; stalkSpeed = 3.0f; freezeWhenSeen = false; noticeRange = 15f; vocals = Sfx("beast_"); break;
                case EntityKind.Needlelimbs: wanderSpeed = 0.6f; stalkSpeed = 2.2f; freezeWhenSeen = true; noticeRange = 18f; vocals = Sfx("eerie_", "deep_"); stepSet = "none"; break;
                case EntityKind.Hazmat: wanderSpeed = 0.8f; stalkSpeed = 2.5f; freezeWhenSeen = false; noticeRange = 16f; vocals = Sfx("spirit_", "demon_"); stepSet = "step_hard_"; break;
                case EntityKind.Hound: wanderSpeed = 1.0f; stalkSpeed = 3.7f; freezeWhenSeen = false; noticeRange = 18f; catchRadius = 1.0f; vocals = Sfx("growl_"); break;
                case EntityKind.Rake: wanderSpeed = 0.7f; stalkSpeed = 3.3f; freezeWhenSeen = false; noticeRange = 16f; catchRadius = 0.95f; vocals = Sfx("demon_", "beast_"); break;
                case EntityKind.Librarian: wanderSpeed = 0f; stalkSpeed = 3.2f; freezeWhenSeen = true; noticeRange = 24f; vocals = Sfx("deep_", "eerie_"); stepSet = "none"; break;
                case EntityKind.Deathmoth: wanderSpeed = 1.2f; stalkSpeed = 3.0f; freezeWhenSeen = false; noticeRange = 18f; catchRadius = 1.1f; vocals = Sfx("spirit_"); stepSet = "none"; break;
                case EntityKind.Spider: wanderSpeed = 1.1f; stalkSpeed = 3.4f; freezeWhenSeen = false; noticeRange = 14f; catchRadius = 1.3f; vocals = Sfx("beast_", "eerie_"); stepSet = "step_hard_"; break;
                case EntityKind.Thinman: wanderSpeed = 0.55f; stalkSpeed = 2.4f; freezeWhenSeen = true; noticeRange = 26f; catchRadius = 1.0f; vocals = Sfx("deep_", "eerie_"); break;
                case EntityKind.Grinner: wanderSpeed = 1.0f; stalkSpeed = 3.1f; freezeWhenSeen = false; noticeRange = 15f; catchRadius = 0.95f; vocals = Sfx("spirit_", "beast_"); break;
                case EntityKind.Shy: wanderSpeed = 0f; stalkSpeed = 3.7f; freezeWhenSeen = false; noticeRange = 999f; catchRadius = 1.0f; shy = true; vocals = Sfx("spirit_", "eerie_"); stepSet = "step_hard_"; break;
                case EntityKind.Ember: wanderSpeed = 1.0f; stalkSpeed = 3.5f; freezeWhenSeen = false; noticeRange = 16f; catchRadius = 1.0f; vocals = Sfx("growl_", "demon_"); stepSet = "step_hard_"; break;
            }
            if (k == EntityKind.Clark && anim && anim.isHuman)
            {
                // the captain limps on his right leg (no limp clip in the library: the walk is bent on top of the animation)
                rKnee = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg); rThigh = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                lFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot); rFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                hips = anim.GetBoneTransform(HumanBodyBones.Hips); spine = anim.GetBoneTransform(HumanBodyBones.Spine);
                if (rKnee) kneeRest = rKnee.localRotation;
            }
            if (k == EntityKind.Deathmoth) flyer = transform.Find("Body");
            stalkSpeed *= 1f + 0.06f * generation;
            sting = Resources.Load<AudioClip>("EntitySfx/deep_1");
            AudioSource Src(float max) { var s = gameObject.AddComponent<AudioSource>(); s.spatialBlend = 1; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 1.5f; s.maxDistance = max; s.dopplerLevel = 0; return s; }
            voice = Src(26f);
            var sc = Sfx(stepSet);
            if (sc.Length > 0) { steps = Src(16f); steps.clip = sc[Random.Range(0, sc.Length)]; steps.loop = true; steps.volume = 0; steps.Play(); }
        }

        // Clark's limp
        Transform rKnee, rThigh, lFoot, rFoot, hips, spine; Quaternion kneeRest; float limpLoad;
        Transform flyer;
        /// <summary>Clark slows down while his weight is on the bad leg and hurries the good step.</summary>
        float LimpMul => rKnee ? Mathf.Lerp(1.2f, 0.45f, Mathf.Clamp01(limpLoad)) : 1f;

        void Limp()
        {
            if (!rKnee || !lFoot || !rFoot || !hips) return;
            float w = Mathf.Clamp01(Speed / 0.6f);
            // which leg carries the body: the lower foot
            float load = Mathf.Clamp((lFoot.position.y - rFoot.position.y) * 12f, -1f, 1f);
            limpLoad = Mathf.Lerp(limpLoad, Mathf.Max(0, load), 0.5f);
            if (w <= 0.01f) return;
            // a stiff right knee that hardly bends
            rKnee.localRotation = Quaternion.Slerp(rKnee.localRotation, kneeRest, 0.8f * w);
            // the stiff leg swings out to the side instead of bending
            if (rThigh && load < 0) rThigh.rotation = Quaternion.AngleAxis(-10f * -load * w, transform.forward) * rThigh.rotation;
            // the body sinks and leans over the bad leg each time it has to carry him
            hips.position += Vector3.down * 0.075f * limpLoad * w;
            if (spine) spine.rotation = Quaternion.AngleAxis(-(6f + 10f * limpLoad) * w, transform.forward) * spine.rotation;
        }

        void Voices(float dt)
        {
            LegacyAnim();
            if (anim)
            {
                anim.SetFloat("Speed", Speed, 0.12f, dt);
                // looked at: it stops walking but keeps breathing and twitching (idle); only a mannequin is a real statue
                anim.speed = mode == Mode.Frozen && kind == EntityKind.Mannequin ? 0f : 1f;
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
            if (testSpeed >= 0) { Speed = testSpeed; Voices(dt); return; }
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
            if (shy)
            {
                if (!Enraged)
                {
                    Speed = 0; mode = Mode.Wander;
                    if (SeenNow && los && bestD < 30f)
                    {
                        Enraged = true; rageT = 3.2f; mode = Mode.Stalk;
                        if (voice) { if (sting) voice.PlayOneShot(sting, 1f); var sc = Resources.Load<AudioClip>("EntitySfx/scare_3"); if (sc) voice.PlayOneShot(sc, 0.8f); }
                    }
                    return;
                }
                if (rageT > 0)
                {
                    // the scream before the run: it stands up and turns to you
                    rageT -= dt; Speed = 0; mode = Mode.Stalk;
                    var to0 = Flat(best.position - transform.position);
                    if (to0.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to0), 180f * dt);
                    return;
                }
                mode = Mode.Stalk; lostTimer = 0;
                if (bestD < catchRadius) { onCatch?.Invoke(best); enabled = false; return; }
                MoveAlongField(best.position, stalkSpeed * Mathf.Lerp(0.85f, 1.3f, Mathf.InverseLerp(3f, 20f, bestD)), dt);
                return;
            }
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
                MoveAlongField(best.position, sp * LimpMul, dt);
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
            MoveAlongField(wanderGoal, wanderSpeed * LimpMul, dt);
        }

        // ------------------------------------------------------------------ navigation

        void MoveAlongField(Vector3 goal, float speed, float dt)
        {
            speed *= GlobalSpeedMul;
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
                // never aim across a wall corner: the body would cut through it
                var cand = nav.Center(next.x, next.y, floorY);
                if (step > 0 && Physics.Linecast(transform.position + Vector3.up * 0.6f, cand + Vector3.up * 0.6f, 1 << LevelBuilder.ArchLayer, QueryTriggerInteraction.Ignore)) break;
                if (step > 0 && Physics.Linecast(transform.position + Vector3.up * 1.4f, cand + Vector3.up * 1.4f, 1 << LevelBuilder.ArchLayer, QueryTriggerInteraction.Ignore)) break;
                cur = next; curD = nd; aim = cand;
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
                drone.pitch = Enraged ? 1.35f : SeenNow ? 0.82f : 1f + 0.04f * generation;
            }
        }

        void LateUpdate()
        {
            Limp();
            // the moth flies: up near the ceiling while wandering, down to head height when it hunts
            if (flyer)
            {
                float h = (mode == Mode.Stalk ? 1.25f : 1.9f) + Mathf.Sin(Time.time * 2.3f) * 0.18f;
                flyer.localPosition = new Vector3(0, Mathf.Lerp(flyer.localPosition.y, h, 1 - Mathf.Exp(-2f * Time.deltaTime)), 0);
            }
            // wrong posture: the head hangs to one side, more with every Echo; snaps upright when looked at
            if (head)
            {
                float tilt = (SeenNow ? 8f : 28f) + generation * 6f;
                if (kind == EntityKind.Mannequin || shy) return;   // a mannequin never moves its head on its own
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
