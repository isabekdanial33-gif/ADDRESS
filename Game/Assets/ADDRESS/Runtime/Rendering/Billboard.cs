using UnityEngine;

namespace Address.Rendering
{
    /// <summary>Turns a far sky card (cloud) to the camera, upright, so its flat edge is never seen.</summary>
    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var c = Camera.main ? Camera.main : null; if (!c) { var cams = Camera.allCameras; if (cams.Length == 0) return; c = cams[0]; }
            var d = transform.position - c.transform.position; d.y = 0;
            if (d.sqrMagnitude > 1) transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }
    }
}
