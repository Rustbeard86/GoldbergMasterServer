using System.Collections.Concurrent;

namespace GoldbergMasterServer.Services;

/// <summary>
///     Manages P2P (peer-to-peer) connections and relays data between peers
/// </summary>
/// <remarks>
///     <para>
///         The P2PRelayManager enables clients to communicate through the master server when direct
///         connections aren't possible due to NAT, firewalls, or network topology restrictions.
///         This is essential for multiplayer gaming scenarios where direct peer-to-peer connections fail.
///     </para>
///     <para>
///         <b>Supported Networking APIs:</b><br />
///         - NetworkOld: Legacy ISteamNetworking (Network_Old)<br />
///         - NetworkPb: ISteamNetworking (Network_pb)<br />
///         - NetworkingSockets: Modern ISteamNetworkingSockets<br />
///         - NetworkingMessages: Alternative networking API
///     </para>
///     <para>
///         <b>Thread Safety:</b><br />
///         All public methods are thread-safe for concurrent access. The _peerConnections dictionary
///         uses locks to protect non-thread-safe HashSet values. See P2P_RELAY_THREAD_SAFETY_FIX.md
///         for detailed thread-safety analysis.
///     </para>
/// </remarks>
public class P2PRelayManager
{
    /// <summary>Stores all active P2P connections indexed by connection ID</summary>
    private readonly ConcurrentDictionary<ulong, P2PConnection> _connections = new();

    /// <summary>Maximum time a connection can be idle before cleanup</summary>
    private readonly TimeSpan _connectionTimeout;

    /// <summary>Protects access to _peerConnections HashSet values (thread-safety)</summary>
    private readonly object _lock = new();

    /// <summary>Logging service for connection events and relay operations</summary>
    private readonly LogService _logService;

    /// <summary>Indexes connection IDs by peer ID for fast peer-based lookups</summary>
    /// <remarks>
    ///     Maps peer ID → HashSet of connection IDs.
    ///     The HashSet values are NOT thread-safe and must be accessed under _lock.
    ///     See P2P_RELAY_THREAD_SAFETY_FIX.md for details.
    /// </remarks>
    private readonly ConcurrentDictionary<ulong, HashSet<ulong>> _peerConnections = new();

    /// <summary>Indicates whether the relay manager has been shut down</summary>
    private bool _isShutdown;

    /// <summary>Counter for generating unique connection IDs (thread-safe with Interlocked)</summary>
    private ulong _nextConnectionId = 1;

    /// <summary>Total bytes relayed across all connections (thread-safe with Interlocked)</summary>
    private long _totalBytesRelayed;

    /// <summary>Total packets relayed across all connections (thread-safe with Interlocked)</summary>
    private long _totalPacketsRelayed;

    /// <summary>
    ///     Initializes a new instance of the P2PRelayManager
    /// </summary>
    /// <param name="connectionTimeout">Maximum idle time before a connection is considered stale</param>
    /// <param name="logService">Logging service for relay operations</param>
    /// <remarks>
    ///     Recommended timeout values:
    ///     - 5 minutes (default): Good for most games
    ///     - 2 minutes: Fast-paced games with short sessions
    ///     - 10 minutes: Turn-based or slow-paced games
    /// </remarks>
    public P2PRelayManager(TimeSpan connectionTimeout, LogService logService)
    {
        _connectionTimeout = connectionTimeout;
        _logService = logService;
        _logService.Debug($"P2P Relay manager initialized with {_connectionTimeout.TotalSeconds}s timeout",
            "P2PRelayManager");
    }

    /// <summary>
    ///     Creates a new P2P connection or retrieves an existing one between two peers
    /// </summary>
    /// <param name="fromPeerId">The Steam ID of the peer initiating the connection</param>
    /// <param name="toPeerId">The Steam ID of the destination peer</param>
    /// <param name="appId">The Steam AppID for game isolation</param>
    /// <param name="type">The networking API type being used</param>
    /// <returns>
    ///     The connection ID for the created or existing connection.
    ///     Returns 0 if the manager has been shut down.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method is idempotent - calling it multiple times with the same peers and type
    ///         will return the same connection ID. Connections are bi-directional, so A→B and B→A
    ///         use the same connection ID.
    ///     </para>
    ///     <para>
    ///         The connection is initially created in the <see cref="ConnectionState.Connecting" /> state
    ///         and should be transitioned to <see cref="ConnectionState.Connected" /> using
    ///         <see cref="UpdateConnectionState" /> once the handshake is complete.
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> This method is thread-safe and can be called concurrently
    ///         from multiple threads.
    ///     </para>
    /// </remarks>
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

