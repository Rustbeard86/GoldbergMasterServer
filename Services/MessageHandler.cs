using System.Net;
using GoldbergMasterServer.Models;
using GoldbergMasterServer.Network;
using Google.Protobuf;

namespace GoldbergMasterServer.Services;

/// <summary>
/// Handles protocol-specific message processing
/// </summary>
public class MessageHandler(PeerManager peerManager, NetworkService networkService)
{
    public async Task HandleMessageAsync(byte[] buffer, IPEndPoint remoteEndPoint)
    {
        try
        {
            var message = Common_Message.Parser.ParseFrom(buffer);

            if (message.MessagesCase == Common_Message.MessagesOneofCase.Announce)
            {
                var announce = message.Announce;

                if (announce.Type == Announce.Types.MessageType.Ping)
                {
                    await HandlePingAsync(message, announce, remoteEndPoint);
                }
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
}