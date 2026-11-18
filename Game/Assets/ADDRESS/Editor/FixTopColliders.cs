using System.Linq;
using Address.Placement;
using UnityEditor;
using UnityEngine;

namespace Address.EditorTools
{
    /// <summary>
    /// Some models get voxel colliders whose top lies below the visible surface (thin desk tops): objects placed on
    /// them sank into the plate. For every "top" surface: raycast down onto the prefab's own colliders over the
    /// surface cells; if the collider is lower than the visual surface, add a thin box exactly under the surface.
    /// </summary>
    public static class FixTopColliders
    {
        public static void Run()
        {
            int fixedN = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:PlacementProfile", new[] { "Assets/ADDRESS/Generated/Profiles" }))
            {
                var prof = AssetDatabase.LoadAssetAtPath<PlacementProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (!prof || !prof.prefab) continue;
                var path = AssetDatabase.GetAssetPath(prof.prefab);
                var root = PrefabUtility.LoadPrefabContents(path);
                root.transform.position = new Vector3(0, -500, 0); Physics.SyncTransforms();
                bool changed = false;
                foreach (var s in prof.surfaces.Where(q => q.kind == "top" && q.cells.Count > 0))
                {
                    float maxGap = 0; Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
                    foreach (var i in s.cells)
                    {
                        var c = new Vector2(s.origin.x + (i % s.w + 0.5f) * s.cell, s.origin.y + (i / s.w + 0.5f) * s.cell);
                        min = Vector2.Min(min, c - Vector2.one * s.cell * 0.5f); max = Vector2.Max(max, c + Vector2.one * s.cell * 0.5f);
                        var o = root.transform.TransformPoint(new Vector3(c.x, s.height + 0.05f, c.y));
                        float hitY = -1;
                        foreach (var col in root.GetComponentsInChildren<Collider>())
                            if (col.Raycast(new Ray(o, Vector3.down), out var h, 0.3f)) hitY = Mathf.Max(hitY, root.transform.InverseTransformPoint(h.point).y);
                        if (hitY > 0) maxGap = Mathf.Max(maxGap, s.height - hitY);
                    }
                    if (maxGap > 0.006f && maxGap < 0.12f)
                    {
                        var holder = root.transform.Find("Colliders") ?? root.transform;
                        var go = new GameObject("TopPlate") { layer = holder.gameObject.layer };
                        go.transform.SetParent(holder, false);
                        var bc = go.AddComponent<BoxCollider>();
                        float th = Mathf.Max(0.012f, maxGap);
                        bc.center = holder.InverseTransformPoint(root.transform.TransformPoint(new Vector3((min.x + max.x) * 0.5f, s.height - th * 0.5f, (min.y + max.y) * 0.5f)));
                        bc.size = new Vector3(max.x - min.x, th, max.y - min.y);
                        changed = true; fixedN++;
                        Debug.Log($"[ADDRESS] top plate: {prof.id} surface {s.height:F3} collider was {maxGap * 100:F1} cm lower");
                    }
                }
                root.transform.position = Vector3.zero;
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ADDRESS] top plates added: {fixedN}");
        }
    }
}
