# VXT Gộp CAD – Gộp file DWG & xuất sheet Revit ra 1 file CAD

Tác giả: Vũ Xuân Tuyến. Phiên bản 1.0.0.
Chương trình chỉ đọc/ghi các file bản vẽ anh chọn trên máy. **Không thu thập, không gửi dữ liệu đi đâu.**

Bộ này có 2 phần:

| Phần | Chạy trong | Lệnh / nút | Làm gì |
|---|---|---|---|
| **VXT.GopCAD** | AutoCAD 2024 | `VXTGOP` hoặc Ribbon **VXT › Gộp CAD** | Gộp nhiều file DWG thành 1 file |
| **VXT.RevitXuatCAD** | Revit 2025 | Ribbon **VXT › Xuất CAD › Xuất sheet ra 1 file CAD** | Chọn sheet → xuất ra 1 file DWG |
| VXT.GopCAD.Core | (thư viện dùng chung) | `VXTGOPJOB` | Lõi gộp, chạy được trong AutoCAD Core Console |

---

## 1. Build và cài đặt

**Cách nhanh nhất – tạo Setup.exe:** cài .NET SDK 8 (`winget install Microsoft.DotNet.SDK.8`) và Inno Setup 6 (`winget install JRSoftware.InnoSetup`), rồi bấm đúp `TaoSetupExe.bat`. File `Output\VXT_GopCAD_Setup_1.0.0.exe` sẽ được tạo; chạy file này để cài (không cần quyền Admin), hoặc mang sang máy khác cài. Gỡ cài đặt trong *Settings › Apps*.

Cách thủ công:

Cần có trên máy: **Visual Studio 2022** (hoặc .NET SDK 8) có .NET Framework 4.8, cùng **AutoCAD 2024** và **Revit 2025**.

1. Chạy `build.bat`. Kết quả nằm trong `bin\AutoCAD` và `bin\Revit`.
   - Nếu AutoCAD/Revit cài ở chỗ khác, build như sau:
     `dotnet build src\VXT.GopCAD\VXT.GopCAD.csproj -c Release -p:AcadDir="D:\Autodesk\AutoCAD 2024\\"`
     `dotnet build src\VXT.RevitXuatCAD\VXT.RevitXuatCAD.csproj -c Release -p:RevitDir="D:\Autodesk\Revit 2025\\"`
