using System.IO;
using System.IO.Compression;
using System.Text;

namespace PrinterTool;

/// <summary>
/// Writes a real .xlsx (not CSV renamed) with zero dependencies: an xlsx file is a
/// zip of small XML parts, and inline strings keep the worksheet part trivial.
/// </summary>
public static class Xlsx
{
    /// <param name="numericColumns">
    /// Column indexes written as real numbers. Everything else stays text: guessing from the
    /// value turned serials like "00123456" into 123456 and long ids into 1.23E+17.
    /// </param>
    public static void Write(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows,
        IReadOnlyCollection<int>? numericColumns = null)
    {
        var numeric = new HashSet<int>(numericColumns ?? Array.Empty<int>());
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        Add(zip, "[Content_Types].xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
</Types>
""");
        Add(zip, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
        Add(zip, "xl/workbook.xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets><sheet name="Printers" sheetId="1" r:id="rId1"/></sheets>
</workbook>
""");
        Add(zip, "xl/_rels/workbook.xml.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
""");
        // one bold style for the header row
        Add(zip, "xl/styles.xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <fonts count="2"><font/><font><b/></font></fonts>
  <fills count="1"><fill/></fills>
  <borders count="1"><border/></borders>
  <cellStyleXfs count="1"><xf/></cellStyleXfs>
  <cellXfs count="2"><xf/><xf fontId="1" applyFont="1"/></cellXfs>
</styleSheet>
""");

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        void Row(IReadOnlyList<string> cells, bool bold)
        {
            sb.Append("<row>");
            for (int col = 0; col < cells.Count; col++)
            {
                string cell = cells[col] ?? "";
                // Only declared numeric columns, parsed culture-invariantly
                if (!bold && numeric.Contains(col) &&
                    long.TryParse(cell, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out long n))
                {
                    sb.Append("<c t=\"n\"").Append(bold ? " s=\"1\"" : "").Append("><v>")
                      .Append(n).Append("</v></c>");
                }
                else
                {
                    sb.Append("<c t=\"inlineStr\"").Append(bold ? " s=\"1\"" : "").Append("><is><t xml:space=\"preserve\">")
                      .Append(System.Security.SecurityElement.Escape(XmlSafe(cell)))
                      .Append("</t></is></c>");
                }
            }
            sb.Append("</row>");
        }

        Row(headers, bold: true);
        foreach (var row in rows) Row(row, bold: false);

        sb.Append("</sheetData></worksheet>");
        Add(zip, "xl/worksheets/sheet1.xml", sb.ToString());
    }

    /// <summary>Removes characters XML 1.0 forbids (control bytes from SNMP strings, lone surrogates).</summary>
    private static string XmlSafe(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (char.IsHighSurrogate(ch) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                sb.Append(ch).Append(s[++i]);    // valid pair (emoji, rare CJK) is legal XML
                continue;
            }
            bool ok = ch == '\t' || ch == '\n' || ch == '\r'
                      || (ch >= 0x20 && ch <= 0xD7FF)
                      || (ch >= 0xE000 && ch <= 0xFFFD);
            if (ok) sb.Append(ch);
        }
        return sb.ToString();
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content.TrimStart());
    }
}
