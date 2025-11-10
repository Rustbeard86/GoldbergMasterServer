# Goldberg Master Server - Communication Architecture

## Overview
This document outlines the message handling architecture for the Goldberg Master Server, which provides relay and coordination services for games using the Goldberg Steam Emulator in non-LAN mode.

## Message Types & Current Implementation Status

### ? **Fully Implemented**

#### 1. **Announce (PING/PONG)**
- **Purpose**: Peer discovery and network topology exchange
- **Handler**: `HandlePingAsync()`
- **Flow**:
  1. Client sends PING with their SteamID, AppID, and TCP port
  2. Server registers/updates peer in PeerManager
  3. Server responds with PONG containing list of other peers in the same app
- **Status**: Complete and functional

#### 2. **Lobby**
- **Purpose**: Lobby creation, updates, and queries
- **Handler**: `HandleLobbyAsync()`, `HandleLobbyQueryAsync()`
- **Features**:
  - Create/update lobbies
  - Query lobbies by filters
  - Delete lobbies
  - Track lobby members, metadata, and gameserver info
- **Status**: Complete with full CRUD operations

#### 3. **Lobby_Messages**
- **Purpose**: Lobby membership and chat messages
- **Handler**: `HandleLobbyMessagesAsync()`
- **Message Types**:
  - JOIN: User joining a lobby
  - LEAVE: User leaving a lobby
  - CHANGE_OWNER: Transfer lobby ownership
  - MEMBER_DATA: Update member metadata
  - CHAT_MESSAGE: Lobby chat messages
- **Status**: Complete with broadcasting to all members

### ? **Handler Implemented (Basic)**

#### 4. **Low_Level**
- **Purpose**: Connection lifecycle management
- **Handler**: `HandleLowLevel()`
- **Message Types**:
  - HEARTBEAT: Keep-alive signal
  - CONNECT: Connection establishment notification
  - DISCONNECT: Graceful disconnect notification
- **Current Implementation**: Updates peer LastSeen time on heartbeat
- **TODO**: 
  - Implement connection state tracking
  - Handle explicit disconnects vs timeout

#### 5. **Gameserver**
- **Purpose**: Dedicated server registration and discovery
- **Handler**: `HandleGameserverAsync()`
- **Fields**: Server name, map, players, max players, IP, ports, game metadata
- **Current Implementation**: Logs server information
- **TODO**:
  - Create GameserverManager service
  - Implement server registry with filtering
  - Support ISteamMatchmakingServers queries
  - Handle server list requests from clients

#### 6. **Friend**
- **Purpose**: Friend status and rich presence updates
- **Handler**: `HandleFriendAsync()`
- **Fields**: Name, rich presence data, current app, lobby, avatar
- **Current Implementation**: Logs friend updates
- **TODO**:
  - Create FriendManager service
  - Track online friends per user
  - Broadcast presence updates to friends
  - Store and serve rich presence data

#### 7. **Auth_Ticket**
- **Purpose**: Authentication ticket lifecycle
- **Handler**: `HandleAuthTicket()`
- **Message Types**:
  - CANCEL: Revoke an auth ticket
- **Current Implementation**: Logs ticket cancellation
- **TODO**:
  - Implement ticket validation system
  - Track active tickets
  - Support ticket revocation

#### 8. **Friend_Messages**
- **Purpose**: Friend-to-friend invitations
- **Handler**: `HandleFriendMessagesAsync()`
- **Message Types**:
  - LOBBY_INVITE: Invite friend to lobby
  - GAME_INVITE: Invite friend to game (with connect string)
- **Current Implementation**: Logs invitations
- **TODO**:
  - Implement invite relay to destination peer
  - Track pending invitations
  - Support invite responses

#### 9. **Steam_Messages**
- **Purpose**: Steam platform messages (primarily chat)
- **Handler**: `HandleSteamMessagesAsync()`
- **Message Types**:
  - FRIEND_CHAT: Friend-to-friend chat messages
- **Current Implementation**: Logs chat messages
- **TODO**:
  - Implement chat relay between friends
  - Store message history (optional)
  - Support read receipts

### ?? **Handler Implemented (Relay Required)**

The following handlers are implemented but require P2P relay functionality to be useful:

