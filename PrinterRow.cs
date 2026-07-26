using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace PrinterTool;

public sealed class TonerBar
{
    public required string Name { get; init; }
    public required int Percent { get; init; }        // 0..100
    public required string Kind { get; init; }        // "k" | "c" | "m" | "y" — XAML maps to theme-aware brush
    public double Width => Math.Max(2, Percent * 0.60);     // 60px track = 100 %
    public string Tooltip => $"{Name}: {Percent}%";
}

public sealed class PrinterRow : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string Unc { get; init; }
    public string Location { get; init; } = "";
    public string Comment { get; init; } = "";
    public string Driver { get; init; } = "";
    /// <summary>Spooler driver version: 3 = classic Type 3, 4 = modern Type 4, 0 = unknown.</summary>
    public uint DriverVersion { get; init; }
    public string DriverTypeText => DriverVersion switch { 4 => "Type 4", 3 => "Type 3", 0 => "", _ => "v" + DriverVersion };
    /// <summary>Type 3 sorts first — those are the queues that cause prompts.</summary>
    public int DriverTypeRank => DriverVersion == 3 ? 0 : DriverVersion == 4 ? 1 : 2;
    public string? DriverTypeTip => DriverVersion switch
    {
        4 => Loc.T("Type4Tip"),
        3 => Loc.T("Type3Tip"),
        _ => null,
    };
    /// <summary>Jobs waiting in this queue right now (from the print server).</summary>
    public uint Jobs { get; init; }
    public string JobsText => Jobs == 0 ? "" : Jobs.ToString();
    public string Port { get; init; } = "";
    /// <summary>Device address parsed from the port name; empty for WSD/LPR/virtual queues.</summary>
    public string Ip { get; init; } = "";

    /// <summary>http URL of the device's web interface, or "" when no real IPv4 is known.</summary>
    private static readonly System.Text.RegularExpressions.Regex Ipv4 =
        new(@"\d{1,3}(?:\.\d{1,3}){3}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The IPv4 address contained in the port name, or "" (handles "IP_10.1.2.3" style names).</summary>
    public string IpAddress
    {
        get
        {
            var m = Ipv4.Match(Ip ?? "");
            return m.Success && System.Net.IPAddress.TryParse(m.Value, out _) ? m.Value : "";
        }
    }

    /// <summary>What the IP column shows: the clean address when one exists, else the raw port name.</summary>
    public string IpDisplay => IpAddress.Length > 0 ? IpAddress : Ip;

    /// <summary>http URL of the device's web interface, or "" when no real IPv4 is known.</summary>
    public string IpUrl => IpAddress.Length > 0 ? "http://" + IpAddress : "";

    /// <summary>Strips characters the server could not encode, so no "&#xFFFD;" boxes appear.</summary>
    public static string Clean(string? value) =>
        string.IsNullOrEmpty(value) ? "" : value!.Replace("\uFFFD", "");

    /// <summary>Location at full size; local printers show "This PC" here instead of a tiny sub-line.</summary>
    public string LocationDisplay => Location.Length > 0 ? Location : (IsLocal ? Loc.T("ThisPC") : "");

    /// <summary>Secondary server line — suppressed for local printers (their location already says it).</summary>
    public string ServerLine => IsLocal ? "" : ServerShort;

    /// <summary>Comment for display: any IPv4 address is stripped since the IP has its own column.</summary>
    public string CommentDisplay =>
        System.Text.RegularExpressions.Regex
            .Replace(
                System.Text.RegularExpressions.Regex.Replace(Clean(Comment), @"https?://\S*", ""),
                @"\b\d{1,3}(?:\.\d{1,3}){3}\b", "")
            .Trim(' ', '-', '–', '·', ',', ';', '(', ')');
    /// <summary>Server host (no leading backslashes); shown only in multi-server mode.</summary>
    public string ServerShort { get; init; } = "";
    /// <summary>True for printers that live on this PC (Print to PDF, OneNote, USB…).</summary>
    public bool IsLocal { get; init; }
    private uint _statusFlags;
    /// <summary>Live status bits; updated in place by the background refresh.</summary>
    public uint StatusFlags
    {
        get => _statusFlags;
        set
        {
            if (_statusFlags == value) return;
            _statusFlags = value;
            Notify();
            Notify(nameof(StatusText));
            Notify(nameof(StatusTooltip));
            Notify(nameof(StatusBrush));
            Notify(nameof(StatusRank));
            Notify(nameof(IsOffline));
        }
    }

    private string? _blob;   // cached — the filter runs this on every keystroke for every row
    public string SearchBlob => _blob ??=
        Normalize(Name + "\n" + Location + "\n" + Comment + "\n" + ServerShort + "\n" + Ip
                  + "\n" + Model + "\n" + Serial);

    /// <summary>
    /// Lower-cases and strips diacritics so a search for "gotzis" also finds "Götzis"
    /// (and "Götzis" still finds it). German umlauts additionally expand: ü → ue.
    /// </summary>
    public static string Normalize(string value)
    {
        var pre = new System.Text.StringBuilder(value.Length + 8);
        foreach (char c in value.ToLowerInvariant())
        {
            switch (c)
            {
                case 'ä': pre.Append("ae"); break;
                case 'ö': pre.Append("oe"); break;
                case 'ü': pre.Append("ue"); break;
                case 'ß': pre.Append("ss"); break;
                default: pre.Append(c); break;
            }
        }
        string decomposed = pre.ToString().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private bool _installed;
    public bool Installed
    {
        get => _installed;
        set
        {
            if (_installed == value) return;
            _installed = value;
            Notify(); Notify(nameof(ActionLabel)); Notify(nameof(CanSetDefault));
        }
    }

    private bool _isDefault;
    /// <summary>True when this queue is the user's Windows default printer.</summary>
    public bool IsDefault
    {
        get => _isDefault;
        set { if (_isDefault == value) return; _isDefault = value; Notify(); Notify(nameof(CanSetDefault)); Notify(nameof(DefaultLabel)); }
    }

    private NetProbe.Result _probe = NetProbe.Result.Unknown;
    private DateTime _probedAt;

    /// <summary>Result of the last direct network check of this device.</summary>
    public NetProbe.Result Probe
    {
        get => _probe;
        set
        {
            _probe = value;
            _probedAt = DateTime.Now;
            Notify(); Notify(nameof(ProbeText)); Notify(nameof(ProbeTip)); Notify(nameof(ProbeIsGood));
        }
    }

    /// <summary>
    /// Secondary line under an offline status: what the device itself says right now.
    /// The print server only knows its own last-known state, which is often stale.
    /// </summary>
    public string ProbeText =>
        _probe == NetProbe.Result.Reachable ? Loc.T("DeviceUp") : "";

    public bool ProbeIsGood => _probe == NetProbe.Result.Reachable;

    public string? ProbeTip => _probe == NetProbe.Result.Unknown
        ? null
        : Loc.T("ProbeHint") + " (" + _probedAt.ToString("HH:mm:ss") + ")";

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        set { _busy = value; Notify(); Notify(nameof(ActionLabel)); }
    }

    public string ActionLabel =>
        Busy ? Loc.T(Installed ? "Removing" : "Installing")
             : Loc.T(Installed ? "Remove" : "Install");

    /// <summary>Grouping key — installed printers form a pinned section on top.</summary>
    public string GroupLabel => Loc.T(Installed ? "Grp_Installed" : "Grp_Available");

    /// <summary>Sort rank for the Status column: ready → activity → warning → error → offline.</summary>
    public int StatusRank
    {
        get
        {
            var f = StatusFlags;
            if ((f & (NativePrinting.PS_OFFLINE | NativePrinting.PS_NOT_AVAILABLE |
                      NativePrinting.PS_SERVER_UNKNOWN)) != 0) return 4;
            if ((f & (NativePrinting.PS_PAPER_JAM | NativePrinting.PS_NO_TONER | NativePrinting.PS_ERROR |
                      NativePrinting.PS_DOOR_OPEN | NativePrinting.PS_USER_INTERVENTION |
                      NativePrinting.PS_OUT_OF_MEMORY | NativePrinting.PS_PAPER_OUT)) != 0) return 3;
            if ((f & (NativePrinting.PS_TONER_LOW | NativePrinting.PS_PAUSED | NativePrinting.PS_PAPER_PROBLEM |
                      NativePrinting.PS_OUTPUT_BIN_FULL | NativePrinting.PS_MANUAL_FEED |
                      NativePrinting.PS_PAGE_PUNT | NativePrinting.PS_PENDING_DELETION)) != 0) return 2;
            if ((f & (NativePrinting.PS_PRINTING | NativePrinting.PS_PROCESSING | NativePrinting.PS_BUSY |
                      NativePrinting.PS_WARMING_UP | NativePrinting.PS_INITIALIZING |
                      NativePrinting.PS_WAITING | NativePrinting.PS_IO_ACTIVE)) != 0) return 1;
            return 0;
        }
    }

    /// <summary>Sort rank for the Toner column: lowest cartridge first; no data sorts last.</summary>
    public int TonerMin
    {
        get
        {
            if (Toner.Count == 0) return int.MaxValue;
            int min = int.MaxValue;
            foreach (var t in Toner) if (t.Percent < min) min = t.Percent;
            return min;
        }
    }

    /// <summary>Most important active condition, e.g. "Paper jam" instead of a bare "Error".</summary>
    public string StatusText
    {
        get
        {
            var keys = NativePrinting.DescribeStatus(StatusFlags);
            return keys.Count == 0 ? Loc.T("St_Ready") : Loc.T(keys[0]);
        }
    }

    /// <summary>All active conditions — shown as the cell tooltip when there is more than one.</summary>
    public string? StatusTooltip
    {
        get
        {
            var keys = NativePrinting.DescribeStatus(StatusFlags);
            return keys.Count < 2 ? null : string.Join(" · ", keys.Select(Loc.T));
        }
    }

    public Brush StatusBrush => StatusRank switch
    {
        4 => Brushes.Gray,
        3 => _err,
        2 => _warn,
        1 => _info,
        _ => _ok,
    };

    private static readonly Brush _ok = Frozen(0x1E, 0x8A, 0x4C);
    private static readonly Brush _warn = Frozen(0xB8, 0x86, 0x00);
    private static readonly Brush _err = Frozen(0xC8, 0x32, 0x2B);
    private static readonly Brush _info = Frozen(0x0F, 0x62, 0xB4);
    private static Brush Frozen(byte r, byte g, byte b)
    { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

    // ---- device data pulled over SNMP ----
    private string _model = "", _serial = "", _display = "", _trays = "";
    private long _pages = -1, _uptimeTicks = -1;
    private int _trayMin = int.MaxValue;
    /// <summary>Lowest tray fill percentage — Paper column sorts on this.</summary>
    public int TrayMin => _trayMin;

    public string Model { get => _model; private set { _model = value; Notify(); } }
    public string Serial { get => _serial; private set { _serial = value; Notify(); } }
    public string Display { get => _display; private set { _display = value; Notify(); } }
    public string Trays { get => _trays; private set { _trays = value; Notify(); } }

    public long Pages => _pages;
    public string PagesText => _pages < 0 ? "" : _pages.ToString("N0");

    public long UptimeTicks => _uptimeTicks;
    public string UptimeText
    {
        get
        {
            if (_uptimeTicks < 0) return "";
            var t = TimeSpan.FromSeconds(_uptimeTicks / 100.0);
            if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            return $"{(int)t.TotalMinutes}m";
        }
    }

    public void SetDevice(Snmp.DeviceInfo info)
    {
        Model = info.Model;
        Serial = info.Serial;
        Display = info.Display;
        _pages = info.PageCount;
        _uptimeTicks = info.UptimeTicks;
        Trays = info.Trays.Count == 0
            ? ""
            : string.Join(" · ", info.Trays.Select(t => $"{Shorten(t.Name)} {t.Percent}%"));
        _trayMin = info.Trays.Count == 0 ? int.MaxValue : info.Trays.Min(t => t.Percent);
        Notify(nameof(PagesText));
        Notify(nameof(UptimeText));
        Notify(nameof(JobsText));
        _blob = null;   // model/serial are searchable, so the cached index must rebuild
        if (info.Supplies.Count > 0) SetSupplies(info.Supplies);
    }

    /// <summary>Tray names from devices are verbose ("Cassette 1 (A4)"); keep them column-sized.</summary>
    private static string Shorten(string name) =>
        name.Length <= 10 ? name : name.Substring(0, 10).TrimEnd();

    public ObservableCollection<TonerBar> Toner { get; } = new();

    public void SetSupplies(IEnumerable<Snmp.Supply> supplies)
    {
        Toner.Clear();
        foreach (var s in supplies)
        {
            if (s.Percent < 0) continue;
            string d = s.Description.ToLowerInvariant();
            // Only marker supplies that behave like ink/toner; skip waste containers, drums, fusers
            if (d.Contains("waste") || d.Contains("resttoner") || d.Contains("fuser") ||
                d.Contains("belt") || d.Contains("roller") || d.Contains("maintenance")) continue;

            string kind =
                d.Contains("cyan") ? "c" :
                d.Contains("magenta") ? "m" :
                d.Contains("yellow") || d.Contains("gelb") ? "y" :
                "k";   // black / unknown

            Toner.Add(new TonerBar { Name = s.Description, Percent = s.Percent, Kind = kind });
            if (Toner.Count == 4) break;
        }
        Notify(nameof(Toner));
        Notify(nameof(TonerMin));
    }

    public void RefreshLanguage()
    {
        Notify(nameof(ActionLabel));
        Notify(nameof(StatusText));
        Notify(nameof(StatusTooltip));
        Notify(nameof(ProbeText));
        Notify(nameof(ProbeTip));
        Notify(nameof(SetDefaultLabel));
        Notify(nameof(TestPageLabel));
        Notify(nameof(ClearQueueLabel));
        Notify(nameof(LocationDisplay));
        Notify(nameof(DefaultLabel));
        Notify(nameof(DefaultTip));
    }

    // ---- labels used by the row context menu and tooltips ----
    public string SetDefaultLabel => Loc.T("SetDefault");
    public string TestPageLabel => Loc.T("TestPage");
    public string ClearQueueLabel => Loc.T("ClearQueue");
    /// <summary>Button text: "✓ Default" when active, "Default" when clickable.</summary>
    public string DefaultLabel => IsDefault ? "✓ " + Loc.T("Default") : Loc.T("Default");
    public string DefaultTip => Loc.T("IsDefault");
    public bool CanSetDefault => Installed && !IsDefault;
    public bool IsOffline =>
        (StatusFlags & (NativePrinting.PS_OFFLINE | NativePrinting.PS_NOT_AVAILABLE)) != 0;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? p = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
