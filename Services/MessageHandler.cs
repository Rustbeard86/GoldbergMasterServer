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

            logService.Info($"Message from SteamID {message.SourceId} at {remoteEndPoint}: Type={message.MessagesCase}", "MessageHandler");

            // Use a switch to handle all relevant message types
            switch (message.MessagesCase)
            {
                case Common_Message.MessagesOneofCase.Announce:
                    var announce = message.Announce;
                    if (announce.Type == Announce.Types.MessageType.Ping)
                        await HandlePingAsync(message, announce, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.Lobby:
                    await HandleLobbyAsync(message, message.Lobby, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.LobbyMessages:
                    await HandleLobbyMessagesAsync(message, message.LobbyMessages, remoteEndPoint);
                    break;

                case Common_Message.MessagesOneofCase.None:
                    logService.Warning($"Received empty message from {remoteEndPoint}", "MessageHandler");
                    break;

                default:
                    logService.Warning($"Unhandled message type: {message.MessagesCase} from {remoteEndPoint}", "MessageHandler");
                    break;
            }
        }
        catch (InvalidProtocolBufferException ex)
        {
            logService.Warning($"Invalid protocol buffer message from {remoteEndPoint}: {ex.Message}", "MessageHandler");
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
        {
            logService.Info($"New peer connected: SteamID={peer.SteamId}, AppID={peer.AppId}, Endpoint={remoteEndPoint}", "MessageHandler");
        }
        else
        {
            logService.Debug($"Peer heartbeat: SteamID={peer.SteamId}", "MessageHandler");
        }

        var peers = peerManager.GetPeersForApp(peer.AppId, peer.SteamId).ToList();
        logService.Debug($"Sending pong with {peers.Count} peers for AppID {peer.AppId}", "MessageHandler");
        await networkService.SendPongMessageAsync(peer, peers);
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
        return [.. members
            .Select(m => peerManager.GetPeer(m.Id))
            .Where(p => p != null)
            .Cast<Peer>()];
    }
}