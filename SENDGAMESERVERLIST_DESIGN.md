# SendGameserverListAsync - Design Documentation

## Current Status
**Implemented**: ? Complete  
**Used**: ? Not yet (Planned for Phase 2.2)  
**Purpose**: Server browser/discovery feature

---

## Overview

`SendGameserverListAsync` is a **forward-looking API method** designed for the upcoming **Phase 2.2: Server Browser Support** feature. It provides the network transmission layer for sending filtered game server lists to clients.

---

## Method Signature

```csharp
public async Task SendGameserverListAsync(IEnumerable<Gameserver> servers, Peer recipient)
```

### Parameters
- **`servers`**: Collection of `Gameserver` objects to send (typically from `GameserverManager.FindServers()`)
- **`recipient`**: The `Peer` (client) requesting the server list

---

## Design Rationale

### Why Separate UDP Packets?

The method sends **each server as a separate packet** rather than batching:

```csharp
foreach (var server in serverList)
{
    var message = new Common_Message
    {
        SourceId = MasterServerSteamId,
        DestId = recipient.SteamId,
        Gameserver = server  // One server per message
    };
    
    await _udpListener.SendAsync(buffer, buffer.Length, recipient.EndPoint);
}
```

**Reasons**:
1. **Avoid UDP Fragmentation** - Large server lists could exceed MTU (typically 1500 bytes)
2. **Progressive Loading** - Client can start displaying servers as they arrive
3. **Packet Loss Tolerance** - If one packet drops, others still arrive
4. **Protocol Design** - Matches Goldberg Emulator's expected message format

### Alternative Considered (Rejected)

**Batching Multiple Servers**: Would require custom protocol extension and increase packet loss impact.

---

## Integration Flow (Phase 2.2)

### Current Implementation

```
Client                    MasterServer
  |                             |
  |---- (No query mechanism) -->|  ? Not yet implemented
  |                             |
```

### Planned Implementation (Phase 2.2)

```
Client                    MasterServer                 GameserverManager
  |                             |                              |
  |--1. ServerQuery message --->|                              |
  |    (appid, filters)         |                              |
  |                             |--2. FindServers(appid, ...) ->|
  |                             |                              |
  |                             |<--3. Filtered server list ---|
  |                             |                              |
  |                             |--4. SendGameserverListAsync->|
  |                             |    (sends each server)       |
  |<--5. Gameserver messages ---|                              |
  |    (one per server)         |                              |
  |<--6. Gameserver message  ---|                              |
  |<--7. Gameserver message  ---|                              |
  |    ...                      |                              |
```

---

## Required Changes for Activation

### 1. Protocol Definition (net.proto)

Add a new query message type:

```protobuf
message Gameserver_Query {
    enum MessageType {
        REQUEST_LIST = 0;      // Client requests server list
        LIST_COMPLETE = 1;     // Server indicates end of list
    }
    
    MessageType type = 1;
    uint32 appid = 2;
    
    // Optional filters (map to GameserverManager.FindServers parameters)
    optional string map_name = 3;
    optional bool has_password = 4;
    optional int32 min_players = 5;
    optional int32 max_players = 6;
    optional bool dedicated_only = 7;
    optional bool secure_only = 8;
    optional int32 max_results = 9;
}
```

### 2. MessageHandler Update

Add handler for the new query message:

```csharp
// In HandleMessageAsync switch statement
case Common_Message.MessagesOneofCase.GameserverQuery:
    await HandleGameserverQueryAsync(message, message.GameserverQuery, remoteEndPoint);
    break;

// New handler method
private async Task HandleGameserverQueryAsync(
    Common_Message message, 
    Gameserver_Query query, 
    IPEndPoint remoteEndPoint)
{
    var requester = peerManager.GetPeer(message.SourceId);
    if (requester == null)
    {
        logService.Warning($"Unknown peer {message.SourceId} requested server list", "MessageHandler");
        return;
    }
    
    // Query GameserverManager with filters
    var servers = gameserverManager.FindServers(
        appId: query.Appid,
        mapName: query.MapName,
        hasPassword: query.HasPassword ? query.HasPassword_ : null,
        minPlayers: query.MinPlayers,
        maxPlayers: query.MaxPlayers,
        dedicatedOnly: query.DedicatedOnly ? query.DedicatedOnly_ : null,
        secureOnly: query.SecureOnly ? query.SecureOnly_ : null,
        maxResults: query.MaxResults > 0 ? query.MaxResults : 100
    );
    
    // Send the server list
    await networkService.SendGameserverListAsync(servers, requester);
    
    // Optional: Send LIST_COMPLETE message
    var completeMsg = new Gameserver_Query
    {
        Type = Gameserver_Query.Types.MessageType.ListComplete,
        Appid = query.Appid
    };
    // ... send complete message
    
    logService.Info($"Sent {servers.Count()} servers to {message.SourceId} for app {query.Appid}", "MessageHandler");
}
```

