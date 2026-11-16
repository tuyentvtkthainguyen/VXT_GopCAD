using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace VXT.GopCAD.DanhSo
{
    /// <summary>Một bản vẽ cần đánh số: một khung (block/polyline) hoặc một layout.</summary>
    public class SheetItem
    {
        public bool Use = true;
        public bool IsLayout;
        public ObjectId FrameId;          // khung (khi đánh số khung)
        public ObjectId LayoutId;         // layout (khi đánh số layout)
        public ObjectId SpaceId;          // BlockTableRecord chứa khung / của layout
        public string SpaceName;          // tên tab chứa khung / tên layout
        public string Handle;             // handle khung hoặc layout (dùng liên kết text)
        public Point3d Min, Max;          // khung bao của khung bản vẽ
        public string Number = "";
        public string Name = "";
        public bool NameEdited;
        public string Info = "";          // mô tả hiển thị (tên block / layout)

        public double W => Max.X - Min.X;
        public double H => Max.Y - Min.Y;
        public Point3d AtUV(Point2d uv) => new Point3d(Min.X + uv.X * W, Min.Y + uv.Y * H, 0);
    }

    /// <summary>Cách điền số / tên.</summary>
    public enum PosMode { None, Point, Attribute }

    /// <summary>Thiết lập đánh số (được nhớ cho lần sau trong %AppData%\VXT\DanhSoBanVe.ini).</summary>
    public class DanhSoSettings
    {
        public bool LayoutMode;
        public string Pattern = "";
        public string Prefix = "KT-", Suffix = "";
        public int Start = 1, Step = 1, Digits = 2, Order;
        public PosMode NumMode = PosMode.Point, NameMode = PosMode.Point;
        public string NumTag = "", NameTag = "";
        public Point2d NumUV, NameUV;        // vị trí tương đối trong khung (0..1)
        public Point3d NumPt, NamePt;        // vị trí trong layout (giấy)
        public bool HasNumPt, HasNamePt;
        public double TextHeight;            // 0 = tự động
        public string Layer = "VXT-SO-BV";
        public bool Catalog = true, CatalogTable = true, CatalogCsv;
        public string CatalogSpace = "";     // nơi đặt bảng (layout mode)
        public bool RenameLayouts = true;
        public int RenameFmt = 1;            // 0 số, 1 số - tên, 2 tên

        public string Format(int i) =>
            Prefix + (Start + i * Step).ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(1, Digits), '0') + Suffix;

        private static string IniPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VXT", "DanhSoBanVe.ini");

        public void Save()
        {
            try
            {
                var d = new Dictionary<string, string>
                {
                    ["Pattern"] = Pattern, ["Prefix"] = Prefix, ["Suffix"] = Suffix,
                    ["Start"] = Start.ToString(), ["Step"] = Step.ToString(), ["Digits"] = Digits.ToString(),
                    ["Order"] = Order.ToString(), ["NumMode"] = NumMode.ToString(), ["NameMode"] = NameMode.ToString(),
                    ["NumTag"] = NumTag, ["NameTag"] = NameTag,
                    ["TextHeight"] = TextHeight.ToString(CultureInfo.InvariantCulture), ["Layer"] = Layer,
                    ["Catalog"] = Catalog ? "1" : "0", ["CatalogTable"] = CatalogTable ? "1" : "0",
                    ["CatalogCsv"] = CatalogCsv ? "1" : "0", ["RenameLayouts"] = RenameLayouts ? "1" : "0",
                    ["RenameFmt"] = RenameFmt.ToString(), ["LayoutMode"] = LayoutMode ? "1" : "0"
                };
                Directory.CreateDirectory(Path.GetDirectoryName(IniPath));
                File.WriteAllLines(IniPath, d.Select(kv => kv.Key + "=" + (kv.Value ?? "").Replace("\n", " ")), Encoding.UTF8);
            }
            catch { }
        }

        public static DanhSoSettings Load()
        {
            var s = new DanhSoSettings();
            try
            {
                if (!File.Exists(IniPath)) return s;
                var d = File.ReadAllLines(IniPath, Encoding.UTF8)
                    .Select(l => l.Split(new[] { '=' }, 2)).Where(a => a.Length == 2)
                    .GroupBy(a => a[0]).ToDictionary(g => g.Key, g => g.First()[1]);
                string G(string k, string def) => d.TryGetValue(k, out var v) ? v : def;
                int I(string k, int def) => int.TryParse(G(k, ""), out var v) ? v : def;
                s.Pattern = G("Pattern", s.Pattern);
                s.Prefix = G("Prefix", s.Prefix);
                s.Suffix = G("Suffix", s.Suffix);
                s.Start = I("Start", 1); s.Step = I("Step", 1); s.Digits = I("Digits", 2); s.Order = I("Order", 0);
                if (Enum.TryParse(G("NumMode", ""), out PosMode m1)) s.NumMode = m1;
                if (Enum.TryParse(G("NameMode", ""), out PosMode m2)) s.NameMode = m2;
                s.NumTag = G("NumTag", ""); s.NameTag = G("NameTag", "");
                double.TryParse(G("TextHeight", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out s.TextHeight);
                s.Layer = G("Layer", s.Layer);
                s.Catalog = G("Catalog", "1") == "1";
                s.CatalogTable = G("CatalogTable", "1") == "1";
                s.CatalogCsv = G("CatalogCsv", "0") == "1";
                s.RenameLayouts = G("RenameLayouts", "1") == "1";
                s.RenameFmt = I("RenameFmt", 1);
                s.LayoutMode = G("LayoutMode", "0") == "1";
            }
            catch { }
            return s;
        }
    }

    /// <summary>
    /// Lõi đánh số bản vẽ. Text do chương trình điền mang xdata "VXT_DSBV" (kind, handle khung/layout)
    /// để lần chạy sau cập nhật đúng chỗ, không tạo trùng (tương thích lệnh LISP DSBV).
    /// Chỉ làm việc trong bản vẽ đang mở, không gửi dữ liệu đi đâu.
    /// </summary>
    public static class DanhSoEngine
    {
        public const string App = "VXT_DSBV";

        // ---------------- chuỗi ----------------
        private static readonly Regex UCode = new Regex(@"\\U\+([0-9A-Fa-f]{4})");

        public static string DecodeU(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return UCode.Replace(s, m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString());
        }

        public static string Clean(string s)
        {
            s = DecodeU(s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Trim();
        }

        public static string PlainText(Entity e)
        {
            if (e is MText mt) return Clean(mt.Text);
            if (e is DBText t) return Clean(t.TextString);
            return "";
        }

        /// <summary>Block xuất từ Revit có dạng "Họ - Kiểu-123456-Tên sheet": lấy "Tên sheet".</summary>
        public static string RevitSheetName(string blockName)
        {
            var m = Regex.Match(blockName ?? "", @"-\d{3,}-(.+)$");
            return m.Success ? Clean(m.Groups[1].Value) : "";
        }

        /// <summary>Mẫu lọc mặc định: phần trước " - " (họ khung tên Revit) + "*".</summary>
        public static string DefaultPattern(string blockName)
        {
            int p = blockName.IndexOf(" - ", StringComparison.Ordinal);
            return p > 0 ? blockName.Substring(0, p) + "*" : blockName;
        }

        public static bool WildMatch(string text, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return true;
            foreach (var pat in pattern.Split(','))
            {
                var rx = "^" + Regex.Escape(pat.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                if (Regex.IsMatch(text ?? "", rx, RegexOptions.IgnoreCase)) return true;
            }
            return false;
        }

        public static string EffectiveName(Transaction tr, BlockReference br)
        {
            var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
        }

        private static bool TryExtents(Entity e, out Extents3d ext)
        {
            try { ext = e.GeometricExtents; return true; }
            catch { ext = new Extents3d(); return false; }
        }

        // ---------------- tìm khung ----------------
        /// <summary>Tìm các khung trong không gian <paramref name="spaceId"/>: block theo mẫu tên, hoặc polyline kín trên layer.</summary>
        public static List<SheetItem> FindFrames(Database db, ObjectId spaceId, string pattern, string polyLayer)
        {
            var list = new List<SheetItem>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForRead);
                string spaceName = SpaceName(tr, btr);
                foreach (ObjectId id in btr)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    string info = null;
                    if (polyLayer == null && e is BlockReference br)
                    {
                        var nm = EffectiveName(tr, br);
                        if (WildMatch(nm, pattern)) info = nm;
                    }
                    else if (polyLayer != null && e is Polyline pl && pl.Closed &&
                             pl.Layer.Equals(polyLayer, StringComparison.OrdinalIgnoreCase))
                        info = "Polyline - " + pl.Layer;
                    if (info == null || !TryExtents(e, out var ext)) continue;
                    if (ext.MaxPoint.X - ext.MinPoint.X <= 0 || ext.MaxPoint.Y - ext.MinPoint.Y <= 0) continue;
                    list.Add(new SheetItem
                    {
                        FrameId = id, SpaceId = spaceId, SpaceName = spaceName, Handle = e.Handle.ToString(),
                        Min = ext.MinPoint, Max = ext.MaxPoint, Info = DecodeU(info)
                    });
                }
                tr.Commit();
            }
            return list;
        }

        public static string SpaceName(Transaction tr, BlockTableRecord btr)
        {
            if (btr.IsLayout && !btr.LayoutId.IsNull)
                return ((Layout)tr.GetObject(btr.LayoutId, OpenMode.ForRead)).LayoutName;
            return btr.Name;
        }

        /// <summary>Sắp xếp: 0 hàng trái→phải, trên→dưới; 1 cột trên→dưới, trái→phải; 2 hàng trái→phải, dưới→trên.</summary>
        public static List<SheetItem> Sort(List<SheetItem> items, int mode)
        {
            if (items.Count < 2) return items.ToList();
            Func<SheetItem, double> cx = it => (it.Min.X + it.Max.X) / 2, cy = it => (it.Min.Y + it.Max.Y) / 2;
            Func<SheetItem, double> pf, sf;
            double tol;
            if (mode == 1) { pf = cx; sf = it => -cy(it); tol = 0.5 * items.Average(i => i.W); }
            else if (mode == 2) { pf = cy; sf = cx; tol = 0.5 * items.Average(i => i.H); }
            else { pf = it => -cy(it); sf = cx; tol = 0.5 * items.Average(i => i.H); }

            var res = new List<SheetItem>();
            var sorted = items.OrderBy(pf).ToList();
            var group = new List<SheetItem>();
            double refv = 0;
            foreach (var it in sorted)
            {
                if (group.Count > 0 && Math.Abs(pf(it) - refv) < tol) group.Add(it);
                else
                {
                    if (group.Count > 0) res.AddRange(group.OrderBy(sf));
                    group = new List<SheetItem> { it };
                    refv = pf(it);
                }
            }
            if (group.Count > 0) res.AddRange(group.OrderBy(sf));
            return res;
        }

        // ---------------- layout ----------------
        public static List<SheetItem> Layouts(Database db)
        {
            var list = new List<(int, SheetItem)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry de in dict)
                {
                    var lay = (Layout)tr.GetObject(de.Value, OpenMode.ForRead);
                    if (lay.ModelType) continue;
                    list.Add((lay.TabOrder, new SheetItem
                    {
                        IsLayout = true, LayoutId = de.Value, SpaceId = lay.BlockTableRecordId,
                        SpaceName = lay.LayoutName, Handle = lay.Handle.ToString(), Info = lay.LayoutName,
                        Min = new Point3d(0, 0, 0), Max = new Point3d(1, 1, 0)
                    }));
                }
                tr.Commit();
            }
            return list.OrderBy(x => x.Item1).Select(x => x.Item2).ToList();
        }

        /// <summary>Tag thuộc tính có trong các khung (khung mode) hoặc các block trong layout.</summary>
        public static List<string> Tags(Database db, IEnumerable<SheetItem> items)
        {
            var tags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var it in items)
                {
                    IEnumerable<ObjectId> ids = it.IsLayout
                        ? ((BlockTableRecord)tr.GetObject(it.SpaceId, OpenMode.ForRead)).Cast<ObjectId>()
                        : new[] { it.FrameId };
                    foreach (var id in ids)
                        if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br)
                            foreach (ObjectId aid in br.AttributeCollection)
                                tags.Add(((AttributeReference)tr.GetObject(aid, OpenMode.ForRead)).Tag.ToUpperInvariant());
                }
                tr.Commit();
            }
            return tags.ToList();
        }

        private static AttributeReference FindAtt(Transaction tr, SheetItem it, string tag, OpenMode mode)
        {
            IEnumerable<ObjectId> ids = it.IsLayout
                ? ((BlockTableRecord)tr.GetObject(it.SpaceId, OpenMode.ForRead)).Cast<ObjectId>()
                : new[] { it.FrameId };
            foreach (var id in ids)
                if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br)
                    foreach (ObjectId aid in br.AttributeCollection)
                    {
                        var ar = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                        if (ar.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase))
                        {
                            if (mode == OpenMode.ForWrite) ar.UpgradeOpen();
                            return ar;
                        }
                    }
            return null;
        }

        // ---------------- text có sẵn & text liên kết ----------------
        private class TBox { public ObjectId Id; public Extents3d Ext; }

        private static List<TBox> TextBoxes(Transaction tr, ObjectId spaceId)
        {
            var r = new List<TBox>();
            var btr = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                if (id.ObjectClass.DxfName != "TEXT" && id.ObjectClass.DxfName != "MTEXT") continue;
                var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                if (TryExtents(e, out var ext)) r.Add(new TBox { Id = id, Ext = ext });
            }
            return r;
        }

        private static ObjectId TextAt(List<TBox> boxes, Point3d p)
        {
            ObjectId best = ObjectId.Null; double ba = double.MaxValue;
            foreach (var b in boxes)
            {
                if (b.Id.IsErased) continue;
                double h = b.Ext.MaxPoint.Y - b.Ext.MinPoint.Y, mg = 0.15 * h;
                if (p.X < b.Ext.MinPoint.X - mg || p.X > b.Ext.MaxPoint.X + mg ||
                    p.Y < b.Ext.MinPoint.Y - mg || p.Y > b.Ext.MaxPoint.Y + mg) continue;
                double a = (b.Ext.MaxPoint.X - b.Ext.MinPoint.X) * h;
                if (a < ba) { ba = a; best = b.Id; }
            }
            return best;
        }

        /// <summary>Đọc liên kết: khóa "handle|kind" → các entity mang xdata VXT_DSBV.</summary>
        private static Dictionary<string, List<ObjectId>> LinkMap(Transaction tr, IEnumerable<ObjectId> spaceIds)
        {
            var map = new Dictionary<string, List<ObjectId>>();
            foreach (var sid in spaceIds.Distinct())
            {
                var btr = (BlockTableRecord)tr.GetObject(sid, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    using (var rb = e.GetXDataForApplication(App))
                    {
                        if (rb == null) continue;
                        var v = rb.AsArray().Where(t => t.TypeCode == 1000).Select(t => t.Value as string).ToArray();
                        if (v.Length < 2) continue;
                        string key = v[1] + "|" + v[0];
                        if (!map.TryGetValue(key, out var l)) map[key] = l = new List<ObjectId>();
                        l.Add(id);
                    }
                }
            }
            return map;
        }

        private static void EnsureRegApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(App)) return;
            rat.UpgradeOpen();
            var r = new RegAppTableRecord { Name = App };
            rat.Add(r);
            tr.AddNewlyCreatedDBObject(r, true);
        }

        private static void Link(Entity e, string handle, string kind)
        {
            e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                new TypedValue(1000, kind),
                new TypedValue(1000, handle));
        }

        public static void EnsureLayer(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var l = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, 2) };
            lt.Add(l);
            tr.AddNewlyCreatedDBObject(l, true);
        }

        private static void SetText(Entity e, string s)
        {
            if (PlainText(e) == Clean(s)) return;   // không đổi -> giữ nguyên định dạng
            e.UpgradeOpen();
            if (e is MText mt) mt.Contents = s.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");
            else if (e is DBText t) t.TextString = s;
        }

        // ---------------- tên mặc định ----------------
        /// <summary>Điền tên mặc định cho các dòng chưa sửa tay: thuộc tính → text đã liên kết → text tại điểm tên → tên sheet Revit / tên layout.</summary>
        public static void FillDefaultNames(Database db, List<SheetItem> items, DanhSoSettings s)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var links = LinkMap(tr, items.Select(i => i.SpaceId));
                var boxCache = new Dictionary<ObjectId, List<TBox>>();
                foreach (var it in items)
                {
                    if (it.NameEdited) continue;
                    string name = null;
                    if (s.NameMode == PosMode.Attribute && s.NameTag != "")
                        name = FindAtt(tr, it, s.NameTag, OpenMode.ForRead)?.TextString;
                    if (string.IsNullOrEmpty(name) && links.TryGetValue(it.Handle + "|NAME", out var l))
                    {
                        var id = l.FirstOrDefault(x => !x.IsErased);
                        if (!id.IsNull) name = PlainText((Entity)tr.GetObject(id, OpenMode.ForRead));
                    }
                    if (string.IsNullOrEmpty(name) && s.NameMode == PosMode.Point)
                    {
                        Point3d? p = it.IsLayout ? (s.HasNamePt ? s.NamePt : (Point3d?)null)
                                                 : (s.HasNamePt ? it.AtUV(s.NameUV) : (Point3d?)null);
                        if (p.HasValue)
                        {
                            if (!boxCache.TryGetValue(it.SpaceId, out var boxes))
                                boxCache[it.SpaceId] = boxes = TextBoxes(tr, it.SpaceId);
                            var tid = TextAt(boxes, p.Value);
                            if (!tid.IsNull) name = PlainText((Entity)tr.GetObject(tid, OpenMode.ForRead));
                        }
                    }
                    if (string.IsNullOrEmpty(name))
                        name = it.IsLayout ? LayoutBaseName(it.SpaceName, s) : RevitSheetName(it.Info);
                    it.Name = Clean(name ?? "");
                }
                tr.Commit();
            }
        }

        private static string LayoutBaseName(string layoutName, DanhSoSettings s)
        {
            if (s.Prefix != "" && layoutName.StartsWith(s.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                int p = layoutName.IndexOf(" - ", StringComparison.Ordinal);
                return p > 0 ? layoutName.Substring(p + 3) : "";
            }
            return layoutName;
        }

        // ---------------- thực hiện ----------------
        /// <summary>Điền số/tên cho các bản vẽ đã chọn. Trả về thông báo kết quả.</summary>
        public static string Apply(Database db, List<SheetItem> items, DanhSoSettings s)
        {
            var use = items.Where(i => i.Use).ToList();
            int nNew = 0, nUpd = 0, nAtt = 0;
            var warn = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureRegApp(tr, db);
                EnsureLayer(tr, db, s.Layer);
                var links = LinkMap(tr, use.Select(i => i.SpaceId));
                var boxCache = new Dictionary<ObjectId, List<TBox>>();
                double h0 = use.Count > 0 ? use[0].H : 1;

                foreach (var it in use)
                {
                    double ht = s.TextHeight > 0
                        ? (it.IsLayout ? s.TextHeight : s.TextHeight * it.H / h0)
                        : (it.IsLayout ? 3.5 : 0.012 * it.H);
                    if (!boxCache.TryGetValue(it.SpaceId, out var boxes))
                        boxCache[it.SpaceId] = boxes = TextBoxes(tr, it.SpaceId);

                    Do("NUM", s.NumMode, s.NumTag, s.HasNumPt, it.IsLayout ? s.NumPt : it.AtUV(s.NumUV), it.Number);
                    Do("NAME", s.NameMode, s.NameTag, s.HasNamePt, it.IsLayout ? s.NamePt : it.AtUV(s.NameUV), it.Name);

                    void Do(string kind, PosMode mode, string tag, bool hasPt, Point3d pt, string str)
                    {
                        if (mode == PosMode.Attribute)
                        {
                            var ar = FindAtt(tr, it, tag, OpenMode.ForWrite);
                            if (ar != null) { ar.TextString = str; nAtt++; }
                            else warn.Add($"{it.Info}: không có thuộc tính {tag}");
                            return;
                        }
                        if (mode != PosMode.Point || !hasPt) return;
                        string key = it.Handle + "|" + kind;
                        var live = links.TryGetValue(key, out var l) ? l.Where(x => !x.IsErased).ToList() : new List<ObjectId>();
                        if (live.Count > 0)
                        {
                            SetText((Entity)tr.GetObject(live[0], OpenMode.ForRead), str);
                            foreach (var x in live.Skip(1)) tr.GetObject(x, OpenMode.ForWrite).Erase();
                            nUpd++;
                            return;
                        }
                        var tid = TextAt(boxes, pt);
                        if (!tid.IsNull)
                        {
                            var e = (Entity)tr.GetObject(tid, OpenMode.ForRead);
                            SetText(e, str);
                            if (!e.IsWriteEnabled) e.UpgradeOpen();
                            Link(e, it.Handle, kind);
                            links[key] = new List<ObjectId> { tid };
                            nUpd++;
                            return;
                        }
                        if (string.IsNullOrEmpty(str)) return;
                        var btr = (BlockTableRecord)tr.GetObject(it.SpaceId, OpenMode.ForWrite);
                        var t = new DBText
                        {
                            TextString = str, Height = ht, Layer = s.Layer, TextStyleId = db.Textstyle,
                            Position = pt, HorizontalMode = TextHorizontalMode.TextCenter,
                            VerticalMode = TextVerticalMode.TextVerticalMid, AlignmentPoint = pt
                        };
                        btr.AppendEntity(t);
                        tr.AddNewlyCreatedDBObject(t, true);
                        t.AdjustAlignment(db);
                        Link(t, it.Handle, kind);
                        links[key] = new List<ObjectId> { t.ObjectId };
                        nNew++;
                    }
                }
                tr.Commit();
            }

            string msg = $"Đã đánh số {use.Count} bản vẽ ({use.FirstOrDefault()?.Number} … {use.LastOrDefault()?.Number}).\n" +
                         $"Cập nhật text có sẵn: {nUpd}, tạo text mới: {nNew}, thuộc tính: {nAtt}.";
            if (s.LayoutMode && s.RenameLayouts) msg += "\n" + RenameLayouts(db, use, s);
            if (warn.Count > 0) msg += "\nLưu ý:\n  " + string.Join("\n  ", warn.Take(10));
            return msg;
        }

        private static string RenameLayouts(Database db, List<SheetItem> items, DanhSoSettings s)
        {
            var lm = Autodesk.AutoCAD.DatabaseServices.LayoutManager.Current;
            var bad = new Regex(@"[<>/\\"":;?*|,=`]");
            int n = 0, i = 0;
            // đổi qua tên tạm để tránh trùng
            foreach (var it in items)
                try { lm.RenameLayout(it.SpaceName, "~vxtdsb" + (++i)); it.SpaceName = "~vxtdsb" + i; } catch { }
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry de in dict) used.Add(de.Key);
                tr.Commit();
            }
            foreach (var it in items)
            {
                string nm = s.RenameFmt == 0 ? it.Number
                          : s.RenameFmt == 2 ? (it.Name != "" ? it.Name : it.Number)
                          : (it.Name != "" ? it.Number + " - " + it.Name : it.Number);
                nm = bad.Replace(nm, "-").Trim();
                if (nm.Length > 250) nm = nm.Substring(0, 250);
                if (nm == "") nm = "Layout";
                string final = nm; int k = 1;
                while (used.Contains(final)) final = nm + " (" + (++k) + ")";
                try { lm.RenameLayout(it.SpaceName, final); used.Remove(it.SpaceName); used.Add(final); it.SpaceName = final; n++; }
                catch { }
            }
            return $"Đã đổi tên {n} tab layout.";
        }

        // ---------------- danh mục ----------------
        public static string CatalogKey(DanhSoSettings s) => "DM:" + s.Prefix.ToUpperInvariant() + "|" + s.Suffix.ToUpperInvariant();

        /// <summary>Tìm bảng danh mục đã tạo trước (cùng tiền tố) → (vị trí, không gian).</summary>
        public static bool FindCatalog(Database db, string key, out Point3d pos, out ObjectId spaceId, out ObjectId tableId)
        {
            pos = Point3d.Origin; spaceId = ObjectId.Null; tableId = ObjectId.Null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId bid in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(bid, OpenMode.ForRead);
                    if (!btr.IsLayout) continue;
                    foreach (ObjectId id in btr)
                    {
                        if (id.ObjectClass.DxfName != "ACAD_TABLE") continue;
                        var tb = (Table)tr.GetObject(id, OpenMode.ForRead);
                        using (var rb = tb.GetXDataForApplication(App))
                        {
                            if (rb == null) continue;
                            var v = rb.AsArray().Where(t => t.TypeCode == 1000).Select(t => t.Value as string).ToArray();
                            if (v.Length >= 2 && v[0] == "TABLE" && v[1] == key)
                            {
                                pos = tb.Position; spaceId = bid; tableId = id;
                                tr.Commit();
                                return true;
                            }
                        }
                    }
                }
                tr.Commit();
            }
            return false;
        }

        /// <summary>Tạo bảng DANH MỤC BẢN VẼ (thay bảng cũ cùng khóa nếu có).</summary>
        public static void MakeTable(Database db, ObjectId spaceId, Point3d pos, List<SheetItem> items, double ht, DanhSoSettings s, ObjectId oldTable)
        {
            var use = items.Where(i => i.Use).ToList();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureRegApp(tr, db);
                EnsureLayer(tr, db, s.Layer);
                if (!oldTable.IsNull && !oldTable.IsErased) tr.GetObject(oldTable, OpenMode.ForWrite).Erase();
                var btr = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForWrite);
                var tb = new Table();
                tb.SetDatabaseDefaults(db);
                tb.TableStyle = db.Tablestyle;
                tb.SetSize(use.Count + 2, 4);
                tb.Position = pos;
                tb.Layer = s.Layer;
                double[] cw = { 6, 16, 50, 16 };
                for (int c = 0; c < 4; c++) tb.Columns[c].Width = cw[c] * ht;
                for (int r = 0; r < use.Count + 2; r++) tb.Rows[r].Height = 2.2 * ht;
                try { tb.MergeCells(CellRange.Create(tb, 0, 0, 0, 3)); } catch { }
                SetCell(0, 0, "DANH MỤC BẢN VẼ", 1.4 * ht, CellAlignment.MiddleCenter);
                string[] hd = { "STT", "SỐ HIỆU BẢN VẼ", "TÊN BẢN VẼ", "GHI CHÚ" };
                for (int c = 0; c < 4; c++) SetCell(1, c, hd[c], ht, CellAlignment.MiddleCenter);
                for (int i = 0; i < use.Count; i++)
                {
                    SetCell(i + 2, 0, (i + 1).ToString(), ht, CellAlignment.MiddleCenter);
                    SetCell(i + 2, 1, use[i].Number, ht, CellAlignment.MiddleCenter);
                    SetCell(i + 2, 2, use[i].Name, ht, CellAlignment.MiddleLeft);
                    SetCell(i + 2, 3, "", ht, CellAlignment.MiddleCenter);
                }
                tb.GenerateLayout();
                btr.AppendEntity(tb);
                tr.AddNewlyCreatedDBObject(tb, true);
                Link(tb, CatalogKey(s), "TABLE");
                tr.Commit();

                void SetCell(int r, int c, string text, double h, CellAlignment al)
                {
                    tb.Cells[r, c].TextString = text ?? "";
                    tb.Cells[r, c].TextHeight = h;
                    tb.Cells[r, c].Alignment = al;
                }
            }
        }

        public static void WriteCsv(string path, List<SheetItem> items)
        {
            string Q(string v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
            var sb = new StringBuilder();
            sb.AppendLine("STT,Số hiệu bản vẽ,Tên bản vẽ,Ghi chú");
            int i = 0;
            foreach (var it in items.Where(x => x.Use))
                sb.AppendLine($"{++i},{Q(it.Number)},{Q(it.Name)},");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
