using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WF = System.Windows.Forms;

namespace VXT.RevitXuatCAD
{
    /// <summary>
    /// Xuất từng sheet ra DWG (MergedViews = true, không xref), rồi gọi AutoCAD Core Console
    /// chạy lệnh VXTGOPJOB của lõi VXT.GopCAD.Core để gộp thành 1 file.
    /// </summary>
    public class SheetExporter
    {
        // Thư mục làm việc chỉ có ký tự ASCII, không dấu cách -> script AutoCAD không bị lỗi đường dẫn.
        private static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VXT", "XuatCAD");

        private readonly Document _doc;
        private readonly List<ExportDWGSettings> _setups;
        private readonly WF.IWin32Window _owner;

        public SheetExporter(Document doc, List<ExportDWGSettings> setups, WF.IWin32Window owner)
        {
            _doc = doc;
            _setups = setups;
            _owner = owner;
        }

        public void Run(XuatJob job)
        {
            string work = Path.Combine(Root, "temp", DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(work);

            var opts = GetOptions(job.SetupName);
            opts.MergedViews = true;
            // Giữ đúng tỷ lệ: 1 đơn vị CAD = 1 mm thật, bản vẽ ở Model luôn 1:1
            opts.TargetUnit = ExportUnit.Millimeter;

            bool gop = job.Kieu != KieuKetQua.ChiFileLe;
            int n = job.Sheets.Count;
            var exported = new List<(SheetRow row, string file)>();
            var errors = new List<string>();
            var sw = Stopwatch.StartNew();
            bool merged = false, stopped = false;
            string mergeMsg = "", finalPath = null, leDir = null;

            var pf = new ProgressForm();
            pf.Show(_owner);
            try
            {
                for (int i = 0; i < n; i++)
                {
                    if (pf.Cancel) { stopped = true; break; }
                    var r = job.Sheets[i];
                    pf.Set(i, n + (gop ? 1 : 0), $"Đang xuất sheet {i + 1}/{n}: {r.So} - {r.Ten}");
                    string name = "S" + (i + 1).ToString("000");
                    try
                    {
                        bool ok = _doc.Export(work, name, new List<ElementId> { r.Id }, opts);
                        string f = FindExported(work, name);
                        if (ok && f != null) exported.Add((r, f));
                        else errors.Add($"{r.So} - {r.Ten}: Revit không tạo được file DWG.");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{r.So} - {r.Ten}: {ex.Message}");
                    }
                }

                if (exported.Count == 0)
                    throw new InvalidOperationException("Không xuất được sheet nào.\n" + string.Join("\n", errors.Take(10)));

                string outDir = Path.GetDirectoryName(job.OutputPath);
                Directory.CreateDirectory(outDir);

                if (gop && !stopped)
                {
                    pf.Set(n, n + 1, "Đang gộp " + exported.Count + " sheet thành 1 file DWG (AutoCAD Core Console chạy ngầm)...");
                    merged = MergeWithAccore(job, exported, work, pf, out mergeMsg, out finalPath);
                }

                if (!gop || !merged || job.KeepSingles || stopped)
                {
                    leDir = gop
                        ? Path.Combine(outDir, Path.GetFileNameWithoutExtension(job.OutputPath) + "_DWG_le")
                        : outDir;
                    Directory.CreateDirectory(leDir);
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (row, file) in exported)
                    {
                        string bn = Txt.TenFile(row.So + " - " + row.Ten), fn = bn;
                        int k = 2;
                        while (!used.Add(fn)) fn = bn + " (" + (k++) + ")";
                        try { File.Copy(file, Path.Combine(leDir, fn + ".dwg"), true); }
                        catch (Exception ex) { errors.Add(fn + ".dwg: " + ex.Message); }
                    }
                }
            }
            finally
            {
                pf.Close();
                pf.Dispose();
                try { Directory.Delete(work, true); } catch { }
            }

            // ===== Báo kết quả =====
            var sb = new StringBuilder();
            sb.AppendLine($"Đã xuất {exported.Count}/{n} sheet trong {sw.Elapsed.TotalSeconds:0} giây.");
            if (stopped) sb.AppendLine("Đã dừng giữa chừng theo yêu cầu.");
            if (merged) sb.AppendLine("File gộp: " + finalPath);
            else if (gop && !stopped) sb.AppendLine("CHƯA GỘP ĐƯỢC: " + mergeMsg);
            if (leDir != null) sb.AppendLine("File DWG lẻ: " + leDir);
            if (merged && !string.IsNullOrEmpty(mergeMsg)) sb.AppendLine().AppendLine(mergeMsg);
            if (errors.Count > 0) sb.AppendLine().AppendLine("Lỗi:").AppendLine(string.Join("\n", errors.Take(15)));

            var td = new TaskDialog("VXT - Xuất sheet ra CAD")
            {
                MainInstruction = merged ? "Đã xuất và gộp xong" : "Đã xuất các file DWG lẻ",
                MainContent = sb.ToString(),
                CommonButtons = TaskDialogCommonButtons.Close
            };
            string show = merged ? finalPath : leDir;
            if (show != null) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Mở thư mục chứa file");
            var res = td.Show();
            if (show != null && (res == TaskDialogResult.CommandLink1 || job.OpenFolder))
                OpenInExplorer(show);
        }

