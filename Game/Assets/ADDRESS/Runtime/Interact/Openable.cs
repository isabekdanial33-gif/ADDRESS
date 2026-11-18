using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Address.Interact
{
    /// <summary>
    /// Opens a container with its own moving parts: box flaps swing out, a tub lid lifts back, a filing-cabinet drawer
    /// slides out, a tied bag's knot comes undone. Models without parts (scanned props) rock a little when searched.
    /// Hinge directions are found by trying both ways and keeping the one that lifts the part up and outward, so the
    /// glTF axis mirroring never matters.
    /// </summary>
    public class Openable : MonoBehaviour
    {
        readonly List<Transform> flaps = new(); Transform lid, knot, bag, tape; readonly List<Transform> drawers = new();
        public bool IsOpen { get; private set; }
        public int drawerIndex;
        public Vector3 LootPoint { get; private set; }

        public static Openable Attach(GameObject go, int seed)
        {
            var o = go.AddComponent<Openable>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("flap_")) o.flaps.Add(t);
                else if (t.name == "lid") o.lid = t;
                else if (t.name == "knot") o.knot = t;
                else if (t.name == "bag") o.bag = t;
                else if (t.name == "flaptape") o.tape = t;
                else if (t.name.StartsWith("drawer_")) o.drawers.Add(t);
            }
            o.drawers.Sort((a, b) => a.position.y.CompareTo(b.position.y));
            if (o.drawers.Count > 0) o.drawerIndex = Mathf.Abs(seed) % o.drawers.Count;
            return o;
        }

        public bool HasParts => flaps.Count > 0 || lid || knot || drawers.Count > 0;

        public string Verb => knot ? "развязать пакет" : drawers.Count > 0 ? "выдвинуть ящик" : flaps.Count > 0 ? "открыть коробку" : lid ? "снять крышку" : "осмотреть";

        public IEnumerator Open()
        {
            if (IsOpen) yield break;
            IsOpen = true;
            var bounds = Bounds();
            LootPoint = bounds.center + Vector3.up * bounds.extents.y * 0.2f;
            if (tape) tape.gameObject.SetActive(false);
            // decide each motion up-front
            var swings = new List<(Transform t, Quaternion from, Quaternion to)>();
            foreach (var f in flaps) swings.Add((f, f.localRotation, BestSwing(f, bounds.center, 115f)));
            if (lid) swings.Add((lid, lid.localRotation, BestSwing(lid, bounds.center, 100f)));
            Transform dr = drawers.Count > 0 ? drawers[drawerIndex] : null;
            Vector3 drFrom = dr ? dr.localPosition : Vector3.zero, drTo = drFrom;
            if (dr)
            {
                var fwd = transform.forward; var c = dr.GetComponent<Renderer>() ? dr.GetComponent<Renderer>().bounds.center : dr.position;
                // the drawer comes out of the face it is on (front = away from the cabinet's centre)
                var outDir = Vector3.Dot(fwd, c - bounds.center) >= 0 ? fwd : -fwd;
                drTo = drFrom + dr.parent.InverseTransformDirection(outDir) * 0.38f;
                LootPoint = c + outDir * 0.38f + Vector3.up * 0.08f;
            }
            Vector3 knot0 = knot ? knot.localScale : Vector3.one, bag0 = bag ? bag.localScale : Vector3.one;
            if (!HasParts) { yield return Rock(); yield break; }
            float T = dr ? 0.55f : 0.75f;
            for (float t = 0; t < T; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0, 1, t / T), kk = 1 - Mathf.Pow(1 - t / T, 3);
                foreach (var (tr, a, b) in swings) tr.localRotation = Quaternion.Slerp(a, b, kk);
                if (dr) dr.localPosition = Vector3.Lerp(drFrom, drTo, k);
                if (knot) knot.localScale = knot0 * (1 - k);
                if (bag) bag.localScale = Vector3.Scale(bag0, new Vector3(1 + 0.06f * Mathf.Sin(k * 9f) * (1 - k), 1 - 0.07f * k, 1 + 0.05f * k));
                yield return null;
            }
            if (knot) knot.gameObject.SetActive(false);
            if (bag) LootPoint = bounds.center + Vector3.up * (bounds.extents.y + 0.1f);
        }

        IEnumerator Rock()
        {
            var r0 = transform.rotation;
            for (float t = 0; t < 0.6f; t += Time.deltaTime) { transform.rotation = r0 * Quaternion.Euler(Mathf.Sin(t * 40f) * 2.5f * (1 - t / 0.6f), 0, 0); yield return null; }
            transform.rotation = r0;
        }

        /// <summary>The rotation (about the part's hinge edge) that lifts it up and away from the body.</summary>
        static Quaternion BestSwing(Transform part, Vector3 bodyCentre, float deg)
        {
            var r = part.GetComponent<Renderer>(); if (!r) return part.localRotation;
            var c0 = r.bounds.center; var hingeToC = c0 - part.position; hingeToC.y = 0;
            var axisW = Vector3.Cross(Vector3.up, hingeToC.sqrMagnitude > 1e-6f ? hingeToC.normalized : part.forward);
            Quaternion best = part.localRotation; float bestScore = float.MinValue;
            foreach (var s in new[] { 1f, -1f })
            {
                var q = Quaternion.AngleAxis(s * deg, axisW) * part.rotation;
                var localQ = Quaternion.Inverse(part.parent.rotation) * q;
                var newC = part.position + Quaternion.AngleAxis(s * deg, axisW) * (c0 - part.position);
                float score = newC.y + Vector3.Distance(new Vector3(newC.x, 0, newC.z), new Vector3(bodyCentre.x, 0, bodyCentre.z)) * 0.5f;
                if (score > bestScore) { bestScore = score; best = localQ; }
            }
            return best;
        }

        Bounds Bounds()
        {
            var rs = GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(transform.position, Vector3.one * 0.3f);
            var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); return b;
        }
    }
}
