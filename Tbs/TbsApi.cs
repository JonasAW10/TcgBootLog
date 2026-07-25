using System.Runtime.InteropServices;

namespace TcgBootLog.Tbs;

public static class TbsApi
{
    public enum TcgLogType : uint
    {
        SrtmCurrent = 0,
        DrtmCurrent = 1,
        SrtmBoot = 2,
        SrtmResume = 3,
    }

    private const uint TbsSuccess = 0;
    private const uint Tpm2CcPcrRead = 0x0000017E;
    private const uint TbsCommandPriorityNormal = 200;

    [StructLayout(LayoutKind.Sequential)]
    private struct TbsContextParams2
    {
        public uint version;
        public uint requestRaw;
    }

    [DllImport("tbs.dll", EntryPoint = "Tbsi_Get_TCG_Log_Ex", CallingConvention = CallingConvention.Winapi)]
    private static extern uint Tbsi_Get_TCG_Log_Ex(uint logType, byte[]? pbOutput, ref uint pcbOutput);

    [DllImport("tbs.dll", EntryPoint = "Tbsi_Context_Create", CallingConvention = CallingConvention.Winapi)]
    private static extern uint Tbsi_Context_Create(ref TbsContextParams2 pContextParams, out IntPtr phContext);

    [DllImport("tbs.dll", EntryPoint = "Tbsip_Submit_Command", CallingConvention = CallingConvention.Winapi)]
    private static extern uint Tbsip_Submit_Command(
        IntPtr hContext, uint locality, uint priority,
        byte[] pabCommand, uint cbCommand, byte[] pabResult, ref uint pcbResult);

    [DllImport("tbs.dll", EntryPoint = "Tbsip_Context_Close", CallingConvention = CallingConvention.Winapi)]
    private static extern uint Tbsip_Context_Close(IntPtr hContext);

    public static byte[] GetTcgLog(TcgLogType logType = TcgLogType.SrtmCurrent)
    {
        uint size = 0;
        uint result = Tbsi_Get_TCG_Log_Ex((uint)logType, null, ref size);

        if (size == 0)
            throw new InvalidOperationException(
                $"Tbsi_Get_TCG_Log_Ex failed to report size (0x{result:X8}). Run as Administrator.");

        var buffer = new byte[size];
        result = Tbsi_Get_TCG_Log_Ex((uint)logType, buffer, ref size);
        if (result != TbsSuccess)
            throw new InvalidOperationException(
                $"Tbsi_Get_TCG_Log_Ex failed (0x{result:X8}). Run as Administrator.");

        if (size < buffer.Length)
            Array.Resize(ref buffer, (int)size);

        return buffer;
    }

    /// <summary>
    /// Reads PCR banks from TPM. Returns [algId][pcrIndex] -> digest.
    /// </summary>
    public static Dictionary<ushort, Dictionary<uint, byte[]>> ReadPcrValues(
        IEnumerable<ushort> algIds, IEnumerable<uint> pcrIndices)
    {
        var result = new Dictionary<ushort, Dictionary<uint, byte[]>>();
        var ctxParams = new TbsContextParams2 { version = 2, requestRaw = 0x04 };
        uint hr = Tbsi_Context_Create(ref ctxParams, out IntPtr hContext);
        if (hr != TbsSuccess)
            throw new InvalidOperationException($"Tbsi_Context_Create failed 0x{hr:X8}");

        try
        {
            foreach (ushort algId in algIds)
            {
                int digestSize = DigestSize(algId);
                if (digestSize == 0) continue;
                var bank = new Dictionary<uint, byte[]>();
                result[algId] = bank;
                foreach (uint pcrIdx in pcrIndices)
                {
                    byte[]? digest = ReadSinglePcr(hContext, algId, pcrIdx);
                    if (digest != null) bank[pcrIdx] = digest;
                }
            }
        }
        finally
        {
            Tbsip_Context_Close(hContext);
        }

        return result;
    }

    private static byte[]? ReadSinglePcr(IntPtr hContext, ushort algId, uint pcrIdx)
    {
        byte[] cmd = new byte[20];
        int pos = 0;
        WriteBE16(cmd, ref pos, 0x8001);
        WriteBE32(cmd, ref pos, 20);
        WriteBE32(cmd, ref pos, Tpm2CcPcrRead);
        WriteBE32(cmd, ref pos, 1);
        WriteBE16(cmd, ref pos, algId);
        cmd[pos++] = 3;
        cmd[pos] = 0; cmd[pos + 1] = 0; cmd[pos + 2] = 0;
        int byteIdx = (int)(pcrIdx / 8);
        int bitIdx = (int)(pcrIdx % 8);
        if (byteIdx < 3) cmd[pos + byteIdx] = (byte)(1 << bitIdx);

        byte[] resp = new byte[256];
        uint respSize = (uint)resp.Length;
        uint hr = Tbsip_Submit_Command(hContext, 0, TbsCommandPriorityNormal,
            cmd, (uint)cmd.Length, resp, ref respSize);
        if (hr != TbsSuccess || respSize < 10) return null;

        int rpos = 0;
        _ = ReadBE16(resp, ref rpos);
        _ = ReadBE32(resp, ref rpos);
        uint rCode = ReadBE32(resp, ref rpos);
        if (rCode != 0) return null;

        _ = ReadBE32(resp, ref rpos); // updateCounter
        uint selCount = ReadBE32(resp, ref rpos);
        for (uint i = 0; i < selCount; i++)
        {
            rpos += 2;
            byte sizeOfSel = resp[rpos++];
            rpos += sizeOfSel;
        }

        uint digestCount = ReadBE32(resp, ref rpos);
        if (digestCount == 0) return null;
        ushort dSize = ReadBE16(resp, ref rpos);
        if (rpos + dSize > respSize) return null;
        var digest = new byte[dSize];
        Array.Copy(resp, rpos, digest, 0, dSize);
        return digest;
    }

    private static int DigestSize(ushort algId) => algId switch
    {
        0x0004 => 20,
        0x000B => 32,
        0x000C => 48,
        0x000D => 64,
        0x0012 => 32,
        _ => 0
    };

    private static void WriteBE16(byte[] buf, ref int pos, ushort val)
    {
        buf[pos++] = (byte)(val >> 8);
        buf[pos++] = (byte)val;
    }

    private static void WriteBE32(byte[] buf, ref int pos, uint val)
    {
        buf[pos++] = (byte)(val >> 24);
        buf[pos++] = (byte)(val >> 16);
        buf[pos++] = (byte)(val >> 8);
        buf[pos++] = (byte)val;
    }

    private static ushort ReadBE16(byte[] buf, ref int pos)
    {
        ushort v = (ushort)((buf[pos] << 8) | buf[pos + 1]);
        pos += 2;
        return v;
    }

    private static uint ReadBE32(byte[] buf, ref int pos)
    {
        uint v = ((uint)buf[pos] << 24) | ((uint)buf[pos + 1] << 16) |
                 ((uint)buf[pos + 2] << 8) | buf[pos + 3];
        pos += 4;
        return v;
    }
}