        private DWGExportOptions GetOptions(string setupName)
        {
            if (!string.IsNullOrEmpty(setupName))
            {
                var s = _setups.FirstOrDefault(x => x.Name == setupName);
                if (s != null) return s.GetDWGExportOptions();
            }
            return new DWGExportOptions();
        }

        private static string FindExported(string dir, string name)
        {
            string exact = Path.Combine(dir, name + ".dwg");
            if (File.Exists(exact)) return exact;
            // Phòng khi Revit tự thêm hậu tố vào tên file
            return Directory.GetFiles(dir, name + "*.dwg")
                .FirstOrDefault(f =>
                {
                    string fn = Path.GetFileNameWithoutExtension(f);
                    return fn.Length > name.Length && !char.IsDigit(fn[name.Length]);
                });
        }

        private bool MergeWithAccore(XuatJob job, List<(SheetRow row, string file)> ex, string work,
                                     ProgressForm pf, out string msg, out string finalPath)
        {
            finalPath = null;
            if (!File.Exists(job.AccorePath)) { msg = "không tìm thấy accoreconsole.exe."; return false; }

            string addinDir = Path.GetDirectoryName(typeof(SheetExporter).Assembly.Location);
            string coreSrc = Path.Combine(addinDir, "AutoCAD", "VXT.GopCAD.Core.dll");
            if (!File.Exists(coreSrc)) { msg = "thiếu file " + coreSrc; return false; }
            string coreDll = Path.Combine(work, "VXT.GopCAD.Core.dll");
            File.Copy(coreSrc, coreDll, true);

            // Mở 1 bản sao làm "bản vẽ chủ" cho Core Console (không đụng tới file nguồn)
            string hostDwg = Path.Combine(work, "host.dwg");
            File.Copy(ex[0].file, hostDwg, true);
            string tmpOut = Path.Combine(work, "KETQUA.dwg");

            var jb = new StringBuilder();
            jb.AppendLine("# VXT XuatCAD job");
            jb.AppendLine("OUTPUT=" + tmpOut);
            jb.AppendLine("PLACEMENT=ROW");
            jb.AppendLine("COLUMNS=" + job.Columns);
            jb.AppendLine("GAP=0");
            jb.AppendLine("LAYOUTS=" + (job.Kieu == KieuKetQua.MoiSheetMotLayout ? "FIRST" : "NONE"));
            jb.AppendLine("LABEL=" + (job.Label ? "1" : "0"));
            jb.AppendLine("ASBLOCK=0");
            jb.AppendLine("LAYERPREFIX=0");
            jb.AppendLine("MANGLE=0");
            jb.AppendLine("SHEETTOMODEL=" + (job.Kieu == KieuKetQua.ModelKhungTenTheoTyLe ? "1" : "0"));
            foreach (var (row, file) in ex)
                jb.AppendLine("FILE=" + file + "|" + Txt.ChoJob(Txt.TheoMau(job.Pattern, row)) + "|" + Txt.ChoJob(row.So + " - " + row.Ten));
            string jobPath = Path.Combine(work, "job.txt");
            File.WriteAllText(jobPath, jb.ToString(), new UTF8Encoding(false));

            // Script: tạm tắt SECURELOAD để NETLOAD được, xong thì trả lại giá trị cũ.
            string scr = Path.Combine(work, "gop.scr");
            File.WriteAllText(scr, string.Join("\r\n", new[]
            {
                "(setq vxt_sl (getvar \"SECURELOAD\"))",
                "(setvar \"SECURELOAD\" 0)",
                "(setvar \"FILEDIA\" 0)",
                "NETLOAD",
                coreDll,
                "VXTGOPJOB",
                jobPath,
                "(setvar \"SECURELOAD\" vxt_sl)",
                "_.QUIT",
                "_N",
                ""
            }), Encoding.ASCII);

            var psi = new ProcessStartInfo(job.AccorePath)
            {
                Arguments = $"/i \"{hostDwg}\" /s \"{scr}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = work
            };

            var timeout = TimeSpan.FromMinutes(Math.Min(60, 5 + ex.Count));
            var sw = Stopwatch.StartNew();
            using (var p = Process.Start(psi))
            {
                if (p == null) { msg = "không khởi động được AutoCAD Core Console."; return false; }
                p.OutputDataReceived += (s, e) => { };
                p.ErrorDataReceived += (s, e) => { };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                while (!p.WaitForExit(300))
                {
                    pf.Pump();
                    if (pf.Cancel || sw.Elapsed > timeout)
                    {
                        try { p.Kill(true); } catch { }
                        msg = pf.Cancel ? "đã dừng theo yêu cầu." : "AutoCAD Core Console chạy quá lâu, đã dừng.";
                        return false;
                    }
                }
            }

            string log = jobPath + ".log";
            string[] lines = File.Exists(log) ? File.ReadAllLines(log, Encoding.UTF8) : Array.Empty<string>();
            if (lines.Length > 0 && lines[0].StartsWith("FATAL"))
            {
                msg = lines[0].Substring(Math.Min(6, lines[0].Length));
                return false;
            }
            if (lines.Length == 0 || !File.Exists(tmpOut))
            {
                msg = "Core Console không chạy được lệnh gộp. Kiểm tra: đúng AutoCAD 2024, " +
                      "file AutoCAD\\VXT.GopCAD.Core.dll build cho đúng phiên bản AutoCAD.";
                return false;
            }

            var notes = lines.Skip(1).Select(l => l.Replace("ERR\t", "Lỗi: ").Replace("WARN\t", "Lưu ý: ")).Take(15).ToList();
            msg = string.Join("\n", notes);

            // Chép ra vị trí người dùng chọn; nếu file đang mở trong AutoCAD thì lưu tên khác.
            finalPath = job.OutputPath;
            try { File.Copy(tmpOut, finalPath, true); }
            catch
            {
                finalPath = Path.Combine(Path.GetDirectoryName(job.OutputPath),
                    Path.GetFileNameWithoutExtension(job.OutputPath) + "_" + DateTime.Now.ToString("HHmmss") + ".dwg");
                File.Copy(tmpOut, finalPath, true);
                msg = ("File cũ đang mở nên đã lưu tên mới.\n" + msg).Trim();
            }
            return true;
        }

        private static void OpenInExplorer(string path)
        {
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }
    }
}
