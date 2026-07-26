using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PrinterTool;

public partial class MainWindow : Window
{
    private const string DefaultServer = @"\\CLSRV27";
    private const string BuildDate = "23.07.2026";
    /// <summary>Product name — identical in every language.</summary>
    internal const string AppName = "PrinterConnect";
    /// <summary>App version from the assembly — set once in the csproj, read everywhere.</summary>
    internal static string AppVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    private readonly List<PrinterRow> _rows = new();
    private readonly ObservableCollection<string> _servers = new();
    private readonly ListCollectionView _view;
    private readonly RowComparer _comparer = new();
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    /// <summary>Pulls fresh status from the server so the list is never stale.</summary>
    private readonly DispatcherTimer _statusPoll = new() { Interval = TimeSpan.FromSeconds(20) };
    private CancellationTokenSource? _snmpCts;
    private bool _loading;
    /// <summary>False until the constructor finished — control initializers fire change events early.</summary>
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();

        Width = App.Settings.WindowWidth;
        Height = App.Settings.WindowHeight;

        BuildLanguageMenu();

        ThemeBox.Items.Add(new IconItem { Glyph = "\uE713", Text = "", Key = "system" });
        ThemeBox.Items.Add(new IconItem { Glyph = "\uE706", Text = "", Key = "light" });
        ThemeBox.Items.Add(new IconItem { Glyph = "\uE708", Text = "", Key = "dark" });
        ThemeBox.SelectedIndex = App.Settings.Theme switch { "light" => 1, "dark" => 2, _ => 0 };

        // Saved servers, seeded with the company default on first run
        foreach (var srv in App.Settings.Servers.Distinct(StringComparer.OrdinalIgnoreCase))
            _servers.Add(srv);
        if (_servers.Count == 0) _servers.Add(DefaultServer);
        ServerList.ItemsSource = _servers;
        ServerBox.Text = string.IsNullOrWhiteSpace(App.Settings.Server) ? _servers[0] : App.Settings.Server;

