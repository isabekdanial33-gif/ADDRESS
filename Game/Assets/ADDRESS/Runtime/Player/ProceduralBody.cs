using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Address.Player
{
    /// <summary>
    /// Procedural animation for the hazmat body (no Animator): walk/run cycle, jump/fall pose, crouch, breathing,
    /// arms reaching for a held object. All swings are applied in BODY space (about the body's right axis), so they do
    /// not depend on the bone roll the FBX importer produced.
    ///
    /// First person: the torso, neck, head, hood and mask are split off into a shadow-only copy, so looking down shows
    /// the legs, boots, arms and gloves instead of a giant chest in front of the lens.
    /// </summary>
    public class ProceduralBody : MonoBehaviour
    {
        public FirstPersonController fpc;
        public GrabSystem grab;
        public bool firstPerson = true;
        public bool freeze;                 // hold the current pose (an entity being looked at stops mid-step)
        Transform hips, spine, chest, head, lUL, rUL, lLL, rLL, lFoot, rFoot, lUA, rUA, lLA, rLA, lHand, rHand;
        Quaternion hips0, spine0, chest0, lUL0, rUL0, lLL0, rLL0, lFoot0, rFoot0, lUA0, rUA0, lLA0, rLA0, lHand0, rHand0;
        float hipsRestH = 0.95f, sit;
        Vector3 hipsPos0, lPalm = Vector3.right, rPalm = Vector3.left;
        float phase, lastSpeed, air, reach, crouchAmt, landKick; Vector3 lastPos; bool split, wasGrounded = true;

        Vector3 base0; bool baseSet;
        void Start() { Bind(); if (firstPerson) ApplyFirstPersonOffset(); }

        /// <summary>In first person the eyes sit in front of the collar: shift the body back under the camera.
        /// Looking down slides it further back, so the view shows the belly and the boots instead of the chest from above.</summary>
        public void ApplyFirstPersonOffset() { if (!baseSet) { base0 = transform.localPosition; baseSet = true; } FpOffset(0); }
        void FpOffset(float pitch)
        {
            if (fpc && fpc.Seated) { transform.localPosition = base0 - (transform.parent ? transform.parent.InverseTransformDirection(transform.forward) : transform.forward) * 0.12f; return; }
            float back = Mathf.Lerp(0.16f + 0.20f * Mathf.Clamp01((pitch - 15f) / 55f), 0.04f, reach);
            transform.localPosition = base0 - (transform.parent ? transform.parent.InverseTransformDirection(transform.forward) : transform.forward) * back;
        }

        public void Bind()
        {
            var all = GetComponentsInChildren<Transform>(true);
            Transform F(string n) { foreach (var t in all) if (t.name == n) return t; return null; }
            hips = F("Hips"); spine = F("Spine"); chest = F("Chest"); head = F("Head");
            lUL = F("LeftUpperLeg"); rUL = F("RightUpperLeg"); lLL = F("LeftLowerLeg"); rLL = F("RightLowerLeg"); lFoot = F("LeftFoot"); rFoot = F("RightFoot");
            lUA = F("LeftUpperArm"); rUA = F("RightUpperArm"); lLA = F("LeftLowerArm"); rLA = F("RightLowerArm"); lHand = F("LeftHand"); rHand = F("RightHand");
            Quaternion R(Transform t) => t ? t.localRotation : Quaternion.identity;
            hips0 = R(hips); spine0 = R(spine); chest0 = R(chest); lUL0 = R(lUL); rUL0 = R(rUL); lLL0 = R(lLL); rLL0 = R(rLL); lFoot0 = R(lFoot); rFoot0 = R(rFoot);
            lUA0 = R(lUA); rUA0 = R(rUA); lLA0 = R(lLA); rLA0 = R(rLA); lHand0 = R(lHand); rHand0 = R(rHand);
            if (hips) { hipsPos0 = hips.localPosition; hipsRestH = hips.position.y - transform.position.y; }
            // palms face the thighs in the rest pose: remember that direction in each hand's own space
            if (lHand && hips) { var d = hips.position - lHand.position; d.y = 0; lPalm = lHand.InverseTransformDirection(d.normalized); }
            if (rHand && hips) { var d = hips.position - rHand.position; d.y = 0; rPalm = rHand.InverseTransformDirection(d.normalized); }
            if (firstPerson && !split) SplitForFirstPerson();
        }

        // ------------------------------------------------------------------ first-person mesh split

        // first person: only hips, legs and boots are drawn - arms come from FpArms, the torso would fill the lens
        static readonly string[] HiddenBones = { "Spine", "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "RightShoulder", "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand" };

        void SplitForFirstPerson()
        {
            split = true;
            foreach (var mr in GetComponentsInChildren<MeshRenderer>(true)) mr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;   // mask, filters
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.name.Contains("hood") || smr.name.Contains("mask") || smr.name.Contains("seal"))
                { smr.shadowCastingMode = ShadowCastingMode.ShadowsOnly; continue; }
                var mesh = smr.sharedMesh; if (!mesh || !mesh.isReadable) { continue; }
                var bones = smr.bones; var hidden = new bool[bones.Length];
                for (int i = 0; i < bones.Length; i++) hidden[i] = bones[i] && System.Array.IndexOf(HiddenBones, bones[i].name) >= 0;
                var bw = mesh.boneWeights; if (bw.Length != mesh.vertexCount) continue;
                // a vertex is "torso" if most of its weight is on torso bones
                var torso = new bool[bw.Length];
                for (int v = 0; v < bw.Length; v++)
                {
                    var w = bw[v]; float t = 0;
                    if (w.boneIndex0 < hidden.Length && hidden[w.boneIndex0]) t += w.weight0;
                    if (w.boneIndex1 < hidden.Length && hidden[w.boneIndex1]) t += w.weight1;
                    if (w.boneIndex2 < hidden.Length && hidden[w.boneIndex2]) t += w.weight2;
                    if (w.boneIndex3 < hidden.Length && hidden[w.boneIndex3]) t += w.weight3;
                    torso[v] = t > 0.35f;
                }
                var visible = Object.Instantiate(mesh); visible.name = mesh.name + "_fp";
                int keptAny = 0;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var tris = mesh.GetTriangles(sm); var keep = new List<int>(tris.Length);
                    for (int i = 0; i < tris.Length; i += 3)
                        if (!torso[tris[i]] && !torso[tris[i + 1]] && !torso[tris[i + 2]]) { keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]); }
                    visible.SetTriangles(keep, sm); keptAny += keep.Count;
                }
                // full mesh keeps casting the complete shadow; the visible part draws without its own shadow
                smr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                if (keptAny == 0) continue;
                var go = new GameObject(smr.name + "_fp"); go.layer = smr.gameObject.layer;
                go.transform.SetParent(smr.transform.parent, false);
                go.transform.localPosition = smr.transform.localPosition; go.transform.localRotation = smr.transform.localRotation; go.transform.localScale = smr.transform.localScale;
                var v2 = go.AddComponent<SkinnedMeshRenderer>();
                v2.sharedMesh = visible; v2.bones = bones; v2.rootBone = smr.rootBone; v2.sharedMaterials = smr.sharedMaterials;
                v2.shadowCastingMode = ShadowCastingMode.Off; v2.updateWhenOffscreen = true; v2.localBounds = smr.localBounds;
            }
        }

        // ------------------------------------------------------------------ pose

        void LateUpdate() => Pose(Time.deltaTime);

        /// <summary>Rotate a bone about a BODY-space axis, on top of its rest pose under the current parent.</summary>
        void Swing(Transform b, Quaternion rest, Vector3 bodyAxis, float deg)
        {
            if (!b) return;
            b.localRotation = rest;
            if (deg != 0) b.rotation = Quaternion.AngleAxis(deg, transform.TransformDirection(bodyAxis)) * b.rotation;
        }

        /// <summary>Turn a limb bone so that it points from its origin towards a world target (keeps its twist).</summary>
        static void Aim(Transform b, Transform child, Vector3 target, float weight)
        {
            if (!b || !child || weight <= 0) return;
            var cur = child.position - b.position; var want = target - b.position;
            if (cur.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) return;
            b.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, want), weight) * b.rotation;
        }

        public void Pose(float dt)
        {
            if (dt <= 0 || freeze) { lastPos = transform.position; return; }
            float speed; bool grounded = true; float vy = 0;
            if (fpc) { speed = new Vector2(fpc.Velocity.x, fpc.Velocity.z).magnitude; grounded = fpc.Grounded; vy = fpc.VerticalSpeed; }
            else
            {
                var d = transform.position - lastPos; float dy = d.y; d.y = 0;
                speed = Mathf.Lerp(lastSpeed, d.magnitude / dt, 0.2f); lastSpeed = speed;
                vy = dy / dt; grounded = Mathf.Abs(vy) < 0.6f;
            }
            lastPos = transform.position;
            if (firstPerson && fpc && baseSet) FpOffset(fpc.Pitch);

            air = Mathf.MoveTowards(air, grounded ? 0 : 1, dt * (grounded ? 8f : 5f));
            if (grounded && !wasGrounded) landKick = 1f;
            wasGrounded = grounded; landKick = Mathf.MoveTowards(landKick, 0, dt * 4f);
            sit = Mathf.MoveTowards(sit, fpc && fpc.Seated ? 1 : 0, dt * 4f);
            float targetCrouch = fpc ? Mathf.InverseLerp(fpc.standHeight, fpc.crouchHeight, GetComponentInParent<CharacterController>()?.height ?? fpc.standHeight) : 0;
            crouchAmt = Mathf.MoveTowards(crouchAmt, targetCrouch, dt * 4f);

            // gait: stride frequency grows with speed, amplitude saturates (walk 1.5, run 3.4)
            float amt = Mathf.Clamp01(speed / 1.4f) * (1 - air);
            float run = Mathf.Clamp01((speed - 1.8f) / 1.4f);
            phase += dt * Mathf.Lerp(5.2f, 8.6f, run) * Mathf.Clamp01(speed / 0.6f);
            float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
            float thigh = Mathf.Lerp(24f, 38f, run) * amt, knee = Mathf.Lerp(38f, 75f, run) * amt, arm = Mathf.Lerp(16f, 34f, run) * amt;
            Vector3 X = Vector3.right; // body right axis: positive angle tips a hanging limb BACKWARDS

            // hips: bounce, sway, crouch drop
            if (hips)
            {
                hips.localRotation = hips0;
                hips.rotation = Quaternion.AngleAxis(s * 5f * amt, transform.up) * hips.rotation;
                float bounce = Mathf.Abs(c) * 0.025f * amt;
                hips.localPosition = hipsPos0;
                hips.position += transform.up * (bounce - crouchAmt * 0.38f - landKick * 0.06f);
                if (sit > 0 && fpc) hips.position += transform.up * sit * ((fpc.SeatHeight + 0.06f) - hipsRestH) + transform.forward * sit * 0.08f;
            }
            if (spine) { spine.localRotation = spine0; spine.rotation = Quaternion.AngleAxis(-(4f * run * amt + crouchAmt * 18f), transform.TransformDirection(X)) * spine.rotation; }
            if (chest) Swing(chest, chest0, X, -Mathf.Sin(Time.time * 1.3f) * 1.2f);

            // legs: thigh forward = negative, knee always bends backwards (positive), crouch folds both
            float airTuck = air * 35f;
            Swing(lUL, lUL0, X, -s * thigh - crouchAmt * 70f - airTuck - sit * 86f);
            Swing(rUL, rUL0, X, s * thigh - crouchAmt * 70f - airTuck * 0.6f - sit * 86f);
            // knee bends during the swing phase of each leg (when it moves forward) + crouch
            Swing(lLL, lLL0, X, Mathf.Max(0, c) * knee + Mathf.Max(0, -s) * knee * 0.25f + crouchAmt * 110f + air * 50f + sit * 84f);
            Swing(rLL, rLL0, X, Mathf.Max(0, -c) * knee + Mathf.Max(0, s) * knee * 0.25f + crouchAmt * 110f + air * 30f + sit * 84f);
            Swing(lFoot, lFoot0, X, -crouchAmt * 40f);
            Swing(rFoot, rFoot0, X, -crouchAmt * 40f);

            // arms: counter-swing, slightly bent; raised when falling
            Swing(lUA, lUA0, X, s * arm - air * 25f);
            Swing(rUA, rUA0, X, -s * arm - air * 25f);
            Swing(lLA, lLA0, X, -(10f + 14f * run) * Mathf.Max(amt, 0.3f));
            Swing(rLA, rLA0, X, -(10f + 14f * run) * Mathf.Max(amt, 0.3f));
            if (lHand) lHand.localRotation = lHand0; if (rHand) rHand.localRotation = rHand0;

            // holding something: both hands go to the object (simple two-bone aim)
            bool holding = grab && grab.Held && !firstPerson;   // in first person the FpArms hold things
            reach = Mathf.MoveTowards(reach, holding ? 1 : 0, dt * 5f);
            if (reach > 0.001f)
            {
                Vector3 obj = holding ? grab.Held.transform.position : (lHand ? lHand.position : transform.position);
                if (holding)
                {
                    var rends = grab.Held.GetComponentsInChildren<Renderer>();
                    if (rends.Length > 0)
                    {
                        var b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
                        // hands hold the near half of the object, on its left and right faces
                        var fwd = transform.forward; float depth = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(fwd.x), Mathf.Abs(fwd.y), Mathf.Abs(fwd.z))));
                        obj = b.center - fwd * depth * 0.5f;
                        float width = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(transform.right.x), 0, Mathf.Abs(transform.right.z))));
                        lastHalf = Mathf.Clamp(width, 0.06f, 0.3f);
                    }
                }
                var side = transform.right * lastHalf;
                AimArm(lUA, lLA, lHand, obj - side, reach);
                AimArm(rUA, rLA, rHand, obj + side, reach);
                Palm(lLA, lHand, lPalm, transform.right, reach);
                Palm(rLA, rHand, rPalm, -transform.right, reach);
            }
        }
        float lastHalf = 0.15f;

        /// <summary>Twist the forearm about its own axis so the palm faces the wanted direction (towards the held object).</summary>
        static void Palm(Transform lower, Transform hand, Vector3 palmLocal, Vector3 want, float w)
        {
            if (!lower || !hand) return;
            var axis = (hand.position - lower.position).normalized;
            var cur = Vector3.ProjectOnPlane(hand.TransformDirection(palmLocal), axis); var tgt = Vector3.ProjectOnPlane(want, axis);
            if (cur.sqrMagnitude < 1e-5f || tgt.sqrMagnitude < 1e-5f) return;
            float ang = Mathf.Clamp(Vector3.SignedAngle(cur, tgt, axis), -95f, 95f) * w;   // never wring the wrist
            lower.rotation = Quaternion.AngleAxis(ang, axis) * lower.rotation;
        }

        static void AimArm(Transform upper, Transform lower, Transform hand, Vector3 target, float w)
        {
            if (!upper || !lower || !hand) return;
            float l1 = Vector3.Distance(upper.position, lower.position), l2 = Vector3.Distance(lower.position, hand.position);
            var to = target - upper.position; float d = Mathf.Clamp(to.magnitude, 0.05f, (l1 + l2) * 0.98f);
            // law of cosines: angle between upper arm and the shoulder->target line; bend the elbow down/out
            float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1);
            float a = Mathf.Acos(cosA) * Mathf.Rad2Deg;
            var dir = to.normalized;
            // elbow bends down and slightly outwards (stable: never flips when the target crosses the body)
            var outward = Vector3.ProjectOnPlane(upper.position - (upper.parent ? upper.parent.position : upper.position), dir);
            var down = Vector3.ProjectOnPlane(Vector3.down + outward.normalized * 0.4f, dir);
            var bendAxis = Vector3.Cross(dir, down); if (bendAxis.sqrMagnitude < 1e-4f) bendAxis = Vector3.Cross(dir, Vector3.down);
            if (bendAxis.sqrMagnitude < 1e-4f) bendAxis = Vector3.right;
            var elbow = upper.position + Quaternion.AngleAxis(a, bendAxis.normalized) * dir * l1;
            Aim(upper, lower, elbow, w);
            Aim(lower, hand, upper.position + dir * d, w);
        }
    }
}
