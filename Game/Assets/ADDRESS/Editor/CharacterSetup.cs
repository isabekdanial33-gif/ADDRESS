using System.IO;
using Address.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Address.EditorTools
{
    /// <summary>Imports the Blender hazmat FBX (Humanoid), converts its materials to URP Lit, builds the player body prefab, renders checks.</summary>
    public static class CharacterSetup
    {
        const string Fbx = "Assets/ADDRESS/Art/Character/hazmat_player.fbx";
        public const string PrefabPath = "Assets/ADDRESS/Generated/Prefabs/HazmatBody.prefab";

        public static void Run()
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.External;
            imp.isReadable = true; imp.importBlendShapes = false; imp.importCameras = false; imp.importLights = false;
            imp.SaveAndReimport();
            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(Fbx);
            Debug.Log($"[ADDRESS] hazmat avatar valid={(avatar ? avatar.isValid : false)} human={(avatar ? avatar.isHuman : false)}");

            // URP Lit materials with the Blender colours/roughness
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            Directory.CreateDirectory("Assets/ADDRESS/Generated/Materials/Character");
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m) continue;
                    var path = $"Assets/ADDRESS/Generated/Materials/Character/{m.name}.mat";
                    var u = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!u) { u = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(u, path); }
                    var col = m.HasProperty("_Color") ? m.color.gamma : Color.white; // Blender colours are linear
                    u.SetColor("_BaseColor", col);
                    float rough = m.name.Contains("suit") ? 0.48f : m.name.Contains("lens") ? 0.04f : m.name.Contains("metal") || m.name.Contains("zipper") ? 0.32f : 0.5f;
                    u.SetFloat("_Smoothness", 1f - rough);
                    u.SetFloat("_Metallic", m.name.Contains("metal") || m.name.Contains("zipper") ? 0.9f : 0f);
                    mats[i] = u; EditorUtility.SetDirty(u);
                }
                r.sharedMaterials = mats;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
            // FBX was exported facing -Z: wrap it so the body faces +Z like everything else
            var root = new GameObject("HazmatBody");
            inst.transform.SetParent(root.transform, false);
            inst.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var pb = root.AddComponent<ProceduralBody>(); pb.firstPerson = true;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Preview();
        }

        /// <summary>Render the body standing in a generated Backrooms level from a few angles (Unity lighting, not Blender).</summary>
        public static void Preview()
        {
            Directory.CreateDirectory("../Docs/reports/character_unity");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var kit = LevelFarm.Kit();
            var level = new Address.World.LevelBuilder(kit).Build(new Address.Core.LevelAddress("BR", 0xBADC0FFEEUL));
            TestFarm.Lighting(new Address.TestLevel.TestLevelBuilder.Result { root = level.root });
            var body = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            body.GetComponent<ProceduralBody>().firstPerson = false;
            body.transform.position = level.start; body.transform.rotation = Quaternion.Euler(0, 200, 0);
            var go = new GameObject("cam"); var cam = go.AddComponent<Camera>(); cam.fieldOfView = 50; cam.nearClipPlane = 0.03f;
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            var rt = new RenderTexture(1080, 1350, 24); cam.targetTexture = rt;
            var views = new[] { (new Vector3(0.6f, 1.4f, -2.6f), "front"), (new Vector3(2.2f, 1.3f, -1.4f), "three_quarter"), (new Vector3(-0.5f, 1.5f, 2.6f), "back"), (new Vector3(0.35f, 1.65f, -0.75f), "head") };
            foreach (var (off, name) in views)
            {
                var o = body.transform.TransformPoint(off);
                go.transform.position = o; go.transform.LookAt(body.transform.position + Vector3.up * (name == "head" ? 1.6f : 0.95f));
                Thumbnails.Save(cam, rt, $"../Docs/reports/character_unity/{name}.png");
            }
            // first-person look down while walking pose
            body.GetComponent<ProceduralBody>().firstPerson = true; body.GetComponent<ProceduralBody>().Bind();
            body.transform.position = level.start;
            body.GetComponent<ProceduralBody>().ApplyFirstPersonOffset();
            go.transform.position = body.transform.parent ? body.transform.parent.position : level.start;
            go.transform.position = level.start + Vector3.up * 1.63f;
            go.transform.rotation = body.transform.rotation * Quaternion.Euler(62, 0, 0);
            cam.fieldOfView = 72;
            Thumbnails.Save(cam, rt, "../Docs/reports/character_unity/fp_look_down.png");
            Debug.Log("[ADDRESS] character previews done");
        }
    }
}
