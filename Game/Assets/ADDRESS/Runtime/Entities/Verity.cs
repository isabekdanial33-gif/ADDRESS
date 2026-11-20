using System.Collections.Generic;
using System.Linq;
using Address.Core;
using Address.Placement;
using Address.World;
using UnityEngine;

namespace Address.Entities
{
    /// <summary>
    /// Verity: the yellow smiley helper (downloaded CC-BY Blockbench model, Resources/Entities/Model_Verity). It drifts
    /// through walls, comes up to the explorer and, when spoken to (E), talks on and on: where the way out is, how that
    /// exit opens, what lives here. Then it flies ahead and leads the way along the real corridors.
    /// The FAKE Verity looks the same, only dimmer. It talks just as cheerfully, but the "exit" it describes is wherever
    /// the monster is, and that is where it leads.
    /// </summary>
    public class Verity : MonoBehaviour
    {
        public bool fake;
        public Transform viewer;                                 // the local camera
        public System.Func<List<Stalker>> monsters;
        public System.Action<string, string, float> say;          // speaker, line, seconds
        public bool Talking => st == St.Talk;
        public bool Busy => st == St.Talk || st == St.Guide;
        public string Key => fake ? "FakeVerity" : "Verity";

        enum St { Roam, Approach, Talk, Guide, Leave }
        St st = St.Roam;
        LevelBuilder.Result level; NavGrid nav; float floorY;
        Transform body; Animator anim; Light glow; AudioSource voice; AudioClip[] blips; AudioClip glitch;
        Vector3 roamGoal; float roamT, bobT, lineT, lineLen, blipT, glitchT, leaveT;
        readonly List<string> lines = new(); int line = -1;
        Vector3 target; int[] field; bool talkedOnce;
        Transform faceClosed, faceOpen, susClosed, susOpen, bored, ball; Vector3 ball0 = Vector3.one; Quaternion ballRot0 = Quaternion.identity; bool mouthOpen; float mouthT;
        void Face(bool open)
        {
            mouthOpen = open;
            foreach (var t in new[] { faceClosed, faceOpen, susClosed, susOpen, bored }) if (t) t.localScale = Vector3.zero;
            var on = open && faceOpen ? faceOpen : faceClosed ? faceClosed : faceOpen;
            if (on) on.localScale = Vector3.one;
        }

        public static Verity Spawn(LevelBuilder.Result level, Vector3 pos, bool fake)
        {
            var model = Resources.Load<GameObject>("Entities/Model_Verity");
            if (!model || level.nav == null) return null;
            var root = new GameObject(fake ? "FakeVerity" : "Verity"); root.transform.SetParent(level.root.transform, false);
            root.transform.position = pos;
            var v = root.AddComponent<Verity>(); v.fake = fake; v.Init(level, model);
            return v;
        }

