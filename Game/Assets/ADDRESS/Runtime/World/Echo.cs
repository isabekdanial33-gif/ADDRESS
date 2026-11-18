using System.Collections.Generic;
using System.Linq;
using Address.Core;
using Address.Placement;
using UnityEngine;
using Pose = Address.Placement.Pose;

namespace Address.World
{
    /// <summary>
    /// Echo copies: generation g = generation g-1 + ONE fixed step of 3 small mutations, chosen deterministically from
    /// hash(seed, step). Applying steps 1..g in order makes distortion accumulate (a copy of a copy), never "jump".
    /// Every mutation that moves an object goes through the placement engine, so Echoes stay physically valid.
    /// </summary>
    public static class Echo
    {
        enum Mut { LightOff, RemoveProp, ShiftProp, DarkenWalls, Flicker, RotateProp }

        public static List<string> Apply(LevelBuilder.Result level, Material wallMat)
        {
            var log = new List<string>();
            int g = level.address.Generation;
            if (g <= 0) return log;
            var lights = level.root.GetComponentsInChildren<Light>(true).OrderBy(l => l.transform.position.x).ThenBy(l => l.transform.position.z).ToList();
            float wallDarken = 0;
            for (int step = 1; step <= g; step++)
            {
                var rng = new Rng(Hashing.Combine(level.address.BaseSeed, (ulong)step * 7919UL));
                for (int m = 0; m < 3; m++)
                {
                    var kind = (Mut)rng.Range(0, 6);
                    var props = level.engine.placed.Where(p => p && p.profile.massClass != MassClass.Fixed && p.supportParent == null).OrderBy(p => p.uid).ToList();
                    switch (kind)
                    {
                        case Mut.LightOff when lights.Count > 2:
                            var l = lights[rng.Range(0, lights.Count)];
                            l.enabled = false;
                            foreach (var r in l.transform.parent.GetComponentsInChildren<Renderer>()) r.enabled = false;
                            log.Add($"step{step}: light off at {l.transform.position}"); break;
                        case Mut.Flicker when lights.Count > 2:
                            var f = lights[rng.Range(0, lights.Count)];
                            if (!f.GetComponent<Flicker>()) f.gameObject.AddComponent<Flicker>().seed = rng.Value * 100f;
                            log.Add($"step{step}: flicker"); break;
                        case Mut.RemoveProp when props.Count > 0:
                            var victim = props[rng.Range(0, props.Count)];
                            string vname = victim.name;
                            RemoveWithRiders(level, victim);
                            log.Add($"step{step}: removed {vname}"); break;
                        case Mut.ShiftProp when props.Count > 0:
                        case Mut.RotateProp when props.Count > 0:
                            var p = props[rng.Range(0, props.Count)];
                            if (level.engine.placed.Any(c => c && c.supportParent == p)) break; // don't move furniture with things on it
                            var prof = p.profile; var pos = p.transform.position; var rot = p.transform.rotation;
                            var room = level.rooms.FirstOrDefault(rr => rr.bounds.Contains(new Vector3(pos.x, rr.bounds.center.y, pos.z)));
                            if (room == null) break;
                            RemoveWithRiders(level, p);
                            level.engine.room = room;
                            bool rotateOnly = kind == Mut.RotateProp;
                            var moved = level.engine.TryPlace(new PlacementEngine.Request
                            {
                                profile = prof, tag = "echo/" + kind,
                                candidate = (r, i) => new Pose(rotateOnly ? pos : pos + new Vector3(r.Range(-0.6f, 0.6f), 0, r.Range(-0.6f, 0.6f)), rot * Quaternion.Euler(0, r.Range(-35f, 35f) + (rotateOnly ? 180 : 0), 0), 1f)
                            }, rng);
                            log.Add($"step{step}: {kind} {prof.id} -> {(moved ? "ok" : "gone")}"); break;
                        case Mut.DarkenWalls:
                            wallDarken += 0.035f; log.Add($"step{step}: walls darker"); break;
                    }
                }
            }
            if (wallDarken > 0 && wallMat)
            {
                var mpb = new MaterialPropertyBlock();
                var c = Color.Lerp(Color.white, new Color(0.55f, 0.5f, 0.38f), Mathf.Clamp01(wallDarken));
                foreach (var r in level.root.GetComponentsInChildren<MeshRenderer>())
                    if (r.sharedMaterial == wallMat) { r.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", c); r.SetPropertyBlock(mpb); }
            }
            return log;
        }

        static void RemoveWithRiders(LevelBuilder.Result level, PlacedObject p)
        {
            foreach (var c in level.engine.placed.Where(c => c && c.supportParent == p).ToList()) RemoveWithRiders(level, c);
            level.engine.placed.Remove(p);
            Object.DestroyImmediate(p.gameObject);
            Physics.SyncTransforms();
        }
    }

    /// <summary>Fluorescent tube failing: irregular dips, deterministic per light.</summary>
    public class Flicker : MonoBehaviour
    {
        public float seed;
        Light l; float baseI;
        void Start() { l = GetComponent<Light>(); baseI = l ? l.intensity : 0; }
        void Update()
        {
            if (!l) return;
            float t = Time.time + seed;
            float n = Mathf.PerlinNoise(t * 7f, seed);
            l.intensity = n < 0.22f ? baseI * 0.08f : baseI * (0.85f + 0.15f * Mathf.PerlinNoise(t * 30f, seed + 3));
        }
    }
}
