# Exam Operations Desktop

Ứng dụng Windows cục bộ (.NET 8 LTS / WPF / SQLite) phục vụ Hội đồng tuyển dụng Bệnh viện Đa khoa Thiện Hạnh tự động hóa việc tạo đề thi thực hành Word và sinh script nạp dữ liệu trên hệ thống HIS eHospital.

---

## 1. Cấu trúc Solution

```text
desktop/
|- ExamGenerator.Desktop.sln
|- src/
|  |- ExamGenerator.Domain/         # Quy tắc nghiệp vụ, thực thể, ExamScenario
|  |- ExamGenerator.Application/    # Use cases, validation
|  |- ExamGenerator.Infrastructure/ # SQLite, SQL Server, OpenXML DOCX, T-SQL Generator, Allocator
|  `- ExamGenerator.Desktop/        # Ứng dụng WPF Modern Healthcare UI, ModernTheme.xaml
`- tests/ExamGenerator.Tests/       # Unit tests (Allocations, Rules, Word, SQL)
```

---

## 2. Tính năng Cốt lõi Đã Triển khai

1. **Phân bổ Kịch bản Khảo thí (`ExamScenarioAllocator`):**
   * Tuân thủ nghiêm ngặt **Quy tắc Rule 3.2**: Mỗi thí sinh được cấp 1 User HIS riêng biệt không trùng lặp trong cùng đợt thi.
   * Preflight Fail-Fast: Chặn ngay lập tức nếu số lượng thí sinh đăng ký trong khoa vượt quá số User HIS khả dụng.
   * Bốc bệnh nhân nguồn hoặc tạo bệnh nhân biến thể khảo thí (chuẩn hóa hạn BHYT theo năm thi, mã TE1 5 năm, DKKCB `66232`).
   * Phân bổ thuốc tồn kho dương từ kho thi đã map và phân bổ dịch vụ CLS theo khoa.
   * Liên kết câu hỏi: Câu đổi dịch vụ hủy đúng dịch vụ câu trước, câu trả thuốc trả đúng thuốc câu trước.

2. **Bộ sinh T-SQL Khảo thí (`SqlScriptGenerator`):**
   * Porting từ `Scripts/02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql`.
   * Phân định chuẩn: Đề tiếp nhận trực tiếp không sinh SQL; Đề nhận bệnh vào khoa sinh SQL nạp ca an toàn có cảnh báo chỉ chạy trên server thi.

3. **Bộ sinh Tài liệu Word Đề thi (`WordExamWriter`):**
   * Kết xuất tệp tin DOCX chuẩn A4 OpenXML: Bảng thông tin thí sinh & User HIS, Bảng thông tin hành chính bệnh nhân, Bảng dịch vụ CLS, Bảng y lệnh thuốc có liều dùng/đường dùng theo ĐVT và khung chữ ký giám khảo.

4. **Giao diện Modern Healthcare UI:**
   * Design System `ModernTheme.xaml` với bảng màu chuẩn y tế: Deep Navy (`#0F172A`), Medical Teal (`#0D9488`), Cyan (`#0284C7`), nền sáng `#F8FAFC`, thẻ Card bo góc mềm mại.
   * 6 Phân hệ hoàn chỉnh:
     1. *Thực thi tạo đề:* Cấu hình đợt thi, thêm thí sinh, bảng thí sinh, nút sinh đề.
     2. *Quản lý mẫu đề:* Master-detail danh sách mẫu đề và bảng câu hỏi/thang điểm.
     3. *Phân quyền Dịch vụ & Kho:* Cấu hình 1 khoa - 1 kho thi duy nhất và nhóm dịch vụ.
     4. *Cài đặt & Danh mục HIS:* Dashboard trạng thái snapshot và cấu hình SQL Server profile.
     5. *Tra cứu tồn kho:* Bộ lọc tìm kiếm thuốc theo kho, nguồn, tên/mã thuốc.
     6. *Lịch sử sinh đề:* Danh sách đợt thi đã sinh, nút mở nhanh tệp ZIP.

5. **Lưu trữ & Chuẩn hóa Danh mục HIS (Pure HIS Sync):**
   * CSDL SQLite cục bộ `exam-generator.sqlite` đặt cạnh phần mềm.
   * **Nguyên tắc Danh mục Chính xác:** Loại bỏ hoàn toàn dữ liệu mẫu (mock data) offline. Chỉ khi người vận hành bấm "Đồng bộ Catalog HIS" từ SQL Server HIS thì dữ liệu đó mới là danh mục chính thức.
   * Dữ liệu snapshot trong SQLite là bất biến và chỉ cập nhật khi người dùng đồng bộ lại lần nữa.
   * Chặn sinh đề (Fail-Fast) nếu cơ sở dữ liệu chưa có danh mục đồng bộ từ HIS.
   * Quản lý bảng `UsedPatient` chống trùng lặp bệnh nhân giữa các đợt thi.

---

## 3. Hướng dẫn Build & Chạy thử

### A. Đóng gói Bản Chạy Tự Động (Khuyến nghị)
Hệ thống cung cấp script PowerShell tự động kiểm thử, làm sạch và đóng gói:
```powershell
.\build_release.ps1 -Note "Ghi chú nội dung thay đổi phiên bản"
```
Script sẽ tự động:
1. Chạy 15/15 unit tests đảm bảo chất lượng.
2. Build & Publish cấu hình `Release` ra thư mục `desktop/publish/ExamOperationsDesktop/`.
3. Dọn dẹp CSDL cũ trong thư mục publish để bảo đảm khởi động sạch cho đồng bộ HIS.
4. Tự động ghi vết lịch sử build vào `BUILD_HISTORY.md`.

### B. Kiểm thử Đơn vị Thủ công (Unit Tests)
Chạy từ thư mục gốc của repository:
```powershell
dotnet test desktop\tests\ExamGenerator.Tests\ExamGenerator.Tests.csproj
```
*(Toàn bộ 15/15 tests Passed 100% OK)*

### C. Chạy Trực tiếp từ Source Code (Dev Mode)
```powershell
dotnet run --project desktop\src\ExamGenerator.Desktop\ExamGenerator.Desktop.csproj
```

### D. File Thực thi Đã Đóng Gói (Executable)
* File thực thi: `desktop/publish/ExamOperationsDesktop/ExamGenerator.Desktop.exe`
* Lịch sử các phiên bản build và thông số chi tiết: Xem tại `BUILD_HISTORY.md`.

