using System.Net;
using GoldbergMasterServer.Models;
using GoldbergMasterServer.Network;
using Google.Protobuf;

namespace GoldbergMasterServer.Services;

/// <summary>
///     Handles protocol-specific message processing
/// </summary>
public class MessageHandler(
    PeerManager peerManager,
    NetworkService networkService,
    LobbyManager lobbyManager,
    LogService logService)
{
    public async Task HandleMessageAsync(byte[] buffer, IPEndPoint remoteEndPoint)
    {
        try
        {
            var message = Common_Message.Parser.ParseFrom(buffer);

            logService.Info($"Message from SteamID {message.SourceId} at {remoteEndPoint}: Type={message.MessagesCase}",
                "MessageHandler");

            // Use a switch to handle all relevant message types
            switch (message.MessagesCase)
            {
                case Common_Message.MessagesOneofCase.Announce:
                    var announce = message.Announce;
                    if (announce.Type == Announce.Types.MessageType.Ping)
                        await HandlePingAsync(message, announce, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.LowLevel:
                    HandleLowLevel(message, message.LowLevel, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.Lobby:
                    await HandleLobbyAsync(message, message.Lobby, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.LobbyMessages:
                    await HandleLobbyMessagesAsync(message, message.LobbyMessages, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.Gameserver:
                    await HandleGameserverAsync(message, message.Gameserver, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.Friend:
                    await HandleFriendAsync(message, message.Friend, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.AuthTicket:
                    HandleAuthTicket(message, message.AuthTicket, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.FriendMessages:
                    await HandleFriendMessagesAsync(message, message.FriendMessages, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.Network:
                    HandleNetworkPb(message, message.Network, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.NetworkOld:
                    HandleNetworkOld(message, message.NetworkOld, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.NetworkingSockets:
                    HandleNetworkingSockets(message, message.NetworkingSockets, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.NetworkingMessages:
                    HandleNetworkingMessages(message, message.NetworkingMessages, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.SteamMessages:
                    await HandleSteamMessagesAsync(message, message.SteamMessages, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.GameserverStatsMessages:
                    HandleGameServerStatsMessages(message, message.GameserverStatsMessages, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.LeaderboardsMessages:
                    HandleLeaderboardsMessages(message, message.LeaderboardsMessages, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.SteamUserStatsMessages:
                    HandleSteamUserStatsMessages(message, message.SteamUserStatsMessages, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.None:
                    logService.Warning($"Received empty message from {remoteEndPoint}", "MessageHandler");
                    break;

                default:
                    logService.Warning($"Unhandled message type: {message.MessagesCase} from {remoteEndPoint}",
                        "MessageHandler");
                    break;
            }
        }
        catch (InvalidProtocolBufferException ex)
        {
            logService.Warning($"Invalid protocol buffer message from {remoteEndPoint}: {ex.Message}",
                "MessageHandler");
        }
        catch (Exception e)
        {
            logService.Error($"Error processing packet from {remoteEndPoint}: {e.Message}", "MessageHandler");
        }
    }

    private async Task HandlePingAsync(Common_Message message, Announce announce, IPEndPoint remoteEndPoint)
    {
        var peer = new Peer
        {
            SteamId = message.SourceId,
            AppId = announce.Appid,
            TcpPort = announce.TcpPort,
            EndPoint = remoteEndPoint,
            LastSeen = DateTime.UtcNow
        };

        var isNewPeer = peerManager.GetPeer(peer.SteamId) == null;
        peerManager.AddOrUpdatePeer(peer);

        if (isNewPeer)
            logService.Info(
                $"New peer connected: SteamID={peer.SteamId}, AppID={peer.AppId}, Endpoint={remoteEndPoint}",
                "MessageHandler");
        else
            logService.Debug($"Peer heartbeat: SteamID={peer.SteamId}", "MessageHandler");

        var peers = peerManager.GetPeersForApp(peer.AppId, peer.SteamId).ToList();
        logService.Debug($"Sending pong with {peers.Count} peers for AppID {peer.AppId}", "MessageHandler");
        await networkService.SendPongMessageAsync(peer, peers);
    }

    /// <summary>
    ///     Handles Low_Level messages (HEARTBEAT, CONNECT, DISCONNECT)
    /// </summary>
    private void HandleLowLevel(Common_Message message, Low_Level lowLevel, IPEndPoint remoteEndPoint)
    {
        logService.Debug($"Received Low_Level message: Type={lowLevel.Type}, SteamID={message.SourceId}",
            "MessageHandler");

        switch (lowLevel.Type)
        {
            case Low_Level.Types.MessageType.Heartbeat:
                // Update peer's last seen time
                var peer = peerManager.GetPeer(message.SourceId);
                if (peer != null)
                {
                    peer.LastSeen = DateTime.UtcNow;
                    logService.Debug($"Heartbeat from peer {message.SourceId}", "MessageHandler");
                }

                break;

            case Low_Level.Types.MessageType.Connect:
                logService.Info($"Peer {message.SourceId} sent CONNECT signal from {remoteEndPoint}", "MessageHandler");
                break;

            case Low_Level.Types.MessageType.Disconnect:
                logService.Info($"Peer {message.SourceId} sent DISCONNECT signal from {remoteEndPoint}",
                    "MessageHandler");
                // Peer will be cleaned up by the cleanup timer
                break;
        }
    }

    /// <summary>
    ///     Handles Gameserver registration and updates
    /// </summary>
    private async Task HandleGameserverAsync(Common_Message message, Gameserver gameserver, IPEndPoint remoteEndPoint)
    {
        logService.Info(
            $"Gameserver update: ID={gameserver.Id}, Name={gameserver.ServerName.ToStringUtf8()}, Map={gameserver.MapName.ToStringUtf8()}, Players={gameserver.NumPlayers}/{gameserver.MaxPlayerCount}, AppID={gameserver.Appid}",
            "MessageHandler");

        // TODO: Implement gameserver registry
        // For now, just acknowledge receipt
        logService.Debug(
            $"Gameserver details: IP={gameserver.Ip}, Port={gameserver.Port}, QueryPort={gameserver.QueryPort}, Offline={gameserver.Offline}",
            "MessageHandler");

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Handles Friend status and rich presence updates
    /// </summary>
    private async Task HandleFriendAsync(Common_Message message, Friend friend, IPEndPoint remoteEndPoint)
    {
        logService.Debug(
            $"Friend update: ID={friend.Id}, Name={friend.Name.ToStringUtf8()}, AppID={friend.Appid}, LobbyID={friend.LobbyId}",
            "MessageHandler");

        // TODO: Implement friend status tracking and broadcasting
        // This would involve maintaining a friends list and broadcasting presence updates
        await Task.CompletedTask;
    }

    /// <summary>
    ///     Handles Auth_Ticket messages (primarily CANCEL)
    /// </summary>
    private void HandleAuthTicket(Common_Message message, Auth_Ticket authTicket, IPEndPoint remoteEndPoint)
    {
        logService.Debug(
            $"Auth ticket message: Number={authTicket.Number}, Type={authTicket.Type}, SteamID={message.SourceId}",
            "MessageHandler");

        if (authTicket.Type == Auth_Ticket.Types.MessageType.Cancel)
            logService.Info($"Auth ticket {authTicket.Number} cancelled by {message.SourceId}", "MessageHandler");
        // TODO: Implement auth ticket tracking and cancellation
    }

    /// <summary>
    ///     Handles Friend_Messages (invites, etc.)
    /// </summary>
    private async Task HandleFriendMessagesAsync(Common_Message message, Friend_Messages friendMessages,
        IPEndPoint remoteEndPoint)
    {
        logService.Debug($"Friend message: Type={friendMessages.Type}, From={message.SourceId}, To={message.DestId}",
            "MessageHandler");

        switch (friendMessages.Type)
        {
            case Friend_Messages.Types.MessageType.LobbyInvite:
                logService.Info(
                    $"Lobby invite from {message.SourceId} to {message.DestId}, LobbyID={friendMessages.LobbyId}",
                    "MessageHandler");
                // TODO: Forward invite to destination peer
                break;

            case Friend_Messages.Types.MessageType.GameInvite:
                logService.Info(
                    $"Game invite from {message.SourceId} to {message.DestId}, ConnectStr={friendMessages.ConnectStr.ToStringUtf8()}",
                    "MessageHandler");
                // TODO: Forward invite to destination peer
                break;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Handles Network_pb messages (ISteamNetworking)
    /// </summary>
    private void HandleNetworkPb(Common_Message message, Network_pb network, IPEndPoint remoteEndPoint)
    {
        logService.Debug(
            $"Network_pb message: Channel={network.Channel}, Type={network.Type}, DataSize={network.Data.Length}, From={message.SourceId}",
            "MessageHandler");

        // TODO: Implement P2P relay functionality
        // This would involve forwarding data between peers
    }

    /// <summary>
    ///     Handles Network_Old messages (legacy ISteamNetworking)
    /// </summary>
    private void HandleNetworkOld(Common_Message message, Network_Old networkOld, IPEndPoint remoteEndPoint)
    {
        logService.Debug(
            $"Network_Old message: Type={networkOld.Type}, ConnectionID={networkOld.ConnectionId}, From={message.SourceId}",
            "MessageHandler");

        // TODO: Implement legacy P2P relay functionality
    }

    /// <summary>
    ///     Handles Networking_Sockets messages (ISteamNetworkingSockets)
    /// </summary>
    private void HandleNetworkingSockets(Common_Message message, Networking_Sockets networkingSockets,
        IPEndPoint remoteEndPoint)
    {
        logService.Debug(
            $"NetworkingSockets message: Type={networkingSockets.Type}, VirtualPort={networkingSockets.VirtualPort}, ConnectionID={networkingSockets.ConnectionId}, MessageNum={networkingSockets.MessageNumber}",
            "MessageHandler");

        // TODO: Implement ISteamNetworkingSockets relay
        // This is the modern Steam networking API
    }

    /// <summary>
    ///     Handles Networking_Messages (another networking variant)
    /// </summary>
    private void HandleNetworkingMessages(Common_Message message, Networking_Messages networkingMessages,
        IPEndPoint remoteEndPoint)
    {
        logService.Debug(
            $"NetworkingMessages message: Type={networkingMessages.Type}, Channel={networkingMessages.Channel}, From={networkingMessages.IdFrom}",
            "MessageHandler");

        // TODO: Implement networking messages relay
    }

    /// <summary>
    ///     Handles Steam_Messages (chat, etc.)
    /// </summary>
    private async Task HandleSteamMessagesAsync(Common_Message message, Steam_Messages steamMessages,
        IPEndPoint remoteEndPoint)
    {
        logService.Debug($"Steam message: Type={steamMessages.Type}, From={message.SourceId}, To={message.DestId}",
            "MessageHandler");

        switch (steamMessages.Type)
        {
            case Steam_Messages.Types.MessageType.FriendChat:
                logService.Info(
                    $"Friend chat from {message.SourceId} to {message.DestId}: {steamMessages.Message.ToStringUtf8()}",
                    "MessageHandler");
                // TODO: Forward chat message to destination peer
                break;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Handles GameServerStats_Messages (server-side stats sync)
    /// </summary>
    private void HandleGameServerStatsMessages(Common_Message message, GameServerStats_Messages statsMessages,
        IPEndPoint remoteEndPoint)
    {
        logService.Debug($"GameServerStats message: Type={statsMessages.Type}, From={message.SourceId}",
            "MessageHandler");

        switch (statsMessages.Type)
        {
            case GameServerStats_Messages.Types.MessageType.RequestAllUserStats:
                logService.Info($"Game server {message.SourceId} requesting user stats", "MessageHandler");
                // TODO: Implement stats request/response
                break;

            case GameServerStats_Messages.Types.MessageType.ResponseAllUserStats:
                logService.Info($"Game server {message.SourceId} responding with user stats", "MessageHandler");
                // TODO: Forward stats response
                break;

            case GameServerStats_Messages.Types.MessageType.UpdateUserStatsFromServer:
                logService.Info($"Game server {message.SourceId} updating user stats", "MessageHandler");
                // TODO: Store and forward updated stats
                break;

            case GameServerStats_Messages.Types.MessageType.UpdateUserStatsFromUser:
                logService.Info($"User {message.SourceId} updating their stats", "MessageHandler");
                // TODO: Store and forward updated stats
                break;
        }
    }

    /// <summary>
    ///     Handles Leaderboards_Messages (leaderboard sync)
    /// </summary>
    private void HandleLeaderboardsMessages(Common_Message message, Leaderboards_Messages leaderboardsMessages,
        IPEndPoint remoteEndPoint)
    {
        logService.Debug(
            $"Leaderboards message: Type={leaderboardsMessages.Type}, AppID={leaderboardsMessages.Appid}, Board={leaderboardsMessages.LeaderboardInfo?.BoardName}",
            "MessageHandler");

        switch (leaderboardsMessages.Type)
        {
            case Leaderboards_Messages.Types.MessageType.UpdateUserScore:
                logService.Info($"User {message.SourceId} updating leaderboard score", "MessageHandler");
                // TODO: Store leaderboard scores
                break;

            case Leaderboards_Messages.Types.MessageType.UpdateUserScoreMutual:
                logService.Info($"User {message.SourceId} updating and requesting leaderboard scores",
                    "MessageHandler");
                // TODO: Store and respond with leaderboard data
                break;

            case Leaderboards_Messages.Types.MessageType.RequestUserScore:
                logService.Info($"User {message.SourceId} requesting leaderboard scores", "MessageHandler");
                // TODO: Respond with leaderboard data
                break;
        }
    }

    /// <summary>
    ///     Handles Steam_User_Stats_Messages (user stats sync between peers)
    /// </summary>
    private void HandleSteamUserStatsMessages(Common_Message message, Steam_User_Stats_Messages statsMessages,
        IPEndPoint remoteEndPoint)
    {
        logService.Debug($"Steam_User_Stats message: Type={statsMessages.Type}, From={message.SourceId}",
            "MessageHandler");

        switch (statsMessages.Type)
        {
            case Steam_User_Stats_Messages.Types.MessageType.RequestUserstats:
                logService.Info($"User stats request from {message.SourceId}", "MessageHandler");
                // TODO: Forward stats request to appropriate peer
                break;

            case Steam_User_Stats_Messages.Types.MessageType.ResponseUserstats:
                logService.Info($"User stats response from {message.SourceId}", "MessageHandler");
                // TODO: Forward stats response to requesting peer
                break;
        }
    }

    /// <summary>
    ///     Handles Lobby creation, updates, and queries
    /// </summary>
    private async Task HandleLobbyAsync(Common_Message message, Lobby lobby, IPEndPoint remoteEndPoint)
    {
        logService.Debug($"Received Lobby message from {message.SourceId} for room {lobby.RoomId}", "MessageHandler");

        var sender = peerManager.GetPeer(message.SourceId);
        if (sender == null)
        {
            logService.Warning($"Unknown peer {message.SourceId} sent lobby message", "MessageHandler");
            return;
        }

        // If RoomId is 0, this is a lobby query
        if (lobby.RoomId == 0)
        {
            await HandleLobbyQueryAsync(message, lobby, sender);
            return;
        }

        // Update lobby owner if not set
        if (lobby.Owner == 0) lobby.Owner = message.SourceId;

        // Handle lobby based on its state
        if (lobby.Deleted)
        {
            await lobbyManager.DeleteLobbyAsync(lobby.RoomId);

            // Broadcast deletion to all members
            var membersToNotify = GetOnlineMembers(lobby.Members);
            if (membersToNotify.Count != 0) await networkService.BroadcastLobbyUpdateAsync(lobby, membersToNotify);
        }
        else
        {
            var success = await lobbyManager.CreateOrUpdateLobbyAsync(lobby);
            if (success)
            {
                // Get all online members
                var membersToNotify = GetOnlineMembers(lobby.Members);
                if (membersToNotify.Count != 0)
                    // Broadcast update to all members
                    await networkService.BroadcastLobbyUpdateAsync(lobby, membersToNotify);
            }
        }
    }

    /// <summary>
    ///     Handles lobby query messages and sends responses
    /// </summary>
    private async Task HandleLobbyQueryAsync(Common_Message message, Lobby queryLobby, Peer sender)
    {
        logService.Debug($"Processing lobby query from {message.SourceId} for app {queryLobby.Appid}",
            "MessageHandler");

        // Get matching lobbies
        var matchingLobbies = lobbyManager.FindLobbies(queryLobby.Appid, queryLobby.Values);

        // Send each matching lobby as a separate message
        foreach (var lobby in matchingLobbies)
        {
            var responseMessage = new Common_Message
            {
                SourceId = message.DestId, // Server's ID
                DestId = message.SourceId, // Querying peer's ID
                Lobby = lobby
            };

            var buffer = responseMessage.ToByteArray();
            try
            {
                await networkService.SendLobbyMessageAsync(new Lobby_Messages { Id = lobby.RoomId }, sender);
            }
            catch (Exception ex)
            {
                logService.Error($"Failed to send lobby query response: {ex.Message}", "MessageHandler");
            }
        }
    }

    /// <summary>
    ///     Handles Lobby messages like joins, leaves, chat, etc.
    /// </summary>
    private async Task HandleLobbyMessagesAsync(Common_Message message, Lobby_Messages lobbyMessages,
        IPEndPoint remoteEndPoint)
    {
        logService.Debug(
            $"Received LobbyMessage (type: {lobbyMessages.Type}) from {message.SourceId} for lobby {lobbyMessages.Id}",
            "MessageHandler");

        var lobby = lobbyManager.GetLobby(lobbyMessages.Id);
        if (lobby == null)
        {
            logService.Warning($"Lobby {lobbyMessages.Id} not found", "MessageHandler");
            return;
        }

        var sender = peerManager.GetPeer(message.SourceId);
        if (sender == null)
        {
            logService.Warning($"Unknown peer {message.SourceId} sent lobby message", "MessageHandler");
            return;
        }

        // Get all online members for broadcasting
        var onlineMembers = GetOnlineMembers(lobby.Members);

        switch (lobbyMessages.Type)
        {
            case Lobby_Messages.Types.MessageType.Join:
                var member = new Lobby.Types.Member
                {
                    Id = message.SourceId
                };
                if (await lobbyManager.JoinLobbyAsync(lobbyMessages.Id, member))
                {
                    if (onlineMembers.Count != 0)
                        // Broadcast join to all members
                        await networkService.BroadcastLobbyMessageAsync(lobbyMessages, message.SourceId, onlineMembers);
                    // Send current lobby state to the new member
                    await networkService.BroadcastLobbyUpdateAsync(lobby, [sender]);
                }

                break;

            case Lobby_Messages.Types.MessageType.Leave:
                if (await lobbyManager.LeaveLobbyAsync(lobbyMessages.Id, message.SourceId) && onlineMembers.Count != 0)
                    // Broadcast leave to remaining members
                    await networkService.BroadcastLobbyMessageAsync(lobbyMessages, message.SourceId, onlineMembers);
                break;

            case Lobby_Messages.Types.MessageType.ChangeOwner:
                if (lobby.Owner == message.SourceId) // Only current owner can change ownership
                {
                    lobby.Owner = lobbyMessages.Idata;
                    if (await lobbyManager.CreateOrUpdateLobbyAsync(lobby) && onlineMembers.Count != 0)
                        // Broadcast owner change
                        await networkService.BroadcastLobbyMessageAsync(lobbyMessages, message.SourceId, onlineMembers);
                }
                else
                {
                    logService.Warning(
                        $"Non-owner {message.SourceId} attempted to change lobby {lobbyMessages.Id} ownership",
                        "MessageHandler");
                }

                break;

            case Lobby_Messages.Types.MessageType.MemberData:
                if (lobbyMessages.Map != null &&
                    await lobbyManager.UpdateMemberDataAsync(lobbyMessages.Id, message.SourceId, lobbyMessages.Map) &&
                    onlineMembers.Count != 0)
                    // Broadcast member data update
                    await networkService.BroadcastLobbyMessageAsync(lobbyMessages, message.SourceId, onlineMembers);
                break;

            case Lobby_Messages.Types.MessageType.ChatMessage:
                if (onlineMembers.Count != 0)
                    // Forward chat message to all other members
                    await networkService.BroadcastLobbyMessageAsync(lobbyMessages, message.SourceId, onlineMembers);
                break;
        }
    }

    /// <summary>
    ///     Gets a list of online peers for the given lobby members
    /// </summary>
    private List<Peer> GetOnlineMembers(IEnumerable<Lobby.Types.Member> members)
    {
        return
        [
            .. members
                .Select(m => peerManager.GetPeer(m.Id))
                .Where(p => p != null)
                .Cast<Peer>()
        ];
    }
}