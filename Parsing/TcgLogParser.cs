using System.Text;

namespace TcgBootLog.Parsing;

public static class TcgLogParser
{
    private static readonly byte[] Tcg2Signature = Encoding.ASCII.GetBytes("Spec ID Event03\0");

    public static TcgEventLog Parse(byte[] data, string sourceName)
    {
        if (data.Length < 32)
            throw new InvalidDataException("Data too small to be a TCG event log.");

        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        var log = new TcgEventLog
        {
            Source = sourceName,
            FileSize = data.Length,
            IsCryptoAgile = DetectCryptoAgile(data),
        };

        if (log.IsCryptoAgile)
            ParseTcg20(br, log);
        else
            ParseTcg12(br, log);

        foreach (var evt in log.Events)
            Enrich(evt);

        return log;
    }

    private static bool DetectCryptoAgile(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            br.ReadUInt32();
            uint eventType = br.ReadUInt32();
            br.ReadBytes(20);
            uint eventSize = br.ReadUInt32();
            if (eventType != 0x00000003 || eventSize < 16) return false;
            var eventData = br.ReadBytes((int)eventSize);
            return eventData.AsSpan(0, 16).SequenceEqual(Tcg2Signature);
        }
        catch
        {
            return false;
        }
    }

    private static void ParseTcg12(BinaryReader br, TcgEventLog log)
    {
        int index = 0;
        while (br.BaseStream.Position < br.BaseStream.Length - 8)
        {
            long offset = br.BaseStream.Position;
            try
            {
                uint pcr = br.ReadUInt32();
                uint type = br.ReadUInt32();
                byte[] sha1 = br.ReadBytes(20);
                uint size = br.ReadUInt32();
                if (br.BaseStream.Position + size > br.BaseStream.Length) break;
                byte[] eventData = br.ReadBytes((int)size);

                log.Events.Add(new TcgEvent
                {
                    Index = index++,
                    PcrIndex = pcr,
                    EventType = type,
                    FileOffset = offset,
                    Digests = [new DigestEntry { AlgorithmId = 0x0004, Digest = sha1 }],
                    EventData = eventData,
                });
            }
            catch (EndOfStreamException)
            {
                break;
            }
        }
    }

    private static void ParseTcg20(BinaryReader br, TcgEventLog log)
    {
        // First event is TCG 1.2 format Spec ID Event
        long offset0 = br.BaseStream.Position;
        uint pcr0 = br.ReadUInt32();
        uint type0 = br.ReadUInt32();
        byte[] sha1_0 = br.ReadBytes(20);
        uint size0 = br.ReadUInt32();
        byte[] data0 = br.ReadBytes((int)size0);

        log.Events.Add(new TcgEvent
        {
            Index = 0,
            PcrIndex = pcr0,
            EventType = type0,
            FileOffset = offset0,
            Digests = [new DigestEntry { AlgorithmId = 0x0004, Digest = sha1_0 }],
            EventData = data0,
        });

        // Parse Spec ID to learn digest algorithms (optional; we read digests as declared)
        int index = 1;
        while (br.BaseStream.Position + 8 < br.BaseStream.Length)
        {
            long offset = br.BaseStream.Position;
            try
            {
                uint pcr = br.ReadUInt32();
                uint type = br.ReadUInt32();
                uint digestCount = br.ReadUInt32();

                var digests = new List<DigestEntry>();
                for (uint i = 0; i < digestCount; i++)
                {
                    ushort alg = br.ReadUInt16();
                    if (!TcgEventTypes.DigestSizes.TryGetValue(alg, out int digSize))
                    {
                        // Unknown alg — abort this event safely
                        return;
                    }
                    digests.Add(new DigestEntry { AlgorithmId = alg, Digest = br.ReadBytes(digSize) });
                }

                uint eventSize = br.ReadUInt32();
                if (br.BaseStream.Position + eventSize > br.BaseStream.Length) break;
                byte[] eventData = br.ReadBytes((int)eventSize);

                log.Events.Add(new TcgEvent
                {
                    Index = index++,
                    PcrIndex = pcr,
                    EventType = type,
                    FileOffset = offset,
                    Digests = digests,
                    EventData = eventData,
                });
            }
            catch (EndOfStreamException)
            {
                break;
            }
        }
    }

    private static void Enrich(TcgEvent evt)
    {
        switch (evt.EventType)
        {
            case 0x80000003: // EV_EFI_BOOT_SERVICES_APPLICATION
            case 0x80000004: // EV_EFI_BOOT_SERVICES_DRIVER
            case 0x80000005: // EV_EFI_RUNTIME_SERVICES_DRIVER
            {
                var (loc, len, link, path) = UefiDevicePath.ParseImageLoadEvent(evt.EventData);
                evt.EfiFilePath = path.FilePath;
                evt.Details =
                    $"ImageLoc=0x{loc:X} Len=0x{len:X} Link=0x{link:X}" +
                    (string.IsNullOrEmpty(path.DevicePathSummary) ? "" : $" | {path.DevicePathSummary}");
                break;
            }

            case 0x80000001: // EV_EFI_VARIABLE_DRIVER_CONFIG
            case 0x80000002: // EV_EFI_VARIABLE_BOOT
            case 0x8000000C: // EV_EFI_VARIABLE_BOOT2
            case 0x800000E0: // EV_EFI_VARIABLE_AUTHORITY
            {
                DecodeEfiVariable(evt);
                break;
            }

            case 0x80000007: // EV_EFI_ACTION
            case 0x00000005: // EV_ACTION
            {
                evt.Details = CleanUnicodeOrAscii(evt.EventData);
                break;
            }

            case 0x8000000A: // EV_EFI_PLATFORM_FIRMWARE_BLOB2
            {
                if (evt.EventData.Length > 1)
                {
                    int nameLen = evt.EventData[0];
                    if (nameLen > 0 && 1 + nameLen <= evt.EventData.Length)
                        evt.Details = Encoding.UTF8.GetString(evt.EventData, 1, nameLen).TrimEnd('\0');
                }
                break;
            }

            default:
            {
                string text = CleanUnicodeOrAscii(evt.EventData);
                if (!string.IsNullOrWhiteSpace(text) && text.Length < 200)
                    evt.Details = text;
                else if (evt.EventData.Length > 0)
                    evt.Details = $"({evt.EventData.Length} bytes)";
                break;
            }
        }
    }

    private static void DecodeEfiVariable(TcgEvent evt)
    {
        var data = evt.EventData;
        if (data.Length < 32)
        {
            evt.Details = "<EFI_VARIABLE too short>";
            return;
        }

        var guid = new Guid(
            BitConverter.ToUInt32(data, 0),
            BitConverter.ToUInt16(data, 4),
            BitConverter.ToUInt16(data, 6),
            data[8], data[9], data[10], data[11],
            data[12], data[13], data[14], data[15]);

        ulong nameLenChars = BitConverter.ToUInt64(data, 16);
        ulong dataLen = BitConverter.ToUInt64(data, 24);
        int nameBytes = (int)(nameLenChars * 2);
        string name = "";
        if (nameLenChars > 0 && 32 + nameBytes <= data.Length)
            name = Encoding.Unicode.GetString(data, 32, nameBytes).TrimEnd('\0');

        evt.Details = $"{name}  GUID={guid}  DataLen={dataLen}";

        // Boot#### load options contain a device path with the EFI file path
        if (name.StartsWith("Boot", StringComparison.OrdinalIgnoreCase)
            && name.Length == 8
            && 32 + nameBytes + (int)dataLen <= data.Length
            && dataLen >= 6)
        {
            int optOffset = 32 + nameBytes;
            var opt = data.AsSpan(optOffset, (int)dataLen);
            ushort filePathListLength = BitConverter.ToUInt16(opt.Slice(4, 2));
            int idx = 6;
            // skip UCS-2 description
            while (idx + 2 <= opt.Length)
            {
                ushort ch = BitConverter.ToUInt16(opt.Slice(idx, 2));
                idx += 2;
                if (ch == 0) break;
            }

            if (filePathListLength > 0 && idx + filePathListLength <= opt.Length)
            {
                var path = UefiDevicePath.Parse(opt.Slice(idx, filePathListLength));
                if (!string.IsNullOrEmpty(path.FilePath))
                    evt.EfiFilePath = path.FilePath;
                if (!string.IsNullOrEmpty(path.DevicePathSummary))
                    evt.Details += $" | {path.DevicePathSummary}";
            }
        }
    }

    private static string CleanUnicodeOrAscii(byte[] data)
    {
        if (data.Length == 0) return "";

        bool looksUnicode = data.Length >= 2 && data.Length % 2 == 0;
        if (looksUnicode)
        {
            string u = Encoding.Unicode.GetString(data).TrimEnd('\0').Trim();
            if (u.Length > 0 && u.All(c => !char.IsControl(c) || c is '\r' or '\n' or '\t'))
                return u;
        }

        bool ascii = data.All(b => b == 0 || (b >= 0x20 && b <= 0x7E));
        if (ascii)
            return Encoding.ASCII.GetString(data).TrimEnd('\0').Trim();

        return "";
    }
}
