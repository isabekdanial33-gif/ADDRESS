using System.IO;
using System.Linq;
using Address.Player;
using UnityEditor;
using UnityEngine;

namespace Address.EditorTools
{
    /// <summary>The downloaded hazmat character (CGTrader, royalty free): humanoid import, URP material, body prefab.</summary>
    public static class HazmatImport
    {
        const string Dir = "Assets/ThirdParty/HazmatSuit";
        const string Fbx = Dir + "/Mesh/SKM_HazmatSuit.fbx";

        public static void Run()
        {
            foreach (var t in new[] { "AlbedoTransparency", "Normal", "MetallicSmoothness", "AO" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath($"{Dir}/Textures/T_HazmatSuit_{t}_4K.png");
                if (!ti) continue;
                ti.maxTextureSize = 2048; ti.textureType = t == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = t == "AlbedoTransparency"; ti.SaveAndReimport();
            }
            var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            imp.animationType = ModelImporterAnimationType.Human; imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.isReadable = true; imp.importAnimation = false; imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Avatar>().FirstOrDefault();
            Debug.Log($"[ADDRESS] hazmat2 avatar valid={avatar && avatar.isValid} human={avatar && avatar.isHuman}");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/M_Hazmat_URP.mat");
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, Dir + "/M_Hazmat_URP.mat"); }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Textures/T_HazmatSuit_AlbedoTransparency_4K.png"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Textures/T_HazmatSuit_Normal_4K.png")); mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Textures/T_HazmatSuit_MetallicSmoothness_4K.png")); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 1f);
            mat.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Textures/T_HazmatSuit_AO_4K.png")); mat.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(mat);

            var blueTex = $"{Dir}/Textures/T_HazmatSuit_Albedo_Blue.png";
            if (AssetImporter.GetAtPath(blueTex) is TextureImporter bti) { bti.maxTextureSize = 2048; bti.SaveAndReimport(); }
            var blue = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/M_Hazmat_Blue.mat");
            if (!blue) { blue = new Material(mat); AssetDatabase.CreateAsset(blue, Dir + "/M_Hazmat_Blue.mat"); }
            blue.CopyPropertiesFromMaterial(mat); blue.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(blueTex)); EditorUtility.SetDirty(blue);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) { var m = r.sharedMaterials; for (int i = 0; i < m.Length; i++) m[i] = mat; r.sharedMaterials = m; if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true; }
            var b = new Bounds(inst.transform.position, Vector3.zero); foreach (var r in inst.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            var an = inst.GetComponent<Animator>();
            Vector3 hips = an && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Hips).position : Vector3.zero;
            Vector3 toes = an && an.isHuman && an.GetBoneTransform(HumanBodyBones.LeftToes) ? an.GetBoneTransform(HumanBodyBones.LeftToes).position : Vector3.zero;
            Debug.Log($"[ADDRESS] hazmat2 bounds {b.size} min {b.min} hips {hips} toes {toes}");
            // normalise: 1.78 m tall, feet at 0, facing +Z (toes in front of hips)
            // normalise: 1.78 m tall, feet at 0, facing +Z (toes in front of hips)
            float s = 1.78f / Mathf.Max(0.1f, b.size.y);
            var root = new GameObject("HazmatBody2");
            inst.transform.SetParent(root.transform, false);
            inst.transform.localScale *= s;
            var face = toes - hips; face.y = 0;
            if (face.z < -0.02f) inst.transform.localRotation = Quaternion.Euler(0, 180, 0) * inst.transform.localRotation;   // only flip if it faces -Z
            var b2 = new Bounds(inst.transform.position, Vector3.zero); foreach (var r in inst.GetComponentsInChildren<Renderer>()) b2.Encapsulate(r.bounds);
            inst.transform.position -= new Vector3(b2.center.x, b2.min.y, b2.center.z);
            root.AddComponent<ProceduralBody>().firstPerson = true;
            Directory.CreateDirectory("Assets/ADDRESS/Generated/Prefabs");
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/ADDRESS/Generated/Prefabs/HazmatBody2.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[ADDRESS] hazmat2 prefab saved, scale " + s);
        }
    }
}
