using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Font = System.Drawing.Font;

namespace VXT.GopCAD.DanhSo
{
    /// <summary>Thiết lập in hàng loạt.</summary>
    public class InSettings
    {
        public string Device = "DWG To PDF.pc3";
        public string Media = "";            // tên chuẩn (canonical) của khổ giấy
        public string StyleSheet = "monochrome.ctb";
        public bool Merge = true;            // gộp 1 file PDF nhiều trang
        public string OutPath = "";          // file PDF (gộp) hoặc thư mục (file lẻ)
        public bool ToFile = true;
    }

    /// <summary>In / xuất PDF hàng loạt theo thứ tự số bản vẽ. Chỉ in ra máy in hoặc file trên máy, không gửi dữ liệu đi đâu.</summary>
    public static class InEngine
    {
        public static bool IsPdf(string device) => device.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0;

        public static List<string> Devices()
        {
            var psv = PlotSettingsValidator.Current;
            psv.RefreshLists(new PlotSettings(true));
            return psv.GetPlotDeviceList().Cast<string>().Where(d => !d.Equals("None", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public static List<string> StyleSheets()
        {
            var psv = PlotSettingsValidator.Current;
            return psv.GetPlotStyleSheetList().Cast<string>().ToList();
        }

        /// <summary>Danh sách khổ giấy của máy in: (tên chuẩn, tên hiển thị).</summary>
        public static List<KeyValuePair<string, string>> Media(Database db, string device)
        {
            var r = new List<KeyValuePair<string, string>>();
            using (var tr = db.TransactionManager.StartTransaction())
            using (var ps = new PlotSettings(true))
            {
                var psv = PlotSettingsValidator.Current;
                psv.SetPlotConfigurationName(ps, device, null);
                psv.RefreshLists(ps);
                foreach (string m in psv.GetCanonicalMediaNameList(ps))
                    r.Add(new KeyValuePair<string, string>(m, psv.GetLocaleMediaName(ps, m)));
                tr.Commit();
            }
            return r;
        }

        private static string SafeName(string s)
        {
            s = Regex.Replace(s ?? "", @"[\\/:*?""<>|]", "-").Trim();
            return s.Length > 120 ? s.Substring(0, 120) : s;
        }

        public static string FileNameOf(SheetItem it, int i) =>
            SafeName((it.Number != "" ? it.Number : (i + 1).ToString("00")) + (it.Name != "" ? " - " + it.Name : "")) + ".pdf";

        /// <summary>In các bản vẽ theo thứ tự danh sách. Trả về thông báo.</summary>
        public static string Plot(Document doc, List<SheetItem> items, InSettings s)
        {
            var use = items.Where(i => i.Use).ToList();
            if (use.Count == 0) return "Không có bản vẽ nào được chọn.";
            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                return "AutoCAD đang in việc khác, hãy thử lại sau.";
            var db = doc.Database;
            object oldBg = AcApp.GetSystemVariable("BACKGROUNDPLOT");
            string oldLayout = LayoutManager.Current.CurrentLayout;
            AcApp.SetSystemVariable("BACKGROUNDPLOT", 0);
            int done = 0;
            var files = new List<string>();
            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                using (var pe = PlotFactory.CreatePublishEngine())
                using (var ppd = new PlotProgressDialog(false, use.Count, true))
                {
                    ppd.set_PlotMsgString(PlotMessageIndex.DialogTitle, "VXT - In hàng loạt");
                    ppd.set_PlotMsgString(PlotMessageIndex.CancelJobButtonMessage, "Dừng in");
                    ppd.set_PlotMsgString(PlotMessageIndex.CancelSheetButtonMessage, "Bỏ tờ này");
                    ppd.set_PlotMsgString(PlotMessageIndex.SheetSetProgressCaption, "Tiến độ");
                    ppd.set_PlotMsgString(PlotMessageIndex.SheetProgressCaption, "Tờ đang in");
                    ppd.LowerPlotProgressRange = 0;
                    ppd.UpperPlotProgressRange = 100;
                    ppd.PlotProgressPos = 0;
                    ppd.OnBeginPlot();
                    ppd.IsVisible = true;
                    pe.BeginPlot(ppd, null);

                    bool docOpen = false;
                    for (int i = 0; i < use.Count; i++)
                    {
                        if (ppd.IsPlotCancelled) break;
                        var it = use[i];
                        var btr = (BlockTableRecord)tr.GetObject(it.SpaceId, OpenMode.ForRead);
                        var layout = (Layout)tr.GetObject(btr.LayoutId, OpenMode.ForRead);
                        // PlotEngine chỉ in được layout đang hiện hành (tránh lỗi eLayoutNotCurrent)
                        var lm = LayoutManager.Current;
                        if (!lm.CurrentLayout.Equals(layout.LayoutName, StringComparison.OrdinalIgnoreCase))
                            lm.CurrentLayout = layout.LayoutName;
                        var pi = new PlotInfo { Layout = btr.LayoutId };
                        var ps = new PlotSettings(layout.ModelType);
                        ps.CopyFrom(layout);
                        var psv = PlotSettingsValidator.Current;
                        psv.SetPlotConfigurationName(ps, s.Device, null);
                        psv.RefreshLists(ps);
                        if (s.Media != "") try { psv.SetCanonicalMediaName(ps, s.Media); } catch { }
                        if (!it.IsLayout)
                        {
                            psv.SetPlotWindowArea(ps, new Extents2d(it.Min.X, it.Min.Y, it.Max.X, it.Max.Y));
                            psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                            psv.SetUseStandardScale(ps, true);
                            psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
                            psv.SetPlotCentered(ps, true);
                            // xoay giấy theo hướng khung
                            psv.SetPlotRotation(ps, PlotRotation.Degrees000);
                            var paper = ps.PlotPaperSize;
                            bool paperLand = paper.X >= paper.Y, frameLand = it.W >= it.H;
                            if (paperLand != frameLand) psv.SetPlotRotation(ps, PlotRotation.Degrees090);
                        }
                        else
                        {
                            psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
                        }
                        if (s.StyleSheet != "") try { psv.SetCurrentStyleSheet(ps, s.StyleSheet); } catch { }
                        pi.OverrideSettings = ps;
                        var piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
                        piv.Validate(pi);

                        bool last = i == use.Count - 1;
                        if (s.Merge || !s.ToFile)
                        {
                            if (!docOpen)
                            {
                                pe.BeginDocument(pi, doc.Name, null, 1, s.ToFile, s.ToFile ? s.OutPath : null);
                                docOpen = true;
                                if (s.ToFile) files.Add(s.OutPath);
                            }
                        }
                        else
                        {
                            string f = Path.Combine(s.OutPath, FileNameOf(it, i));
                            pe.BeginDocument(pi, doc.Name, null, 1, true, f);
                            files.Add(f);
                        }

                        ppd.StatusMsgString = $"Đang in {i + 1}/{use.Count}: {it.Number} {it.Name}";
                        ppd.OnBeginSheet();
                        ppd.LowerSheetProgressRange = 0;
                        ppd.UpperSheetProgressRange = 100;
                        ppd.SheetProgressPos = 0;
                        var ppi = new PlotPageInfo();
                        pe.BeginPage(ppi, pi, (s.Merge || !s.ToFile) ? last : true, null);
                        pe.BeginGenerateGraphics(null);
                        ppd.SheetProgressPos = 50;
                        pe.EndGenerateGraphics(null);
                        pe.EndPage(null);
                        ppd.SheetProgressPos = 100;
                        ppd.OnEndSheet();
                        ppd.PlotProgressPos = (int)(100.0 * (i + 1) / use.Count);
                        done++;

                        if (!(s.Merge || !s.ToFile)) pe.EndDocument(null);
                        else if (last) { pe.EndDocument(null); docOpen = false; }
                    }
                    if (docOpen) pe.EndDocument(null);   // trường hợp dừng giữa chừng
                    ppd.PlotProgressPos = 100;
                    ppd.OnEndPlot();
                    pe.EndPlot(null);
                    tr.Commit();
                }
            }
            finally
            {
                AcApp.SetSystemVariable("BACKGROUNDPLOT", oldBg);
                try
                {
                    using (doc.LockDocument())
                        if (!LayoutManager.Current.CurrentLayout.Equals(oldLayout, StringComparison.OrdinalIgnoreCase))
                            LayoutManager.Current.CurrentLayout = oldLayout;
                }
                catch { }
            }
            string msg = $"Đã in {done}/{use.Count} bản vẽ theo thứ tự ({use[0].Number} … {use[use.Count - 1].Number}).";
            if (s.ToFile) msg += s.Merge ? "\nFile PDF: " + s.OutPath : $"\n{files.Count} file PDF trong thư mục: " + s.OutPath;
            else msg += "\nĐã gửi tới máy in: " + s.Device;
            return msg;
        }
    }

    /// <summary>Hộp thoại in hàng loạt / xuất PDF theo thứ tự.</summary>
    public class InPdfForm : Form
    {
        private readonly Document doc;
        private readonly Database db;
        public List<SheetItem> Items { get; private set; } = new List<SheetItem>();
        public InSettings S { get; } = new InSettings();
        private readonly DanhSoSettings ds = DanhSoSettings.Load();
        private List<KeyValuePair<string, string>> media = new List<KeyValuePair<string, string>>();

        private readonly RadioButton rbFrame = new RadioButton(), rbLayout = new RadioButton();
        private readonly TextBox tbPattern = new TextBox(), tbOut = new TextBox();
        private readonly ComboBox cbDevice = new ComboBox(), cbMedia = new ComboBox(), cbCtb = new ComboBox(), cbSort = new ComboBox();
        private readonly RadioButton rbMerge = new RadioButton(), rbSplit = new RadioButton();
        private readonly DataGridView grid = new DataGridView();
        private readonly Label lbCount = new Label();
        private bool loading;

        public InPdfForm(Document doc)
        {
            this.doc = doc; db = doc.Database;
            Text = "VXT - In hàng loạt / Xuất PDF theo thứ tự";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ClientSize = new Size(1000, 620);
            MinimumSize = new Size(860, 560);

            var left = new Panel { Bounds = new Rectangle(8, 8, 380, 604), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
            Controls.Add(left);
            int y = 0;
            rbFrame.Text = "Khung bản vẽ trong Model"; rbFrame.SetBounds(4, y, 190, 22);
            rbLayout.Text = "Layout"; rbLayout.SetBounds(200, y, 100, 22);
            left.Controls.AddRange(new Control[] { rbFrame, rbLayout });
            y += 28;
            left.Controls.Add(new Label { Text = "Lọc tên block khung:", Bounds = new Rectangle(4, y + 3, 120, 20) });
            tbPattern.SetBounds(126, y, 160, 23);
            var btnFind = new Button { Text = "Tìm lại", Bounds = new Rectangle(292, y - 1, 80, 26) };
            left.Controls.AddRange(new Control[] { tbPattern, btnFind });
            y += 30;
            left.Controls.Add(new Label { Text = "Thứ tự in:", Bounds = new Rectangle(4, y + 3, 120, 20) });
            cbSort.SetBounds(126, y, 246, 23); cbSort.DropDownStyle = ComboBoxStyle.DropDownList;
            cbSort.Items.AddRange(new object[] { "Theo số bản vẽ (KC-01, KC-02...)", "Theo vị trí (như khi đánh số)" });
            left.Controls.Add(cbSort);
            y += 36;

            var gP = new GroupBox { Text = "Máy in / khổ giấy", Bounds = new Rectangle(0, y, 376, 120) };
            gP.Controls.Add(new Label { Text = "Máy in:", Bounds = new Rectangle(10, 24, 60, 20) });
            cbDevice.SetBounds(80, 21, 286, 23); cbDevice.DropDownStyle = ComboBoxStyle.DropDownList;
            gP.Controls.Add(new Label { Text = "Khổ giấy:", Bounds = new Rectangle(10, 54, 60, 20) });
            cbMedia.SetBounds(80, 51, 286, 23); cbMedia.DropDownStyle = ComboBoxStyle.DropDownList;
            gP.Controls.Add(new Label { Text = "Kiểu nét:", Bounds = new Rectangle(10, 84, 60, 20) });
            cbCtb.SetBounds(80, 81, 286, 23); cbCtb.DropDownStyle = ComboBoxStyle.DropDownList;
            gP.Controls.AddRange(new Control[] { cbDevice, cbMedia, cbCtb });
            left.Controls.Add(gP);
            y += 126;
            left.Controls.Add(new Label
            {
                Text = "Khung trong Model in vừa khổ giấy, tự xoay ngang/dọc theo khung. Layout in theo thiết lập trang của layout.",
                Bounds = new Rectangle(4, y, 372, 34), ForeColor = Color.DimGray
            });
            y += 40;

            var gO = new GroupBox { Text = "Kết quả (máy in PDF)", Bounds = new Rectangle(0, y, 376, 112) };
            rbMerge.Text = "Gộp 1 file PDF nhiều trang theo thứ tự"; rbMerge.SetBounds(10, 22, 350, 22); rbMerge.Checked = true;
            rbSplit.Text = "Mỗi bản vẽ 1 file (đặt tên \"Số - Tên.pdf\")"; rbSplit.SetBounds(10, 46, 350, 22);
            tbOut.SetBounds(10, 76, 284, 23);
            var btnOut = new Button { Text = "...", Bounds = new Rectangle(300, 75, 66, 25) };
            gO.Controls.AddRange(new Control[] { rbMerge, rbSplit, tbOut, btnOut });
            left.Controls.Add(gO);
            y += 118;
            left.Controls.Add(new Label
            {
                Text = "Chỉ in ra máy in / file trên máy này, không gửi dữ liệu đi đâu.",
                Bounds = new Rectangle(4, y, 372, 20), ForeColor = Color.DimGray
            });

            grid.SetBounds(396, 8, 596, 556);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "In", Width = 40 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Trang", Width = 50, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Số bản vẽ", Width = 90, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tên bản vẽ", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            Controls.Add(grid);

            int bx = 396;
            void Btn(string t, int w, EventHandler h) { var b = new Button { Text = t, Bounds = new Rectangle(bx, 548, w, 26), Anchor = AnchorStyles.Bottom | AnchorStyles.Left }; b.Click += h; Controls.Add(b); bx += w + 6; }
            Btn("▲ Lên", 66, (s, e) => MoveRow(-1));
            Btn("▼ Xuống", 70, (s, e) => MoveRow(1));
            Btn("Chọn hết", 70, (s, e) => { foreach (var it in Items) it.Use = true; Fill(); });
            Btn("Bỏ chọn", 66, (s, e) => { foreach (var it in Items) it.Use = false; Fill(); });
            lbCount.SetBounds(bx + 6, 552, 120, 20); lbCount.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; Controls.Add(lbCount);

            var btnGo = new Button { Text = "BẮT ĐẦU IN", Bounds = new Rectangle(790, 580, 112, 30), Font = new Font(Font, FontStyle.Bold), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            var btnClose = new Button { Text = "Đóng", Bounds = new Rectangle(908, 580, 84, 30), DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            Controls.AddRange(new Control[] { btnGo, btnClose });
            CancelButton = btnClose;
            grid.Height = 532;

            // giá trị ban đầu
            loading = true;
            tbPattern.Text = ds.Pattern;
            cbSort.SelectedIndex = 0;
            rbLayout.Checked = ds.LayoutMode; rbFrame.Checked = !ds.LayoutMode;
            try
            {
                foreach (var d in InEngine.Devices()) cbDevice.Items.Add(d);
                int k = cbDevice.Items.IndexOf("DWG To PDF.pc3");
                cbDevice.SelectedIndex = k >= 0 ? k : (cbDevice.Items.Count > 0 ? 0 : -1);
                cbCtb.Items.Add("(giữ nguyên)");
                foreach (var c in InEngine.StyleSheets()) cbCtb.Items.Add(c);
                int m = cbCtb.Items.IndexOf("monochrome.ctb");
                cbCtb.SelectedIndex = m >= 0 ? m : 0;
            }
            catch { }
            string dir = Path.GetDirectoryName(db.Filename ?? "");
            if (!Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            tbOut.Text = Path.Combine(dir, Path.GetFileNameWithoutExtension(doc.Name) + ".pdf");
            loading = false;

            cbDevice.SelectedIndexChanged += (s, e) => LoadMedia();
            rbFrame.CheckedChanged += (s, e) => { if (!loading) LoadItems(); };
            cbSort.SelectedIndexChanged += (s, e) => { if (!loading) LoadItems(); };
            btnFind.Click += (s, e) => LoadItems();
            rbSplit.CheckedChanged += (s, e) => FixOut();
            btnOut.Click += (s, e) => ChooseOut();
            btnGo.Click += (s, e) => Go();
            grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += (s, e) =>
            {
                if (loading || e.RowIndex < 0 || e.ColumnIndex != 0) return;
                Items[e.RowIndex].Use = (bool)(grid.Rows[e.RowIndex].Cells[0].Value ?? false);
                BeginInvoke((Action)Fill);
            };

            LoadMedia();
            LoadItems();
        }

        private void LoadMedia()
        {
            cbMedia.Items.Clear();
            media.Clear();
            if (cbDevice.SelectedItem == null) return;
            try { media = InEngine.Media(db, (string)cbDevice.SelectedItem); } catch { }
            foreach (var m in media) cbMedia.Items.Add(m.Value);
            int k = media.FindIndex(m => Regex.IsMatch(m.Value, @"A3", RegexOptions.IgnoreCase) && m.Value.IndexOf("full bleed", StringComparison.OrdinalIgnoreCase) >= 0);
            if (k < 0) k = media.FindIndex(m => Regex.IsMatch(m.Value, @"\bA3\b", RegexOptions.IgnoreCase));
            cbMedia.SelectedIndex = k >= 0 ? k : (cbMedia.Items.Count > 0 ? 0 : -1);
            bool pdf = InEngine.IsPdf((string)cbDevice.SelectedItem);
            rbMerge.Enabled = rbSplit.Enabled = tbOut.Enabled = pdf;
        }

        private void LoadItems()
        {
            ds.LayoutMode = rbLayout.Checked;
            if (ds.LayoutMode) Items = DanhSoEngine.Layouts(db);
            else
            {
                if (tbPattern.Text.Trim() == "") { Items = new List<SheetItem>(); Fill(); return; }
                Items = DanhSoEngine.Sort(DanhSoEngine.FindFrames(db, db.CurrentSpaceId, tbPattern.Text.Trim(), null), ds.Order);
            }
            DanhSoEngine.ReadNumbers(db, Items, ds);
            if (ds.LayoutMode) foreach (var it in Items) if (it.Number == "") it.Number = it.SpaceName;
            if (cbSort.SelectedIndex == 0)
            {
                var numbered = Items.Where(i => i.Number != "").ToList();
                numbered.Sort((a, b) => DanhSoEngine.NaturalCompare(a.Number, b.Number));
                Items = numbered.Concat(Items.Where(i => i.Number == "")).ToList();
            }
            Fill();
        }

        private void Fill()
        {
            loading = true;
            grid.Rows.Clear();
            int p = 0;
            foreach (var it in Items) grid.Rows.Add(it.Use, it.Use ? (++p).ToString() : "", it.Number, it.Name);
            lbCount.Text = $"{Items.Count(i => i.Use)}/{Items.Count} bản vẽ";
            loading = false;
        }

        private void MoveRow(int d)
        {
            int i = grid.CurrentCell?.RowIndex ?? -1, j = i + d;
            if (i < 0 || j < 0 || j >= Items.Count) return;
            (Items[i], Items[j]) = (Items[j], Items[i]);
            Fill();
            grid.CurrentCell = grid.Rows[j].Cells[2];
        }

        private void FixOut()
        {
            string t = tbOut.Text.Trim();
            if (rbSplit.Checked && t.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) tbOut.Text = Path.GetDirectoryName(t);
            else if (rbMerge.Checked && !t.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                tbOut.Text = Path.Combine(t, Path.GetFileNameWithoutExtension(doc.Name) + ".pdf");
        }

        private void ChooseOut()
        {
            if (rbMerge.Checked)
                using (var d = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = Path.GetFileName(tbOut.Text), InitialDirectory = SafeDir(Path.GetDirectoryName(tbOut.Text)) })
                { if (d.ShowDialog(this) == DialogResult.OK) tbOut.Text = d.FileName; }
            else
                using (var d = new FolderBrowserDialog { SelectedPath = SafeDir(tbOut.Text) })
                { if (d.ShowDialog(this) == DialogResult.OK) tbOut.Text = d.SelectedPath; }
        }

        private static string SafeDir(string d) => !string.IsNullOrEmpty(d) && Directory.Exists(d) ? d : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        private void Go()
        {
            if (Items.Count(i => i.Use) == 0) { MessageBox.Show(this, "Chưa chọn bản vẽ nào để in.", "VXT"); return; }
            if (cbDevice.SelectedItem == null) { MessageBox.Show(this, "Chưa chọn máy in.", "VXT"); return; }
            S.Device = (string)cbDevice.SelectedItem;
            S.Media = cbMedia.SelectedIndex >= 0 ? media[cbMedia.SelectedIndex].Key : "";
            S.StyleSheet = cbCtb.SelectedIndex > 0 ? (string)cbCtb.SelectedItem : "";
            S.ToFile = InEngine.IsPdf(S.Device);
            S.Merge = rbMerge.Checked;
            S.OutPath = tbOut.Text.Trim();
            if (S.ToFile)
            {
                try
                {
                    if (S.Merge)
                    {
                        if (!S.OutPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) S.OutPath += ".pdf";
                        Directory.CreateDirectory(Path.GetDirectoryName(S.OutPath));
                        if (File.Exists(S.OutPath) &&
                            MessageBox.Show(this, "File PDF đã có, ghi đè?", "VXT", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    }
                    else Directory.CreateDirectory(S.OutPath);
                }
                catch (Exception ex) { MessageBox.Show(this, "Đường dẫn không hợp lệ: " + ex.Message, "VXT"); return; }
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
