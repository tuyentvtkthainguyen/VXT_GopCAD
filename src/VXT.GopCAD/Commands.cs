using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using DS = VXT.GopCAD.DanhSo;

[assembly: CommandClass(typeof(VXT.GopCAD.Commands))]
[assembly: CommandClass(typeof(VXT.GopCAD.DanhSoCommands))]
[assembly: ExtensionApplication(typeof(VXT.GopCAD.PluginApp))]

namespace VXT.GopCAD
{
    public class Commands
    {
        /// <summary>Mở hộp thoại gộp nhiều file DWG thành 1 file.</summary>
        [CommandMethod("VXT", "VXTGOP", CommandFlags.Session)]
        public void Gop()
        {
            string path = null;
            bool danhSo = false;
            using (var f = new GopForm())
            {
                if (AcApp.ShowModalDialog(f) == System.Windows.Forms.DialogResult.OK && f.OpenAfter)
                {
                    path = f.ResultPath;
                    danhSo = f.DanhSoAfter;
                }
            }
            if (path == null || !File.Exists(path)) return;
            try
            {
                var nd = AcApp.DocumentManager.Open(path, false);
                if (danhSo && nd != null)
                {
                    AcApp.DocumentManager.MdiActiveDocument = nd;
                    nd.SendStringToExecute("VXTDANHSO ", true, false, true);
                }
            }
            catch (System.Exception ex)
            {
                AcApp.ShowAlertDialog("Không mở được file gộp:\n" + ex.Message);
            }
        }
    }

    public class DanhSoCommands
    {
        /// <summary>Đánh số bản vẽ: khung (block/polyline) hoặc layout; điền số, tên; xuất danh mục.</summary>
        [CommandMethod("VXT", "VXTDANHSO", CommandFlags.Modal)]
        public void RunDanhSo()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;
            DS.DanhSoForm f;
            try { f = new DS.DanhSoForm(db, ed); }
            catch (System.Exception ex) { ed.WriteMessage("\nVXTDANHSO: " + ex.Message); return; }
            using (f)
                if (AcApp.ShowModalDialog(f) != System.Windows.Forms.DialogResult.OK) return;

            var s = f.S;
            var items = f.Items;
            string msg;
            try
            {
                using (doc.LockDocument())
                {
                    msg = DS.DanhSoEngine.Apply(db, items, s);
                    if (s.Catalog) msg += Catalog(doc, items, s);
                }
            }
            catch (System.Exception ex)
            {
                AcApp.ShowAlertDialog("Lỗi khi đánh số bản vẽ:\n" + ex.Message);
                return;
            }
            ed.Regen();
            ed.WriteMessage("\n" + msg.Replace("\n", "\n  ") + "\nGõ U để hoàn tác.");
            AcApp.ShowAlertDialog(msg + "\n\nGõ U để hoàn tác nếu cần.");
        }

