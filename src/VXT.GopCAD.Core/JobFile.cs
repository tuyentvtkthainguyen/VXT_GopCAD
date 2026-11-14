using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace VXT.GopCAD.Core
{
    /// <summary>
    /// File "job" dạng văn bản UTF-8 để chương trình khác (add-in Revit) giao việc gộp
    /// cho AutoCAD Core Console. Mỗi dòng KEY=VALUE:
    ///   OUTPUT=C:\...\KetQua.dwg
    ///   PLACEMENT=ROW | KEEP
    ///   COLUMNS=5
    ///   GAP=0
    ///   LAYOUTS=NONE | FIRST | ALL
    ///   ASBLOCK=0|1   LAYERPREFIX=0|1   LABEL=0|1   MANGLE=0|1
    ///   FILE=đường_dẫn.dwg|Tên layout|Chữ ghi dưới bản vẽ
    /// </summary>
    public static class JobFile
    {
        public static MergeOptions Read(string path)
        {
            var o = new MergeOptions();
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim().ToUpperInvariant();
                string v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "OUTPUT": o.OutputPath = v; break;
                    case "PLACEMENT":
                        o.Placement = v.Equals("KEEP", StringComparison.OrdinalIgnoreCase)
                            ? PlacementMode.GiuToaDo : PlacementMode.XepHang;
                        break;
                    case "COLUMNS":
                        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int c)) o.Columns = Math.Max(0, c);
                        break;
                    case "GAP":
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double g)) o.Gap = Math.Max(0, g);
                        break;
                    case "LAYOUTS":
                        switch (v.ToUpperInvariant())
                        {
                            case "NONE": o.Layouts = LayoutImportMode.KhongLay; break;
                            case "ALL": o.Layouts = LayoutImportMode.TatCa; break;
                            default: o.Layouts = LayoutImportMode.LayoutDau; break;
                        }
                        break;
                    case "ASBLOCK": o.AsBlock = IsTrue(v); break;
                    case "LAYERPREFIX": o.LayerPrefix = IsTrue(v); break;
                    case "LABEL": o.Label = IsTrue(v); break;
                    case "MANGLE": o.MangleDuplicates = IsTrue(v); break;
                    case "FILE":
                        var parts = v.Split('|');
                        o.Items.Add(new MergeItem
                        {
                            FilePath = parts[0].Trim(),
                            LayoutName = parts.Length > 1 ? parts[1].Trim() : null,
                            Label = parts.Length > 2 ? parts[2].Trim() : null
                        });
                        break;
                }
            }
            if (string.IsNullOrWhiteSpace(o.OutputPath)) throw new InvalidDataException("File job thiếu dòng OUTPUT=.");
            return o;
        }

        private static bool IsTrue(string v) => v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);

        /// <summary>Ghi kết quả: dòng đầu "OK\tsố_file_ok\ttổng\tgiây" hoặc "FATAL\tthông_báo".</summary>
        public static void WriteLog(string logPath, MergeResult r, int total)
        {
            var sb = new StringBuilder();
            sb.AppendLine("OK\t" + r.Ok + "\t" + total + "\t" +
                          r.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture));
            foreach (var e in r.Errors) sb.AppendLine("ERR\t" + e.Replace("\r", " ").Replace("\n", " "));
            foreach (var w in r.Warnings) sb.AppendLine("WARN\t" + w.Replace("\r", " ").Replace("\n", " "));
            File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(false));
        }

        public static void WriteFatal(string logPath, string msg)
        {
            try { File.WriteAllText(logPath, "FATAL\t" + msg.Replace("\r", " ").Replace("\n", " "), new UTF8Encoding(false)); }
            catch { }
        }
    }
}
