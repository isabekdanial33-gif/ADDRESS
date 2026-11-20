using UnityEngine;

namespace Address.Entities
{
    /// <summary>
    /// Some downloaded rigs only show their real size once an animation drives them (scaled sub-nodes, bind poses far
    /// from the skeleton). A few frames after spawning, the body is measured as it actually is on screen and, if it is
    /// clearly off, scaled to the intended height and set back on its feet over the root.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class FitHeight : MonoBehaviour
    {
        public float height = 2f;   // metres, set by the import tool
        public float extra = 1f;    // per-Echo growth
        int frame;

        void LateUpdate()
        {
            frame++;
            if (frame != 3 && frame != 12) { if (frame > 12) enabled = false; return; }
            if (!Measure(out var b)) return;
            float want = height * extra;
            float k = want / Mathf.Max(0.01f, b.size.y);
            if (k < 0.9f || k > 1.1f)
            {
                transform.localScale *= k;
                if (!Measure(out b)) return;
                Debug.Log($"[ADDRESS] fit {name}: x{k:F3} -> {b.size.y:F2} m");
            }
            var root = transform.parent ? transform.parent.position : transform.position;
            var off = new Vector3(b.center.x - root.x, b.min.y - root.y, b.center.z - root.z);
            if (new Vector2(off.x, off.z).magnitude > 0.5f || Mathf.Abs(off.y) > 0.25f) transform.position -= off;
        }

        bool Measure(out Bounds b)
        {
            b = default;
            Renderer best = null; int bv = -1;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                int v = r is SkinnedMeshRenderer s ? (s.sharedMesh ? s.sharedMesh.vertexCount : 0) : r.GetComponent<MeshFilter>() is MeshFilter f && f.sharedMesh ? f.sharedMesh.vertexCount : 0;
                if (v > bv) { bv = v; best = r; }
            }
            if (!best) return false;
            if (best is SkinnedMeshRenderer sk && sk.sharedMesh)
            {
                // the posed body exactly as it is drawn now
                var mesh = new Mesh(); sk.BakeMesh(mesh, true);
                var vs = mesh.vertices; var tr = sk.transform;
                b = new Bounds(tr.TransformPoint(vs[0]), Vector3.zero);
                for (int i = 0; i < vs.Length; i += 3) b.Encapsulate(tr.TransformPoint(vs[i]));
                Destroy(mesh);
            }
            else b = best.bounds;
            return b.size.y > 0.01f;
        }
    }
}