        void Init(LevelBuilder.Result lv, GameObject model)
        {
            level = lv; nav = lv.nav; floorY = lv.start.y;
            var b = Instantiate(model, transform); b.name = "Body"; body = b.transform;
            body.localPosition = Vector3.zero; body.localRotation = Quaternion.identity;
            foreach (var c in b.GetComponentsInChildren<Collider>(true)) Destroy(c);
            anim = b.GetComponentInChildren<Animator>();
            if (anim) { anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
            // the fake one is dimmer: a darker, greyer yellow and a weak glow
            foreach (var r in b.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var ms = r.materials;
                foreach (var m in ms)
                {
                    if (!m) continue;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", fake ? new Color(0.42f, 0.4f, 0.32f) : Color.white);
                    if (!fake && m.HasProperty("_EmissionColor") && m.mainTexture)
                    {
                        // the real Verity shines a little by itself (its own face texture as the glow)
                        m.EnableKeyword("_EMISSION"); m.SetTexture("_EmissionMap", m.mainTexture); m.SetColor("_EmissionColor", new Color(0.45f, 0.4f, 0.2f));
                    }
                }
                r.materials = ms;
            }
            // the model carries five faces (Blockbench groups switched by scale 0/1): show one, flap the mouth while talking.
            // Its own clips also move the root about, so the body is animated here instead.
            if (anim) { anim.enabled = false; anim = null; }
            foreach (var t in b.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n.StartsWith("mouth_closed_idle")) faceClosed = t; else if (n.StartsWith("mouth_open_idle")) faceOpen = t;
                else if (n.StartsWith("mouth_closed_sus")) susClosed = t; else if (n.StartsWith("mouth_open_sus")) susOpen = t;
                else if (n.StartsWith("mouth_closed_bored")) bored = t;
                else if (n.StartsWith("root_")) ball = t;
            }
            if (ball) { ball0 = ball.localScale; ballRot0 = ball.localRotation; }
            if (fake && susClosed) { faceClosed = susClosed; if (susOpen) faceOpen = susOpen; }
            Face(false);
            var lg = new GameObject("glow"); lg.transform.SetParent(transform, false); lg.transform.localPosition = new Vector3(0, 0.35f, 0);
            glow = lg.AddComponent<Light>(); glow.type = LightType.Point; glow.shadows = LightShadows.None;
            glow.color = fake ? new Color(0.8f, 0.72f, 0.45f) : new Color(1f, 0.88f, 0.35f);
            glow.intensity = fake ? 0.35f : 1.6f; glow.range = fake ? 2.5f : 5f;
            voice = gameObject.AddComponent<AudioSource>(); voice.spatialBlend = 1; voice.rolloffMode = AudioRolloffMode.Linear; voice.minDistance = 1.5f; voice.maxDistance = 18f; voice.dopplerLevel = 0;
            var all = Resources.LoadAll<AudioClip>("VeritySfx");
            blips = all.Where(c => c.name.StartsWith("tick") || c.name.StartsWith("pluck") || c.name.StartsWith("select")).ToArray();
            glitch = all.FirstOrDefault(c => c.name.StartsWith("glitch"));
            roamGoal = transform.position;
            bobT = Random.value * 10f;
        }

        // ------------------------------------------------------------------ talking

        /// <summary>Start (or restart) the conversation.</summary>
        public void Talk()
        {
            if (st == St.Talk) { lineT = lineLen; return; }   // E again: skip to the next line
            BuildLines();
            st = St.Talk; line = -1; lineT = 0; lineLen = 0; talkedOnce = true;
            Bestiary.SeeCreature(Key);
        }

        static string FormName(ExitForm f) => f switch { ExitForm.Elevator => "лифт", ExitForm.VentHatch => "вентиляционный люк", ExitForm.Archway => "арка", _ => "дверь без ручки" };
        static string FormTip(ExitForm f) => f switch
        {
            ExitForm.Elevator => "Нажми E у дверей лифта и подожди. Он приезжает не сразу, это нормально.",
            ExitForm.VentHatch => "Люк у самого пола. Подцепи решётку, E, и пролезай внутрь.",
            ExitForm.Archway => "Через арку просто проходи. Двери там нет.",
            _ => "У неё нет ручки, но она открывается. Подойди вплотную и нажми E.",
        };

        string Direction(Vector3 to)
        {
            var f = viewer ? viewer.forward : transform.forward; f.y = 0; to.y = 0;
            float a = Vector3.SignedAngle(f, to, Vector3.up);
            return Mathf.Abs(a) < 35 ? "прямо перед тобой" : Mathf.Abs(a) > 140 ? "у тебя за спиной" : a > 0 ? "справа от тебя" : "слева от тебя";
        }

