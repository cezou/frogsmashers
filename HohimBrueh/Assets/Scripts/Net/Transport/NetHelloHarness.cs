using System.IO;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace FrogSmashers.Net.Transport
{
    /// <summary>
    /// Networked hello-world gate. Launch one instance with -netHost
    /// (creates a relay session and writes its join code to a shared
    /// file) and another with -netJoin (reads the code and connects).
    /// Host passes once a second player is in the session; the client
    /// passes once joined. Exit codes: 0 pass, 1 fail.
    /// </summary>
    public class NetHelloHarness : MonoBehaviour
    {
        enum Role
        {
            None,
            Host,
            Join,
            HostDirect,
            JoinDirect,
            HostRelay,
            JoinRelay,
        }

        const float hostTimeout = 120f;
        const float joinTimeout = 90f;

        static Role role;

        static string CodeFile
        {
            get
            {
                return Path.Combine(
                    Application.persistentDataPath,
                    "netHelloJoinCode.txt");
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (HasCliArg("-netHost"))
                role = Role.Host;
            else if (HasCliArg("-netJoin"))
                role = Role.Join;
            else if (HasCliArg("-netHostDirect"))
                role = Role.HostDirect;
            else if (HasCliArg("-netJoinDirect"))
                role = Role.JoinDirect;
            else if (HasCliArg("-netHostRelay"))
                role = Role.HostRelay;
            else if (HasCliArg("-netJoinRelay"))
                role = Role.JoinRelay;
            else
                return;
            var host = new GameObject("NetHelloHarness");
            DontDestroyOnLoad(host);
            host.AddComponent<NetHelloHarness>();
        }

        async void Start()
        {
            Debug.Log($"[NetHello] role={role}");
            try
            {
                if (role == Role.Host)
                    await RunHost();
                else if (role == Role.Join)
                    await RunJoin();
                else if (role == Role.HostDirect)
                    await RunHostDirect();
                else if (role == Role.JoinDirect)
                    await RunJoinDirect();
                else if (role == Role.HostRelay)
                    await RunHostRelay();
                else
                    await RunJoinRelay();
            }
            catch (System.Exception e)
            {
                Debug.Log($"[NetHello] FAIL: {e}");
                Application.Quit(1);
            }
        }

        async Task RunHost()
        {
            if (File.Exists(CodeFile))
                File.Delete(CodeFile);
            var net = await NetSession.CreateAsync(4, false);
            File.WriteAllText(CodeFile, net.JoinCode);
            Debug.Log($"[NetHello] code {net.JoinCode} written, waiting");
            var manager = NetworkManager.Singleton;
            float deadline = Time.realtimeSinceStartup + hostTimeout;
            bool seen = false;
            while (!seen && Time.realtimeSinceStartup < deadline)
            {
                seen = manager.ConnectedClientsIds.Count >= 2;
                await Task.Delay(250);
            }
            if (seen)
            {
                Debug.Log("[NetHello] PASS: client connected over"
                    + " relay");
                await Task.Delay(3000);
                Application.Quit(0);
            }
            else
            {
                Debug.Log("[NetHello] FAIL: timeout waiting for client");
                Application.Quit(1);
            }
        }

        async Task RunJoin()
        {
            float deadline = Time.realtimeSinceStartup + joinTimeout;
            while (!File.Exists(CodeFile)
                && Time.realtimeSinceStartup < deadline)
            {
                await Task.Delay(500);
            }
            if (!File.Exists(CodeFile))
            {
                Debug.Log("[NetHello] FAIL: no join code file");
                Application.Quit(1);
                return;
            }
            string code = File.ReadAllText(CodeFile).Trim();
            Debug.Log($"[NetHello] joining with code {code}");
            await NetSession.JoinByCodeAsync(code);
            var manager = NetworkManager.Singleton;
            bool connected = false;
            while (!connected && Time.realtimeSinceStartup < deadline)
            {
                connected = manager.IsConnectedClient;
                await Task.Delay(250);
            }
            if (connected)
            {
                Debug.Log("[NetHello] PASS: connected to host over"
                    + " relay");
                Application.Quit(0);
            }
            else
            {
                Debug.Log("[NetHello] FAIL: relay connect timeout");
                Application.Quit(1);
            }
        }

        async Task RunHostRelay()
        {
            if (File.Exists(CodeFile))
                File.Delete(CodeFile);
            await NetBootstrap.EnsureServicesAsync();
            var allocation = await RelayService.Instance
                .CreateAllocationAsync(4);
            string code = await RelayService.Instance
                .GetJoinCodeAsync(allocation.AllocationId);
            var data = allocation.ToRelayServerData("dtls");
            Debug.Log($"[NetHello] relay host endpoint {data.Endpoint}"
                + $" region {allocation.Region}");
            var manager = NetworkManager.Singleton;
            var utp = (UnityTransport)
                manager.NetworkConfig.NetworkTransport;
            utp.SetRelayServerData(data);
            bool started = manager.StartHost();
            Debug.Log($"[NetHello] relay host started={started}");
            File.WriteAllText(CodeFile, code);
            float deadline = Time.realtimeSinceStartup + hostTimeout;
            bool seen = false;
            manager.OnClientConnectedCallback += _ => seen = true;
            while (!seen && Time.realtimeSinceStartup < deadline)
                await Task.Delay(500);
            if (seen)
            {
                Debug.Log("[NetHello] PASS: relay client connected");
                Application.Quit(0);
            }
            else
            {
                Debug.Log("[NetHello] FAIL: relay host timeout");
                Application.Quit(1);
            }
        }

        async Task RunJoinRelay()
        {
            float deadline = Time.realtimeSinceStartup + joinTimeout;
            while (!File.Exists(CodeFile)
                && Time.realtimeSinceStartup < deadline)
            {
                await Task.Delay(500);
            }
            string code = File.ReadAllText(CodeFile).Trim();
            await NetBootstrap.EnsureServicesAsync();
            var join = await RelayService.Instance
                .JoinAllocationAsync(code);
            var data = join.ToRelayServerData("dtls");
            Debug.Log($"[NetHello] relay join endpoint {data.Endpoint}"
                + $" region {join.Region}");
            var manager = NetworkManager.Singleton;
            var utp = (UnityTransport)
                manager.NetworkConfig.NetworkTransport;
            utp.SetRelayServerData(data);
            bool started = manager.StartClient();
            Debug.Log($"[NetHello] relay client started={started}");
            while (!manager.IsConnectedClient
                && Time.realtimeSinceStartup < deadline)
            {
                await Task.Delay(500);
            }
            if (manager.IsConnectedClient)
            {
                Debug.Log("[NetHello] PASS: relay connected to host");
                Application.Quit(0);
            }
            else
            {
                Debug.Log("[NetHello] FAIL: relay connect timeout");
                Application.Quit(1);
            }
        }

        async Task RunHostDirect()
        {
            var manager = NetBootstrap.EnsureNetworkManager();
            var utp = (Unity.Netcode.Transports.UTP.UnityTransport)
                manager.NetworkConfig.NetworkTransport;
            utp.SetConnectionData("127.0.0.1", 7777);
            bool started = manager.StartHost();
            Debug.Log($"[NetHello] direct host started={started}");
            float deadline = Time.realtimeSinceStartup + hostTimeout;
            while (manager.ConnectedClientsIds.Count < 2
                && Time.realtimeSinceStartup < deadline)
            {
                await Task.Delay(500);
            }
            if (manager.ConnectedClientsIds.Count >= 2)
            {
                Debug.Log("[NetHello] PASS: direct client connected");
                Application.Quit(0);
            }
            else
            {
                Debug.Log("[NetHello] FAIL: direct timeout");
                Application.Quit(1);
            }
        }

        async Task RunJoinDirect()
        {
            await Task.Delay(3000);
            var manager = NetBootstrap.EnsureNetworkManager();
            var utp = (Unity.Netcode.Transports.UTP.UnityTransport)
                manager.NetworkConfig.NetworkTransport;
            utp.SetConnectionData("127.0.0.1", 7777);
            bool started = manager.StartClient();
            Debug.Log($"[NetHello] direct client started={started}");
            float deadline = Time.realtimeSinceStartup + joinTimeout;
            while (!manager.IsConnectedClient
                && Time.realtimeSinceStartup < deadline)
            {
                await Task.Delay(500);
            }
            if (manager.IsConnectedClient)
            {
                Debug.Log("[NetHello] PASS: direct connected to host");
                Application.Quit(0);
            }
            else
            {
                Debug.Log("[NetHello] FAIL: direct connect timeout");
                Application.Quit(1);
            }
        }

        static bool HasCliArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == name)
                    return true;
            }
            return false;
        }
    }
}
