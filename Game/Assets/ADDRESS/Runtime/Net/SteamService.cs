using UnityEngine;
#if UNITY_STANDALONE && STEAMWORKSNET && !DISABLESTEAMWORKS
using Steamworks;
using Netcode.Transports;
#endif

namespace Address.Net
{
    /// <summary>
    /// Steam: the explorer's Steam name, a friends-only lobby for co-op, invites through the Steam overlay and joining
    /// through Steam's relay (no IP, no port forwarding). Without Steam running (or on phones) everything falls back to
    /// the LAN / direct IP co-op. App id: steam_appid.txt next to the exe (480 = Valve's test app until ADDRESS has its own).
    /// </summary>
    public static class SteamService
    {
        public const uint AppId = 480;
        public static bool Ready { get; private set; }
        public static string PersonaName { get; private set; } = "";
        public static string Status { get; private set; } = "";
        public static System.Action onJoined;   // a lobby was joined and the client is starting
        public static System.Action ensureManager;   // creates the NetworkManager (the game knows the prefabs)

#if UNITY_STANDALONE && STEAMWORKSNET && !DISABLESTEAMWORKS
        static Callback<LobbyCreated_t> cbCreated;
        static Callback<GameLobbyJoinRequested_t> cbJoinReq;
        static Callback<LobbyEnter_t> cbEnter;
        static CSteamID lobby;
        static System.Func<bool> hostStarter;

        public static void Init()
        {
            if (Ready || Application.isBatchMode) return;
            try { Ready = SteamAPI.Init(); }
            catch (System.Exception e) { Debug.Log("[ADDRESS] steam: not available (" + e.GetType().Name + ")"); Ready = false; }
            if (!Ready) { Debug.Log("[ADDRESS] steam: not running - LAN co-op only"); return; }
            PersonaName = SteamFriends.GetPersonaName();
            cbCreated = Callback<LobbyCreated_t>.Create(OnCreated);
            cbJoinReq = Callback<GameLobbyJoinRequested_t>.Create(r => { Status = "вхожу в лобби друга..."; SteamMatchmaking.JoinLobby(r.m_steamIDLobby); });
            cbEnter = Callback<LobbyEnter_t>.Create(OnEnter);
            var go = new GameObject("SteamPump"); Object.DontDestroyOnLoad(go); go.AddComponent<Pump>();
            Debug.Log("[ADDRESS] steam: signed in as " + PersonaName);
            // started by accepting an invite while the game was closed: Steam passes +connect_lobby <id>
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out var id)) SteamMatchmaking.JoinLobby(new CSteamID(id));
        }

        class Pump : MonoBehaviour
        {
            void Update() { if (Ready) SteamAPI.RunCallbacks(); }
            void OnApplicationQuit() { if (Ready) SteamAPI.Shutdown(); }
        }

        /// <summary>Host over Steam: switch the NetworkManager to the Steam transport, open a friends-only lobby, show the invite dialog.</summary>
        public static void HostLobby(System.Func<bool> startHost)
        {
            if (!Ready) { Status = "Steam не запущен"; return; }
            UseSteamTransport();
            hostStarter = startHost;
            Status = "создаю лобби...";
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 2);
        }

        public static void Invite()
        {
            if (Ready && lobby.IsValid()) SteamFriends.ActivateGameOverlayInviteDialog(lobby);
            else if (Ready) SteamFriends.ActivateGameOverlay("friends");
        }

        static void OnCreated(LobbyCreated_t r)
        {
            if (r.m_eResult != EResult.k_EResultOK) { Status = "лобби не создано: " + r.m_eResult; return; }
            lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(lobby, "host", SteamUser.GetSteamID().m_SteamID.ToString());
            SteamMatchmaking.SetLobbyData(lobby, "game", "ADDRESS");
            bool ok = hostStarter != null && hostStarter();
            Status = ok ? "лобби открыто - пригласите друга (Shift+Tab)" : "не удалось начать игру";
            if (ok) SteamFriends.ActivateGameOverlayInviteDialog(lobby);
        }

        static void OnEnter(LobbyEnter_t r)
        {
            lobby = new CSteamID(r.m_ulSteamIDLobby);
            var owner = SteamMatchmaking.GetLobbyOwner(lobby);
            if (owner == SteamUser.GetSteamID()) return;   // our own lobby
            ensureManager?.Invoke();
            UseSteamTransport();
            var t = NetLauncher.Manager ? NetLauncher.Manager.GetComponent<SteamNetworkingSocketsTransport>() : null;
            if (!t) { Status = "нет сетевого менеджера"; return; }
            t.ConnectToSteamID = owner.m_SteamID;
            Status = "подключаюсь к " + SteamFriends.GetFriendPersonaName(owner) + "...";
            if (NetLauncher.Manager.StartClient()) onJoined?.Invoke();
        }

        static void UseSteamTransport()
        {
            var m = NetLauncher.Manager; if (!m) return;
            var t = m.GetComponent<SteamNetworkingSocketsTransport>() ?? m.gameObject.AddComponent<SteamNetworkingSocketsTransport>();
            m.NetworkConfig.NetworkTransport = t;
        }
#else
        public static void Init() { }
        public static void HostLobby(System.Func<bool> startHost) { Status = "Steam недоступен в этой версии"; }
        public static void Invite() { }
#endif
    }
}
