using System.Net;
using System.Net.Sockets;
using GoldbergMasterServer.Models;
using GoldbergMasterServer.Network;
using Google.Protobuf;

namespace GoldbergMasterServer.Services;

/// <summary>
///     Handles UDP network communication and message processing
/// </summary>
public sealed class NetworkService(int port) : IDisposable
{
    private const ulong MasterServerSteamId = 0x100001DEADBEEF;
    private readonly UdpClient _udpListener = new(port);
    private bool _disposed;

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

        if (disposing) _udpListener.Dispose();

        _disposed = true;
    }

    public async Task<(byte[] Buffer, IPEndPoint EndPoint)> ReceiveAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var result = await _udpListener.ReceiveAsync();
        return (result.Buffer, result.RemoteEndPoint);
    }

    public async Task SendPongMessageAsync(Peer recipient, IEnumerable<Peer> peers)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NetworkService));

        var pongAnnounce = new Announce
        {
            Type = Announce.Types.MessageType.Pong,
            Appid = recipient.AppId,
            TcpPort = (uint)port
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
        await _udpListener.SendAsync(pongBuffer, pongBuffer.Length, recipient.EndPoint);
    }
}