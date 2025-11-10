# Goldberg Master Server - Developer Quick Start

## Project Overview

The Goldberg Master Server is a C# (.NET 8) UDP-based master server for the Goldberg Steam Emulator. It provides peer discovery, lobby management, and relay services for games running in non-LAN mode.

## Getting Started

### Prerequisites
- .NET 8 SDK
- Visual Studio 2022 or VS Code with C# extension
- Git
- (Optional) Docker for deployment

### Building the Project

```bash
# Clone the repository
git clone https://github.com/Rustbeard86/GoldbergMasterServer.git
cd GoldbergMasterServer

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run
dotnet run
```

### Configuration

Edit `appsettings.json` to configure the server:

```json
{
  "Server": {
    "Port": 26900
  },
  "Logging": {
    "MinimumLevel": "Debug",
    "IncludeTimestamps": true,
    "IncludeSourceInfo": true
  }
}
```

### Testing with Goldberg Emulator

1. Get a game using Goldberg Emulator
2. Edit the emulator's configuration file
3. Set master server IP to your server's IP
4. Set master server port to 26900
5. Launch the game

## Project Structure

```
GoldbergMasterServer/
??? Configuration/          # Config management
?   ??? AppConfig.cs
?   ??? ConfigurationManager.cs
??? Models/                 # Data models
?   ??? Peer.cs
??? Protos/                 # Protobuf definitions
?   ??? net.proto
??? Services/               # Core services
?   ??? LogService.cs
?   ??? MessageHandler.cs
?   ??? NetworkService.cs
?   ??? PeerManager.cs
?   ??? LobbyManager.cs
??? MasterServer.cs         # Main server logic
??? Program.cs              # Entry point
??? appsettings.json        # Configuration
```

## Key Components

### MessageHandler
**File**: `Services/MessageHandler.cs`  
**Purpose**: Processes all incoming protobuf messages

**Current Handlers**:
- Announce (PING/PONG) - Peer discovery
- Lobby - Lobby CRUD operations
- Lobby_Messages - Join, leave, chat
- Low_Level - Heartbeats, connect/disconnect
- Gameserver - Server registration (stub)
- Friend - Friend presence (stub)
- And 10+ more message types

**To Add a Handler**:
```csharp
private async Task HandleMyMessageAsync(Common_Message message, MyMessage myMsg, IPEndPoint remoteEndPoint)
{
    // 1. Log the message
    logService.Debug($"Received MyMessage from {message.SourceId}", "MessageHandler");
    
    // 2. Validate the sender
    var sender = peerManager.GetPeer(message.SourceId);
    if (sender == null)
    {
        logService.Warning($"Unknown peer {message.SourceId}", "MessageHandler");
        return;
    }
    
    // 3. Process the message
    // ... your logic here ...
    
    // 4. Respond or broadcast
    await networkService.SendSomethingAsync(sender, response);
}
```

### PeerManager
**File**: `Services/PeerManager.cs`  
**Purpose**: Track active peers and their metadata

**Usage**:
```csharp
// Add or update a peer
peerManager.AddOrUpdatePeer(peer);

// Get a peer by SteamID
var peer = peerManager.GetPeer(steamId);

// Get all peers for an app
var peers = peerManager.GetPeersForApp(appId, excludeSteamId);

// Cleanup runs automatically via timer
```

### LobbyManager
**File**: `Services/LobbyManager.cs`  
**Purpose**: Manage lobby lifecycle and state

**Usage**:
```csharp
// Create or update
await lobbyManager.CreateOrUpdateLobbyAsync(lobby);

// Query lobbies
var lobbies = lobbyManager.FindLobbies(appId, filters);

// Join/leave
await lobbyManager.JoinLobbyAsync(roomId, member);
await lobbyManager.LeaveLobbyAsync(roomId, userId);

// Get lobby
var lobby = lobbyManager.GetLobby(roomId);
```

### NetworkService
**File**: `Services/NetworkService.cs`  
**Purpose**: UDP communication layer

