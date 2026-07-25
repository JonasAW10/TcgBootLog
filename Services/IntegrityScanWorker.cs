using TcgBootLog.Parsing;

namespace TcgBootLog.Services;

/// <summary>
/// Runs NTP → EK → AK → PCR on a background thread so the UI (spinner + window drag) stays alive.
/// Results are published one-by-one under a lock for the UI to pick up.
/// </summary>
public sealed class IntegrityScanWorker
{
    private readonly object _gate = new();
    private Thread? _thread;
    private bool _running;
    private bool _finished;
    private string _scanningText = "";
    private string _status = "";
    private string? _error;

    private NtpCheckResult? _ntp;
    private EkCheckResult? _ek;
    private AkCheckResult? _ak;
    private PcrCheckResult? _pcr;

    public bool IsRunning { get { lock (_gate) return _running; } }
    public bool Finished { get { lock (_gate) return _finished; } }
    public string? Error { get { lock (_gate) return _error; } }

    public void Snapshot(IntegrityScanState into)
    {
        lock (_gate)
        {
            into.Ntp = _ntp;
            into.Ek = _ek;
            into.Ak = _ak;
            into.Pcr = _pcr;
        }
    }

    public string GetScanningText()
    {
        lock (_gate) return _scanningText;
    }

    public string GetStatus()
    {
        lock (_gate) return _status;
    }

    public void Start(TcgEventLog? log)
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;
            _finished = false;
            _error = null;
            _ntp = null;
            _ek = null;
            _ak = null;
            _pcr = null;
            _scanningText = "Scanning for NTP…";
            _status = "Scanning…";
        }

        _thread = new Thread(() => Run(log))
        {
            IsBackground = true,
            Name = "IntegrityScan",
        };
        _thread.Start();
    }

    private void Run(TcgEventLog? log)
    {
        try
        {
            SetScanning("Scanning for NTP…");
            var ntp = IntegrityChecker.CheckNtp();
            lock (_gate) { _ntp = ntp; _status = "NTP done — scanning EK…"; }
            Thread.Sleep(200); // brief pause so the UI can show the checkmark

            SetScanning("Scanning for EK…");
            var ek = IntegrityChecker.CheckEk();
            lock (_gate) { _ek = ek; _status = "EK done — scanning AK…"; }
            Thread.Sleep(200);

            SetScanning("Scanning for AK…");
            var ak = IntegrityChecker.CheckAk();
            lock (_gate) { _ak = ak; _status = "AK done — scanning PCR…"; }
            Thread.Sleep(200);

            SetScanning("Scanning for PCR…");
            var pcr = IntegrityChecker.CheckPcr(log);
            lock (_gate)
            {
                _pcr = pcr;
                _scanningText = "";
                _finished = true;
                _running = false;
                bool allOk = _ntp is { Ok: true } && _ek is { Ok: true } && _ak is { Ok: true } && _pcr is { Ok: true };
                _status = allOk ? "All checks passed" : "One or more checks failed";
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _error = ex.Message;
                _scanningText = "";
                _finished = true;
                _running = false;
                _status = "Error: " + ex.Message;
            }
        }
    }

    private void SetScanning(string text)
    {
        lock (_gate)
        {
            _scanningText = text;
            _status = text;
        }
    }
}
