using TcgBootLog.Parsing;
using TcgBootLog.Tbs;

namespace TcgBootLog.Services;

public sealed class PcrCompareRow
{
    public uint Pcr { get; init; }
    public bool Match { get; init; }
    public string ExpectedHex { get; init; } = "";
    public string ActualHex { get; init; } = "";
}

public sealed class PcrCheckResult
{
    public bool Ok { get; init; }
    public string Reason { get; init; } = "";
    public List<PcrCompareRow> Rows { get; init; } = [];
}

/// <summary>
/// Mutable scan state — filled one check at a time so the UI can reveal results progressively.
/// </summary>
public sealed class IntegrityScanState
{
    public NtpCheckResult? Ntp { get; set; }
    public EkCheckResult? Ek { get; set; }
    public AkCheckResult? Ak { get; set; }
    public PcrCheckResult? Pcr { get; set; }

    public bool AllDone => Ntp != null && Ek != null && Ak != null && Pcr != null;
    public bool AllOk =>
        AllDone && Ntp!.Ok && Ek!.Ok && Ak!.Ok && Pcr!.Ok;
}

public static class IntegrityChecker
{
    public static NtpCheckResult CheckNtp() => NtpChecker.Check();

    public static EkCheckResult CheckEk() => EkChecker.Check();

    public static AkCheckResult CheckAk() => AkChecker.Check();

    public static PcrCheckResult CheckPcr(TcgEventLog? log)
    {
        if (log == null || log.Events.Count == 0)
            return new PcrCheckResult { Ok = false, Reason = "No TCG log loaded — load Events first" };

        try
        {
            var replayed = PcrReplayer.Replay(log);
            ushort alg = replayed.ContainsKey(0x000B) ? (ushort)0x000B :
                         replayed.Keys.FirstOrDefault();
            if (alg == 0)
                return new PcrCheckResult { Ok = false, Reason = "No digests in log to replay" };

            var bank = replayed[alg];
            var indices = bank.Keys.OrderBy(x => x).ToArray();
            var actual = TbsApi.ReadPcrValues([alg], indices);
            actual.TryGetValue(alg, out var hwBank);
            hwBank ??= new Dictionary<uint, byte[]>();

            var rows = new List<PcrCompareRow>();
            int match = 0, total = 0;
            foreach (uint pcr in indices)
            {
                total++;
                bank.TryGetValue(pcr, out var exp);
                hwBank.TryGetValue(pcr, out var act);
                exp ??= [];
                act ??= [];
                bool ok = exp.Length > 0 && act.Length > 0 && exp.AsSpan().SequenceEqual(act);
                if (ok) match++;
                rows.Add(new PcrCompareRow
                {
                    Pcr = pcr,
                    Match = ok,
                    ExpectedHex = Convert.ToHexString(exp).ToLowerInvariant(),
                    ActualHex = Convert.ToHexString(act).ToLowerInvariant(),
                });
            }

            bool allOk = total > 0 && match == total;
            return new PcrCheckResult
            {
                Ok = allOk,
                Reason = $"{match}/{total} PCR match (alg 0x{alg:X4})",
                Rows = rows,
            };
        }
        catch (Exception ex)
        {
            return new PcrCheckResult { Ok = false, Reason = "PCR error: " + ex.Message };
        }
    }
}
