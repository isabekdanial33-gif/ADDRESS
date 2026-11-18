using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Address.Player
{
    /// <summary>
    /// First-person arms made from the real character (a second copy of the body under the camera that draws only
    /// forearms, hands and fingers). Two-bone IK puts each hand on its own side of a held object (or flat on furniture
    /// you push), the palm turns towards the object and the fingers close around it. Idle the arms are lowered out of
    /// view. They never stretch, cross or show the inside of the body.
    /// </summary>
    public class FpArms : MonoBehaviour
    {
        public Camera cam; public GrabSystem grab; public FirstPersonController fpc;
        GameObject rig; Animator an; ProceduralBody pb;
        public GameObject Rig => rig;
        Transform lUA, lLA, lH, rUA, rLA, rH;
        readonly List<(Transform t, Quaternion rest, float side, int seg)> fingers = new();
        Quaternion lUA0, lLA0, lH0, rUA0, rLA0, rH0;
        Vector3 lPalm, rPalm; float upperLen, lowerLen;
        float hold, reach, reachT, bob, grip; Vector3 reachPoint; bool ready, shown = true;
        Renderer[] rigRenderers; readonly HashSet<Transform> thumbs = new();

        public void Init(GameObject bodyPrefab)
        {
            if (!bodyPrefab) return;
            rig = Instantiate(bodyPrefab, cam.transform); rig.name = "FpArmsRig";
            rig.transform.localPosition = new Vector3(0, -1.5f, -0.1f); rig.transform.localRotation = Quaternion.identity;
            foreach (var c in rig.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in rig.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pb = rig.GetComponent<ProceduralBody>();
            if (pb) { pb.firstPerson = true; pb.splitMode = ProceduralBody.SplitMode.Arms; pb.freeze = true; }
            an = rig.GetComponentInChildren<Animator>();
        }

        void Bind()
        {
            ready = true;
            if (!an || !an.isHuman) return;
            Transform B(HumanBodyBones b) => an.GetBoneTransform(b);
            lUA = B(HumanBodyBones.LeftUpperArm); lLA = B(HumanBodyBones.LeftLowerArm); lH = B(HumanBodyBones.LeftHand);
            rUA = B(HumanBodyBones.RightUpperArm); rLA = B(HumanBodyBones.RightLowerArm); rH = B(HumanBodyBones.RightHand);
            if (!lUA || !rUA) return;
            // the arms copy draws only its visible split: the full original (shadow copy) is not needed here
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!smr.name.EndsWith("_fp")) smr.enabled = false; else { smr.updateWhenOffscreen = false; smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            rigRenderers = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name.EndsWith("_fp")).ToArray();
            foreach (var tb in new[] { HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal, HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal }) { var t = B(tb); if (t) thumbs.Add(t); }
            lUA0 = lUA.localRotation; lLA0 = lLA.localRotation; lH0 = lH.localRotation; rUA0 = rUA.localRotation; rLA0 = rLA.localRotation; rH0 = rH.localRotation;
            upperLen = Vector3.Distance(lUA.position, lLA.position); lowerLen = Vector3.Distance(lLA.position, lH.position);
            var mid = (lUA.position + rUA.position) * 0.5f;
            lPalm = lH.InverseTransformDirection((mid - lH.position).normalized); rPalm = rH.InverseTransformDirection((mid - rH.position).normalized);
            for (var b = HumanBodyBones.LeftThumbProximal; b <= HumanBodyBones.RightLittleDistal; b++)
            {
                var t = B(b); if (!t) continue;
                int idx = (int)b - (int)HumanBodyBones.LeftThumbProximal;
                fingers.Add((t, t.localRotation, idx < 15 ? -1 : 1, idx % 3));
            }
        }

        public void Reach(Vector3 worldPoint) { reachPoint = worldPoint; reachT = 0.45f; }

        void LateUpdate()
        {
            if (!rig || !cam) return;
            if (!ready) { if (pb && Time.frameCount > 2) Bind(); return; }
            if (!lUA) return;
            float dt = Time.deltaTime;
            bool pushing = grab && grab.Pushing, holding = grab && (grab.Held || pushing);
            hold = Mathf.MoveTowards(hold, holding ? 1 : 0, dt * 4f);
            reachT -= dt; reach = Mathf.MoveTowards(reach, reachT > 0 ? 1 : 0, dt * (reachT > 0 ? 9f : 4f));
            grip = Mathf.MoveTowards(grip, grab && grab.Held ? 1 : pushing ? 0.15f : 0, dt * 3f);
            bool show = hold > 0.02f || reach > 0.02f;
            if (show != shown && rigRenderers != null) { shown = show; foreach (var r in rigRenderers) r.enabled = show; }
            if (!show) return;

            float speed = fpc ? new Vector2(fpc.Velocity.x, fpc.Velocity.z).magnitude : 0;
            bob += dt * Mathf.Lerp(2f, 9f, Mathf.Clamp01(speed / 3.4f));
            var sway = new Vector3(Mathf.Sin(bob) * 0.01f, Mathf.Abs(Mathf.Cos(bob)) * 0.012f, 0) * Mathf.Clamp01(speed / 1.5f);

            // shoulders sit below and slightly behind the eyes
            lUA.localRotation = lUA0; lLA.localRotation = lLA0; lH.localRotation = lH0; rUA.localRotation = rUA0; rLA.localRotation = rLA0; rH.localRotation = rH0;
            var mid = (lUA.position + rUA.position) * 0.5f;
            rig.transform.position += cam.transform.TransformPoint(new Vector3(0, -0.27f, -0.14f)) - mid;

            // targets: the two sides of the held object / the face of what you push / the thing you use
            Vector3 c = new Vector3(0, -0.25f, 0.45f); float half = 0.14f; Vector3 objW = cam.transform.TransformPoint(c);
            if (pushing)
            {
                var rs = grab.Pushing.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                var eye = cam.transform.position; var aim = b.ClosestPoint(eye + cam.transform.forward * 0.6f);
                aim.y = Mathf.Clamp(eye.y - 0.45f, b.min.y + 0.1f, b.max.y - 0.03f);
                c = cam.transform.InverseTransformPoint(aim); c.z = Mathf.Clamp(c.z - 0.03f, 0.3f, 0.55f); half = 0.19f; objW = aim + cam.transform.forward * 0.3f;
            }
            else if (grab && grab.Held)
            {
                var rs = grab.Held.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    objW = b.center; c = cam.transform.InverseTransformPoint(b.center);
                    var e = cam.transform.InverseTransformVector(b.extents);
                    half = Mathf.Clamp(Mathf.Abs(e.x) + 0.015f, 0.05f, 0.3f); c.z -= Mathf.Abs(e.z) * 0.3f;
                }
            }
            Solve(lUA, lLA, lH, lPalm, -1, c, half, sway, objW, 0);
            Solve(rUA, rLA, rH, rPalm, 1, c, half, sway, objW, reach);
            // fingers close around the object
            foreach (var (t, rest, side, seg) in fingers)
            {
                bool thumb = thumbs.Contains(t);
                t.localRotation = rest;
                var hand = side < 0 ? lH : rH; var palm = hand.TransformDirection(side < 0 ? lPalm : rPalm);
                var child = t.childCount > 0 ? t.GetChild(0) : null; var dir = child ? (child.position - t.position).normalized : hand.forward;
                var axis = Vector3.Cross(dir, palm); if (axis.sqrMagnitude < 1e-6f) continue;
                float ang = grip * (thumb ? 25f : seg == 0 ? 55f : 60f);
                t.rotation = Quaternion.AngleAxis(ang, axis.normalized) * t.rotation;
            }
        }

        void Solve(Transform ua, Transform la, Transform hand, Vector3 palmLocal, float side, Vector3 c, float half, Vector3 sway, Vector3 objW, float reachK)
        {
            var idle = new Vector3(side * 0.24f, -0.75f, 0.2f);
            var holdP = c + new Vector3(side * half, -0.02f, 0);
            var reachP = Vector3.ClampMagnitude(cam.transform.InverseTransformPoint(reachPoint), 0.6f);
            var target = Vector3.Lerp(idle, holdP, Ease(hold)); if (reachK > 0 && side > 0) target = Vector3.Lerp(target, reachP, Ease(reachK));
            var tw = cam.transform.TransformPoint(target + sway);
            // two-bone IK (law of cosines), elbow pointing down and out
            var s = ua.position; var to = tw - s; float d = Mathf.Clamp(to.magnitude, 0.05f, (upperLen + lowerLen) * 0.995f);
            float cosA = Mathf.Clamp((upperLen * upperLen + d * d - lowerLen * lowerLen) / (2 * upperLen * d), -1, 1);
            var dir = to.normalized;
            var pole = Vector3.ProjectOnPlane(-cam.transform.up + cam.transform.right * side * 0.6f, dir).normalized;
            var elbow = s + Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, Vector3.Cross(dir, pole).normalized) * dir * upperLen;
            ua.rotation = Quaternion.FromToRotation(la.position - ua.position, elbow - ua.position) * ua.rotation;
            la.rotation = Quaternion.FromToRotation(hand.position - la.position, s + dir * d - la.position) * la.rotation;
            // palm towards the object (twist the forearm, clamped), then the hand lines up with the forearm
            var axis = (hand.position - la.position).normalized;
            var want = Vector3.ProjectOnPlane(objW - hand.position, axis);
            var cur = Vector3.ProjectOnPlane(hand.TransformDirection(palmLocal), axis);
            if (want.sqrMagnitude > 1e-5f && cur.sqrMagnitude > 1e-5f)
                la.rotation = Quaternion.AngleAxis(Mathf.Clamp(Vector3.SignedAngle(cur, want, axis), -110f, 110f), axis) * la.rotation;
        }

        static float Ease(float x) => 1 - Mathf.Pow(1 - Mathf.Clamp01(x), 3);
    }
}
