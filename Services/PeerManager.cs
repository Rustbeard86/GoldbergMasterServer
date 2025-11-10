using System.Collections.Concurrent;
using GoldbergMasterServer.Models;

namespace GoldbergMasterServer.Services;

/// <summary>
///     Manages the collection of peers and their lifecycle
/// </summary>
public class PeerManager
{
    private readonly LogService _logService;
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<ulong, Peer>> _peersByApp = new();
    private readonly TimeSpan _peerTimeout;
    private bool _isShutdown;

    public PeerManager(TimeSpan peerTimeout, LogService logService)
    {
        _peerTimeout = peerTimeout;
        _logService = logService;
        _logService.Debug($"Peer manager initialized with {_peerTimeout.TotalSeconds}s timeout", "PeerManager");
    }

    public void AddOrUpdatePeer(Peer peer)
    {
        if (_isShutdown) return;

        var appPeers = _peersByApp.GetOrAdd(peer.AppId, _ => new ConcurrentDictionary<ulong, Peer>());
        var isNew = !appPeers.ContainsKey(peer.SteamId);
        appPeers.AddOrUpdate(peer.SteamId, peer, (_, _) => peer);

        _logService.Debug(
            isNew
                ? $"New peer {peer.SteamId} added for app {peer.AppId}"
                : $"Updated peer {peer.SteamId} for app {peer.AppId}", "PeerManager");
    }

    /// <summary>
    ///     Gets a specific peer by their Steam ID
    /// </summary>
    public Peer? GetPeer(ulong steamId)
    {
        if (_isShutdown) return null;

        // Search through all apps for the peer
        foreach (var appPeers in _peersByApp.Values)
            if (appPeers.TryGetValue(steamId, out var peer))
            {
                // Check if peer is still active
                if (DateTime.UtcNow - peer.LastSeen <= _peerTimeout) return peer;

                // If peer is stale, remove it
                appPeers.TryRemove(steamId, out _);
                _logService.Debug($"Removed stale peer {steamId}", "PeerManager");
                return null;
            }

        return null;
    }

    public IEnumerable<Peer> GetPeersForApp(uint appId, ulong excludeSteamId)
    {
        if (_isShutdown) return [];

        if (!_peersByApp.TryGetValue(appId, out var appPeers))
            return [];

        var now = DateTime.UtcNow;
        return appPeers.Values
            .Where(p => p.SteamId != excludeSteamId && now - p.LastSeen <= _peerTimeout);
    }

    public void CleanupStaleMembers()
    {
        if (_isShutdown) return;

        var cutoff = DateTime.UtcNow - _peerTimeout;
        var totalRemoved = 0;

        foreach (var (appId, appPeers) in _peersByApp)
        {
            var stalePeers = appPeers.Values
                .Where(p => p.LastSeen < cutoff)
                .ToList();

            foreach (var stalePeer in stalePeers)
            {
                if (appPeers.TryRemove(stalePeer.SteamId, out _))
                {
                    totalRemoved++;
                    _logService.Info($"Peer disconnected (timeout): SteamID={stalePeer.SteamId}, AppID={appId}, LastSeen={stalePeer.LastSeen:HH:mm:ss}", "PeerManager");
                }
            }
        }

        if (totalRemoved > 0)
        {
            _logService.Info($"Cleanup complete: Removed {totalRemoved} stale peer(s)", "PeerManager");
        }
    }

    public void Shutdown()
    {
        _isShutdown = true;
        _logService.Info("Peer manager shutting down", "PeerManager");
        _peersByApp.Clear();
    }
}