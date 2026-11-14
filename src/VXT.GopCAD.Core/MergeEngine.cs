using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace VXT.GopCAD.Core
{
    /// <summary>
    /// Lõi gộp nhiều file DWG thành 1 file. Đọc từng file bằng side-database (không mở lên màn hình)
    /// nên chạy nhanh và chạy được cả trong AutoCAD Core Console (accoreconsole.exe).
    /// Không ghi/gửi bất kỳ dữ liệu người dùng nào ra ngoài.
    /// </summary>
    public class MergeEngine
    {
        /// <summary>(chỉ số file, tổng số file, mô tả)</summary>
        public event Action<int, int, string> Progress;
        public Func<bool> IsCancelled;

        private class PlaceState
        {
            public double X, RowTop, RowMaxH, Gap = -1;
            public int Col;
        }

        internal class SrcLayout
        {
            public ObjectId Id;
            public string Name;
            public int Tab;
            public int EntityCount;
        }

        public MergeResult Run(MergeOptions o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            if (o.Items.Count == 0) throw new ArgumentException("Chưa có file nào để gộp.");
            if (string.IsNullOrWhiteSpace(o.OutputPath)) throw new ArgumentException("Chưa chọn file kết quả.");

            string outFull = Path.GetFullPath(o.OutputPath);
            foreach (var it in o.Items)
                if (string.Equals(Path.GetFullPath(it.FilePath), outFull, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("File kết quả trùng với một file nguồn: " + it.FilePath);

            var res = new MergeResult();
            var sw = Stopwatch.StartNew();
            string dir = Path.GetDirectoryName(outFull);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            Database prev = HostApplicationServices.WorkingDatabase;
            using (var outDb = new Database(true, true))
            {
                try
                {
                    // LayoutManager làm việc trên WorkingDatabase nên phải chuyển sang file gộp.
                    HostApplicationServices.WorkingDatabase = outDb;
                    var defaultLayouts = GetPaperLayoutIds(outDb);
                    var st = new PlaceState();
                    var usedLayoutNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int imported = 0;
                    bool first = true;

                    for (int i = 0; i < o.Items.Count; i++)
                    {
                        if (IsCancelled != null && IsCancelled())
                        {
                            res.Warnings.Add("Đã dừng theo yêu cầu, chỉ gộp " + res.Ok + " file đầu.");
                            break;
                        }
                        var it = o.Items[i];
                        Progress?.Invoke(i, o.Items.Count, Path.GetFileName(it.FilePath));
                        try
                        {
                            imported += MergeOne(outDb, it, o, st, first, usedLayoutNames, res);
                            first = false;
                            res.Ok++;
                        }
                        catch (System.Exception ex)
                        {
                            res.Errors.Add(Path.GetFileName(it.FilePath) + ": " + ex.Message);
                        }
                    }

                    if (res.Ok == 0)
                        throw new InvalidOperationException("Không gộp được file nào.\n" + string.Join("\n", res.Errors));

                    Progress?.Invoke(o.Items.Count, o.Items.Count, "Đang lưu file...");
                    if (imported > 0 && o.RemoveDefaultLayouts) DeleteLayouts(outDb, defaultLayouts);
                    outDb.SaveAs(outFull, DwgVersion.Current);
                }
                finally
                {
                    if (prev != null) HostApplicationServices.WorkingDatabase = prev;
                }
            }

            res.OutputPath = outFull;
            res.Elapsed = sw.Elapsed;
            return res;
        }

        private int MergeOne(Database outDb, MergeItem it, MergeOptions o, PlaceState st, bool first,
                             HashSet<string> usedLayoutNames, MergeResult res)
        {
            if (!File.Exists(it.FilePath)) throw new FileNotFoundException("Không tìm thấy file.");
            string baseName = Path.GetFileNameWithoutExtension(it.FilePath);

            using (var sdb = new Database(false, true))
            {
                sdb.ReadDwgFile(it.FilePath, FileOpenMode.OpenForReadAndAllShare, true, "");
                sdb.CloseInput(true);

                if (first) CopyHeader(sdb, outDb);
                else if (sdb.Insunits != outDb.Insunits)
                    res.Warnings.Add(Path.GetFileName(it.FilePath) + ": đơn vị (" + sdb.Insunits +
                                     ") khác file đầu (" + outDb.Insunits + ").");

                if (o.SheetToModel) SheetFlattener.Flatten(sdb, Path.GetFileName(it.FilePath), res);
                if (o.LayerPrefix) PrefixLayers(sdb, Names.CleanSymbol(baseName));

                CollectModel(sdb, out ObjectIdCollection msIds, out Extents3d? ext);

                // ---- Tính độ dời ----
                var disp = new Vector3d(0, 0, 0);
                double th = 2.5;
                if (ext.HasValue)
                {
                    var e = ext.Value;
                    double w = e.MaxPoint.X - e.MinPoint.X;
                    double h = e.MaxPoint.Y - e.MinPoint.Y;
                    th = Math.Max(w, h) * 0.02;
                    if (th <= 0) th = 2.5;

                    if (o.Placement == PlacementMode.XepHang)
                    {
                        if (st.Gap < 0)
                        {
                            st.Gap = o.Gap > 0 ? o.Gap : Math.Max(w, h) * 0.1;
                            if (st.Gap <= 0) st.Gap = 1000;
                        }
                        if (o.Columns > 0 && st.Col >= o.Columns)
                        {
                            st.RowTop -= st.RowMaxH + st.Gap;
                            st.X = 0; st.Col = 0; st.RowMaxH = 0;
                        }
                        // Góc trên-trái của bản vẽ đặt vào con trỏ (X, RowTop)
                        disp = new Vector3d(st.X - e.MinPoint.X, st.RowTop - e.MaxPoint.Y, 0);
                        st.X += w + st.Gap;
                        st.Col++;
                        st.RowMaxH = Math.Max(st.RowMaxH, h + (o.Label ? th * 3 : 0));
                    }
                }

                var dup = o.MangleDuplicates ? DuplicateRecordCloning.MangleName : DuplicateRecordCloning.Ignore;
                ObjectId outMs = SymbolUtilityServices.GetBlockModelSpaceId(outDb);

                // ---- Model ----
                if (msIds.Count > 0)
                {
                    if (o.AsBlock) InsertAsBlock(sdb, outDb, outMs, baseName, disp);
                    else CloneModel(sdb, msIds, outDb, outMs, disp, dup);
                }

                if (o.Label && ext.HasValue)
                {
                    string text = string.IsNullOrWhiteSpace(it.Label) ? baseName : it.Label;
                    var p = new Point3d(ext.Value.MinPoint.X + disp.X, ext.Value.MinPoint.Y + disp.Y - th * 2, 0);
                    AddLabel(outDb, outMs, text, p, th);
                }

                // ---- Layout ----
                int n = 0;
                if (o.Layouts != LayoutImportMode.KhongLay && !o.SheetToModel)
                {
                    var all = GetSourceLayouts(sdb);
                    List<SrcLayout> pick;
                    if (o.Layouts == LayoutImportMode.LayoutDau)
                    {
                        var one = all.FirstOrDefault(l => l.EntityCount > 1) ?? all.FirstOrDefault(l => l.EntityCount > 0);
                        pick = one == null ? new List<SrcLayout>() : new List<SrcLayout> { one };
                    }
                    else pick = all.Where(l => l.EntityCount > 1).ToList();

                    for (int k = 0; k < pick.Count; k++)
                    {
                        string want;
                        if (k == 0 && !string.IsNullOrWhiteSpace(it.LayoutName)) want = it.LayoutName;
                        else if (o.Layouts == LayoutImportMode.TatCa) want = baseName + " - " + pick[k].Name;
                        else want = baseName;
                        CopyLayout(sdb, pick[k].Id, outDb, want, disp, dup, usedLayoutNames);
                        n++;
                    }
                }
                return n;
            }
        }

        // ================= MODEL =================

        private static void CollectModel(Database sdb, out ObjectIdCollection ids, out Extents3d? ext)
        {
            ids = new ObjectIdCollection();
            ext = null;
            using (var tr = sdb.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(sdb), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    ids.Add(id);
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || !ent.Visible) continue;
                    try
                    {
                        var e = ent.GeometricExtents; // Xline/Ray sẽ báo lỗi -> bỏ qua
                        if (ext == null) ext = e;
                        else { var x = ext.Value; x.AddExtents(e); ext = x; }
                    }
                    catch { }
                }
                tr.Commit();
            }
        }

        private static void CloneModel(Database sdb, ObjectIdCollection ids, Database outDb, ObjectId outMs,
                                       Vector3d disp, DuplicateRecordCloning dup)
        {
            using (var map = new IdMapping())
            {
                sdb.WblockCloneObjects(ids, outMs, map, dup, false);
                if (disp.Length < 1e-9) return;
                var mat = Matrix3d.Displacement(disp);
                using (var tr = outDb.TransactionManager.StartTransaction())
                {
                    foreach (IdPair p in map)
                    {
                        if (!p.IsPrimary || !p.IsCloned) continue;
                        // forceOpenOnLockedLayer = true: đối tượng trên layer khóa vẫn dời được
                        var ent = tr.GetObject(p.Value, OpenMode.ForWrite, false, true) as Entity;
                        if (ent == null) continue;
                        try { ent.TransformBy(mat); } catch { }
                    }
                    tr.Commit();
                }
            }
        }

        private static void InsertAsBlock(Database sdb, Database outDb, ObjectId outMs, string baseName, Vector3d disp)
        {
            string name = UniqueBlockName(outDb, Names.CleanSymbol(baseName));
            Point3d basePt = sdb.Insbase;
            ObjectId btrId = outDb.Insert(name, sdb, true);
            using (var tr = outDb.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(outMs, OpenMode.ForWrite);
                var br = new BlockReference(basePt + disp, btrId);
                br.SetDatabaseDefaults(outDb);
                br.Layer = "0";
                ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                tr.Commit();
            }
        }

        private static void AddLabel(Database db, ObjectId ms, string text, Point3d pos, double h)
        {
            const string LN = "VXT_TEN_BAN_VE";
            const string TS = "VXT_ARIAL";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(LN))
                {
                    lt.UpgradeOpen();
                    var l = new LayerTableRecord
                    {
                        Name = LN,
                        Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 2)
                    };
                    lt.Add(l);
                    tr.AddNewlyCreatedDBObject(l, true);
                }

                var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                ObjectId styleId;
                if (tst.Has(TS)) styleId = tst[TS];
                else
                {
                    tst.UpgradeOpen();
                    var s = new TextStyleTableRecord { Name = TS, FileName = "arial.ttf" };
                    try { s.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Arial", false, false, 0, 0); } catch { }
                    styleId = tst.Add(s);
                    tr.AddNewlyCreatedDBObject(s, true);
                }

                var t = new DBText();
                t.SetDatabaseDefaults(db);
                t.Layer = LN;
                t.TextStyleId = styleId;
                t.Height = h;
                t.Position = pos;
                t.TextString = text;
                var btr = (BlockTableRecord)tr.GetObject(ms, OpenMode.ForWrite);
                btr.AppendEntity(t);
                tr.AddNewlyCreatedDBObject(t, true);
                tr.Commit();
            }
        }

        // ================= LAYOUT =================

        internal static List<SrcLayout> GetSourceLayouts(Database sdb)
        {
            var list = new List<SrcLayout>();
            using (var tr = sdb.TransactionManager.StartOpenCloseTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(sdb.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry de in dict)
                {
                    var l = tr.GetObject(de.Value, OpenMode.ForRead) as Layout;
                    if (l == null || l.ModelType) continue;
                    int cnt = 0;
                    var btr = (BlockTableRecord)tr.GetObject(l.BlockTableRecordId, OpenMode.ForRead);
                    foreach (ObjectId _ in btr) cnt++;
                    list.Add(new SrcLayout { Id = de.Value, Name = l.LayoutName, Tab = l.TabOrder, EntityCount = cnt });
                }
                tr.Commit();
            }
            return list.OrderBy(x => x.Tab).ToList();
        }

        /// <summary>
        /// Chép 1 layout sang file gộp: tạo layout mới, chép thiết lập in, chép toàn bộ đối tượng
        /// paper space, rồi dời ViewTarget của các viewport đúng bằng độ dời của Model
        /// để viewport vẫn nhìn đúng bản vẽ của nó.
        /// </summary>
        private static void CopyLayout(Database sdb, ObjectId srcLayoutId, Database outDb, string wantName,
                                       Vector3d disp, DuplicateRecordCloning dup, HashSet<string> used)
        {
            string name = UniqueLayoutName(outDb, Names.CleanLayout(wantName), used);
            var ids = new ObjectIdCollection();
            ObjectId overallId = ObjectId.Null;
            ObjectId newBtrId;

            using (var ps = new PlotSettings(false))
            {
                using (var tr = sdb.TransactionManager.StartOpenCloseTransaction())
                {
                    var sl = (Layout)tr.GetObject(srcLayoutId, OpenMode.ForRead);
                    try { ps.CopyFrom(sl); } catch { }
                    var btr = (BlockTableRecord)tr.GetObject(sl.BlockTableRecordId, OpenMode.ForRead);
                    foreach (ObjectId id in btr) ids.Add(id);
                    overallId = FindOverallViewport(tr, sl, btr);
                    tr.Commit();
                }

                ObjectId newLayoutId = LayoutManager.Current.CreateLayout(name);
                used.Add(name);
                using (var tr = outDb.TransactionManager.StartTransaction())
                {
                    var nl = (Layout)tr.GetObject(newLayoutId, OpenMode.ForWrite);
                    try { nl.CopyFrom(ps); } catch { }
                    if (nl.LayoutName != name) { try { nl.LayoutName = name; } catch { } }
                    newBtrId = nl.BlockTableRecordId;
                    tr.Commit();
                }
            }

            if (ids.Count == 0) return;
            using (var map = new IdMapping())
            {
                sdb.WblockCloneObjects(ids, newBtrId, map, dup, false);
                if (disp.Length < 1e-9) return;
                var vpClass = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Viewport));
                using (var tr = outDb.TransactionManager.StartTransaction())
                {
                    foreach (IdPair p in map)
                    {
                        if (!p.IsPrimary || !p.IsCloned || p.Key == overallId) continue;
                        if (!p.Value.ObjectClass.IsDerivedFrom(vpClass)) continue;
                        var vp = (Viewport)tr.GetObject(p.Value, OpenMode.ForWrite, false, true);
                        vp.ViewTarget = vp.ViewTarget + disp;
                    }
                    tr.Commit();
                }
            }
        }

        /// <summary>
        /// Tìm viewport tổng của paper space (không được dời). Ưu tiên Layout.GetViewports();
        /// nếu không có thì coi viewport đầu tiên là viewport tổng khi nó bao trùm mọi nội dung paper space.
        /// </summary>
        internal static ObjectId FindOverallViewport(Transaction tr, Layout sl, BlockTableRecord btr)
        {
            try
            {
                var vps = sl.GetViewports();
                if (vps != null && vps.Count > 0) return vps[0];
            }
            catch { }

            Viewport firstVp = null;
            Extents3d? other = null;
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                Extents3d e;
                if (ent is Viewport vp)
                {
                    if (firstVp == null) { firstVp = vp; continue; }
                    e = Rect(vp);
                }
                else
                {
                    try { e = ent.GeometricExtents; } catch { continue; }
                }
                if (other == null) other = e;
                else { var x = other.Value; x.AddExtents(e); other = x; }
            }
            if (firstVp == null || other == null) return ObjectId.Null;

            var r = Rect(firstVp);
            var o = other.Value;
            double tol = Math.Max(r.MaxPoint.X - r.MinPoint.X, r.MaxPoint.Y - r.MinPoint.Y) * 0.01;
            bool contains = r.MinPoint.X <= o.MinPoint.X + tol && r.MinPoint.Y <= o.MinPoint.Y + tol &&
                            r.MaxPoint.X >= o.MaxPoint.X - tol && r.MaxPoint.Y >= o.MaxPoint.Y - tol;
            return contains ? firstVp.ObjectId : ObjectId.Null;
        }

        internal static Extents3d Rect(Viewport vp)
        {
            var c = vp.CenterPoint;
            double hw = vp.Width / 2, hh = vp.Height / 2;
            return new Extents3d(new Point3d(c.X - hw, c.Y - hh, 0), new Point3d(c.X + hw, c.Y + hh, 0));
        }

        private static List<ObjectId> GetPaperLayoutIds(Database db)
        {
            var list = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry de in dict)
                {
                    var l = tr.GetObject(de.Value, OpenMode.ForRead) as Layout;
                    if (l != null && !l.ModelType) list.Add(de.Value);
                }
                tr.Commit();
            }
            return list;
        }

        private static void DeleteLayouts(Database db, List<ObjectId> ids)
        {
            var names = new List<string>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var id in ids)
                {
                    if (id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is Layout l) names.Add(l.LayoutName);
                }
                tr.Commit();
            }
            foreach (var n in names)
            {
                try { LayoutManager.Current.DeleteLayout(n); } catch { }
            }
        }

        private static string UniqueLayoutName(Database db, string name, HashSet<string> used)
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry de in dict) existing.Add(de.Key);
                tr.Commit();
            }
            string n = name;
            int k = 2;
            while (existing.Contains(n) || used.Contains(n) || n.Equals("Model", StringComparison.OrdinalIgnoreCase))
            {
                string suf = " (" + (k++) + ")";
                n = (name.Length + suf.Length > 255 ? name.Substring(0, 255 - suf.Length) : name) + suf;
            }
            return n;
        }

        private static string UniqueBlockName(Database db, string name)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                string n = name;
                int k = 2;
                while (bt.Has(n)) n = name + "_" + (k++);
                tr.Commit();
                return n;
            }
        }

        // ================= KHÁC =================

        private static void CopyHeader(Database s, Database d)
        {
            try { d.Insunits = s.Insunits; } catch { }
            try { d.Measurement = s.Measurement; } catch { }
            try { d.Lunits = s.Lunits; d.Luprec = s.Luprec; } catch { }
            try { d.Ltscale = s.Ltscale; d.Psltscale = s.Psltscale; } catch { }
        }

        private static void PrefixLayers(Database sdb, string prefix)
        {
            using (var tr = sdb.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(sdb.LayerTableId, OpenMode.ForRead);
                var ids = new List<ObjectId>();
                foreach (ObjectId id in lt) ids.Add(id);
                foreach (var id in ids)
                {
                    var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (l.IsDependent || l.Name == "0" || l.Name.Equals("Defpoints", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string nn = prefix + "_" + l.Name;
                    if (nn.Length > 255) nn = nn.Substring(0, 255);
                    if (lt.Has(nn)) continue;
                    try { l.UpgradeOpen(); l.Name = nn; } catch { }
                }
                tr.Commit();
            }
        }
    }

    internal static class Names
    {
        private const string Bad = "<>/\\\":;?*|,=`";

        public static string CleanSymbol(string s) => Clean(s, 200, "VXT");
        public static string CleanLayout(string s) => Clean(s, 255, "Layout");

        private static string Clean(string s, int max, string fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsControl(c)) continue;
                sb.Append(Bad.IndexOf(c) >= 0 ? '_' : c);
            }
            string r = sb.ToString().Trim();
            if (r.Length == 0) r = fallback;
            if (r.Length > max) r = r.Substring(0, max).Trim();
            return r;
        }
    }
}
