# Game Server Discovery System

## Overview

The game server discovery system allows dedicated servers to register with the master server and enables clients to browse and query available servers.

---

## Status

**Phase**: 2.1 Complete | 2.2 Pending  
**Build**: ? Successful  
**Thread-Safety**: ? Verified  

---

## GameserverManager

### Features Implemented

? **Server Registration** - Dedicated servers can register  
? **Server Updates** - Track players, map, metadata changes  
? **Server Queries** - Filter servers by criteria  
? **Automatic Offline Detection** - Cleanup stale servers  
? **Thread-Safe Operations** - Concurrent access safe  

### API Methods

#### Active Methods

**RegisterOrUpdateServer()**
```csharp
public void RegisterOrUpdateServer(Gameserver server)
```
Registers new server or updates existing server information.

**GetServersForApp()**
```csharp
public IEnumerable<Gameserver> GetServersForApp(uint appId)
```
Returns all non-offline servers for a specific AppID.

**CleanupStaleServers()**
```csharp
public void CleanupStaleServers()
```
Removes servers that haven't updated within timeout period (5 minutes default).

---

#### Planned Methods (Phase 2.2, 9)

**GetServer()** - Direct server lookup by ID  
**FindServers()** - Advanced filtering (map, players, password, dedicated, secure)  
**MarkServerOffline()** - Graceful shutdown support  
**GetTotalServerCount()** - Dashboard statistics  
**GetServerCountForApp()** - Per-app statistics  

**Planned Use Cases**:
- Server browser with filters (Phase 2.2)
- Admin dashboard (Phase 9)
- Server management tools

---

## Server Registration Flow

```
Dedicated Server          Master Server         GameserverManager
       ?                        ?                      ?
       ?? Gameserver Msg ???????>?                      ?
       ?  (name, map,            ???Parse???????????????>?
       ?   players, etc.)        ?                      ?
       ?                        ?                      ?? RegisterOrUpdate
       ?                        ?                      ?  - Store server
       ?                        ?                      ?  - Index by AppID
       ?                        ?                      ?  - Update LastSeen
       ?                        ?<??????????????????????
       ?<??? Success ?????????????                      ?
```

---

## Server Query Flow (Planned Phase 2.2)

```
Client                   Master Server         GameserverManager
  ?                           ?                      ?
  ?? Query Request ???????????>?                      ?
  ?  (appId, filters)         ???FindServers()???????>?
  ?                           ?                      ?
  ?                           ?                      ?? Filter by:
  ?                           ?                      ?  - AppID
  ?                           ?                      ?  - Map name
  ?                           ?                      ?  - Player count
  ?                           ?                      ?  - Has password
  ?                           ?                      ?  - Dedicated
  ?                           ?                      ?  - Secure (VAC)
  ?                           ?<??Server List?????????
  ?<??? Server List ????????????                      ?
```

---

## Gameserver Data Structure

```csharp
public class Gameserver
{
    public ulong Id { get; set; }              // Unique server ID
    public uint Appid { get; set; }            // Steam AppID
    public string Name { get; set; }           // Server name
    public string Map { get; set; }            // Current map
    public int Players { get; set; }           // Current players
    public int MaxPlayers { get; set; }        // Max capacity
    public string Ip { get; set; }             // Server IP
    public ushort Port { get; set; }           // Game port
    public ushort QueryPort { get; set; }      // Query port
    public bool Dedicated { get; set; }        // Is dedicated server
    public bool Secure { get; set; }           // VAC secured
    public bool HasPassword { get; set; }      // Password protected
    public Dictionary<string, ByteString> Values { get; set; }  // Metadata
    public DateTime LastUpdate { get; set; }   // Last seen time
    public bool Offline { get; set; }          // Offline flag
}
```

---

## Configuration

**Timeout**: 5 minutes (servers auto-marked offline)  
**Cleanup Frequency**: Every 10 seconds  

```csharp
_gameserverManager = new GameserverManager(
    TimeSpan.FromMinutes(5),
    _logService
);
```

---

## Thread Safety

Uses copy-inside-lock pattern for safe enumeration:

```csharp
List<ulong> serverIds;
lock (_lock)
{
    if (!_serversByApp.TryGetValue(appId, out var serverSet))
        return [];
    serverIds = [..serverSet];  // Copy inside lock
}
// Process outside lock (safe, no contention)
return serverIds
    .Select(id => _gameservers.GetValueOrDefault(id))
    .Where(s => s is { Offline: false })
    .Cast<Gameserver>();
```

See [Thread Safety Guide](../../technical/THREAD_SAFETY.md) for details.

---

## Future Enhancements

### Phase 2.2: Server Browser Support
- [ ] Advanced filtering API (FindServers)
- [ ] Server list pagination
- [ ] Region/ping-based filtering
- [ ] Source query protocol passthrough

### Phase 9: Monitoring & Admin
- [ ] Server statistics dashboard
- [ ] Per-app server counts
- [ ] Admin server management
- [ ] Graceful server offline marking

---

## Integration

### For Dedicated Servers

Send periodic Gameserver messages with current status:

```csharp
var serverMsg = new Gameserver
{
    Id = myServerId,
    Appid = 730,  // CS:GO
    Name = "My Awesome Server",
    Map = "de_dust2",
    Players = currentPlayers,
    MaxPlayers = 32,
    Ip = myPublicIP,
    Port = 27015,
    QueryPort = 27015,
    Dedicated = true,
    Secure = true,
    HasPassword = false
};
// Send every 60 seconds to keep alive
```

### For Clients (Planned Phase 2.2)

Query servers with filters:

```csharp
var servers = gameserverManager.FindServers(
    appId: 730,
    mapName: "dust",
    hasPassword: false,
    minPlayers: 1,
    dedicatedOnly: true,
    secureOnly: true,
    maxResults: 50
);
```

---

## See Also

- [Thread Safety Guide](../../technical/THREAD_SAFETY.md)
- [System Architecture](../../architecture/SYSTEM_ARCHITECTURE.md)
- [Roadmap](../../ROADMAP.md) - Phase 2.2, 9
