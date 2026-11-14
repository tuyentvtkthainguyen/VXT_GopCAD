using System.IO;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(VXT.GopCAD.Core.CoreCommands))]

namespace VXT.GopCAD.Core
{
    /// <summary>
    /// Lệnh chạy được trong AutoCAD Core Console (không giao diện).
    /// Dùng trong script: VXTGOPJOB ↵ C:\...\job.txt ↵
    /// </summary>
    public class CoreCommands
    {
        [CommandMethod("VXT", "VXTGOPJOB", CommandFlags.Modal)]
        public void GopJob()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var pso = new PromptStringOptions("\nĐường dẫn file job: ") { AllowSpaces = true };
            var pr = ed.GetString(pso);
            if (pr.Status != PromptStatus.OK) return;

            string path = pr.StringResult.Trim().Trim('"');
            string log = path + ".log";
            try
            {
                var opt = JobFile.Read(path);
                var eng = new MergeEngine();
                eng.Progress += (i, n, s) => ed.WriteMessage("\n[{0}/{1}] {2}", i + 1 > n ? n : i + 1, n, s);
                var res = eng.Run(opt);
                JobFile.WriteLog(log, res, opt.Items.Count);
                ed.WriteMessage("\nVXT: đã gộp {0}/{1} file -> {2}", res.Ok, opt.Items.Count, res.OutputPath);
            }
            catch (System.Exception ex)
            {
                JobFile.WriteFatal(log, ex.Message);
                ed.WriteMessage("\nVXT lỗi: " + ex.Message);
            }
        }
    }
}