        void BuildLines()
        {
            lines.Clear();
            var me = viewer ? viewer.position : transform.position;
            var mons = monsters?.Invoke()?.Where(m => m).ToList() ?? new List<Stalker>();
            var exits = level.exits.Where(e => e.reachable && e.instance && !e.instance.Hidden).ToList();
            var forward = exits.Where(e => e.role == ExitRole.Forward).OrderBy(e => (e.position - me).sqrMagnitude).FirstOrDefault()
                          ?? exits.OrderBy(e => (e.position - me).sqrMagnitude).FirstOrDefault();
            lines.Add(talkedOnce ? "Это снова я, Verity! Слушай внимательно." : "Привет! Я Verity, твой личный помощник. Спрашивай что угодно, я знаю всё!");
            if (!fake)
            {
                if (forward != null)
                {
                    target = forward.Approach;
                    lines.Add($"Выход отсюда: {FormName(forward.form)}. Примерно {Mathf.Max(3, Mathf.Round((forward.position - me).magnitude))} метров, {Direction(forward.position - me)}.");
                    lines.Add(FormTip(forward.form));
                }
                else { target = level.start; lines.Add("Хм... отсюда я выхода не вижу. Попробуй сфотографировать стены, иногда выход виден только на снимке."); }
                if (exits.Any(e => e.role == ExitRole.False)) lines.Add("Осторожно: тут есть дверь в никуда. Не каждая дверь ведёт дальше, за некоторыми просто стена.");
                if (exits.Any(e => e.role == ExitRole.Loop)) lines.Add("Некоторые выходы возвращают сюда же, только немного иначе. Это эхо этого места, там всё опаснее.");
                if (mons.Count > 0)
                {
                    var m = mons.OrderBy(x => (x.transform.position - me).sqrMagnitude).First();
                    lines.Add($"Здесь бродит {Bestiary.Name(m.kind)}. Он {Direction(m.transform.position - me)}, примерно в {Mathf.Round((m.transform.position - me).magnitude)} метрах.");
                    lines.Add(m.passive ? "Он мирный, просто не мешай ему." : m.freezeWhenSeen ? "Смотри прямо на него: под взглядом он замирает. Отвернёшься, и он пойдёт." : "Взгляд его не остановит. Не подпускай близко и не заходи в тупики.");
                }
                else lines.Add("Сейчас здесь никого нет. Почти никого.");
                lines.Add("Если станет страшно, ищи вентиляционные люки, они низко у пола. И не трогай торт на Уровне Веселья.");
                lines.Add("Следуй за мной! Я покажу дорогу.");
            }
            else
            {
                // the same cheerful voice, but every word leads to the monster
                var m = mons.OrderBy(x => (x.transform.position - me).sqrMagnitude).FirstOrDefault();
                var falseExit = exits.FirstOrDefault(e => e.role == ExitRole.False);
                if (m) target = m.transform.position;
                else if (falseExit != null) target = falseExit.Approach;
                else target = level.start;
                var fakeForm = forward != null ? forward.form : ExitForm.HandlelessDoor;
                lines.Add($"Выход отсюда: {FormName(fakeForm)}. Совсем рядом, {Direction(target - me)}, метров {Mathf.Max(4, Mathf.Round((target - me).magnitude))}.");
                lines.Add("Там безопасно, никого нет. Совсем никого. Я проверял.");
                lines.Add("Не смотри по сторонам, просто иди за мной. Быстрее.");
            }
            BuildField();
        }

        // ------------------------------------------------------------------ guiding along the real corridors

        void BuildField()
        {
            field = new int[nav.w * nav.h]; for (int i = 0; i < field.Length; i++) field[i] = -1;
            var g = nav.CellOf(target); var q = new Queue<Vector2Int>();
            for (int r = 0; r < 10 && q.Count == 0; r++)
                for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
                {
                    var c = g + new Vector2Int(dx, dz);
                    if (nav.Free(c.x, c.y) && field[c.x + c.y * nav.w] < 0) { field[c.x + c.y * nav.w] = 0; q.Enqueue(c); }
                }
            var d4 = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            while (q.Count > 0)
            {
                var c = q.Dequeue(); int d = field[c.x + c.y * nav.w] + 1;
                foreach (var dd in d4)
                {
                    var n = c + dd; if (!nav.Free(n.x, n.y)) continue;
                    int i = n.x + n.y * nav.w; if (field[i] >= 0) continue; field[i] = d; q.Enqueue(n);
                }
            }
        }

