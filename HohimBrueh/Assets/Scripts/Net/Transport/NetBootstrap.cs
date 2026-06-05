using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace FrogSmashers.Net.Transport
{
    /// <summary>
    /// One-time setup for online play: Unity Gaming Services init,
    /// anonymous sign-in, and programmatic NetworkManager creation
    /// (no scene object, per the no-Editor-UI rule).
    /// </summary>
    public static class NetBootstrap
    {
        static bool servicesReady;

        /// <summary>Initializes UGS + anonymous auth (idempotent).
        /// Pass -authProfile NAME to isolate the anonymous identity of
        /// each instance when testing several on one machine.</summary>
        public static async Task EnsureServicesAsync()
        {
            if (!servicesReady)
            {
                if (UnityServices.State
                    == ServicesInitializationState.Uninitialized)
                {
                    var options = new InitializationOptions();
                    string profile = GetCliArg("-authProfile");
                    if (!string.IsNullOrEmpty(profile))
                        options.SetProfile(profile);
                    await UnityServices.InitializeAsync(options);
                }
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance
                        .SignInAnonymouslyAsync();
                }
                servicesReady = true;
                Debug.Log("[NetBootstrap] UGS ready, player id "
                    + AuthenticationService.Instance.PlayerId);
            }
            EnsureNetworkManager();
        }

        /// <summary>Creates the NetworkManager singleton if missing.</summary>
        public static NetworkManager EnsureNetworkManager()
        {
            if (NetworkManager.Singleton != null)
                return NetworkManager.Singleton;
            var host = new GameObject("NetworkManager");
            Object.DontDestroyOnLoad(host);
            var manager = host.AddComponent<NetworkManager>();
            var transport = host.AddComponent<UnityTransport>();
            if (System.Array.IndexOf(
                System.Environment.GetCommandLineArgs(), "-relayWss")
                >= 0)
            {
                transport.UseWebSockets = true;
            }
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
            };
            manager.LogLevel = LogLevel.Developer;
            manager.OnClientConnectedCallback += id =>
                Debug.Log($"[NetBootstrap] Client connected: {id}");
            manager.OnClientDisconnectCallback += id =>
                Debug.Log($"[NetBootstrap] Client disconnected: {id}"
                    + $" reason='{manager.DisconnectReason}'");
            manager.OnTransportFailure += () =>
                Debug.Log("[NetBootstrap] Transport failure");
            return manager;
        }

        static string GetCliArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                    return args[i + 1];
            }
            return null;
        }
    }
}
