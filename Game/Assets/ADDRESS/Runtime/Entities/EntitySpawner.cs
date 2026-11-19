using System.Collections.Generic;
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
    public enum EntityKind { Echo, Salesman, Crawler, Mannequin, Limper }

    public static class EntitySpawner
    {
        /// <summary>Which kind lives here: showrooms keep a Salesman, dark zones a Crawler, the Poolrooms mannequins.</summary>
        static EntityKind PickKind(LevelBuilder.Result level, Rng rng)
        {
            var opts = new List<EntityKind>();
            if (level.aesthetic == "PR") { opts.AddRange(new[] { EntityKind.Mannequin, EntityKind.Mannequin, EntityKind.Limper, EntityKind.Echo }); return opts[rng.Range(0, opts.Count)]; }
            opts.AddRange(new[] { EntityKind.Echo, EntityKind.Echo, EntityKind.Limper, EntityKind.Mannequin });
            var rooms = level.layout?.rooms;
            if (rooms != null && rooms.Exists(r => r.kind == "showroom")) opts.AddRange(new[] { EntityKind.Salesman, EntityKind.Salesman, EntityKind.Salesman });
            if (rooms != null && rooms.Exists(r => r.dark)) opts.AddRange(new[] { EntityKind.Crawler, EntityKind.Crawler, EntityKind.Crawler });
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
                    bool home = kind == EntityKind.Salesman ? r.kind == "showroom" : kind == EntityKind.Crawler ? r.dark : false;
                    if (home && r.World.Contains(new Vector2(p.x, p.z))) { res.Add(c); break; }
                }
            }
            return res;
        }
        public static float ChanceFor(string aesthetic, int generation)
        {
            float c = aesthetic switch { "BR" => 0.45f, "PR" => 0.22f, "FA" => 0f, _ => 0.3f };
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
            if (far.Count == 0) return list;
            for (int k = 0; k < count; k++)
            {
                var kind = PickKind(level, rng);
                var home = HomeCells(level, kind, far);
                var c = home.Count > 0 ? home[rng.Range(0, home.Count)] : far[rng.Range(0, far.Count)];
                var pos = nav.Center(c.x, c.y, level.start.y);
                var st = Build(bodyPrefab, level, pos, Quaternion.Euler(0, rng.Range(0f, 360f), 0), k, kind);
                st.Init(level, level.address.Generation, drone);
                st.Configure(kind);
                st.wakeDelay += k * 20f;
                list.Add(st);
            }
            return list;
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
            bool mannequin = kind == EntityKind.Mannequin;
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
                foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                {
                    var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = m; r.sharedMaterials = ms;
                    if (r is SkinnedMeshRenderer s0) s0.updateWhenOffscreen = true;
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
