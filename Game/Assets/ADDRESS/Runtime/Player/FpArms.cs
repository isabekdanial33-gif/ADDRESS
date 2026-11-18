using UnityEngine;

namespace Address.Player
{
    /// <summary>
    /// First-person arms (forearm in the suit sleeve + glove, cut from the character model). They live under the
    /// camera, so they never stretch, never cross and never show the inside of the body. Idle they are lowered out
    /// of view; holding something, each hand grips its own side of the object; using something, the right hand
    /// reaches for it. The full body stays only for the shadow and for the co-op partner.
    /// </summary>
    public class FpArms : MonoBehaviour
    {
        public Camera cam; public GrabSystem grab; public FirstPersonController fpc;
        Transform left, right;
        float hold, reach, reachT, bob; Vector3 reachPoint;
        const float Forearm = 0.24f, HandMid = 0.08f;
        public float rollLeft = -90f, rollRight = 90f;     // palm orientation fix-up (degrees about the forearm)

        public void Init(GameObject leftPrefab, GameObject rightPrefab)
        {
            left = Make(leftPrefab, "FpArmLeft"); right = Make(rightPrefab, "FpArmRight");
        }

        Transform Make(GameObject prefab, string n)
        {
            if (!prefab) return null;
            var go = Instantiate(prefab, cam.transform); go.name = n;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = true; }
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            return go.transform;
        }

        /// <summary>Quick reach of the right hand towards a point (press E / pick up).</summary>
        public void Reach(Vector3 worldPoint) { reachPoint = worldPoint; reachT = 0.45f; }

        void LateUpdate()
        {
            if (!cam || (!left && !right)) return;
            float dt = Time.deltaTime;
            bool pushing = grab && grab.Pushing;
            bool holding = grab && (grab.Held || pushing);
            hold = Mathf.MoveTowards(hold, holding ? 1 : 0, dt * 4f);
            reachT -= dt; reach = Mathf.MoveTowards(reach, reachT > 0 ? 1 : 0, dt * (reachT > 0 ? 9f : 4f));
            float speed = fpc ? new Vector2(fpc.Velocity.x, fpc.Velocity.z).magnitude : 0;
            bob += dt * Mathf.Lerp(2f, 9f, Mathf.Clamp01(speed / 3.4f));
            var sway = new Vector3(Mathf.Sin(bob) * 0.01f, Mathf.Abs(Mathf.Cos(bob)) * 0.012f, 0) * Mathf.Clamp01(speed / 1.5f);

            // held object: the two grip points on its left and right faces (in camera space)
            Vector3 c = Vector3.zero; float half = 0.12f;
            if (pushing)
            {
                // both palms flat on the near face of the furniture, at chest height, shoulder-width apart
                var po = grab.Pushing; var rs0 = po.GetComponentsInChildren<Renderer>();
                var b = rs0[0].bounds; foreach (var r in rs0) b.Encapsulate(r.bounds);
                var eye = cam.transform.position; var aim = b.ClosestPoint(eye + cam.transform.forward * 0.6f);
                aim.y = Mathf.Clamp(eye.y - 0.45f, b.min.y + 0.1f, b.max.y - 0.03f);
                c = cam.transform.InverseTransformPoint(aim); c.z = Mathf.Clamp(c.z - 0.04f, 0.3f, 0.6f); half = 0.2f;
            }
            else if (holding)
            {
                var rs = grab.Held.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); c = cam.transform.InverseTransformPoint(b.center); var e = cam.transform.InverseTransformVector(b.extents); half = Mathf.Clamp(Mathf.Abs(e.x) * 0.9f + 0.02f, 0.06f, 0.32f); c.z -= Mathf.Abs(e.z) * 0.35f; }
            }
            Pose(left, -1, c, half, sway, rollLeft, 0);
            Pose(right, 1, c, half, sway, rollRight, reach);
        }

        void Pose(Transform arm, float side, Vector3 objC, float half, Vector3 sway, float roll, float reachK)
        {
            if (!arm) return;
            float k = Mathf.Max(hold, reachK);
            bool show = k > 0.02f;
            if (arm.gameObject.activeSelf != show) arm.gameObject.SetActive(show);
            if (!show) return;
            // where the hand should be (camera space)
            var idleHand = new Vector3(side * 0.26f, -0.75f, 0.25f);
            var holdHand = objC + new Vector3(side * half, -0.02f, 0);
            var reachHand = cam.transform.InverseTransformPoint(reachPoint);
            reachHand = Vector3.ClampMagnitude(reachHand, 0.55f); reachHand.x = Mathf.Max(reachHand.x, 0.05f);
            var hand = Vector3.Lerp(idleHand, holdHand, Ease(hold));
            if (reachK > 0) hand = Vector3.Lerp(hand, reachHand, Ease(reachK));
            hand += sway;
            // forearm comes from below/outside, like from a shoulder under the camera
            var shoulder = new Vector3(side * 0.26f, -0.62f, -0.02f);   // below the frame: the sleeve rises into view, its open end never faces the lens
            var dir = (hand - shoulder).normalized;
            var elbow = hand - dir * (Forearm + HandMid + 0.12f);   // the sleeve continues past the elbow
            arm.localPosition = elbow;
            // palm towards the body midline; thumbs up
            var up = Vector3.Cross(dir, new Vector3(-side, 0, 0)).normalized; if (up.y < 0) up = -up;
            arm.localRotation = Quaternion.LookRotation(dir, up) * Quaternion.Euler(0, 0, roll);
        }

        static float Ease(float x) => 1 - Mathf.Pow(1 - Mathf.Clamp01(x), 3);
    }
}
