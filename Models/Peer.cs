using System.Net;

namespace GoldbergMasterServer.Models;

/// <summary>
///     Represents a game server or client peer.
/// </summary>
public class Peer
{
    public ulong SteamId { get; set; }
    public required IPEndPoint EndPoint { get; set; }
    public uint AppId { get; set; }
    public uint TcpPort { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Converts the peer's IP address to the big-endian uint32 format
    ///     expected by the Goldberg emulator.
    /// </summary>
    public uint GetIpAddressAsUint32()
    {
        var ipBytes = EndPoint.Address.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
            // Convert from little-endian (C# default) to big-endian (network order)
            return (uint)((ipBytes[0] << 24) | (ipBytes[1] << 16) | (ipBytes[2] << 8) | ipBytes[3]);
        return BitConverter.ToUInt32(ipBytes, 0);
    }
}