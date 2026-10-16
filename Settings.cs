using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PrinterTool;

[DataContract]
public sealed class Settings
{
    [DataMember] public string Server { get; set; } = "";
    [DataMember] public List<string> Servers { get; set; } = new();
    [DataMember] public string? Language { get; set; }
    [DataMember] public string Theme { get; set; } = "system";
    [DataMember] public List<string> Columns { get; set; } = new();
    [DataMember] public int ColumnsVersion { get; set; }
    [DataMember] public string ColumnAlign { get; set; } = "left";
    [DataMember] public double WindowWidth { get; set; } = 980;
    [DataMember] public double WindowHeight { get; set; } = 680;

    private static string Path =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrinterConnect", "settings.json");

    private static string BackupPath => Path + ".bak";

    public static Settings Load()
    {
        // A second instance may be mid-save: retry briefly, then fall back to the backup
        // before starting from defaults — defaults would overwrite the real settings.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { return TryLoad(Path) ?? TryLoad(BackupPath) ?? new Settings(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                System.Threading.Thread.Sleep(100);   // mid-swap by another instance
            }
        }
        return TryLoad(BackupPath) ?? new Settings();
    }

    private static Settings? TryLoad(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var ser = new DataContractJsonSerializer(typeof(Settings));
                if (ser.ReadObject(fs) is Settings s)
                {
                    s.Servers ??= new List<string>();
                    s.Server ??= "";
                    s.Theme ??= "system";
                    s.Columns ??= new List<string>();
                    s.ColumnAlign ??= "left";
                    return s;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw; }   // locked: retry
        catch { /* corrupt — try the backup */ }
        return null;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            using var ms = new MemoryStream();
            new DataContractJsonSerializer(typeof(Settings)).WriteObject(ms, this);
            // Write-then-swap: a crash or full disk mid-write can never leave an empty
            // settings file behind; the previous good copy is kept as settings.json.bak.
            string tmp = Path + ".tmp";
            File.WriteAllBytes(tmp, ms.ToArray());
            if (File.Exists(Path)) File.Replace(tmp, Path, BackupPath, ignoreMetadataErrors: true);
            else File.Move(tmp, Path);
        }
        catch { /* non-fatal */ }
    }
}
