using System.Linq;
using Address.World;
using UnityEngine;

namespace Address.Game
{
    /// <summary>
    /// AI Director (algorithmic, no ML): quiet escalation. No markers, no text - the place itself starts to give the
    /// forward exit away the longer the player searches: a draft of dust drifting towards it, then the nearest lamp
    /// failing, then the sound of moving air. The player believes they noticed it themselves.
    /// </summary>
    public class Director : MonoBehaviour
    {
        public float stage1 = 480f, stage2 = 780f, stage3 = 1080f;   // seconds in the level
        LevelBuilder.Result level; ExitPlan target; float t; int stage;
        AudioClip airClip; Material dustMat;

        public void Begin(LevelBuilder.Result lvl, AudioClip air, Material dust)
        {
            level = lvl; airClip = air; dustMat = dust; t = 0; stage = 0;
            target = lvl.exits.Where(e => e.role == ExitRole.Forward && e.reachable && e.instance).OrderBy(e => e.index).FirstOrDefault();
        }

        public Transform player; Vector3 lastPos; float still;
        public float silentSeconds = 60f;

        void Update()
        {
            if (level == null || target == null || !target.instance) return;
            t += Time.deltaTime;
            if (level.rare == "silent" && target.instance.Hidden && player)
            {
                still = (player.position - lastPos).sqrMagnitude < 0.0004f ? still + Time.deltaTime : 0f;
                lastPos = player.position;
                if (still > silentSeconds) { target.instance.SetHidden(false); Debug.Log("[ADDRESS] director: the silent place opened its exit"); }
                return;
            }
            if (stage == 0 && t > stage1) { stage = 1; Draft(); }
            if (stage == 1 && t > stage2) { stage = 2; FailingLamp(); }
            if (stage == 2 && t > stage3) { stage = 3; Air(); }
        }

        /// <summary>Dust motes in the exit's room slowly drifting into it.</summary>
        void Draft()
        {
            var go = new GameObject("Draft"); go.transform.SetParent(target.instance.transform, false);
            go.transform.localPosition = new Vector3(0, 1.0f, 2.2f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = 9f; main.startSpeed = 0.02f; main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.maxParticles = 160; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startColor = new Color(1f, 0.97f, 0.88f, 0.55f);
            var em = ps.emission; em.rateOverTime = 14;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(2.5f, 1.8f, 2.5f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.z = new ParticleSystem.MinMaxCurve(-0.22f, -0.12f);           // towards the exit plane
            vel.y = new ParticleSystem.MinMaxCurve(-0.01f, 0.02f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.05f; noise.frequency = 0.4f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (dustMat) r.sharedMaterial = dustMat;   // asset reference: Shader.Find would be stripped from builds
            Debug.Log($"[ADDRESS] director: draft towards exit {target.index}");
        }

        void FailingLamp()
        {
            var l = level.root.GetComponentsInChildren<Light>().Where(x => x.type == LightType.Spot && x.enabled)
                .OrderBy(x => (x.transform.position - target.position).sqrMagnitude).FirstOrDefault();
            if (l && !l.GetComponent<Flicker>()) l.gameObject.AddComponent<Flicker>().seed = 17f;
            Debug.Log("[ADDRESS] director: lamp near the exit starts failing");
        }

        void Air()
        {
            if (!airClip) return;
            var s = target.instance.gameObject.AddComponent<AudioSource>();
            s.clip = airClip; s.loop = true; s.spatialBlend = 1; s.volume = 0.35f; s.pitch = 0.55f; s.minDistance = 0.5f; s.maxDistance = 7f; s.Play();
            Debug.Log("[ADDRESS] director: air moving behind the exit");
        }
    }
}