        // Index by peer (protected by lock for thread-safety)
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
    /// <param name="fromPeerId">The Steam ID of the first peer</param>
    /// <param name="toPeerId">The Steam ID of the second peer</param>
    /// <param name="type">The networking API type to match</param>
    /// <returns>
    ///     The connection ID if found, 0 if no matching connection exists or manager is shut down.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This search is bi-directional: A→B and B→A will find the same connection.
    ///         Only active (non-closed) connections are considered.
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> This method acquires a lock on _lock to safely enumerate
    ///         the peer connections HashSet.
    ///     </para>
    ///     <para>
    ///         <b>Performance:</b> O(k) where k = number of connections for the peer (typically small)
    ///     </para>
    /// </remarks>
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
    ///     Gets a connection by its unique connection ID
    /// </summary>
    /// <param name="connectionId">The connection ID to look up</param>
    /// <returns>
    ///     The <see cref="P2PConnection" /> if found, null if not found or manager is shut down.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         <b>Current Status:</b> Currently unused but provides direct connection lookup.
    ///     </para>
    ///     <para>
    ///         <b>Planned Use (Phase 6 - Statistics & Monitoring):</b><br />
    ///         - Admin panel connection inspection<br />
    ///         - Debugging relay issues<br />
    ///         - Connection state monitoring<br />
    ///         - Performance analysis tools
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> Thread-safe, uses ConcurrentDictionary.
    ///     </para>
    ///     <para>
    ///         <b>Performance:</b> O(1) dictionary lookup.
    ///     </para>
    /// </remarks>
    public P2PConnection? GetConnection(ulong connectionId)
    {
        if (_isShutdown) return null;
        return _connections.GetValueOrDefault(connectionId);
    }

