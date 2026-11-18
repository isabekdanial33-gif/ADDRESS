using System.Linq;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Address.Net
{
    /// <summary>Creates the NetworkManager in code and starts a 2-player host or client (LAN / direct IP, port 7777).</summary>
    public static class NetLauncher
    {
        public const ushort Port = 7777;
        public static NetworkManager Manager { get; private set; }

        public static NetworkManager Ensure(GameObject avatarPrefab, GameObject sessionPrefab)
        {
            if (Manager) return Manager;
            var go = new GameObject("NetworkManager");
            Object.DontDestroyOnLoad(go);
            var utp = go.AddComponent<UnityTransport>();
            Manager = go.AddComponent<NetworkManager>();
            Manager.NetworkConfig = new NetworkConfig { NetworkTransport = utp, PlayerPrefab = avatarPrefab, ConnectionApproval = true, TickRate = 30 };
            Manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = avatarPrefab });
            Manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = sessionPrefab });
            // two explorers at most
            Manager.ConnectionApprovalCallback = (req, resp) => { resp.Approved = Manager.ConnectedClientsIds.Count < 2; resp.CreatePlayerObject = true; };
            return Manager;
        }

        public static bool Host(GameObject sessionPrefab)
        {
            Manager.GetComponent<UnityTransport>().SetConnectionData("0.0.0.0", Port, "0.0.0.0");
            if (!Manager.StartHost()) return false;
            var s = Object.Instantiate(sessionPrefab); s.GetComponent<NetworkObject>().Spawn();
            return true;
        }

        public static bool Join(string ip)
        {
            Manager.GetComponent<UnityTransport>().SetConnectionData(string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim(), Port);
            return Manager.StartClient();
        }

        public static void Stop() { if (Manager && (Manager.IsClient || Manager.IsServer)) Manager.Shutdown(); }

        public static string LocalIp()
        {
            try { return Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1"; }
            catch { return "127.0.0.1"; }
        }
    }
}
