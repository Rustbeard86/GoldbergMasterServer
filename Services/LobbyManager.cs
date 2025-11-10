using System.Collections.Concurrent;
using GoldbergMasterServer.Network;
using Google.Protobuf;
using Google.Protobuf.Collections;

namespace GoldbergMasterServer.Services;

/// <summary>
///     Manages lobby lifecycle and state
/// </summary>
public class LobbyManager
{
    private readonly ConcurrentDictionary<ulong, Lobby> _lobbies = new();
    private readonly TimeSpan _lobbyTimeout;
    private readonly LogService _logService;
    private readonly ConcurrentDictionary<ulong, HashSet<ulong>> _userLobbies = new();
    private bool _isShutdown;

    public LobbyManager(TimeSpan lobbyTimeout, LogService logService)
    {
        _lobbyTimeout = lobbyTimeout;
        _logService = logService;
        _logService.Debug($"Lobby manager initialized with {_lobbyTimeout.TotalMinutes}m timeout", "LobbyManager");
        // Start cleanup task
        _ = Task.Run(CleanupLoopAsync);
    }

    /// <summary>
    ///     Creates or updates a lobby
    /// </summary>
    public Task<bool> CreateOrUpdateLobbyAsync(Lobby lobby)
    {
        if (_isShutdown) return Task.FromResult(false);

        var isNew = !_lobbies.ContainsKey(lobby.RoomId);
        _lobbies.AddOrUpdate(lobby.RoomId, lobby, (_, _) => lobby);

        // Update user lobby mapping
        _userLobbies.AddOrUpdate(
            lobby.Owner,
            _ => [lobby.RoomId],
            (_, rooms) =>
            {
                rooms.Add(lobby.RoomId);
                return rooms;
            });

        _logService.Debug(
            isNew ? $"Created new lobby {lobby.RoomId} for app {lobby.Appid}" : $"Updated lobby {lobby.RoomId}",
            "LobbyManager");

        return Task.FromResult(true);
    }

    /// <summary>
    ///     Gets all lobbies for a specific app
    /// </summary>
    public IEnumerable<Lobby> GetLobbiesForApp(uint appId)
    {
        return _lobbies.Values.Where(l => l.Appid == appId && !l.Deleted);
    }

    /// <summary>
    ///     Finds lobbies matching specific criteria
    /// </summary>
    public IEnumerable<Lobby> FindLobbies(uint appId, IDictionary<string, ByteString>? filters = null,
        int maxResults = 50)
    {
        if (_isShutdown) return [];

        // Build the query
        IEnumerable<Lobby> query = _lobbies.Values;

        // Basic filters
        query = query.Where(l => l.Appid == appId && l is { Deleted: false, Joinable: true });

        // Filter by lobby type if specified in metadata
        if (filters?.TryGetValue("lobby_type", out var lobbyTypeBytes) == true)
        {
            var lobbyType = BitConverter.ToUInt32(lobbyTypeBytes.ToByteArray());
            query = query.Where(l => l.Type == lobbyType);
            _logService.Debug($"Applied lobby type filter: {lobbyType}", "LobbyManager");
        }

        // Filter by member count/limits
        query = query.Where(l => l.MemberLimit == 0 || l.Members.Count < l.MemberLimit);

        // Apply custom metadata filters if provided
        if (filters?.Any() == true)
            query = query.Where(lobby =>
                filters.All(filter =>
                {
                    // Skip special filter keys that we handle separately
                    if (filter.Key == "lobby_type") return true;

                    return lobby.Values.TryGetValue(filter.Key, out var value) &&
                           value.Equals(filter.Value);
                }));

        // Materialize the filtered results once
        var matchingLobbies = query
            .OrderByDescending(l => l.Members.Count)
            .ThenByDescending(l => l.Gameserver != null)
            .Take(maxResults)
            .ToList();

        _logService.Debug($"Found {matchingLobbies.Count} lobbies matching criteria for app {appId}", "LobbyManager");

        return matchingLobbies;
    }

    /// <summary>
    ///     Gets all lobbies that a user is a member of
    /// </summary>
    public IEnumerable<Lobby> GetUserLobbies(ulong userId)
    {
        if (_isShutdown) return [];

        if (_userLobbies.TryGetValue(userId, out var lobbyIds))
        {
            var userLobbies = lobbyIds
                .Select(id => _lobbies.GetValueOrDefault(id))
                .Where(l => l is { Deleted: false })
                .Cast<Lobby>()
                .ToList();

            _logService.Debug($"Found {userLobbies.Count} active lobbies for user {userId}", "LobbyManager");
            return userLobbies;
        }

        return [];
    }

