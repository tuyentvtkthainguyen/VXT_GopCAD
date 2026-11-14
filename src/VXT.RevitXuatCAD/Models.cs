using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace VXT.RevitXuatCAD
{
    public class SheetRow
    {
        public ElementId Id;
        public string So;
        public string Ten;
        public bool Chon;
    }

    public enum KieuKetQua { MoiSheetMotLayout = 0, ChiModel = 1, ChiFileLe = 2 }

    public class XuatJob
    {
        public List<SheetRow> Sheets = new List<SheetRow>();
        public string SetupName;
        public KieuKetQua Kieu;
        public int Columns;
        public string Pattern;
        public bool Label;
        public bool KeepSingles;
        public bool OpenFolder;
        public string OutputPath;
        public string AccorePath;
    }

    /// <summary>Thiết lập lưu cục bộ trong %AppData%\VXT\XuatCAD\settings.json (không gửi đi đâu).</summary>
    public class XuatSettings
    {
        public string SetupName { get; set; }
        public int Kieu { get; set; } = 0;
        public int Columns { get; set; } = 5;
        public string Pattern { get; set; } = "{SoHieu} - {Ten}";
        public bool Label { get; set; } = false;
        public bool KeepSingles { get; set; } = false;
        public bool OpenFolder { get; set; } = true;
        public string LastFolder { get; set; }
        public string AccorePath { get; set; }

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VXT", "XuatCAD", "settings.json");

        public static XuatSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<XuatSettings>(File.ReadAllText(FilePath, Encoding.UTF8)) ?? new XuatSettings();
            }
            catch { }
            return new XuatSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>Tìm accoreconsole.exe, ưu tiên AutoCAD 2024 (bản lõi gộp được build cho 2024).</summary>
        public static string DetectAccore()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var v in new[] { "2024", "2023", "2022", "2021", "2025", "2026", "2027" })
            {
                foreach (var prod in new[] { "AutoCAD " + v, "AutoCAD LT " + v })
                {
                    string p = Path.Combine(pf, "Autodesk", prod, "accoreconsole.exe");
                    if (File.Exists(p)) return p;
                }
            }
            return "";
        }
    }

    /// <summary>So sánh tự nhiên: "KC-2" đứng trước "KC-10".</summary>
    public class NaturalComparer : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            a ??= ""; b ??= "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                    int c = string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }

    internal static class Txt
    {
        /// <summary>Bỏ dấu tiếng Việt để tìm kiếm không phân biệt dấu.</summary>
        public static string BoDau(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var n = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(n.Length);
            foreach (char c in n)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c == 'đ' ? 'd' : c == 'Đ' ? 'D' : c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        /// <summary>Tên file hợp lệ của Windows.</summary>
        public static string TenFile(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            string r = sb.ToString().Trim().TrimEnd('.');
            if (r.Length == 0) r = "Sheet";
            return r.Length > 150 ? r.Substring(0, 150) : r;
        }

        /// <summary>Bỏ ký tự phân tách của file job.</summary>
        public static string ChoJob(string s) => (s ?? "").Replace('|', '_').Replace('\r', ' ').Replace('\n', ' ').Trim();

        public static string TheoMau(string pattern, SheetRow r) =>
            (string.IsNullOrWhiteSpace(pattern) ? "{SoHieu} - {Ten}" : pattern)
                .Replace("{SoHieu}", r.So ?? "").Replace("{Ten}", r.Ten ?? "");
    }
}