        _view = new ListCollectionView(_rows);
        _view.Filter = FilterRow;
        _view.CustomSort = _comparer;
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PrinterRow.GroupLabel)));
        List.ItemsSource = _view;

        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); RefreshView(); };
        _statusPoll.Tick += async (_, _) => await RefreshStatusAsync();

        // A gripper drag means the person wants their own widths — stop auto-fitting
        AddHandler(System.Windows.Controls.Primitives.Thumb.DragDeltaEvent,
            new System.Windows.Controls.Primitives.DragDeltaEventHandler((_, args) =>
            {
                if (args.OriginalSource is System.Windows.Controls.Primitives.Thumb { Name: "PART_HeaderGripper" })
                    _userSizedColumns = true;
            }), handledEventsToo: true);

        BuildColumns();
        ApplyColumnAlignment(App.Settings.ColumnAlign, save: false);

        _ready = true;
        ApplyLanguage();
        // ContentRendered fires after the first frame is on screen, so the window
        // appears instantly and the server enumeration happens behind it.
        ContentRendered += async (_, _) => await LoadAllServersAsync();
        Closing += (_, _) =>
        {
            _statusPoll.Stop();
            if (Grid.Columns.Count > 0)
            {
                var order = Grid.Columns
                    .Select(col => _columns.FirstOrDefault(c => c.Column == col)?.Key)
                    .Where(k => k is not null).Cast<string>().ToList();
                if (order.Count > 0) { App.Settings.Columns = order; }
            }
            App.Settings.WindowWidth = Width;
            App.Settings.WindowHeight = Height;
            App.Settings.Server = ServerBox.Text.Trim();
        };
    }

    // ---------- localization ----------

    private static readonly string[] ThemeKeys = { "Th_System", "Th_Light", "Th_Dark" };

    /// <summary>Item for the icon-only dropdowns: glyph shown when closed, glyph + label when open.</summary>
    private sealed class IconItem
    {
        public required string Glyph { get; init; }
        public required string Text { get; set; }
        public required string Key { get; init; }
    }

    private void ApplyLanguage()
    {
        Title = AppName;
        TitleText.Text = AppName;
        ServerLabel.Text = Loc.T("Server");
        LoadButton.Content = Loc.T("Load");
        SearchHint.Text = Loc.T("Search");
        UpdateActivity();
        AboutButton.ToolTip = Loc.T("About");
        AddDeviceButton.Content = Loc.T("AddDevice");
        LoadButton.ToolTip = Loc.T("Refresh");
        ExportButton.ToolTip = Loc.T("ExportShort");
        ThemeBox.ToolTip = Loc.T("Theme");
        for (int i = 0; i < ThemeBox.Items.Count; i++)
            ((IconItem)ThemeBox.Items[i]).Text = Loc.T(ThemeKeys[i]);
        ThemeBox.Items.Refresh();
        AddServerButton.Content = Loc.T("AddServer");
        ThemeBox.ToolTip = Loc.T("Theme");
        LangButton.ToolTip = Loc.T("Language");
        foreach (var r in _rows) r.RefreshLanguage();
        foreach (var c in _columns) c.RefreshLabel();
        ColumnsTitle.Text = Loc.T("Columns");
        ColumnsButton.ToolTip = Loc.T("Columns");
        AlignLeftOption.Content = Loc.T("AlignLeft");
        AlignCenterOption.Content = Loc.T("AlignCenter");
        UpdateHeaders();
        RefreshView();   // re-evaluates localized group headers
    }

    private static System.Windows.Media.Imaging.BitmapImage Flag(string code) =>
        new(new Uri($"pack://application:,,,/flags/{code}.png"));

    /// <summary>Builds the flag menu: columns of five, so all languages are visible at once.</summary>
    private void BuildLanguageMenu()
    {
        LangList.Items.Clear();

        StackPanel? column = null;
        for (int i = 0; i < Loc.Languages.Length; i++)
        {
            if (i % 5 == 0)
            {
                column = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
                LangList.Items.Add(column);
            }
            var (code, native) = Loc.Languages[i];

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 18, Height = 18,
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Fill = new System.Windows.Media.ImageBrush(Flag(code)) { Stretch = System.Windows.Media.Stretch.UniformToFill },
                Stroke = (System.Windows.Media.Brush)Application.Current.Resources["Line"],
                StrokeThickness = 1,
            });
            content.Children.Add(new TextBlock
            {
                Text = native, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources[code == Loc.Lang ? "Accent" : "Ink"],
            });

            var item = new Button
            {
                Content = content, Tag = code, Cursor = Cursors.Hand,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0), Padding = new Thickness(10, 6, 12, 6),
                HorizontalContentAlignment = HorizontalAlignment.Left, MinWidth = 132,
            };
            item.Click += (_, _) =>
            {
                LangButton.IsChecked = false;
                SetLanguage((string)item.Tag);
            };
            column!.Children.Add(item);
        }
    }

    private void SetLanguage(string code)
    {
        Loc.Set(code);
        App.Settings.Language = code;
        App.Settings.Save();
        ApplyLanguage();
        BuildLanguageMenu();   // refresh flags menu highlighting + button flag
    }

    // ---------- theme ----------

    private bool EffectiveDark() => App.Settings.Theme switch
    {
        "light" => false,
        "dark" => true,
        _ => ThemeManager.IsDark(),
    };

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (ThemeBox.SelectedItem is not IconItem { Key: string t }) return;
        App.Settings.Theme = t;
        App.Settings.Save();
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        bool dark = EffectiveDark();
        ThemeManager.Apply(dark);
        ThemeManager.ApplyTitlebar(this, dark);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeManager.ApplyTitlebar(this, EffectiveDark());
        ThemeManager.HideCaptionIcon(this);
        if (PresentationSource.FromVisual(this) is HwndSource src)
            src.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SETTINGCHANGE = 0x001A;
        if (msg == WM_SETTINGCHANGE &&
            App.Settings.Theme == "system" &&                       // explicit choice wins
            Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
        {
            Dispatcher.BeginInvoke(ApplyTheme);
        }
        return IntPtr.Zero;
    }

    // ---------- server management ----------

    private void ServerBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = LoadAllServersAsync();
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e) => await LoadAllServersAsync();

    private async void ServerItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Content is not string server) return;
        ServerDrop.IsChecked = false;
        ServerBox.Text = server;
        await LoadAllServersAsync();
    }

    private void AddServer_Click(object sender, RoutedEventArgs e)
    {
        ServerDrop.IsChecked = false;
        ServerBox.Clear();
        ServerBox.Focus();
    }

    private void ServerDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string server) return;
        var res = MessageBox.Show(this,
            string.Format(Loc.T("ConfirmRemove"), server),
            AppName, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        _servers.Remove(server);
        App.Settings.Servers = _servers.ToList();

        // The box may still hold the deleted name — clear it, otherwise the reload
        // below would treat it as a new server and add it straight back.
        if (string.Equals(ServerBox.Text.Trim(), server, StringComparison.OrdinalIgnoreCase))
            ServerBox.Text = _servers.Count > 0 ? _servers[0] : "";
        if (string.Equals(App.Settings.Server, server, StringComparison.OrdinalIgnoreCase))
            App.Settings.Server = _servers.Count > 0 ? _servers[0] : "";
        App.Settings.Save();

        ServerDrop.IsChecked = false;
        if (_servers.Count == 0)
        {
            _rows.Clear();
            _view.Refresh();
            UpdateCount();
            return;
        }
        _ = LoadAllServersAsync();
    }

    private static string NormalizeServer(string raw)
    {
        string s = raw.Trim().TrimEnd('\\');
        if (s.Length == 0) return "";
        return s.StartsWith(@"\\", StringComparison.Ordinal) ? s : @"\\" + s;
    }

    // ---------- loading (all saved servers, aggregated) ----------

    private async Task LoadAllServersAsync()
    {
        if (_loading) return;
        _loading = true;

        // A server typed into the box joins the list once it loads successfully
        string typed = NormalizeServer(ServerBox.Text);
        if (typed.Length > 0) ServerBox.Text = typed;
        bool typedIsNew = typed.Length > 0 &&
                          !_servers.Contains(typed, StringComparer.OrdinalIgnoreCase);

        var targets = _servers.ToList();
        if (typedIsNew) targets.Add(typed);
        if (targets.Count == 0) { _loading = false; return; }

        _snmpCts?.Cancel();
        _snmpCts?.Dispose();
        _snmpCts = new CancellationTokenSource();
        var ct = _snmpCts.Token;

        UpdateActivity();          // non-blocking banner; the list stays interactive
        LoadButton.IsEnabled = false;

        var failed = new List<string>();
        try
        {
            // Enumerate every server in parallel off the UI thread
            var results = await Task.Run(() =>
            {
                var localConnections = NativePrinting.EnumerateLocalConnections();
                var localPrinters = NativePrinting.EnumerateLocalPrinters();
                string defaultPrinter = NativePrinting.GetDefault();
                var perServer = targets.AsParallel().WithDegreeOfParallelism(Math.Min(4, targets.Count))
                    .Select(srv =>
                    {
                        try
                        {
                            return (srv, printers: NativePrinting.EnumerateServerPrinters(srv),
                                    drivers: NativePrinting.EnumerateDriverVersions(srv), ok: true);
                        }
                        catch
                        {
                            return (srv, printers: new List<NativePrinting.ServerPrinter>(),
                                    drivers: new Dictionary<string, uint>(), ok: false);
                        }
                    })
                    .ToList();
                var localDrivers = NativePrinting.EnumerateDriverVersions(null);
                return (perServer, localConnections, localPrinters, defaultPrinter, localDrivers);
            });

            bool multi = results.perServer.Count(r => r.ok) > 1;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fresh = new List<PrinterRow>();

            foreach (var (srv, printers, drivers, ok) in results.perServer)
            {
                if (!ok) { failed.Add(srv); continue; }
                string shortName = srv.TrimStart('\\');
                foreach (var p in printers)
                {
                    // Remote EnumPrinters returns full UNC names (\\server\printer):
                    // show only the printer name, keep the UNC for connect calls.
                    bool isUnc = p.Name.StartsWith(@"\\", StringComparison.Ordinal);
                    string display = isUnc ? p.Name.Substring(p.Name.LastIndexOf('\\') + 1) : p.Name;
                    string unc = isUnc ? p.Name : srv + "\\" + p.Name;
                    if (!seen.Add(unc)) continue;

                    fresh.Add(new PrinterRow
                    {
                        Name = PrinterRow.Clean(display),
                        Unc = unc,
                        Location = PrinterRow.Clean(p.Location),
                        Comment = PrinterRow.Clean(p.Comment),
                        Driver = p.Driver ?? "",
                        Port = p.Port ?? "",
                        Ip = HostFromPort(p.Port ?? "") ?? "",
                        ServerShort = multi ? shortName : "",
                        StatusFlags = p.Status,
                        Jobs = p.Jobs,
                        Installed = results.localConnections.Contains(unc),
                        IsDefault = string.Equals(results.defaultPrinter, unc, StringComparison.OrdinalIgnoreCase),
                    });
                }
            }

            // Printers that live on this PC — Microsoft Print to PDF, OneNote, USB queues
            foreach (var p in results.localPrinters)
            {
                if (!seen.Add(p.Name)) continue;
                fresh.Add(new PrinterRow
                {
                    Name = PrinterRow.Clean(p.Name),
                    Unc = p.Name,
                    Location = PrinterRow.Clean(p.Location),
                    Comment = PrinterRow.Clean(p.Comment),
                    Driver = p.Driver ?? "",
                    DriverVersion = results.localDrivers.TryGetValue(p.Driver ?? "", out uint ldv) ? ldv : 0,
                    Port = p.Port ?? "",
                    ServerShort = Loc.T("ThisPC"),
                    StatusFlags = p.Status,
                    Jobs = p.Jobs,
                    Installed = true,
                    IsLocal = true,
                    IsDefault = string.Equals(results.defaultPrinter, p.Name, StringComparison.OrdinalIgnoreCase),
                });
            }

            fresh.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));


            _rows.Clear();
            _rows.AddRange(fresh);
            _view.Refresh();   // single refresh: filter, sort and grouping applied in one pass

            // Persist server list: keep known servers plus a successfully loaded new one
            if (typedIsNew && !failed.Contains(typed))
                _servers.Add(typed);
            App.Settings.Servers = _servers.ToList();
            App.Settings.Server = typed.Length > 0 ? typed : App.Settings.Server;
            App.Settings.Save();

            UpdateCount();
            _statusPoll.Start();
            _ = Dispatcher.InvokeAsync(async () => await RefreshStatusAsync(),
                                       DispatcherPriority.Background);
            _ = ProbeOfflineAsync(ct);   // live device check, background
            _ = RefreshTonerAsync(ct);   // best-effort, fire and forget

            if (failed.Count > 0)
                MessageBox.Show(this, string.Format(Loc.T("LoadFailed"), string.Join(", ", failed)),
                    AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            LoadButton.IsEnabled = true;
            _loading = false;
            UpdateActivity();
            SearchBox.Focus();
        }
    }

    /// <summary>
    /// Re-reads status, installed state and the default printer without rebuilding the
    /// list: rows update in place, so scroll position, selection and search all survive.
    /// </summary>
    private async Task RefreshStatusAsync()
    {
        if (_loading || _rows.Count == 0) return;

        var servers = _servers.ToList();
        var snapshot = _rows.ToArray();
        Dictionary<string, uint> status;
        HashSet<string> connections;
        string defaultPrinter;
        try
        {
            (status, connections, defaultPrinter) = await Task.Run(() =>
            {
                var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in NativePrinting.EnumerateLocalPrinters()) map[p.Name] = p.Status;
                foreach (var srv in servers)
                {
                    try
                    {
                        foreach (var p in NativePrinting.EnumerateServerPrinters(srv))
                        {
                            string unc = p.Name.StartsWith(@"\\", StringComparison.Ordinal)
                                ? p.Name : srv + "\\" + p.Name;
                            map[unc] = p.Status;
                        }
                    }
                    catch { /* a server being briefly unreachable must not disturb the UI */ }
                }
                return (map, NativePrinting.EnumerateLocalConnections(), NativePrinting.GetDefault());
            });
        }
        catch { return; }

        bool regroup = false;
        foreach (var row in snapshot)
        {
            if (status.TryGetValue(row.Unc, out uint flags)) row.StatusFlags = flags;
            bool installed = row.IsLocal || connections.Contains(row.Unc);
            if (row.Installed != installed && !row.Busy) { row.Installed = installed; regroup = true; }
            row.IsDefault = string.Equals(defaultPrinter, row.Unc, StringComparison.OrdinalIgnoreCase);
        }

        if (regroup || _comparer.Key == SortKey.Status) RefreshView();

        if (_snmpCts is { IsCancellationRequested: false } cts)
            _ = ProbeOfflineAsync(cts.Token);
    }

    /// <summary>
    /// Asks each offline-reported device directly whether it answers on the network.
    /// Runs in the background, 16 at a time, and never blocks the UI.
    /// </summary>
    private async Task ProbeOfflineAsync(CancellationToken ct)
    {
        var targets = _rows.Where(r => r.IsOffline && r.Ip.Length > 0).ToArray();
        if (targets.Length == 0) return;

        using var gate = new SemaphoreSlim(16);
        var tasks = targets.Select(async row =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var result = await NetProbe.CheckAsync(row.Ip, ct);
                if (!ct.IsCancellationRequested)
                    await Dispatcher.InvokeAsync(() => row.Probe = result);
            }
            catch { /* best-effort */ }
            finally { gate.Release(); }
        }).ToList();
        try { await Task.WhenAll(tasks); } catch { }
    }

    /// <summary>Best-effort toner query straight to each device via SNMP, max 12 in flight.</summary>
    /// <summary>Which SNMP data the currently visible columns actually require.</summary>
    private Snmp.Needs RequiredDeviceData()
    {
        var needs = Snmp.Needs.None;
        foreach (var c in _columns.Where(c => c.Visible))
        {
            needs |= c.Key switch
            {
                "toner" => Snmp.Needs.Supplies,
                "model" or "serial" => Snmp.Needs.Identity,
                "pages" or "uptime" => Snmp.Needs.Counters,
                "trays" => Snmp.Needs.Trays,
                "display" => Snmp.Needs.Display,
                _ => Snmp.Needs.None,
            };
        }
        return needs;
    }

    private async Task RefreshTonerAsync(CancellationToken ct)
    {
        var needs = RequiredDeviceData();
        if (needs == Snmp.Needs.None) return;   // nothing on screen needs the devices

        var rows = _rows.ToArray();   // snapshot — collection may change during the scan
        using var gate = new SemaphoreSlim(20);
        var tasks = rows.Select(async row =>
        {
            var hosts = HostCandidates(row);
            if (hosts.Count == 0) return;
            await gate.WaitAsync(ct);
            try
            {
                var info = await Snmp.QueryDeviceAsync(hosts, needs, ct);
                if (info is not null && !ct.IsCancellationRequested)
                    await Dispatcher.InvokeAsync(() => row.SetDevice(info));
            }
            catch { /* best-effort */ }
            finally { gate.Release(); }
        }).ToList();
        try { await Task.WhenAll(tasks); } catch { }

        if (!ct.IsCancellationRequested &&
            _comparer.Key is SortKey.Toner or SortKey.Pages or SortKey.Model
                           or SortKey.Serial or SortKey.Uptime or SortKey.Paper or SortKey.Display)
            await Dispatcher.InvokeAsync(RefreshView);
    }

    /// <summary>
    /// Candidate device addresses to try for SNMP, in order of likelihood:
    /// 1) IP/hostname embedded in the port name (standard TCP/IP ports),
    /// 2) the queue name itself — many orgs name the queue after the printer's DNS name.
    /// WSD/LPR/virtual queues expose no reachable address, so those get no toner data.
    /// </summary>
    private static List<string> HostCandidates(PrinterRow row)
    {
        var hosts = new List<string>(2);
        string? fromPort = HostFromPort(row.Port);
        if (fromPort is not null) hosts.Add(fromPort);
        if (Regex.IsMatch(row.Name, @"^[A-Za-z0-9][A-Za-z0-9\.\-_]{1,80}$") &&
            !hosts.Contains(row.Name, StringComparer.OrdinalIgnoreCase))
            hosts.Add(row.Name);
        return hosts;
    }

    private static string? HostFromPort(string port)
    {
        if (string.IsNullOrWhiteSpace(port)) return null;
        // "IP_10.1.2.3", "10.1.2.3", "printer01.corp.local"; WSD/LPR/virtual ports are unusable
        var m = Regex.Match(port, @"\b(\d{1,3}(?:\.\d{1,3}){3})\b");
        if (m.Success) return m.Value;
        if (port.StartsWith("WSD", StringComparison.OrdinalIgnoreCase)) return null;
        if (port.IndexOf(':') >= 0 || port.IndexOf('\\') >= 0 || port.IndexOf('/') >= 0) return null;
        return Regex.IsMatch(port, @"^[A-Za-z0-9][A-Za-z0-9\.\-_]{1,80}$") ? port : null;
    }

    // ---------- search ----------

    private string[] _terms = Array.Empty<string>();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchHint();
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void SearchBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateSearchHint();

    /// <summary>Hide the placeholder as soon as the field is focused, so the caret never sits on it.</summary>
    private void UpdateSearchHint() =>
        SearchHint.Visibility = SearchBox.Text.Length == 0 && !SearchBox.IsKeyboardFocused
            ? Visibility.Visible : Visibility.Collapsed;

    private bool FilterRow(object o)
    {
        if (_terms.Length == 0) return true;
        if (o is not PrinterRow row) return false;
        string blob = row.SearchBlob;
        foreach (var term in _terms)
            if (blob.IndexOf(term, StringComparison.Ordinal) < 0) return false;
        return true;
    }

    private void RefreshView()
    {
        if (!_ready) return;
        _terms = PrinterRow.Normalize(SearchBox.Text.Trim())
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        _view.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (!_ready) return;
        int total = _rows.Count;
        int visible = _view.Count;
        if (visible == total)
        {
            int installed = 0;
            foreach (var r in _rows) if (r.Installed) installed++;
            CountText.Text = string.Format(Loc.T("CountLoaded"), total, installed);
        }
        else
        {
            CountText.Text = string.Format(Loc.T("Filtered"), visible, total);
        }
    }

    // ---------- sorting ----------

    private enum SortKey { Name, Location, Ip, Status, Toner, Jobs, Pages, Model, Serial, Uptime, Paper, Display, Driver, DriverType }

    private sealed class RowComparer : IComparer
    {
        public SortKey Key = SortKey.Name;
        public bool Ascending = true;

        public int Compare(object? x, object? y)
        {
            var a = (PrinterRow)x!;
            var b = (PrinterRow)y!;

            // Installed section always pinned on top, regardless of user sort
            int c = b.Installed.CompareTo(a.Installed);
            if (c != 0) return c;

            c = Key switch
            {
                SortKey.Location => string.Compare(a.Location, b.Location, StringComparison.OrdinalIgnoreCase),
                SortKey.Ip => CompareIp(a.Ip, b.Ip),
                SortKey.Status => a.StatusRank.CompareTo(b.StatusRank),
                SortKey.Toner => a.TonerMin.CompareTo(b.TonerMin),
                SortKey.Jobs => b.Jobs.CompareTo(a.Jobs),          // busiest queue first
                SortKey.Pages => b.Pages.CompareTo(a.Pages),        // highest counter first
                SortKey.Model => string.Compare(a.Model, b.Model, StringComparison.OrdinalIgnoreCase),
                SortKey.Serial => string.Compare(a.Serial, b.Serial, StringComparison.OrdinalIgnoreCase),
                SortKey.Uptime => b.UptimeTicks.CompareTo(a.UptimeTicks),   // longest running first
                SortKey.Paper => a.TrayMin.CompareTo(b.TrayMin),            // emptiest tray first
                SortKey.Display => string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase),
                SortKey.Driver => string.Compare(a.Driver, b.Driver, StringComparison.OrdinalIgnoreCase),
                SortKey.DriverType => a.DriverTypeRank.CompareTo(b.DriverTypeRank),
                _ => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            };
            if (!Ascending) c = -c;
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Sorts IPv4 addresses numerically per octet; empty values sort last.</summary>
    private static int CompareIp(string a, string b)
    {
        if (a.Length == 0) return b.Length == 0 ? 0 : 1;
        if (b.Length == 0) return -1;
        var pa = a.Split('.');
        var pb = b.Split('.');
        if (pa.Length == 4 && pb.Length == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(pa[i], out int x) || !int.TryParse(pb[i], out int y)) break;
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        var header = e.OriginalSource as GridViewColumnHeader;
        if (header is null && e.OriginalSource is DependencyObject src)
        {
            for (var d = src; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
                if (d is GridViewColumnHeader h) { header = h; break; }
        }
        if (header is not { Column: { } col }) return;
        SortKey? key =
            col == ColPrinter ? SortKey.Name :
            col == ColLocation ? SortKey.Location :
            col == ColIp ? SortKey.Ip :
            col == ColStatus ? SortKey.Status :
            col == ColToner ? SortKey.Toner :
            col == ColJobs ? SortKey.Jobs :
            col == ColPages ? SortKey.Pages :
            col == ColModel ? SortKey.Model :
            col == ColSerial ? SortKey.Serial :
            col == ColUptime ? SortKey.Uptime :
            col == ColTrays ? SortKey.Paper :
            col == ColDisplay ? SortKey.Display :
            col == ColDriver ? SortKey.Driver :
            col == ColDriverType ? SortKey.DriverType : null;
        if (key is null) return;   // Installed column: section is always pinned

        if (_comparer.Key == key.Value) _comparer.Ascending = !_comparer.Ascending;
        else { _comparer.Key = key.Value; _comparer.Ascending = true; }

        UpdateHeaders();
        _view.Refresh();
    }

    private void UpdateHeaders()
    {
        string Arrow(SortKey k) =>
            _comparer.Key == k ? (_comparer.Ascending ? "  ▲" : "  ▼") : "";
        ColPrinter.Header = Loc.T("ColPrinter") + Arrow(SortKey.Name);
        ColLocation.Header = Loc.T("ColLocation") + Arrow(SortKey.Location);
        ColIp.Header = Loc.T("ColIp") + Arrow(SortKey.Ip);
        ColStatus.Header = Loc.T("ColStatus") + Arrow(SortKey.Status);
        ColToner.Header = Loc.T("ColToner") + Arrow(SortKey.Toner);
        ColJobs.Header = Loc.T("ColJobs") + Arrow(SortKey.Jobs);
        ColPages.Header = Loc.T("ColPages") + Arrow(SortKey.Pages);
        ColModel.Header = Loc.T("ColModel") + Arrow(SortKey.Model);
        ColSerial.Header = Loc.T("ColSerial") + Arrow(SortKey.Serial);
        ColUptime.Header = Loc.T("ColUptime") + Arrow(SortKey.Uptime);
        ColTrays.Header = Loc.T("ColTrays") + Arrow(SortKey.Paper);
        ColDisplay.Header = Loc.T("ColDisplay") + Arrow(SortKey.Display);
        ColDriver.Header = Loc.T("ColDriver") + Arrow(SortKey.Driver);
        ColDriverType.Header = Loc.T("DriverType") + Arrow(SortKey.DriverType);
        ColDefault.Header = Loc.T("Default");
        ColAction.Header = Loc.T("ColAction");
    }

    // ---------- columns: registry, chooser, persistence ----------

    /// <summary>One selectable column. Printer and Installed are always shown.</summary>
    public sealed class ColumnOption : INotifyPropertyChanged
    {
        public required string Key { get; init; }
        public required string LocKey { get; init; }
        public required GridViewColumn Column { get; init; }
        public required double Share { get; init; }
        public required double Min { get; init; }
        public bool CanHide { get; init; } = true;

        public string Label => Loc.T(LocKey);

        private bool _visible;
        public bool Visible
        {
            get => _visible;
            set { if (_visible == value) return; _visible = value; Notify(); }
        }

        public void RefreshLabel() => Notify(nameof(Label));

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? p = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    private List<ColumnOption> _columns = new();
    /// <summary>Set when the user drags a column gripper; auto-layout then stands down.</summary>
    private bool _userSizedColumns;
    private GridView Grid => (GridView)List.View;

    /// <summary>Columns shown when nothing has been configured yet — the rollout default.</summary>
    private static readonly string[] DefaultColumns =
        { "printer", "location", "ip", "status", "toner", "model", "serial", "pages", "default", "installed" };

    private void BuildColumns()
    {
        _columns = new List<ColumnOption>
        {
            // what and where
            new() { Key = "printer",  LocKey = "ColPrinter",  Column = ColPrinter,  Share = .24, Min = 190, CanHide = false },
            new() { Key = "location", LocKey = "ColLocation", Column = ColLocation, Share = .16, Min = 130 },
            new() { Key = "ip",       LocKey = "ColIp",       Column = ColIp,       Share = .11, Min = 100 },
            // current state
            new() { Key = "status",   LocKey = "ColStatus",   Column = ColStatus,   Share = .13, Min = 120 },
            new() { Key = "toner",    LocKey = "ColToner",    Column = ColToner,    Share = .08, Min = 80 },
            new() { Key = "trays",    LocKey = "ColTrays",    Column = ColTrays,    Share = .13, Min = 130 },
            new() { Key = "jobs",     LocKey = "ColJobs",     Column = ColJobs,     Share = .05, Min = 60 },
            // device identity and history
            new() { Key = "model",    LocKey = "ColModel",    Column = ColModel,    Share = .14, Min = 140 },
            new() { Key = "serial",   LocKey = "ColSerial",   Column = ColSerial,   Share = .11, Min = 110 },
            new() { Key = "pages",    LocKey = "ColPages",    Column = ColPages,    Share = .08, Min = 90 },
            new() { Key = "uptime",   LocKey = "ColUptime",   Column = ColUptime,   Share = .07, Min = 80 },
            new() { Key = "display",  LocKey = "ColDisplay",  Column = ColDisplay,  Share = .13, Min = 130 },
            new() { Key = "driver",   LocKey = "ColDriver",   Column = ColDriver,   Share = .14, Min = 140 },
            new() { Key = "drivertype",LocKey = "DriverType",  Column = ColDriverType, Share = .07, Min = 90 },
            // action
            new() { Key = "default",  LocKey = "Default",     Column = ColDefault,  Share = .05, Min = 70 },
            new() { Key = "installed",LocKey = "ColAction",   Column = ColAction,   Share = .11, Min = 120, CanHide = false },
        };

                var saved = App.Settings.Columns is { Count: > 0 } ? App.Settings.Columns.ToList() : DefaultColumns.ToList();

        // Users whose saved layout predates the Default column get it merged in once —
        // otherwise a feature they asked for stays invisible behind their old settings.
        if (App.Settings.ColumnsVersion < 1)
        {
            if (!saved.Contains("default", StringComparer.OrdinalIgnoreCase)) saved.Add("default");
            App.Settings.ColumnsVersion = 1;
            App.Settings.Columns = saved;
            App.Settings.Save();
        }

        foreach (var c in _columns)
            c.Visible = !c.CanHide || saved.Contains(c.Key, StringComparer.OrdinalIgnoreCase);

        // Saved list order = the user's column order; unknown/new columns keep registry position.
        // Hard rule: Printer is always first and Installed always last, so newly introduced
        // columns can never end up to the right of the action column.
        _columns = _columns
            .OrderBy(c => { int i = saved.FindIndex(k => string.Equals(k, c.Key, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; })
            .ToList();
        EnforceEdgeColumns();

        ColumnList.ItemsSource = _columns;
        ApplyColumns(save: false);
    }

    private void EnforceEdgeColumns()
    {
        var printer = _columns.First(c => c.Key == "printer");
        var installed = _columns.First(c => c.Key == "installed");
        _columns.Remove(printer);
        _columns.Remove(installed);
        _columns.Insert(0, printer);
        _columns.Add(installed);
    }

    private void Align_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ApplyColumnAlignment(ReferenceEquals(sender, AlignCenterOption) ? "center" : "left", save: true);
    }

    /// <summary>Applies the user's cell/header alignment via the shared CellAlign resource.</summary>
    private void ApplyColumnAlignment(string align, bool save)
    {
        bool center = string.Equals(align, "center", StringComparison.OrdinalIgnoreCase);
        Application.Current.Resources["CellAlign"] =
            center ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        AlignLeftOption.IsChecked = !center;
        AlignCenterOption.IsChecked = center;
        if (!save) return;
        App.Settings.ColumnAlign = center ? "center" : "left";
        App.Settings.Save();
    }

    private void ColumnToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ApplyColumns(save: true);

        // A newly shown SNMP column needs its data fetched now, not on the next load.
        if (_snmpCts is { IsCancellationRequested: false } cts && _rows.Count > 0)
            _ = RefreshTonerAsync(cts.Token);
    }

    /// <summary>Rebuilds the grid to match the chosen set, then persists it.</summary>
    private void ApplyColumns(bool save)
    {
        // If the user has dragged columns around, adopt that order before rebuilding
        if (Grid.Columns.Count > 0)
        {
            var current = Grid.Columns.ToList();
            _columns = _columns
                .OrderBy(c => { int i = current.IndexOf(c.Column); return i < 0 ? int.MaxValue : i; })
                .ToList();
            EnforceEdgeColumns();
        }

        Grid.Columns.Clear();
        foreach (var c in _columns.Where(c => c.Visible))
            Grid.Columns.Add(c.Column);

        LayoutColumns(List.ActualWidth);

        if (!save) return;
        App.Settings.Columns = _columns.Where(c => c.Visible).Select(c => c.Key).ToList();
        App.Settings.Save();
    }

    private void List_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_ready || e.NewSize.Width <= 0) return;
        LayoutColumns(e.NewSize.Width);
    }

    /// <summary>Distributes width across the visible columns, honouring each minimum.</summary>
    private void LayoutColumns(double totalWidth)
    {
        if (_userSizedColumns) return;   // respect manual widths
        var visible = _columns.Where(c => c.Visible).ToList();
        if (visible.Count == 0) return;

        double available = totalWidth - SystemParameters.VerticalScrollBarWidth - 10;
        if (available <= 0) return;

        double shareSum = visible.Sum(c => c.Share);
        double minSum = visible.Sum(c => c.Min);
        bool cramped = available < minSum;

        foreach (var c in visible)
            c.Column.Width = cramped ? c.Min : Math.Max(c.Min, available * (c.Share / shareSum));
    }

    // ---------- install / remove ----------

    private async void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => await InstallSelectedAsync();

    private void List_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src &&
            ItemsControl.ContainerFromElement(List, src) is ListViewItem item)
            item.IsSelected = true;
    }

    private async void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await InstallSelectedAsync();
    }

    /// <summary>Installs every selected, not-yet-installed printer concurrently — the UI never blocks.</summary>
    private async Task InstallSelectedAsync()
    {
        var targets = List.SelectedItems.OfType<PrinterRow>()
            .Where(r => r is { Installed: false, Busy: false })
            .ToList();
        if (targets.Count == 0) return;
        await Task.WhenAll(targets.Select(InstallAsync));
    }

    private async void RowAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PrinterRow row || row.Busy) return;

        if (row.Installed)
            await RemoveAsync(row);      // immediate; the printer can be reinstalled in one click
        else
            await InstallAsync(row);
    }

    /// <summary>The Load spinner covers server loading; per-row rings cover installs.</summary>
    private void UpdateActivity() =>
        LoadSpinner.Visibility = _loading ? Visibility.Visible : Visibility.Collapsed;

    private async Task InstallAsync(PrinterRow row)
    {
        row.Busy = true;
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            await Task.Run(() => NativePrinting.Connect(row.Unc, hwnd));
            row.Installed = true;
            string current = NativePrinting.GetDefault();
            foreach (var r in _rows)
                r.IsDefault = string.Equals(current, r.Unc, StringComparison.OrdinalIgnoreCase);
        }
        catch (Win32Exception ex)
        {
            ShowError(ex.NativeErrorCode, row.Unc);
        }
        finally
        {
            row.Busy = false;
            RefreshView();   // re-group: row moves into the pinned section
        }
    }

    private async Task RemoveAsync(PrinterRow row)
    {
        bool wasDefault = row.IsDefault;
        row.Busy = true;
        try
        {
            if (row.IsLocal)
            {
                await Task.Run(() => NativePrinting.DeleteLocal(row.Unc));
                _rows.Remove(row);          // the queue no longer exists at all
            }
            else
            {
                await Task.Run(() => NativePrinting.Disconnect(row.Unc));
                row.Installed = false;
                row.IsDefault = false;
            }
            await EnsureSensibleDefaultAsync(wasDefault);
        }
        catch (Win32Exception ex)
        {
            ShowError(ex.NativeErrorCode, row.Unc);
        }
        finally
        {
            row.Busy = false;
            RefreshView();
        }
    }

    // ---------- default printer ----------

    private void SetDefault_Click(object sender, RoutedEventArgs e)
    {
        var row = (sender as FrameworkElement)?.DataContext as PrinterRow
                  ?? List.SelectedItem as PrinterRow;
        if (row is null) return;
        if (!row.Installed)
        {
            MessageBox.Show(this, Loc.T("InstallFirst"), AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            NativePrinting.MakeDefault(row.Unc);
            foreach (var r in _rows) r.IsDefault = ReferenceEquals(r, row);
        }
        catch (Win32Exception ex)
        {
            ShowError(ex.NativeErrorCode, row.Unc);
        }
    }

    // ---------- add device (native Windows flow) ----------

    /// <summary>
    /// Opens Windows' own Add Printer wizard (search the network, add by IP/hostname,
    /// pick drivers). Deliberately not reimplemented: the native flow is what users
    /// know from Settings, supports every discovery protocol, and keeps this app free
    /// of multicast scanning that security tooling would flag.
    /// </summary>
    private void AddDevice_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Absolute path + fixed arguments: nothing user-controlled reaches the shell
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.IO.Path.Combine(Environment.SystemDirectory, "rundll32.exe"),
                Arguments = "printui.dll,PrintUIEntry /il",
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OpenIp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PrinterRow row) return;
        string ip = row.IpAddress;
        if (ip.Length == 0) return;

        // Failsafe scheme choice: if the device answers on 443 it runs HTTPS
        // (many force it and don't redirect from 80); otherwise plain HTTP.
        bool https = false;
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connect = client.ConnectAsync(ip, 443);
            https = await Task.WhenAny(connect, Task.Delay(500)) == connect && client.Connected;
        }
        catch { }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = (https ? "https://" : "http://") + ip,
                UseShellExecute = true,   // default browser
            });
        }
        catch { /* no browser association — nothing sensible to do */ }
    }

    /// <summary>
    /// Keeps the default printer sensible after removals: a sole remaining printer
    /// becomes the default, and when the default itself was removed the best
    /// remaining candidate takes over (reachable server printers first, then any
    /// server printer, then local ones like Print to PDF).
    /// </summary>
    private async Task EnsureSensibleDefaultAsync(bool removedWasDefault)
    {
        var installed = _rows.Where(r => r.Installed).ToList();
        if (installed.Count == 0) return;
        if (installed.Any(r => r.IsDefault) && !removedWasDefault) return;

        PrinterRow target;
        if (installed.Count == 1)
        {
            target = installed[0];
        }
        else if (removedWasDefault)
        {
            target = installed
                .OrderBy(r => r.IsLocal ? 2 : r.IsOffline ? 1 : 0)   // reachable server printers first
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .First();
        }
        else
        {
            return;
        }
        if (target.IsDefault) return;

        try
        {
            await Task.Run(() => NativePrinting.MakeDefault(target.Unc));
            foreach (var r in _rows) r.IsDefault = ReferenceEquals(r, target);
        }
        catch { /* Windows may refuse (e.g. managed default policy) — leave as-is */ }
    }

    // ---------- export ----------

    private void Export_Click(object sender, RoutedEventArgs e) => ExportXlsx();

    /// <summary>Exports every column (visible or not) for all loaded printers as .xlsx.</summary>
    internal void ExportXlsx()
    {
        if (_rows.Count == 0) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"PrinterConnect-{DateTime.Now:yyyy-MM-dd}.xlsx",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
        };
        if (dlg.ShowDialog(this) != true) return;

        var headers = new[]
        {
            Loc.T("ColPrinter"), Loc.T("ColLocation"), Loc.T("ColIp"), Loc.T("ColStatus"),
            Loc.T("ColToner"), Loc.T("ColTrays"), Loc.T("ColJobs"), Loc.T("ColModel"),
            Loc.T("ColSerial"), Loc.T("ColPages"), Loc.T("ColUptime"), Loc.T("ColDisplay"),
            Loc.T("ColDriver"), Loc.T("DriverType"), Loc.T("Server"), Loc.T("Default"), Loc.T("ColAction"),
        };
        var data = _rows
            .OrderBy(r => r.Installed ? 0 : 1).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => (IReadOnlyList<string>)new[]
            {
                r.Name, r.LocationDisplay, r.IpDisplay, r.StatusText,
                string.Join(" · ", r.Toner.Select(t => t.Tooltip)), r.Trays,
                r.JobsText, r.Model, r.Serial, r.PagesText, r.UptimeText,
                r.Display, r.Driver, r.DriverTypeText, r.IsLocal ? Loc.T("ThisPC") : r.ServerShort,
                r.IsDefault ? "✓" : "", r.Installed ? "✓" : "",
            });
        try
        {
            Xlsx.Write(dlg.FileName, headers, data);
            ShowExportDone(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Small Windows-11-style confirmation: seamless titlebar, Open + Close.</summary>
    private void ShowExportDone(string path)
    {
        var win = new Window
        {
            Owner = this,
            Title = AppName,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["Paper"],
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Ink"],
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 13,
        };
        win.SourceInitialized += (_, _) => ThemeManager.ApplyTitlebar(win, EffectiveDark());

        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("ExportDone"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });
        panel.Children.Add(new TextBlock
        {
            Text = System.IO.Path.GetFileName(path),
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["InkSoft"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var open = new Button
        {
            Content = Loc.T("Open"),
            Style = (Style)Application.Current.Resources["PrimaryButton"],
            MinWidth = 96, Height = 32, Margin = new Thickness(0, 0, 8, 0),
        };
        open.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            catch { }
            win.Close();
        };
        var close = new Button
        {
            Content = Loc.T("Close"),
            Style = (Style)Application.Current.Resources["ActionButton"],
            MinWidth = 96, Height = 32,
        };
        close.Click += (_, _) => win.Close();
        buttons.Children.Add(open);
        buttons.Children.Add(close);
        panel.Children.Add(buttons);

        win.Content = panel;
        win.ShowDialog();
    }

    // ---------- clear queue ----------

    private async void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        var row = (sender as FrameworkElement)?.DataContext as PrinterRow
                  ?? List.SelectedItem as PrinterRow;
        if (row is null) return;

        uint before = row.Jobs;
        try
        {
            int removed = await Task.Run(() => NativePrinting.ClearQueue(row.Unc));
            int shown = removed < 0 ? (int)before : removed;
            CountText.Text = string.Format(Loc.T("ClearedJobs"), shown, row.Name);
            _copyFlash.Stop();
            _copyFlash.Tick -= CopyFlashEnd;
            _copyFlash.Tick += CopyFlashEnd;
            _copyFlash.Start();
            _ = RefreshStatusAsync();   // pull the fresh jobs count promptly
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            ShowError(ex.NativeErrorCode, row.Unc);
        }
    }

    // ---------- test page ----------

    /// <summary>
    /// Prints Windows' standard test page via the spooler's own mechanism
    /// (printui /k) — same page as Settings, drivers fully respected.
    /// </summary>
    private void TestPage_Click(object sender, RoutedEventArgs e)
    {
        var row = (sender as FrameworkElement)?.DataContext as PrinterRow
                  ?? List.SelectedItem as PrinterRow;
        if (row is null) return;
        if (!row.Installed)
        {
            MessageBox.Show(this, Loc.T("InstallFirst"), AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (row.Unc.IndexOf('"') >= 0) return;   // not legal in printer names; guard anyway

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.IO.Path.Combine(Environment.SystemDirectory, "rundll32.exe"),
                Arguments = $"printui.dll,PrintUIEntry /k /n \"{row.Unc}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            CountText.Text = string.Format(Loc.T("TestSent"), row.Name);
            _copyFlash.Stop();
            _copyFlash.Tick -= CopyFlashEnd;
            _copyFlash.Tick += CopyFlashEnd;
            _copyFlash.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- copy to clipboard ----------

    private readonly DispatcherTimer _copyFlash = new() { Interval = TimeSpan.FromSeconds(1.6) };

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string value || value.Length == 0) return;
        try { Clipboard.SetText(value); } catch { return; }

        // Feedback where the click happened: the copy glyph flips to an accent
        // checkmark for a moment, plus the status-bar line.
        if (sender is Button btn && FindGlyph(btn) is { } glyph)
        {
            string original = glyph.Text;
            var originalBrush = glyph.Foreground;
            glyph.Text = "\uE73E";   // checkmark
            glyph.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Accent"];
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                glyph.Text = original;
                glyph.Foreground = originalBrush;
            };
            t.Start();
        }

        CountText.Text = string.Format(Loc.T("Copied"), value);
        _copyFlash.Stop();
        _copyFlash.Tick -= CopyFlashEnd;
        _copyFlash.Tick += CopyFlashEnd;
        _copyFlash.Start();
    }

    private static TextBlock? FindGlyph(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock tb) return tb;
            if (FindGlyph(child) is { } nested) return nested;
        }
        return null;
    }

    private void CopyFlashEnd(object? sender, EventArgs e)
    {
        _copyFlash.Stop();
        UpdateCount();
    }

    // ---------- about & errors ----------

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        int installed = 0;
        foreach (var r in _rows) if (r.Installed) installed++;
        AboutWindow.Show(this, AppName, AppVersion, BuildDate,
            _servers.Count > 0 ? string.Join(", ", _servers) : "—",
            _rows.Count, installed);
    }

    private void ShowError(int win32Error, string? context = null)
    {
        string friendly = string.Format(Loc.T(NativePrinting.ErrorKey(win32Error)), $"0x{win32Error:X8}");
        string sysMsg;
        try { sysMsg = new Win32Exception(win32Error).Message; } catch { sysMsg = ""; }
        string msg = friendly +
            $"\n\n— {Loc.T("Details")} —" +
            $"\nError 0x{win32Error:X8} ({win32Error})" +
            (sysMsg.Length > 0 ? $"\n{sysMsg}" : "") +
            (context is { Length: > 0 } ? $"\n{context}" : "");
        MessageBox.Show(this, msg, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
