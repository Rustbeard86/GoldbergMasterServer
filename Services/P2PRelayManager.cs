using System.Collections.Concurrent;

namespace GoldbergMasterServer.Services;

/// <summary>
///     Manages P2P connections and relays data between peers
/// </summary>
public class P2PRelayManager
{
    private readonly ConcurrentDictionary<ulong, P2PConnection> _connections = new();
    private readonly TimeSpan _connectionTimeout;
    private readonly object _lock = new();
    private readonly LogService _logService;
    private readonly ConcurrentDictionary<ulong, HashSet<ulong>> _peerConnections = new();
    private bool _isShutdown;
    private ulong _nextConnectionId = 1;
    private long _totalBytesRelayed;

    // Statistics
    private long _totalPacketsRelayed;

    public P2PRelayManager(TimeSpan connectionTimeout, LogService logService)
    {
        _connectionTimeout = connectionTimeout;
        _logService = logService;
        _logService.Debug($"P2P Relay manager initialized with {_connectionTimeout.TotalSeconds}s timeout",
            "P2PRelayManager");
    }

    /// <summary>
    ///     Creates or gets a connection between two peers
    /// </summary>
    public ulong CreateOrGetConnection(ulong fromPeerId, ulong toPeerId, uint appId, ConnectionType type)
    {
        if (_isShutdown) return 0;

        // Check if connection already exists
        var existingConnectionId = FindConnection(fromPeerId, toPeerId, type);
        if (existingConnectionId != 0)
            // Update last activity
            if (_connections.TryGetValue(existingConnectionId, out var existing))
            {
                existing.LastActivity = DateTime.UtcNow;
                _logService.Debug($"Reusing existing connection {existingConnectionId}: {fromPeerId} -> {toPeerId}",
                    "P2PRelayManager");
                return existingConnectionId;
            }

        // Create new connection
        var connectionId = Interlocked.Increment(ref _nextConnectionId);
        var connection = new P2PConnection
        {
            ConnectionId = connectionId,
            FromPeerId = fromPeerId,
            ToPeerId = toPeerId,
            AppId = appId,
            Type = type,
            State = ConnectionState.Connecting,
            Created = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };

        _connections[connectionId] = connection;

        // Index by peer
        lock (_lock)
        {
            if (!_peerConnections.TryGetValue(fromPeerId, out var fromConnections))
            {
                fromConnections = [];
                _peerConnections[fromPeerId] = fromConnections;
            }

            fromConnections.Add(connectionId);

            if (!_peerConnections.TryGetValue(toPeerId, out var toConnections))
            {
                toConnections = [];
                _peerConnections[toPeerId] = toConnections;
            }

            toConnections.Add(connectionId);
        }

        _logService.Info(
            $"Created P2P connection {connectionId}: {fromPeerId} -> {toPeerId}, Type={type}, AppID={appId}",
            "P2PRelayManager");

        return connectionId;
    }

    /// <summary>
    ///     Finds an existing connection between two peers
    /// </summary>
    public ulong FindConnection(ulong fromPeerId, ulong toPeerId, ConnectionType type)
    {
        if (_isShutdown) return 0;

        lock (_lock)
        {
            if (!_peerConnections.TryGetValue(fromPeerId, out var connections))
                return 0;

            foreach (var connectionId in connections)
                if (_connections.TryGetValue(connectionId, out var connection))
                    if (connection.Type == type &&
                        ((connection.FromPeerId == fromPeerId && connection.ToPeerId == toPeerId) ||
                         (connection.FromPeerId == toPeerId && connection.ToPeerId == fromPeerId)))
                        return connectionId;
        }

        return 0;
    }

    /// <summary>
    ///     Gets a connection by ID
    /// </summary>
    public P2PConnection? GetConnection(ulong connectionId)
    {
        if (_isShutdown) return null;
        return _connections.TryGetValue(connectionId, out var connection) ? connection : null;
    }

    /// <summary>
    ///     Updates connection state
    /// </summary>
    public bool UpdateConnectionState(ulong connectionId, ConnectionState newState)
    {
        if (_isShutdown) return false;

        if (_connections.TryGetValue(connectionId, out var connection))
        {
            var oldState = connection.State;
            connection.State = newState;
            connection.LastActivity = DateTime.UtcNow;

            _logService.Debug($"Connection {connectionId} state changed: {oldState} -> {newState}", "P2PRelayManager");
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Records packet relay statistics
    /// </summary>
    public void RecordPacketRelayed(ulong connectionId, int packetSize)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            connection.LastActivity = DateTime.UtcNow;
            connection.PacketsRelayed++;
            connection.BytesRelayed += packetSize;

            Interlocked.Increment(ref _totalPacketsRelayed);
            Interlocked.Add(ref _totalBytesRelayed, packetSize);
        }
    }

