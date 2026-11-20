using System.Collections.Generic;
using System.Linq;
using Address.Player;
using UnityEngine;

namespace Address.Game
{
    [System.Serializable]
    public class AudioKit
    {
        public AudioClip hum, roomTone, breath, door, elevator, vent, shutter, travel;
        public AudioClip[] steps;
        public AudioClip doorReal, shutterReal, poolHall;
    }

    /// <summary>
    /// Sound of the place, built so that nothing loops audibly:
    ///  - every lit fixture near you buzzes with its own generated ballast hum (never the same twice, rare crackles);
    ///  - the room tone is generated air that slowly swells; the Poolrooms have a real recording of an empty pool
    ///    hall; Frutiger Aero has soft open-air wind instead of hum;
    ///  - far-away events (a thud, a door, metal) happen at random times and places, muffled by distance;
    ///  - footsteps never repeat the same sample twice in a row and vary in pitch and level; tiles click, carpet thuds;
    ///  - breathing in the mask is only heard after running.
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        public AudioKit kit;
        public FirstPersonController fpc;
        readonly List<AudioSource> humPool = new();
        AudioSource tone, breath, oneShots, place, music;
        ProcVoice toneVoice;
        List<Light> lights = new();
        float distAcc, timer, eventTimer = 25f, runT; int lastStep = -1;
        Vector3 lastPos; string aesthetic = "BR";
        static AudioClip silence;

        public void Init(AudioKit k, FirstPersonController player)
        {
            kit = k; fpc = player;
            if (!silence) silence = AudioClip.Create("silence", 4410, 1, 44100, false);
            for (int i = 0; i < (Application.isMobilePlatform ? 3 : 6); i++)
            {
                var s = Voice($"hum{i}", ProcVoice.Kind.Hum, 0.22f, true, i);
                s.minDistance = 0.7f; s.maxDistance = 8f; s.rolloffMode = AudioRolloffMode.Logarithmic;
                humPool.Add(s);
            }
            tone = Voice("air", ProcVoice.Kind.Air, 0.5f, false, 99); toneVoice = tone.GetComponent<ProcVoice>();
            place = Src("place", null, true, 0f, false);
            breath = Src("breath", kit.breath, true, 0f, false);
            oneShots = Src("oneshots", null, false, 1f, false);
            music = Src("music", null, true, 0f, false);
            lastPos = player.transform.position;
        }

        AudioSource Voice(string n, ProcVoice.Kind kind, float vol, bool spatial, int seed)
        {
            var go = new GameObject(n); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>(); s.clip = silence; s.loop = true; s.volume = vol; s.spatialBlend = spatial ? 1 : 0; s.playOnAwake = false;
            var v = go.AddComponent<ProcVoice>(); v.kind = kind; v.seed = seed;
            s.Play(); return s;
        }

        AudioSource Src(string n, AudioClip c, bool loop, float vol, bool spatial)
        {
            var go = new GameObject(n); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>(); s.clip = c; s.loop = loop; s.volume = vol; s.spatialBlend = spatial ? 1 : 0; s.playOnAwake = false;
            if (c && loop) s.Play();
            return s;
        }

        public void SetLevel(GameObject levelRoot)
        {
            lights = levelRoot ? levelRoot.GetComponentsInChildren<Light>(true).Where(l => l.type != LightType.Directional).ToList() : new List<Light>();
            aesthetic = levelRoot && levelRoot.name.StartsWith("Level ") && levelRoot.name.Length > 8 ? levelRoot.name.Substring(6, 2) : "BR";
            // a quiet downloaded drone under everything: darker in the frightening places, none in the open park
            bool darkPlace = aesthetic == "LO" || aesthetic == "CV" || aesthetic == "RN" || aesthetic == "EL" || aesthetic == "LB" || aesthetic == "PD";
            var wd = Address.Mods.ModSystem.World(aesthetic); if (wd != null) darkPlace = wd.music == "dark";
            var clip = aesthetic == "FA" ? null : Resources.Load<AudioClip>(darkPlace ? "Ambience/amb_dark" : "Ambience/amb_calm");
            if (clip && music.clip != clip) { music.clip = clip; music.time = Random.Range(0, clip.length * 0.7f); music.Play(); }
            else if (!clip) music.Stop();
            musicVol = darkPlace ? 0.2f : 0.12f;
            if (toneVoice) toneVoice.kind = aesthetic == "FA" ? ProcVoice.Kind.Wind : ProcVoice.Kind.Air;
            // the Poolrooms: a real empty pool hall under everything, started at a random point
            if (aesthetic == "PR" && kit.poolHall) { place.clip = kit.poolHall; place.volume = 0.35f; place.time = Random.Range(0, kit.poolHall.length * 0.8f); place.Play(); }
            else place.Stop();
            eventTimer = Random.Range(18f, 40f);
        }

        public void Play(AudioClip c, float vol = 1f) { if (c) oneShots.PlayOneShot(c, vol); }

        public void PlayAt(AudioClip c, Vector3 p, float vol = 1f) { if (c) AudioSource.PlayClipAtPoint(c, p, vol); }