        /// <summary>A point some metres further down the path from p towards the target.</summary>
        Vector3 Ahead(Vector3 p, float metres)
        {
            if (field == null) return target;
            var c = nav.CellOf(p);
            int Dist(Vector2Int x) => nav.InGrid(x.x, x.y) ? field[x.x + x.y * nav.w] : -1;
            if (Dist(c) < 0)
            {
                for (int r = 1; r < 12 && Dist(c) < 0; r++)
                    for (int dz = -r; dz <= r && Dist(c) < 0; dz++) for (int dx = -r; dx <= r; dx++) { var n = c + new Vector2Int(dx, dz); if (Dist(n) >= 0) { c = n; break; } }
                if (Dist(c) < 0) return target;
            }
            int steps = Mathf.RoundToInt(metres / nav.cell);
            for (int s = 0; s < steps && Dist(c) > 0; s++)
            {
                Vector2Int best = c; int bd = Dist(c);
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) { var n = c + new Vector2Int(dx, dz); int dn = Dist(n); if (dn >= 0 && dn < bd) { bd = dn; best = n; } }
                if (best == c) break; c = best;
            }
            return nav.Center(c.x, c.y, floorY);
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.deltaTime; if (dt <= 0) return;
            bobT += dt;
            var me = viewer ? viewer.position : transform.position + transform.forward * 5f;
            var flatMe = new Vector3(me.x, floorY, me.z);
            float dMe = Vector3.Distance(Flat(transform.position), Flat(me));
            Vector3 goal = transform.position; float speed = 1.2f; bool facePlayer = false;

            switch (st)
            {
                case St.Roam:
                    roamT -= dt;
                    if (roamT <= 0 || Flat(roamGoal - transform.position).sqrMagnitude < 0.3f)
                    {
                        roamT = Random.Range(6f, 12f);
                        var c = nav.CellOf(transform.position) + new Vector2Int(Random.Range(-40, 41), Random.Range(-40, 41));
                        if (nav.InGrid(c.x, c.y)) roamGoal = nav.Center(c.x, c.y, floorY);
                    }
                    goal = roamGoal; speed = 0.9f;
                    if (dMe < 16f && viewer) st = St.Approach;
                    break;
                case St.Approach:
                    {
                        // hang in the air a couple of metres in front of the explorer, a little to the side
                        var f = viewer.forward; f.y = 0; f.Normalize();
                        goal = flatMe + f * 2.3f + Vector3.Cross(Vector3.up, f) * 0.35f;
                        speed = dMe > 6 ? 3.2f : 1.6f; facePlayer = true;
                        if (dMe > 26f) st = St.Roam;
                        break;
                    }
                case St.Talk:
                    facePlayer = true; speed = 0.8f;
                    {
                        var f = viewer ? viewer.forward : Vector3.forward; f.y = 0; f.Normalize();
                        goal = flatMe + f * 2.0f;
                    }
                    lineT += dt;
                    if (lineT >= lineLen)
                    {
                        line++;
                        if (line >= lines.Count) { st = St.Guide; break; }
                        lineT = 0; lineLen = Mathf.Clamp(1.6f + lines[line].Length * 0.055f, 2.6f, 7.5f);
                        say?.Invoke(fake ? "Verity?" : "Verity", lines[line], lineLen);
                    }
                    // babble while the words come out
                    blipT -= dt;
                    if (blipT <= 0 && lineT < lineLen * 0.7f && blips.Length > 0)
                    {
                        blipT = Random.Range(0.07f, 0.13f);
                        voice.pitch = fake ? Random.Range(0.72f, 0.92f) : Random.Range(1.15f, 1.55f);
                        voice.PlayOneShot(blips[Random.Range(0, blips.Length)], 0.35f);
                    }
                    break;
                case St.Guide:
                    {
                        float dTarget = Vector3.Distance(Flat(me), Flat(target));
                        if (dTarget < 2.6f)
                        {
                            say?.Invoke(fake ? "Verity?" : "Verity", fake ? "...хи-хи." : "Вот он! Удачи, путник. Я буду где-то рядом.", 3.5f);
                            if (fake && glitch) voice.PlayOneShot(glitch, 0.8f);
                            st = St.Leave; leaveT = 0; break;
                        }
                        // wait for the explorer when they fall behind, otherwise stay a few metres ahead on the path
                        goal = dMe > 7f ? transform.position : Ahead(flatMe, 4.5f);
                        speed = 2.8f;
                        if (dMe > 30f) st = St.Roam;
                        break;
                    }
                case St.Leave:
                    leaveT += dt;
                    goal = transform.position + (transform.position - me).normalized * 3f; speed = 1.5f;
                    if (fake)
                    {
                        // the fake one does not leave: it just goes out
                        glow.intensity = Mathf.Max(0, 0.35f - leaveT * 0.2f);
                        if (leaveT > 1.6f) { Destroy(gameObject); return; }
                    }
                    else if (leaveT > 6f) { st = St.Roam; roamT = 0; }
                    break;
            }