    /// <summary>
    ///     Updates the state of a P2P connection
    /// </summary>
    /// <param name="connectionId">The connection ID to update</param>
    /// <param name="newState">The new connection state</param>
    /// <returns>True if the connection was found and updated, false otherwise</returns>
    /// <remarks>
    ///     <para>
    ///         Common state transitions:<br />
    ///         - Connecting → Connected: Handshake completed<br />
    ///         - Connected → Disconnecting: Graceful shutdown initiated<br />
    ///         - Connected → Closed: Connection terminated
    ///     </para>
    ///     <para>
    ///         The LastActivity timestamp is updated whenever the state changes,
    ///         preventing premature timeout during state transitions.
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> Thread-safe, uses ConcurrentDictionary.
    ///     </para>
    /// </remarks>
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
    ///     Records statistics for a relayed packet
    /// </summary>
    /// <param name="connectionId">The connection ID that relayed the packet</param>
    /// <param name="packetSize">The size of the packet in bytes</param>
    /// <remarks>
    ///     <para>
    ///         Updates both per-connection statistics (PacketsRelayed, BytesRelayed)
    ///         and global statistics (_totalPacketsRelayed, _totalBytesRelayed).
    ///     </para>
    ///     <para>
    ///         Also updates the connection's LastActivity timestamp to prevent timeout
    ///         during active relay operations.
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> Thread-safe. Uses Interlocked operations for counters
    ///         and ConcurrentDictionary for connection lookup.
    ///     </para>
    ///     <para>
    ///         <b>Performance:</b> Very fast, O(1) lookup + atomic increments.
    ///     </para>
    /// </remarks>
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
    ///     Gets the destination peer for a connection given the source peer
    /// </summary>
    /// <param name="connectionId">The connection ID to query</param>
    /// <param name="sourcePeerId">The Steam ID of the source peer</param>
    /// <returns>
    ///     The Steam ID of the destination peer (the other peer in the connection),
    ///     or null if the connection is not found or the source peer is not part of the connection.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         <b>Current Status:</b> Currently unused but provides bi-directional peer lookup.
    ///     </para>
    ///     <para>
    ///         <b>Planned Use (Phase 4 - P2P Relay Enhancement):</b><br />
    ///         - Broadcast relay patterns (one-to-many)<br />
    ///         - Connection mirroring/duplication<br />
    ///         - Advanced routing logic<br />
    ///         - Multi-hop relay scenarios
    ///     </para>
    ///     <para>
    ///         <b>Example Use Case:</b><br />
    ///         When implementing broadcast relay, this allows finding all destination peers
    ///         for a given source peer without iterating through all connections.
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> Thread-safe, uses ConcurrentDictionary.
    ///     </para>
    ///     <para>
    ///         <b>Performance:</b> O(1) dictionary lookup.
    ///     </para>
    /// </remarks>
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
    ///     Closes a specific P2P connection
    /// </summary>
    /// <param name="connectionId">The connection ID to close</param>
    /// <param name="reason">The reason for closing the connection (for logging)</param>
    /// <returns>True if the connection was found and closed, false otherwise</returns>
    /// <remarks>
    ///     <para>
    ///         Performs complete cleanup:<br />
    ///         1. Removes connection from _connections dictionary<br />
    ///         2. Removes connection ID from both peers' index in _peerConnections<br />
    ///         3. Logs connection statistics (duration, packets, bytes)
    ///     </para>
    ///     <para>
    ///         <b>Common Reasons:</b><br />
    ///         - "Requested": Explicit closure by peer<br />
    ///         - "Timeout": Idle timeout exceeded<br />
    ///         - "Peer disconnected": Peer went offline
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> Uses lock on _lock to protect HashSet modifications
    ///         in _peerConnections.
    ///     </para>
    /// </remarks>
    public bool CloseConnection(ulong connectionId, string reason = "Requested")
    {
        if (_isShutdown) return false;

        if (_connections.TryRemove(connectionId, out var connection))
        {
            // Remove from peer indexes (protected by lock for thread-safety)
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
    ///     Closes all connections associated with a specific peer
    /// </summary>
    /// <param name="peerId">The Steam ID of the peer whose connections should be closed</param>
    /// <param name="reason">The reason for closing the connections (for logging)</param>
    /// <returns>The number of connections closed</returns>
    /// <remarks>
    ///     <para>
    ///         This is typically called when a peer disconnects from the master server.
    ///         All active connections (both initiated by and targeted to the peer) are closed.
    ///     </para>
    ///     <para>
    ///         <b>Process:</b><br />
    ///         1. Copy peer's connection IDs inside lock (thread-safe)<br />
    ///         2. Release lock before closing (minimize lock contention)<br />
    ///         3. Close each connection individually (updates both peers' indexes)
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> Uses lock to safely copy connection IDs from HashSet,
    ///         then processes outside lock to minimize contention.
    ///     </para>
    /// </remarks>
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
    ///     Gets all active connections for a specific peer
    /// </summary>
    /// <param name="peerId">The Steam ID of the peer to query</param>
    /// <returns>Enumerable of all active P2P connections involving the peer</returns>
    /// <remarks>
    ///     <para>
    ///         <b>Current Status:</b> Currently unused but provides per-peer connection listing.
    ///     </para>
    ///     <para>
    ///         <b>Planned Use (Phase 6 - Statistics & Monitoring):</b><br />
    ///         - Admin panel: View all connections for a peer<br />
    ///         - Debugging: Inspect active peer connections<br />
    ///         - User dashboard: Show connection status<br />
    ///         - Load balancing: Identify high-traffic peers
    ///     </para>
    ///     <para>
    ///         <b>Example Use Cases:</b><br />
    ///         - Admin views peer connections in dashboard<br />
    ///         - Debugging why peer A can't connect to peer B<br />
    ///         - Monitoring relay server load per peer<br />
    ///         - Generating connection reports
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> Copies connection IDs inside lock, then processes
    ///         outside lock to minimize contention. See P2P_RELAY_THREAD_SAFETY_FIX.md.
    ///     </para>
    ///     <para>
    ///         <b>Performance:</b> O(k) where k = peer's connection count (typically 1-5).
    ///         Fast for normal use cases.
    ///     </para>
    /// </remarks>
    public IEnumerable<P2PConnection> GetConnectionsForPeer(ulong peerId)
    {
        if (_isShutdown) return [];

        List<ulong> connectionIds;

        lock (_lock)
        {
            if (!_peerConnections.TryGetValue(peerId, out var connectionSet))
                return [];

            // Create a copy of the IDs while inside the lock to avoid enumeration issues
            connectionIds = [..connectionSet];
        }

        // Process outside the lock to minimize lock contention
        var result = new List<P2PConnection>();
        foreach (var connectionId in connectionIds)
            if (_connections.TryGetValue(connectionId, out var connection))
                result.Add(connection);

        return result;
    }

    /// <summary>
    ///     Removes stale (idle) connections that have exceeded the timeout period
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Called periodically (every 10 seconds in MasterServer) to cleanup idle connections.
    ///         Connections are considered stale if their LastActivity timestamp is older than
    ///         the configured _connectionTimeout.
    ///     </para>
    ///     <para>
    ///         <b>Process:</b><br />
    ///         1. Identify stale connections (LastActivity &lt; cutoff)<br />
    ///         2. Close each stale connection (triggers full cleanup)<br />
    ///         3. Log total number of connections cleaned up
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> Enumerates ConcurrentDictionary (thread-safe snapshot).
    ///         CloseConnection() handles thread-safe removal.
    ///     </para>
    ///     <para>
    ///         <b>Performance:</b> O(n) where n = total connections. Efficient since most
    ///         connections are active and skipped quickly.
    ///     </para>
    /// </remarks>
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
    ///     Gets the total number of active P2P connections
    /// </summary>
    /// <returns>Count of active connections across all peers and apps</returns>
    /// <remarks>
    ///     <para>
    ///         <b>Current Status:</b> Currently unused but provides global connection count.
    ///     </para>
    ///     <para>
    ///         <b>Planned Use (Phase 6 - Statistics & Monitoring):</b><br />
    ///         - Admin dashboard metrics<br />
    ///         - Server load monitoring<br />
    ///         - Capacity planning<br />
    ///         - Performance tracking
    ///     </para>
    ///     <para>
    ///         <b>Example Dashboard Display:</b><br />
    ///         "Active P2P Connections: 1,234"<br />
    ///         "Average connections per peer: 2.5"<br />
    ///         "Peak connections today: 3,456"
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> Thread-safe, ConcurrentDictionary.Count is atomic.
    ///     </para>
    ///     <para>
    ///         <b>Performance:</b> O(1) - ConcurrentDictionary maintains count.
    ///     </para>
    /// </remarks>
    public int GetActiveConnectionCount()
    {
        return _connections.Count;
    }

    /// <summary>
    ///     Gets global relay statistics
    /// </summary>
    /// <returns>
    ///     A tuple containing (TotalPackets, TotalBytes) relayed across all connections
    ///     since the manager was initialized.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         <b>Current Status:</b> Currently unused but provides global relay statistics.
    ///     </para>
    ///     <para>
    ///         <b>Planned Use (Phase 6 - Statistics & Monitoring):</b><br />
    ///         - Admin dashboard: Total relay throughput<br />
    ///         - Performance metrics: Packets/second, MB/second<br />
    ///         - Cost analysis: Bandwidth usage tracking<br />
    ///         - Capacity planning: Growth trends
    ///     </para>
    ///     <para>
    ///         <b>Example Dashboard Display:</b><br />
    ///         "Total packets relayed: 1,234,567"<br />
    ///         "Total data relayed: 5.2 GB"<br />
    ///         "Average packet size: 1,024 bytes"
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> Thread-safe. Uses Interlocked operations for atomic reads
    ///         of counter fields.
    ///     </para>
    ///     <para>
    ///         <b>Performance:</b> O(1) - Simple field access.
    ///     </para>
    ///     <para>
    ///         <b>Note:</b> Statistics are cumulative and persist until restart. For per-connection
    ///         statistics, use <see cref="GetConnection(ulong)" /> or <see cref="GetConnectionsForPeer(ulong)" />.
    ///     </para>
    /// </remarks>
    public (long TotalPackets, long TotalBytes) GetStatistics()
    {
        return (_totalPacketsRelayed, _totalBytesRelayed);
    }

    /// <summary>
    ///     Shuts down the P2P relay manager and releases all resources
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Shutdown Process:</b><br />
    ///         1. Sets _isShutdown flag (prevents new operations)<br />
    ///         2. Logs final statistics (connections, packets, bytes)<br />
    ///         3. Clears all connections (ConcurrentDictionary)<br />
    ///         4. Clears peer indexes (protected by lock for thread-safety)
    ///     </para>
    ///     <para>
    ///         All active connections are immediately terminated without notification to peers.
    ///         Peers will detect the disconnection through timeout or when they attempt to send data.
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> Protects _peerConnections.Clear() with lock.
    ///         See P2P_RELAY_THREAD_SAFETY_FIX.md for details.
    ///     </para>
    ///     <para>
    ///         <b>Called By:</b> MasterServer.Dispose() during graceful shutdown.
    ///     </para>
    /// </remarks>
    public void Shutdown()
    {
        _isShutdown = true;

        _logService.Info(
            $"P2P Relay manager shutting down: Active connections={_connections.Count}, " +
            $"Total packets relayed={_totalPacketsRelayed}, Total bytes={_totalBytesRelayed}",
            "P2PRelayManager");

        _connections.Clear();

        lock (_lock)
        {
            _peerConnections.Clear();
        }
    }
}

/// <summary>
///     Represents a P2P connection between two peers
/// </summary>
/// <remarks>
///     <para>
///         A P2PConnection tracks the state and statistics of a relayed connection between
///         two peers. Connections are bi-directional - A→B and B→A use the same connection object.
///     </para>
///     <para>
///         <b>Lifecycle:</b><br />
///         1. Created: ConnectionState = Connecting<br />
///         2. Handshake complete: ConnectionState = Connected<br />
///         3. Data relay: PacketsRelayed and BytesRelayed increment<br />
///         4. Termination: ConnectionState = Disconnecting/Closed
///     </para>
/// </remarks>
public class P2PConnection
{
    /// <summary>Unique identifier for this connection (auto-generated)</summary>
    public required ulong ConnectionId { get; init; }

    /// <summary>Steam ID of the peer that initiated the connection</summary>
    public required ulong FromPeerId { get; init; }

    /// <summary>Steam ID of the destination peer</summary>
    public required ulong ToPeerId { get; init; }

    /// <summary>Steam AppID for game isolation (prevents cross-game connections)</summary>
    public required uint AppId { get; init; }

    /// <summary>Networking API type used for this connection</summary>
    public required ConnectionType Type { get; init; }

    /// <summary>Current state of the connection (Connecting, Connected, etc.)</summary>
    public ConnectionState State { get; set; }

    /// <summary>Timestamp when the connection was created</summary>
    public DateTime Created { get; init; }

    /// <summary>Timestamp of the last packet relayed or state change</summary>
    /// <remarks>Used for idle timeout detection in CleanupStaleConnections()</remarks>
    public DateTime LastActivity { get; set; }

    /// <summary>Total number of packets relayed through this connection</summary>
    /// <remarks>Updated by RecordPacketRelayed() - includes both directions</remarks>
    public long PacketsRelayed { get; set; }

    /// <summary>Total bytes relayed through this connection</summary>
    /// <remarks>Updated by RecordPacketRelayed() - includes both directions</remarks>
    public long BytesRelayed { get; set; }

    /// <summary>Virtual port used for NetworkingSockets connections (optional)</summary>
    /// <remarks>Only used by ISteamNetworkingSockets API, 0 for other types</remarks>
    public int VirtualPort { get; set; }

    /// <summary>Channel ID used for Network_pb and NetworkingMessages (optional)</summary>
    /// <remarks>Only used by specific networking APIs, 0 for others</remarks>
    public int Channel { get; set; }
}

/// <summary>
///     Type of P2P connection indicating which networking API is being used
/// </summary>
/// <remarks>
///     Different games use different Steam networking APIs. The relay manager supports
///     all major APIs to ensure compatibility with various Goldberg Emulator clients.
/// </remarks>
public enum ConnectionType
{
    /// <summary>Legacy ISteamNetworking (Network_Old) - Minimal implementation, rarely used</summary>
    NetworkOld,

    /// <summary>ISteamNetworking (Network_pb) - Channel-based networking, widely used</summary>
    NetworkPb,

    /// <summary>ISteamNetworkingSockets (Networking_Sockets) - Modern API with full features</summary>
    NetworkingSockets,

    /// <summary>Alternative networking API (Networking_Messages) - Simplified messaging</summary>
    NetworkingMessages
}

/// <summary>
///     State of a P2P connection
/// </summary>
/// <remarks>
///     Represents the current lifecycle stage of a relayed P2P connection.
/// </remarks>
public enum ConnectionState
{
    /// <summary>Connection is being established (initial state)</summary>
    /// <remarks>Transitions to Connected after handshake completes</remarks>
    Connecting,

    /// <summary>Connection is active and ready for data relay</summary>
    /// <remarks>Primary state for active connections</remarks>
    Connected,

    /// <summary>Connection is being gracefully closed</summary>
    /// <remarks>Transitional state before Closed</remarks>
    Disconnecting,

    /// <summary>Connection has been terminated and cleaned up</summary>
    /// <remarks>Final state, connection object will be removed</remarks>
    Closed
}