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
        _logService.Info($"Network service initialized and listening on UDP port {port}", "Network.Initialize");
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
            _logService.Info("Shutting down network service...", "Network.Shutdown");
            try
            {
                _udpListener.Close();
                _udpListener.Dispose();
                _logService.Info("Network service successfully shut down", "Network.Shutdown");
            }
            catch (Exception ex)
            {
                _logService.Error($"Error during network service shutdown: {ex.Message}", "Network.Shutdown");
            }
        }

        _disposed = true;
    }

    public async Task<(byte[] Buffer, IPEndPoint EndPoint)> ReceiveAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        try
        {
            var result = await _udpListener.ReceiveAsync();
            _logService.Debug(
                $"Received packet: Size={result.Buffer.Length} bytes, From={result.RemoteEndPoint}, First4Bytes={BitConverter.ToString([.. result.Buffer.Take(4)])}",
                "Network.Receive");

            if (result.Buffer.Length >= 8)
            {
                _logService.Debug($"Packet header: {BitConverter.ToString([.. result.Buffer.Take(8)])}", "Network.PacketDetails");
            }

            return (result.Buffer, result.RemoteEndPoint);
        }
        catch (SocketException ex) when (_disposed && (ex.ErrorCode == 995 || ex.ErrorCode == 10004))
        {
            // Socket was closed during shutdown - this is expected, rethrow to be handled by caller
            _logService.Debug($"Socket receive cancelled during shutdown (ErrorCode={ex.ErrorCode})", "Network.Shutdown");
            throw;
        }
        catch (SocketException ex)
        {
            _logService.Error($"Socket error while receiving data: {ex.Message} (ErrorCode={ex.ErrorCode})", "Network.Error");
            throw;
        }
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

        var peerList = peers.ToList();
        foreach (var peer in peerList)
        {
            pongAnnounce.Peers.Add(new Announce.Types.Other_Peers
            {
                Id = peer.SteamId,
                Ip = peer.GetIpAddressAsUint32(),
                UdpPort = (uint)peer.EndPoint.Port,
                Appid = peer.AppId
            });
        }

        var pongMessage = new Common_Message
        {
            SourceId = MasterServerSteamId,
            DestId = recipient.SteamId,
            Announce = pongAnnounce
        };

        var pongBuffer = pongMessage.ToByteArray();
        _logService.Debug(
            $"Sending PONG: To={recipient.SteamId}, AppId={recipient.AppId}, PeerCount={peerList.Count}, Size={pongBuffer.Length}bytes",
            "Network.Pong");

        try
        {
            await _udpListener.SendAsync(pongBuffer, pongBuffer.Length, recipient.EndPoint);
            _logService.Debug($"PONG sent successfully to {recipient.EndPoint}", "Network.Pong");
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to send PONG to {recipient.SteamId}: {ex.Message}", "Network.Error");
            throw;
        }
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
        _logService.Debug($"Broadcasting lobby update: LobbyId={lobby.RoomId}, Size={buffer.Length}bytes", "Network.Lobby");

        foreach (var recipient in recipients)
        {
            message.DestId = recipient.SteamId;
            try
            {
                await _udpListener.SendAsync(buffer, buffer.Length, recipient.EndPoint);
                _logService.Debug($"Lobby update sent: LobbyId={lobby.RoomId}, To={recipient.SteamId}, EndPoint={recipient.EndPoint}", "Network.Lobby");
            }
            catch (Exception ex)
            {
                _logService.Error($"Failed to send lobby update: To={recipient.SteamId}, Error={ex.Message}", "Network.Error");
            }
        }
    }

    /// <summary>
    ///     Broadcasts a lobby message to all members except the sender
    /// </summary>
    public async Task BroadcastLobbyMessageAsync(Lobby_Messages lobbyMessage, ulong senderId, IEnumerable<Peer> recipients)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var message = new Common_Message
        {
            SourceId = senderId,
            LobbyMessages = lobbyMessage
        };

        var buffer = message.ToByteArray();
        _logService.Debug($"Broadcasting lobby message: Type={lobbyMessage.Type}, LobbyId={lobbyMessage.Id}, Size={buffer.Length}bytes", "Network.Lobby");

        foreach (var recipient in recipients.Where(r => r.SteamId != senderId))
        {
            message.DestId = recipient.SteamId;
            try
            {
                await _udpListener.SendAsync(buffer, buffer.Length, recipient.EndPoint);
                _logService.Debug($"Lobby message sent: Type={lobbyMessage.Type}, To={recipient.SteamId}, EndPoint={recipient.EndPoint}", "Network.Lobby");
            }
            catch (Exception ex)
            {
                _logService.Error($"Failed to broadcast lobby message: To={recipient.SteamId}, Error={ex.Message}", "Network.Error");
            }
        }
    }

    /// <summary>
    ///     Sends a direct lobby message to a specific peer
    /// </summary>
    public async Task SendLobbyMessageAsync(Lobby_Messages lobbyMessage, Peer recipient, ulong senderId = MasterServerSteamId)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var message = new Common_Message
        {
            SourceId = senderId,
            DestId = recipient.SteamId,
            LobbyMessages = lobbyMessage
        };

        var buffer = message.ToByteArray();
        _logService.Debug($"Sending direct lobby message: Type={lobbyMessage.Type}, To={recipient.SteamId}, Size={buffer.Length}bytes", "Network.Lobby");

        try
        {
            await _udpListener.SendAsync(buffer, buffer.Length, recipient.EndPoint);
            _logService.Debug($"Direct lobby message sent: Type={lobbyMessage.Type}, To={recipient.SteamId}, EndPoint={recipient.EndPoint}", "Network.Lobby");
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to send direct lobby message: To={recipient.SteamId}, Error={ex.Message}", "Network.Error");
        }
    }

    /// <summary>
    ///     Sends a gameserver list response to a peer
    /// </summary>
    public async Task SendGameserverListAsync(IEnumerable<Gameserver> servers, Peer recipient)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var serverList = servers.ToList();
        _logService.Debug($"Sending gameserver list: Count={serverList.Count}, To={recipient.SteamId}", "Network.Gameserver");

        foreach (var server in serverList)
        {
            var message = new Common_Message
            {
                SourceId = MasterServerSteamId,
                DestId = recipient.SteamId,
                Gameserver = server
            };

            var buffer = message.ToByteArray();
            try
            {
                await _udpListener.SendAsync(buffer, buffer.Length, recipient.EndPoint);
                _logService.Debug($"Gameserver sent: ID={server.Id}, Name={server.ServerName.ToStringUtf8()}, To={recipient.EndPoint}", "Network.Gameserver");
            }
            catch (Exception ex)
            {
                _logService.Error($"Failed to send gameserver: ID={server.Id}, Error={ex.Message}", "Network.Error");
            }
        }
    }

    /// <summary>
    ///     Sends a Network_pb message (ISteamNetworking)
    /// </summary>
    public async Task SendNetworkMessageAsync(Network_pb networkMessage, Peer recipient, ulong senderId = MasterServerSteamId)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var message = new Common_Message
        {
            SourceId = senderId,
            DestId = recipient.SteamId,
            Network = networkMessage
        };

        var buffer = message.ToByteArray();
        _logService.Debug($"Sending Network_pb: Type={networkMessage.Type}, Channel={networkMessage.Channel}, To={recipient.SteamId}, Size={buffer.Length}bytes", "Network.P2P");

        try
        {
            await _udpListener.SendAsync(buffer, buffer.Length, recipient.EndPoint);
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to send Network_pb: To={recipient.SteamId}, Error={ex.Message}", "Network.Error");
            throw;
        }
    }

    /// <summary>
    ///     Sends a Networking_Sockets message (ISteamNetworkingSockets)
    /// </summary>
    public async Task SendNetworkingSocketsMessageAsync(Networking_Sockets networkingMessage, Peer recipient, ulong senderId = MasterServerSteamId)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var message = new Common_Message
        {
            SourceId = senderId,
            DestId = recipient.SteamId,
            NetworkingSockets = networkingMessage
        };

        var buffer = message.ToByteArray();
        _logService.Debug($"Sending NetworkingSockets: Type={networkingMessage.Type}, Port={networkingMessage.VirtualPort}, To={recipient.SteamId}, Size={buffer.Length}bytes", "Network.P2P");

        try
        {
            await _udpListener.SendAsync(buffer, buffer.Length, recipient.EndPoint);
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to send NetworkingSockets: To={recipient.SteamId}, Error={ex.Message}", "Network.Error");
            throw;
        }
    }

    /// <summary>
    ///     Sends a Networking_Messages message
    /// </summary>
    public async Task SendNetworkingMessagesAsync(Networking_Messages networkingMessage, Peer recipient, ulong senderId = MasterServerSteamId)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var message = new Common_Message
        {
            SourceId = senderId,
            DestId = recipient.SteamId,
            NetworkingMessages = networkingMessage
        };

        var buffer = message.ToByteArray();
        _logService.Debug($"Sending NetworkingMessages: Type={networkingMessage.Type}, Channel={networkingMessage.Channel}, To={recipient.SteamId}, Size={buffer.Length}bytes", "Network.P2P");

        try
        {
            await _udpListener.SendAsync(buffer, buffer.Length, recipient.EndPoint);
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to send NetworkingMessages: To={recipient.SteamId}, Error={ex.Message}", "Network.Error");
            throw;
        }
    }
}