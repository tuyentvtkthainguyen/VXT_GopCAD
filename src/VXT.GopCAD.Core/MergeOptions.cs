using System;
using System.Collections.Generic;

namespace VXT.GopCAD.Core
{
    /// <summary>Cách đặt nội dung Model của từng file vào file gộp.</summary>
    public enum PlacementMode
    {
        /// <summary>Xếp các bản vẽ thành hàng, tự xuống hàng theo số cột.</summary>
        XepHang,
        /// <summary>Giữ nguyên tọa độ gốc (các bản vẽ chồng lên nhau).</summary>
        GiuToaDo
    }

    /// <summary>Lấy Layout (paper space) từ file nguồn hay không.</summary>
    public enum LayoutImportMode
    {
        KhongLay,
        /// <summary>Layout đầu tiên có nội dung – phù hợp file sheet xuất từ Revit.</summary>
        LayoutDau,
        TatCa
    }

    public class MergeItem
    {
        public string FilePath;
        /// <summary>Tên layout mong muốn cho layout đầu tiên (để trống = tên file).</summary>
        public string LayoutName;
        /// <summary>Chữ ghi dưới bản vẽ ở Model (để trống = tên file).</summary>
        public string Label;
    }

    public class MergeOptions
    {
        public List<MergeItem> Items = new List<MergeItem>();
        public string OutputPath;
        public PlacementMode Placement = PlacementMode.XepHang;
        /// <summary>Số bản vẽ trên 1 hàng; 0 = tất cả trên 1 hàng.</summary>
        public int Columns = 5;
        /// <summary>Khoảng cách giữa các bản vẽ (đơn vị bản vẽ); 0 = tự động.</summary>
        public double Gap = 0;
        public LayoutImportMode Layouts = LayoutImportMode.LayoutDau;
        /// <summary>Chèn Model của mỗi file thành 1 block.</summary>
        public bool AsBlock = false;
        /// <summary>Thêm tên file vào trước tên layer để tránh trùng.</summary>
        public bool LayerPrefix = false;
        /// <summary>Ghi tên file dưới mỗi bản vẽ ở Model.</summary>
        public bool Label = false;
        /// <summary>true = đổi tên block/kiểu trùng; false = dùng định nghĩa của file đầu tiên.</summary>
        public bool MangleDuplicates = false;
        /// <summary>Xóa Layout1/Layout2 mặc định khi đã chép được layout.</summary>
        public bool RemoveDefaultLayouts = true;
        /// <summary>
        /// Đưa sheet xuống Model theo kiểu CAD truyền thống: bản vẽ giữ 1:1, khung tên và nội dung
        /// trên Layout được phóng theo tỷ lệ của viewport chính (1/100 → ×100). Không lấy Layout.
        /// </summary>
        public bool SheetToModel = false;
    }

    public class MergeResult
    {
        public int Ok;
        public string OutputPath;
        public TimeSpan Elapsed;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
    }
}
