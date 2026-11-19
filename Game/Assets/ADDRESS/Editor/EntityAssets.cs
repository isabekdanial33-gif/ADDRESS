using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Address.EditorTools
{
    /// <summary>
    /// Entity animation setup: the CC0 Quaternius Universal Animation Library (UAL1 + UAL2) imported as Humanoid, so any
    /// humanoid body (hazmat suit, mannequin) plays them. Builds one AnimatorController per gait set.
    /// </summary>
    public static class EntityAssets
    {
        const string Dir = "Assets/ThirdParty/UAL/";

        public static void ImportHumanoid()
        {
            foreach (var f in new[] { "UAL1_Standard.fbx", "UAL2_Standard.fbx", "Mannequin_F.fbx" })
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(Dir + f);
                if (imp == null) { Debug.Log("[ADDRESS] missing " + f); continue; }
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                var clips = imp.defaultClipAnimations;
                foreach (var c in clips)
                {
                    string n = c.name.ToLowerInvariant();
                    c.loopTime = n.Contains("walk") || n.Contains("run") || n.Contains("jog") || n.Contains("sprint") || n.Contains("idle") || n.Contains("crawl") || n.Contains("loop") || n.Contains("crouch");
                    c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;
                    c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
                }
                imp.clipAnimations = clips;
                imp.SaveAndReimport();
                var all = AssetDatabase.LoadAllAssetsAtPath(Dir + f).OfType<AnimationClip>().Where(a => !a.name.StartsWith("__preview")).ToList();
                var av = AssetDatabase.LoadAllAssetsAtPath(Dir + f).OfType<Avatar>().FirstOrDefault();
                Debug.Log($"[ADDRESS] {f}: avatar {(av ? av.isHuman.ToString() : "none")} clips {all.Count}: {string.Join(", ", all.Select(a => $"{a.name}({a.length:F1}s)"))}");
            }
        }

        static AnimationClip Clip(string name)
        {
            foreach (var f in new[] { "UAL1_Standard.fbx", "UAL2_Standard.fbx" })
            {
                var c = AssetDatabase.LoadAllAssetsAtPath(Dir + f).OfType<AnimationClip>().FirstOrDefault(a => a.name == "Armature|" + name);
                if (c) return c;
            }
            Debug.LogError("[ADDRESS] no clip " + name); return null;
        }

        /// <summary>One base controller (Speed blend: idle / walk / fast) + an override per entity kind; mannequin prefab.</summary>
        public static void Build()
        {
            ImportHumanoid();
            const string R = "Assets/ADDRESS/Resources/Entities/";
            System.IO.Directory.CreateDirectory(R);
            var basePath = R + "EntityBase.controller";
            AssetDatabase.DeleteAsset(basePath);
            var ctl = AnimatorController.CreateAnimatorControllerAtPath(basePath);
            ctl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = ctl.layers[0].stateMachine;
            var st = ctl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D; tree.blendParameter = "Speed"; tree.useAutomaticThresholds = false;
            var idle = Clip("Idle_Loop"); var walk = Clip("Walk_Loop"); var fast = Clip("Jog_Fwd_Loop");
            tree.AddChild(idle, 0f); tree.AddChild(walk, 1.1f); tree.AddChild(fast, 3.2f);
            sm.defaultState = st;
            EditorUtility.SetDirty(ctl);

            void Override(string kind, string idleN, string walkN, string fastN)
            {
                var path = R + kind + ".overrideController";
                AssetDatabase.DeleteAsset(path);
                var o = new AnimatorOverrideController(ctl) { name = kind };
                o[idle] = Clip(idleN); o[walk] = Clip(walkN); o[fast] = Clip(fastN);
                AssetDatabase.CreateAsset(o, path);
            }
            Override("Echo", "Zombie_Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop");
            Override("Salesman", "Idle_Talking_Loop", "Walk_Formal_Loop", "Sprint_Loop");
            Override("Crawler", "Crouch_Idle_Loop", "Crouch_Fwd_Loop", "Crouch_Fwd_Loop");
            Override("Mannequin", "Idle_FoldArms_Loop", "Walk_Loop", "Sprint_Loop");
            Override("Limper", "Zombie_Idle_Loop", "Zombie_Walk_Fwd_Loop", "Zombie_Walk_Fwd_Loop");

            // the mannequin body as a prefab under Resources
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "Mannequin_F.fbx");
            if (fbx)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                var an = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
                an.avatar = AssetDatabase.LoadAllAssetsAtPath(Dir + "Mannequin_F.fbx").OfType<Avatar>().FirstOrDefault();
                an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var b = new Bounds(go.transform.position, Vector3.zero); foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                Debug.Log($"[ADDRESS] mannequin bounds {b.min} .. {b.max}, renderers {go.GetComponentsInChildren<Renderer>().Length}");
                PrefabUtility.SaveAsPrefabAsset(go, R + "Mannequin.prefab");
                Object.DestroyImmediate(go);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ADDRESS] entity animators built");
        }
    }
}
