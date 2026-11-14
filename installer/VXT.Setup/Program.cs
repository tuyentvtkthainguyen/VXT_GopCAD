using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace VXT.Setup
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    /// <summary>Cài/gỡ VXT Gộp CAD. Chỉ chép file vào thư mục AppData của người dùng, không gửi dữ liệu đi đâu.</summary>
    internal class SetupForm : Form
    {
        private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static readonly string AcadBundle = Path.Combine(AppData, @"Autodesk\ApplicationPlugins\VXT_GopCAD.bundle");
        private static readonly string RevitAddins = Path.Combine(AppData, @"Autodesk\Revit\Addins\2025");

        private readonly CheckBox ckAcad = new CheckBox();
        private readonly CheckBox ckRevit = new CheckBox();
        private readonly TextBox tbLog = new TextBox();

        public SetupForm()
        {
            Text = "Cài đặt VXT Gộp CAD 1.0.0";
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 360);

            Controls.Add(new Label
            {
                Text = "VXT Gộp CAD",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                Bounds = new Rectangle(16, 12, 480, 30)
            });
            Controls.Add(new Label
            {
                Text = "Gộp nhiều file DWG thành 1 file và xuất sheet Revit ra 1 file CAD.\nTác giả: Vũ Xuân Tuyến. Không cần quyền quản trị.",
                Bounds = new Rectangle(18, 44, 490, 40)
            });

            bool hasAcad = Directory.Exists(@"C:\Program Files\Autodesk\AutoCAD 2024");
            bool hasRevit = Directory.Exists(@"C:\Program Files\Autodesk\Revit 2025");
            ckAcad.Text = "Plugin AutoCAD 2024 – lệnh VXTGOP" + (hasAcad ? "" : "  (chưa thấy AutoCAD 2024)");
            ckAcad.Bounds = new Rectangle(20, 92, 480, 24);
            ckAcad.Checked = hasAcad || !hasRevit;
            ckRevit.Text = "Add-in Revit 2025 – Xuất sheet ra 1 file CAD" + (hasRevit ? "" : "  (chưa thấy Revit 2025)");
            ckRevit.Bounds = new Rectangle(20, 118, 480, 24);
            ckRevit.Checked = hasRevit || !hasAcad;
            Controls.Add(ckAcad);
            Controls.Add(ckRevit);

            tbLog.Multiline = true;
            tbLog.ReadOnly = true;
            tbLog.ScrollBars = ScrollBars.Vertical;
            tbLog.Bounds = new Rectangle(20, 152, 480, 150);
            Controls.Add(tbLog);

            var btnCai = new Button { Text = "Cài đặt", Bounds = new Rectangle(220, 314, 90, 32) };
            var btnGo = new Button { Text = "Gỡ cài đặt", Bounds = new Rectangle(316, 314, 90, 32) };
            var btnDong = new Button { Text = "Đóng", Bounds = new Rectangle(412, 314, 88, 32) };
            btnCai.Click += (s, e) => CaiDat();
            btnGo.Click += (s, e) => GoCaiDat();
            btnDong.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { btnCai, btnGo, btnDong });
            AcceptButton = btnCai;
        }

        private void Log(string s) => tbLog.AppendText(s + Environment.NewLine);

        private static void Extract(string resName, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
            {
                if (src == null) throw new InvalidOperationException("Thiếu dữ liệu " + resName + " trong bộ cài.");
                using (var dst = File.Create(target)) src.CopyTo(dst);
            }
        }

        private void CaiDat()
        {
            tbLog.Clear();
            if (!ckAcad.Checked && !ckRevit.Checked) { Log("Chưa chọn phần nào để cài."); return; }
            try
            {
                if (ckAcad.Checked)
                {
                    Extract("PackageContents.xml", Path.Combine(AcadBundle, "PackageContents.xml"));
                    Extract("VXT.GopCAD.dll", Path.Combine(AcadBundle, @"Contents\VXT.GopCAD.dll"));
                    Extract("VXT.GopCAD.Core.dll", Path.Combine(AcadBundle, @"Contents\VXT.GopCAD.Core.dll"));
                    Log("[OK] AutoCAD 2024: " + AcadBundle);
                }
                if (ckRevit.Checked)
                {
                    Extract("VXT.RevitXuatCAD.addin", Path.Combine(RevitAddins, "VXT.RevitXuatCAD.addin"));
                    Extract("VXT.RevitXuatCAD.dll", Path.Combine(RevitAddins, @"VXT.RevitXuatCAD\VXT.RevitXuatCAD.dll"));
                    Extract("VXT.GopCAD.Core.dll", Path.Combine(RevitAddins, @"VXT.RevitXuatCAD\AutoCAD\VXT.GopCAD.Core.dll"));
                    Log("[OK] Revit 2025: " + RevitAddins);
                }
                Log("");
                Log("CÀI XONG. Hãy đóng và mở lại AutoCAD / Revit, tìm tab \"VXT\" trên Ribbon.");
            }
            catch (IOException ex)
            {
                Log("LỖI: " + ex.Message);
                Log("File đang bị dùng – hãy đóng AutoCAD và Revit rồi bấm Cài đặt lại.");
            }
            catch (Exception ex)
            {
                Log("LỖI: " + ex.Message);
            }
        }

        private void GoCaiDat()
        {
            tbLog.Clear();
            try
            {
                if (Directory.Exists(AcadBundle)) Directory.Delete(AcadBundle, true);
                Log("[OK] Đã gỡ plugin AutoCAD.");
                string addin = Path.Combine(RevitAddins, "VXT.RevitXuatCAD.addin");
                if (File.Exists(addin)) File.Delete(addin);
                string dir = Path.Combine(RevitAddins, "VXT.RevitXuatCAD");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Log("[OK] Đã gỡ add-in Revit.");
            }
            catch (Exception ex)
            {
                Log("LỖI: " + ex.Message);
                Log("Hãy đóng AutoCAD và Revit rồi thử lại.");
            }
        }
    }
}
