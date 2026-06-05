using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace FrogSmashers.Net.Transport
{
    /// <summary>
    /// Online session over Unity Relay (free tier) using the explicit
    /// allocation flow. A discoverable session additionally publishes
    /// a UGS lobby entry (host only) named after the creator, carrying
    /// the relay join code as a public property so other players can
    /// join from the in-game lobby list without typing codes.
    /// </summary>
    public class NetSession
    {
        /// <summary>Lobby property holding the relay join code.</summary>
        public const string CodeProperty = "relayCode";

        /// <summary>Lobby property holding the player count.</summary>
        public const string CountProperty = "players";

        /// <summary>Session this process is currently part of.</summary>
        public static NetSession Current { get; private set; }

        public string JoinCode { get; private set; }

        public bool IsHost { get; private set; }

        ISession lobby;

        /// <summary>Creates a relay allocation and hosts the match.
        /// When discoverable, also publishes a UGS lobby entry.</summary>
        public static async Task<NetSession> CreateAsync(
            int maxPlayers, bool discoverable)
        {
            await NetBootstrap.EnsureServicesAsync();
            var allocation = await RelayService.Instance
                .CreateAllocationAsync(maxPlayers - 1);
            string code = await RelayService.Instance
                .GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log("[NetSession] Relay allocated:"
                + $" {allocation.Region}, code {code}");
            ConfigureTransport(allocation.ToRelayServerData(Protocol()));
            if (!NetworkManager.Singleton.StartHost())
                throw new System.InvalidOperationException(
                    "StartHost failed");
            Current = new NetSession { JoinCode = code, IsHost = true };
            if (discoverable)
                await Current.PublishLobbyAsync(maxPlayers, code);
            return Current;
        }

        /// <summary>Joins a hosted match through its join code.</summary>
        public static async Task<NetSession> JoinByCodeAsync(string code)
        {
            await NetBootstrap.EnsureServicesAsync();
            var allocation = await RelayService.Instance
                .JoinAllocationAsync(code);
            Debug.Log("[NetSession] Relay joined:"
                + $" {allocation.Region}, code {code}");
            ConfigureTransport(allocation.ToRelayServerData(Protocol()));
            if (!NetworkManager.Singleton.StartClient())
                throw new System.InvalidOperationException(
                    "StartClient failed");
            Current = new NetSession { JoinCode = code, IsHost = false };
            return Current;
        }

        /// <summary>Updates the published player count (host).</summary>
        public async void UpdatePlayerCount(int count)
        {
            if (lobby == null)
                return;
            try
            {
                var host = lobby.AsHost();
                host.SetProperty(CountProperty, new SessionProperty(
                    count.ToString(), VisibilityPropertyOptions.Public));
                await host.SavePropertiesAsync();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(
                    $"[NetSession] Count update failed: {e.Message}");
            }
        }

        /// <summary>Removes the lobby entry (match started).</summary>
        public async void Unpublish()
        {
            if (lobby == null)
                return;
            var doomed = lobby;
            lobby = null;
            try
            {
                await doomed.AsHost().DeleteAsync();
                Debug.Log("[NetSession] Lobby entry deleted");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(
                    $"[NetSession] Unpublish failed: {e.Message}");
            }
        }

        /// <summary>Shuts the connection down and clears Current.</summary>
        public void Leave()
        {
            Unpublish();
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();
            if (Current == this)
                Current = null;
        }

        async Task PublishLobbyAsync(int maxPlayers, string code)
        {
            var options = new SessionOptions
            {
                Name = System.Environment.UserName,
                MaxPlayers = maxPlayers,
                IsPrivate = false,
                IsLocked = false,
                SessionProperties =
                    new Dictionary<string, SessionProperty>
                    {
                        [CodeProperty] = new SessionProperty(code,
                            VisibilityPropertyOptions.Public),
                        [CountProperty] = new SessionProperty("1",
                            VisibilityPropertyOptions.Public),
                    },
            };
            lobby = await MultiplayerService.Instance
                .CreateSessionAsync(options);
            Debug.Log("[NetSession] Lobby published:"
                + $" '{lobby.Name}' ({lobby.Id})");
        }

        static void ConfigureTransport(
            Unity.Networking.Transport.Relay.RelayServerData data)
        {
            var manager = NetBootstrap.EnsureNetworkManager();
            var transport = (UnityTransport)
                manager.NetworkConfig.NetworkTransport;
            transport.SetRelayServerData(data);
        }

        static string Protocol()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-relayWss") >= 0)
                return "wss";
            if (System.Array.IndexOf(args, "-relayUdp") >= 0)
                return "udp";
            return "dtls";
        }
    }
}
