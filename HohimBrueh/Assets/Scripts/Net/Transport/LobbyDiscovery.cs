using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;

namespace FrogSmashers.Net.Transport
{
    /// <summary>One joinable lobby as shown in the main menu list.</summary>
    public struct LobbyEntry
    {
        public string Name;
        public string RelayCode;
        public int Players;
        public int MaxPlayers;
    }

    /// <summary>
    /// Queries the public UGS lobby list (host-published sessions
    /// carrying their relay join code as a property).
    /// </summary>
    public static class LobbyDiscovery
    {
        /// <summary>Fetches the current list of joinable lobbies.</summary>
        public static async Task<List<LobbyEntry>> QueryAsync()
        {
            await NetBootstrap.EnsureServicesAsync();
            var results = await MultiplayerService.Instance
                .QuerySessionsAsync(new QuerySessionsOptions
                {
                    Count = 12,
                });
            var entries = new List<LobbyEntry>();
            foreach (var info in results.Sessions)
            {
                if (info.Properties == null
                    || !info.Properties.TryGetValue(
                        NetSession.CodeProperty, out var codeProp))
                {
                    continue;
                }
                int players = 1;
                if (info.Properties.TryGetValue(
                    NetSession.CountProperty, out var countProp))
                {
                    int.TryParse(countProp.Value, out players);
                }
                entries.Add(new LobbyEntry
                {
                    Name = info.Name,
                    RelayCode = codeProp.Value,
                    Players = players,
                    MaxPlayers = info.MaxPlayers,
                });
            }
            return entries;
        }
    }
}
