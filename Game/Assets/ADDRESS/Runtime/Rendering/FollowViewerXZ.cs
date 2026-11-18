using UnityEngine;

namespace Address.Rendering
{
    /// <summary>Sky dressing that travels with the player (clouds in an endless world never fall behind).</summary>
    public class FollowViewerXZ : MonoBehaviour
    {
        Vector3 origin; bool init;
        void LateUpdate()
        {
            var c = Camera.main; if (!c) return;
            if (!init) { origin = transform.position; init = true; }
            var p = c.transform.position; transform.position = origin + new Vector3(p.x, 0, p.z);
        }
    }
}
