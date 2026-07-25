using System.Numerics;
using System.Text;
using ImGuiNET;
using TcgBootLog.Parsing;
using TcgBootLog.Services;
using TcgBootLog.Tbs;

namespace TcgBootLog.Ui;

public sealed class AppUi
{
    private enum Page { Events, TpmValues, Integrity, BootOrder, WindowsSecurity }

    private Page _page = Page.Events;
    private List<TcgEvent> _all = [];
    private List<TcgEvent> _filtered = [];
    private TcgEventLog? _log;
    private string _status = "Ready — click Load to fetch the TCG log (Administrator required).";
    private string _error = "";
    private string _filter = "";
    private byte[] _filterBuf = new byte[256];
    private int _logTypeIndex;
    private bool _efiOnly;
    private bool _bootPcrOnly = true;
    private int _selected = -1;
    private bool _autoLoaded;
    private string _meta = "";

    // TPM Values page state
    private Dictionary<ushort, Dictionary<uint, byte[]>> _pcrs = new();
    private string _tpmError = "";
    private string _tpmStatus = "Click Refresh to read PCR values from the TPM.";

    // Integrity page state — background worker keeps spinner + window drag alive
    private readonly IntegrityScanWorker _scanWorker = new();
    private readonly IntegrityScanState _scan = new();
    private string _integrityStatus = "Click Scan to begin.";

    // Boot Order page state
    private BootOrderReport? _bootOrder;
    private string _bootStatus = "Click Refresh for Boot Order (TPM + NVRAM).";

    // Windows Security page state
    private List<SecurityFeatureStatus> _secFeatures = [];
    private string _secStatus = "Click Refresh to read Windows Security status.";
    private string _secError = "";

    private static readonly string[] LogTypes = ["SRTM Current", "SRTM Boot", "SRTM Resume", "DRTM Current"];

    public void Draw(Vector2 displaySize)
    {
        if (!_autoLoaded)
        {
            _autoLoaded = true;
            LoadLog();
        }

        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(displaySize);
        ImGui.Begin("##root",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus);

        DrawHeader();
        ImGui.Spacing();
        DrawNav();
        ImGui.Spacing();

        switch (_page)
        {
            case Page.Events: DrawEventsPage(); break;
            case Page.TpmValues: DrawTpmPage(); break;
            case Page.Integrity: DrawIntegrityPage(); break;
            case Page.BootOrder: DrawBootOrderPage(); break;
            case Page.WindowsSecurity: DrawWindowsSecurityPage(); break;
        }

        DrawStatusBar();
        ImGui.End();
    }

