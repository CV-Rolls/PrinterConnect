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

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                using var fs = File.OpenRead(Path);
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
        catch { /* corrupt settings — start fresh */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            using var ms = new MemoryStream();
            new DataContractJsonSerializer(typeof(Settings)).WriteObject(ms, this);
            File.WriteAllBytes(Path, ms.ToArray());
        }
        catch { /* non-fatal */ }
    }
}
