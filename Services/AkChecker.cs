using Tpm2Lib;

namespace TcgBootLog.Services;

public sealed class AkCheckResult
{
    public bool Ok { get; init; }
    public string Reason { get; init; } = "";
    public string AkNameHex { get; init; } = "";
}

/// <summary>
/// Local AK probe: create EK (or use persisted) + SRK + restricted signing AK.
/// Success means TPM can produce a usable attestation key (no remote server).
/// </summary>
public static class AkChecker
{
    static readonly byte[] EkPolicy = Convert.FromHexString(
        "837197674484b3f81a90cc8d46a5d724fd52d76e06520b64f2a1da1b331469aa");

    public static AkCheckResult Check()
    {
        try
        {
            using var device = new TbsDevice();
            device.Connect();
            using var tpm = new Tpm2(device);

            var (ekHandle, _, ekPersisted) = GetOrCreateEk(tpm);
            TpmHandle srk = TpmHandle.RhNull;
            TpmHandle ak = TpmHandle.RhNull;
            try
            {
                srk = CreateSrk(tpm);
                var (akHandle, _, akName) = CreateAk(tpm, srk);
                ak = akHandle;
                return new AkCheckResult
                {
                    Ok = true,
                    Reason = "AK created under SRK (local TPM probe)",
                    AkNameHex = Convert.ToHexString(akName).ToLowerInvariant(),
                };
            }
            finally
            {
                tpm._AllowErrors().FlushContext(ak);
                tpm._AllowErrors().FlushContext(srk);
                if (!ekPersisted)
                    tpm._AllowErrors().FlushContext(ekHandle);
            }
        }
        catch (Exception ex)
        {
            return new AkCheckResult { Ok = false, Reason = "AK error: " + ex.Message };
        }
    }

    private static (TpmHandle handle, TpmPublic pub, bool persisted) GetOrCreateEk(Tpm2 tpm)
    {
        // Try common persistent EK handle 0x81010001
        var persistent = new TpmHandle(0x81010001);
        try
        {
            var read = tpm.ReadPublic(persistent, out _, out _);
            return (persistent, read, true);
        }
        catch
        {
            // create primary in Endorsement
        }

        var ekTemplate = new TpmPublic(
            TpmAlgId.Sha256,
            ObjectAttr.FixedTPM | ObjectAttr.FixedParent | ObjectAttr.SensitiveDataOrigin |
            ObjectAttr.AdminWithPolicy | ObjectAttr.Restricted | ObjectAttr.Decrypt,
            EkPolicy,
            new RsaParms(
                new SymDefObject(TpmAlgId.Aes, 128, TpmAlgId.Cfb),
                new NullAsymScheme(),
                2048,
                0),
            new Tpm2bPublicKeyRsa());

        var inSens = new SensitiveCreate(Array.Empty<byte>(), Array.Empty<byte>());
        var handle = tpm.CreatePrimary(
            TpmRh.Endorsement, inSens, ekTemplate,
            Array.Empty<byte>(), Array.Empty<PcrSelection>(),
            out TpmPublic pub, out _, out _, out _);
        return (handle, pub, false);
    }

    private static TpmHandle CreateSrk(Tpm2 tpm)
    {
        var srkTemplate = new TpmPublic(
            TpmAlgId.Sha256,
            ObjectAttr.FixedTPM | ObjectAttr.FixedParent | ObjectAttr.SensitiveDataOrigin |
            ObjectAttr.UserWithAuth | ObjectAttr.NoDA | ObjectAttr.Restricted | ObjectAttr.Decrypt,
            Array.Empty<byte>(),
            new RsaParms(
                new SymDefObject(TpmAlgId.Aes, 128, TpmAlgId.Cfb),
                new NullAsymScheme(),
                2048,
                0),
            new Tpm2bPublicKeyRsa());

        var inSens = new SensitiveCreate(Array.Empty<byte>(), Array.Empty<byte>());
        return tpm.CreatePrimary(
            TpmRh.Owner, inSens, srkTemplate,
            Array.Empty<byte>(), Array.Empty<PcrSelection>(),
            out _, out _, out _, out _);
    }

    private static (TpmHandle handle, TpmPublic pub, byte[] name) CreateAk(Tpm2 tpm, TpmHandle srk)
    {
        var akTemplate = new TpmPublic(
            TpmAlgId.Sha256,
            ObjectAttr.FixedTPM | ObjectAttr.FixedParent | ObjectAttr.SensitiveDataOrigin |
            ObjectAttr.UserWithAuth | ObjectAttr.NoDA | ObjectAttr.Restricted | ObjectAttr.Sign,
            Array.Empty<byte>(),
            new RsaParms(
                new SymDefObject(),
                new SchemeRsassa(TpmAlgId.Sha256),
                2048,
                0),
            new Tpm2bPublicKeyRsa());

        TpmPrivate priv = tpm.Create(srk, new SensitiveCreate(), akTemplate,
            Array.Empty<byte>(), Array.Empty<PcrSelection>(),
            out TpmPublic pub, out _, out _, out _);
        var handle = tpm.Load(srk, priv, pub);
        tpm.ReadPublic(handle, out byte[] name, out _);
        return (handle, pub, name);
    }
}
