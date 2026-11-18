using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Address.Placement;

namespace Address.EditorTools
{
    /// <summary>
    /// Renders every generated prefab from the front (+Z side) and from a 3/4 view with its collider boxes and surfaces
    /// drawn as overlays. Used by me to check orientation ("front") and collider quality by eye.
    /// </summary>
    public static class Thumbnails
    {
        public static void BatchRender()
        {
            var outDir = "../Docs/reports/thumbs";
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGo = new GameObject("sun");
            var sun = lightGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.6f; sun.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(45, 150, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = Vector3.one * 2;
            var fm = new Material(Shader.Find("Universal Render Pipeline/Lit")); fm.color = new Color(0.35f, 0.35f, 0.35f); floor.GetComponent<Renderer>().sharedMaterial = fm;

            var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.75f, 0.78f, 0.82f);
            cam.fieldOfView = 30;
            var rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            var boxMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            boxMat.SetFloat("_Surface", 1); boxMat.SetFloat("_Blend", 0);
            boxMat.SetColor("_BaseColor", new Color(1f, 0.2f, 0.1f, 0.28f));
            boxMat.renderQueue = 3000; boxMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            boxMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); boxMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); boxMat.SetInt("_ZWrite", 0);
            var surfMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); surfMat.SetColor("_BaseColor", new Color(0.1f, 1f, 0.3f));

            foreach (var guid in AssetDatabase.FindAssets("t:PlacementProfile", new[] { "Assets/ADDRESS/Generated/Profiles" }))
            {
                var prof = AssetDatabase.LoadAssetAtPath<PlacementProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (!prof || !prof.prefab) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prof.prefab);
                float r = Mathf.Max(prof.size.x, prof.size.y, prof.size.z);
                var center = new Vector3(0, prof.size.y * 0.5f, 0);
                float dist = r / Mathf.Tan(15 * Mathf.Deg2Rad) * 0.62f + r * 0.5f;

                // front view (camera on +Z looking toward -Z): what we see is the model's front
                cam.transform.position = center + new Vector3(0, r * 0.15f, dist);
                cam.transform.LookAt(center);
                Save(cam, rt, $"{outDir}/{prof.id}_front.png");

                // overlay view: colliders + surfaces
                var overlay = new GameObject("overlay");
                foreach (var bx in prof.boxes)
                {
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(c.GetComponent<Collider>());
                    c.transform.SetParent(overlay.transform); c.transform.localPosition = bx.center; c.transform.localScale = bx.size;
                    c.GetComponent<Renderer>().sharedMaterial = boxMat;
                }
                foreach (var s in prof.surfaces)
                    foreach (var ci in s.cells)
                    {
                        var p = s.CellCenter(ci);
                        var q = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Object.DestroyImmediate(q.GetComponent<Collider>());
                        q.transform.SetParent(overlay.transform);
                        q.transform.localPosition = new Vector3(p.x, s.height + 0.002f, p.y);
                        q.transform.localScale = new Vector3(s.cell * 0.6f, 0.003f, s.cell * 0.6f);
                        q.GetComponent<Renderer>().sharedMaterial = surfMat;
                    }
                cam.transform.position = center + new Vector3(dist * 0.7f, dist * 0.6f, dist * 0.7f);
                cam.transform.LookAt(center);
                Save(cam, rt, $"{outDir}/{prof.id}_overlay.png");
                Object.DestroyImmediate(overlay);
                Object.DestroyImmediate(inst);
            }
            Debug.Log("[ADDRESS] thumbnails done");
        }

        public static void Save(Camera cam, RenderTexture rt, string path)
        {
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
