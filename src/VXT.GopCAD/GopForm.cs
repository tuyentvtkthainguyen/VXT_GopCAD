using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using VXT.GopCAD.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace VXT.GopCAD
{
    /// <summary>Hộp thoại gộp nhiều file DWG thành 1 file.</summary>
    public class GopForm : Form
    {
        private readonly ListView lv = new ListView();
        private readonly RadioButton rbHang = new RadioButton();
        private readonly RadioButton rbGiu = new RadioButton();
        private readonly NumericUpDown nCot = new NumericUpDown();
        private readonly TextBox tbGap = new TextBox();
        private readonly ComboBox cbLayout = new ComboBox();
        private readonly CheckBox ckBlock = new CheckBox();
        private readonly CheckBox ckPrefix = new CheckBox();
        private readonly CheckBox ckLabel = new CheckBox();
        private readonly CheckBox ckMangle = new CheckBox();
        private readonly CheckBox ckOpen = new CheckBox();
        private readonly TextBox tbOut = new TextBox();
        private readonly ProgressBar pb = new ProgressBar();
        private readonly Label lbStatus = new Label();
        private readonly Button btnGop = new Button();
        private readonly Button btnDong = new Button();
        private bool running, cancel;

        public string ResultPath { get; private set; }
        public bool OpenAfter => ckOpen.Checked;

        public GopForm()
        {
            Text = "VXT - Gộp nhiều file AutoCAD thành 1 file";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 560);
            AllowDrop = true;

            // ----- Danh sách file -----
            lv.SetBounds(12, 12, 600, 262);
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.HideSelection = false;
            lv.AllowDrop = true;
            lv.Columns.Add("STT", 45);
            lv.Columns.Add("Tên file", 230);
            lv.Columns.Add("Thư mục", 320);
            Controls.Add(lv);

            int bx = 622, by = 12;
            AddBtn("Thêm file...", bx, ref by, (s, e) => ThemFile());
            AddBtn("Thêm thư mục...", bx, ref by, (s, e) => ThemThuMuc());
            AddBtn("Xóa dòng chọn", bx, ref by, (s, e) => XoaChon());
            AddBtn("Xóa hết", bx, ref by, (s, e) => { lv.Items.Clear(); DanhSo(); });
            by += 8;
            AddBtn("▲ Lên", bx, ref by, (s, e) => DiChuyen(-1));
            AddBtn("▼ Xuống", bx, ref by, (s, e) => DiChuyen(1));
            AddBtn("Sắp xếp A→Z", bx, ref by, (s, e) => SapXep());

            // ----- Tùy chọn -----
            var gb = new GroupBox { Text = "Tùy chọn gộp", Bounds = new Rectangle(12, 284, 736, 152) };
            Controls.Add(gb);

            rbHang.Text = "Xếp các bản vẽ thành hàng, số bản vẽ/hàng:";
            rbHang.SetBounds(12, 22, 290, 22);
            rbHang.Checked = true;
            nCot.SetBounds(305, 21, 60, 23);
            nCot.Minimum = 0; nCot.Maximum = 100; nCot.Value = 5;
            var lbGap = new Label { Text = "Khoảng cách (0 = tự động):", Bounds = new Rectangle(385, 24, 165, 20) };
            tbGap.SetBounds(552, 21, 80, 23);
            tbGap.Text = "0";
            rbGiu.Text = "Giữ nguyên tọa độ gốc (các bản vẽ nằm chồng lên nhau)";
            rbGiu.SetBounds(12, 48, 400, 22);

            var lbLay = new Label { Text = "Layout:", Bounds = new Rectangle(12, 80, 55, 20) };
            cbLayout.SetBounds(70, 77, 420, 23);
            cbLayout.DropDownStyle = ComboBoxStyle.DropDownList;
            cbLayout.Items.AddRange(new object[]
            {
                "Không lấy Layout (chỉ gộp Model)",
                "Lấy Layout đầu tiên của mỗi file (hợp với file sheet xuất từ Revit)",
                "Lấy tất cả Layout của mỗi file"
            });
            cbLayout.SelectedIndex = 1;

            ckBlock.Text = "Mỗi file thành 1 block";
            ckBlock.SetBounds(12, 108, 180, 22);
            ckPrefix.Text = "Thêm tên file vào trước tên layer";
            ckPrefix.SetBounds(200, 108, 240, 22);
            ckLabel.Text = "Ghi tên file dưới mỗi bản vẽ";
            ckLabel.SetBounds(450, 108, 230, 22);
            ckMangle.Text = "Block/kiểu chữ trùng tên: đổi tên (bỏ chọn = dùng bản của file đầu tiên)";
            ckMangle.SetBounds(12, 128, 600, 22);

            gb.Controls.AddRange(new Control[] { rbHang, nCot, lbGap, tbGap, rbGiu, lbLay, cbLayout, ckBlock, ckPrefix, ckLabel, ckMangle });
            rbHang.CheckedChanged += (s, e) => { nCot.Enabled = tbGap.Enabled = rbHang.Checked; };

            // ----- File kết quả -----
            Controls.Add(new Label { Text = "File kết quả:", Bounds = new Rectangle(12, 451, 85, 20) });
            tbOut.SetBounds(100, 448, 560, 23);
            Controls.Add(tbOut);
            var btnOut = new Button { Text = "...", Bounds = new Rectangle(668, 447, 80, 25) };
            btnOut.Click += (s, e) => ChonFileKetQua();
            Controls.Add(btnOut);

            ckOpen.Text = "Mở file sau khi gộp";
            ckOpen.Checked = true;
            ckOpen.SetBounds(12, 478, 200, 22);
            Controls.Add(ckOpen);

            pb.SetBounds(12, 504, 736, 16);
            Controls.Add(pb);
            lbStatus.SetBounds(12, 528, 530, 22);
            lbStatus.Text = "Kéo thả file/thư mục DWG vào danh sách hoặc bấm \"Thêm file...\"";
            Controls.Add(lbStatus);

            btnGop.Text = "Gộp";
            btnGop.SetBounds(560, 525, 90, 28);
            btnGop.Click += (s, e) => Gop();
            btnDong.Text = "Đóng";
            btnDong.SetBounds(658, 525, 90, 28);
            btnDong.Click += (s, e) => { if (running) cancel = true; else Close(); };
            Controls.Add(btnGop);
            Controls.Add(btnDong);

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            lv.DragEnter += OnDragEnter;
            lv.DragDrop += OnDragDrop;
            lv.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) XoaChon(); };
            FormClosing += (s, e) => { if (running) { cancel = true; e.Cancel = true; } };
        }

        private void AddBtn(string text, int x, ref int y, EventHandler h)
        {
            var b = new Button { Text = text, Bounds = new Rectangle(x, y, 126, 28) };
            b.Click += h;
            Controls.Add(b);
            y += 34;
        }

        // ================= Danh sách =================

        private void ThemFiles(IEnumerable<string> files)
        {
            var have = new HashSet<string>(lv.Items.Cast<ListViewItem>().Select(i => (string)i.Tag), StringComparer.OrdinalIgnoreCase);
            lv.BeginUpdate();
            foreach (var f in files)
            {
                if (!f.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) || have.Contains(f)) continue;
                have.Add(f);
                var it = new ListViewItem(new[] { "", Path.GetFileName(f), Path.GetDirectoryName(f) }) { Tag = f };
                lv.Items.Add(it);
            }
            lv.EndUpdate();
            DanhSo();
            if (string.IsNullOrWhiteSpace(tbOut.Text) && lv.Items.Count > 0)
            {
                string d = Path.GetDirectoryName((string)lv.Items[0].Tag);
                tbOut.Text = Path.Combine(d, "GOP_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".dwg");
            }
        }

        private void DanhSo()
        {
            for (int i = 0; i < lv.Items.Count; i++) lv.Items[i].SubItems[0].Text = (i + 1).ToString();
            lbStatus.Text = "Có " + lv.Items.Count + " file.";
        }

        private void ThemFile()
        {
            using (var d = new OpenFileDialog { Multiselect = true, Filter = "Bản vẽ AutoCAD (*.dwg)|*.dwg", Title = "Chọn các file cần gộp" })
                if (d.ShowDialog(this) == DialogResult.OK) ThemFiles(d.FileNames);
        }

        private void ThemThuMuc()
        {
            using (var d = new FolderBrowserDialog { Description = "Chọn thư mục chứa các file DWG" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var r = MessageBox.Show(this, "Lấy cả file trong các thư mục con?", "VXT", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                var opt = r == DialogResult.Yes ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                try { ThemFiles(Directory.GetFiles(d.SelectedPath, "*.dwg", opt).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "VXT", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        private void XoaChon()
        {
            foreach (ListViewItem it in lv.SelectedItems.Cast<ListViewItem>().ToList()) lv.Items.Remove(it);
            DanhSo();
        }

        private void DiChuyen(int dir)
        {
            if (lv.SelectedIndices.Count != 1) return;
            int i = lv.SelectedIndices[0], j = i + dir;
            if (j < 0 || j >= lv.Items.Count) return;
            var it = lv.Items[i];
            lv.Items.RemoveAt(i);
            lv.Items.Insert(j, it);
            it.Selected = true;
            it.Focused = true;
            DanhSo();
        }

        private void SapXep()
        {
            var items = lv.Items.Cast<ListViewItem>().OrderBy(i => (string)i.Tag, new NaturalComparer()).ToList();
            lv.BeginUpdate();
            lv.Items.Clear();
            lv.Items.AddRange(items.ToArray());
            lv.EndUpdate();
            DanhSo();
        }

        private void OnDragEnter(object s, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object s, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] paths)) return;
            var files = new List<string>();
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p, "*.dwg", SearchOption.AllDirectories));
                    else files.Add(p);
                }
                catch { }
            }
            ThemFiles(files);
        }

        private void ChonFileKetQua()
        {
            using (var d = new SaveFileDialog { Filter = "Bản vẽ AutoCAD (*.dwg)|*.dwg", Title = "Lưu file gộp", FileName = Path.GetFileName(tbOut.Text) })
            {
                try { if (!string.IsNullOrWhiteSpace(tbOut.Text)) d.InitialDirectory = Path.GetDirectoryName(tbOut.Text); } catch { }
                if (d.ShowDialog(this) == DialogResult.OK) tbOut.Text = d.FileName;
            }
        }

        // ================= Gộp =================

        private void Gop()
        {
            if (running) { cancel = true; return; }
            if (lv.Items.Count == 0) { Bao("Chưa có file nào trong danh sách."); return; }
            string outp = tbOut.Text.Trim();
            if (outp.Length == 0) { Bao("Chưa chọn file kết quả."); return; }
            if (!outp.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) outp += ".dwg";
            if (!double.TryParse(tbGap.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double gap) || gap < 0)
            { Bao("Khoảng cách không hợp lệ."); return; }
            if (File.Exists(outp) &&
                MessageBox.Show(this, "File kết quả đã có, ghi đè?", "VXT", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            var o = new MergeOptions
            {
                OutputPath = outp,
                Placement = rbHang.Checked ? PlacementMode.XepHang : PlacementMode.GiuToaDo,
                Columns = (int)nCot.Value,
                Gap = gap,
                Layouts = cbLayout.SelectedIndex == 0 ? LayoutImportMode.KhongLay
                        : cbLayout.SelectedIndex == 2 ? LayoutImportMode.TatCa : LayoutImportMode.LayoutDau,
                AsBlock = ckBlock.Checked,
                LayerPrefix = ckPrefix.Checked,
                Label = ckLabel.Checked,
                MangleDuplicates = ckMangle.Checked
            };
            foreach (ListViewItem it in lv.Items) o.Items.Add(new MergeItem { FilePath = (string)it.Tag });

            running = true;
            cancel = false;
            btnGop.Text = "Dừng";
            UseWaitCursor = true;
            try
            {
                var eng = new MergeEngine { IsCancelled = () => cancel };
                eng.Progress += (i, n, s) =>
                {
                    pb.Maximum = Math.Max(1, n);
                    pb.Value = Math.Min(i, pb.Maximum);
                    lbStatus.Text = i < n ? "Đang gộp " + (i + 1) + "/" + n + ": " + s : s;
                    Application.DoEvents();
                };

                MergeResult res;
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                using (doc?.LockDocument())
                    res = eng.Run(o);

                pb.Value = pb.Maximum;
                ResultPath = res.OutputPath;
                string msg = "Đã gộp " + res.Ok + "/" + o.Items.Count + " file trong " + res.Elapsed.TotalSeconds.ToString("0.0") + " giây.\n" + res.OutputPath;
                if (res.Errors.Count > 0) msg += "\n\nLỗi:\n" + string.Join("\n", res.Errors.Take(15));
                if (res.Warnings.Count > 0) msg += "\n\nLưu ý:\n" + string.Join("\n", res.Warnings.Take(10));
                lbStatus.Text = "Xong: " + res.OutputPath;
                MessageBox.Show(this, msg, "VXT - Gộp CAD", MessageBoxButtons.OK,
                                res.Errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                running = false;
                if (ckOpen.Checked) { DialogResult = DialogResult.OK; Close(); }
            }
            catch (Exception ex)
            {
                lbStatus.Text = "Lỗi.";
                MessageBox.Show(this, ex.Message, "VXT - Lỗi gộp", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                running = false;
                btnGop.Text = "Gộp";
                UseWaitCursor = false;
            }
        }

        private void Bao(string s) => MessageBox.Show(this, s, "VXT", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>So sánh tự nhiên: "KC-2" đứng trước "KC-10".</summary>
    public class NaturalComparer : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            a = a ?? ""; b = b ?? "";
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
}