    /// <summary>
    ///     Gets a specific lobby by ID
    /// </summary>
    public Lobby? GetLobby(ulong roomId)
    {
        _lobbies.TryGetValue(roomId, out var lobby);
        return lobby;
    }

    /// <summary>
    ///     Joins a user to a lobby
    /// </summary>
    public Task<bool> JoinLobbyAsync(ulong roomId, Lobby.Types.Member member)
    {
        if (_isShutdown || !_lobbies.TryGetValue(roomId, out var lobby))
            return Task.FromResult(false);

        if (!lobby.Joinable || lobby.Deleted ||
            (lobby.MemberLimit > 0 && lobby.Members.Count >= lobby.MemberLimit))
        {
            _logService.Debug($"Join failed for user {member.Id} to lobby {roomId}: " +
                              $"Joinable={lobby.Joinable}, Deleted={lobby.Deleted}, " +
                              $"Members={lobby.Members.Count}/{lobby.MemberLimit}", "LobbyManager");
            return Task.FromResult(false);
        }

        // Add member if not already present
        if (lobby.Members.All(m => m.Id != member.Id))
        {
            lobby.Members.Add(member);
            _logService.Debug($"User {member.Id} joined lobby {roomId}", "LobbyManager");
        }

        // Update user's lobby list
        _userLobbies.AddOrUpdate(
            member.Id,
            _ => [roomId],
            (_, rooms) =>
            {
                rooms.Add(roomId);
                return rooms;
            });

        return Task.FromResult(true);
    }

    /// <summary>
    ///     Removes a user from a lobby
    /// </summary>
    public Task<bool> LeaveLobbyAsync(ulong roomId, ulong userId)
    {
        if (_isShutdown || !_lobbies.TryGetValue(roomId, out var lobby))
            return Task.FromResult(false);

        var member = lobby.Members.FirstOrDefault(m => m.Id == userId);
        if (member != null)
        {
            lobby.Members.Remove(member);
            _logService.Debug($"User {userId} left lobby {roomId}", "LobbyManager");

            // If owner left and there are other members, transfer ownership
            if (userId == lobby.Owner && lobby.Members.Count != 0)
            {
                lobby.Owner = lobby.Members.First().Id;
                _logService.Debug($"Transferred lobby {roomId} ownership to {lobby.Owner}", "LobbyManager");
            }
            // If no members left, mark for deletion
            else if (lobby.Members.Count == 0)
            {
                lobby.Deleted = true;
                lobby.TimeDeleted = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _logService.Debug($"Marked empty lobby {roomId} for deletion", "LobbyManager");
            }

            // Remove from user's lobby list
            if (_userLobbies.TryGetValue(userId, out var rooms))
            {
                rooms.Remove(roomId);
                if (rooms.Count == 0) _userLobbies.TryRemove(userId, out _);
            }

            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    ///     Updates member data in a lobby
    /// </summary>
    public Task<bool> UpdateMemberDataAsync(ulong roomId, ulong userId, MapField<string, ByteString> values)
    {
        if (_isShutdown || !_lobbies.TryGetValue(roomId, out var lobby))
            return Task.FromResult(false);

        var member = lobby.Members.FirstOrDefault(m => m.Id == userId);
        if (member != null)
        {
            foreach (var (key, value) in values) member.Values[key] = value;
            _logService.Debug($"Updated data for user {userId} in lobby {roomId}", "LobbyManager");
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    ///     Marks a lobby as deleted
    /// </summary>
    public Task<bool> DeleteLobbyAsync(ulong roomId)
    {
        if (_isShutdown || !_lobbies.TryGetValue(roomId, out var lobby))
            return Task.FromResult(false);

        lobby.Deleted = true;
        lobby.TimeDeleted = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _logService.Debug($"Marked lobby {roomId} as deleted", "LobbyManager");
        return Task.FromResult(true);
    }

    private async Task CleanupLoopAsync()
    {
        while (!_isShutdown)
        {
            var now = DateTimeOffset.UtcNow;
            var cutoff = now.Subtract(_lobbyTimeout);

            // Find deleted lobbies older than the timeout
            var oldLobbies = _lobbies.Values
                .Where(l => l.Deleted && l.TimeDeleted < (ulong)cutoff.ToUnixTimeSeconds())
                .Select(l => l.RoomId)
                .ToList();

            // Remove old lobbies
            foreach (var roomId in oldLobbies)
            {
                _lobbies.TryRemove(roomId, out _);
                _logService.Debug($"Removed expired lobby {roomId}", "LobbyManager");
            }

            await Task.Delay(TimeSpan.FromMinutes(1));
        }
    }

    /// <summary>
    ///     Shuts down the lobby manager
    /// </summary>
    public void Shutdown()
    {
        _isShutdown = true;
        _logService.Info("Lobby manager shutting down", "LobbyManager");
    }
}