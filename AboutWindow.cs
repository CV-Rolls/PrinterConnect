using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PrinterTool;

/// <summary>
/// About + release notes. Notes are curated: major, user-visible changes only,
/// the way mainstream apps write them — not a commit log.
/// </summary>
public static class AboutWindow
{
    private static readonly (string Version, string Note)[] Notes =
    {
        ("3.9",  "Attribution simplified: ClearVantage.io is named as the developer throughout"),
        ("3.8",  "Clear print queue: one click removes stuck jobs (whole queue with manage rights, your own jobs otherwise)"),
        ("3.7",  "Column alignment choice (left / center) · Windows 11 Fluent buttons: neutral secondaries and quiet-destructive Remove"),
        ("3.6",  "Smart default printer: a sole remaining printer becomes default, and removing the default promotes the best remaining one"),
        ("3.5",  "Driver type column (Type 3 / Type 4) — see at a glance which queues cause admin prompts and which are Protected-Print-ready"),
        ("3.4",  "Export button with completion dialog · Excel export file fixed · single brand header · refined test-page button"),
        ("3.3",  "Excel export of the full printer list · smarter IP links (https detection) · copy buttons for IP and serial"),
        ("3.2",  "Test page button on every installed printer"),
        ("3.1",  "Movable and resizable columns · printer web pages open from the IP column · flag language menu (follows the PC language) · Windows 11 dark palette"),
        ("2.19", "Auto-load on server selection · quieter Explorer-style column headers"),
        ("2.18", "Seamless Windows 11 titlebar · uniform row heights · 6 new languages (14 total) · free software by ClearVantage.io"),
        ("2.16", "Add device via the native Windows flow · full code and security review"),
        ("2.14", "New app icon · default printer selection · local printers (Print to PDF, OneNote) listed and removable"),
        ("2.13", "Much faster device queries (SNMPv2c) · every column sortable"),
        ("2.11", "Device columns: model, serial, pages, paper trays, display · column chooser with saved layout"),
        ("2.9",  "Windows 11 look: accent buttons and progress ring · live reachability check for offline printers"),
        ("2.7",  "Instant start · list refreshes itself in the background"),
        ("2.4",  "Light/dark/system theme · IP column · multi-server support"),
        ("2.3",  "Install several printers at once · clear error messages with exact codes"),
        ("2.0",  "Complete modern rebuild: search, live status, toner levels, 8 languages"),
    };

    public static void Show(Window owner, string appName, string version, string buildDate,
        string servers, int printers, int installed)
    {
        var win = new Window
        {
            Owner = owner,
            Title = appName,
            Width = 540,
            Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)Application.Current.Resources["Paper"],
            Foreground = (Brush)Application.Current.Resources["Ink"],
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 13,
        };
        // Handle exists only after SourceInitialized — earlier calls silently did nothing,
        // which is why this window's titlebar didn't match the main window.
        win.SourceInitialized += (_, _) => ThemeManager.ApplyTitlebar(win, ThemeManager.IsDark());

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // header block
        var head = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        title.Children.Add(new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/logo.png")),
            Width = 32, Height = 32, Margin = new Thickness(0, 0, 10, 0),
        });
        var tt = new StackPanel();
        tt.Children.Add(new TextBlock { Text = appName, FontSize = 18, FontWeight = FontWeights.SemiBold });
        tt.Children.Add(new TextBlock
        {
            Text = $"v{version} · {buildDate}",
            Foreground = (Brush)Application.Current.Resources["InkSoft"],
        });
        title.Children.Add(tt);
        head.Children.Add(title);

        var info = new StringBuilder()
            .AppendLine($".NET Framework 4.8 · {Environment.OSVersion}")
            .AppendLine($"{Loc.T("Server")}: {servers}")
            .AppendLine($"{Loc.T("ColPrinter")}: {printers} · {Loc.T("ColAction")}: {installed}")
            .Append($"User: {Environment.UserName} @ {Environment.MachineName}");
        // "Free software by ClearVantage.io" with a real link
        var credit = new TextBlock { Margin = new Thickness(0, 0, 0, 2) };
        credit.Inlines.Add(new System.Windows.Documents.Run("Free software by ")
        {
            Foreground = (Brush)Application.Current.Resources["InkSoft"],
        });
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("ClearVantage.io"))
        {
            NavigateUri = new Uri("https://clearvantage.io"),
            Foreground = (Brush)Application.Current.Resources["Accent"],
            TextDecorations = null,
        };
        link.RequestNavigate += (_, args) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = args.Uri.AbsoluteUri,
                    UseShellExecute = true,
                });
            }
            catch { }
        };
        credit.Inlines.Add(link);
        head.Children.Add(credit);

        head.Children.Add(new TextBlock
        {
            Text = info.ToString(),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["InkSoft"],
            Margin = new Thickness(0, 0, 0, 14),
        });
        head.Children.Add(new TextBlock
        {
            Text = Loc.T("Signing"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        head.Children.Add(new TextBlock
        {
            Text = Loc.T("SigningText"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["InkSoft"],
            Margin = new Thickness(0, 0, 0, 14),
        });
        head.Children.Add(new TextBlock
        {
            Text = Loc.T("ReleaseNotes"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });
        Grid.SetRow(head, 0);
        root.Children.Add(head);

        // scrollable release notes
        var list = new StackPanel();
        foreach (var (v, note) in Notes)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = new Border
            {
                Background = (Brush)Application.Current.Resources["AccentSoft"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = v, FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["Accent"],
                },
            };
            row.Children.Add(badge);
            var body = new TextBlock
            {
                Text = note,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["InkSoft"],
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(body, 1);
            row.Children.Add(body);
            list.Children.Add(row);
        }
        var scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        win.Content = root;
        win.ShowDialog();
    }
}