#### 10. **Network_pb (ISteamNetworking)**
- **Purpose**: P2P networking data relay
- **Handler**: `HandleNetworkPb()`
- **Message Types**:
  - DATA: Raw networking data with channel
  - FAILED_CONNECT: Connection failure notification
- **TODO**: Implement full P2P relay system (see below)

#### 11. **Network_Old (Legacy ISteamNetworking)**
- **Purpose**: Legacy P2P networking API
- **Handler**: `HandleNetworkOld()`
- **Message Types**:
  - CONNECTION_REQUEST_IP: Request connection by IP
  - CONNECTION_REQUEST_STEAMID: Request connection by SteamID
  - CONNECTION_ACCEPTED: Connection established
  - CONNECTION_END: Connection terminated
  - DATA: Raw networking data
- **TODO**: Implement legacy P2P relay (lower priority)

#### 12. **Networking_Sockets (ISteamNetworkingSockets)**
- **Purpose**: Modern Steam networking API
- **Handler**: `HandleNetworkingSockets()`
- **Message Types**:
  - CONNECTION_REQUEST: Initiate connection
  - CONNECTION_ACCEPTED: Accept connection
  - CONNECTION_END: Terminate connection
  - DATA: Reliable/unreliable data with message numbers
- **TODO**: Implement modern networking relay

#### 13. **Networking_Messages**
- **Purpose**: Another networking variant
- **Handler**: `HandleNetworkingMessages()`
- **Message Types**:
  - CONNECTION_NEW: New connection
  - CONNECTION_ACCEPT: Accept connection
  - CONNECTION_END: End connection
  - DATA: Raw data
- **TODO**: Implement relay functionality

### ? **Handler Implemented (Stats/Leaderboards)**

#### 14. **GameServerStats_Messages**
- **Purpose**: Game server ? user stats synchronization
- **Handler**: `HandleGameServerStatsMessages()`
- **Message Types**:
  - Request_AllUserStats: Server requests user stats
  - Response_AllUserStats: Response with stats
  - UpdateUserStatsFromServer: Server updates user stats
  - UpdateUserStatsFromUser: User updates their stats
- **Current Implementation**: Logs stats operations
- **TODO**:
  - Create StatsManager service
  - Store user stats and achievements
  - Relay stats between gameservers and clients
  - Validate stats updates

#### 15. **Leaderboards_Messages**
- **Purpose**: Leaderboard score synchronization
- **Handler**: `HandleLeaderboardsMessages()`
- **Message Types**:
  - UpdateUserScore: Update user's score
  - UpdateUserScoreMutual: Update and request scores
  - RequestUserScore: Request leaderboard data
- **Current Implementation**: Logs leaderboard operations
- **TODO**:
  - Create LeaderboardManager service
  - Store leaderboard scores per app
  - Support score queries with ranking
  - Handle multiple leaderboards per game

#### 16. **Steam_User_Stats_Messages**
- **Purpose**: Peer-to-peer stats synchronization
- **Handler**: `HandleSteamUserStatsMessages()`
- **Message Types**:
  - REQUEST_USERSTATS: Request another user's stats
  - RESPONSE_USERSTATS: Respond with stats
- **Current Implementation**: Logs stats requests/responses
- **TODO**:
  - Relay stats requests between peers
  - Cache frequently requested stats
  - Enforce privacy settings

## Architecture Components

### Current Services

#### PeerManager
- **Purpose**: Track active peers by AppID
- **Features**:
  - Add/update peers with heartbeat tracking
  - Query peers by SteamID or AppID
  - Automatic cleanup of stale peers (30s timeout)
- **Status**: Complete

#### LobbyManager
- **Purpose**: Manage lobby lifecycle
- **Features**:
  - Create/update/delete lobbies
  - Query lobbies with filtering
  - Track lobby members and metadata
  - Automatic cleanup (5 minute timeout)
- **Status**: Complete

#### NetworkService
- **Purpose**: UDP communication layer
- **Features**:
  - Send/receive UDP packets
  - Broadcast lobby updates
  - Send direct messages
- **Status**: Complete for basic operations

### Services To Be Implemented