        float musicVol;
        void Update()
        {
            if (!fpc) return;
            bool amb = Settings.Ambience;
            if (music) music.volume = Mathf.MoveTowards(music.volume, amb ? musicVol : 0f, Time.deltaTime * 0.2f);
            if (tone) tone.mute = !amb;
            var p = fpc.transform.position;
            // hum follows the nearest lit fixtures (none in the open-air park)
            timer -= Time.deltaTime;
            if (timer <= 0)
            {
                timer = 0.4f;
                var near = aesthetic == "FA" ? new List<Light>() : lights.Where(l => l && l.enabled && l.intensity > 0.3f).OrderBy(l => (l.transform.position - p).sqrMagnitude).Take(humPool.Count).ToList();
                for (int i = 0; i < humPool.Count; i++)
                {
                    if (i < near.Count) { humPool[i].transform.position = near[i].transform.position; humPool[i].mute = !Settings.Ambience; humPool[i].volume = aesthetic == "PR" ? 0.12f : 0.22f; }
                    else humPool[i].mute = true;
                }
            }
            // footsteps: no immediate repeats, pitch/level variation, tiles sound harder than carpet
            var d = p - lastPos; d.y = 0; lastPos = p;
            if (fpc.Grounded && d.magnitude < 1f) distAcc += d.magnitude;
            bool running = fpc.Velocity.magnitude > 2.5f;
            float stride = running ? 0.85f : 0.62f;
            if (distAcc > stride && kit.steps != null && kit.steps.Length > 0)
            {
                distAcc = 0;
                int i; do i = Random.Range(0, kit.steps.Length); while (kit.steps.Length > 1 && i == lastStep); lastStep = i;
                oneShots.pitch = (aesthetic == "BR" ? 1f : 1.35f) * Random.Range(0.9f, 1.1f);
                oneShots.PlayOneShot(kit.steps[i], (running ? 0.5f : 0.28f) * Random.Range(0.8f, 1.1f));
            }
            // breathing only after a run, and it fades away
            runT = running ? Mathf.Min(runT + Time.deltaTime, 8f) : Mathf.Max(0, runT - Time.deltaTime * 0.5f);
            breath.volume = Mathf.MoveTowards(breath.volume, Mathf.Clamp01((runT - 2f) / 6f) * 0.14f, Time.deltaTime * 0.05f);
            if (breath.clip && breath.volume > 0.001f && !breath.isPlaying) breath.Play();
            // something far away
            eventTimer -= Time.deltaTime;
            if (eventTimer <= 0)
            {
                eventTimer = Random.Range(28f, 75f) * (aesthetic == "FA" ? 2f : 1f);
                var dir = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward;
                var at = p + dir * Random.Range(14f, 26f) + Vector3.up * 1.5f;
                var go = new GameObject("distant"); go.transform.position = at;
                var s = go.AddComponent<AudioSource>(); s.spatialBlend = 1; s.minDistance = 4f; s.maxDistance = 40f; s.rolloffMode = AudioRolloffMode.Linear;
                s.pitch = Random.Range(0.75f, 1.05f);
                var lp = go.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = Random.Range(900f, 2200f);
                float r = Random.value;
                s.clip = r < 0.35f ? Address.Interact.Sfx.DistantThud : r < 0.65f && kit.doorReal ? kit.doorReal : Address.Interact.Sfx.MetalTick;
                s.volume = Random.Range(0.35f, 0.7f); s.Play(); Destroy(go, s.clip.length / s.pitch + 0.5f);
            }
        }
    }

    /// <summary>Endless generated sound (audio thread): ballast hum, room air, open-air wind.</summary>
    public class ProcVoice : MonoBehaviour
    {
        public enum Kind { Hum, Air, Wind }
        public Kind kind; public int seed;
        double ph1, ph2, ph3; float lp, lp2, env = 0.5f, envT = 0.5f, crack; uint rng = 1;
        int rate;
        void Awake() { rate = AudioSettings.outputSampleRate; rng = (uint)(seed * 7919 + 17); }
        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777215f; }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (rate == 0) rate = 48000;
            double f = 120.0 * (1 + (seed % 7 - 3) * 0.0015);
            for (int i = 0; i < data.Length; i += channels)
            {
                float v;
                if (Rand() < 0.0001f) envT = 0.35f + Rand() * 0.65f;            // slow, random swells
                env += (envT - env) * 0.00002f;
                switch (kind)
                {
                    case Kind.Hum:
                        ph1 += f / rate; ph2 += 2 * f / rate; ph3 += 3 * f / rate;
                        v = (float)(System.Math.Sin(ph1 * 6.2832) * 0.5 + System.Math.Sin(ph2 * 6.2832) * 0.3 + System.Math.Sin(ph3 * 6.2832) * 0.12);
                        float buzz = (Rand() * 2 - 1) * 0.06f; lp += (buzz - lp) * 0.3f;
                        if (Rand() < 0.00003f) crack = 1f;                           // a ballast tick now and then
                        crack *= 0.995f;
                        v = (v * 0.11f + lp) * env + (Rand() * 2 - 1) * crack * 0.25f;
                        break;
                    case Kind.Wind:
                        lp += ((Rand() * 2 - 1) - lp) * 0.02f; lp2 += (lp - lp2) * 0.05f;
                        v = lp2 * 0.5f * (0.4f + env);
                        break;
                    default:
                        lp += ((Rand() * 2 - 1) - lp) * 0.01f; lp2 += (lp - lp2) * 0.02f;   // brown-ish, very low
                        v = lp2 * 0.35f * env;
                        break;
                }
                for (int c = 0; c < channels; c++) data[i + c] = v;
            }
        }
    }
}
