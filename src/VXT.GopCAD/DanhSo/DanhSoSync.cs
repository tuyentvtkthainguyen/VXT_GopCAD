using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace VXT.GopCAD.DanhSo
{
    /// <summary>
    /// Đồng bộ 2 chiều giữa chữ số/tên trong khung tên và bảng DANH MỤC BẢN VẼ:
    /// sửa tên/số ở khung tên -> bảng tự cập nhật; sửa ô trong bảng -> khung tên tự cập nhật.
    /// Chỉ theo dõi các đối tượng do VXT tạo/liên kết (xdata VXT_DSBV), không gửi dữ liệu đi đâu.
    /// </summary>
    public static class DanhSoSync
    {
        private static bool busy;
        private static readonly List<(Database Db, ObjectId Id, string Kind)> queue = new List<(Database, ObjectId, string)>();
        private static readonly HashSet<Database> attached = new HashSet<Database>();

        public static void Init()
        {
            foreach (Document d in AcApp.DocumentManager) Attach(d);
            AcApp.DocumentManager.DocumentCreated += (s, e) => Attach(e.Document);
            AcApp.DocumentManager.DocumentToBeDestroyed += (s, e) =>
            {
                var db = e.Document.Database;
                db.ObjectModified -= OnModified;
                attached.Remove(db);
                queue.RemoveAll(q => q.Db == db);
            };
            AcApp.Idle += OnIdle;
        }

        private static void Attach(Document d)
        {
            if (d == null || attached.Contains(d.Database)) return;
            d.Database.ObjectModified += OnModified;
            attached.Add(d.Database);
        }

        private static string[] XValues(DBObject o)
        {
            try
            {
                using (var rb = o.GetXDataForApplication(DanhSoEngine.App))
                {
                    if (rb == null) return null;
                    return rb.AsArray().Where(t => t.TypeCode == 1000).Select(t => t.Value as string).ToArray();
                }
            }
            catch { return null; }
        }

        private static void OnModified(object sender, ObjectEventArgs e)
        {
            if (busy) return;
            var o = e.DBObject;
            if (o == null || o.IsErased) return;
            if (!(o is MText || o is DBText || o is Table)) return;
            var v = XValues(o);
            if (v == null || v.Length < 2) return;
            string kind = v[0];
            if (kind != "NUM" && kind != "NAME" && kind != "TABLE") return;
            var db = (Database)sender;
            if (!queue.Any(q => q.Id == o.ObjectId)) queue.Add((db, o.ObjectId, kind));
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            if (busy || queue.Count == 0) return;
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null || !string.IsNullOrEmpty(doc.CommandInProgress)) return;
            var mine = queue.Where(q => q.Db == doc.Database).ToList();
            if (mine.Count == 0) return;
            queue.RemoveAll(q => q.Db == doc.Database);
            busy = true;
            int n = 0;
            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var tables = Tables(tr, doc.Database);
                    Dictionary<string, List<ObjectId>> links = null;
                    foreach (var q in mine)
                    {
                        if (q.Id.IsErased || !q.Id.IsValid) continue;
                        if (q.Kind == "TABLE")
                        {
                            if (links == null) links = DanhSoEngine.LinkMap(tr, LayoutSpaces(tr, doc.Database));
                            n += TableToTexts(tr, q.Id, links);
                        }
                        else n += TextToTables(tr, q.Id, q.Kind, tables);
                    }
                    tr.Commit();
                }
                if (n > 0) doc.Editor.WriteMessage($"\nVXT: đã đồng bộ {n} ô số/tên giữa khung tên và danh mục bản vẽ.");
            }
            catch (Exception ex) { doc.Editor.WriteMessage("\nVXT đồng bộ danh mục: " + ex.Message); }
            finally { busy = false; }
        }

        private static List<ObjectId> LayoutSpaces(Transaction tr, Database db)
        {
            var r = new List<ObjectId>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in bt)
                if (((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).IsLayout) r.Add(id);
            return r;
        }

        private static List<ObjectId> Tables(Transaction tr, Database db)
        {
            var r = new List<ObjectId>();
            foreach (var sid in LayoutSpaces(tr, db))
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(sid, OpenMode.ForRead))
                {
                    if (id.ObjectClass.DxfName != "ACAD_TABLE") continue;
                    var v = XValues(tr.GetObject(id, OpenMode.ForRead));
                    if (v != null && v.Length >= 2 && v[0] == "TABLE") r.Add(id);
                }
            return r;
        }

        private static string CellText(Table tb, int r, int c)
        {
            string raw = tb.Cells[r, c].TextString ?? "";
            using (var m = new MText { Contents = raw }) return DanhSoEngine.Clean(m.Text);
        }

        private static void SetCell(Table tb, int r, int c, string s)
        {
            if (CellText(tb, r, c) == DanhSoEngine.Clean(s)) return;
            if (!tb.IsWriteEnabled) tb.UpgradeOpen();
            tb.Cells[r, c].TextString = s;
        }

        /// <summary>Chữ số/tên trong khung tên đổi -> cập nhật ô tương ứng trong bảng danh mục.</summary>
        private static int TextToTables(Transaction tr, ObjectId textId, string kind, List<ObjectId> tables)
        {
            var e = (Entity)tr.GetObject(textId, OpenMode.ForRead);
            var v = XValues(e);
            if (v == null || v.Length < 2) return 0;
            string handle = v[1], txt = DanhSoEngine.PlainText(e);
            int col = kind == "NUM" ? 1 : 2, n = 0;
            foreach (var tid in tables)
            {
                var tb = (Table)tr.GetObject(tid, OpenMode.ForRead);
                var tv = XValues(tb);
                if (tv == null) continue;
                for (int i = 2; i < tv.Length; i++)
                {
                    int row = i - 2 + 2;   // 2 dòng đầu: tiêu đề + đầu cột
                    if (tv[i] != handle || row >= tb.Rows.Count) continue;
                    if (CellText(tb, row, col) != txt) { SetCell(tb, row, col, txt); n++; }
                }
            }
            return n;
        }

        /// <summary>Ô trong bảng danh mục đổi -> cập nhật chữ số/tên trong khung tên.</summary>
        private static int TableToTexts(Transaction tr, ObjectId tableId, Dictionary<string, List<ObjectId>> links)
        {
            var tb = (Table)tr.GetObject(tableId, OpenMode.ForRead);
            var tv = XValues(tb);
            if (tv == null) return 0;
            int n = 0;
            for (int i = 2; i < tv.Length; i++)
            {
                int row = i - 2 + 2;
                if (row >= tb.Rows.Count) break;
                foreach (var (kind, col) in new[] { ("NUM", 1), ("NAME", 2) })
                {
                    string val = CellText(tb, row, col);
                    if (!links.TryGetValue(tv[i] + "|" + kind, out var ids)) continue;
                    foreach (var id in ids.Where(x => !x.IsErased))
                    {
                        var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        if (DanhSoEngine.PlainText(e) == val) continue;
                        DanhSoEngine.SetText(e, val);
                        n++;
                    }
                }
            }
            return n;
        }
    }
}
