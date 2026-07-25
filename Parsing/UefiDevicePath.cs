using System.Text;

namespace TcgBootLog.Parsing;

/// <summary>
/// Parses UEFI EFI_DEVICE_PATH_PROTOCOL chains and extracts MEDIA_FILEPATH paths.
/// </summary>
public static class UefiDevicePath
{
    private const byte MediaDevicePath = 0x04;
    private const byte MediaFilePathDp = 0x04;
    private const byte MediaHardDriveDp = 0x01;
    private const byte EndDevicePathType = 0x7F;

    public sealed record ParsedPath(
        string? FilePath,
        string DevicePathSummary);

    public static ParsedPath Parse(ReadOnlySpan<byte> devicePath)
    {
        if (devicePath.IsEmpty)
            return new ParsedPath(null, "");

        var parts = new List<string>();
        string? filePath = null;
        int i = 0;

        while (i + 4 <= devicePath.Length)
        {
            byte type = devicePath[i];
            byte subType = devicePath[i + 1];
            ushort length = BitConverter.ToUInt16(devicePath.Slice(i + 2, 2));

            if (length < 4 || i + length > devicePath.Length)
                break;

            var data = devicePath.Slice(i + 4, length - 4);

            if (type == EndDevicePathType)
                break;

            if (type == MediaDevicePath && subType == MediaFilePathDp)
            {
                var path = Encoding.Unicode.GetString(data).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(path))
                {
                    filePath = path;
                    parts.Add($"FilePath={path}");
                }
            }
            else if (type == MediaDevicePath && subType == MediaHardDriveDp && data.Length >= 38)
            {
                uint partNum = BitConverter.ToUInt32(data.Slice(0, 4));
                byte sigType = data[37];
                if (sigType == 2 && data.Length >= 36)
                {
                    var guid = new Guid(data.Slice(20, 16));
                    parts.Add($"HD(Part={partNum},GPT,{guid})");
                }
                else
                {
                    parts.Add($"HD(Part={partNum})");
                }
            }
            else
            {
                parts.Add($"Type=0x{type:X2}/0x{subType:X2}");
            }

            i += length;
        }

        return new ParsedPath(filePath, string.Join(" / ", parts));
    }

    /// <summary>
    /// UEFI_IMAGE_LOAD_EVENT (64-bit): ImageLoc(8) + ImageLen(8) + LinkTime(8) + DevicePathLen(8) + DevicePath
    /// </summary>
    public static (ulong ImageLocation, ulong ImageLength, ulong LinkTime, ParsedPath Path) ParseImageLoadEvent(byte[] eventData)
    {
        if (eventData.Length < 32)
            return (0, 0, 0, new ParsedPath(null, ""));

        ulong loc = BitConverter.ToUInt64(eventData, 0);
        ulong len = BitConverter.ToUInt64(eventData, 8);
        ulong link = BitConverter.ToUInt64(eventData, 16);
        ulong pathLen = BitConverter.ToUInt64(eventData, 24);

        ParsedPath path = new(null, "");
        if (pathLen > 0 && 32 + (long)pathLen <= eventData.Length)
            path = Parse(eventData.AsSpan(32, (int)pathLen));

        return (loc, len, link, path);
    }
}
