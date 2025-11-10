using System.Net;
using GoldbergMasterServer.Models;
using GoldbergMasterServer.Network;
using Google.Protobuf;

namespace GoldbergMasterServer.Services;

/// <summary>
///     Handles protocol-specific message processing
/// </summary>
public class MessageHandler(PeerManager peerManager, NetworkService networkService, LobbyManager lobbyManager)
{
    public async Task HandleMessageAsync(byte[] buffer, IPEndPoint remoteEndPoint)
    {
        try
        {
            var message = Common_Message.Parser.ParseFrom(buffer);

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
                    // Ignore empty messages
                    break;

                default:
                    Console.WriteLine($"[WARN] Unhandled message type: {message.MessagesCase}");
                    break;
            }
        }
        catch (InvalidProtocolBufferException)
        {
            // Not a valid proto message, ignore
        }
        catch (Exception e)
        {
            Console.WriteLine($"[WARN] Error processing packet: {e.Message}");
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

        peerManager.AddOrUpdatePeer(peer);

        var peers = peerManager.GetPeersForApp(peer.AppId, peer.SteamId);
        await networkService.SendPongMessageAsync(peer, peers);
    }

    /// <summary>
    ///     Handles Lobby creation, updates, and queries
    /// </summary>
    private async Task HandleLobbyAsync(Common_Message message, Lobby lobby, IPEndPoint remoteEndPoint)
    {
        Console.WriteLine($"[INFO] Received Lobby message from {message.SourceId} for room {lobby.RoomId}");

        // Update lobby owner if not set
        if (lobby.Owner == 0) lobby.Owner = message.SourceId;

        // Handle lobby based on its state
        if (lobby.Deleted)
            await lobbyManager.DeleteLobbyAsync(lobby.RoomId);
        else
            await lobbyManager.CreateOrUpdateLobbyAsync(lobby);
    }

    /// <summary>
    ///     Handles Lobby messages like joins, leaves, chat, etc.
    /// </summary>
    private async Task HandleLobbyMessagesAsync(Common_Message message, Lobby_Messages lobbyMessages,
        IPEndPoint remoteEndPoint)
    {
        Console.WriteLine(
            $"[INFO] Received LobbyMessage (type: {lobbyMessages.Type}) from {message.SourceId} for lobby {lobbyMessages.Id}");

        var lobby = lobbyManager.GetLobby(lobbyMessages.Id);
        if (lobby == null)
        {
            Console.WriteLine($"[WARN] Lobby {lobbyMessages.Id} not found");
            return;
        }

        switch (lobbyMessages.Type)
        {
            case Lobby_Messages.Types.MessageType.Join:
                var member = new Lobby.Types.Member
                {
                    Id = message.SourceId
                };
                await lobbyManager.JoinLobbyAsync(lobbyMessages.Id, member);
                break;

            case Lobby_Messages.Types.MessageType.Leave:
                await lobbyManager.LeaveLobbyAsync(lobbyMessages.Id, message.SourceId);
                break;

            case Lobby_Messages.Types.MessageType.ChangeOwner:
                if (lobby.Owner == message.SourceId) // Only current owner can change ownership
                {
                    lobby.Owner = lobbyMessages.Idata;
                    await lobbyManager.CreateOrUpdateLobbyAsync(lobby);
                }

                break;

            case Lobby_Messages.Types.MessageType.MemberData:
                if (lobbyMessages.Map != null)
                    await lobbyManager.UpdateMemberDataAsync(lobbyMessages.Id, message.SourceId, lobbyMessages.Map);
                break;

            case Lobby_Messages.Types.MessageType.ChatMessage:
                // Chat messages are handled by broadcasting the message directly to other members
                // The Goldberg emulator handles displaying the message on the client side
                break;
        }

        // Broadcast lobby updates to all members
        // TODO: Implement lobby state broadcast
    }
}