    private void DrawHeader()
    {
        ImGui.SetWindowFontScale(1.35f);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentHover);
        ImGui.Text("TcgBootLog");
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1f);
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
        ImGui.Text("  ·  Measured Boot / TPM Toolkit");
        ImGui.PopStyleColor();

        if (!string.IsNullOrEmpty(_meta))
        {
            ImGui.SameLine(ImGui.GetWindowWidth() - ImGui.CalcTextSize(_meta).X - 24);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
            ImGui.Text(_meta);
            ImGui.PopStyleColor();
        }
    }

    private void DrawNav()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Bg1);
        ImGui.BeginChild("nav", new Vector2(0, 54), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetCursorPos(new Vector2(16, 10));

        NavButton("Events", Page.Events);
        ImGui.SameLine();
        NavButton("TPM Values", Page.TpmValues);
        ImGui.SameLine();
        NavButton("Integrity", Page.Integrity);
        ImGui.SameLine();
        NavButton("Boot Order", Page.BootOrder);
        ImGui.SameLine();
        NavButton("Windows Security", Page.WindowsSecurity);

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void NavButton(string label, Page page)
    {
        bool active = _page == page;
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentHover);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Bg3);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentSoft);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent);
        }

        if (ImGui.Button(label, new Vector2(0, 34)))
        {
            _page = page;
            // Always reload TPM PCRs when opening that page so the table is never left empty.
            if (page == Page.TpmValues) RefreshTpmValues();
            if (page == Page.BootOrder && _bootOrder == null) RefreshBootOrder();
            if (page == Page.WindowsSecurity && _secFeatures.Count == 0) RefreshWindowsSecurity();
        }
        ImGui.PopStyleColor(3);
    }

    // ── Events ────────────────────────────────────────────────────────────

    private void DrawEventsPage()
    {
        DrawEventsToolbar();
        ImGui.Spacing();

        if (!string.IsNullOrEmpty(_error))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Danger);
            ImGui.TextWrapped(_error);
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        float detailH = _selected >= 0 && _selected < _filtered.Count ? 160f : 0f;
        float tableH = Math.Max(200f, ImGui.GetContentRegionAvail().Y - 36f - detailH);
        DrawEventTable(tableH);
        if (detailH > 0)
        {
            ImGui.Spacing();
            DrawEventDetails(_filtered[_selected]);
        }
    }

    private void DrawEventsToolbar()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Bg1);
        ImGui.BeginChild("toolbar", new Vector2(0, 64), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetCursorPos(new Vector2(16, 14));
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Log");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        ImGui.Combo("##logtype", ref _logTypeIndex, LogTypes, LogTypes.Length);
        ImGui.SameLine();
        AccentButton("Load TCG Log", new Vector2(160, 0), LoadLog);
        ImGui.SameLine();
        if (ImGui.Checkbox("EFI images", ref _efiOnly)) ApplyFilter();
        ImGui.SameLine();
        if (ImGui.Checkbox("Boot PCRs 0–7", ref _bootPcrOnly)) ApplyFilter();
        ImGui.SameLine();
        ImGui.Text("Filter");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220);
        if (ImGui.InputText("##filter", _filterBuf, (uint)_filterBuf.Length))
        {
            _filter = Encoding.UTF8.GetString(_filterBuf).TrimEnd('\0');
            ApplyFilter();
        }
        ImGui.SameLine();
        if (ImGui.Button("Export CSV")) ExportCsv();
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawEventTable(float height)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Bg1);
        ImGui.BeginChild("tablehost", new Vector2(0, height), ImGuiChildFlags.Borders);
        // Scroll via parent child only — Table ScrollY without outer_size collapses height to 0.
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("events", 6, flags))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableSetupColumn("PCR", ImGuiTableColumnFlags.WidthFixed, 56);
            ImGui.TableSetupColumn("Event Type", ImGuiTableColumnFlags.WidthStretch, 1.3f);
            ImGui.TableSetupColumn("EFI File Path", ImGuiTableColumnFlags.WidthStretch, 1.6f);
            ImGui.TableSetupColumn("Digest", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch, 2.0f);
            ImGui.TableHeadersRow();

            for (int i = 0; i < _filtered.Count; i++)
            {
                var e = _filtered[i];
                ImGui.TableNextRow();
                ImGui.PushID(i);
                ImGui.TableSetColumnIndex(0);
                if (ImGui.Selectable($"{e.Index}", i == _selected,
                        ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap))
                    _selected = i;
                ImGui.TableSetColumnIndex(1);
                ImGui.TextColored(PcrColor(e.PcrIndex), e.PcrIndex.ToString());
                ImGui.TableSetColumnIndex(2);
                ImGui.Text(e.EventTypeName);
                ImGui.TableSetColumnIndex(3);
                if (!string.IsNullOrEmpty(e.EfiFilePath))
                    ImGui.TextColored(Theme.EfiPath, e.EfiFilePath);
                else
                    ImGui.TextDisabled("—");
                ImGui.TableSetColumnIndex(4);
                ImGui.TextDisabled(ShortDigest(e.Sha256Hex));
                ImGui.TableSetColumnIndex(5);
                ImGui.TextUnformatted(Truncate(e.Details, 120));
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawEventDetails(TcgEvent e)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Bg1);
        ImGui.BeginChild("details", new Vector2(0, 150), ImGuiChildFlags.Borders);
        ImGui.TextColored(Theme.AccentHover, "Selected event");
        ImGui.Separator();
        ImGui.Columns(2, "##detailcols", false);
        ImGui.SetColumnWidth(0, 160);
        LabelValue("Index", e.Index.ToString());
        LabelValue("PCR", e.PcrIndex.ToString());
        LabelValue("Type", e.EventTypeName);
        LabelValue("Offset", $"0x{e.FileOffset:X}");
        ImGui.NextColumn();
        LabelValue("EFI path", string.IsNullOrEmpty(e.EfiFilePath) ? "—" : e.EfiFilePath);
        LabelValue("Digest", e.Sha256Hex);
        LabelValue("Details", e.Details);
        ImGui.Columns(1);
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    // ── TPM Values ────────────────────────────────────────────────────────

    private void DrawTpmPage()
    {
        AccentButton("Refresh TPM PCRs", new Vector2(200, 0), RefreshTpmValues);
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, _tpmStatus);
        ImGui.Spacing();

        if (!string.IsNullOrEmpty(_tpmError))
        {
            ImGui.TextColored(Theme.Danger, _tpmError);
            ImGui.Spacing();
        }

        // Keep a minimum height so ScrollY child never collapses to 0.
        float h = Math.Max(280f, ImGui.GetContentRegionAvail().Y - 40f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Bg1);
        ImGui.BeginChild("tpmvals", new Vector2(0, h), ImGuiChildFlags.Borders);

        int total = _pcrs.Values.Sum(b => b.Count);
        if (total == 0)
        {
            ImGui.TextColored(Theme.Warn, "No PCR values loaded yet.");
            ImGui.TextDisabled("Click \"Refresh TPM PCRs\" (Administrator required).");
        }
        else
        {
            // One table for all banks — do NOT use TableFlags.ScrollY without an outer size
            // (that collapses the table to zero height). Parent child scrolls instead.
            if (ImGui.BeginTable("pcr_all", 3,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Bank", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("PCR", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Value (hex)", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                foreach (var (algId, bank) in _pcrs.OrderBy(k => k.Key))
                {
                    string algName = TcgEventTypes.AlgNames.TryGetValue(algId, out var n) ? n : $"0x{algId:X4}";
                    foreach (var (pcr, digest) in bank.OrderBy(x => x.Key))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextDisabled(algName);
                        ImGui.TableSetColumnIndex(1);
                        ImGui.TextColored(PcrColor(pcr), $"PCR[{pcr}]");
                        ImGui.TableSetColumnIndex(2);
                        ImGui.TextUnformatted(Convert.ToHexString(digest).ToLowerInvariant());
                    }
                }

                ImGui.EndTable();
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void RefreshTpmValues()
    {
        try
        {
            _tpmError = "";
            _tpmStatus = "Reading PCRs from TPM…";
            // SHA-256 and SHA-1 banks, PCR 0..23
            ushort[] algs = [0x000B, 0x0004];
            uint[] indices = Enumerable.Range(0, 24).Select(i => (uint)i).ToArray();
            _pcrs = TbsApi.ReadPcrValues(algs, indices);
            int count = _pcrs.Values.Sum(b => b.Count);
            if (count == 0)
            {
                _tpmError = "TPM returned no PCR digests. Is the TPM available and are you running as Administrator?";
                _tpmStatus = "No PCR data";
            }
            else
            {
                string breakdown = string.Join(", ",
                    _pcrs.Select(kv =>
                    {
                        string name = TcgEventTypes.AlgNames.TryGetValue(kv.Key, out var n) ? n : $"0x{kv.Key:X4}";
                        return $"{name}:{kv.Value.Count}";
                    }));
                _tpmStatus = $"Loaded {count} PCR values ({breakdown}).";
            }
            _status = _tpmStatus;
        }
        catch (Exception ex)
        {
            _tpmError = ex.Message;
            _tpmStatus = "Error";
            _status = "TPM Values error";
            _pcrs = new();
        }
    }

    // ── Integrity ─────────────────────────────────────────────────────────

    private void DrawIntegrityPage()
    {
        // Pull latest results from the background worker (non-blocking)
        _scanWorker.Snapshot(_scan);
        bool scanning = _scanWorker.IsRunning;
        _integrityStatus = _scanWorker.GetStatus();
        if (!string.IsNullOrEmpty(_integrityStatus))
            _status = _integrityStatus;

        if (scanning)
            ImGui.BeginDisabled();
        AccentButton("Scan", new Vector2(140, 0), StartIntegrityScan);
        if (scanning)
            ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, _integrityStatus);
        ImGui.Spacing();

        // Spinner keeps animating because checks run off the UI thread
        if (scanning)
        {
            LoadingSpinner.Draw(14f, 3.4f);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Theme.AccentHover, _scanWorker.GetScanningText());
            ImGui.Spacing();
        }

        float h = Math.Max(280f, ImGui.GetContentRegionAvail().Y - 40f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Bg1);
        ImGui.BeginChild("integrity", new Vector2(0, h), ImGuiChildFlags.Borders);

        if (_scan.Ntp != null)
        {
            DrawCheckRow("NTP", _scan.Ntp.Ok, CleanReason(_scan.Ntp.Reason));
            if (_scan.Ntp.LocalUtc != default)
            {
                ImGui.Indent(34);
                ImGui.TextDisabled($"Local UTC: {_scan.Ntp.LocalUtc:u}");
                ImGui.TextDisabled($"NTP UTC:   {_scan.Ntp.NtpUtc:u}  ({_scan.Ntp.Server})");
                ImGui.TextDisabled($"Offset:    {_scan.Ntp.OffsetSeconds:+0.000;-0.000;0}s");
                ImGui.Unindent(34);
            }
            ImGui.Spacing();
        }

        if (_scan.Ek != null)
        {
            DrawCheckRow("EK", _scan.Ek.Ok, CleanReason(_scan.Ek.Reason));
            if (_scan.Ek.Certs.Count > 0)
            {
                ImGui.Indent(34);
                foreach (var c in _scan.Ek.Certs.Take(6))
                    ImGui.TextDisabled($"{(c.IsLeafLikely ? "leaf" : "ca  ")}  {Truncate(c.Subject, 80)}");
                ImGui.Unindent(34);
            }
            ImGui.Spacing();
        }

        if (_scan.Ak != null)
        {
            DrawCheckRow("AK", _scan.Ak.Ok, CleanReason(_scan.Ak.Reason));
            if (!string.IsNullOrEmpty(_scan.Ak.AkNameHex))
            {
                ImGui.Indent(34);
                ImGui.TextDisabled("AK Name: " + _scan.Ak.AkNameHex);
                ImGui.Unindent(34);
            }
            ImGui.Spacing();
        }

        if (_scan.Pcr != null)
        {
            DrawCheckRow("PCR", _scan.Pcr.Ok, CleanReason(_scan.Pcr.Reason));
            if (_scan.Pcr.Rows.Count > 0 && ImGui.BeginTable("pcrcmp", 4,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.ScrollY,
                    new Vector2(0, 280)))
            {
                ImGui.TableSetupColumn("PCR", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Match", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Expected (replay)", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Actual (TPM)", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                foreach (var row in _scan.Pcr.Rows)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text(row.Pcr.ToString());
                    ImGui.TableSetColumnIndex(1);
                    StatusIcon.Draw(row.Match, ImGui.GetTextLineHeight());
                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextDisabled(ShortDigest(row.ExpectedHex));
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextDisabled(ShortDigest(row.ActualHex));
                }
                ImGui.EndTable();
            }
            ImGui.Spacing();
        }

        if (_scanWorker.Finished && _scan.AllDone)
        {
            ImGui.Separator();
            StatusIcon.Draw(_scan.AllOk, ImGui.GetTextLineHeight() * 1.4f);
            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4);
            ImGui.TextColored(_scan.AllOk ? Theme.Ok : Theme.Danger,
                _scan.AllOk ? "All checks passed" : "Checks failed");
        }
        else if (!_scanWorker.IsRunning && !_scan.AllDone)
        {
            ImGui.TextDisabled("Press Scan to check NTP, EK, AK and PCR one by one.");
        }

        string? err = _scanWorker.Error;
        if (!string.IsNullOrEmpty(err))
            ImGui.TextColored(Theme.Danger, err);

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void StartIntegrityScan()
    {
        if (_scanWorker.IsRunning) return;
        _scan.Ntp = null;
        _scan.Ek = null;
        _scan.Ak = null;
        _scan.Pcr = null;
        _integrityStatus = "Scanning…";
        _status = _integrityStatus;
        _scanWorker.Start(_log);
    }

    private static void DrawCheckRow(string name, bool ok, string reason)
    {
        StatusIcon.Draw(ok);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{name}");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
        ImGui.TextWrapped(reason);
        ImGui.PopStyleColor();
    }

    private static string CleanReason(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return "";
        if (reason.StartsWith("OK —", StringComparison.Ordinal)) return reason[4..].Trim();
        if (reason.StartsWith("OK -", StringComparison.Ordinal)) return reason[4..].Trim();
        return reason;
    }

    // ── Boot Order ────────────────────────────────────────────────────────

    private void DrawBootOrderPage()
    {
        AccentButton("Refresh Boot Order", new Vector2(220, 0), RefreshBootOrder);
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, _bootStatus);
        ImGui.Spacing();

        if (_bootOrder == null) return;

        if (_bootOrder.OrderIds.Count > 0)
        {
            ImGui.TextColored(Theme.AccentHover, "BootOrder IDs:");
            ImGui.SameLine();
            ImGui.Text(string.Join(" → ", _bootOrder.OrderIds.Select(id => $"Boot{id:X4}")));
            ImGui.Spacing();
        }

        float h = Math.Max(180f, (ImGui.GetContentRegionAvail().Y - 48f) / 2f);

        DrawBootTable("From TPM (TCG event log)", _bootOrder.FromTpm, _bootOrder.TpmError, h);
        ImGui.Spacing();
        DrawBootTable("From NVRAM (UEFI firmware variables)", _bootOrder.FromNvram, _bootOrder.NvramError, h);
    }

    private void DrawBootTable(string title, List<BootEntry> entries, string? error, float height)
    {
        ImGui.TextColored(Theme.AccentHover, title);
        if (!string.IsNullOrEmpty(error))
            ImGui.TextColored(Theme.Danger, error);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Bg1);
        ImGui.BeginChild(title, new Vector2(0, height), ImGuiChildFlags.Borders);

        if (ImGui.BeginTable(title + "_tbl", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("EFI Path", ImGuiTableColumnFlags.WidthStretch, 1.6f);
            ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableHeadersRow();

            foreach (var e in entries)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text(e.OrderIndex.ToString());
                ImGui.TableSetColumnIndex(1);
                ImGui.Text(e.Id);
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(e.Description);
                ImGui.TableSetColumnIndex(3);
                if (!string.IsNullOrEmpty(e.EfiPath))
                    ImGui.TextColored(Theme.EfiPath, e.EfiPath);
                else
                    ImGui.TextDisabled("—");
                ImGui.TableSetColumnIndex(4);
                ImGui.TextDisabled(e.Source);
            }
            ImGui.EndTable();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void RefreshBootOrder()
    {
        try
        {
            _bootOrder = BootOrderService.Build(_all);
            int n = _bootOrder.FromTpm.Count + _bootOrder.FromNvram.Count;
            _bootStatus = $"Boot Order: {_bootOrder.FromTpm.Count} TPM + {_bootOrder.FromNvram.Count} NVRAM entries.";
            _status = _bootStatus;
            if (n == 0 && _bootOrder.NvramError != null)
                _bootStatus += "  " + _bootOrder.NvramError;
        }
        catch (Exception ex)
        {
            _bootStatus = ex.Message;
            _status = "Boot Order error";
        }
    }

    // ── Windows Security ──────────────────────────────────────────────────

    private void DrawWindowsSecurityPage()
    {
        AccentButton("Refresh", new Vector2(140, 0), RefreshWindowsSecurity);
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, _secStatus);
        ImGui.Spacing();

        if (!string.IsNullOrEmpty(_secError))
            ImGui.TextColored(Theme.Danger, _secError);

        float h = Math.Max(280f, ImGui.GetContentRegionAvail().Y - 40f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Bg1);
        ImGui.BeginChild("winsec", new Vector2(0, h), ImGuiChildFlags.Borders);

        if (_secFeatures.Count == 0)
        {
            ImGui.TextDisabled("Press Refresh to check Hypervisor, VBS, HVCI, Secure Boot, driver signing, CI, and vulnerable-driver policy.");
            ImGui.EndChild();
            ImGui.PopStyleColor();
            return;
        }

        if (ImGui.BeginTable("winsec_tbl", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.ScrollY,
                new Vector2(0, h - 16f)))
        {
            ImGui.TableSetupColumn(" ", ImGuiTableColumnFlags.WidthFixed, 48);
            ImGui.TableSetupColumn("Feature", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 56);
            ImGui.TableSetupColumn("Detail", ImGuiTableColumnFlags.WidthStretch, 2.2f);
            ImGui.TableHeadersRow();

            foreach (var f in _secFeatures)
            {
                ImGui.TableNextRow();
                float rowY = ImGui.GetCursorPosY();

                ImGui.TableSetColumnIndex(0);
                ImGui.SetCursorPosY(rowY + 6f);
                FeatureIcons.Draw(f.Kind, ImGui.GetTextLineHeight() * 1.45f);

                ImGui.TableSetColumnIndex(1);
                ImGui.SetCursorPosY(rowY + 10f);
                ImGui.TextUnformatted(f.Name);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
                ImGui.TextWrapped(f.Summary);
                ImGui.PopStyleColor();

                ImGui.TableSetColumnIndex(2);
                ImGui.SetCursorPosY(rowY + 12f);
                StatusIcon.Draw(f.Ok, ImGui.GetTextLineHeight() * 1.2f);

                ImGui.TableSetColumnIndex(3);
                ImGui.SetCursorPosY(rowY + 10f);
                ImGui.TextDisabled(f.Detail);
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        int okCount = _secFeatures.Count(f => f.Ok);
        StatusIcon.Draw(okCount == _secFeatures.Count, ImGui.GetTextLineHeight() * 1.3f);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(
            okCount == _secFeatures.Count ? Theme.Ok : Theme.Danger,
            $"{okCount}/{_secFeatures.Count} checks passed");

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void RefreshWindowsSecurity()
    {
        try
        {
            _secError = "";
            _secFeatures = WindowsSecurityChecker.CheckAll();
            int ok = _secFeatures.Count(f => f.Ok);
            _secStatus = $"Windows Security: {ok}/{_secFeatures.Count} OK";
            _status = _secStatus;
        }
        catch (Exception ex)
        {
            _secError = ex.Message;
            _secStatus = "Windows Security error";
            _status = _secStatus;
            _secFeatures = [];
        }
    }

    // ── shared helpers ────────────────────────────────────────────────────

    private static void AccentButton(string label, Vector2 size, Action onClick)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.AccentSoft);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentHover);
        if (ImGui.Button(label, size)) onClick();
        ImGui.PopStyleColor(3);
    }

    private static void LabelValue(string label, string value)
    {
        ImGui.TextColored(Theme.TextDim, label);
        ImGui.SameLine(150);
        ImGui.TextWrapped(value);
    }

    private void DrawStatusBar()
    {
        ImGui.SetCursorPosY(ImGui.GetWindowHeight() - 28);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
        ImGui.Text(_status);
        ImGui.PopStyleColor();
    }

    private void LoadLog()
    {
        try
        {
            _error = "";
            _status = "Fetching TCG log via TBS…";
            var raw = TbsApi.GetTcgLog(SelectedLogType());
            _log = TcgLogParser.Parse(raw, SelectedLogType().ToString());
            _all = _log.Events;
            int withPath = _all.Count(e => !string.IsNullOrEmpty(e.EfiFilePath));
            _meta = $"{_all.Count} events · {(_log.IsCryptoAgile ? "TCG 2.0" : "TCG 1.2")} · {_log.FileSize / 1024f:0.0} KB";
            _status = $"Loaded {_all.Count} events — {withPath} with EFI path.";
            _selected = -1;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _status = "Load failed.";
            _all = [];
            _filtered = [];
            _log = null;
            _meta = "";
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<TcgEvent> q = _all;
        if (_efiOnly) q = q.Where(e => e.IsEfiImageLoad || !string.IsNullOrEmpty(e.EfiFilePath));
        if (_bootPcrOnly) q = q.Where(e => e.PcrIndex <= 7);
        string f = _filter.Trim();
        if (f.Length > 0)
        {
            q = q.Where(e =>
                e.EventTypeName.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (e.EfiFilePath?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false)
                || e.Details.Contains(f, StringComparison.OrdinalIgnoreCase)
                || e.Sha256Hex.Contains(f, StringComparison.OrdinalIgnoreCase)
                || e.PcrIndex.ToString() == f);
        }
        _filtered = q.ToList();
        if (_selected >= _filtered.Count) _selected = -1;
        if (_all.Count > 0) _status = $"Showing {_filtered.Count} / {_all.Count} events.";
    }

    private void ExportCsv()
    {
        if (_filtered.Count == 0) return;
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"tcg-boot-log-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        using var sw = new StreamWriter(path);
        sw.WriteLine("Index,PCR,EventType,EfiFilePath,Digest,Details");
        foreach (var e in _filtered)
        {
            static string Esc(string s) =>
                s.Contains('"') || s.Contains(',') || s.Contains('\n')
                    ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            sw.WriteLine(string.Join(',', e.Index, e.PcrIndex, Esc(e.EventTypeName),
                Esc(e.EfiFilePath ?? ""), Esc(e.Sha256Hex), Esc(e.Details)));
        }
        _status = $"Exported {_filtered.Count} rows → {path}";
    }

    private TbsApi.TcgLogType SelectedLogType() => _logTypeIndex switch
    {
        1 => TbsApi.TcgLogType.SrtmBoot,
        2 => TbsApi.TcgLogType.SrtmResume,
        3 => TbsApi.TcgLogType.DrtmCurrent,
        _ => TbsApi.TcgLogType.SrtmCurrent,
    };

    private static Vector4 PcrColor(uint pcr) => pcr switch
    {
        <= 3 => Theme.Warn,
        <= 7 => Theme.AccentHover,
        _ => Theme.TextDim,
    };

    private static string ShortDigest(string hex) =>
        string.IsNullOrEmpty(hex) ? "" : hex.Length <= 20 ? hex : hex[..10] + "…" + hex[^8..];

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