2. Chạy `install.bat`. File cài sẽ được chép vào:
   - AutoCAD: `%APPDATA%\Autodesk\ApplicationPlugins\VXT_GopCAD.bundle` (AutoCAD tự nạp khi mở).
   - Revit: `%APPDATA%\Autodesk\Revit\Addins\2025\VXT.RevitXuatCAD.addin` cùng thư mục `VXT.RevitXuatCAD\`.
3. Đóng rồi mở lại AutoCAD và Revit. Lần đầu nạp, Revit sẽ hỏi có tin add-in không: chọn **Always Load**.

Gỡ cài đặt: chạy `uninstall.bat`.

---

## 2. Gộp nhiều file DWG trong AutoCAD (`VXTGOP`)

1. Gõ `VXTGOP` (hoặc bấm nút **Gộp file DWG** trên tab VXT).
2. Thêm file theo một trong các cách: **Thêm file...**, **Thêm thư mục...**, hoặc kéo thả file/thư mục vào danh sách. Dùng **Lên/Xuống** hoặc **Sắp xếp A→Z** để đổi thứ tự (thứ tự sắp xếp kiểu tự nhiên: KC-2 đứng trước KC-10).
3. Chọn tùy chọn:
   - **Xếp thành hàng**: các bản vẽ ở Model được xếp từ trái sang phải, đủ số bản vẽ trên 1 hàng thì xuống hàng. Để khoảng cách là 0 thì chương trình tự lấy bằng 10% kích thước bản vẽ đầu tiên.
   - **Giữ nguyên tọa độ**: dùng khi các file vẽ chung một hệ tọa độ (ví dụ các tầng hoặc các bộ môn).
   - **Layout**: chọn *Layout đầu tiên* khi gộp file sheet xuất từ Revit. Viewport được tự dời theo, nên vẫn nhìn đúng bản vẽ của nó.
   - **Mỗi file thành 1 block**: gọn, dễ di chuyển từng bản vẽ.
   - **Thêm tên file vào trước tên layer**: tránh trường hợp hai file có layer cùng tên nhưng khác màu hoặc nét bị gộp làm một.
   - **Ghi tên file dưới mỗi bản vẽ**: chữ nằm trên layer `VXT_TEN_BAN_VE`, font Arial.
4. Chọn file kết quả rồi bấm **Gộp**. Có thể bấm **Dừng** giữa chừng.

**Vì sao chạy nhanh:** từng file được đọc thẳng vào bộ nhớ, không mở lên màn hình, rồi chép đối tượng một lần (WblockClone).

---

## 3. Xuất sheet Revit ra 1 file CAD

1. Trong Revit, có thể chọn sẵn các sheet ở Project Browser. Sau đó bấm **VXT › Xuất sheet ra 1 file CAD**.
2. Tick các sheet cần xuất. Ô tìm kiếm không cần gõ dấu. Muốn bật/tắt nhiều sheet cùng lúc: bôi chọn nhiều dòng rồi bấm phím cách.
3. Chọn **DWG Export Setup** đã có trong dự án (layer, nét, màu theo bộ của anh).
4. Chọn kiểu kết quả:
   - **Mỗi sheet 1 Layout** *(khuyên dùng)*: ra 1 file DWG, mỗi sheet là một tab Layout có khung tên và viewport giống hệt Revit. Các bản vẽ ở Model được xếp thành hàng, không chồng lên nhau.
   - **Chỉ Model**: ra 1 file DWG, chỉ gồm nội dung Model xếp thành hàng. Lưu ý: khung tên của Revit nằm ở Layout nên kiểu này **không có khung tên**.
   - **Chỉ file lẻ**: mỗi sheet một file DWG, đặt tên theo "Số hiệu - Tên sheet".
5. Bấm **Xuất**.

**Cách hoạt động:** Revit không có lệnh gộp sheet, nên add-in làm theo các bước sau:
1. Xuất từng sheet ra DWG (bật *MergedViews*, tức là không tạo xref) vào thư mục tạm `C:\ProgramData\VXT\XuatCAD\temp`.
2. Gọi `accoreconsole.exe` của AutoCAD 2024. Chương trình này chạy ngầm, không mở AutoCAD, và dùng chung lõi VXT.GopCAD.Core để gộp.
3. Chép file kết quả ra chỗ anh chọn, rồi xóa thư mục tạm.

Nếu máy không có AutoCAD thì add-in vẫn xuất được các file lẻ. Khi đó có thể mở AutoCAD trên máy khác và gộp bằng `VXTGOP`, chọn *Layout đầu tiên*.

Về SECURELOAD: script tạm đặt `SECURELOAD = 0` để nạp được DLL từ thư mục tạm, chạy xong thì trả lại giá trị cũ. Nếu Core Console bị dừng giữa chừng, nên mở AutoCAD và kiểm tra lại biến `SECURELOAD` (mặc định là 1).

---

## 4. Lưu ý / giới hạn của bản 1.0

- Lõi gộp được build cho **AutoCAD 2024 (.NET Framework 4.8)**. Muốn dùng với AutoCAD 2025 trở lên (.NET 8) thì phải build lại `VXT.GopCAD.Core` với `TargetFramework=net8.0-windows` và trỏ `AcadDir` sang bản 2025.
- Block hoặc kiểu chữ trùng tên giữa các file: mặc định lấy định nghĩa của file đầu tiên. Nếu nội dung khác nhau thì bật tùy chọn "đổi tên".
- Nhận biết viewport tổng của paper space dựa vào cách thông thường. Nếu một Layout mở ra bị trống, gõ `Z` ↵ `E` ↵ là thấy.
- Chưa có chế độ "đưa cả khung tên Revit xuống Model" (giống lệnh EXPORTLAYOUT). Có thể bổ sung ở bản sau.
- Nên thử trước với vài sheet hoặc vài file rồi mới chạy cả bộ lớn.

## 5. Cấu trúc mã nguồn

```
VXT_GopCAD.sln
src/VXT.GopCAD.Core/   MergeEngine.cs (lõi gộp), JobFile.cs, CoreCommands.cs (VXTGOPJOB)
src/VXT.GopCAD/        GopForm.cs (giao diện), Commands.cs (VXTGOP + Ribbon)
src/VXT.RevitXuatCAD/  App.cs, XuatSheetForm.cs, SheetExporter.cs, Models.cs
bundle/PackageContents.xml   (tự nạp AutoCAD)
build.bat / install.bat / uninstall.bat
```
