# System Architecture

## Overview
The Goldberg Master Server provides relay and coordination services for games using the Goldberg Steam Emulator in non-LAN mode. It handles peer discovery, lobby management, game server registration, and P2P relay functionality.

## Core Components

### Network Layer

#### NetworkService
- **Purpose**: UDP communication layer
- **Responsibilities**:
  - Send/receive UDP packets
  - Broadcast messages to multiple peers
  - Serialize/deserialize Protobuf messages
- **Status**: ? Complete

### Service Managers

#### PeerManager
- **Purpose**: Track active peers by AppID
- **Features**:
  - Peer registration and heartbeat tracking
  - Query peers by SteamID or AppID
  - Automatic cleanup of stale peers (30s timeout)
- **Status**: ? Complete

#### LobbyManager
- **Purpose**: Manage lobby lifecycle
- **Features**:
  - Create/update/delete lobbies
  - Query lobbies with filtering
  - Track lobby members and metadata
  - Automatic cleanup (5 minute timeout)
- **Status**: ? Complete

#### GameserverManager
- **Purpose**: Game server registry and discovery
- **Features**:
  - Register dedicated servers
  - Track server status (players, map, metadata)
  - Query servers by filters
  - Automatic offline detection and cleanup
- **Status**: ? Complete

#### P2PRelayManager
- **Purpose**: Relay P2P networking data between peers
- **Features**:
  - Route packets between peers (4 networking APIs)
  - Connection lifecycle tracking
  - Bandwidth statistics
  - Automatic timeout handling
- **Status**: ? Complete

### Message Handler

#### MessageHandler
- **Purpose**: Route incoming messages to appropriate service managers
- **Responsibilities**:
  - Parse incoming Protobuf messages
  - Dispatch to service-specific handlers
  - Coordinate responses
- **Status**: ? Complete for implemented features

## Message Flow

```
Client                    NetworkService              MessageHandler              Service Manager
  ?                             ?                            ?                           ?
  ???UDP Packet ????????????????>?                            ?                           ?
  ?                             ???Parse Protobuf???????????>?                           ?
  ?                             ?                            ???Route by Type???????????>?
  ?                             ?                            ?                           ?
  ?                             ?                            ?                    ???????????????
  ?                             ?                            ?                    ?  Business   ?
  ?                             ?                            ?                    ?   Logic     ?
  ?                             ?                            ?                    ???????????????
  ?                             ?                            ?<??Return Data??????????????
  ?                             ?<??Response Message??????????                           ?
  ?<????UDP Packet???????????????                            ?                           ?
```

## Feature Status

### ? Fully Implemented

| Feature | Manager | Status |
|---------|---------|--------|
| Peer Discovery | PeerManager | Complete |
| Lobby System | LobbyManager | Complete |
| Game Server Discovery | GameserverManager | Complete |
| P2P Relay | P2PRelayManager | Complete |

### ? Planned Features

| Feature | Priority | Phase |
|---------|----------|-------|
| Server Browser Support | MEDIUM | Phase 2.2 |
| Friend & Presence System | MEDIUM | Phase 3 |
| Stats & Achievements | MEDIUM | Phase 5 |
| Leaderboards | MEDIUM | Phase 6 |
| Database Persistence | MEDIUM | Phase 7 |

## Thread Safety

All service managers use proper synchronization:
- **ConcurrentDictionary** for main data storage
- **Locks** for non-thread-safe nested collections (HashSet)
- **Copy-inside-lock pattern** for safe enumeration
- Detailed analysis: [Thread Safety Guide](../technical/THREAD_SAFETY.md)

## Performance Characteristics

### Current Capacity
- **Concurrent Peers**: 100+ supported, 1000+ target
- **Message Latency**: <10ms typical
- **P2P Relay Overhead**: +2-10ms
- **Storage**: In-memory only (ephemeral)

### Optimization Opportunities
- Database backend for persistence (Phase 7)
- Redis caching layer (Phase 7.3)
- Message batching (Phase 7.3)
- Horizontal scaling with load balancer

## Configuration

Configuration is managed via `appsettings.json`:

```json
{
  "Port": 26900,
  "LogLevel": "Info",
  "Timeouts": {
    "Peer": "00:00:30",
    "Lobby": "00:05:00",
    "Gameserver": "00:05:00",
    "P2PConnection": "00:05:00"
  }
}
```

## Security Considerations

### Current
- Basic Steam ID validation
- Connection isolation by AppID
- Automatic timeout handling

### Future Enhancements
- Rate limiting (Phase 8.2)
- Auth ticket validation (Phase 8.1)
- Bandwidth throttling (Phase 4.3)
- DDoS protection (Phase 8.2)

## Deployment

### Requirements
- .NET 8 Runtime
- UDP port (default: 26900)
- ~200 MB RAM (base)
- Minimal CPU

### Docker Support
- Dockerfile available
- docker-compose configuration
- Health check endpoint (planned)

## Monitoring

### Available Metrics
- Active peer count
- Active lobby count  
- Active gameserver count
- P2P relay statistics (packets, bytes)

### Planned Monitoring
- Prometheus metrics endpoint (Phase 9.1)
- Grafana dashboards (Phase 9.1)
- Admin API (Phase 9.2)
- Web dashboard (Phase 9.3)

## References

- [Roadmap](../ROADMAP.md) - Development phases and timeline
- [Message Flow](MESSAGE_FLOW.md) - Detailed message routing diagrams
- [Implementation Summary](../IMPLEMENTATION_SUMMARY.md) - Feature implementation details
- [Developer Guide](../guides/DEVELOPER_GUIDE.md) - Getting started with development
