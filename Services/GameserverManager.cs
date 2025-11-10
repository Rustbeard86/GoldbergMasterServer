using System.Collections.Concurrent;
using GoldbergMasterServer.Network;

namespace GoldbergMasterServer.Services;

/// <summary>
///     Manages dedicated game server registration and discovery
/// </summary>
public class GameserverManager
{
    private readonly ConcurrentDictionary<ulong, Gameserver> _gameservers = new();
    private readonly object _lock = new();
    private readonly LogService _logService;
    private readonly ConcurrentDictionary<uint, HashSet<ulong>> _serversByApp = new();
    private bool _isShutdown;

    public GameserverManager(TimeSpan serverTimeout, LogService logService)
    {
        _logService = logService;
        _logService.Debug($"Gameserver manager initialized with {serverTimeout.TotalSeconds}s timeout",
            "GameserverManager");
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
            _logService.Warning($"Invalid server registration: ID={server.Id}, AppID={server.Appid}",
                "GameserverManager");
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
            _logService.Info(
                $"New gameserver registered: ID={server.Id}, Name={server.ServerName.ToStringUtf8()}, AppID={server.Appid}, Players={server.NumPlayers}/{server.MaxPlayerCount}",
                "GameserverManager");
        else
            _logService.Debug(
                $"Gameserver updated: ID={server.Id}, Players={server.NumPlayers}/{server.MaxPlayerCount}, Map={server.MapName.ToStringUtf8()}",
                "GameserverManager");

        return true;
    }

    /// <summary>
    ///     Gets a specific server by ID
    /// </summary>
    /// <param name="serverId">The unique server ID to look up</param>
    /// <returns>The gameserver if found, null if not found or shutdown</returns>
    /// <remarks>
    ///     Used by server browser queries and admin panel (Phase 2.2).
    ///     Currently unused but part of planned server discovery API.
    /// </remarks>
    public Gameserver? GetServer(ulong serverId)
    {
        return _isShutdown ? null : _gameservers.GetValueOrDefault(serverId);
    }

    /// <summary>
    ///     Gets all servers for a specific app
    /// </summary>
    public IEnumerable<Gameserver> GetServersForApp(uint appId)
    {
        if (_isShutdown) return [];

        List<ulong> serverIds;

        lock (_lock)
        {
            if (!_serversByApp.TryGetValue(appId, out var serverSet))
                return [];

            // Create a copy of the IDs while inside the lock to avoid enumeration issues
            serverIds = [..serverSet];
        }

        // Process outside the lock to minimize lock contention
        return serverIds
            .Select(id => _gameservers.GetValueOrDefault(id))
            .Where(s => s is { Offline: false })
            .Cast<Gameserver>();
    }

    /// <summary>
    ///     Finds servers matching specific criteria
    /// </summary>
    /// <param name="appId">The Steam AppID to filter by</param>
    /// <param name="mapName">Optional: Filter by map name (partial match, case-insensitive)</param>
    /// <param name="hasPassword">Optional: Filter by password protection status</param>
    /// <param name="minPlayers">Optional: Minimum number of players</param>
    /// <param name="maxPlayers">Optional: Maximum player capacity</param>
    /// <param name="dedicatedOnly">Optional: Only return dedicated servers</param>
    /// <param name="secureOnly">Optional: Only return VAC-secured servers</param>
    /// <param name="maxResults">Maximum number of results to return (default: 100)</param>
    /// <returns>Enumerable of gameservers matching the criteria</returns>
    /// <remarks>
    ///     Advanced server filtering for server browser (Phase 2.2).
    ///     Currently unused but will be essential for client server discovery.
    ///     Supports multiple filter combinations for flexible queries.
    /// </remarks>
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
            servers = servers.Where(s =>
                s.MapName.ToStringUtf8().Contains(mapName, StringComparison.OrdinalIgnoreCase));

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
    /// <param name="serverId">The server ID to mark as offline</param>
    /// <returns>True if server was found and marked offline, false otherwise</returns>
    /// <remarks>
    ///     Used for graceful server shutdown and admin tools (Phase 2.2).
    ///     Currently unused but allows servers to explicitly mark themselves
    ///     as offline before automatic cleanup occurs.
    /// </remarks>
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
            if (server.Offline)
                serversToRemove.Add(serverId);

        // Remove offline servers
        foreach (var serverId in serversToRemove)
            if (_gameservers.TryRemove(serverId, out var server))
            {
                lock (_lock)
                {
                    if (_serversByApp.TryGetValue(server.Appid, out var serverSet)) serverSet.Remove(serverId);
                }

                _logService.Info($"Removed offline gameserver: ID={serverId}, Name={server.ServerName.ToStringUtf8()}",
                    "GameserverManager");
            }

        if (serversToRemove.Count > 0)
            _logService.Info($"Cleanup complete: Removed {serversToRemove.Count} offline gameserver(s)",
                "GameserverManager");
    }

    /// <summary>
    ///     Gets total server count
    /// </summary>
    /// <returns>Count of active (non-offline) servers across all apps</returns>
    /// <remarks>
    ///     Used for admin dashboard and monitoring (Phase 9).
    ///     Currently unused but provides global server statistics.
    ///     Note: Can be expensive with many servers; consider caching if needed.
    /// </remarks>
    public int GetTotalServerCount()
    {
        return _gameservers.Count(s => !s.Value.Offline);
    }

    /// <summary>
    ///     Gets server count for a specific app
    /// </summary>
    /// <param name="appId">The Steam AppID to count servers for</param>
    /// <returns>Count of active servers for the specified app</returns>
    /// <remarks>
    ///     Used for statistics and monitoring dashboards (Phase 9).
    ///     Currently unused but provides per-app server statistics.
    ///     Calls GetServersForApp() internally, which is thread-safe.
    /// </remarks>
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

        lock (_lock)
        {
            _serversByApp.Clear();
        }
    }
}