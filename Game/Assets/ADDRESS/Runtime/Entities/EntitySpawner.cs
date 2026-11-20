using System.Collections.Generic;
using System.Linq;
using Address.Core;
using Address.Player;
using Address.World;
using UnityEngine;

namespace Address.Entities
{
    /// <summary>
    /// Decides (deterministically from the address, so co-op peers agree) whether a place has an entity and where it
    /// starts, then builds its body from the hazmat model: stretched limbs, a darker soiled suit, black lenses with two
    /// dim points behind them. Light places rarely have one; Echo copies have them more often.
    /// </summary>
    // Smiler, Skin Stealer, Partygoer, Growlers, Needlelimbs and the distorted Captain Clark are downloaded CC-BY Sketchfab
    // models (Resources/Entities/Model_<Kind>, credits in ThirdParty/Sketchfab/CREDITS.txt).
    public enum EntityKind { Echo, Salesman, Crawler, Mannequin, Limper, Smiler, Faceling, Partygoer, SkinStealer, Clark, Growler, Needlelimbs, Howler, Lifeform, Hazmat, Hound, Rake, Librarian, Deathmoth }

    public static class EntitySpawner
    {
        /// <summary>Which kind lives here: showrooms keep a Salesman, dark zones a Crawler, the Poolrooms mannequins.</summary>
        static EntityKind PickKind(LevelBuilder.Result level, Rng rng)
        {
            var opts = new List<EntityKind>();
            if (level.aesthetic == "PG") { opts.AddRange(new[] { EntityKind.Crawler, EntityKind.Limper, EntityKind.Echo, EntityKind.Smiler, EntityKind.Smiler, EntityKind.Growler, EntityKind.Growler, EntityKind.SkinStealer, EntityKind.Hound, EntityKind.Rake }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "HT") { opts.AddRange(new[] { EntityKind.Mannequin, EntityKind.Salesman, EntityKind.Echo, EntityKind.Partygoer, EntityKind.Partygoer, EntityKind.Faceling, EntityKind.Needlelimbs, EntityKind.Needlelimbs, EntityKind.Clark, EntityKind.Rake }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "PD") { opts.AddRange(new[] { EntityKind.Howler, EntityKind.Howler, EntityKind.Lifeform, EntityKind.Growler, EntityKind.Hazmat, EntityKind.Deathmoth }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "WH") { opts.AddRange(new[] { EntityKind.Lifeform, EntityKind.Lifeform, EntityKind.Howler, EntityKind.SkinStealer, EntityKind.Crawler, EntityKind.Hazmat, EntityKind.Hound, EntityKind.Deathmoth }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "LO") { opts.AddRange(new[] { EntityKind.Smiler, EntityKind.Smiler, EntityKind.Smiler, EntityKind.Crawler, EntityKind.Howler, EntityKind.Rake, EntityKind.Rake }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "EL") { opts.AddRange(new[] { EntityKind.Hazmat, EntityKind.Howler, EntityKind.Lifeform, EntityKind.Clark }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "CV") { opts.AddRange(new[] { EntityKind.Rake, EntityKind.Rake, EntityKind.Hound, EntityKind.Hound, EntityKind.Deathmoth, EntityKind.Crawler }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "RN") { opts.AddRange(new[] { EntityKind.Hound, EntityKind.Partygoer, EntityKind.Rake, EntityKind.Growler, EntityKind.SkinStealer }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "FL") { opts.AddRange(new[] { EntityKind.Smiler, EntityKind.Deathmoth, EntityKind.Deathmoth, EntityKind.Lifeform, EntityKind.Echo }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "LB") { opts.AddRange(new[] { EntityKind.Librarian, EntityKind.Librarian, EntityKind.Librarian, EntityKind.Smiler }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "LA") { opts.AddRange(new[] { EntityKind.Hazmat, EntityKind.Hazmat, EntityKind.Hazmat, EntityKind.SkinStealer, EntityKind.Clark, EntityKind.Howler }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "OF") { opts.AddRange(new[] { EntityKind.Clark, EntityKind.Clark, EntityKind.Needlelimbs, EntityKind.Salesman, EntityKind.Echo, EntityKind.SkinStealer, EntityKind.Faceling, EntityKind.Hazmat }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "LF") { opts.AddRange(new[] { EntityKind.Partygoer, EntityKind.Partygoer, EntityKind.Partygoer, EntityKind.Partygoer, EntityKind.Smiler }); return opts[rng.Range(0, opts.Count)]; }
            if (level.aesthetic == "PR") { opts.AddRange(new[] { EntityKind.Mannequin, EntityKind.Mannequin, EntityKind.Limper, EntityKind.Echo, EntityKind.Needlelimbs }); return opts[rng.Range(0, opts.Count)]; }
            opts.AddRange(new[] { EntityKind.Echo, EntityKind.Echo, EntityKind.Limper, EntityKind.Mannequin, EntityKind.Faceling, EntityKind.Partygoer, EntityKind.Partygoer, EntityKind.SkinStealer, EntityKind.SkinStealer, EntityKind.Clark, EntityKind.Howler, EntityKind.Lifeform, EntityKind.Hound, EntityKind.Hazmat, EntityKind.Deathmoth });
            var rooms = level.layout?.rooms;
            if (rooms != null && rooms.Exists(r => r.kind == "showroom")) opts.AddRange(new[] { EntityKind.Salesman, EntityKind.Salesman, EntityKind.Salesman });
            if (rooms != null && rooms.Exists(r => r.dark)) opts.AddRange(new[] { EntityKind.Crawler, EntityKind.Crawler, EntityKind.Smiler, EntityKind.Smiler, EntityKind.Smiler });
            return opts[rng.Range(0, opts.Count)];
        }

        static List<Vector2Int> HomeCells(LevelBuilder.Result level, EntityKind kind, List<Vector2Int> far)
        {
            var res = new List<Vector2Int>(); var rooms = level.layout?.rooms; if (rooms == null) return res;
            foreach (var c in far)
            {
                var p = level.nav.Center(c.x, c.y, 0);
                foreach (var r in rooms)
                {
                    bool home = kind == EntityKind.Salesman ? r.kind == "showroom" : kind == EntityKind.Crawler || kind == EntityKind.Smiler || kind == EntityKind.Growler ? r.dark : false;
                    if (home && r.World.Contains(new Vector2(p.x, p.z))) { res.Add(c); break; }
                }
            }
            return res;
        }
        public static float ChanceFor(string aesthetic, int generation)
        {
            float c = aesthetic switch { "BR" => 0.45f, "PR" => 0.22f, "FA" => 0f, "PG" => 0.5f, "HT" => 0.5f, "OF" => 0.45f, "LF" => 0.65f, "PD" => 0.55f, "WH" => 0.45f, "LO" => 0.7f, "EL" => 0.55f, "CV" => 0.7f, "RN" => 1f, "FL" => 0.5f, "LB" => 0.75f, "LA" => 0.5f, _ => 0.3f };
            var d = Address.Mods.ModSystem.World(aesthetic); if (d != null && d.entityChance >= 0) c = d.entityChance;
            return Mathf.Clamp01(c + generation * 0.15f);
        }

        public static List<Stalker> Spawn(LevelBuilder.Result level, GameObject bodyPrefab, AudioClip drone, bool force = false)
        {
            var list = new List<Stalker>();
            if (!bodyPrefab || level.nav == null) return list;
            var rng = new Rng(Hashing.Combine(level.address.BaseSeed, Hashing.Fnv64("entities:" + level.address.Generation)));
            if (!force && !rng.Chance(ChanceFor(level.aesthetic, level.address.Generation))) return list;
            int count = level.address.Generation >= 2 && rng.Chance(0.4f) ? 2 : 1;

            // reachable roomy cells far enough from the start (BFS from the start cell)
            var nav = level.nav; var dist = new int[nav.w * nav.h]; for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var s = nav.CellOf(level.start); var q = new Queue<Vector2Int>();
            if (!nav.InGrid(s.x, s.y)) return list;
            dist[s.x + s.y * nav.w] = 0; q.Enqueue(s);
            var far = new List<Vector2Int>();
            var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            while (q.Count > 0)
            {
                var c = q.Dequeue(); int d = dist[c.x + c.y * nav.w];
                if (d * nav.cell > 22f && nav.Roomy(c.x, c.y)) far.Add(c);
                foreach (var dd in dirs)
                {
                    var n = c + dd; if (!nav.InGrid(n.x, n.y) || !nav.Free(n.x, n.y)) continue;
                    int i = n.x + n.y * nav.w; if (dist[i] >= 0) continue; dist[i] = d + 1; q.Enqueue(n);
                }
            }
            // Level Run: they are already behind you, at the start of the corridor
            if (level.aesthetic == "RN")
            {
                var behind = new List<Vector2Int>();
                for (int z = 0; z < nav.h; z++) for (int x = 0; x < nav.w; x++)
                {
                    var p0 = nav.Center(x, z, 0);
                    if (nav.Roomy(x, z) && p0.z < level.start.z - 9f) behind.Add(new Vector2Int(x, z));
                }
                if (behind.Count > 0) { far = behind; count = 2; }
            }
            if (far.Count == 0) return list;
            for (int k = 0; k < count; k++)
            {
                // creatures from mods: the world's own list, or any mod creature that lives here
                var wdef = Address.Mods.ModSystem.World(level.aesthetic);
                string modPick = null;
                if (wdef != null && wdef.entities != null && wdef.entities.Length > 0)
                {
                    var key = wdef.entities[rng.Range(0, wdef.entities.Length)];
                    if (System.Enum.TryParse<EntityKind>(key, out var ek)) { var c0 = far[rng.Range(0, far.Count)]; var st0 = Build(bodyPrefab, level, nav.Center(c0.x, c0.y, level.start.y), Quaternion.Euler(0, rng.Range(0f, 360f), 0), k, ek); st0.Init(level, level.address.Generation, drone); st0.Configure(ek); st0.wakeDelay += k * 20f; list.Add(st0); continue; }
                    modPick = key;
                }
                else
                {
                    var mods = Address.Mods.ModSystem.Entities.Where(e => e.worlds == null || e.worlds.Length == 0 || System.Array.IndexOf(e.worlds, level.aesthetic) >= 0).ToList();
                    int total = mods.Sum(e => Mathf.Max(0, e.weight));
                    if (total > 0 && rng.Range(0, total + 10) < total)
                    {
                        int r = rng.Range(0, total);
                        foreach (var e in mods) { r -= Mathf.Max(0, e.weight); if (r < 0) { modPick = e.id; break; } }
                    }
                }
                var med = modPick != null ? Address.Mods.ModSystem.Entity(modPick) : null;
                if (med != null)
                {
                    var c1 = far[rng.Range(0, far.Count)];
                    var ms = BuildMod(level, med, nav.Center(c1.x, c1.y, level.start.y), Quaternion.Euler(0, rng.Range(0f, 360f), 0), k);
                    ms.Init(level, level.address.Generation, drone); ms.ConfigureMod(med); ms.wakeDelay += k * 20f;
                    list.Add(ms); continue;
                }
                var kind = PickKind(level, rng);
                var home = HomeCells(level, kind, far);
                var c = home.Count > 0 ? home[rng.Range(0, home.Count)] : far[rng.Range(0, far.Count)];
                var pos = nav.Center(c.x, c.y, level.start.y);
                var st = Build(bodyPrefab, level, pos, Quaternion.Euler(0, rng.Range(0f, 360f), 0), k, kind);
                st.Init(level, level.address.Generation, drone);
                st.Configure(kind);
                st.wakeDelay += k * 20f;
                if (level.aesthetic == "RN") st.wakeDelay = 5f + k * 6f;   // Level Run: it is already coming
                list.Add(st);
            }
            return list;
        }

        /// <summary>A creature from a mod: a copy of its loaded glTF model (legacy animations) driven by a Stalker.</summary>
        public static Stalker BuildMod(LevelBuilder.Result level, Address.Mods.EntityDef e, Vector3 pos, Quaternion rot, int index)
        {
            var root = new GameObject($"Entity_{index}_mod_{e.id}"); root.transform.SetParent(level.root.transform, false);
            root.transform.SetPositionAndRotation(pos, rot);
            var body = Object.Instantiate(e.template, root.transform); body.name = "Body"; body.SetActive(true);
            body.transform.localPosition = Vector3.zero; body.transform.localRotation = Quaternion.identity;
            foreach (var c in body.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            return root.AddComponent<Stalker>();
        }

        static GameObject Build0(GameObject prefab, Transform parent) { var g = Object.Instantiate(prefab, parent); return g; }

        /// <summary>An entity of a given kind at a given pose (also used by the test tour).</summary>
        public static Stalker SpawnKind(LevelBuilder.Result level, GameObject bodyPrefab, EntityKind kind, Vector3 pos, Quaternion rot, AudioClip drone)
        {
            var st = Build(bodyPrefab, level, pos, rot, 90 + (int)kind, kind);
            st.Init(level, level.address.Generation, drone); st.Configure(kind);
            return st;
        }

        static Stalker Build(GameObject bodyPrefab, LevelBuilder.Result level, Vector3 pos, Quaternion rot, int index, EntityKind kind = EntityKind.Echo)
        {
            var root = new GameObject($"Entity_{index}_{kind}"); root.transform.SetParent(level.root.transform, false);
            root.transform.SetPositionAndRotation(pos, rot);
            var model = Resources.Load<GameObject>("Entities/Model_" + kind);
            if (model) return BuildModel(root, model, level, kind);
            if (kind == EntityKind.Smiler)
            {
                // no body at all: eyes and a grin hanging in the dark at head height, always turned to you
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(q.GetComponent<Collider>());
                q.name = "Grin"; q.transform.SetParent(root.transform, false); q.transform.localPosition = new Vector3(0, 1.62f, 0); q.transform.localScale = Vector3.one * 0.95f;
                var mr = q.GetComponent<MeshRenderer>(); mr.sharedMaterial = Resources.Load<Material>("Entities/SmilerMat"); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                q.AddComponent<Address.Rendering.Billboard>();
                return root.AddComponent<Stalker>();
            }
            bool mannequin = kind == EntityKind.Mannequin || kind == EntityKind.Faceling || kind == EntityKind.Partygoer;
            var mp = mannequin ? Resources.Load<GameObject>("Entities/Mannequin") : null;
            var body = Build0(mp ? mp : bodyPrefab, root.transform); body.name = "Body";
            body.transform.localPosition = Vector3.zero; body.transform.localRotation = Quaternion.identity;
            int gen = level.address.Generation;
            body.transform.localScale = kind switch
            {
                EntityKind.Salesman => new Vector3(0.95f, 1.34f + gen * 0.03f, 0.95f),   // towering
                EntityKind.Mannequin => new Vector3(1f, 1.02f, 1f),
                EntityKind.Limper => new Vector3(0.92f, 1.0f, 0.92f),
                _ => new Vector3(0.86f, 1.13f + gen * 0.03f, 0.86f),                       // tall and thin
            };
            foreach (var c in body.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            var pb = body.GetComponent<ProceduralBody>(); if (pb) Object.DestroyImmediate(pb);   // real animations drive the body now
            foreach (var t in body.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            var an = body.GetComponentInChildren<Animator>();
            if (an)
            {
                an.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Entities/" + kind);
                an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate; an.enabled = true;
                if (an.runtimeAnimatorController) body.transform.localRotation = Quaternion.Euler(0, 180f, 0);   // UAL clips face -Z
            }

            Transform F(string n) { foreach (var t in body.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }
            if (mannequin)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                var m = new Material(lit) { name = "mannequin_plastic" };
                m.SetColor("_BaseColor", new Color(0.86f, 0.83f, 0.78f)); m.SetFloat("_Smoothness", 0.62f); m.SetFloat("_Metallic", 0f);
                if (kind == EntityKind.Faceling) { m.SetColor("_BaseColor", new Color(0.62f, 0.5f, 0.42f)); m.SetFloat("_Smoothness", 0.3f); }   // skin, no features
                if (kind == EntityKind.Partygoer) { m.SetColor("_BaseColor", new Color(0.95f, 0.78f, 0.12f)); m.SetFloat("_Smoothness", 0.5f); }
                foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                {
                    var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = m; r.sharedMaterials = ms;
                    if (r is SkinnedMeshRenderer s0) s0.updateWhenOffscreen = true;
                }
                if (kind == EntityKind.Partygoer)
                {
                    var hd = F("Head"); var fm = Resources.Load<Material>("Entities/PartyFaceMat");
                    if (hd && fm)
                    {
                        var face = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(face.GetComponent<Collider>()); face.name = "Face";
                        face.transform.SetParent(hd, false);
                        face.transform.position = hd.position + root.transform.forward * 0.115f + Vector3.up * 0.07f;
                        face.transform.rotation = Quaternion.LookRotation(-root.transform.forward, Vector3.up);
                        face.transform.localScale = Vector3.one * 0.2f / Mathf.Max(0.001f, hd.lossyScale.x);
                        face.GetComponent<MeshRenderer>().sharedMaterial = fm;
                    }
                }
                return root.AddComponent<Stalker>();
            }

            // stretch forearms, shins and neck by moving the child joints out (the skin between them stretches)
            float limb = (kind == EntityKind.Salesman ? 1.32f : kind == EntityKind.Crawler ? 1.3f : kind == EntityKind.Limper ? 1.04f : 1.22f) + gen * 0.06f;
            foreach (var n in new[] { "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand" }) { var t = F(n); if (t) t.localPosition *= limb; }
            foreach (var n in new[] { "LeftLowerLeg", "RightLowerLeg" }) { var t = F(n); if (t) t.localPosition *= 1.08f; }
            var neck = F("Head"); if (neck) neck.localPosition *= (kind == EntityKind.Limper ? 1.1f : 1.35f) + gen * 0.08f;
            // per kind suit colour: soiled black (echo, crawler), sick green-grey (limper), sun-faded tan (salesman)
            Color tint = kind switch { EntityKind.Salesman => new Color(0.62f, 0.5f, 0.34f), EntityKind.Limper => new Color(0.3f, 0.36f, 0.26f), _ => Color.clear };

            // soiled, darker suit; black glossy lenses
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m || !m.HasProperty("_BaseColor")) continue;
                    var c = m.GetColor("_BaseColor"); string n = m.name.ToLowerInvariant();
                    if (n.Contains("lens")) { m.SetColor("_BaseColor", new Color(0.01f, 0.01f, 0.012f)); if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.95f); }
                    else
                    {
                        float g = c.grayscale; var dark = Color.Lerp(c, new Color(g, g, g), 0.85f) * 0.14f; dark.a = 1;
                        if (tint.a > 0) { dark = tint * (0.5f + 0.5f * g); dark.a = 1; }
                        m.SetColor("_BaseColor", dark); if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
                        if (m.HasProperty("_MetallicGlossMap")) { m.SetTexture("_MetallicGlossMap", null); m.DisableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Metallic", 0f); }
                    }
                }
                r.materials = mats;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }

            // two faint points behind the lenses
            var head = F("Head");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (head && unlit)
            {
                var eyeMat = new Material(unlit); eyeMat.SetColor("_BaseColor", new Color(1f, 0.92f, 0.75f));
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); var mesh = sphere.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(sphere);
                foreach (var side in new[] { -1f, 1f })
                {
                    var e = new GameObject("eye"); e.transform.SetParent(head, true);
                    e.transform.position = head.position + root.transform.forward * 0.16f + root.transform.right * side * 0.034f + root.transform.up * 0.035f;
                    e.transform.localScale = Vector3.one * 0.016f / Mathf.Max(0.001f, head.lossyScale.x);
                    e.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = e.AddComponent<MeshRenderer>(); mr.sharedMaterial = eyeMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            return root.AddComponent<Stalker>();
        }

        /// <summary>A downloaded creature: prefab already scaled to height with feet at 0 and its controller assigned.</summary>
        static Stalker BuildModel(GameObject root, GameObject model, LevelBuilder.Result level, EntityKind kind)
        {
            var body = Object.Instantiate(model, root.transform); body.name = "Body";
            body.transform.localPosition = Vector3.zero; body.transform.localRotation = Quaternion.identity;
            int gen = level.address.Generation;
            body.transform.localScale = Vector3.one * (1f + gen * 0.03f);
            var fit = body.GetComponent<FitHeight>(); if (fit) fit.extra = 1f + gen * 0.03f;
            foreach (var c in body.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            foreach (var t in body.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            var an = body.GetComponentInChildren<Animator>();
            if (an)
            {
                an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate; an.enabled = true;
                if (an.isHuman) body.transform.localRotation = Quaternion.Euler(0, 180f, 0);   // UAL clips face -Z
                if (kind == EntityKind.Clark && an.isHuman)
                {
                    // the distorted captain: arms and neck pulled too long, the head hangs (Stalker tilts it)
                    void Stretch(HumanBodyBones b, float k) { var t = an.GetBoneTransform(b); if (t) t.localPosition *= k; }
                    Stretch(HumanBodyBones.LeftHand, 1.45f); Stretch(HumanBodyBones.RightHand, 1.45f);
                    Stretch(HumanBodyBones.LeftLowerArm, 1.2f); Stretch(HumanBodyBones.RightLowerArm, 1.2f);
                    Stretch(HumanBodyBones.Head, 1.6f + gen * 0.1f);
                    var hd = an.GetBoneTransform(HumanBodyBones.Head); if (hd) hd.name = "Head";
                }
            }
            return root.AddComponent<Stalker>();
        }

        // ------------------------------------------------------------------ sounds (synthesised)

        static AudioClip drone, hit;
        public static AudioClip Drone => drone ? drone : drone = MakeDrone();
        public static AudioClip Hit
        {
            get
            {
                var c = Resources.Load<AudioClip>("EntitySfx/scare_" + Random.Range(1, 5));   // downloaded CC0 jump scares
                return c ? c : hit ? hit : hit = MakeHit();
            }
        }

        static AudioClip MakeDrone()
        {
            int sr = 44100, n = sr * 6; var d = new float[n]; var r = new System.Random(7); float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sr;
                float beat = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2 / 6f * 3);   // loop-safe (whole cycles in 6 s)
                float tone = Mathf.Sin(t * 2 * Mathf.PI * 55f) * 0.5f + Mathf.Sin(t * 2 * Mathf.PI * 55.5f * 2) * 0.22f + Mathf.Sin(t * 2 * Mathf.PI * 82.5f) * 0.18f * beat;
                lp += ((float)r.NextDouble() * 2 - 1 - lp) * 0.02f;   // low rumble noise
                d[i] = (tone * 0.55f + lp * 1.6f) * 0.5f;
            }
            var c = AudioClip.Create("entity_drone", n, 1, sr, false); c.SetData(d, 0); return c;
        }

        static AudioClip MakeHit()
        {
            int sr = 44100, n = (int)(sr * 1.6f); var d = new float[n]; var r = new System.Random(3); float ph = 0, lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n; float f = Mathf.Lerp(420f, 38f, Mathf.Pow(t, 0.4f)); ph += f / sr * Mathf.PI * 2;
                lp += ((float)r.NextDouble() * 2 - 1 - lp) * 0.25f;
                float env = Mathf.Min(1, i / 200f) * Mathf.Pow(1 - t, 1.5f);
                d[i] = Mathf.Clamp((Mathf.Sin(ph) * 0.8f + lp * 0.9f + Mathf.Sign(Mathf.Sin(ph * 0.5f)) * 0.25f) * env * 1.4f, -0.95f, 0.95f);
            }
            var c = AudioClip.Create("entity_hit", n, 1, sr, false); c.SetData(d, 0); return c;
        }
    }
}