**Usage**:
```csharp
// Receive (called by MasterServer loop)
var (buffer, endpoint) = await networkService.ReceiveAsync();

// Send pong with peer list
await networkService.SendPongMessageAsync(recipient, peers);

// Broadcast to lobby members
await networkService.BroadcastLobbyUpdateAsync(lobby, members);
await networkService.BroadcastLobbyMessageAsync(message, senderId, members);
```

## Message Flow Example: Peer Discovery

```
Client                          Server                          Other Clients
  |                               |                                   |
  |-- PING (UDP) ---------------->|                                   |
  |   (SteamID, AppID, TCPPort)   |                                   |
  |                               |                                   |
  |                               |-- Register/Update in PeerManager  |
  |                               |                                   |
  |<-- PONG (UDP) ----------------|                                   |
  |   (Peer List for AppID)       |                                   |
  |                               |                                   |
```

## Message Flow Example: Lobby Creation

```
Client A                        Server                          Client B
  |                               |                                   |
  |-- Lobby (UDP) --------------->|                                   |
  |   (Create, RoomID, Metadata)  |                                   |
  |                               |                                   |
  |                               |-- CreateOrUpdateLobbyAsync()      |
  |                               |                                   |
  |<-- Lobby (UDP) ---------------|                                   |
  |   (Confirmation)              |                                   |
  |                               |                                   |
  |-- Lobby_Messages (JOIN) ----->|                                   |
  |                               |                                   |
  |                               |-- JoinLobbyAsync()                |
  |                               |                                   |
  |                               |-- Broadcast to all members ------>|
  |<-----------------------------|                                   |
  |                               |                                   |
```

## Contributing

### Adding a New Feature

1. **Plan**: Check ROADMAP.md for priorities
2. **Design**: Update COMMUNICATION_ARCHITECTURE.md if needed
3. **Implement**: 
   - Create service if needed (e.g., GameserverManager)
   - Add handler in MessageHandler
   - Update NetworkService if new message types
4. **Test**: Add unit tests
5. **Document**: Update relevant docs
6. **Submit**: Create pull request

### Code Style

- Use C# 12 features (collection expressions, primary constructors)
- Follow Microsoft naming conventions
- Add XML documentation comments for public APIs
- Use `async`/`await` for all I/O operations
- Handle errors gracefully with try-catch
- Log appropriately (Debug, Info, Warning, Error)

### Example: Adding a Service

```csharp
namespace GoldbergMasterServer.Services;

/// <summary>
/// Brief description of what this service does
/// </summary>
public class MyNewManager
{
    private readonly LogService _logService;
    private readonly ConcurrentDictionary<ulong, MyEntity> _entities = new();
    
    public MyNewManager(LogService logService)
    {
        _logService = logService;
        _logService.Info("MyNewManager initialized", "MyNewManager");
    }
    
    public void AddEntity(MyEntity entity)
    {
        _entities.AddOrUpdate(entity.Id, entity, (_, _) => entity);
        _logService.Debug($"Entity {entity.Id} added", "MyNewManager");
    }
    
    public MyEntity? GetEntity(ulong id)
    {
        return _entities.TryGetValue(id, out var entity) ? entity : null;
    }
}
```

### Logging Best Practices

```csharp
// Debug - Verbose info for development
logService.Debug($"Processing message: {message.Type}", "MyService");

// Info - Important state changes
logService.Info($"Peer {steamId} connected", "MyService");

// Warning - Recoverable issues
logService.Warning($"Invalid message from {endpoint}", "MyService");

// Error - Errors that need attention
logService.Error($"Failed to process message: {ex.Message}", "MyService");

// Critical - Severe errors
logService.Critical($"Service crashed: {ex}", "MyService");
```

## Common Tasks

### Adding a New Message Type

1. **Define in proto** (if not already defined):
```protobuf
message My_New_Message {
    enum MessageType {
        TYPE_A = 0;
        TYPE_B = 1;
    }
    MessageType type = 1;
    uint64 id = 2;
    bytes data = 3;
}
```

