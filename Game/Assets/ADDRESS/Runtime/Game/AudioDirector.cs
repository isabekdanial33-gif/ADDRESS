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
    }

    /// <summary>
    /// Sound of the place: a pool of 3D hum sources glued to the nearest working fixtures, a quiet room tone,
    /// footsteps by distance walked, breathing inside the mask (faster when running).
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        public AudioKit kit;
        public FirstPersonController fpc;
        readonly List<AudioSource> humPool = new();
        AudioSource tone, breath, oneShots;
        List<Light> lights = new();
        float distAcc, timer;
        Vector3 lastPos;

        public void Init(AudioKit k, FirstPersonController player)
        {
            kit = k; fpc = player;
            for (int i = 0; i < 6; i++)
            {
                var s = Src($"hum{i}", kit.hum, true, 0.28f, true);
                s.minDistance = 0.8f; s.maxDistance = 9f; s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.pitch = 1f + (i - 3) * 0.004f;   // slightly detuned ballasts beat against each other
                s.time = Random.Range(0f, kit.hum ? kit.hum.length : 0f);
                humPool.Add(s);
            }
            tone = Src("roomtone", kit.roomTone, true, 0.22f, false);
            breath = Src("breath", kit.breath, true, 0.16f, false);
            oneShots = Src("oneshots", null, false, 1f, false);
            lastPos = player.transform.position;
        }

        AudioSource Src(string n, AudioClip c, bool loop, float vol, bool spatial)
        {
            var go = new GameObject(n); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>(); s.clip = c; s.loop = loop; s.volume = vol; s.spatialBlend = spatial ? 1 : 0; s.playOnAwake = false;
            if (c && loop) s.Play();
            return s;
        }

        public void SetLevel(GameObject levelRoot) => lights = levelRoot ? levelRoot.GetComponentsInChildren<Light>(true).ToList() : new List<Light>();

        public void Play(AudioClip c, float vol = 1f) { if (c) oneShots.PlayOneShot(c, vol); }

        public void PlayAt(AudioClip c, Vector3 p, float vol = 1f) { if (c) AudioSource.PlayClipAtPoint(c, p, vol); }

        void Update()
        {
            if (!fpc) return;
            var p = fpc.transform.position;
            // hum follows the nearest lit fixtures
            timer -= Time.deltaTime;
            if (timer <= 0)
            {
                timer = 0.4f;
                var near = lights.Where(l => l && l.enabled).OrderBy(l => (l.transform.position - p).sqrMagnitude).Take(humPool.Count).ToList();
                for (int i = 0; i < humPool.Count; i++)
                {
                    if (i < near.Count) { humPool[i].transform.position = near[i].transform.position; if (!humPool[i].isPlaying) humPool[i].Play(); }
                    else humPool[i].Stop();
                }
            }
            // footsteps by distance
            var d = p - lastPos; d.y = 0; lastPos = p;
            if (fpc.Grounded && d.magnitude < 1f) distAcc += d.magnitude;
            float stride = fpc.Velocity.magnitude > 2.5f ? 0.85f : 0.62f;
            if (distAcc > stride && kit.steps != null && kit.steps.Length > 0)
            {
                distAcc = 0;
                oneShots.PlayOneShot(kit.steps[Random.Range(0, kit.steps.Length)], fpc.Velocity.magnitude > 2.5f ? 0.55f : 0.32f);
            }
            // breathing speeds up after running
            float target = fpc.Velocity.magnitude > 2.5f ? 1.35f : 1f;
            breath.pitch = Mathf.MoveTowards(breath.pitch, target, Time.deltaTime * 0.08f);
        }
    }
}
