using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Address.Rendering
{
    /// <summary>
    /// Runtime cost control for procedural levels (no baked occlusion is possible):
    ///  - lights: only fixtures within range are enabled; only the N nearest cast shadows (per quality tier)
    ///  - props: renderers beyond a distance are disabled (fog hides the cut), in time-sliced batches
    ///  - architecture: static-batched once after the level is built
    /// </summary>
    public class LevelOptimizer : MonoBehaviour
    {
        public Transform viewer;
        struct Tier { public float lightRange, propRange; public int shadows; }
        static readonly Tier[] Tiers =
        {
            new Tier { lightRange = 11f, propRange = 16f, shadows = 0 },   // Low
            new Tier { lightRange = 14f, propRange = 22f, shadows = 1 },   // Medium
            new Tier { lightRange = 18f, propRange = 28f, shadows = 2 },   // High
            new Tier { lightRange = 24f, propRange = 36f, shadows = 4 },   // Ultra
        };

        Light[] lights; Renderer[] fixtureRenderers; Renderer[] props;
        int propCursor; float lightTimer;

        public void Init(GameObject levelRoot, Transform view)
        {
            viewer = view;
            lights = levelRoot.GetComponentsInChildren<Light>(true);
            var props = levelRoot.transform.Find("Props");
            this.props = props ? props.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            var arch = levelRoot.transform.Find("Architecture");
            if (arch) StaticBatchingUtility.Combine(arch.gameObject);
            Tick(true);
        }

        Tier Current => Tiers[Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, Tiers.Length - 1)];

        void Update() => Tick(false);

        public void Tick(bool full)
        {
            if (viewer == null || lights == null) return;
            var t = Current; var p = viewer.position;
            lightTimer -= Time.unscaledDeltaTime;
            if (full || lightTimer <= 0)
            {
                lightTimer = 0.2f;
                float r2 = t.lightRange * t.lightRange;
                var near = new List<(Light l, float d)>();
                foreach (var l in lights)
                {
                    if (!l || l.type == LightType.Directional) continue;   // the sun is always on
                    float d = (l.transform.position - p).sqrMagnitude;
                    bool on = d < r2 && !IsEchoOff(l) && !Address.Interact.LightSwitch.SwitchedOff.Contains(l);
                    if (l.enabled != on) l.enabled = on;
                    if (l.enabled) near.Add((l, d));
                }
                near.Sort((a, b) => a.d.CompareTo(b.d));
                for (int i = 0; i < near.Count; i++)
                {
                    var want = i < t.shadows ? LightShadows.Soft : LightShadows.None;
                    if (near[i].l.shadows != want) near[i].l.shadows = want;
                }
            }
            // props: time-sliced distance culling (all of them on a full tick)
            if (props.Length == 0) return;
            float pr2 = t.propRange * t.propRange;
            int n = full ? props.Length : Mathf.Max(64, props.Length / 8);
            for (int k = 0; k < n; k++)
            {
                propCursor = (propCursor + 1) % props.Length;
                var r = props[propCursor]; if (!r) continue;
                bool vis = (r.bounds.center - p).sqrMagnitude < pr2;
                if (r.enabled != vis) r.enabled = vis;
            }
        }

        // lights the Echo turned off stay off (their fixture renderers are disabled)
        static bool IsEchoOff(Light l)
        {
            var parent = l.transform.parent;
            if (!parent) return false;
            var rs = parent.GetComponentsInChildren<Renderer>();
            return rs.Length > 0 && rs.All(r => !r.enabled);
        }
    }
}