2. **Add to Common_Message** oneof:
```protobuf
message Common_Message {
    // ... existing fields ...
    oneof messages {
        // ... existing messages ...
        My_New_Message my_new_message = 19;
    }
}
```

3. **Regenerate protobuf**:
```bash
protoc --csharp_out=. --csharp_opt=file_extension=.g.cs Protos/net.proto
```

4. **Add handler in MessageHandler**:
```csharp
case Common_Message.MessagesOneofCase.MyNewMessage:
    HandleMyNewMessage(message, message.MyNewMessage, remoteEndPoint);
    break;
```

5. **Implement handler**:
```csharp
private void HandleMyNewMessage(Common_Message message, My_New_Message myMsg, IPEndPoint remoteEndPoint)
{
    // Implementation
}
```

### Adding a Network Method

In `NetworkService.cs`:

```csharp
/// <summary>
/// Sends a custom message to a peer
/// </summary>
public async Task SendCustomMessageAsync(MyMessage message, Peer recipient)
{
    ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

    var commonMessage = new Common_Message
    {
        SourceId = MasterServerSteamId,
        DestId = recipient.SteamId,
        MyNewMessage = message
    };

    var buffer = commonMessage.ToByteArray();
    _logService.Debug($"Sending custom message to {recipient.SteamId}", "Network");

    try
    {
        await _udpListener.SendAsync(buffer, buffer.Length, recipient.EndPoint);
    }
    catch (Exception ex)
    {
        _logService.Error($"Failed to send: {ex.Message}", "Network");
        throw;
    }
}
```

## Debugging

### Enable Detailed Logging

Set `MinimumLevel` to `"Trace"` in appsettings.json:

```json
{
  "Logging": {
    "MinimumLevel": "Trace"
  }
}
```

### Inspecting Packets

Add packet inspection in `NetworkService.ReceiveAsync()`:

```csharp
_logService.Debug($"Packet hex: {BitConverter.ToString(result.Buffer)}", "Network.Receive");
```

### Common Issues

**"Socket operation aborted"**
- This is normal during shutdown, ignore it

**"Invalid protocol buffer message"**
- Check protobuf compatibility with client
- Verify message is complete (not truncated)

**"Peer not found"**
- Peer hasn't sent PING yet
- Peer timed out (check timeout settings)

## Testing

### Manual Testing

Use the Goldberg Emulator test client:

1. Configure two instances with same AppID
2. Start server
3. Launch both clients
4. Verify they discover each other
5. Create lobby in client 1
6. Join lobby from client 2

### Unit Test Template

```csharp
[TestClass]
public class MyManagerTests
{
    private MyManager _manager;
    private LogService _logService;
    
    [TestInitialize]
    public void Setup()
    {
        _logService = new LogService(LogLevel.None);
        _manager = new MyManager(_logService);
    }
    
    [TestMethod]
    public void AddEntity_ShouldSucceed()
    {
        // Arrange
        var entity = new MyEntity { Id = 123 };
        
        // Act
        _manager.AddEntity(entity);
        var result = _manager.GetEntity(123);
        
        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(123, result.Id);
    }
}
```

## Performance Tips

1. **Avoid blocking operations** - Use async/await
2. **Use ConcurrentDictionary** - For thread-safe collections
3. **Pool objects** - Reuse byte arrays for network buffers
4. **Batch messages** - Send multiple updates together
5. **Profile regularly** - Use dotnet-trace or PerfView

## Getting Help

- **Documentation**: Read COMMUNICATION_ARCHITECTURE.md
- **Roadmap**: Check ROADMAP.md for planned features
- **Issues**: Check GitHub issues
- **Reference**: See Goldberg Emulator source code
- **Protocol**: Study net.proto for message definitions

## Resources

- [Goldberg Emulator](https://gitlab.com/Mr_Goldberg/goldberg_emulator)
- [Protocol Buffers](https://developers.google.com/protocol-buffers)
- [.NET 8 Documentation](https://docs.microsoft.com/en-us/dotnet/)
- [Steam API Reference](https://partner.steamgames.com/doc/api)

## License

This project follows the same license as the Goldberg Emulator (LGPL v3).
