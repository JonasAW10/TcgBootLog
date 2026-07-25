using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace TcgBootLog.Services;

public sealed class EkCertInfo
{
    public string Subject { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string Thumbprint { get; init; } = "";
    public DateTime NotBefore { get; init; }
    public DateTime NotAfter { get; init; }
    public bool IsLeafLikely { get; init; }
}

public sealed class EkCheckResult
{
    public bool Ok { get; init; }
    public string Reason { get; init; } = "";
    public List<EkCertInfo> Certs { get; init; } = [];
}

public static class EkChecker
{
    public static EkCheckResult Check()
    {
        var ders = new List<byte[]>();
        ReadBlobs(@"SYSTEM\CurrentControlSet\Services\TPM\WMI\Endorsement\EKCertStore\Certificates", ders);
        ReadBlobs(@"SYSTEM\CurrentControlSet\Services\TPM\WMI\Endorsement\IntermediateCACertStore\Certificates", ders);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var certs = new List<EkCertInfo>();
        foreach (var der in ders)
        {
            try
            {
#pragma warning disable SYSLIB0057
                using var c = new X509Certificate2(der);
#pragma warning restore SYSLIB0057
                if (!seen.Add(c.Thumbprint)) continue;
                certs.Add(new EkCertInfo
                {
                    Subject = c.Subject,
                    Issuer = c.Issuer,
                    Thumbprint = c.Thumbprint,
                    NotBefore = c.NotBefore.ToUniversalTime(),
                    NotAfter = c.NotAfter.ToUniversalTime(),
                    IsLeafLikely = !string.Equals(c.Subject, c.Issuer, StringComparison.OrdinalIgnoreCase),
                });
            }
            catch { /* skip bad blob */ }
        }

        if (certs.Count == 0)
            return new EkCheckResult { Ok = false, Reason = "No EK certificates found in registry" };

        var now = DateTime.UtcNow;
        var leaf = certs.FirstOrDefault(c => c.IsLeafLikely) ?? certs[0];
        if (now < leaf.NotBefore || now > leaf.NotAfter)
            return new EkCheckResult
            {
                Ok = false,
                Reason = $"EK cert not currently valid ({leaf.NotBefore:u} – {leaf.NotAfter:u})",
                Certs = certs,
            };

        return new EkCheckResult
        {
            Ok = true,
            Reason = $"{certs.Count} cert(s), leaf: {Truncate(leaf.Subject, 70)}",
            Certs = certs,
        };
    }

    private static void ReadBlobs(string regPath, List<byte[]> outList)
    {
        using var key = Registry.LocalMachine.OpenSubKey(regPath);
        if (key == null) return;
        foreach (string thumb in key.GetSubKeyNames())
        {
            using var ck = key.OpenSubKey(thumb);
            if (ck == null) continue;
            foreach (string vn in ck.GetValueNames())
            {
                if (ck.GetValue(vn) is not byte[] data || data.Length < 4) continue;
                foreach (var der in ParseDerBlobs(data))
                    outList.Add(der);
            }
        }
    }

    private static List<byte[]> ParseDerBlobs(byte[] data)
    {
        var list = new List<byte[]>();
        if (data.Length > 12 && data[0] != 0x30)
        {
            // CERT_PROP format
            int pos = 0;
            while (pos + 12 <= data.Length)
            {
                uint propId = BitConverter.ToUInt32(data, pos);
                uint cb = BitConverter.ToUInt32(data, pos + 8);
                pos += 12;
                if (cb == 0 || pos + cb > data.Length) break;
                if (propId == 32)
                {
                    var der = new byte[cb];
                    Array.Copy(data, pos, der, 0, (int)cb);
                    list.Add(der);
                }
                pos += (int)cb;
            }
            if (list.Count > 0) return list;
        }

        int p = 0;
        while (p < data.Length - 4)
        {
            if (data[p] != 0x30) { p++; continue; }
            int hLen, len;
            if (data[p + 1] < 0x80) { len = data[p + 1]; hLen = 2; }
            else
            {
                int nb = data[p + 1] & 0x7F;
                if (p + 2 + nb > data.Length) break;
                len = 0;
                for (int i = 0; i < nb; i++) len = (len << 8) | data[p + 2 + i];
                hLen = 2 + nb;
            }
            int total = hLen + len;
            if (p + total > data.Length) break;
            var der = new byte[total];
            Array.Copy(data, p, der, 0, total);
            list.Add(der);
            p += total;
        }
        return list;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
