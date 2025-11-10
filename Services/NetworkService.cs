using System.Net;
using System.Net.Sockets;
using GoldbergMasterServer.Models;
using GoldbergMasterServer.Network;
using Google.Protobuf;

namespace GoldbergMasterServer.Services;

/// <summary>
///     Handles UDP network communication and message processing
/// </summary>
public sealed class NetworkService : IDisposable
{
    private const ulong MasterServerSteamId = 0x100001DEADBEEF;
    private readonly LogService _logService;
    private readonly UdpClient _udpListener;
    private bool _disposed;

    public NetworkService(int port, LogService logService)
    {
        _logService = logService;
        _udpListener = new UdpClient(port);
        _logService.Debug($"Network service initialized on port {port}", "NetworkService");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~NetworkService()
    {
        Dispose(false);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _logService.Debug("Disposing network resources", "NetworkService");
            _udpListener.Dispose();
        }

        _disposed = true;
    }

    public async Task<(byte[] Buffer, IPEndPoint EndPoint)> ReceiveAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var result = await _udpListener.ReceiveAsync();
        _logService.Debug($"Received {result.Buffer.Length} bytes from {result.RemoteEndPoint}", "NetworkService");
        return (result.Buffer, result.RemoteEndPoint);
    }

    public async Task SendPongMessageAsync(Peer recipient, IEnumerable<Peer> peers)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var pongAnnounce = new Announce
        {
            Type = Announce.Types.MessageType.Pong,
            Appid = recipient.AppId,
            TcpPort = (uint)_udpListener.Client.LocalEndPoint!.GetHashCode()
        };

        // Add all peers to the response
        foreach (var peer in peers)
            pongAnnounce.Peers.Add(new Announce.Types.Other_Peers
            {
                Id = peer.SteamId,
                Ip = peer.GetIpAddressAsUint32(),
                UdpPort = (uint)peer.EndPoint.Port,
                Appid = peer.AppId
            });

        var pongMessage = new Common_Message
        {
            SourceId = MasterServerSteamId,
            DestId = recipient.SteamId,
            Announce = pongAnnounce
        };

        var pongBuffer = pongMessage.ToByteArray();
        _logService.Debug($"Sending PONG to {recipient.SteamId} with {pongAnnounce.Peers.Count} peers",
            "NetworkService");
        await _udpListener.SendAsync(pongBuffer, pongBuffer.Length, recipient.EndPoint);
    }

    /// <summary>
    ///     Broadcasts a lobby update to all members
    /// </summary>
    public async Task BroadcastLobbyUpdateAsync(Lobby lobby, IEnumerable<Peer> recipients)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var message = new Common_Message
        {
            SourceId = MasterServerSteamId,
            Lobby = lobby
        };

        var buffer = message.ToByteArray();

        foreach (var recipient in recipients)
        {
            message.DestId = recipient.SteamId;
            try
            {
                await _udpListener.SendAsync(buffer, buffer.Length, recipient.EndPoint);
                _logService.Debug($"Sent lobby update for {lobby.RoomId} to {recipient.SteamId}", "NetworkService");
            }
            catch (Exception ex)
            {
                _logService.Error($"Failed to send lobby update to {recipient.SteamId}: {ex.Message}",
                    "NetworkService");
            }
        }
    }

    /// <summary>
    ///     Broadcasts a lobby message to all members except the sender
    /// </summary>
    public async Task BroadcastLobbyMessageAsync(Lobby_Messages lobbyMessage, ulong senderId,
        IEnumerable<Peer> recipients)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var message = new Common_Message
        {
            SourceId = senderId,
            LobbyMessages = lobbyMessage
        };

        var buffer = message.ToByteArray();

        foreach (var recipient in recipients.Where(r => r.SteamId != senderId))
        {
            message.DestId = recipient.SteamId;
            try
            {
                await _udpListener.SendAsync(buffer, buffer.Length, recipient.EndPoint);
                _logService.Debug(
                    $"Sent lobby message type {lobbyMessage.Type} for {lobbyMessage.Id} to {recipient.SteamId}",
                    "NetworkService");
            }
            catch (Exception ex)
            {
                _logService.Error($"Failed to send lobby message to {recipient.SteamId}: {ex.Message}",
                    "NetworkService");
            }
        }
    }

    /// <summary>
    ///     Sends a direct lobby message to a specific peer
    /// </summary>
    public async Task SendLobbyMessageAsync(Lobby_Messages lobbyMessage, Peer recipient,
        ulong senderId = MasterServerSteamId)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var message = new Common_Message
        {
            SourceId = senderId,
            DestId = recipient.SteamId,
            LobbyMessages = lobbyMessage
        };

        var buffer = message.ToByteArray();
        try
        {
            await _udpListener.SendAsync(buffer, buffer.Length, recipient.EndPoint);
            _logService.Debug(
                $"Sent direct lobby message type {lobbyMessage.Type} for {lobbyMessage.Id} to {recipient.SteamId}",
                "NetworkService");
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to send direct lobby message to {recipient.SteamId}: {ex.Message}",
                "NetworkService");
        }
    }
}