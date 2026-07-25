namespace TcgBootLog.Parsing;

public sealed class DigestEntry
{
    public ushort AlgorithmId { get; init; }
    public string AlgorithmName =>
        TcgEventTypes.AlgNames.TryGetValue(AlgorithmId, out var n) ? n : $"ALG_0x{AlgorithmId:X4}";
    public byte[] Digest { get; init; } = [];
    public string DigestHex => Convert.ToHexString(Digest).ToLowerInvariant();
}

public sealed class TcgEvent
{
    public int Index { get; init; }
    public uint PcrIndex { get; init; }
    public uint EventType { get; init; }
    public string EventTypeName => TcgEventTypes.GetName(EventType);
    public List<DigestEntry> Digests { get; init; } = [];
    public byte[] EventData { get; init; } = [];
    public long FileOffset { get; init; }

    /// <summary>EFI file path extracted from UEFI device path (e.g. \EFI\Microsoft\Boot\bootmgfw.efi).</summary>
    public string? EfiFilePath { get; set; }

    /// <summary>Human-readable device path / variable summary.</summary>
    public string Details { get; set; } = "";

    public string Sha256Hex =>
        Digests.FirstOrDefault(d => d.AlgorithmId == 0x000B)?.DigestHex
        ?? Digests.FirstOrDefault()?.DigestHex
        ?? "";

    public bool IsEfiImageLoad =>
        EventType is 0x80000003 or 0x80000004 or 0x80000005;
}

public sealed class TcgEventLog
{
    public string Source { get; init; } = "";
    public int FileSize { get; init; }
    public bool IsCryptoAgile { get; init; }
    public List<TcgEvent> Events { get; init; } = [];
}
