using Address.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Address.Game
{
    /// <summary>All asset references the running game needs (filled by the editor GameSetup).</summary>
    [CreateAssetMenu(menuName = "ADDRESS/Game Config")]
    public class GameConfig : ScriptableObject
    {
        public LevelKit kit;
        public Material ghostOk, ghostBad;
        public VolumeProfile post;
        public GameObject bodyPrefab, fpArmLeft, fpArmRight;
        public AudioKit audio;
        public Material dustMat;
        public GameObject netAvatarPrefab, netSessionPrefab;
    }

}
