using Unity.Netcode;
using UnityEngine;

namespace Address.Net
{
    /// <summary>Network body of a player: owner writes position/yaw/speed, everyone else shows the hazmat body there.</summary>
    public class NetAvatar : NetworkBehaviour
    {
        public readonly NetworkVariable<Vector3> Pos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<float> Yaw = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public static NetAvatar Local;
        public Transform follow;          // local player rig (owner only)
        public GameObject visual;         // hazmat body (remote only)
        Vector3 smooth;

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
            if (visual) visual.SetActive(!IsOwner);
            smooth = Pos.Value;
        }

        void Update()
        {
            if (IsOwner)
            {
                if (follow) { Pos.Value = follow.position; Yaw.Value = follow.eulerAngles.y; }
                return;
            }
            smooth = Vector3.Lerp(smooth, Pos.Value, 1 - Mathf.Exp(-12f * Time.deltaTime));
            transform.SetPositionAndRotation(smooth, Quaternion.Euler(0, Yaw.Value, 0));
        }
    }
}
