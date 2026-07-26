using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PrinterTool;

/// <summary>
/// Thin wrapper over winspool.drv. Uses exactly the same calls Explorer's
/// "Connect" uses (AddPrinterConnection), so Point-and-Print policy checks
/// on the client still fully apply — the tool cannot and does not bypass them.
/// </summary>
public static class NativePrinting
{
    private const int PRINTER_ENUM_LOCAL = 0x00000002;
    private const int PRINTER_ENUM_NAME = 0x00000008;
    private const int PRINTER_ENUM_CONNECTIONS = 0x00000004;

    // PRINTER_INFO_2.Status flags — full set, values per the Windows SDK
    public const uint PS_PAUSED            = 0x00000001;
    public const uint PS_ERROR             = 0x00000002;
    public const uint PS_PENDING_DELETION  = 0x00000004;
    public const uint PS_PAPER_JAM         = 0x00000008;
    public const uint PS_PAPER_OUT         = 0x00000010;
    public const uint PS_MANUAL_FEED       = 0x00000020;
    public const uint PS_PAPER_PROBLEM     = 0x00000040;
    public const uint PS_OFFLINE           = 0x00000080;
    public const uint PS_IO_ACTIVE         = 0x00000100;
    public const uint PS_BUSY              = 0x00000200;
    public const uint PS_PRINTING          = 0x00000400;
    public const uint PS_OUTPUT_BIN_FULL   = 0x00000800;
    public const uint PS_NOT_AVAILABLE     = 0x00001000;
    public const uint PS_WAITING           = 0x00002000;
    public const uint PS_PROCESSING        = 0x00004000;
    public const uint PS_INITIALIZING      = 0x00008000;
    public const uint PS_WARMING_UP        = 0x00010000;
    public const uint PS_TONER_LOW         = 0x00020000;
    public const uint PS_NO_TONER          = 0x00040000;
    public const uint PS_PAGE_PUNT         = 0x00080000;
    public const uint PS_USER_INTERVENTION = 0x00100000;
    public const uint PS_OUT_OF_MEMORY     = 0x00200000;
    public const uint PS_DOOR_OPEN         = 0x00400000;
    public const uint PS_SERVER_UNKNOWN    = 0x00800000;
    public const uint PS_POWER_SAVE        = 0x01000000;

