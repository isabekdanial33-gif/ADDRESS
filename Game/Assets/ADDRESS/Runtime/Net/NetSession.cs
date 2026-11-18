using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Address.Net
{
    /// <summary>
    /// Shared state of a co-op session. Only the ADDRESS travels over the network: every peer generates the level
    /// itself (generation is deterministic). The host also publishes a hash of the generated level so clients can
    /// verify they are standing in the same place.
    /// </summary>
    public class NetSession : NetworkBehaviour
    {
        public static NetSession Instance { get; private set; }
        public readonly NetworkVariable<FixedString128Bytes> Address = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public readonly NetworkVariable<FixedString64Bytes> LevelHash = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public System.Action<string> onAddress;
        public System.Action<int, ulong> onTravelRequest;   // exit index, requesting client

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Address.OnValueChanged += (_, v) => onAddress?.Invoke(v.ToString());
            if (!IsServer && Address.Value.Length > 0) onAddress?.Invoke(Address.Value.ToString());
        }

        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; }

        public System.Action<int, Vector3, Quaternion> onObjectPose;

        /// <summary>Someone moved a world object: uid is identical on every peer (deterministic generation order).</summary>
        [Rpc(SendTo.NotMe, RequireOwnership = false)]
        public void ObjectPoseRpc(int uid, Vector3 pos, Quaternion rot) => onObjectPose?.Invoke(uid, pos, rot);

        public System.Action<int, Vector3, float, byte> onEntityPose;

        /// <summary>Host-simulated entities: clients only show them.</summary>
        [Rpc(SendTo.NotServer)]
        public void EntityPoseRpc(int index, Vector3 pos, float yaw, byte mode) => onEntityPose?.Invoke(index, pos, yaw, mode);

        [Rpc(SendTo.Server)]
        public void RequestTravelRpc(int exitIndex, RpcParams p = default) => onTravelRequest?.Invoke(exitIndex, p.Receive.SenderClientId);
    }

}