    /// <summary>
    ///     Gets the destination peer for a connection
    /// </summary>
    public ulong? GetDestinationPeer(ulong connectionId, ulong sourcePeerId)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            // Return the other peer in the connection
            if (connection.FromPeerId == sourcePeerId)
                return connection.ToPeerId;
            if (connection.ToPeerId == sourcePeerId)
                return connection.FromPeerId;
        }

        return null;
    }

    /// <summary>
    ///     Closes a connection
    /// </summary>
    public bool CloseConnection(ulong connectionId, string reason = "Requested")
    {
        if (_isShutdown) return false;

        if (_connections.TryRemove(connectionId, out var connection))
        {
            // Remove from peer indexes
            lock (_lock)
            {
                if (_peerConnections.TryGetValue(connection.FromPeerId, out var fromConnections))
                    fromConnections.Remove(connectionId);

                if (_peerConnections.TryGetValue(connection.ToPeerId, out var toConnections))
                    toConnections.Remove(connectionId);
            }

            _logService.Info(
                $"Closed P2P connection {connectionId}: {connection.FromPeerId} <-> {connection.ToPeerId}, " +
                $"Reason={reason}, Duration={DateTime.UtcNow - connection.Created:hh\\:mm\\:ss}, " +
                $"Packets={connection.PacketsRelayed}, Bytes={connection.BytesRelayed}",
                "P2PRelayManager");

            return true;
        }

        return false;
    }

    /// <summary>
    ///     Closes all connections for a peer
    /// </summary>
    public int CloseConnectionsForPeer(ulong peerId, string reason = "Peer disconnected")
    {
        if (_isShutdown) return 0;

        var connectionsToClose = new List<ulong>();

        lock (_lock)
        {
            if (_peerConnections.TryGetValue(peerId, out var connections)) connectionsToClose.AddRange(connections);
        }

        var closedCount = 0;
        foreach (var connectionId in connectionsToClose)
            if (CloseConnection(connectionId, reason))
                closedCount++;

        if (closedCount > 0)
            _logService.Info($"Closed {closedCount} connection(s) for peer {peerId}", "P2PRelayManager");

        return closedCount;
    }

    /// <summary>
    ///     Gets all active connections for a peer
    /// </summary>
    public IEnumerable<P2PConnection> GetConnectionsForPeer(ulong peerId)
    {
        if (_isShutdown) return [];

        var result = new List<P2PConnection>();

        lock (_lock)
        {
            if (_peerConnections.TryGetValue(peerId, out var connectionIds))
                foreach (var connectionId in connectionIds)
                    if (_connections.TryGetValue(connectionId, out var connection))
                        result.Add(connection);
        }

        return result;
    }

    /// <summary>
    ///     Cleans up stale connections
    /// </summary>
    public void CleanupStaleConnections()
    {
        if (_isShutdown) return;

        var cutoff = DateTime.UtcNow - _connectionTimeout;
        var connectionsToClose = new List<ulong>();

        foreach (var (connectionId, connection) in _connections)
            if (connection.LastActivity < cutoff)
                connectionsToClose.Add(connectionId);

        foreach (var connectionId in connectionsToClose) CloseConnection(connectionId, "Timeout");

        if (connectionsToClose.Count > 0)
            _logService.Info($"Cleanup complete: Closed {connectionsToClose.Count} stale connection(s)",
                "P2PRelayManager");
    }

    /// <summary>
    ///     Gets total active connection count
    /// </summary>
    public int GetActiveConnectionCount()
    {
        return _connections.Count;
    }

    /// <summary>
    ///     Gets relay statistics
    /// </summary>
    public (long TotalPackets, long TotalBytes) GetStatistics()
    {
        return (_totalPacketsRelayed, _totalBytesRelayed);
    }

    /// <summary>
    ///     Shuts down the relay manager
    /// </summary>
    public void Shutdown()
    {
        _isShutdown = true;

        _logService.Info(
            $"P2P Relay manager shutting down: Active connections={_connections.Count}, " +
            $"Total packets relayed={_totalPacketsRelayed}, Total bytes={_totalBytesRelayed}",
            "P2PRelayManager");

        _connections.Clear();
        _peerConnections.Clear();
    }
}

/// <summary>
///     Represents a P2P connection between two peers
/// </summary>
public class P2PConnection
{
    public required ulong ConnectionId { get; init; }
    public required ulong FromPeerId { get; init; }
    public required ulong ToPeerId { get; init; }
    public required uint AppId { get; init; }
    public required ConnectionType Type { get; init; }
    public ConnectionState State { get; set; }
    public DateTime Created { get; init; }
    public DateTime LastActivity { get; set; }
    public long PacketsRelayed { get; set; }
    public long BytesRelayed { get; set; }
    public int VirtualPort { get; set; }
    public int Channel { get; set; }
}

/// <summary>
///     Type of P2P connection
/// </summary>
public enum ConnectionType
{
    /// <summary>Legacy ISteamNetworking (Network_Old)</summary>
    NetworkOld,

    /// <summary>ISteamNetworking (Network_pb)</summary>
    NetworkPb,

    /// <summary>ISteamNetworkingSockets (Networking_Sockets)</summary>
    NetworkingSockets,

    /// <summary>Alternative networking API (Networking_Messages)</summary>
    NetworkingMessages
}

/// <summary>
///     State of a P2P connection
/// </summary>
public enum ConnectionState
{
    /// <summary>Connection is being established</summary>
    Connecting,

    /// <summary>Connection is active and ready for data</summary>
    Connected,

    /// <summary>Connection is being closed</summary>
    Disconnecting,

    /// <summary>Connection is closed</summary>
    Closed
}