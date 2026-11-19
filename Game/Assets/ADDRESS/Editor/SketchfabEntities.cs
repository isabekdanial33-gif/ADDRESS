using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Address.EditorTools
{
    /// <summary>
    /// Downloaded CC-BY Sketchfab creatures (see ThirdParty/Sketchfab/CREDITS.txt) -> Resources/Entities/Model_&lt;Kind&gt;.prefab
    /// plus an override controller. Mixamo-rigged ones are imported as Humanoid and play the CC0 UAL clips; the others
    /// keep their own idle / walk / run clips.
    /// </summary>
    public static class SketchfabEntities
    {
        const string Dir = "Assets/ThirdParty/Sketchfab/";
        const string R = "Assets/ADDRESS/Resources/Entities/";

        // file, kind, humanoid, height (m), extra yaw for generic rigs, idle / walk / fast (UAL names for humanoids, substrings of own takes otherwise)
        static readonly (string file, string kind, bool human, float height, float yaw, string idle, string walk, string fast)[] Models =
        {
            ("smiler.fbx",      "Smiler",      true,  2.35f, 0f, "Zombie_Idle_Loop",  "Zombie_Walk_Fwd_Loop", "Jog_Fwd_Loop"),
            ("skinstealer.fbx", "SkinStealer", true,  1.95f, 0f, "Crouch_Idle_Loop",  "Crouch_Fwd_Loop",      "Jog_Fwd_Loop"),
            ("clark.fbx",       "Clark",       true,  1.86f, 0f, "Zombie_Idle_Loop",  "Zombie_Walk_Fwd_Loop", "Sprint_Loop"),
            ("partygoer.fbx",   "Partygoer",   false, 2.25f, 0f, "idle",              "walk",                 "run"),
            ("growler.fbx",     "Growler",     false, 2.6f,  0f, "Idle",              "Walk",                 "Walk"),
            ("needlelimbs.fbx", "Needlelimbs", false, 2.7f,  0f, "Layer0",            "Layer0",               "Layer0"),
        };

        public static void Build()
        {
            EntityAssets.Build();   // base controller + UAL import first
            var baseCtl = AssetDatabase.LoadAssetAtPath<AnimatorController>(R + "EntityBase.controller");
            var bt = (BlendTree)baseCtl.layers[0].stateMachine.defaultState.motion;
            var baseClips = bt.children.Select(c => (AnimationClip)c.motion).ToArray();   // idle, walk, fast

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir.TrimEnd('/') }))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (ti && ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; ti.SaveAndReimport(); }
            }
            foreach (var m in Models)
            {
                string p = Dir + m.file;
                var imp = (ModelImporter)AssetImporter.GetAtPath(p);
                if (imp == null) { Debug.LogError("[ADDRESS] missing " + p); continue; }
                imp.animationType = m.human ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.importAnimation = !m.human;
                if (!m.human)
                {
                    var clips = imp.defaultClipAnimations;
                    foreach (var c in clips)
                    {
                        string n = c.name.ToLowerInvariant();
                        c.loopTime = !n.Contains("attack") && !n.Contains("kill") && !n.Contains("sit") && !n.Contains("eat");
                        c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;
                        c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
                    }
                    imp.clipAnimations = clips;
                }
                imp.SaveAndReimport();
                var subs = AssetDatabase.LoadAllAssetsAtPath(p);
                var av = subs.OfType<Avatar>().FirstOrDefault();
                var own = subs.OfType<AnimationClip>().Where(a => !a.name.StartsWith("__preview")).ToList();
                Debug.Log($"[ADDRESS] {m.file}: avatar {(av ? (av.isHuman ? "human" : "generic") + (av.isValid ? "" : " INVALID") : "none")} clips {string.Join(", ", own.Select(a => $"{a.name}({a.length:F1}s)"))}");

                // controller: override of the shared Speed blend
                AnimationClip Pick(string key)
                {
                    if (m.human) return AssetDatabase.LoadAllAssetsAtPath("Assets/ThirdParty/UAL/UAL1_Standard.fbx").Concat(AssetDatabase.LoadAllAssetsAtPath("Assets/ThirdParty/UAL/UAL2_Standard.fbx"))
                        .OfType<AnimationClip>().FirstOrDefault(a => a.name == "Armature|" + key);
                    return own.FirstOrDefault(a => a.name.ToLowerInvariant().EndsWith(key.ToLowerInvariant()))
                        ?? own.FirstOrDefault(a => a.name.ToLowerInvariant().Contains(key.ToLowerInvariant())) ?? own.FirstOrDefault();
                }
                var ocPath = R + m.kind + ".overrideController";
                AssetDatabase.DeleteAsset(ocPath);
                var oc = new AnimatorOverrideController(baseCtl) { name = m.kind };
                var picks = new[] { Pick(m.idle), Pick(m.walk), Pick(m.fast) };
                for (int i = 0; i < 3; i++) if (picks[i]) oc[baseClips[i]] = picks[i]; else Debug.LogError($"[ADDRESS] {m.kind}: no clip {i}");
                AssetDatabase.CreateAsset(oc, ocPath);
                Debug.Log($"[ADDRESS] {m.kind} clips: {string.Join(" / ", picks.Select(c => c ? c.name : "-"))}");

                // prefab: feet on y=0, centred, scaled to the target height
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                var root = new GameObject("Model_" + m.kind);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx); inst.transform.SetParent(root.transform, false);
                inst.transform.localRotation = Quaternion.Euler(0, m.yaw, 0);
                var an = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
                an.avatar = av; an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate; an.runtimeAnimatorController = oc;
                // generic rigs: measure the posed body (their clips can move the root far from the rest pose)
                if (!m.human && picks[0]) picks[0].SampleAnimation(inst, Mathf.Min(0.3f, picks[0].length * 0.5f));
                var b = Bounds(inst);
                float s = m.height / Mathf.Max(0.01f, b.size.y);
                inst.transform.localScale *= s;
                b = Bounds(inst);
                inst.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
                // a model whose fbx lost its texture link: <stem>_base.png onto a fresh Lit material
                var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + System.IO.Path.GetFileNameWithoutExtension(m.file) + "_base.png");
                Material baseMat = null;
                if (baseTex)
                {
                    string mp = R + "Mat_" + m.kind + ".mat"; AssetDatabase.DeleteAsset(mp);
                    baseMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Mat_" + m.kind };
                    baseMat.SetTexture("_BaseMap", baseTex); baseMat.SetColor("_BaseColor", Color.white); baseMat.SetFloat("_Smoothness", 0.35f);
                    AssetDatabase.CreateAsset(baseMat, mp);
                }
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    if (baseMat) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = baseMat; r.sharedMaterials = ms; }
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                }
                b = Bounds(inst);
                Debug.Log($"[ADDRESS] Model_{m.kind}: scale {s:F3} bounds {b.min} .. {b.max} renderers {inst.GetComponentsInChildren<Renderer>().Length} mats {string.Join(",", inst.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Where(x => x).Select(x => x.name + ":" + x.shader.name + ":" + (x.mainTexture ? x.mainTexture.name : "-")).Distinct())}");
                PrefabUtility.SaveAsPrefabAsset(root, R + "Model_" + m.kind + ".prefab");
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ADDRESS] sketchfab entities built");
        }

        static Bounds Bounds(GameObject go)
        {
            // the body = the renderer with the most vertices (balloons, strings and props must not set the height)
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            Renderer best = rs[0]; int bv = -1;
            foreach (var r in rs)
            {
                int v = r is SkinnedMeshRenderer s ? (s.sharedMesh ? s.sharedMesh.vertexCount : 0) : r.GetComponent<MeshFilter>() is MeshFilter f && f.sharedMesh ? f.sharedMesh.vertexCount : 0;
                if (v > bv) { bv = v; best = r; }
            }
            if (best is SkinnedMeshRenderer sk && sk.sharedMesh)
            {
                // bake the current pose; BakeMesh output is in the renderer's local space (scale included)
                var mesh = new Mesh(); sk.BakeMesh(mesh, true);
                var vs = mesh.vertices; var tr = sk.transform;
                var bb = new Bounds(tr.TransformPoint(vs[0]), Vector3.zero);
                foreach (var v in vs) bb.Encapsulate(tr.TransformPoint(v));
                Object.DestroyImmediate(mesh);
                return bb;
            }
            return best.bounds;
        }
    }
}
