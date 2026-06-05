using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace FrogSmashers.Net.Transport
{
    /// <summary>
    /// Online session over Unity Relay (free tier) using the explicit
    /// allocation flow: the creator allocates a relay, gets a 6-char
    /// join code and hosts the authoritative simulation; joiners
    /// connect through the relay with that code. (The Sessions API
    /// network module proved broken in 2.2.3, so NGO is started
    /// directly; lobby browsing can come later.)
    /// </summary>
    public class NetSession
    {
        /// <summary>Session this process is currently part of.</summary>
        public static NetSession Current { get; private set; }

        public string JoinCode { get; private set; }

        public bool IsHost { get; private set; }

        /// <summary>Creates a relay allocation and hosts the match.</summary>
        public static async Task<NetSession> CreateAsync(int maxPlayers)
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

        /// <summary>Shuts the connection down and clears Current.</summary>
        public void Leave()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();
            if (Current == this)
                Current = null;
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
