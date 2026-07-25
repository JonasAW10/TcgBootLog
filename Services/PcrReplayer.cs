using System.Security.Cryptography;
using TcgBootLog.Parsing;

namespace TcgBootLog.Services;

public static class PcrReplayer
{
    public static Dictionary<ushort, Dictionary<uint, byte[]>> Replay(TcgEventLog log)
    {
        var banks = new Dictionary<ushort, Dictionary<uint, byte[]>>();

        // Discover algs from events
        foreach (var evt in log.Events)
        foreach (var d in evt.Digests)
            if (!banks.ContainsKey(d.AlgorithmId))
                banks[d.AlgorithmId] = new Dictionary<uint, byte[]>();

        if (banks.Count == 0)
            banks[0x000B] = new Dictionary<uint, byte[]>();

        byte locality = DetectStartupLocality(log);
        if (locality != 0)
        {
            foreach (var (algId, bank) in banks)
            {
                int size = TcgEventTypes.DigestSizes.TryGetValue(algId, out var s) ? s : 32;
                var seed = new byte[size];
                seed[size - 1] = locality;
                bank[0] = seed;
            }
        }

        foreach (var evt in log.Events)
        {
            if (evt.EventType == 0x00000003) continue; // EV_NO_ACTION
            if (evt.PcrIndex == 0xFFFFFFFF) continue;

            foreach (var digest in evt.Digests)
            {
                if (!banks.TryGetValue(digest.AlgorithmId, out var bank)) continue;
                if (!bank.TryGetValue(evt.PcrIndex, out var current))
                    current = new byte[digest.Digest.Length];
                bank[evt.PcrIndex] = Extend(digest.AlgorithmId, current, digest.Digest);
            }
        }

        return banks;
    }

    private static byte DetectStartupLocality(TcgEventLog log)
    {
        var sig = "StartupLocality\0"u8.ToArray();
        foreach (var evt in log.Events)
        {
            if (evt.EventType != 0x00000003) continue;
            if (evt.EventData.Length < 17) continue;
            bool match = true;
            for (int i = 0; i < 16; i++)
                if (evt.EventData[i] != sig[i]) { match = false; break; }
            if (match) return evt.EventData[16];
        }
        return 0;
    }

    private static byte[] Extend(ushort algId, byte[] pcr, byte[] digest)
    {
        var combined = new byte[pcr.Length + digest.Length];
        Buffer.BlockCopy(pcr, 0, combined, 0, pcr.Length);
        Buffer.BlockCopy(digest, 0, combined, pcr.Length, digest.Length);
        return algId switch
        {
            0x0004 => SHA1.HashData(combined),
            0x000B => SHA256.HashData(combined),
            0x000C => SHA384.HashData(combined),
            0x000D => SHA512.HashData(combined),
            _ => SHA256.HashData(combined),
        };
    }
}
