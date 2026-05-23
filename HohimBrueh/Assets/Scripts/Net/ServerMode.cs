using System;

namespace FrogSmashers.Net
{
    public static class ServerMode
    {
        static readonly bool _isServer = DetectServer();

        public static bool IsServer => _isServer;
        public static bool IsClient => !_isServer;

        static bool DetectServer()
        {
#if UNITY_SERVER
            return true;
#else
            foreach (var arg in Environment.GetCommandLineArgs())
                if (arg == "-server" || arg == "--server") return true;
            return false;
#endif
        }
    }
}
