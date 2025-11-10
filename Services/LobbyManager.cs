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
    private readonly ConcurrentDictionary<ulong, HashSet<ulong>> _userLobbies = new();
    private bool _isShutdown;

    public LobbyManager(TimeSpan lobbyTimeout)
    {
        _lobbyTimeout = lobbyTimeout;
        // Start cleanup task
        _ = Task.Run(CleanupLoopAsync);
    }

    /// <summary>
    ///     Creates or updates a lobby
    /// </summary>
    public Task<bool> CreateOrUpdateLobbyAsync(Lobby lobby)
    {
        if (_isShutdown) return Task.FromResult(false);

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
            return Task.FromResult(false);

        // Add member if not already present
        if (lobby.Members.All(m => m.Id != member.Id)) lobby.Members.Add(member);

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

            // If owner left and there are other members, transfer ownership
            if (userId == lobby.Owner && lobby.Members.Count != 0)
            {
                lobby.Owner = lobby.Members.First().Id;
            }
            // If no members left, mark for deletion
            else if (lobby.Members.Count == 0)
            {
                lobby.Deleted = true;
                lobby.TimeDeleted = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
            foreach (var roomId in oldLobbies) _lobbies.TryRemove(roomId, out _);

            await Task.Delay(TimeSpan.FromMinutes(1));
        }
    }

    /// <summary>
    ///     Shuts down the lobby manager
    /// </summary>
    public void Shutdown()
    {
        _isShutdown = true;
    }
}