#### GameserverManager
- **Purpose**: Game server registry and discovery
- **Features**:
  - Register dedicated servers
  - Track server status (players, map, etc.)
  - Query servers by filters (region, map, players)
  - Handle Source query protocol integration
  - Automatic cleanup of offline servers

#### FriendManager
- **Purpose**: Friend presence and relationships
- **Features**:
  - Track online friends
  - Store friend lists per user
  - Broadcast presence updates
  - Relay friend messages
  - Rich presence data storage

#### StatsManager
- **Purpose**: Stats and achievements synchronization
- **Features**:
  - Store user stats per app
  - Store achievements and unlock status
  - Handle stats updates from users and servers
  - Validate stat modifications
  - Support stat queries

#### LeaderboardManager
- **Purpose**: Leaderboard management
- **Features**:
  - Store leaderboard scores
  - Multiple leaderboards per app
  - Score ranking and queries
  - Support different sort methods
  - Handle score details (e.g., replay data)

#### P2PRelayManager
- **Purpose**: Relay P2P networking data between peers
- **Features**:
  - Route packets between peers
  - Handle multiple networking APIs
  - Connection state tracking
  - NAT traversal assistance
  - Bandwidth management

## P2P Relay System Design

### Overview
The P2P relay system allows peers to communicate when direct connections aren't possible (NAT, firewall, etc.).

### Flow
1. **Connection Request**
   - Peer A sends connection request with destination SteamID
   - Server looks up Peer B's endpoint
   - Server forwards request to Peer B

2. **Connection Establishment**
   - Peer B accepts/rejects connection
   - Server relays response to Peer A
   - Connection state tracked on server

3. **Data Relay**
   - Peers send data to server with destination ID
   - Server forwards to destination peer
   - Supports multiple channels/ports

4. **Connection Teardown**
   - Either peer can disconnect
   - Server cleans up connection state
   - Notifies other peer

### Implementation Notes
- Use connection ID mapping to route packets
- Track connection state (CONNECTING, CONNECTED, DISCONNECTING)
- Implement bandwidth throttling per peer
- Support both reliable (TCP-like) and unreliable (UDP) channels
- Log bandwidth usage for monitoring

## Future Server-Side Features

### Custom Account Management
- User registration and authentication
- Profile management
- Custom Steam ID assignment

### Persistent Friend Lists
- Server-side friend list storage
- Friend request system
- Friend presence history

### Global Stats & Achievements
- Cross-install stats persistence
- Achievement showcase
- Global leaderboards

### Server Browser
- Web-based server browser
- Advanced filtering
- Favorite servers

### Admin Panel
- Web-based administration
- User management
- Server monitoring
- Ban management

## Security Considerations

### Current
- Basic Steam ID validation
- IP-based rate limiting (to be implemented)

### Future
- Auth ticket validation
- Encrypted communications (TLS)
- DDoS protection
- Admin authentication
- Stats validation (prevent cheating)

## Performance Considerations

### Current Bottlenecks
- All operations run on single UDP thread
- In-memory storage only

### Optimizations To Implement
- Connection pooling
- Message batching
- Async processing with work queues
- Database backend for persistence
- Caching layer (Redis)
- Horizontal scaling with load balancer

## Testing Strategy

### Unit Tests (To Implement)
- PeerManager operations
- LobbyManager operations
- Message parsing
- Stats calculations

### Integration Tests (To Implement)
- Multi-peer lobby operations
- P2P relay functionality
- Gameserver registration and queries
- Stats synchronization

### Load Tests (To Implement)
- 100+ concurrent peers
- 1000+ lobbies
- P2P relay bandwidth
- Message throughput

## Deployment Considerations

### Configuration
- Port configuration (default: 26900)
- Timeout values
- Logging levels
- Feature flags (enable/disable services)

### Monitoring
- Active peer count
- Lobby count
- Message throughput
- Error rates
- Bandwidth usage

### Maintenance
- Graceful shutdown
- Peer notification on shutdown
- State persistence on restart
- Database migrations

## References
- Goldberg Emulator: https://gitlab.com/Mr_Goldberg/goldberg_emulator
- net.proto: Message definitions
- Steam API Documentation: https://partner.steamgames.com/doc/api
