using System.Net;
using System.Net.Sockets;

namespace TcgBootLog.Services;

public sealed class NtpCheckResult
{
    public bool Ok { get; init; }
    public string Reason { get; init; } = "";
    public DateTime LocalUtc { get; init; }
    public DateTime NtpUtc { get; init; }
    public double OffsetSeconds { get; init; }
    public string Server { get; init; } = "";
}

public static class NtpChecker
{
    public static NtpCheckResult Check(string server = "time.windows.com", double maxSkewSeconds = 5.0)
    {
        try
        {
            var addresses = Dns.GetHostEntry(server).AddressList;
            var ep = new IPEndPoint(addresses[0], 123);
            var ntpUtc = GetNtpUtc(ep);
            var localUtc = DateTime.UtcNow;
            double skew = Math.Abs((localUtc - ntpUtc).TotalSeconds);
            bool ok = skew <= maxSkewSeconds;
            return new NtpCheckResult
            {
                Ok = ok,
                Server = server,
                LocalUtc = localUtc,
                NtpUtc = ntpUtc,
                OffsetSeconds = (localUtc - ntpUtc).TotalSeconds,
                Reason = ok
                    ? $"skew {skew:0.000}s (limit {maxSkewSeconds}s)"
                    : $"skew {skew:0.000}s exceeds {maxSkewSeconds}s",
            };
        }
        catch (Exception ex)
        {
            return new NtpCheckResult { Ok = false, Server = server, Reason = ex.Message };
        }
    }

    private static DateTime GetNtpUtc(IPEndPoint ep)
    {
        var ntpData = new byte[48];
        ntpData[0] = 0x1B;
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.ReceiveTimeout = 4000;
        socket.SendTimeout = 4000;
        socket.Connect(ep);
        socket.Send(ntpData);
        socket.Receive(ntpData);

        const byte serverReplyTime = 40;
        ulong intPart = BitConverter.ToUInt32(ntpData, serverReplyTime);
        ulong fractPart = BitConverter.ToUInt32(ntpData, serverReplyTime + 4);
        intPart = SwapEndianness(intPart);
        fractPart = SwapEndianness(fractPart);
        var milliseconds = intPart * 1000 + fractPart * 1000 / 0x100000000L;
        return new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds((long)milliseconds);
    }

    private static uint SwapEndianness(ulong x) =>
        (uint)(((x & 0x000000ff) << 24) + ((x & 0x0000ff00) << 8) +
               ((x & 0x00ff0000) >> 8) + ((x & 0xff000000) >> 24));
}