        private static string Catalog(Document doc, System.Collections.Generic.List<DS.SheetItem> items, DS.DanhSoSettings s)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            string res = "";
            var first = items.FirstOrDefault(i => i.Use);
            if (s.CatalogTable && first != null)
            {
                string key = DS.DanhSoEngine.CatalogKey(s);
                if (DS.DanhSoEngine.FindCatalog(db, key, out var pos, out var spaceId, out var oldId))
                {
                    DS.DanhSoEngine.MakeTable(db, spaceId, pos, items, Ht(spaceId), s, oldId);
                    res += "\nĐã cập nhật bảng danh mục bản vẽ (tại chỗ cũ).";
                }
                else
                {
                    string dest = s.LayoutMode ? s.CatalogSpace : LayoutManager.Current.CurrentLayout;
                    try
                    {
                        if (!LayoutManager.Current.CurrentLayout.Equals(dest, StringComparison.OrdinalIgnoreCase))
                            LayoutManager.Current.CurrentLayout = dest;
                        if (!dest.Equals("Model", StringComparison.OrdinalIgnoreCase)) ed.SwitchToPaperSpace();
                    }
                    catch { }
                    var po = new PromptPointOptions("\nChọn góc trên-trái để đặt BẢNG DANH MỤC BẢN VẼ (Enter = bỏ qua): ") { AllowNone = true };
                    var pr = ed.GetPoint(po);
                    if (pr.Status == PromptStatus.OK)
                    {
                        var p = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                        DS.DanhSoEngine.MakeTable(db, db.CurrentSpaceId, p, items, Ht(db.CurrentSpaceId), s, ObjectId.Null);
                        res += "\nĐã tạo bảng danh mục " + items.Count(i => i.Use) + " bản vẽ.";
                    }
                    else res += "\nBỏ qua bảng danh mục.";
                }

                double Ht(ObjectId sp)
                {
                    if (s.TextHeight > 0) return s.TextHeight;
                    if (!s.LayoutMode) return 0.012 * first.H;
                    return sp == SymbolUtilityServices.GetBlockModelSpaceId(db) ? 250 : 3.5;
                }
            }
            if (s.CatalogCsv)
            {
                string dir = Path.GetDirectoryName(db.Filename ?? "");
                string nm = Path.GetFileNameWithoutExtension(doc.Name) + "_DanhMucBanVe.csv";
                using (var d = new System.Windows.Forms.SaveFileDialog
                {
                    Filter = "CSV (Excel)|*.csv", FileName = nm,
                    InitialDirectory = Directory.Exists(dir) ? dir : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                })
                {
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        try { DS.DanhSoEngine.WriteCsv(d.FileName, items); res += "\nĐã xuất danh mục: " + d.FileName; }
                        catch (System.Exception ex) { res += "\nKhông ghi được file CSV: " + ex.Message; }
                    }
                }
            }
            return res;
        }
    }

    /// <summary>Nạp khi AutoCAD khởi động: thêm nút vào Ribbon tab "VXT".</summary>
    public class PluginApp : IExtensionApplication
    {
        public void Initialize()
        {
            AcApp.Idle += OnIdle;
        }

        public void Terminate() { }

        private void OnIdle(object sender, EventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            AcApp.Idle -= OnIdle;
            try { RibbonVXT.Add(); } catch { }
            // Đổi workspace thì Ribbon dựng lại -> thêm lại nút
            AcApp.SystemVariableChanged += (s, a) =>
            {
                if (a.Name.Equals("WSCURRENT", StringComparison.OrdinalIgnoreCase))
                    try { RibbonVXT.Add(); } catch { }
            };
        }
    }

    internal static class RibbonVXT
    {
        private const string TabId = "VXT_TAB";
        private const string PanelId = "VXT_GOPCAD_PANEL";

        public static void Add()
        {
            var rc = ComponentManager.Ribbon;
            if (rc == null) return;

            var tab = rc.Tabs.FirstOrDefault(t => t.Id == TabId);
            if (tab == null)
            {
                tab = new RibbonTab { Title = "VXT", Id = TabId };
                rc.Tabs.Add(tab);
            }
            if (tab.Panels.Any(p => p.Source != null && p.Source.Id == PanelId)) return;

            var src = new RibbonPanelSource { Title = "Gộp CAD", Id = PanelId };
            src.Items.Add(new RibbonButton
            {
                Id = "VXT_BTN_GOP",
                Text = "Gộp\nfile DWG",
                ShowText = true,
                ShowImage = false,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                ToolTip = "Gộp nhiều file DWG thành 1 file (lệnh VXTGOP)",
                CommandParameter = "\u0003\u0003VXTGOP ",
                CommandHandler = new RibbonCmd()
            });
            src.Items.Add(new RibbonButton
            {
                Id = "VXT_BTN_DANHSO",
                Text = "Đánh số\nbản vẽ",
                ShowText = true,
                ShowImage = false,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                ToolTip = "Đánh số, điền tên bản vẽ cho khung/layout và xuất danh mục bản vẽ (lệnh VXTDANHSO)",
                CommandParameter = "\u0003\u0003VXTDANHSO ",
                CommandHandler = new RibbonCmd()
            });
            tab.Panels.Add(new RibbonPanel { Source = src });
        }
    }

    internal class RibbonCmd : System.Windows.Input.ICommand
    {
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (parameter is RibbonButton b && b.CommandParameter is string cmd && doc != null)
                doc.SendStringToExecute(cmd, true, false, true);
        }
    }
}
