using UnityEngine;

namespace Address.Rendering
{
    /// <summary>Plays the caustics flipbook through a spot light cookie (16 frames, ~14 fps, per-pool phase).</summary>
    public class CausticAnimator : MonoBehaviour
    {
        Texture[] frames; float phase; Light l;
        public void Setup(Texture[] f, float ph) { frames = f; phase = ph; l = GetComponent<Light>(); }
        void Update()
        {
            if (frames == null || frames.Length == 0 || !l) return;
            int i = (int)((Time.time + phase) * 14f) % frames.Length;
            if (l.cookie != frames[i]) l.cookie = frames[i];
        }
    }
}
