# Lobby System

## Overview

The lobby system enables players to create, join, and discover game lobbies for matchmaking and multiplayer sessions.

---

## Status

**Phase**: 1.2 Complete  
**Build**: ? Successful  
**Thread-Safety**: ? Verified  

---

## LobbyManager

### Features Implemented

? **Lobby Creation** - Players can create lobbies with metadata  
? **Lobby Updates** - Owner can update lobby settings  
? **Lobby Queries** - Search lobbies with filters  
? **Member Management** - Join, leave, owner transfer  
? **Lobby Messages** - Chat and member data  
? **Automatic Cleanup** - Remove inactive lobbies (5 minutes)  
? **Thread-Safe Operations** - Concurrent access safe  

### API Methods

#### Active Methods

**CreateOrUpdateLobbyAsync()**
```csharp
public async Task<Lobby> CreateOrUpdateLobbyAsync(Lobby lobby, ulong ownerId)
```
Creates new lobby or updates existing lobby (owner only).

**GetLobby()**
```csharp
public Lobby? GetLobby(ulong roomId)
```
Retrieves specific lobby by room ID.

**FindLobbies()**
```csharp
public IEnumerable<Lobby> FindLobbies(
    uint appId,
    Dictionary<string, ByteString> filters,
    LobbyType? type = null,
    bool? hasServer = null,
    int? minMembers = null,
    int maxResults = 50)
```
Advanced lobby search with multiple filters.

**JoinLobbyAsync()**
```csharp
public async Task<bool> JoinLobbyAsync(ulong roomId, LobbyMember member)
```
Adds member to lobby (checks capacity).

**LeaveLobbyAsync()**
```csharp
public async Task<bool> LeaveLobbyAsync(ulong roomId, ulong userId)
```
Removes member from lobby, handles owner transfer.

**DeleteLobby()**
```csharp
public void DeleteLobby(ulong roomId)
```
Soft-deletes lobby (marked deleted, cleanup later).

**CleanupStaleLobbies()**
```csharp
public void CleanupStaleLobbies()
```
Removes lobbies inactive > 5 minutes.

---

#### Planned Methods (Phase 3)

**GetLobbiesForApp()** - Simple unfiltered lobby list  
**GetUserLobbies()** - All lobbies user is member of  

**Planned Use Cases**:
- Lobby browser UI (Phase 3)
- User session management
- Reconnection after disconnect
- Duplicate join prevention

**Future Example**:
```csharp
// Check if user already in a lobby
var userLobbies = lobbyManager.GetUserLobbies(userId);
if (userLobbies.Any())
{
    Console.WriteLine($"User already in {userLobbies.Count()} lobby(ies)");
    return false;  // Can't join another
}
```

---

## Lobby Data Structure

```csharp
public class Lobby
{
    public ulong RoomId { get; set; }           // Unique lobby ID
    public uint Appid { get; set; }             // Steam AppID
    public ulong Owner { get; set; }            // Owner SteamID
    public string Name { get; set; }            // Lobby name
    public LobbyType Type { get; set; }         // Public/Private/FriendsOnly/Invisible
    public int MemberLimit { get; set; }        // Max members
    public bool Joinable { get; set; }          // Accepting joins?
    public List<LobbyMember> Members { get; set; }  // Current members
    public Dictionary<string, ByteString> Values { get; set; }  // Metadata (game mode, map, etc.)
    public ulong? GameserverId { get; set; }    // Associated server
    public DateTime LastUpdate { get; set; }    // Last activity
    public bool Deleted { get; set; }           // Soft delete flag
}
```

---

## Lobby Member Management

### Join Flow
```
Client                  Master Server          LobbyManager
  ?                           ?                     ?
  ?? JOIN Request ????????????>?                     ?
  ?                           ???JoinLobbyAsync()???>?
  ?                           ?                     ?
  ?                           ?                     ?? Check capacity
  ?                           ?                     ?? Add member
  ?                           ?                     ?? Track user?lobby
  ?                           ?<????Success??????????
  ?<??JOIN Confirmation????????                     ?
  ?                           ?                     ?
  ?                           ?? Broadcast to all??>?
  ?<??Member Joined????????????  lobby members      ?
```