    /// <summary>Every active condition, most severe first, as localization keys.</summary>
    public static List<string> DescribeStatus(uint status)
    {
        var keys = new List<string>();
        void Add(uint flag, string key) { if ((status & flag) != 0) keys.Add(key); }

        // severe / blocking
        Add(PS_PAPER_JAM, "St_PaperJam");
        Add(PS_NO_TONER, "St_NoToner");
        Add(PS_DOOR_OPEN, "St_DoorOpen");
        Add(PS_PAPER_OUT, "St_PaperOut");
        Add(PS_OUTPUT_BIN_FULL, "St_BinFull");
        Add(PS_PAPER_PROBLEM, "St_PaperProblem");
        Add(PS_USER_INTERVENTION, "St_UserAction");
        Add(PS_OUT_OF_MEMORY, "St_OutOfMemory");
        Add(PS_ERROR, "St_Error");
        Add(PS_OFFLINE, "St_Offline");
        Add(PS_NOT_AVAILABLE, "St_NotAvailable");
        Add(PS_SERVER_UNKNOWN, "St_ServerUnknown");
        // warnings
        Add(PS_TONER_LOW, "St_TonerLow");
        Add(PS_PAUSED, "St_Paused");
        Add(PS_PENDING_DELETION, "St_PendingDeletion");
        Add(PS_MANUAL_FEED, "St_ManualFeed");
        Add(PS_PAGE_PUNT, "St_PagePunt");
        // activity / informational
        Add(PS_PRINTING, "St_Printing");
        Add(PS_PROCESSING, "St_Processing");
        Add(PS_WARMING_UP, "St_WarmingUp");
        Add(PS_INITIALIZING, "St_Initializing");
        Add(PS_BUSY, "St_Busy");
        Add(PS_WAITING, "St_Waiting");
        Add(PS_IO_ACTIVE, "St_IoActive");
        Add(PS_POWER_SAVE, "St_PowerSave");
        return keys;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTER_INFO_2
    {
        public string? pServerName;
        public string pPrinterName;
        public string? pShareName;
        public string? pPortName;
        public string? pDriverName;
        public string? pComment;
        public string? pLocation;
        public IntPtr pDevMode;
        public string? pSepFile;
        public string? pPrintProcessor;
        public string? pDatatype;
        public string? pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs;
        public uint AveragePPM;
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(int Flags, string? Name, uint Level,
        IntPtr pPrinterEnum, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersA", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern bool EnumPrintersAnsi(int Flags, string? Name, uint Level,
        IntPtr pPrinterEnum, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    /// <summary>
    /// Same layout as PRINTER_INFO_2 but with raw pointers, so the ANSI variant can be
    /// decoded with the system code page. Used only to repair mojibake (see RepairText).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PRINTER_INFO_2_RAW
    {
        public IntPtr pServerName, pPrinterName, pShareName, pPortName, pDriverName,
                      pComment, pLocation, pDevMode, pSepFile, pPrintProcessor,
                      pDatatype, pParameters, pSecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, cJobs, AveragePPM;
    }

    [DllImport("winspool.drv", EntryPoint = "AddPrinterConnectionW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AddPrinterConnection(string pName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTER_CONNECTION_INFO_1
    {
        public uint dwFlags;
        public string? pszDriverName;
    }

    [DllImport("winspool.drv", EntryPoint = "AddPrinterConnection2W", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AddPrinterConnection2(IntPtr hWnd, string pszName, uint dwLevel,
        ref PRINTER_CONNECTION_INFO_1 pConnectionInfo);

    [DllImport("winspool.drv", EntryPoint = "DeletePrinterConnectionW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeletePrinterConnection(string pName);

    [StructLayout(LayoutKind.Sequential)]
    private struct PRINTER_DEFAULTS
    {
        public IntPtr pDatatype;
        public IntPtr pDevMode;
        public uint DesiredAccess;
    }

    private const uint PRINTER_ALL_ACCESS = 0x000F000C;

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool DeletePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    /// <summary>
    /// Deletes a printer installed on this PC (Print to PDF, OneNote, USB queues).
    /// May require admin rights for machine-wide printers — the resulting access-denied
    /// error is surfaced to the user like any other.
    /// </summary>
    public static void DeleteLocal(string printerName)
    {
        var pd = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ALL_ACCESS };
        if (!OpenPrinter(printerName, out IntPtr h, ref pd))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!DeletePrinter(h))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            ClosePrinter(h);
        }
    }

    [DllImport("winspool.drv", EntryPoint = "SetDefaultPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDefaultPrinter(string pName);

    public sealed record ServerPrinter(
        string Name, string? Location, string? Comment,
        string? Driver, string? Port, uint Status, uint Jobs);

    /// <summary>Enumerate all shared printers on a print server (\\server).</summary>
    public static List<ServerPrinter> EnumerateServerPrinters(string server)
    {
        var list = Enumerate(PRINTER_ENUM_NAME, server)
            .Select(p => new ServerPrinter(
                p.pPrinterName, p.pLocation, p.pComment,
                p.pDriverName, p.pPortName, p.Status, p.cJobs))
            .ToList();

        // Some servers hold location/comment text that the wide API cannot decode and
        // returns as U+FFFD ("B<?>ro"). The ANSI view of the same record usually still
        // carries the original bytes, so read those and decode with the system code page.
        if (list.Any(p => HasBadChar(p.Location) || HasBadChar(p.Comment)))
        {
            var ansi = EnumerateAnsi(server);
            if (ansi.Count > 0)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (!HasBadChar(p.Location) && !HasBadChar(p.Comment)) continue;
                    if (!ansi.TryGetValue(p.Name, out var fix)) continue;
                    list[i] = p with
                    {
                        Location = Prefer(p.Location, fix.Location),
                        Comment = Prefer(p.Comment, fix.Comment),
                    };
                }
            }
        }
        return list;
    }

    private static bool HasBadChar(string? s) => s is not null && s.IndexOf('\uFFFD') >= 0;

    private static string? Prefer(string? wide, string? ansi) =>
        HasBadChar(wide) && !string.IsNullOrEmpty(ansi) && !HasBadChar(ansi) ? ansi : wide;

    /// <summary>Printer name → location/comment as decoded from the ANSI API.</summary>
    private static Dictionary<string, (string? Location, string? Comment)> EnumerateAnsi(string server)
    {
        var map = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
        EnumPrintersAnsi(PRINTER_ENUM_NAME, server, 2, IntPtr.Zero, 0, out uint needed, out _);
        if (needed == 0) return map;

        IntPtr buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrintersAnsi(PRINTER_ENUM_NAME, server, 2, buf, needed, out needed, out uint count))
                return map;

            int size = Marshal.SizeOf<PRINTER_INFO_2_RAW>();
            for (int i = 0; i < count; i++)
            {
                var raw = Marshal.PtrToStructure<PRINTER_INFO_2_RAW>(buf + i * size);
                string? name = Marshal.PtrToStringAnsi(raw.pPrinterName);
                if (string.IsNullOrEmpty(name)) continue;
                map[name!] = (Marshal.PtrToStringAnsi(raw.pLocation),
                              Marshal.PtrToStringAnsi(raw.pComment));
            }
        }
        catch { /* repair is best-effort */ }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return map;
    }

    /// <summary>
    /// Printers installed directly on this PC — virtual ones like Microsoft Print to PDF
    /// and OneNote, plus USB/locally-created queues. Server connections are excluded:
    /// they come from the server enumeration and would otherwise appear twice.
    /// </summary>
    public static List<ServerPrinter> EnumerateLocalPrinters()
    {
        return Enumerate(PRINTER_ENUM_LOCAL, null)
            .Where(p => !p.pPrinterName.StartsWith(@"\\", StringComparison.Ordinal))
            .Select(p => new ServerPrinter(
                p.pPrinterName, p.pLocation, p.pComment,
                p.pDriverName, p.pPortName, p.Status, p.cJobs))
            .ToList();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DRIVER_INFO_2_RAW
    {
        public uint cVersion;
        public IntPtr pName, pEnvironment, pDriverPath, pDataFile, pConfigFile;
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrinterDriversW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinterDrivers(string? pName, string? pEnvironment, uint Level,
        IntPtr pDriverInfo, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    /// <summary>
    /// Driver name → spooler driver version for a server (null = this PC).
    /// Version 4 = modern Type 4 (installs without admin prompts, WPP-ready);
    /// version 3 = classic Type 3 (may trigger credential prompts, KB5005652).
    /// </summary>
    public static Dictionary<string, uint> EnumerateDriverVersions(string? server)
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        EnumPrinterDrivers(server, null, 2, IntPtr.Zero, 0, out uint needed, out _);
        if (needed == 0) return map;

        IntPtr buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrinterDrivers(server, null, 2, buf, needed, out needed, out uint count))
                return map;
            int size = Marshal.SizeOf<DRIVER_INFO_2_RAW>();
            for (int i = 0; i < count; i++)
            {
                var d = Marshal.PtrToStructure<DRIVER_INFO_2_RAW>(buf + i * size);
                string? name = Marshal.PtrToStringUni(d.pName);
                if (!string.IsNullOrEmpty(name)) map[name!] = d.cVersion;
            }
        }
        catch { /* best-effort — the column just stays empty */ }
        finally { Marshal.FreeHGlobal(buf); }
        return map;
    }

    /// <summary>Full UNC names (\\server\printer) of connections installed for the current user.</summary>
    public static HashSet<string> EnumerateLocalConnections()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Enumerate(PRINTER_ENUM_CONNECTIONS, null))
            set.Add(p.pPrinterName);
        return set;
    }

    private static IEnumerable<PRINTER_INFO_2> Enumerate(int flags, string? name)
    {
        EnumPrinters(flags, name, 2, IntPtr.Zero, 0, out uint needed, out _);
        if (needed == 0)
        {
            int err = Marshal.GetLastWin32Error();
            const int ERROR_INSUFFICIENT_BUFFER = 122;
            if (err != 0 && err != ERROR_INSUFFICIENT_BUFFER)
                throw new Win32Exception(err);
            yield break;
        }

        IntPtr buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrinters(flags, name, 2, buf, needed, out needed, out uint count))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            int size = Marshal.SizeOf<PRINTER_INFO_2>();
            for (int i = 0; i < count; i++)
                yield return Marshal.PtrToStructure<PRINTER_INFO_2>(buf + i * size);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>
    /// Connect using AddPrinterConnection2 (Vista+): unlike the legacy call it
    /// downloads and installs the driver from the server when needed, showing
    /// Windows' own driver-install progress owned by our window.
    /// </summary>
    public static void Connect(string uncName, IntPtr ownerHwnd)
    {
        var info = new PRINTER_CONNECTION_INFO_1 { dwFlags = 0, pszDriverName = null };
        try
        {
            if (AddPrinterConnection2(ownerHwnd, uncName, 1, ref info)) return;
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch (EntryPointNotFoundException)
        {
            if (!AddPrinterConnection(uncName))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public static void Disconnect(string uncName)
    {
        if (!DeletePrinterConnection(uncName))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDefaultPrinter(System.Text.StringBuilder? pszBuffer, ref int pcchBuffer);

    /// <summary>
    /// The user's current default printer. Windows may report a network queue either as
    /// "\\server\printer" or as "printer on server"; both are normalised to the UNC form.
    /// </summary>
    public static string GetDefault()
    {
        int size = 0;
        GetDefaultPrinter(null, ref size);
        if (size <= 0) return "";
        var sb = new System.Text.StringBuilder(size);
        if (!GetDefaultPrinter(sb, ref size)) return "";

        string name = sb.ToString().Trim();
        if (name.StartsWith(@"\\", StringComparison.Ordinal)) return name;

        int on = name.LastIndexOf(" on ", StringComparison.OrdinalIgnoreCase);
        if (on > 0)
        {
            string printer = name.Substring(0, on);
            string server = name.Substring(on + 4).Trim();
            if (server.Length > 0)
                return @"\\" + server.TrimStart('\\') + "\\" + printer;
        }
        return name;
    }

    private const int PRINTER_CONTROL_PURGE = 3;
    private const int JOB_CONTROL_DELETE = 5;
    private const uint PRINTER_ACCESS_USE = 0x00000008;

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool SetPrinter(IntPtr hPrinter, uint Level, IntPtr pPrinter, uint Command);

    [DllImport("winspool.drv", EntryPoint = "EnumJobsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumJobs(IntPtr hPrinter, uint FirstJob, uint NoJobs, uint Level,
        IntPtr pJob, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    [DllImport("winspool.drv", EntryPoint = "SetJobW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetJob(IntPtr hPrinter, uint JobId, uint Level, IntPtr pJob, uint Command);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOB_INFO_1_RAW
    {
        public uint JobId;
        public IntPtr pPrinterName, pMachineName, pUserName, pDocument, pDatatype, pStatus;
        public uint Status, Priority, Position, TotalPages, PagesPrinted;
        public SYSTEMTIME Submitted;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    /// <summary>
    /// Clears a print queue. With manage rights the whole queue is purged in one call;
    /// otherwise the current user's own jobs are deleted individually — which standard
    /// users are always permitted to do.
    /// </summary>
    /// <returns>Jobs removed (-1 = full purge, exact count unknown).</returns>
    public static int ClearQueue(string printerName)
    {
        // Tier 1: full purge (needs Manage Documents on the queue)
        var pdAdmin = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ALL_ACCESS };
        if (OpenPrinter(printerName, out IntPtr hAdmin, ref pdAdmin))
        {
            try
            {
                if (SetPrinter(hAdmin, 0, IntPtr.Zero, PRINTER_CONTROL_PURGE)) return -1;
            }
            finally { ClosePrinter(hAdmin); }
        }

        // Tier 2: delete only this user's jobs
        var pdUse = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ACCESS_USE };
        if (!OpenPrinter(printerName, out IntPtr h, ref pdUse))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            EnumJobs(h, 0, 512, 1, IntPtr.Zero, 0, out uint needed, out _);
            if (needed == 0) return 0;
            IntPtr buf = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!EnumJobs(h, 0, 512, 1, buf, needed, out needed, out uint count)) return 0;
                int size = Marshal.SizeOf<JOB_INFO_1_RAW>();
                string me = Environment.UserName;
                int removed = 0;
                for (int i = 0; i < count; i++)
                {
                    var job = Marshal.PtrToStructure<JOB_INFO_1_RAW>(buf + i * size);
                    string owner = Marshal.PtrToStringUni(job.pUserName) ?? "";
                    if (!string.Equals(owner, me, StringComparison.OrdinalIgnoreCase)) continue;
                    if (SetJob(h, job.JobId, 0, IntPtr.Zero, JOB_CONTROL_DELETE)) removed++;
                }
                return removed;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        finally { ClosePrinter(h); }
    }

    public static void MakeDefault(string uncName)
    {
        if (!SetDefaultPrinter(uncName))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>Translate the Win32 errors users actually hit into a localization key.</summary>
    public static string ErrorKey(int win32Error) => win32Error switch
    {
        0x011B => "Err_Rpc",          // 0x0000011b RPC auth level mismatch / firewall
        0x0BCB => "Err_Driver",       // driver not found on server for client arch
        0x0709 => "Err_NotFound",     // printer name invalid / server unreachable
        5      => "Err_AccessDenied", // Point-and-Print policy blocks non-admin install
        0x07D1 => "Err_Driver",       // unknown driver
        1722   => "Err_ServerDown",   // RPC server unavailable
        53     => "Err_ServerDown",   // network path not found
        _      => "Err_Generic"
    };
}