            // fly straight there: walls do not exist for Verity
            var to = Flat(goal - transform.position);
            float step = Mathf.Min(speed * dt, to.magnitude);
            if (to.sqrMagnitude > 1e-4f) transform.position += to.normalized * step;
            float hover = 1.15f + Mathf.Sin(bobT * 1.7f) * 0.09f;
            transform.position = new Vector3(transform.position.x, Mathf.Lerp(transform.position.y, floorY + hover, 1 - Mathf.Exp(-4f * dt)), transform.position.z);
            var look = facePlayer || to.sqrMagnitude < 0.01f ? Flat(me - transform.position) : to;
            if (look.sqrMagnitude > 1e-3f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look.normalized, Vector3.up), 1 - Mathf.Exp(-5f * dt));
            // the mouth moves with the words
            bool speaking = st == St.Talk && lineT < lineLen * 0.7f;
            mouthT -= dt;
            if (speaking && mouthT <= 0) { mouthT = Random.Range(0.08f, 0.16f); Face(!mouthOpen); }
            else if (!speaking && mouthOpen) Face(false);
            if (ball)
            {
                // squash and stretch with the words, a little wobble when flying
                float sq = speaking ? (mouthOpen ? 1.06f : 0.97f) : 1f + Mathf.Sin(bobT * 3.1f) * 0.015f;
                ball.localScale = Vector3.Lerp(ball.localScale, new Vector3(ball0.x / Mathf.Sqrt(sq), ball0.y * sq, ball0.z / Mathf.Sqrt(sq)), 1 - Mathf.Exp(-18f * dt));
                ball.localRotation = ballRot0 * Quaternion.Euler(Mathf.Sin(bobT * 1.3f) * 6f, 0, Mathf.Sin(bobT * 0.9f) * 5f);
            }
            if (anim) anim.SetFloat("Speed", st == St.Talk ? 3.2f : step / dt > 0.3f ? 1.1f : 0f, 0.15f, dt);

            // the fake one flickers now and then
            if (fake && st != St.Leave)
            {
                glitchT -= dt;
                glow.intensity = glitchT > 0 ? 0.05f : 0.35f;
                if (glitchT < -Random.Range(2f, 6f)) glitchT = Random.Range(0.05f, 0.15f);
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        // ------------------------------------------------------------------ spawning

        /// <summary>Deterministic per address: a real Verity in some places, a fake one in others (more in Echoes).</summary>
        public static List<Verity> SpawnFor(LevelBuilder.Result level, bool force = false)
        {
            var list = new List<Verity>();
            if (level.nav == null) return list;
            var rng = new Rng(Hashing.Combine(level.address.BaseSeed, Hashing.Fnv64("verity:" + level.address.Generation)));
            float pReal = level.aesthetic == "LO" || level.aesthetic == "CV" || level.aesthetic == "RN" ? 0.2f : 0.38f;
            float pFake = Mathf.Clamp01(0.14f + level.address.Generation * 0.1f);
            bool real = force || rng.Chance(pReal), fk = force || rng.Chance(pFake);
            var nav = level.nav; var cells = new List<Vector2Int>();
            var s = nav.CellOf(level.start);
            for (int z = 0; z < nav.h; z += 4) for (int x = 0; x < nav.w; x += 4)
            {
                if (!nav.Free(x, z)) continue;
                float d = (new Vector2(x, z) - (Vector2)s).magnitude * nav.cell;
                if (d > 9f && d < 30f) cells.Add(new Vector2Int(x, z));
            }
            if (cells.Count == 0) return list;
            foreach (var isFake in new[] { false, true })
            {
                if (isFake ? !fk : !real) continue;
                var c = cells[rng.Range(0, cells.Count)];
                var v = Spawn(level, nav.Center(c.x, c.y, level.start.y + 1.2f), isFake);
                if (v) list.Add(v);
            }
            return list;
        }
    }
}