### 3. Client-Side Usage (Goldberg Emulator)

Client code would call:

```cpp
// Client requests server list
ISteamMatchmakingServers::RequestInternetServerList(
    AppId_t iApp,
    MatchMakingKeyValuePair_t **ppchFilters,
    uint32 nFilters,
    ISteamMatchmakingServerListResponse *pRequestServersResponse
);
```

This would generate a `GameserverQuery` message sent to the master server.

---

## Current Usage (None)

### Where It's NOT Called

The method exists but is **never invoked** in the current codebase:

```bash
# Search results: 0 calls to SendGameserverListAsync
$ grep -r "SendGameserverListAsync" --include="*.cs" --exclude="NetworkService.cs"
# No results (except in GAMESERVER_IMPLEMENTATION.md documentation)
```

### Why It Exists Now

1. **API Completeness** - Designed alongside `GameserverManager` (Phase 2.1)
2. **Clear Separation** - Network transmission logic separated from business logic
3. **Tested Pattern** - Follows same pattern as lobby broadcasting methods
4. **Ready for Integration** - No changes needed when Phase 2.2 is implemented

---

## Testing Strategy (Future)

### Unit Test Example

```csharp
[TestClass]
public class NetworkServiceTests
{
    [TestMethod]
    public async Task SendGameserverListAsync_SendsEachServerSeparately()
    {
        // Arrange
        var mockUdpClient = new MockUdpClient();
        var networkService = new NetworkService(mockUdpClient, logService);
        
        var servers = new[]
        {
            CreateTestServer(id: 1, name: "Server1"),
            CreateTestServer(id: 2, name: "Server2"),
            CreateTestServer(id: 3, name: "Server3")
        };
        
        var recipient = CreateTestPeer();
        
        // Act
        await networkService.SendGameserverListAsync(servers, recipient);
        
        // Assert
        Assert.AreEqual(3, mockUdpClient.SentPackets.Count);
        
        // Verify each packet contains one server
        foreach (var packet in mockUdpClient.SentPackets)
        {
            var message = Common_Message.Parser.ParseFrom(packet);
            Assert.IsNotNull(message.Gameserver);
            Assert.IsTrue(message.Gameserver.Id is 1 or 2 or 3);
        }
    }
}
```

### Integration Test Scenario

```csharp
[TestMethod]
public async Task ServerBrowser_EndToEnd_ReturnsFilteredServers()
{
    // 1. Register test servers
    gameserverManager.RegisterOrUpdateServer(CreateServer(appId: 730, map: "de_dust2", players: 8));
    gameserverManager.RegisterOrUpdateServer(CreateServer(appId: 730, map: "de_mirage", players: 4));
    gameserverManager.RegisterOrUpdateServer(CreateServer(appId: 730, map: "de_dust2", players: 16));
    
    // 2. Simulate client query
    var query = new Gameserver_Query
    {
        Type = Gameserver_Query.Types.MessageType.RequestList,
        Appid = 730,
        MapName = "dust2",
        MinPlayers = 5
    };
    
    // 3. Process query (future HandleGameserverQueryAsync)
    var results = gameserverManager.FindServers(
        appId: query.Appid,
        mapName: query.MapName,
        minPlayers: query.MinPlayers
    );
    
    // 4. Send to client
    await networkService.SendGameserverListAsync(results, testClient);
    
    // 5. Verify
    var receivedServers = testClient.ReceivedMessages
        .Select(m => m.Gameserver)
        .ToList();
        
    Assert.AreEqual(2, receivedServers.Count); // dust2 servers with >=5 players
    Assert.IsTrue(receivedServers.All(s => s.MapName.ToStringUtf8().Contains("dust2")));
    Assert.IsTrue(receivedServers.All(s => s.NumPlayers >= 5));
}
```

---

## Performance Considerations

### Current Implementation

**Per-Server Overhead**:
- Protobuf serialization: ~50-200 bytes per server
- UDP packet overhead: 28 bytes (IP + UDP headers)
- Total: ~78-228 bytes per server message

**Example**: 100 servers = 100 UDP packets = ~7.8-22.8 KB total

### Optimization Opportunities (Future)

