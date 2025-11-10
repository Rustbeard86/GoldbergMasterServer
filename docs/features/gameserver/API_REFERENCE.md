# GameserverManager API Reference

## Active Methods

### RegisterOrUpdateServer
```csharp
public void RegisterOrUpdateServer(Gameserver server)
```

Registers new server or updates existing server information.

**Actions**:
- Adds/updates server in registry
- Indexes by AppID for fast lookups
- Updates LastUpdate timestamp

**Thread-Safe**: ? Yes  
**Used By**: MessageHandler.HandleGameserverAsync()

---

### GetServersForApp
```csharp
public IEnumerable<Gameserver> GetServersForApp(uint appId)
```

Returns all active (non-offline) servers for specific AppID.

**Returns**: Enumerable of gameservers  
**Thread-Safe**: ? Yes (copy-inside-lock pattern)  
**Performance**: O(n) where n = servers for app

---

### CleanupStaleServers
```csharp
public void CleanupStaleServers()
```

Marks servers offline if not updated within timeout period (5 minutes default).

**Frequency**: Every 10 seconds (via MasterServer timer)  
**Thread-Safe**: ? Yes

---

### Shutdown
```csharp
public void Shutdown()
```

Graceful shutdown with cleanup.

**Thread-Safe**: ? Yes

---

## Planned Methods (Phase 2.2, 9)

### GetServer
```csharp
public Gameserver? GetServer(ulong serverId)
```

**Planned Use**: Direct server lookup, server browser detail view  
**Phase**: 2.2 (Server Browser Support)  
**Status**: ? Currently unused

---

### FindServers
```csharp
public IEnumerable<Gameserver> FindServers(
    uint appId,
    string? mapName = null,
    bool? hasPassword = null,
    int? minPlayers = null,
    int? maxPlayers = null,
    bool dedicatedOnly = false,
    bool secureOnly = false,
    int maxResults = 100)
```

**Planned Use**: Advanced server filtering for browser  
**Phase**: 2.2 (Server Browser Support)  
**Status**: ? Currently unused

**Parameters**:
- `appId` - Steam AppID filter
- `mapName` - Partial match, case-insensitive
- `hasPassword` - Password protection filter
- `minPlayers` - Minimum player count
- `maxPlayers` - Maximum capacity
- `dedicatedOnly` - Dedicated servers only
- `secureOnly` - VAC-secured only
- `maxResults` - Result limit

**Future Example**:
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

### MarkServerOffline
```csharp
public bool MarkServerOffline(ulong serverId)
```

**Planned Use**: Graceful server shutdown, admin tools  
**Phase**: 2.2 (Server Browser Support)  
**Status**: ? Currently unused

**Returns**: True if server found and marked offline

---

### GetTotalServerCount
```csharp
public int GetTotalServerCount()
```

**Planned Use**: Admin dashboard statistics  
**Phase**: 9 (Monitoring & Admin)  
**Status**: ? Currently unused

**Performance Note**: Can be expensive with many servers; consider caching

---

### GetServerCountForApp
```csharp
public int GetServerCountForApp(uint appId)
```

**Planned Use**: Per-app statistics, monitoring  
**Phase**: 9 (Monitoring & Admin)  
**Status**: ? Currently unused

**Future Example**:
```csharp
var csgoServers = gameserverManager.GetServerCountForApp(730);
Console.WriteLine($"CS:GO Servers: {csgoServers}");
```

---

## Thread Safety

All methods use proper synchronization:
- Main storage: `ConcurrentDictionary<ulong, Gameserver>`
- AppID index: `ConcurrentDictionary<uint, HashSet<ulong>>` with lock protection
- Copy-inside-lock pattern for safe enumeration

See [Thread Safety Guide](../../technical/THREAD_SAFETY.md) for analysis.

---

## Configuration

```csharp
_gameserverManager = new GameserverManager(
    TimeSpan.FromMinutes(5),  // Server timeout
    _logService
);
```

---

## See Also

- [Implementation Guide](IMPLEMENTATION.md)
- [Thread Safety](../../technical/THREAD_SAFETY.md)
- [Roadmap](../../ROADMAP.md) - Phase 2.2, 9
