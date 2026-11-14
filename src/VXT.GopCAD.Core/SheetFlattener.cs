using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace VXT.GopCAD.Core
{
    /// <summary>
    /// Đưa 1 sheet (file DWG xuất từ Revit) xuống Model theo kiểu vẽ CAD truyền thống:
    ///  - Bản vẽ của viewport chính (viewport lớn nhất) giữ nguyên kích thước thật 1:1.
    ///  - Khung tên và mọi nội dung trên Layout được phóng theo tỷ lệ viewport chính
    ///    (1/100 → ×100) và đặt đúng vị trí như trên sheet.
    ///  - Viewport phụ cùng tỷ lệ: dời về đúng chỗ trong khung tên, giữ 1:1.
    ///  - Viewport phụ khác tỷ lệ (chi tiết 1/20 trên sheet 1/100): phóng hình cho đúng
    ///    kích thước in, và chỉnh hệ số đo kích thước (DIMLFAC) để con số dim vẫn đúng giá trị thật.
    /// Chỉ sửa bản sao trong bộ nhớ, không ghi đè file nguồn.
    /// </summary>
    internal static class SheetFlattener
    {
        private class Vp
        {
            public Point3d Cp;      // tâm viewport trên giấy
            public Point3d Mc;      // điểm Model hiện ở tâm viewport
            public double K;        // tỷ lệ: đơn vị giấy / đơn vị model (1/100 = 0.01)
            public double W, H;     // kích thước viewport trên giấy
            public double Area => W * H;
            public Extents3d ModelRect => new Extents3d(
                new Point3d(Mc.X - W / K / 2, Mc.Y - H / K / 2, 0),
                new Point3d(Mc.X + W / K / 2, Mc.Y + H / K / 2, 0));
            public Matrix3d ToOut;  // biến đổi nội dung Model của viewport này
            public double F = 1;    // hệ số phóng của nội dung viewport này
        }

        public static void Flatten(Database sdb, string fileName, MergeResult res)
        {
            var layouts = MergeEngine.GetSourceLayouts(sdb);
            var pick = layouts.FirstOrDefault(l => l.EntityCount > 1) ?? layouts.FirstOrDefault(l => l.EntityCount > 0);
            if (pick == null) return; // không có Layout -> giữ nguyên Model

            var vps = new List<Vp>();
            var paperIds = new ObjectIdCollection();
            bool hasTwist = false;

            using (var tr = sdb.TransactionManager.StartOpenCloseTransaction())
            {
                var sl = (Layout)tr.GetObject(pick.Id, OpenMode.ForRead);
                var psBtr = (BlockTableRecord)tr.GetObject(sl.BlockTableRecordId, OpenMode.ForRead);
                ObjectId overall = MergeEngine.FindOverallViewport(tr, sl, psBtr);
                foreach (ObjectId id in psBtr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    if (ent is Viewport vp)
                    {
                        if (id == overall || vp.CustomScale <= 1e-12 || vp.Width <= 0 || vp.Height <= 0) continue;
                        if (Math.Abs(vp.TwistAngle) > 1e-9 || !vp.ViewDirection.IsParallelTo(Vector3d.ZAxis)) hasTwist = true;
                        vps.Add(new Vp
                        {
                            Cp = vp.CenterPoint,
                            Mc = vp.ViewTarget + new Vector3d(vp.ViewCenter.X, vp.ViewCenter.Y, 0),
                            K = vp.CustomScale,
                            W = vp.Width,
                            H = vp.Height
                        });
                    }
                    else paperIds.Add(id);
                }
                tr.Commit();
            }
            if (hasTwist) res.Warnings.Add(fileName + ": có viewport xoay hoặc không nhìn từ trên xuống, vị trí có thể lệch.");

            // Viewport chính = viewport lớn nhất trên giấy
            var main = vps.OrderByDescending(v => v.Area).FirstOrDefault();
            double k = main?.K ?? 1.0;
            Point3d mc = main?.Mc ?? Point3d.Origin;
            Point3d cp = main?.Cp ?? Point3d.Origin;
            Point3d PaperToModel(Point3d p) => mc + (p - cp) * (1.0 / k);

            foreach (var v in vps)
            {
                v.F = v.K / k;
                Point3d target = PaperToModel(v.Cp);
                v.ToOut = Matrix3d.Displacement(target - v.Mc) * Matrix3d.Scaling(v.F, v.Mc);
            }

            // 1) Dời / phóng nội dung Model theo từng viewport
            if (vps.Count > 1 || (vps.Count == 1 && !IsIdentity(vps[0].ToOut)))
            {
                ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(sdb);
                using (var tr = sdb.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);
                    var ids = new List<ObjectId>();
                    foreach (ObjectId id in ms) ids.Add(id);
                    foreach (var id in ids)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        Point3d c;
                        try
                        {
                            var e = ent.GeometricExtents;
                            c = e.MinPoint + (e.MaxPoint - e.MinPoint) * 0.5;
                        }
                        catch { continue; }

                        Vp owner = null;
                        double best = double.MaxValue;
                        foreach (var v in vps)
                        {
                            var r = v.ModelRect;
                            double tol = Math.Max(r.MaxPoint.X - r.MinPoint.X, r.MaxPoint.Y - r.MinPoint.Y) * 0.01;
                            if (c.X < r.MinPoint.X - tol || c.X > r.MaxPoint.X + tol || c.Y < r.MinPoint.Y - tol || c.Y > r.MaxPoint.Y + tol) continue;
                            double a = (r.MaxPoint.X - r.MinPoint.X) * (r.MaxPoint.Y - r.MinPoint.Y);
                            if (a < best) { best = a; owner = v; }
                        }
                        if (owner == null || IsIdentity(owner.ToOut)) continue;
                        try
                        {
                            ent = (Entity)tr.GetObject(id, OpenMode.ForWrite, false, true);
                            ent.TransformBy(owner.ToOut);
                            AdjustScaled(ent, owner.F);
                        }
                        catch { }
                    }
                    tr.Commit();
                }
            }

            // 2) Chép khung tên + nội dung Layout xuống Model, phóng theo tỷ lệ viewport chính
            if (paperIds.Count > 0)
            {
                ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(sdb);
                var paperMat = Matrix3d.Displacement(mc - cp) * Matrix3d.Scaling(1.0 / k, cp);
                using (var map = new IdMapping())
                {
                    sdb.DeepCloneObjects(paperIds, msId, map, false);
                    using (var tr = sdb.TransactionManager.StartTransaction())
                    {
                        foreach (IdPair p in map)
                        {
                            if (!p.IsPrimary || !p.IsCloned) continue;
                            if (!(tr.GetObject(p.Value, OpenMode.ForWrite, false, true) is Entity ent)) continue;
                            try
                            {
                                ent.TransformBy(paperMat);
                                AdjustScaled(ent, 1.0 / k);
                            }
                            catch { }
                        }
                        tr.Commit();
                    }
                }
            }
        }

        /// <summary>Sau khi phóng đối tượng f lần: giữ mẫu nét đứt cân đối và con số kích thước đúng giá trị thật.</summary>
        private static void AdjustScaled(Entity ent, double f)
        {
            if (Math.Abs(f - 1) < 1e-9) return;
            try { ent.LinetypeScale = ent.LinetypeScale * f; } catch { }
            if (ent is Dimension d)
            {
                try { d.Dimlfac = d.Dimlfac / f; } catch { }
            }
        }

        private static bool IsIdentity(Matrix3d m) => m.IsEqualTo(Matrix3d.Identity, new Tolerance(1e-9, 1e-9));
    }
}