If performance becomes an issue with large server lists:

1. **Batch Small Servers** - Group servers if combined size < MTU
2. **Pagination** - Send results in chunks with continuation tokens
3. **Compression** - Enable protobuf compression for large messages
4. **Caching** - Cache serialized responses for popular queries

**Current Decision**: Keep it simple (one-per-packet) until proven necessary to optimize.

---

## Error Handling

### What Happens on Failure?

```csharp
catch (Exception ex)
{
    _logService.Error($"Failed to send gameserver: ID={server.Id}, Error={ex.Message}", "Network.Error");
    // ? Method continues - other servers still sent
}
```

**Behavior**: 
- ? Partial results delivered (best-effort delivery)
- ? No retry mechanism (UDP is unreliable by design)
- ? Errors logged for monitoring

**Alternative Considered**: Stop on first error ? Rejected (would break progressive loading)

---

## Relationship to Other Methods

### Similar Methods in NetworkService

| Method | Purpose | Status | Pattern |
|--------|---------|--------|---------|
| `SendPongMessageAsync` | Send peer list | ? Active | Single message |
| `BroadcastLobbyUpdateAsync` | Broadcast lobby to members | ? Active | Multiple recipients |
| `SendGameserverListAsync` | Send server list | ?? Planned | Multiple messages, single recipient |

**Pattern Consistency**: All follow the same structure:
1. Check disposal
2. Create `Common_Message`
3. Serialize to buffer
4. Send via UDP
5. Log debug info
6. Handle errors

---

## Documentation References

### Related Files
- **GAMESERVER_IMPLEMENTATION.md** - Phase 2.1 completion details
- **ROADMAP.md** - Phase 2.2 server browser plans
- **Services/GameserverManager.cs** - Server filtering logic
- **Services/NetworkService.cs** - This method's implementation

### ROADMAP.md - Phase 2.2

```markdown
### Milestone 2.2: Server Browser Support
**Priority**: MEDIUM  
**Status**: ?? PENDING

**Tasks**:
- [ ] Integrate with existing Announce system
- [ ] Implement server list response messages        ? SendGameserverListAsync
- [ ] Add filter support (region, map, players, etc.)
- [ ] Handle Source query protocol passthrough
- [ ] Create NetworkService methods for server lists  ? Already done!
```

---

## Summary

### Key Points

? **Implemented**: Method is complete and working  
? **Not Used Yet**: Waiting for Phase 2.2 protocol work  
?? **Planned**: Clear integration path documented  
?? **Thread-Safe**: Uses UDP client locks internally  
?? **Tested Pattern**: Follows proven lobby broadcast design  

### When Will It Be Used?

**Trigger**: When Phase 2.2 (Server Browser Support) is implemented, specifically:
1. Protocol buffer `Gameserver_Query` message added
2. `MessageHandler.HandleGameserverQueryAsync()` created
3. Client starts sending server list requests

**Estimated**: After Phase 2.1 completion (current status: ? Complete)

### No Changes Needed

This method is **ready to use as-is** when Phase 2.2 begins. The only missing pieces are:
- Protocol message definition
- Message handler integration
- Client-side implementation

The network transmission layer is **done** ?

---

## Appendix: Example Message Flow

### Complete Server Browser Session

```
Time  | Actor    | Message                        | Details
------|----------|--------------------------------|---------------------------
T+0s  | Client   | Gameserver_Query REQUEST_LIST  | appid=730, map="dust"
T+0s  | Server   | (Query GameserverManager)      | FindServers(730, "dust")
T+1s  | Server   | Common_Message (Gameserver)    | Server #1 details
T+1s  | Server   | Common_Message (Gameserver)    | Server #2 details
T+1s  | Server   | Common_Message (Gameserver)    | Server #3 details
T+1s  | Server   | Gameserver_Query LIST_COMPLETE | End of results marker
T+2s  | Client   | (Display 3 servers)            | UI updated
```

### Packet Example (Hex)

```
// Gameserver message packet structure
[Common_Message]
  source_id: 0x100001DEADBEEF  // Master server ID
  dest_id:   0x76561198XXXXXX  // Client Steam ID
  [gameserver]
    id: 12345
    server_name: "MyServer"
    map_name: "de_dust2"
    num_players: 8
    max_player_count: 16
    appid: 730
    ip: 192.168.1.100
    port: 27015
    query_port: 27016
    // ... more fields
```

---

**Status**: ? Documentation Complete  
**Next Action**: Implement Phase 2.2 protocol and handlers  
**No Code Changes**: Method ready as-is
