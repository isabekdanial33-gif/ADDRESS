using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Address.EditorTools
{
    public static class AssetTools
    {
        /// <summary>Re-import glTF models so glTFast generates URP materials (run after the pipeline is set).</summary>
        public static void ReimportModels()
        {
            AssetDatabase.ImportAsset("Assets/ThirdParty/PolyHaven", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("[ADDRESS] models reimported");
        }

        /// <summary>
        /// ambientCG maps -> correct texture import (normal maps, linear masks) and one URP Lit material per set.
        /// Roughness is converted into URP's smoothness-in-alpha of a packed MetallicGloss map (R=metal, A=1-rough).
        /// </summary>
        public static void BuildSurfaceMaterials()
        {
            const string root = "Assets/ThirdParty/AmbientCG";
            const string outDir = "Assets/ADDRESS/Generated/Materials";
            Directory.CreateDirectory(outDir);
            foreach (var dir in Directory.GetDirectories(root))
            {
                string id = Path.GetFileName(dir);
                var files = Directory.GetFiles(dir, "*.jpg").Select(f => f.Replace('\\', '/')).ToArray();
                string F(string key) => files.FirstOrDefault(f => Path.GetFileName(f).ToLowerInvariant().Contains(key));
                string color = F("_color"), normal = F("normalgl"), rough = F("roughness"), metal = F("metalness"), ao = F("ambientocclusion");
                foreach (var f in files)
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath(f);
                    if (ti == null) continue;
                    bool isNormal = f == normal, isColor = f == color;
                    ti.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.sRGBTexture = isColor;
                    ti.maxTextureSize = 2048;
                    ti.mipmapEnabled = true; ti.streamingMipmaps = true;
                    ti.anisoLevel = 4;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    ti.SaveAndReimport();
                }
                // packed metallic/smoothness map
                string packedPath = $"{outDir}/{id}_MS.png";
                if (rough != null)
                {
                    var r = Readable(rough); var m = metal != null ? Readable(metal) : null;
                    var px = r.GetPixels(); var mp = m?.GetPixels();
                    var outT = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false, true);
                    var o = new Color[px.Length];
                    for (int i = 0; i < px.Length; i++) { float mm = mp != null && mp.Length == px.Length ? mp[i].r : 0f; o[i] = new Color(mm, mm, mm, 1f - px[i].r); }
                    outT.SetPixels(o); outT.Apply();
                    File.WriteAllBytes(packedPath, outT.EncodeToPNG());
                    AssetDatabase.ImportAsset(packedPath);
                    var pti = (TextureImporter)AssetImporter.GetAtPath(packedPath);
                    pti.sRGBTexture = false; pti.maxTextureSize = 2048; pti.streamingMipmaps = true; pti.textureCompression = TextureImporterCompression.CompressedHQ; pti.SaveAndReimport();
                }
                var matPath = $"{outDir}/{id}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
                if (color != null) mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(color));
                if (normal != null) { mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal)); mat.EnableKeyword("_NORMALMAP"); mat.SetFloat("_BumpScale", 1f); }
                if (rough != null) { mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath)); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Smoothness", 1f); mat.SetFloat("_GlossMapScale", 1f); }
                if (ao != null) { mat.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ao)); mat.EnableKeyword("_OCCLUSIONMAP"); }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ADDRESS] surface materials built");
        }

        static Texture2D Readable(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            t.LoadImage(bytes);
            return t;
        }

        public static void PrepareAll()
        {
            ReimportModels();
            BuildSurfaceMaterials();
            BuildAestheticMaterials();
        }

        /// <summary>Materials whose colour map is generated by Tools/make_textures.py (normal/mask maps reused from ambientCG).</summary>
        public static void BuildAestheticMaterials()
        {
            const string tex = "Assets/ADDRESS/Art/Textures";
            const string outDir = "Assets/ADDRESS/Generated/Materials";
            AssetDatabase.ImportAsset(tex, ImportAssetOptions.ImportRecursive);
            void Make(string name, string colorPng, string detailFrom, float smoothScale)
            {
                var cp = $"{tex}/{colorPng}";
                var ti = (TextureImporter)AssetImporter.GetAtPath(cp);
                if (ti != null) { ti.sRGBTexture = true; ti.maxTextureSize = 2048; ti.streamingMipmaps = true; ti.anisoLevel = 8; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport(); }
                var src = AssetDatabase.LoadAssetAtPath<Material>($"{outDir}/{detailFrom}.mat");
                var path = $"{outDir}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!mat) { mat = new Material(src); AssetDatabase.CreateAsset(mat, path); }
                else mat.CopyPropertiesFromMaterial(src);
                mat.shaderKeywords = src.shaderKeywords;
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(cp));
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_GlossMapScale", smoothScale); mat.SetFloat("_Smoothness", smoothScale);
                EditorUtility.SetDirty(mat);
            }
            Make("BR_Wallpaper", "BR_Wallpaper_Color.png", "Wallpaper001A", 0.55f);
            Make("BR_Carpet", "BR_Carpet_Color.png", "Carpet016", 0.6f);
            Make("PR_Basin", "pool_tile_aqua.png", "Tiles107", 0.85f);
            // white pool tiles: glossy glazed ceramic
            var tile = AssetDatabase.LoadAssetAtPath<Material>($"{outDir}/Tiles107.mat");
            if (tile) { tile.SetFloat("_GlossMapScale", 0.85f); tile.SetFloat("_Smoothness", 0.85f); EditorUtility.SetDirty(tile); }
            // glass (transparent, glossy, faint aqua) and Aero white surfaces
            var gp = $"{outDir}/Glass.mat"; var gm = AssetDatabase.LoadAssetAtPath<Material>(gp);
            if (!gm) { gm = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(gm, gp); }
            gm.SetFloat("_Surface", 1); gm.SetFloat("_Blend", 0); gm.SetColor("_BaseColor", new Color(0.78f, 0.94f, 1f, 0.22f));
            gm.SetFloat("_Smoothness", 0.98f); gm.SetFloat("_Metallic", 0f); gm.renderQueue = 3000;
            gm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); gm.SetOverrideTag("RenderType", "Transparent");
            gm.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); gm.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); gm.SetInt("_ZWrite", 0);
            EditorUtility.SetDirty(gm);
            var ap = $"{outDir}/FA_Wall.mat"; var am = AssetDatabase.LoadAssetAtPath<Material>(ap);
            var plaster = AssetDatabase.LoadAssetAtPath<Material>($"{outDir}/PaintedPlaster017.mat");
            if (!am && plaster) { am = new Material(plaster); AssetDatabase.CreateAsset(am, ap); }
            if (am) { am.SetColor("_BaseColor", new Color(1f, 1f, 1f)); am.SetFloat("_GlossMapScale", 0.5f); EditorUtility.SetDirty(am); }
            var fp = $"{outDir}/FA_Floor.mat"; var fm = AssetDatabase.LoadAssetAtPath<Material>(fp);
            var tiles = AssetDatabase.LoadAssetAtPath<Material>($"{outDir}/Tiles107.mat");
            if (!fm && tiles) { fm = new Material(tiles); AssetDatabase.CreateAsset(fm, fp); }
            if (fm) { fm.SetColor("_BaseColor", new Color(0.97f, 0.99f, 1f)); fm.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f)); fm.SetFloat("_GlossMapScale", 0.95f); EditorUtility.SetDirty(fm); }
            // water
            var np = $"{tex}/water_normal.png"; var nti = (TextureImporter)AssetImporter.GetAtPath(np);
            if (nti != null) { nti.textureType = TextureImporterType.NormalMap; nti.SaveAndReimport(); }
            var wp = $"{outDir}/Water.mat"; var wm = AssetDatabase.LoadAssetAtPath<Material>(wp);
            if (!wm) { wm = new Material(Shader.Find("ADDRESS/Water")); AssetDatabase.CreateAsset(wm, wp); }
            wm.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(np)); EditorUtility.SetDirty(wm);
            for (int i = 0; i < 16; i++)
            {
                var cp = $"{tex}/caustic_{i:00}.png"; var cti = (TextureImporter)AssetImporter.GetAtPath(cp);
                if (cti == null) continue;
                cti.textureType = TextureImporterType.Default; cti.wrapMode = TextureWrapMode.Clamp; cti.sRGBTexture = false; cti.mipmapEnabled = false;
                cti.alphaSource = TextureImporterAlphaSource.FromGrayScale; cti.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ADDRESS] aesthetic materials built");
        }
    }
}
