using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace VXT.RevitXuatCAD
{
    /// <summary>Hộp thoại chọn sheet và tùy chọn xuất.</summary>
    public class XuatSheetForm : Form
    {
        private readonly List<SheetRow> _rows;
        private readonly XuatSettings _s;
        private readonly TextBox tbFilter = new();
        private readonly DataGridView dgv = new();
        private readonly Label lbCount = new();
        private readonly ComboBox cbSetup = new();
        private readonly RadioButton rbLayout = new(), rbModelTL = new(), rbModel = new(), rbLe = new();
        private readonly NumericUpDown nCot = new();
        private readonly CheckBox ckLabel = new(), ckLe = new(), ckOpen = new();
        private readonly TextBox tbPattern = new(), tbOut = new(), tbAccore = new();
        private bool _filling;

        public XuatJob Job { get; private set; }

        public XuatSheetForm(List<SheetRow> rows, List<string> setups, XuatSettings s, string docTitle)
        {
            _rows = rows;
            _s = s;
            Text = "VXT - Xuất sheet Revit ra 1 file CAD";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(920, 600);

            // ===== Bên trái: danh sách sheet =====
            Controls.Add(new Label { Text = "Tìm sheet:", Bounds = new Rectangle(12, 15, 68, 20) });
            tbFilter.SetBounds(82, 12, 438, 23);
            tbFilter.PlaceholderText = "gõ số hiệu hoặc tên (không cần dấu)";
            tbFilter.TextChanged += (o, e) => Fill();
            Controls.Add(tbFilter);

            dgv.SetBounds(12, 42, 508, 500);
            dgv.AllowUserToAddRows = false;
            dgv.AllowUserToDeleteRows = false;
            dgv.AllowUserToResizeRows = false;
            dgv.RowHeadersVisible = false;
            dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgv.BackgroundColor = SystemColors.Window;
            dgv.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Chọn", FillWeight = 12 });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Số hiệu", ReadOnly = true, FillWeight = 25 });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tên sheet", ReadOnly = true, FillWeight = 63 });
            dgv.CurrentCellDirtyStateChanged += (o, e) =>
            {
                if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            dgv.CellValueChanged += (o, e) =>
            {
                if (_filling || e.RowIndex < 0 || e.ColumnIndex != 0) return;
                var r = (SheetRow)dgv.Rows[e.RowIndex].Tag;
                r.Chon = Convert.ToBoolean(dgv.Rows[e.RowIndex].Cells[0].Value ?? false);
                UpdateCount();
            };
            // Chọn nhiều dòng rồi bấm phím cách để bật/tắt cùng lúc
            dgv.KeyDown += (o, e) =>
            {
                if (e.KeyCode != Keys.Space || dgv.SelectedRows.Count < 2) return;
                bool v = !((SheetRow)dgv.SelectedRows[0].Tag).Chon;
                foreach (DataGridViewRow gr in dgv.SelectedRows) ((SheetRow)gr.Tag).Chon = v;
                Fill();
                e.Handled = true;
            };
            Controls.Add(dgv);

            AddButton("Chọn hết", 12, 552, 85, (o, e) => SetVisible(_ => true));
            AddButton("Bỏ hết", 102, 552, 85, (o, e) => SetVisible(_ => false));
            AddButton("Đảo chọn", 192, 552, 85, (o, e) => SetVisible(r => !r.Chon));
            lbCount.SetBounds(290, 557, 230, 20);
            Controls.Add(lbCount);

            // ===== Bên phải: tùy chọn =====
            int x = 540, w = 368;
            Controls.Add(new Label { Text = "Thiết lập xuất DWG (DWG Export Setup):", Bounds = new Rectangle(x, 12, w, 20) });
            cbSetup.SetBounds(x, 32, w, 23);
            cbSetup.DropDownStyle = ComboBoxStyle.DropDownList;
            cbSetup.Items.Add("<Mặc định của Revit>");
            foreach (var n in setups) cbSetup.Items.Add(n);
            int si = string.IsNullOrEmpty(s.SetupName) ? 0 : cbSetup.Items.IndexOf(s.SetupName);
            cbSetup.SelectedIndex = si < 0 ? 0 : si;
            Controls.Add(cbSetup);

            var gb = new GroupBox { Text = "Kiểu kết quả", Bounds = new Rectangle(x, 64, w, 140) };
            rbModelTL.Text = "1 file DWG ở Model: bản vẽ 1:1, khung tên phóng theo tỷ lệ (in thẳng từ Model)";
            rbModelTL.SetBounds(10, 20, w - 20, 36);
            rbLayout.Text = "1 file DWG: mỗi sheet 1 Layout, bản vẽ 1:1 ở Model";
            rbLayout.SetBounds(10, 58, w - 20, 22);
            rbModel.Text = "1 file DWG: chỉ Model, không có khung tên";
            rbModel.SetBounds(10, 82, w - 20, 22);
            rbLe.Text = "Chỉ xuất các file DWG lẻ (không gộp)";
            rbLe.SetBounds(10, 106, w - 20, 22);
            gb.Controls.AddRange(new Control[] { rbModelTL, rbLayout, rbModel, rbLe });
            Controls.Add(gb);
            (s.Kieu == 0 ? rbLayout : s.Kieu == 1 ? rbModel : s.Kieu == 2 ? rbLe : rbModelTL).Checked = true;

            Controls.Add(new Label { Text = "Số sheet mỗi hàng ở Model:", Bounds = new Rectangle(x, 216, 190, 20) });
            nCot.SetBounds(x + 195, 213, 60, 23);
            nCot.Minimum = 1;
            nCot.Maximum = 50;
            nCot.Value = Math.Max(1, Math.Min(50, s.Columns));
            Controls.Add(nCot);

            Controls.Add(new Label { Text = "Tên Layout:", Bounds = new Rectangle(x, 246, 75, 20) });
            tbPattern.SetBounds(x + 80, 243, w - 80, 23);
            tbPattern.Text = string.IsNullOrWhiteSpace(s.Pattern) ? "{SoHieu} - {Ten}" : s.Pattern;
            Controls.Add(tbPattern);
            Controls.Add(new Label
            {
                Text = "Dùng {SoHieu} và {Ten}, ví dụ: KC-{SoHieu}",
                ForeColor = SystemColors.GrayText,
                Bounds = new Rectangle(x + 80, 268, w - 80, 18)
            });

            ckLabel.Text = "Ghi số hiệu + tên sheet dưới mỗi bản vẽ ở Model";
            ckLabel.SetBounds(x, 292, w, 22);
            ckLabel.Checked = s.Label;
            ckLe.Text = "Giữ thêm các file DWG lẻ của từng sheet";
            ckLe.SetBounds(x, 316, w, 22);
            ckLe.Checked = s.KeepSingles;
            ckOpen.Text = "Mở thư mục chứa file khi xong";
            ckOpen.SetBounds(x, 340, w, 22);
            ckOpen.Checked = s.OpenFolder;
            Controls.AddRange(new Control[] { ckLabel, ckLe, ckOpen });

            Controls.Add(new Label { Text = "File DWG kết quả:", Bounds = new Rectangle(x, 372, w, 20) });
            tbOut.SetBounds(x, 392, w - 40, 23);
            string folder = !string.IsNullOrEmpty(s.LastFolder) && Directory.Exists(s.LastFolder)
                ? s.LastFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            tbOut.Text = Path.Combine(folder, Txt.TenFile(docTitle) + "_Sheets.dwg");
            Controls.Add(tbOut);
            AddButton("...", x + w - 34, 391, 34, (o, e) => ChonOut());

            Controls.Add(new Label { Text = "AutoCAD Core Console (accoreconsole.exe):", Bounds = new Rectangle(x, 426, w, 20) });
            tbAccore.SetBounds(x, 446, w - 40, 23);
            tbAccore.Text = !string.IsNullOrEmpty(s.AccorePath) && File.Exists(s.AccorePath) ? s.AccorePath : XuatSettings.DetectAccore();
            Controls.Add(tbAccore);
            AddButton("...", x + w - 34, 445, 34, (o, e) => ChonAccore());

            Controls.Add(new Label
            {
                Text = "Revit xuất từng sheet ra DWG, sau đó AutoCAD Core Console (có sẵn khi cài AutoCAD 2024) " +
                       "chạy ngầm để gộp thành 1 file – không cần mở AutoCAD. Không có AutoCAD thì vẫn xuất được file lẻ.",
                ForeColor = SystemColors.GrayText,
                Bounds = new Rectangle(x, 478, w, 64)
            });

            AddButton("Xuất", x + w - 186, 552, 90, (o, e) => OnOk());
            AddButton("Hủy", x + w - 90, 552, 90, (o, e) => { DialogResult = DialogResult.Cancel; Close(); });

            rbLe.CheckedChanged += (o, e) => CapNhatTrangThai();
            rbModel.CheckedChanged += (o, e) => CapNhatTrangThai();
            rbLayout.CheckedChanged += (o, e) => CapNhatTrangThai();
            CapNhatTrangThai();
            Fill();
        }

        private void AddButton(string text, int x, int y, int w, EventHandler h)
        {
            var b = new Button { Text = text, Bounds = new Rectangle(x, y, w, 28) };
            b.Click += h;
            Controls.Add(b);
        }

        private void CapNhatTrangThai()
        {
            bool gop = !rbLe.Checked;
            nCot.Enabled = gop;
            ckLabel.Enabled = gop;
            ckLe.Enabled = gop;
            tbAccore.Enabled = gop;
            tbPattern.Enabled = rbLayout.Checked;
        }

        private void Fill()
        {
            string f = Txt.BoDau(tbFilter.Text.Trim());
            _filling = true;
            dgv.SuspendLayout();
            dgv.Rows.Clear();
            foreach (var r in _rows)
            {
                if (f.Length > 0 && !Txt.BoDau(r.So).Contains(f) && !Txt.BoDau(r.Ten).Contains(f)) continue;
                int i = dgv.Rows.Add(r.Chon, r.So, r.Ten);
                dgv.Rows[i].Tag = r;
            }
            dgv.ResumeLayout();
            _filling = false;
            UpdateCount();
        }

        private void SetVisible(Func<SheetRow, bool> v)
        {
            foreach (DataGridViewRow gr in dgv.Rows)
            {
                var r = (SheetRow)gr.Tag;
                r.Chon = v(r);
            }
            Fill();
        }

        private void UpdateCount() =>
            lbCount.Text = "Đã chọn " + _rows.Count(r => r.Chon) + " / " + _rows.Count + " sheet";

        private void ChonOut()
        {
            using var d = new SaveFileDialog { Filter = "Bản vẽ AutoCAD (*.dwg)|*.dwg", Title = "Lưu file DWG kết quả" };
            try
            {
                d.InitialDirectory = Path.GetDirectoryName(tbOut.Text);
                d.FileName = Path.GetFileName(tbOut.Text);
            }
            catch { }
            if (d.ShowDialog(this) == DialogResult.OK) tbOut.Text = d.FileName;
        }

        private void ChonAccore()
        {
            using var d = new OpenFileDialog { Filter = "accoreconsole.exe|accoreconsole.exe", Title = "Chọn accoreconsole.exe trong thư mục cài AutoCAD" };
            if (d.ShowDialog(this) == DialogResult.OK) tbAccore.Text = d.FileName;
        }

        private void OnOk()
        {
            var sel = _rows.Where(r => r.Chon).ToList();
            if (sel.Count == 0) { Bao("Chưa chọn sheet nào."); return; }

            string outp = tbOut.Text.Trim();
            if (outp.Length == 0) { Bao("Chưa chọn file kết quả."); return; }
            if (!outp.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) outp += ".dwg";
            try { outp = Path.GetFullPath(outp); }
            catch { Bao("Đường dẫn file kết quả không hợp lệ."); return; }

            var kieu = rbLayout.Checked ? KieuKetQua.MoiSheetMotLayout : rbModel.Checked ? KieuKetQua.ChiModel
                     : rbLe.Checked ? KieuKetQua.ChiFileLe : KieuKetQua.ModelKhungTenTheoTyLe;
            string acc = tbAccore.Text.Trim();
            if (kieu != KieuKetQua.ChiFileLe && !File.Exists(acc))
            {
                var r = MessageBox.Show(this,
                    "Không tìm thấy accoreconsole.exe của AutoCAD nên chưa gộp được.\n\nChỉ xuất các file DWG lẻ của từng sheet?",
                    "VXT", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
                kieu = KieuKetQua.ChiFileLe;
            }
            if (kieu != KieuKetQua.ChiFileLe && File.Exists(outp) &&
                MessageBox.Show(this, "File kết quả đã có, ghi đè?", "VXT", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _s.SetupName = cbSetup.SelectedIndex <= 0 ? null : (string)cbSetup.SelectedItem;
            _s.Kieu = (int)(rbLayout.Checked ? KieuKetQua.MoiSheetMotLayout : rbModel.Checked ? KieuKetQua.ChiModel
                          : rbLe.Checked ? KieuKetQua.ChiFileLe : KieuKetQua.ModelKhungTenTheoTyLe);
            _s.Columns = (int)nCot.Value;
            _s.Pattern = tbPattern.Text;
            _s.Label = ckLabel.Checked;
            _s.KeepSingles = ckLe.Checked;
            _s.OpenFolder = ckOpen.Checked;
            _s.LastFolder = Path.GetDirectoryName(outp);
            if (File.Exists(acc)) _s.AccorePath = acc;

            Job = new XuatJob
            {
                Sheets = sel,
                SetupName = _s.SetupName,
                Kieu = kieu,
                Columns = (int)nCot.Value,
                Pattern = tbPattern.Text,
                Label = ckLabel.Checked,
                KeepSingles = ckLe.Checked,
                OpenFolder = ckOpen.Checked,
                OutputPath = outp,
                AccorePath = acc
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Bao(string s) => MessageBox.Show(this, s, "VXT", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Cửa sổ tiến trình nhỏ, có nút Dừng.</summary>
    public class ProgressForm : Form
    {
        private readonly Label lb = new();
        private readonly ProgressBar pb = new();
        public bool Cancel { get; private set; }

        public ProgressForm()
        {
            Text = "VXT - Đang xuất";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ControlBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 110);
            lb.SetBounds(12, 12, 456, 36);
            pb.SetBounds(12, 52, 456, 18);
            var b = new Button { Text = "Dừng", Bounds = new Rectangle(388, 76, 80, 26) };
            b.Click += (o, e) => { Cancel = true; lb.Text = "Đang dừng..."; };
            Controls.AddRange(new Control[] { lb, pb, b });
        }

        public void Set(int i, int n, string text)
        {
            pb.Maximum = Math.Max(1, n);
            pb.Value = Math.Max(0, Math.Min(i, pb.Maximum));
            if (!Cancel) lb.Text = text;
            Application.DoEvents();
        }

        public void Pump() => Application.DoEvents();
    }
}
