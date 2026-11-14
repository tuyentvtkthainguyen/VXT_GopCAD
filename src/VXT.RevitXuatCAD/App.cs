using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WF = System.Windows.Forms;

namespace VXT.RevitXuatCAD
{
    /// <summary>Thêm nút "Xuất sheet ra 1 file CAD" vào tab VXT của Revit.</summary>
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication a)
        {
            const string tab = "VXT";
            try { a.CreateRibbonTab(tab); } catch { /* tab đã có (do add-in VXT khác tạo) */ }
            var panel = a.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "Xuất CAD") ?? a.CreateRibbonPanel(tab, "Xuất CAD");
            var data = new PushButtonData("VXT_XuatSheetCAD", "Xuất sheet\nra 1 file CAD",
                                          Assembly.GetExecutingAssembly().Location, typeof(CmdXuatSheet).FullName)
            {
                ToolTip = "Chọn các sheet và xuất thành 1 file DWG duy nhất (mỗi sheet 1 Layout hoặc xếp ở Model).",
                LongDescription = "Revit xuất từng sheet ra DWG, sau đó AutoCAD Core Console chạy ngầm để gộp. Cần AutoCAD 2024 để gộp."
            };
            panel.AddItem(data);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication a) => Result.Succeeded;
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdXuatSheet : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var uidoc = cd.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show("VXT", "Hãy mở một dự án Revit trước.");
                return Result.Cancelled;
            }
            var doc = uidoc.Document;
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show("VXT", "Lệnh này dùng trong file dự án (.rvt), không dùng trong file family.");
                return Result.Cancelled;
            }

            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && !s.IsTemplate).ToList();
            if (sheets.Count == 0)
            {
                TaskDialog.Show("VXT", "Dự án chưa có sheet nào.");
                return Result.Cancelled;
            }

            // Sheet đang chọn trong Project Browser thì tick sẵn
            var pre = new HashSet<ElementId>(uidoc.Selection.GetElementIds());
            var rows = sheets
                .Select(s => new SheetRow { Id = s.Id, So = s.SheetNumber, Ten = s.Name, Chon = pre.Contains(s.Id) })
                .OrderBy(r => r.So, new NaturalComparer())
                .ToList();

            var setups = new FilteredElementCollector(doc).OfClass(typeof(ExportDWGSettings))
                .Cast<ExportDWGSettings>().ToList();

            var settings = XuatSettings.Load();
            var owner = new Owner(cd.Application.MainWindowHandle);
            XuatJob job;
            using (var f = new XuatSheetForm(rows, setups.Select(x => x.Name).OrderBy(x => x).ToList(), settings, doc.Title))
            {
                if (f.ShowDialog(owner) != WF.DialogResult.OK || f.Job == null) return Result.Cancelled;
                job = f.Job;
            }
            settings.Save();

            try
            {
                new SheetExporter(doc, setups, owner).Run(job);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("VXT - Lỗi", ex.Message);
                return Result.Failed;
            }
        }
    }

    internal class Owner : WF.IWin32Window
    {
        public Owner(IntPtr h) { Handle = h; }
        public IntPtr Handle { get; }
    }
}
