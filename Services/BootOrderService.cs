using System.Text;
using TcgBootLog.Parsing;

namespace TcgBootLog.Services;

public sealed class BootEntry
{
    public int OrderIndex { get; init; }
    public string Id { get; init; } = "";           // Boot0001
    public string Description { get; init; } = "";
    public string? EfiPath { get; init; }
    public string Source { get; init; } = "";        // TPM / NVRAM
    public string Details { get; init; } = "";
}

public sealed class BootOrderReport
{
    public List<ushort> OrderIds { get; init; } = [];
    public List<BootEntry> FromTpm { get; init; } = [];
    public List<BootEntry> FromNvram { get; init; } = [];
    public string? TpmError { get; set; }
    public string? NvramError { get; set; }
}

public static class BootOrderService
{
    public static BootOrderReport Build(IReadOnlyList<TcgEvent> events)
    {
        var report = new BootOrderReport();
        try
        {
            report.FromTpm.AddRange(FromTcgLog(events, report.OrderIds));
        }
        catch (Exception ex)
        {
            report.TpmError = ex.Message;
        }

        try
        {
            report.FromNvram.AddRange(FromNvram(out var order));
            if (report.OrderIds.Count == 0)
                report.OrderIds.AddRange(order);
        }
        catch (Exception ex)
        {
            report.NvramError = ex.Message;
        }

        return report;
    }

    private static List<BootEntry> FromTcgLog(IReadOnlyList<TcgEvent> events, List<ushort> orderOut)
    {
        var list = new List<BootEntry>();
        var bootVars = new Dictionary<string, (string desc, string? path, string details)>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in events)
        {
            if (e.EventType is not (0x80000002 or 0x8000000C)) continue; // VARIABLE_BOOT / BOOT2
            if (!TryParseEfiVariable(e.EventData, out string name, out byte[] varData))
                continue;

            if (name.Equals("BootOrder", StringComparison.OrdinalIgnoreCase))
            {
                orderOut.Clear();
                for (int i = 0; i + 1 < varData.Length; i += 2)
                    orderOut.Add(BitConverter.ToUInt16(varData, i));
                continue;
            }

            if (name.StartsWith("Boot", StringComparison.OrdinalIgnoreCase) && name.Length == 8)
            {
                var (desc, path) = ParseLoadOption(varData);
                bootVars[name] = (desc, path ?? e.EfiFilePath, e.Details);
            }
        }

        if (orderOut.Count == 0)
        {
            // still show any Boot#### found
            int i = 0;
            foreach (var kv in bootVars.OrderBy(k => k.Key))
            {
                list.Add(new BootEntry
                {
                    OrderIndex = i++,
                    Id = kv.Key,
                    Description = kv.Value.desc,
                    EfiPath = kv.Value.path,
                    Source = "TPM (TCG log)",
                    Details = kv.Value.details,
                });
            }
            return list;
        }

        for (int i = 0; i < orderOut.Count; i++)
        {
            string id = $"Boot{orderOut[i]:X4}";
            bootVars.TryGetValue(id, out var info);
            list.Add(new BootEntry
            {
                OrderIndex = i,
                Id = id,
                Description = info.desc ?? "",
                EfiPath = info.path,
                Source = "TPM (TCG log)",
                Details = info.details ?? "",
            });
        }

        return list;
    }

    private static List<BootEntry> FromNvram(out List<ushort> order)
    {
        order = [];
        var list = new List<BootEntry>();
        var orderBytes = EfiNvram.ReadVariable("BootOrder")
            ?? throw new InvalidOperationException("BootOrder EFI variable not found (requires UEFI + admin).");

        for (int i = 0; i + 1 < orderBytes.Length; i += 2)
            order.Add(BitConverter.ToUInt16(orderBytes, i));

        for (int i = 0; i < order.Count; i++)
        {
            string id = $"Boot{order[i]:X4}";
            string desc = "";
            string? path = null;
            string details = "";
            try
            {
                var opt = EfiNvram.ReadVariable(id);
                if (opt != null)
                {
                    (desc, path) = ParseLoadOption(opt);
                    details = $"{opt.Length} bytes";
                }
            }
            catch (Exception ex)
            {
                details = ex.Message;
            }

            list.Add(new BootEntry
            {
                OrderIndex = i,
                Id = id,
                Description = desc,
                EfiPath = path,
                Source = "NVRAM (UEFI)",
                Details = details,
            });
        }

        return list;
    }

    private static bool TryParseEfiVariable(byte[] data, out string name, out byte[] varData)
    {
        name = "";
        varData = [];
        if (data.Length < 32) return false;
        ulong nameLenChars = BitConverter.ToUInt64(data, 16);
        ulong dataLen = BitConverter.ToUInt64(data, 24);
        int nameBytes = (int)(nameLenChars * 2);
        if (32 + nameBytes + (long)dataLen > data.Length) return false;
        name = Encoding.Unicode.GetString(data, 32, nameBytes).TrimEnd('\0');
        varData = new byte[dataLen];
        Array.Copy(data, 32 + nameBytes, varData, 0, (int)dataLen);
        return true;
    }

    private static (string description, string? path) ParseLoadOption(byte[] opt)
    {
        if (opt.Length < 6) return ("", null);
        ushort filePathListLength = BitConverter.ToUInt16(opt, 4);
        int idx = 6;
        var chars = new List<char>();
        while (idx + 2 <= opt.Length)
        {
            ushort ch = BitConverter.ToUInt16(opt, idx);
            idx += 2;
            if (ch == 0) break;
            chars.Add((char)ch);
        }
        string desc = new string(chars.ToArray());
        string? path = null;
        if (filePathListLength > 0 && idx + filePathListLength <= opt.Length)
        {
            var parsed = UefiDevicePath.Parse(opt.AsSpan(idx, filePathListLength));
            path = parsed.FilePath;
        }
        return (desc, path);
    }
}
