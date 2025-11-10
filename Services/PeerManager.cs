using System.Collections.Concurrent;
using GoldbergMasterServer.Models;

namespace GoldbergMasterServer.Services;

/// <summary>
///     Manages the collection of peers and their lifecycle
/// </summary>
public class PeerManager(TimeSpan peerTimeout)
{
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<ulong, Peer>> _peersByApp = new();
    private bool _isShutdown;

    public void AddOrUpdatePeer(Peer peer)
    {
        if (peer.SteamId == 0 || peer.AppId == 0) return; // Invalid peer

        // Ensure the dictionary for this AppId exists
        _peersByApp.TryAdd(peer.AppId, new ConcurrentDictionary<ulong, Peer>());

        // Get the list of peers for specific APPID
        var appPeers = _peersByApp[peer.AppId];

        // Update or add the peer
        appPeers.AddOrUpdate(peer.SteamId, peer, (_, existing) =>
        {
            existing.EndPoint = peer.EndPoint;
            existing.TcpPort = peer.TcpPort;
            existing.LastSeen = DateTime.UtcNow;
            return existing;
        });
    }

    public IEnumerable<Peer> GetPeersForApp(uint appId, ulong excludeSteamId)
    {
        return _peersByApp.TryGetValue(appId, out var appPeers)
            ? appPeers.Values.Where(p => p.SteamId != excludeSteamId)
            : [];
    }

    public void CleanupStaleMembers()
    {
        if (_isShutdown) return;

        var now = DateTime.UtcNow;

        foreach (var (appId, appPeers) in _peersByApp)
        foreach (var (steamId, peer) in appPeers)
            if (now - peer.LastSeen > peerTimeout)
                if (appPeers.TryRemove(steamId, out _))
                    Console.WriteLine($"[LEAVE] Stale peer removed: {steamId} from AppID {appId}");
    }

    public void Shutdown()
    {
        _isShutdown = true;
    }
}