using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace VXT.GopCAD.DanhSo
{
    /// <summary>Hộp thoại đánh số bản vẽ: chọn khung hoặc layout, vị trí điền số/tên, sửa tên, xuất danh mục.</summary>
    public class DanhSoForm : Form
    {
        private readonly Database db;
        private readonly Editor ed;
        public DanhSoSettings S { get; }
        public List<SheetItem> Items { get; private set; } = new List<SheetItem>();
        private List<string> tags = new List<string>();
        private string polyLayer;          // khung là polyline kín trên layer này
        private bool loading;

        private readonly RadioButton rbFrame = new RadioButton(), rbLayout = new RadioButton();
        private readonly Button btnSample = new Button(), btnFind = new Button();
        private readonly TextBox tbPattern = new TextBox();
        private readonly Label lbSample = new Label();
        private readonly ComboBox cbOrder = new ComboBox();
        private readonly TextBox tbPre = new TextBox(), tbSuf = new TextBox(), tbHt = new TextBox(), tbLayer = new TextBox();
        private readonly NumericUpDown nStart = new NumericUpDown(), nStep = new NumericUpDown(), nDig = new NumericUpDown();
        private readonly Label lbDemo = new Label();
        private readonly ComboBox cbNum = new ComboBox(), cbName = new ComboBox();
        private readonly Button btnNumPt = new Button(), btnNamePt = new Button();
        private readonly Label lbNumPt = new Label(), lbNamePt = new Label();
        private readonly CheckBox ckRen = new CheckBox();
        private readonly ComboBox cbRenFmt = new ComboBox();
        private readonly CheckBox ckDm = new CheckBox(), ckTable = new CheckBox(), ckCsv = new CheckBox();
        private readonly ComboBox cbDest = new ComboBox();
        private readonly DataGridView grid = new DataGridView();
        private readonly Label lbCount = new Label();

        public DanhSoForm(Database db, Editor ed)
        {
            this.db = db; this.ed = ed;
            S = DanhSoSettings.Load();

            Text = "VXT - Đánh số bản vẽ tự động";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1060, 660);
            MinimumSize = new Size(900, 600);

            // ---------- cột trái: thiết lập ----------
            var left = new Panel { Bounds = new Rectangle(8, 8, 410, 644), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left, AutoScroll = true };
            Controls.Add(left);
            int y = 0;

            rbFrame.Text = "Khung bản vẽ (block / polyline kín)"; rbFrame.SetBounds(4, y, 240, 22);
            rbLayout.Text = "Layout"; rbLayout.SetBounds(250, y, 120, 22);
            left.Controls.AddRange(new Control[] { rbFrame, rbLayout });
            y += 26;

            var gF = new GroupBox { Text = "Khung bản vẽ", Bounds = new Rectangle(0, y, 400, 110) };
            btnSample.Text = "Chọn khung mẫu..."; btnSample.SetBounds(10, 20, 130, 26);
            lbSample.SetBounds(148, 24, 245, 20); lbSample.AutoEllipsis = true; lbSample.Text = "(chưa chọn)";
            gF.Controls.Add(new Label { Text = "Lọc tên block:", Bounds = new Rectangle(10, 54, 90, 20) });
            tbPattern.SetBounds(100, 51, 200, 23);
            btnFind.Text = "Tìm khung"; btnFind.SetBounds(306, 50, 84, 26);
            gF.Controls.Add(new Label { Text = "Thứ tự:", Bounds = new Rectangle(10, 83, 50, 20) });
            cbOrder.SetBounds(62, 80, 328, 23); cbOrder.DropDownStyle = ComboBoxStyle.DropDownList;
            cbOrder.Items.AddRange(new object[] { "Theo hàng: trái → phải, trên → dưới", "Theo cột: trên → dưới, trái → phải", "Theo hàng: trái → phải, dưới → trên" });
            gF.Controls.AddRange(new Control[] { btnSample, lbSample, tbPattern, btnFind, cbOrder });
            left.Controls.Add(gF);
            y += 116;

            var gN = new GroupBox { Text = "Định dạng số bản vẽ", Bounds = new Rectangle(0, y, 400, 104) };
            gN.Controls.Add(new Label { Text = "Tiền tố", Bounds = new Rectangle(10, 24, 50, 20) });
            tbPre.SetBounds(62, 21, 90, 23);
            gN.Controls.Add(new Label { Text = "Hậu tố", Bounds = new Rectangle(170, 24, 50, 20) });
            tbSuf.SetBounds(222, 21, 90, 23);
            gN.Controls.Add(new Label { Text = "Bắt đầu", Bounds = new Rectangle(10, 53, 50, 20) });
            nStart.SetBounds(62, 50, 60, 23); nStart.Minimum = -9999; nStart.Maximum = 99999;
            gN.Controls.Add(new Label { Text = "Bước", Bounds = new Rectangle(135, 53, 35, 20) });
            nStep.SetBounds(172, 50, 50, 23); nStep.Minimum = -100; nStep.Maximum = 1000;
            gN.Controls.Add(new Label { Text = "Số chữ số", Bounds = new Rectangle(235, 53, 62, 20) });
            nDig.SetBounds(300, 50, 45, 23); nDig.Minimum = 1; nDig.Maximum = 6;
            lbDemo.SetBounds(10, 78, 380, 20); lbDemo.ForeColor = Color.DarkBlue;
            gN.Controls.AddRange(new Control[] { tbPre, tbSuf, nStart, nStep, nDig, lbDemo });
            left.Controls.Add(gN);
            y += 110;

            var gP = new GroupBox { Text = "Vị trí điền", Bounds = new Rectangle(0, y, 400, 150) };
            gP.Controls.Add(new Label { Text = "Số:", Bounds = new Rectangle(10, 24, 30, 20) });
            cbNum.SetBounds(42, 21, 348, 23); cbNum.DropDownStyle = ComboBoxStyle.DropDownList;
            btnNumPt.Text = "Chọn điểm số..."; btnNumPt.SetBounds(42, 47, 120, 24);
            lbNumPt.SetBounds(168, 51, 222, 20); lbNumPt.AutoEllipsis = true;
            gP.Controls.Add(new Label { Text = "Tên:", Bounds = new Rectangle(10, 80, 30, 20) });
            cbName.SetBounds(42, 77, 348, 23); cbName.DropDownStyle = ComboBoxStyle.DropDownList;
            btnNamePt.Text = "Chọn điểm tên..."; btnNamePt.SetBounds(42, 103, 120, 24);
            lbNamePt.SetBounds(168, 107, 222, 20); lbNamePt.AutoEllipsis = true;
            gP.Controls.AddRange(new Control[] { cbNum, btnNumPt, lbNumPt, cbName, btnNamePt, lbNamePt });
            left.Controls.Add(gP);
            y += 156;

            var gT = new GroupBox { Text = "Chữ mới (khi chỗ đó chưa có text)", Bounds = new Rectangle(0, y, 400, 54) };
            gT.Controls.Add(new Label { Text = "Cao chữ (0 = tự động)", Bounds = new Rectangle(10, 24, 130, 20) });
            tbHt.SetBounds(140, 21, 60, 23);
            gT.Controls.Add(new Label { Text = "Layer", Bounds = new Rectangle(214, 24, 40, 20) });
            tbLayer.SetBounds(256, 21, 134, 23);
            gT.Controls.AddRange(new Control[] { tbHt, tbLayer });
            left.Controls.Add(gT);
            y += 60;

            ckRen.Text = "Đổi tên tab layout:"; ckRen.SetBounds(4, y, 140, 22);
            cbRenFmt.SetBounds(146, y, 250, 23); cbRenFmt.DropDownStyle = ComboBoxStyle.DropDownList;
            cbRenFmt.Items.AddRange(new object[] { "Số bản vẽ", "Số - Tên bản vẽ", "Tên bản vẽ" });
            left.Controls.AddRange(new Control[] { ckRen, cbRenFmt });
            y += 28;

            var gD = new GroupBox { Text = "Danh mục bản vẽ", Bounds = new Rectangle(0, y, 400, 104) };
            ckDm.Text = "Xuất danh mục bản vẽ sau khi đánh số"; ckDm.SetBounds(10, 20, 380, 22);
            ckTable.Text = "Bảng trong bản vẽ"; ckTable.SetBounds(28, 44, 150, 22);
            ckCsv.Text = "File CSV (mở bằng Excel)"; ckCsv.SetBounds(190, 44, 200, 22);
            gD.Controls.Add(new Label { Text = "Đặt bảng tại:", Bounds = new Rectangle(28, 74, 80, 20) });
            cbDest.SetBounds(110, 71, 280, 23); cbDest.DropDownStyle = ComboBoxStyle.DropDownList;
            gD.Controls.AddRange(new Control[] { ckDm, ckTable, ckCsv, cbDest });
            left.Controls.Add(gD);
            y += 110;
            left.Controls.Add(new Label
            {
                Text = "Chương trình chỉ làm việc trong bản vẽ đang mở, không gửi dữ liệu đi đâu.",
                Bounds = new Rectangle(4, y, 396, 32), ForeColor = Color.DimGray
            });

            // ---------- cột phải: danh sách ----------
            grid.SetBounds(428, 8, 624, 572);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "use", HeaderText = "Đánh số", Width = 60 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "stt", HeaderText = "STT", Width = 40, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "num", HeaderText = "Số bản vẽ", Width = 90, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Tên bản vẽ (sửa trực tiếp)", Width = 260 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "info", HeaderText = "Khung / Layout", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            Controls.Add(grid);

            int bx = 428, by = 588;
            Btn("▲ Lên", 70, (s, e) => MoveRow(-1));
            Btn("▼ Xuống", 70, (s, e) => MoveRow(1));
            Btn("Nhập tên từ file .txt...", 150, (s, e) => ImportNames());
            Btn("Lấy lại tên từ bản vẽ", 140, (s, e) => { foreach (var it in Items) it.NameEdited = false; DefaultNames(); });
            lbCount.SetBounds(bx + 4, by + 4, 180, 20); lbCount.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(lbCount);

            var btnOk = new Button { Text = "ĐÁNH SỐ", Bounds = new Rectangle(852, 620, 110, 32), Font = new Font(Font, FontStyle.Bold), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            var btnClose = new Button { Text = "Đóng", Bounds = new Rectangle(968, 620, 84, 32), DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnOk.Click += (s, e) => Accept();
            Controls.AddRange(new Control[] { btnOk, btnClose });
            AcceptButton = null; CancelButton = btnClose;

            void Btn(string text, int w, EventHandler h)
            {
                var b = new Button { Text = text, Bounds = new Rectangle(bx, by, w, 26), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
                b.Click += h; Controls.Add(b); bx += w + 6;
            }

            // ---------- giá trị ban đầu ----------
            loading = true;
            tbPattern.Text = S.Pattern; tbPre.Text = S.Prefix; tbSuf.Text = S.Suffix;
            nStart.Value = Clamp(S.Start, nStart); nStep.Value = Clamp(S.Step == 0 ? 1 : S.Step, nStep); nDig.Value = Clamp(S.Digits, nDig);
            cbOrder.SelectedIndex = Math.Max(0, Math.Min(2, S.Order));
            tbHt.Text = S.TextHeight.ToString(CultureInfo.InvariantCulture); tbLayer.Text = S.Layer;
            ckRen.Checked = S.RenameLayouts; cbRenFmt.SelectedIndex = Math.Max(0, Math.Min(2, S.RenameFmt));
            ckDm.Checked = S.Catalog; ckTable.Checked = S.CatalogTable; ckCsv.Checked = S.CatalogCsv;
            rbLayout.Checked = S.LayoutMode; rbFrame.Checked = !S.LayoutMode;
            S.HasNumPt = S.HasNamePt = false;
            loading = false;

            // ---------- sự kiện ----------
            btnSample.Click += (s, e) => PickSample();
            btnFind.Click += (s, e) => FindFrames();
            cbOrder.SelectedIndexChanged += (s, e) => { if (!loading && Items.Count > 0 && !S.LayoutMode) { Items = DanhSoEngine.Sort(Items, cbOrder.SelectedIndex); Renumber(); } };
            EventHandler fmt = (s, e) => { if (!loading) Renumber(); };
            tbPre.TextChanged += fmt; tbSuf.TextChanged += fmt; nStart.ValueChanged += fmt; nStep.ValueChanged += fmt; nDig.ValueChanged += fmt;
            rbFrame.CheckedChanged += (s, e) => { if (!loading) SwitchMode(); };
            cbNum.SelectedIndexChanged += (s, e) => UpdateUi();
            cbName.SelectedIndexChanged += (s, e) => { UpdateUi(); if (!loading) DefaultNames(); };
            btnNumPt.Click += (s, e) => PickPoint(true);
            btnNamePt.Click += (s, e) => PickPoint(false);
            ckDm.CheckedChanged += (s, e) => UpdateUi();
            ckTable.CheckedChanged += (s, e) => UpdateUi();
            ckRen.CheckedChanged += (s, e) => UpdateUi();
            grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += GridChanged;

            SwitchMode();
        }

        private static decimal Clamp(int v, NumericUpDown n) => Math.Max(n.Minimum, Math.Min(n.Maximum, v));

        // ---------------- chế độ ----------------
        private void SwitchMode()
        {
            S.LayoutMode = rbLayout.Checked;
            S.HasNumPt = S.HasNamePt = false;
            Items = new List<SheetItem>();
            if (S.LayoutMode)
            {
                Items = DanhSoEngine.Layouts(db);
                tags = DanhSoEngine.Tags(db, Items);
            }
            else
            {
                tags = new List<string>();
                if (tbPattern.Text.Trim() != "") FindFrames(false);
            }
            FillPosCombos();
            cbDest.Items.Clear();
            if (S.LayoutMode)
            {
                cbDest.Items.Add("Model");
                foreach (var it in Items) cbDest.Items.Add(it.SpaceName);
                cbDest.SelectedIndex = Items.Count > 0 ? 1 : 0;
            }
            else
            {
                cbDest.Items.Add("Không gian đang mở (" + LayoutManager.Current.CurrentLayout + ")");
                cbDest.SelectedIndex = 0;
            }
            DefaultNames();
            UpdateUi();
        }

        private void FillPosCombos()
        {
            loading = true;
            foreach (var cb in new[] { cbNum, cbName })
            {
                bool isNum = cb == cbNum;
                var mode = isNum ? S.NumMode : S.NameMode;
                var tag = isNum ? S.NumTag : S.NameTag;
                cb.Items.Clear();
                cb.Items.Add("Không điền");
                cb.Items.Add("Tại điểm chọn (cập nhật chữ có sẵn, chưa có thì tạo mới)");
                foreach (var t in tags) cb.Items.Add("Thuộc tính khung tên: " + t);
                int idx = mode == PosMode.None ? 0 : mode == PosMode.Point ? 1 : 2 + tags.FindIndex(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
                if (idx < 0 || (mode == PosMode.Attribute && idx < 2))
                {
                    var guess = tags.FindIndex(t => isNum ? (t.Contains("SO") || t.Contains("NO") || t.Contains("NUM") || t.Contains("SHEET"))
                                                          : (t.Contains("TEN") || t.Contains("NAME") || t.Contains("TITLE")));
                    idx = guess >= 0 ? guess + 2 : 1;
                }
                cb.SelectedIndex = Math.Min(idx, cb.Items.Count - 1);
            }
            loading = false;
        }

        private void UpdateUi()
        {
            bool frame = !S.LayoutMode;
            btnSample.Enabled = tbPattern.Enabled = btnFind.Enabled = cbOrder.Enabled = frame;
            if (polyLayer != null) tbPattern.Enabled = false;
            btnNumPt.Enabled = cbNum.SelectedIndex == 1;
            btnNamePt.Enabled = cbName.SelectedIndex == 1;
            lbNumPt.Text = cbNum.SelectedIndex != 1 ? "" : S.HasNumPt ? "✓ đã chọn điểm" : "chưa chọn điểm";
            lbNamePt.Text = cbName.SelectedIndex != 1 ? "" : S.HasNamePt ? "✓ đã chọn điểm" : "chưa chọn điểm";
            lbNumPt.ForeColor = S.HasNumPt ? Color.DarkGreen : Color.Firebrick;
            lbNamePt.ForeColor = S.HasNamePt ? Color.DarkGreen : Color.Firebrick;
            ckRen.Enabled = cbRenFmt.Enabled = S.LayoutMode;
            cbRenFmt.Enabled = S.LayoutMode && ckRen.Checked;
            ckTable.Enabled = ckCsv.Enabled = ckDm.Checked;
            cbDest.Enabled = ckDm.Checked && ckTable.Checked && S.LayoutMode;
        }

        // ---------------- chọn khung / điểm ----------------
        private void PickSample()
        {
            PromptEntityResult r;
            using (ed.StartUserInteraction(this))
            {
                var o = new PromptEntityOptions("\nChọn 1 khung bản vẽ mẫu (block khung tên hoặc polyline kín): ");
                o.SetRejectMessage("\nPhải là block hoặc polyline.");
                o.AddAllowedClass(typeof(BlockReference), false);
                o.AddAllowedClass(typeof(Polyline), false);
                r = ed.GetEntity(o);
            }
            if (r.Status != PromptStatus.OK) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var e = tr.GetObject(r.ObjectId, OpenMode.ForRead);
                if (e is BlockReference br)
                {
                    var nm = DanhSoEngine.EffectiveName(tr, br);
                    polyLayer = null;
                    tbPattern.Text = DanhSoEngine.DefaultPattern(nm);
                    lbSample.Text = DanhSoEngine.DecodeU(nm);
                }
                else if (e is Polyline pl)
                {
                    polyLayer = pl.Layer;
                    tbPattern.Text = "";
                    lbSample.Text = "Polyline kín, layer " + pl.Layer;
                }
                tr.Commit();
            }
            FindFrames();
        }

        private void FindFrames(bool showMsg = true)
        {
            if (polyLayer == null && tbPattern.Text.Trim() == "")
            {
                if (showMsg) MessageBox.Show(this, "Hãy chọn khung mẫu hoặc nhập mẫu tên block (dùng * và ?).", "VXT");
                return;
            }
            var list = DanhSoEngine.FindFrames(db, db.CurrentSpaceId, tbPattern.Text.Trim(), polyLayer);
            Items = DanhSoEngine.Sort(list, cbOrder.SelectedIndex);
            S.HasNumPt = S.HasNamePt = false;
            tags = DanhSoEngine.Tags(db, Items);
            FillPosCombos();
            DefaultNames();
            UpdateUi();
            if (showMsg && Items.Count == 0)
                MessageBox.Show(this, "Không tìm thấy khung nào trong không gian đang mở (" + LayoutManager.Current.CurrentLayout + ").", "VXT");
        }

        private void PickPoint(bool isNum)
        {
            if (Items.Count == 0) { MessageBox.Show(this, "Chưa có bản vẽ nào trong danh sách.", "VXT"); return; }
            using (ed.StartUserInteraction(this))
            {
                if (S.LayoutMode)
                {
                    var first = (Items.FirstOrDefault(i => i.Use) ?? Items[0]).SpaceName;
                    LayoutManager.Current.CurrentLayout = first;
                    try { ed.SwitchToPaperSpace(); } catch { }
                }
                var r = ed.GetPoint(isNum
                    ? "\nChọn điểm đặt SỐ bản vẽ (bấm vào chữ số có sẵn trong khung tên): "
                    : "\nChọn điểm đặt TÊN bản vẽ (bấm vào chữ tên có sẵn trong khung tên): ");
                if (r.Status != PromptStatus.OK) return;
                var p = r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                if (S.LayoutMode)
                {
                    if (isNum) { S.NumPt = p; S.HasNumPt = true; } else { S.NamePt = p; S.HasNamePt = true; }
                }
                else
                {
                    var f = Items.FirstOrDefault(i => p.X >= i.Min.X && p.X <= i.Max.X && p.Y >= i.Min.Y && p.Y <= i.Max.Y);
                    if (f == null) { ed.WriteMessage("\nĐiểm không nằm trong khung nào trong danh sách."); return; }
                    var uv = new Point2d((p.X - f.Min.X) / f.W, (p.Y - f.Min.Y) / f.H);
                    if (isNum) { S.NumUV = uv; S.HasNumPt = true; } else { S.NameUV = uv; S.HasNamePt = true; }
                }
            }
            if (!isNum) DefaultNames();
            UpdateUi();
        }

        // ---------------- danh sách ----------------
        private void ReadPos()
        {
            S.NumMode = cbNum.SelectedIndex <= 0 ? PosMode.None : cbNum.SelectedIndex == 1 ? PosMode.Point : PosMode.Attribute;
            S.NumTag = cbNum.SelectedIndex >= 2 ? tags[cbNum.SelectedIndex - 2] : "";
            S.NameMode = cbName.SelectedIndex <= 0 ? PosMode.None : cbName.SelectedIndex == 1 ? PosMode.Point : PosMode.Attribute;
            S.NameTag = cbName.SelectedIndex >= 2 ? tags[cbName.SelectedIndex - 2] : "";
        }

        private void DefaultNames()
        {
            if (loading) return;
            ReadPos();
            if (Items.Count > 0) DanhSoEngine.FillDefaultNames(db, Items, S);
            Renumber();
        }

        private void Renumber()
        {
            S.Prefix = tbPre.Text; S.Suffix = tbSuf.Text;
            S.Start = (int)nStart.Value; S.Step = (int)nStep.Value; S.Digits = (int)nDig.Value;
            int k = 0;
            foreach (var it in Items) it.Number = it.Use ? S.Format(k++) : "";
            lbDemo.Text = "Ví dụ: " + S.Format(0) + ", " + S.Format(1) + ", " + S.Format(2) + ", ...";
            FillGrid();
        }

        private void FillGrid()
        {
            loading = true;
            int cur = grid.CurrentCell?.RowIndex ?? -1;
            grid.Rows.Clear();
            int i = 0;
            foreach (var it in Items)
                grid.Rows.Add(it.Use, ++i, it.Number, it.Name, it.IsLayout ? "Layout: " + it.SpaceName : it.Info);
            if (cur >= 0 && cur < grid.Rows.Count) grid.CurrentCell = grid.Rows[cur].Cells[3];
            lbCount.Text = $"{Items.Count(x => x.Use)}/{Items.Count} bản vẽ";
            loading = false;
        }

        private void GridChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (loading || e.RowIndex < 0 || e.RowIndex >= Items.Count) return;
            var it = Items[e.RowIndex];
            if (e.ColumnIndex == 0) { it.Use = (bool)(grid.Rows[e.RowIndex].Cells[0].Value ?? false); BeginInvoke((Action)Renumber); }
            else if (e.ColumnIndex == 3) { it.Name = DanhSoEngine.Clean(grid.Rows[e.RowIndex].Cells[3].Value as string); it.NameEdited = true; }
        }

        private void MoveRow(int d)
        {
            int i = grid.CurrentCell?.RowIndex ?? -1, j = i + d;
            if (i < 0 || j < 0 || j >= Items.Count) return;
            (Items[i], Items[j]) = (Items[j], Items[i]);
            Renumber();
            grid.CurrentCell = grid.Rows[j].Cells[3];
        }

        private void ImportNames()
        {
            using (var d = new OpenFileDialog { Filter = "Text (*.txt;*.csv)|*.txt;*.csv|Tất cả|*.*", Title = "Mỗi dòng 1 tên bản vẽ (UTF-8)" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var lines = File.ReadAllLines(d.FileName, Encoding.UTF8).Select(l => l.Trim()).Where(l => l != "").ToList();
                int k = 0;
                foreach (var it in Items.Where(x => x.Use))
                {
                    if (k >= lines.Count) break;
                    it.Name = DanhSoEngine.Clean(lines[k++]); it.NameEdited = true;
                }
                FillGrid();
            }
        }

        // ---------------- đồng ý ----------------
        private void Accept()
        {
            grid.EndEdit();
            ReadPos();
            Renumber();
            string err = null;
            bool htOk = double.TryParse(tbHt.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double ht);
            if (Items.Count(i => i.Use) == 0) err = "Chưa có bản vẽ nào được chọn để đánh số.";
            else if (S.NumMode == PosMode.Point && !S.HasNumPt) err = "Chưa chọn điểm đặt SỐ bản vẽ (nút \"Chọn điểm số...\").";
            else if (S.NameMode == PosMode.Point && !S.HasNamePt) err = "Chưa chọn điểm đặt TÊN bản vẽ (nút \"Chọn điểm tên...\").";
            else if (!htOk || ht < 0) err = "Chiều cao chữ không hợp lệ.";
            else if (!SymbolUtilityServices.IsValidSymbolName(tbLayer.Text.Trim(), false)) err = "Tên layer không hợp lệ.";
            else if (S.NumMode == PosMode.None && S.NameMode == PosMode.None && !(S.LayoutMode && ckRen.Checked) && !ckDm.Checked)
                err = "Chưa chọn việc cần làm.";
            if (err == null) S.TextHeight = ht;
            if (err != null) { MessageBox.Show(this, err, "VXT", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            S.Pattern = tbPattern.Text.Trim();
            S.Order = cbOrder.SelectedIndex;
            S.Layer = tbLayer.Text.Trim();
            S.RenameLayouts = ckRen.Checked; S.RenameFmt = cbRenFmt.SelectedIndex;
            S.Catalog = ckDm.Checked; S.CatalogTable = ckTable.Checked; S.CatalogCsv = ckCsv.Checked;
            S.CatalogSpace = S.LayoutMode ? (cbDest.SelectedItem as string ?? "Model") : LayoutManager.Current.CurrentLayout;
            S.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
