using UnityEditor;
using UnityEngine;

namespace Address.EditorTools
{
    /// <summary>Layers and physics matrix the generator relies on. Idempotent.</summary>
    public static class ProjectSetup
    {
        public const int LayerArchitecture = 8;
        public const int LayerProps = 9;
        public const int LayerProbe = 10;     // temporary geometry used while building profiles
        public const int LayerPlayer = 11;
        public const int LayerHeld = 12;

        [MenuItem("ADDRESS/Setup Layers")]
        public static void EnsureLayers()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            Set(layers, LayerArchitecture, "Architecture");
            Set(layers, LayerProps, "Props");
            Set(layers, LayerProbe, "Probe");
            Set(layers, LayerPlayer, "Player");
            Set(layers, LayerHeld, "Held");
            tm.ApplyModifiedPropertiesWithoutUndo();

            // Probe geometry never collides with anything else
            for (int i = 0; i < 32; i++) if (i != LayerProbe) Physics.IgnoreLayerCollision(LayerProbe, i, true);
            // Held object does not push the player that holds it
            Physics.IgnoreLayerCollision(LayerHeld, LayerPlayer, true);
            AssetDatabase.SaveAssets();
        }

        static void Set(SerializedProperty layers, int index, string name)
        {
            var p = layers.GetArrayElementAtIndex(index);
            if (p.stringValue != name) p.stringValue = name;
        }
    }
}