### Leave Flow
```
Client                  Master Server          LobbyManager
  ?                           ?                     ?
  ?? LEAVE Request ???????????>?                     ?
  ?                           ???LeaveLobbyAsync()??>?
  ?                           ?                     ?
  ?                           ?                     ?? Remove member
  ?                           ?                     ?? Transfer owner if needed
  ?                           ?                     ?? Delete if empty
  ?                           ?<????Success??????????
  ?<??LEAVE Confirmation???????                     ?
  ?                           ?                     ?
  ?                           ?? Broadcast to all??>?
  ?<??Member Left??????????????  remaining members  ?
```

---

## Lobby Queries

### Filter Options

**Type Filter**: Public, Private, FriendsOnly, Invisible  
**Server Filter**: Has gameserver or not  
**Member Filter**: Minimum member count  
**Metadata Filters**: Custom key-value pairs  
**Result Limit**: Configurable max results  

### Query Example

```csharp
var filters = new Dictionary<string, ByteString>
{
    ["game_mode"] = ByteString.CopyFromUtf8("competitive"),
    ["map"] = ByteString.CopyFromUtf8("dust2")
};

var lobbies = lobbyManager.FindLobbies(
    appId: 730,
    filters: filters,
    type: LobbyType.Public,
    hasServer: true,
    minMembers: 2,
    maxResults: 50
);
```

---

## Configuration

**Timeout**: 5 minutes (lobbies auto-cleanup)  
**Cleanup Frequency**: Every 10 seconds  

```csharp
_lobbyManager = new LobbyManager(
    TimeSpan.FromMinutes(5),
    _logService
);
```

---

## Thread Safety

Uses `ConcurrentDictionary` for main storage, no nested non-thread-safe collections.

**Safe Operations**:
- ? Concurrent lobby creation
- ? Concurrent queries
- ? Concurrent member operations
- ? No locks needed for main dictionary

---

## Lobby Messages

### Message Types

| Type | Description |
|------|-------------|
| `JOIN` | User joining lobby |
| `LEAVE` | User leaving lobby |
| `CHANGE_OWNER` | Owner transfer |
| `MEMBER_DATA` | Member metadata update |
| `CHAT_MESSAGE` | Lobby chat |

### Broadcasting

All lobby events broadcast to all current members automatically.

---

## Future Enhancements

### Phase 3: Lobby Browser & Session Management
- [ ] GetLobbiesForApp() - Simple lobby listing
- [ ] GetUserLobbies() - User's active lobbies
- [ ] Reconnection support
- [ ] Duplicate join prevention
- [ ] User dashboard UI

---

## Integration

### For Clients

**Create Lobby**:
```csharp
var lobby = new Lobby
{
    RoomId = GenerateUniqueId(),
    Appid = 730,
    Owner = mySteamId,
    Name = "My Awesome Game",
    Type = LobbyType.Public,
    MemberLimit = 8,
    Joinable = true,
    Values = new Dictionary<string, ByteString>
    {
        ["game_mode"] = ByteString.CopyFromUtf8("competitive"),
        ["map"] = ByteString.CopyFromUtf8("de_dust2")
    }
};

await lobbyManager.CreateOrUpdateLobbyAsync(lobby, mySteamId);
```

**Find Lobbies**:
```csharp
var lobbies = lobbyManager.FindLobbies(
    appId: 730,
    filters: myFilters,
    type: LobbyType.Public,
    minMembers: 2,
    maxResults: 50
);
```

**Join Lobby**:
```csharp
var member = new LobbyMember
{
    Id = mySteamId,
    Name = "PlayerName"
};

await lobbyManager.JoinLobbyAsync(roomId, member);
```

---

## See Also

- [System Architecture](../../architecture/SYSTEM_ARCHITECTURE.md)
- [Roadmap](../../ROADMAP.md) - Phase 1.2, 3
