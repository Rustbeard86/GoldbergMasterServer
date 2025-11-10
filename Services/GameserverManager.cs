using System.Collections.Concurrent;
using GoldbergMasterServer.Network;

namespace GoldbergMasterServer.Services;

/// <summary>
///     Manages dedicated game server registration and discovery
/// </summary>
public class GameserverManager
{
    private readonly LogService _logService;
    private readonly ConcurrentDictionary<ulong, Gameserver> _gameservers = new();
    private readonly ConcurrentDictionary<uint, HashSet<ulong>> _serversByApp = new();
    private readonly TimeSpan _serverTimeout;
    private readonly object _lock = new();
    private bool _isShutdown;

    public GameserverManager(TimeSpan serverTimeout, LogService logService)
    {
        _serverTimeout = serverTimeout;
        _logService = logService;
        _logService.Debug($"Gameserver manager initialized with {_serverTimeout.TotalSeconds}s timeout", "GameserverManager");
    }

    /// <summary>
    ///     Registers or updates a game server
    /// </summary>
    public bool RegisterOrUpdateServer(Gameserver server)
    {
        if (_isShutdown) return false;

        // Validate required fields
        if (server.Id == 0 || server.Appid == 0)
        {
            _logService.Warning($"Invalid server registration: ID={server.Id}, AppID={server.Appid}", "GameserverManager");
            return false;
        }

        var isNew = !_gameservers.ContainsKey(server.Id);
        _gameservers[server.Id] = server;

        // Update app index
        lock (_lock)
        {
            if (!_serversByApp.TryGetValue(server.Appid, out var serverSet))
            {
                serverSet = [];
                _serversByApp[server.Appid] = serverSet;
            }
            serverSet.Add(server.Id);
        }

        if (isNew)
        {
            _logService.Info(
                $"New gameserver registered: ID={server.Id}, Name={server.ServerName.ToStringUtf8()}, AppID={server.Appid}, Players={server.NumPlayers}/{server.MaxPlayerCount}",
                "GameserverManager");
        }
        else
        {
            _logService.Debug(
                $"Gameserver updated: ID={server.Id}, Players={server.NumPlayers}/{server.MaxPlayerCount}, Map={server.MapName.ToStringUtf8()}",
                "GameserverManager");
        }

        return true;
    }

    /// <summary>
    ///     Gets a specific server by ID
    /// </summary>
    public Gameserver? GetServer(ulong serverId)
    {
        if (_isShutdown) return null;
        return _gameservers.TryGetValue(serverId, out var server) ? server : null;
    }

    /// <summary>
    ///     Gets all servers for a specific app
    /// </summary>
    public IEnumerable<Gameserver> GetServersForApp(uint appId)
    {
        if (_isShutdown) return [];

        lock (_lock)
        {
            if (!_serversByApp.TryGetValue(appId, out var serverIds))
                return [];

            return serverIds
                .Select(id => _gameservers.TryGetValue(id, out var server) ? server : null)
                .Where(s => s != null && !s.Offline)
                .Cast<Gameserver>();
        }
    }

    /// <summary>
    ///     Finds servers matching specific criteria
    /// </summary>
    public IEnumerable<Gameserver> FindServers(
        uint appId,
        string? mapName = null,
        bool? hasPassword = null,
        int? minPlayers = null,
        int? maxPlayers = null,
        bool? dedicatedOnly = null,
        bool? secureOnly = null,
        int maxResults = 100)
    {
        if (_isShutdown) return [];

        var servers = GetServersForApp(appId);

        // Apply filters
        if (!string.IsNullOrEmpty(mapName))
            servers = servers.Where(s => s.MapName.ToStringUtf8().Contains(mapName, StringComparison.OrdinalIgnoreCase));

        if (hasPassword.HasValue)
            servers = servers.Where(s => s.PasswordProtected == hasPassword.Value);

        if (minPlayers.HasValue)
            servers = servers.Where(s => s.NumPlayers >= minPlayers.Value);

        if (maxPlayers.HasValue)
            servers = servers.Where(s => s.MaxPlayerCount <= maxPlayers.Value);

        if (dedicatedOnly.HasValue && dedicatedOnly.Value)
            servers = servers.Where(s => s.DedicatedServer);

        if (secureOnly.HasValue && secureOnly.Value)
            servers = servers.Where(s => s.Secure);

        return servers.Take(maxResults);
    }

    /// <summary>
    ///     Marks a server as offline
    /// </summary>
    public bool MarkServerOffline(ulong serverId)
    {
        if (_isShutdown) return false;

        if (_gameservers.TryGetValue(serverId, out var server))
        {
            server.Offline = true;
            _logService.Info($"Gameserver marked offline: ID={serverId}", "GameserverManager");
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Removes stale or offline servers
    /// </summary>
    public void CleanupStaleServers()
    {
        if (_isShutdown) return;

        var serversToRemove = new List<ulong>();

        // Remove servers marked as offline (we don't have a timestamp, so remove immediately)
        foreach (var (serverId, server) in _gameservers)
        {
            if (server.Offline)
            {
                serversToRemove.Add(serverId);
            }
        }

        // Remove offline servers
        foreach (var serverId in serversToRemove)
        {
            if (_gameservers.TryRemove(serverId, out var server))
            {
                lock (_lock)
                {
                    if (_serversByApp.TryGetValue(server.Appid, out var serverSet))
                    {
                        serverSet.Remove(serverId);
                    }
                }

                _logService.Info($"Removed offline gameserver: ID={serverId}, Name={server.ServerName.ToStringUtf8()}", "GameserverManager");
            }
        }

        if (serversToRemove.Count > 0)
        {
            _logService.Info($"Cleanup complete: Removed {serversToRemove.Count} offline gameserver(s)", "GameserverManager");
        }
    }

    /// <summary>
    ///     Gets total server count
    /// </summary>
    public int GetTotalServerCount()
    {
        return _gameservers.Count(s => !s.Value.Offline);
    }

    /// <summary>
    ///     Gets server count for a specific app
    /// </summary>
    public int GetServerCountForApp(uint appId)
    {
        return GetServersForApp(appId).Count();
    }

    /// <summary>
    ///     Shuts down the gameserver manager
    /// </summary>
    public void Shutdown()
    {
        _isShutdown = true;
        _logService.Info("Gameserver manager shutting down", "GameserverManager");
        _gameservers.Clear();
        _serversByApp.Clear();
    }
}
