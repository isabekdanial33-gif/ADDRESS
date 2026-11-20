using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Address.Game
{
    /// <summary>
    /// Fear: when something hunts you the picture closes in (dark red vignette), colours drain, the edges split
    /// (chromatic aberration), grain rises, the lens pulses with the heartbeat and the view trembles.
    /// Heartbeat: downloaded CC0 loops, slow then fast.
    /// </summary>
    public class FearFx : MonoBehaviour
    {
        public Transform head;
        public float target;     // 0..1 set by the game every frame
        public float Fear { get; private set; }
        Volume vol; ChromaticAberration ca; Vignette vg; FilmGrain fg; LensDistortion ld; ColorAdjustments col;
        AudioSource heart; AudioClip slow, fast; float beat;

        public void Init()
        {
            var go = new GameObject("FearVolume"); go.transform.SetParent(transform, false);
            vol = go.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 50; vol.weight = 0;
            var p = ScriptableObject.CreateInstance<VolumeProfile>(); vol.sharedProfile = p;
            ca = p.Add<ChromaticAberration>(true); ca.intensity.Override(0.9f);
            vg = p.Add<Vignette>(true); vg.intensity.Override(0.5f); vg.smoothness.Override(0.85f); vg.color.Override(new Color(0.25f, 0f, 0f));
            fg = p.Add<FilmGrain>(true); fg.type.Override(FilmGrainLookup.Medium3); fg.intensity.Override(0.75f);
            ld = p.Add<LensDistortion>(true); ld.intensity.Override(0f);
            col = p.Add<ColorAdjustments>(true); col.saturation.Override(-45f); col.contrast.Override(18f);
            slow = Resources.Load<AudioClip>("Ambience/heartbeat_slow"); fast = Resources.Load<AudioClip>("Ambience/heartbeat_fast");
            heart = gameObject.AddComponent<AudioSource>(); heart.loop = true; heart.spatialBlend = 0; heart.volume = 0; heart.playOnAwake = false;
        }

        void Update()
        {
            if (!vol) return;
            float dt = Time.deltaTime;
            Fear = Mathf.MoveTowards(Fear, Mathf.Clamp01(target), dt * (target > Fear ? 1.5f : 0.35f));
            beat += dt * Mathf.Lerp(1.1f, 2.6f, Fear) * Mathf.PI * 2f;
            float pulse = Mathf.Pow(Mathf.Max(0, Mathf.Sin(beat)), 6f);
            vol.weight = Fear;
            ld.intensity.value = -0.18f * pulse * Fear;
            vg.intensity.value = 0.42f + 0.12f * pulse;
            // heartbeat: slow loop when uneasy, fast one when it is close
            var want = Fear > 0.55f ? fast : slow;
            if (want && heart.clip != want) { heart.clip = want; heart.Play(); }
            heart.volume = Mathf.MoveTowards(heart.volume, Fear > 0.08f ? Mathf.Lerp(0.15f, 0.8f, Fear) : 0f, dt * 0.8f);
            if (heart.volume <= 0.001f && heart.isPlaying) heart.Stop(); else if (heart.volume > 0.001f && !heart.isPlaying && heart.clip) heart.Play();
        }

        void LateUpdate()
        {
            // the view trembles (after the controller placed the head this frame)
            if (!head || Fear < 0.05f) return;
            float t = Time.time * 23f, a = Fear * Fear * 0.012f;
            head.localPosition += new Vector3(Mathf.PerlinNoise(t, 1.3f) - 0.5f, Mathf.PerlinNoise(2.7f, t) - 0.5f, 0) * a * 2f;
            head.localRotation *= Quaternion.Euler((Mathf.PerlinNoise(t * 0.7f, 5f) - 0.5f) * Fear * 1.6f, 0, (Mathf.PerlinNoise(9f, t * 0.6f) - 0.5f) * Fear * 2.2f);
        }
    }
}
