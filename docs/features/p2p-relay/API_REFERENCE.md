# P2P Relay Manager API Reference

## Overview

`P2PRelayManager` manages peer-to-peer connections and relays data between peers when direct connections aren't possible due to NAT/firewalls.

---

## Supported Networking APIs

- **NetworkOld** - Legacy ISteamNetworking (Network_Old) - Stub only
- **NetworkPb** - ISteamNetworking (Network_pb) - ? Full support
- **NetworkingSockets** - Modern ISteamNetworkingSockets - ? Full support
- **NetworkingMessages** - Alternative networking API - ? Full support

---

## Connection States

| State | Description |
|-------|-------------|
| `Connecting` | Connection being established |
| `Connected` | Active and ready for data |
| `Disconnecting` | Being closed |
| `Closed` | Terminated |

---

## Active API Methods

### CreateOrGetConnection
```csharp
public ulong CreateOrGetConnection(
    ulong fromPeerId, 
    ulong toPeerId, 
    uint appId, 
    ConnectionType type)
```

Creates a new connection or returns existing connection ID. Connections are bi-directional.

**Returns**: Connection ID (unique identifier)  
**Thread-Safe**: ? Yes  
**Used By**: MessageHandler (all relay handlers)

---

### FindConnection
```csharp
public ulong FindConnection(
    ulong fromPeerId, 
    ulong toPeerId, 
    ConnectionType type)
```

Finds existing connection between two peers. Works in both directions (A?B or B?A).

**Returns**: Connection ID, or 0 if not found  
**Thread-Safe**: ? Yes  
**Performance**: O(k) where k = peer's connection count (typically 1-5)

---

### UpdateConnectionState
```csharp
public void UpdateConnectionState(
    ulong connectionId, 
    ConnectionState newState)
```

Updates connection lifecycle state.

**Common Transitions**:
- `Connecting` ? `Connected` (on accept)
- `Connected` ? `Disconnecting` (on end request)
- `Disconnecting` ? `Closed` (on cleanup)

**Thread-Safe**: ? Yes

---

### RecordPacketRelayed
```csharp
public void RecordPacketRelayed(
    ulong connectionId, 
    int packetSize)
```

Records statistics for relayed packets.

**Updates**:
- Connection packet count (+1)
- Connection byte count (+packetSize)
- Global statistics
- Last activity timestamp

**Thread-Safe**: ? Yes (atomic operations)

---

### CloseConnection
```csharp
public void CloseConnection(ulong connectionId)
```

Closes a specific connection and removes it from tracking.

**Actions**:
- Logs final statistics
- Removes from all indices
- Updates state to `Closed`

**Thread-Safe**: ? Yes

---

### CloseConnectionsForPeer
```csharp
public void CloseConnectionsForPeer(ulong peerId)
```

Closes all connections for a peer (used when peer disconnects).

**Thread-Safe**: ? Yes  
**Use Case**: Peer cleanup on disconnect

---

### CleanupStaleConnections
```csharp
public void CleanupStaleConnections()
```

Removes connections idle beyond timeout period.

**Frequency**: Every 10 seconds (via MasterServer timer)  
**Thread-Safe**: ? Yes

---

### Shutdown
```csharp
public void Shutdown()
```

Graceful shutdown with statistics logging.

**Actions**:
- Sets shutdown flag
- Clears all connections
- Logs final statistics

**Thread-Safe**: ? Yes

---

## Planned API Methods (Phase 6)

### GetConnection
```csharp
public P2PConnection? GetConnection(ulong connectionId)
```

**Planned Use**: Admin panel connection inspection  
**Phase**: 6 (Statistics & Monitoring)  
**Status**: ? Currently unused

**Future Example**:
```csharp
var conn = relayManager.GetConnection(connectionId);
Console.WriteLine($"Packets: {conn.PacketsRelayed}");
Console.WriteLine($"Bytes: {conn.BytesRelayed}");
```

---

### GetDestinationPeer
```csharp
public ulong? GetDestinationPeer(
    ulong connectionId, 
    ulong sourcePeerId)
```

**Planned Use**: Broadcast relay patterns (one-to-many)  
**Phase**: 4.3 (Advanced Relay Features)  
**Status**: ? Currently unused

---

### GetConnectionsForPeer
```csharp
public IEnumerable<P2PConnection> GetConnectionsForPeer(ulong peerId)
```

**Planned Use**: Admin monitoring, debugging  
**Phase**: 6 (Statistics & Monitoring)  
**Status**: ? Currently unused  
**Performance**: O(k) where k = peer's connections

**Future Example**:
```csharp
var connections = relayManager.GetConnectionsForPeer(peerId);
Console.WriteLine($"Peer has {connections.Count()} active connections");
```

---

### GetActiveConnectionCount
```csharp
public int GetActiveConnectionCount()
```

**Planned Use**: Dashboard metrics, capacity planning  
**Phase**: 6 (Statistics & Monitoring)  
**Status**: ? Currently unused  
**Performance**: O(1)

**Future Example**:
```csharp
var active = relayManager.GetActiveConnectionCount();
Console.WriteLine($"Active P2P Connections: {active}");
```

---

### GetStatistics
```csharp
public (long TotalPackets, long TotalBytes) GetStatistics()
```

**Planned Use**: Throughput tracking, cost analysis  
**Phase**: 6 (Statistics & Monitoring)  
**Status**: ? Currently unused  
**Performance**: O(1)

**Future Example**:
```csharp
var (packets, bytes) = relayManager.GetStatistics();
Console.WriteLine($"Total relayed: {bytes / 1024.0 / 1024.0:F2} MB");
```

---

## P2PConnection Class

```csharp
public class P2PConnection
{
    public ulong ConnectionId { get; init; }
    public ulong FromPeerId { get; init; }
    public ulong ToPeerId { get; init; }
    public uint AppId { get; init; }
    public ConnectionType Type { get; init; }
    public ConnectionState State { get; set; }
    public DateTime Created { get; init; }
    public DateTime LastActivity { get; set; }
    public long PacketsRelayed { get; set; }
    public long BytesRelayed { get; set; }
    
    // Optional fields
    public uint? VirtualPort { get; set; }
    public uint? Channel { get; set; }
}
```

---

## Thread Safety

All public methods are thread-safe for concurrent access:
- `_connections` uses `ConcurrentDictionary`
- `_peerConnections` uses locks for non-thread-safe HashSet values
- Copy-inside-lock pattern for safe enumeration

See [Thread Safety Guide](../../technical/THREAD_SAFETY.md) for detailed analysis.

---

## Configuration

```csharp
// In MasterServer.cs
_p2pRelayManager = new P2PRelayManager(
    TimeSpan.FromMinutes(5),  // Connection timeout
    _peerManager,
    logService
);
```

**Recommended Timeouts**:
- 5 minutes (default) - Most games
- 2 minutes - Fast-paced games
- 10 minutes - Turn-based games

---

## Statistics

### Per Connection
- Packets relayed count
- Bytes relayed count
- Duration (Created to Closed)

### Global (Planned Phase 6)
- Total packets relayed
- Total bytes relayed
- Active connection count

---

## See Also

- [Implementation Guide](IMPLEMENTATION.md) - Complete feature documentation
- [Thread Safety](../../technical/THREAD_SAFETY.md) - Thread-safety analysis
- [System Architecture](../../architecture/SYSTEM_ARCHITECTURE.md) - Overall design
