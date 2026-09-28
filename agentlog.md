# Nhật ký Agent (Agent Log) - Exam Generator & HIS Data Populator

Nhật ký ghi lại các thay đổi, quyết định thiết kế và tiến trình thực thi của Antigravity Coding Assistant trong suốt phiên làm việc.

## [2026-09-23] Khắc phục Lịch sử Sinh Đề, Cách ly Thư mục data/ & Bổ sung Câu hỏi Tra cứu Thanh toán

### 1. Khắc phục Lỗi "Lịch sử tạo đề chưa được lưu":
* **Nguyên nhân:**
  - `ReloadHistoryDataAsync()` không được gọi trong `Window_Loaded` khi ứng dụng khởi động.
  - Tab 6 ("6. Lịch sử Sinh đề") chưa bắt sự kiện chuyển tab (`SelectionChanged`).
* **Giải pháp:**
  - Tự động nạp lịch sử trong `Window_Loaded` và khi người dùng bấm chuyển sang Tab 6.
  - Cải tiến giao diện Tab 6: cột Ngày thi chuẩn `dd/MM/yyyy`, Tệp tin ZIP ngắn gọn, Thời điểm tạo định dạng `dd/MM/yyyy HH:mm:ss`.
  - Hỗ trợ Double-click vào dòng lịch sử để mở Explorer và chọn đúng file ZIP (có fallback tìm trong `./data/output/`).
  - Thêm unit test `Exam_generation_history_is_persisted_and_retrieved_in_descending_order`.

### 2. Cách ly Thư mục Dữ liệu `data/` (Bảo vệ tuyệt đối Mapping & CSDL):
* **Kiến trúc mới:**
  - Thư mục gốc chỉ chứa file nhị phân và mã thực thi (`.exe`, `.dll`, `.json` runtime, `runtimes/`).
  - Thư mục `data/` chứa toàn bộ dữ liệu người dùng: `data/exam-generator.sqlite`, `data/appsettings.local.json`, `data/credentials.local.bin`, và `data/output/`.
* **Tự động chuyển đổi dữ liệu cũ (Auto-migration):**
  - `LocalApplicationStorage` tự động phát hiện file database/cấu hình ở thư mục gốc và di chuyển an toàn vào `data/` trong lần đầu khởi động.
* **Cập nhật không mất dữ liệu:**
  - Thư mục phát hành (Publish) không chứa thư mục `data/`. Người dùng có thể copy đè tất cả file mới vào thư mục chạy mà không lo đè mất dữ liệu hay mapping.
  - Cung cấp script tự động `CapNhat_KhongMatDuLieu.bat` với bộ lọc loại trừ thông minh của `robocopy`.

### 3. Đóng gói Bản Standalone (Self-Contained win-x64):
* `build_release.ps1` tự động đóng gói cả 2 bản:
  - Bản Tiêu chuẩn: `desktop/publish/ExamOperationsDesktop/`
  - Bản Standalone win-x64: `desktop/publish/ExamOperationsDesktop-Standalone/` (đầy đủ .NET 8 Runtime, chạy trên mọi máy tính).

### 4. Bổ sung Câu hỏi Thư viện HIS: `HC_TRA_CUU_THANH_TOAN`:
* Thêm dạng câu hỏi "Tra cứu thông tin thanh toán" (2.0 đ) thuộc phân hệ "Hành chính & Tra cứu".
* Render đầy đủ các trường bệnh nhân, số bệnh án nội trú, số tiền thanh toán, tạm ứng, còn phải thu và tổng số tiền trả ra đề thi Word.

### 5. Rà soát & Đảm bảo An toàn Tuyệt đối Dữ liệu Mapping Khoa - Kho:
* Khẳng định và kiểm chứng: Mapping Khoa - Kho Dược được bảo vệ 3 lớp, không thể bị mất khi cập nhật code, khi đồng bộ từ HIS, hay khi chỉnh sửa mẫu đề.
* Toàn bộ 34/34 Unit Tests PASSED 100%.

---

## [2026-09-23] Xử lý Triệt để Lỗi APPCRASH (Stop Working) trên Windows 7 khi vào Quản lý Biên tập Mẫu Đề

### 1. Phân tích Nguyên nhân Gốc rễ (Root Cause Analysis):
*   **Báo cáo lỗi của người dùng:**
    - `Problem Event Name: APPCRASH`, `Fault Module: KERNELBASE.dll`, `Exception Code: e0434352` (Mã ngoại lệ CLR không bắt được trong .NET).
    - `OS Version: 6.1.7601` (Windows 7 SP1 64-bit).
*   **Các nguyên nhân kỹ thuật cụ thể đã phát hiện:**
    1.  **Thiếu tài nguyên XAML `BrandTealDark`:** Trong `ManageActionCatalogWindow.xaml` có dòng `<Setter Property="Foreground" Value="{StaticResource BrandTealDark}"/>` nhưng trong `ModernTheme.xaml` chưa định nghĩa khóa `BrandTealDark`. Khi mở cửa sổ, WPF ném ra `XamlParseException` (`ResourceReferenceKeyNotFoundException`). Do không có bộ bắt lỗi toàn cục, ứng dụng bị ngắt đột ngột và báo lỗi APPCRASH ra hệ điều hành.
    2.  **Định nghĩa ControlTemplate tùy biến trong DataGrid không hoàn chỉnh:** Thẻ `<ControlTemplate TargetType="TextBox">` có `ScrollViewer x:Name="PART_ContentHost"` trong `<DataGrid.Resources>` thiếu các thuộc tính native (như `Focusable="false"`, VisualStateManager triggers), dẫn tới lỗi layout/measure trên Windows 7 SP1 khi DataGrid kết xuất các dòng câu hỏi.
    3.  **Lỗi bất đồng bộ không được bọc try-catch trong `TemplateSelected`:** Sự kiện `SelectionChanged` của `TemplatesGrid` chạy hàm `async void TemplateSelected`. Khi có ngoại lệ phát sinh trong hàm `async void`, ngoại lệ sẽ thoát khỏi luồng UI và bị ném thẳng ra ThreadPool/SynchronizationContext gây sập ứng dụng.
    4.  **Xung đột đồ họa phần cứng trên Windows 7:** Trên Windows 7 SP1, driver đồ họa DirectX 11 cũ hoặc thiếu bản vá cập nhật Direct3D có thể gây lỗi `COMException` / `D3DCompiler` khi WPF kết xuất tăng tốc phần cứng.

### 2. Giải pháp Xử lý Toàn diện:
*   **Bổ sung Bắt lỗi Toàn cục & Ghi nhật ký Crash (`App.xaml.cs`):**
    - Đăng ký lắng nghe 3 sự kiện lỗi cấp cao nhất: `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, và `TaskScheduler.UnobservedTaskException`.
    - Khi có lỗi phát sinh bất ngờ: Đặt `args.Handled = true` để **ngăn chặn tuyệt đối việc Windows tắt ứng dụng với thông báo Stop working**, ghi lại toàn bộ StackTrace và InnerException vào file `crash_log.txt`, đồng thời hiển thị hộp thoại `MessageBox` giải thích chi tiết nguyên nhân.
    - Tự động kích hoạt cơ chế `RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly` nếu phát hiện hệ điều hành là Windows 7 / Windows 8 (`Major < 10`), loại bỏ hoàn toàn nguy cơ sập ứng dụng do driver đồ họa.
*   **Sửa lỗi Tài nguyên XAML:**
    - Khai báo bổ sung `<SolidColorBrush x:Key="BrandTealDark" Color="#0F766E"/>` trong `ModernTheme.xaml`.
    - Chuẩn hóa style của `TextBox` trong `<DataGrid.Resources>`: loại bỏ `ControlTemplate` thừa, giữ nguyên template gốc của hệ thống và chỉ thiết lập `Height="28"`, `Padding="6,2"`, `VerticalContentAlignment="Center"`.
*   **Bọc Try-Catch An toàn cho các Sự kiện Biên tập:**
    - Bọc toàn bộ logic trong `TemplateSelected` và `ManageActionLibrary_Click` bằng khối `try ... catch`.
    - Thay thế các lệnh ép kiểu tài nguyên thô `(Brush)FindResource(...)` bằng hàm an toàn `TryFindResource(...) as Brush ?? fallbackBrush`.
*   **Đóng gói Cập nhật:**
    - 27/27 Unit tests Passed 100%.
    - Cập nhật cả 2 bản build: Framework-Dependent (`desktop/publish/ExamOperationsDesktop/`) và Standalone Self-Contained (`desktop/publish/ExamOperationsDesktop-Standalone/`).

---

## [2026-09-23] Tự do Thêm & Quản lý Dạng Câu hỏi Mới với Số điểm Mẫu vào Thư viện Nghiệp vụ HIS

### 1. Quyết định nghiệp vụ & Thiết kế:
*   **Bảng CSDL SQLite mới (`ActionDefinition`):**
    - Thiết kế bảng lưu trữ vĩnh viễn các dạng câu hỏi/thao tác nghiệp vụ: `ActionCode`, `ActionName`, `DefaultScore`, `Category`, `Description`, `IsBuiltIn`, `CreatedAt`.
    - Tự động nạp sẵn 13 dạng nghiệp vụ chuẩn (`IsBuiltIn = 1`) trong `SqliteDatabase.InitializeAsync`.
    - Bảo vệ toàn vẹn hệ thống: Chặn xóa các dạng câu hỏi mặc định của hệ thống, chỉ cho phép xóa các dạng câu hỏi do người dùng tự tạo.
*   **Giao diện Quản lý Thư viện (`ManageActionCatalogWindow`):**
    - Bổ sung nút `⚙ Quản lý Thư viện / Thêm dạng mới...` ngay tại khung Thêm câu hỏi ở Tab 2 (Biên tập mẫu đề).
    - Cung cấp Form trực quan để tạo dạng câu hỏi mới: Mã hành động (tự động chuẩn hóa viết hoa và gạch dưới), Tên câu hỏi, Số điểm mẫu mặc định (hỗ trợ nhập cả `.` và `,`), Phân hệ nghiệp vụ, và Mô tả thao tác / Hướng dẫn mẫu.
    - Hiển thị bảng tra cứu toàn bộ câu hỏi trong thư viện, phân loại rõ ràng bằng badge `Hệ thống` / `Tự tạo`.
    - Cho phép xóa câu hỏi tự tạo khi không còn nhu cầu sử dụng.
*   **Phản ứng Tức thì (Reactive Auto-Reload):**
    - Khi lưu dạng câu hỏi mới trong cửa sổ quản lý, hệ thống tự động lưu vào SQLite, đóng/cập nhật và `ActionLibraryComboBox` ở Tab 2 sẽ tự động nạp lại, đồng thời tự động chọn ngay dạng câu hỏi vừa tạo để người dùng bấm `+ Thêm vào đề`.
*   **Tích hợp với Sinh Đề thi & Văn bản Word (`ExamScenarioAllocator`):**
    - Khi sinh đề thi có chứa dạng câu hỏi tự tạo, hệ thống tự động nạp mô tả thao tác tùy biến từ bảng `ActionDefinition` và in trực tiếp vào câu hỏi trong tệp đề thi Word kèm dòng chấm điểm `Số điểm đạt được:………(đ)`.
*   **Kiểm thử & Phát hành:**
    - Bổ sung 2 bài test tự động chuyên biệt trong `CatalogSynchronizationTests.cs` và `ExamScenarioAllocatorTests.cs`.
    - Toàn bộ **27/27 Unit tests Passed 100%**.
    - Cập nhật bản phát hành Release tại `desktop/publish/ExamOperationsDesktop/ExamGenerator.Desktop.exe`.

---

## [2026-09-23] Khắc phục lỗi Co chữ DataGrid Câu hỏi & Rà soát Toàn diện Phần mềm

### 1. Khắc phục Triệt để Lỗi Co chữ / Mất chữ trên DataGrid Câu hỏi & Thang điểm:
*   **Nguyên nhân gốc rễ (Root Cause):**
    - Trong `ModernTheme.xaml`, style toàn cục `Style TargetType="TextBox"` đặt thuộc tính `Height="34"` và `Margin="0,4,0,8"`.
    - Khi `DataGrid` đặt `RowHeight="34"`, ô nhập liệu TextBox bên trong cell có chiều cao cộng dồn lề lên tới $34 + 4 + 8 = 46$px, vượt quá kích thước hàng $34$px dẫn đến chữ bị cắt xén (clip) ở cạnh trên và cạnh dưới, không nhìn rõ dấu tiếng Việt (`hành`, `định`, `thuốc`).
*   **Giải pháp xử lý:**
    - Định nghĩa override riêng cho `TextBox` bên trong `<DataGrid.Resources>`: `Height="30"`, `Margin="2,0"`, `Padding="6,2"`, `VerticalContentAlignment="Center"`, `VerticalAlignment="Center"`.
    - Tăng `RowHeight="44"` và tăng chiều cao lưới `Height="270"` để chữ hiển thị thoáng, đẹp mắt, giữ nguyên dấu thanh tiếng Việt.
    - Bổ sung style toàn cục cho `DataGridCell` với `VerticalContentAlignment="Center"`, `Padding="4,0"`, `BorderThickness="0"`.
    - Căn giữa cột STT, nới rộng cột Mã hành động lên `190` font Consolas để không bị cụt mã dài như `YL_CHI_DINH_THUOC_VTYT`.
    - Nới rộng cột Điểm `Width="85"`, font chữ đậm (Bold), căn giữa. Cột Thao tác nới `Width="110"`.
*   **Khắc phục ComboBox Khoa/Phòng áp dụng trên Form Editor:**
    - Cấu hình `IsEditable="True"` và nạp nguồn dữ liệu gộp từ cả snapshot SQLite và các khoa từ mẫu đề mẫu (`allKnownDepts`), giúp ComboBox luôn hiển thị đúng Khoa của mẫu đề đang chọn (ví dụ: *Khoa Chấn thương chỉnh hình*) ngay cả trước khi đồng bộ SQL Server HIS.
    - Cập nhật tương tự cho ComboBox Khoa phòng ở `CloneTemplateWindow` và màn hình Tạo đề.

### 2. Rà soát Toàn diện Hệ thống Phần mềm (Comprehensive Audit):
*   **Tab 1: Thực thi Tạo đề thi:**
    - Đã có cơ chế bảo vệ trước khi sinh đề (Preflight Validation: kiểm tra tồn kho, số lượng User HIS, số hồ sơ bệnh nhân BHYT/Viện phí).
    - Cảnh báo rõ ràng và thân thiện nếu chưa đồng bộ catalog.
*   **Tab 2: Quản lý & Biên tập Mẫu đề thi:**
    - Lưu hai chiều, tính tổng điểm thời gian thực, clone đề sang khoa khác mượt mà.
    - Xóa/sắp xếp câu hỏi hoạt động ổn định.
*   **Tab 3: Phân quyền Kho thi & Dịch vụ:**
    - Mapping 1 Khoa - 1 Kho cơ số (Rule 3.3).
    - Quản lý trạng thái Tạm ngưng thi độc lập với cờ TamNgung trên HIS.
    - Cơ chế đồng bộ Additive Sync bảo toàn toàn bộ cấu hình đã thiết lập.
*   **Tab 4: Cài đặt & HIS Catalog:**
    - Kết nối an toàn qua ADO.NET SqlConnectionStringBuilder; đường dẫn SQLite cục bộ tương đối an toàn.
*   **Tab 5: Tra cứu Tồn kho Dược:**
    - Bộ lọc theo Kho và từ khóa, phân trang nhanh.
*   **Độ tin cậy Test & Build:**
    - Hoàn thiện thuật toán băm hạt giống ngẫu nhiên xác định `GetDeterministicHashCode` trong `ExamScenarioAllocator`, giúp kết quả sinh đề và test chạy đồng nhất tuyệt đối trên cả môi trường Debug và Release.
    - **25/25 Unit tests Passed 100%**.
    - Bản build Release tại `desktop/publish/ExamOperationsDesktop/ExamGenerator.Desktop.exe` đã được cập nhật thành công.

---

## [2026-09-23] Nâng cấp Toàn diện Bộ Biên tập Mẫu Đề Thi (Interactive Template Editor), Tùy chỉnh Câu hỏi & Phân bổ Điểm số, Nhân bản Đề Linh hoạt

### 1. Quyết định nghiệp vụ & Thiết kế:
*   **Bộ biên tập Mẫu đề Trực quan (Interactive Template Editor):**
    - Chuyển đổi khung xem chi tiết readonly trước đây thành Form biên tập động hai chiều tại Tab 2.
    - Cho phép chỉnh sửa trực tiếp Tên mẫu đề, Khoa/Phòng áp dụng, Vị trí chuyên môn và Hình thức nhận ca (Tiếp nhận trực tiếp vs Hàng chờ nhận ca).
*   **Thư viện Dạng câu hỏi Nghiệp vụ HIS chuẩn (`SystemActionCatalog`):**
    - Chuẩn hóa 13 dạng câu hỏi nghiệp vụ HIS: Tiếp nhận, Nhận bệnh vào khoa, Chỉ định CLS, Lên y lệnh Dược/VTYT, Trả thuốc, Đổi/thêm CLS, Kiểm tra tồn kho cơ số, Chuyển khoa, Cho ra viện, Thu tạm ứng, Thanh toán ra viện, Tra cứu bệnh sử, Trả kết quả CLS.
    - Tích hợp ComboBox chọn nhanh dạng câu hỏi kèm gợi ý điểm mặc định và nút `+ Thêm vào đề`.
*   **Phân bổ Điểm số Linh hoạt & Kiểm soát Tổng điểm Thời gian thực:**
    - Cho phép sửa điểm từng câu trực tiếp trên DataGrid (hỗ trợ cả dấu `.` và dấu `,`).
    - Hỗ trợ đổi thứ tự câu hỏi trong đề (`▲` Lên, `▼` Xuống) và xóa câu (`✕`).
    - Tính tổng điểm tự động: Hiển thị Badge trực quan màu xanh lá khi đủ 10.0đ; cảnh báo màu cam nếu khác 10.0đ và xác nhận khi lưu.
*   **Hộp thoại Nhân bản Đề thi Linh hoạt (`CloneTemplateWindow`):**
    - Khi bấm Clone, mở hộp thoại cho phép nhập Tên mẫu đề mới, chọn Khoa/Phòng áp dụng đích và Vị trí.
    - Mẫu đề sau khi nhân bản tự động lưu và mở ngay trên khung Editor để người dùng tinh chỉnh.
*   **Tạo mới Mẫu đề trống & Bộ lọc Khoa:**
    - Nút `+ Tạo mới` chuyển sang chế độ tạo đề sạch, tự do chọn khoa, vị trí và tự thêm các câu hỏi mong muốn.
    - Bổ sung ComboBox lọc danh sách đề thi theo Khoa/Phòng tại cột bên trái.
    - Đồng bộ ngay lập tức sang Tab 1 (Tạo đề thi) sau mỗi thao tác Lưu, Clone, Xóa.
*   **Kiểm thử & Phát hành:**
    - Bổ sung 3 unit tests mới trong `ExamRulesTests.cs` và `CatalogSynchronizationTests.cs` kiểm tra `SystemActionCatalog`, lưu điểm số tùy biến và nhân bản mẫu đề sang khoa mới.
    - Toàn bộ **25/25 unit tests Passed 100%**.
    - Đóng gói bản phát hành Release tại `desktop/publish/ExamOperationsDesktop/ExamGenerator.Desktop.exe`.

---

## [2026-09-16] Bảo vệ Danh mục Khoa/Phòng, Cấu hình Mapping và Mẫu Đề thi khi Đồng bộ HIS

### 1. Quyết định nghiệp vụ & Thiết kế:
*   **Chiến lược Đồng bộ Cộng dồn (Additive / Non-destructive Sync) cho Khoa/Phòng:**
    - Khoa/Phòng ban trong bệnh viện mang tính ổn định cao, hiếm khi thay đổi.
    - Loại bỏ hoàn toàn lệnh `DELETE FROM CatalogItem WHERE CatalogType = 'Departments'`.
    - Khi đồng bộ từ SQL Server HIS, hệ thống chỉ kiểm tra và `INSERT` những Khoa/Phòng mới phát sinh trên HIS (`SyncDepartmentsAdditiveAsync`).
    - Các Khoa/Phòng hiện hữu và toàn bộ cấu hình Mapping Kho thi (`DepartmentConfiguration`), Nhóm Dịch vụ (`DepartmentServiceGroup`), Mẫu Đề thi (`ExamTemplate`) được bảo lưu nguyên vẹn 100%.
*   **Quyền Quản lý Tạm ngưng Thi do Người dùng Làm chủ (User-Controlled Status):**
    - Trạng thái một khoa có tham gia kỳ thi hay không là quyết định của Hội đồng thi, không phụ thuộc máy móc vào cờ `TamNgung` trên HIS.
    - Bổ sung trường `IsDisabledForExam` vào bảng `CatalogItem`.
    - Thêm Checkbox tại màn hình Phân quyền: *"Cho phép khoa này tham gia khảo thí (Bỏ chọn để Tạm ngưng thi thủ công mà không làm mất mapping & đề thi)"*.
    - Thêm badge trực quan `[Tạm ngưng]` màu đỏ trên danh sách Khoa/Phòng khi khoa bị tạm dừng.
    - ComboBox chọn Khoa tại màn hình **Thực thi tạo đề** tự động lọc chỉ hiển thị các Khoa đang kích hoạt thi (`!IsDisabled`).
*   **Báo cáo Đồng bộ Chi tiết:**
    - Popup thông báo sau khi đồng bộ HIS thể hiện rõ:
      `• Khoa / Phòng (Cấp 1): {deptTotal} khoa (Bảo lưu: {deptPreserved} khoa & mapping/mẫu đề; Bổ sung mới: {deptAdded} khoa)`
*   **Kiểm thử & Phát hành:**
    - Bổ sung 2 bài test tự động chuyên biệt trong `CatalogSynchronizationTests.cs` kiểm chứng cơ chế Additive Sync và việc duy trì trạng thái Tạm ngưng khi đồng bộ. Toàn bộ **22/22 unit tests Passed 100%**.
    - Cập nhật bản phát hành tại `desktop/publish/ExamOperationsDesktop/` và `desktop/publish/ExamOperationsDesktop-final/`.

---

## [2026-09-14] Chuẩn hóa toàn diện Đề thi theo mẫu thực tế BV Thiện Hạnh (Thi_2021) & Tối ưu in ấn, kiểm soát tồn kho lũy kế

### 1. Quyết định nghiệp vụ & Thiết kế:
*   **Cấu trúc Đề thi Word & Layout in ấn 2 trang:**
    - Bổ sung Bảng Điểm & Chữ ký của cán bộ coi thi/chấm thi (Table 1) ngay trên đầu đề thi.
    - Đồng bộ 100% font chữ Times New Roman trên mọi thành phần văn bản.
    - Tối ưu căn lề trang in A4: Lề trái 20mm (dập ghim), lề trên/dưới/phải 10mm. Đề thi lâm sàng 8 câu vừa khít trong đúng 2 trang (1 tờ A4 in 2 mặt).
*   **Cấu hình Thời gian làm bài thi:**
    - Mặc định 30 phút cho đề lâm sàng/nội trú, 15 phút cho Lễ tân/Khám bệnh/Xét nghiệm.
    - Hiển thị trên giao diện và tự động điền vào phần thông tin đầu đề thi.
*   **Câu 2: Phân loại CĐHA/CLS ngẫu nhiên theo 4 modality:**
    - Tự động phân chia dịch vụ Cấp 1 (`Cap = 1 OR Cap IS NULL`) thành 4 nhóm: Siêu âm, X-quang, CT/MRI, Xét nghiệm.
    - Bốc ngẫu nhiên theo từng nhóm, in đậm tiêu đề từng mục. Câu 5 đổi/hủy dịch vụ liên kết chặt chẽ với Câu 2.
*   **Câu 3: Chuẩn hóa 5 dòng Dược & Kiểm soát Tồn kho lũy kế (Cumulative Stock Tracking):**
    - Chuẩn hóa đúng tỷ lệ: 3 dòng đầu là Thuốc nguồn BHYT (SL: 1, 2, 1); 2 dòng sau là VTYT nguồn Viện phí (SL: 2, 2).
    - Khấu trừ tồn kho khả dụng `remainingStock` liên tục giữa các thí sinh trong cùng khoa thi để đảm bảo:
      $$\sum_{\text{thí sinh}} \text{SL xuất Dược } i \le \text{Tồn kho Dược } i$$
    - Tránh tình trạng nhiều thí sinh cùng xuất dẫn đến vượt tồn kho và bị lỗi trên HIS. Chặn trước (preflight) nếu kho không đủ hàng.
*   **Câu 6, 7, 8:**
    - Câu 6 đa dạng hóa loại dược Viện phí tra cứu giữa các thí sinh.
    - Câu 7 & 8 tự động đặt mốc ngày chặn $X = \text{ExamDate} - 10\text{ ngày}$.
    - Bổ sung dòng chấm `....................` để thí sinh ghi câu trả lời.
*   **Tính năng In Gộp Toàn Bộ Đề Thi (`_InGop_TatCaDeThi...`):**
    - Tự động xuất thêm 1 tệp Word duy nhất chứa toàn bộ đề thi của các thí sinh với ngắt trang chuẩn `BreakValues.Page` để in 1 lần ra toàn bộ đề của cả hội đồng.
*   **Kiểm thử & Bản phát hành:**
    - 20/20 Unit tests Passed 100%.
    - Cập nhật bản phát hành hoàn thiện tại `desktop/publish/ExamOperationsDesktop-final/`.

---

## [2026-09-14] Hiệu chỉnh theo phản hồi kiểm thử CSDL thi thử eHospital: Lọc Khoa Cấp 1, Popup Thông Báo Chi Tiết, Tối Ưu Mapping & Tra Cứu Tồn Kho

### 1. Quyết định nghiệp vụ & Thiết kế:
*   **Lọc Khoa/Phòng Cấp 1 (`DM_PhongBan`):**
    - Áp dụng điều kiện `AND Cap = 1` trong câu query đồng bộ: `SELECT ... FROM dbo.DM_PhongBan WHERE ISNULL(TamNgung, 0) = 0 AND Cap = 1 ORDER BY TenPhongBan`.
    - Loại bỏ các buồng khám lẻ, phòng khám con, chỉ lấy các Khoa và Phòng ban chính thức.
*   **Popup Thông Báo Đồng Bộ Chi Tiết:**
    - Sau khi bấm "Đồng bộ Catalog HIS", nếu thành công sẽ hiển thị hộp thoại `MessageBox` liệt kê chi tiết số lượng bản ghi từng danh mục (Khoa phòng, Kho dược, Nhóm dịch vụ, Dịch vụ CLS, User HIS, Thuốc tồn kho, Bệnh nhân).
    - Nếu thất bại, bắt ngoại lệ và hiển thị `MessageBox` lỗi (`MessageBoxImage.Error`) nêu rõ nguyên nhân.
*   **Tối ưu Giao diện Mapping (Phân quyền):**
    - Chỉ hiển thị Tên trực tiếp, không kèm mã.
    - Chuyển `ServiceGroupsList` sang mô hình `ServiceGroupItemViewModel` với `CheckBox` hai chiều (`IsChecked`), bổ sung nút "Chọn tất cả" và "Bỏ chọn tất cả".
    - Tìm kiếm nhóm dịch vụ không làm mất các lựa chọn đã tích trước đó.
*   **Bổ sung Bộ Đề Mẫu Chuẩn Phong Phú (Seed Data):**
    - Mở rộng `EnsureDefaultTemplatesAsync()` nạp sẵn 12 đề thi chuẩn (Điều dưỡng Nội, Ngoại, CTCH, HSTC, Sản, Nhi, Cấp cứu; Lễ tân Nội trú, Khám bệnh; KTV Xét nghiệm, CĐHA; Thu ngân viện phí) theo cấu trúc họ đề từ hệ thống cũ.
*   **Hiệu chỉnh Tra cứu Tồn kho Chính xác:**
    - Truy vấn tồn kho trích xuất quan hệ `DM_KhoDuoc.PhongBan_Id -> DM_PhongBan` để lưu `DepartmentCode`, `DepartmentName` vào `SnapshotDrug`.
    - Khi chọn Khoa tại Tab Tra cứu tồn kho, combobox Kho Dược tự động lọc theo các kho thuộc khoa quản lý.
    - Cập nhật câu lệnh SQLite lọc đúng theo Khoa và Kho, hỗ trợ tìm kiếm tên/mã thuốc không phân biệt hoa thường và không dấu.
    - Bổ sung cột hiển thị `Khoa / Phòng` trong bảng kết quả tra cứu tồn kho.
*   **Kiểm thử & Đóng gói:**
    - Toàn bộ 16/16 unit tests Passed 100%.
    - Đóng gói cả bản Framework-dependent tại `desktop/publish/ExamOperationsDesktop/` và bản Single-file Self-contained tại `desktop/publish/ExamOperationsDesktop-final/`.

---

## [2026-09-07] Chuẩn hóa Dữ liệu Đồng bộ HIS: Loại bỏ hoàn toàn Dữ liệu Mẫu Offline, Snapshot SQLite là Duy nhất & Bất biến

### 1. Quyết định nghiệp vụ & Thiết kế:
*   **Loại bỏ 100% Mock / Sample Data Offline:**
    - Không nạp bất kỳ bệnh nhân, thuốc, dịch vụ, hay user HIS giả định/mẫu vào SQLite khi khởi tạo database.
    - Cơ chế snapshot SQLite hoàn toàn sạch (`0` records khi khởi tạo).
    - Ngăn chặn triệt để tình trạng lệch danh mục, sai mã phòng ban, sai mã dịch vụ hoặc tồn kho ảo giữa dữ liệu mẫu và môi trường bệnh viện thực tế.
*   **Chỉ Đồng bộ từ SQL Server HIS mới là Danh mục Chính xác:**
    - Khi người dùng bấm **"Đồng bộ Catalog HIS"** tại Tab Cài đặt, hệ thống thực hiện `SELECT` danh mục chính thức từ SQL Server HIS (`eHospital_ThienHanh`):
      1. Danh mục Khoa/Phòng (`DM_PhongBan`)
      2. Danh mục Kho Dược (`DM_Kho`)
      3. Danh mục Nhóm dịch vụ (`DM_NhomDichVu`)
      4. Danh mục Dịch vụ CLS (`DM_DichVu` gắn theo `DM_PhongBan_DichVu`)
      5. Danh mục User HIS (`Sys_Users` kèm mapping nhân viên và phòng ban)
      6. Danh mục Thuốc/VTYT còn tồn (`DM_Duoc` inner join `DuocTonKho` có `SoLuongTon > 0`, `TamNgung = 0`)
      7. Danh mục Bệnh nhân nguồn (`DM_BenhNhan` inner join `DM_BenhNhan_BHYT` còn hạn)
    - Dữ liệu được ghi đè nguyên tử (atomic transaction) vào các bảng snapshot SQLite (`snapshot_departments`, `snapshot_warehouses`, `snapshot_services`, `snapshot_users`, `snapshot_drugs`, `snapshot_patients`).
    - Dữ liệu snapshot trong SQLite **chỉ thay đổi khi người vận hành bấm đồng bộ lại lần nữa**.
*   **Kiểm soát Chặt chẽ Luồng Sinh Đề (Fail-Fast Preflight):**
    - Nếu cơ sở dữ liệu SQLite chưa có dữ liệu đồng bộ (số lượng snapshot = 0), hệ thống **chặn hoàn toàn** việc tạo kịch bản đề thi và thông báo rõ ràng cho người dùng phải cấu hình kết nối và bấm "Đồng bộ Catalog HIS" trước.
    - Giao diện Dashboard và Cài đặt hiển thị số lượng thực tế từ SQLite (nếu chưa đồng bộ sẽ hiển thị `0` kèm cảnh báo trực quan màu cam).

---

## [2026-09-07] Hiện thực hóa Core Engine Exam Operations Desktop, Khắc phục Rule 3.2 và Nâng cấp Giao diện Modern Healthcare UI

### 1. Quyết định nghiệp vụ & Thiết kế:
*   **Khắc phục triệt để lỗ hổng Rule 3.2 (User HIS):**
    - Loại bỏ hoàn toàn cơ chế chọn 1 User combobox chung cho cả đợt thi.
    - Xây dựng `ExamScenarioAllocator`: tự động truy vấn danh sách User HIS của khoa và phân bổ mỗi thí sinh 1 User HIS riêng biệt không trùng lặp trong đợt thi.
    - Cơ chế Preflight Fail-Fast: Chặn ngay từ đầu nếu số lượng thí sinh trong một khoa vượt quá số lượng User HIS khả dụng của khoa đó.
*   **Chuyển giao Bộ sinh T-SQL (`SqlScriptGenerator`) sang C# .NET 8:**
    - Porting logic nạp tham số từ template `Scripts/02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql` sang C#.
    - Hỗ trợ phân định chính xác: Đề Tiếp nhận trực tiếp không sinh SQL; Đề Nhận bệnh vào khoa sinh script an toàn có cảnh báo chỉ chạy trên server thi.
*   **Nâng cấp Bộ sinh Tài liệu Word (`WordExamWriter`):**
    - Thiết kế tài liệu Word chuẩn A4 OpenXML chuyên nghiệp: Bảng thông tin thí sinh, Bảng thông tin hành chính bệnh nhân (PID, BHYT, DKKCB 66232), Bảng dịch vụ CLS, Bảng y lệnh thuốc & VTYT (liều dùng, đường dùng theo ĐVT), liên kết logic đổi dịch vụ / trả thuốc và khung chữ ký giám khảo.
*   **Thiết kế lại Giao diện (UI/UX Redesign):**
    - Xây dựng `Resources/ModernTheme.xaml` theo chuẩn Modern Fluent Healthcare Dashboard: Bảng màu Deep Navy (`#0F172A`), Slate (`#1E293B`), Medical Teal (`#0D9488`), Cyan (`#0284C7`), nền sáng `#F8FAFC`, thẻ Card bo góc mềm mại (`CornerRadius="8"`), viền mỏng `#E2E8F0`.
    - Thiết kế lại toàn bộ 6 phân hệ giao diện: Dashboard thống kê 4 chỉ số, form cấu hình gọn gàng, DataGrid hiện đại, badges trạng thái trực quan.
*   **Mở rộng SQLite Database:**
    - Hỗ trợ lưu trữ `UsedPatient` chống trùng bệnh nhân giữa các đợt thi, lưu snapshot và nạp dữ liệu bệnh viện mặc định phục vụ khảo thí offline hoàn chỉnh.

### 2. Công việc đã thực hiện:
*   Tạo `desktop/src/ExamGenerator.Domain/ExamScenario.cs`.
*   Tạo `desktop/src/ExamGenerator.Infrastructure/SqlScriptGenerator.cs`.
*   Nâng cấp `desktop/src/ExamGenerator.Infrastructure/WordExamWriter.cs`.
*   Nâng cấp `desktop/src/ExamGenerator.Infrastructure/SqliteDatabase.cs`.
*   Tạo `desktop/src/ExamGenerator.Infrastructure/ExamScenarioAllocator.cs`.
*   Tạo `desktop/src/ExamGenerator.Desktop/Resources/ModernTheme.xaml` và nhúng vào `App.xaml`.
*   Viết lại toàn diện `desktop/src/ExamGenerator.Desktop/MainWindow.xaml` và `MainWindow.xaml.cs`.
*   Tạo bộ kiểm thử đơn vị `desktop/tests/ExamGenerator.Tests/ExamScenarioAllocatorTests.cs`.
*   Chạy `dotnet test` đạt 14/14 tests Passed (100% OK).
*   Chạy `dotnet publish` Release build thành công vào `desktop/publish/ExamOperationsDesktop/`.

---

## [2026-08-31] Chốt định hướng Exam Operations Desktop và quy tắc nghiệp vụ

### Quyết định nghiệp vụ và kiến trúc:
* Dự án được định hướng lại thành ứng dụng Windows mới: C# / .NET 8 LTS / WPF / MVVM; không bị ràng buộc phải giữ mã Python/FastAPI hiện tại.
* SQLite cục bộ là dữ liệu vận hành và snapshot; Excel chỉ là import dự phòng, import thí sinh hoặc export đối chiếu.
* Ứng dụng chỉ đọc danh mục HIS bằng `SELECT`; không tự thực hiện thao tác thay đổi dữ liệu HIS.
* Danh mục và mapping được đồng bộ trực tiếp từ SQL Server, gồm khoa/phòng, kho, tồn, thuốc/VTYT, dịch vụ, quyền dịch vụ, user HIS và bệnh nhân nguồn.
* Mỗi khoa khảo thí được cấu hình đúng một kho thi; dịch vụ phải theo `DM_PhongBan_DichVu`, không suy diễn bằng `CROSS JOIN`.
* Mỗi thí sinh dùng một user HIS không trùng trong cùng đợt; user phải đúng khoa. Mật khẩu server thi `123` là cấu hình môi trường, không lấy từ danh mục HIS.
* Bệnh nhân không được tái sử dụng; có thể tạo bệnh nhân khảo thí biến thể có kiểm soát từ dữ liệu nguồn.
* Thẻ BHYT `TE1` có hiệu lực từ `01/01/<năm thi>` đến `31/12/<năm thi + 4>`; ví dụ năm 2026 là `01/01/2026` đến `31/12/2030`.
* Đề tiếp nhận trực tiếp không sinh SQL. Sản, Nhi, Cấp cứu và Khám bệnh bắt buộc tiếp nhận trực tiếp cho cả điều dưỡng và lễ tân khi dùng template tương ứng.
* SQL chỉ được sinh cho đề bắt đầu từ nhận bệnh vào khoa mà không kiểm tra tiếp nhận trực tiếp. SQL do người vận hành chạy thủ công trên server thi; ứng dụng không tự chạy SQL.
* Nhi chỉ là biến thể nội trú có bệnh nhân trẻ em và nhóm dịch vụ đã map; không mặc định có nghiệp vụ mẹ-con hoặc tạo thẻ trẻ.
* Word dùng DOCX template theo họ đề, ưu tiên đầu ra chuẩn, đẹp, rõ ràng; không nằm trong phạm vi PDF, ký số hoặc mã hóa.

### Công việc đã thực hiện:
* Khảo sát kho đề `D:\CÔNG VIỆC\Thi\Thi_2021` và schema HIS `D:\CÔNG VIỆC\CAUTRUCHIS\schema_output_markdown`.
* Viết lại `project.md` thành đặc tả dự án và quy tắc nghiệp vụ hiện hành trước khi thực thi.
* Chốt SQLite đặt cạnh thư mục cài phần mềm, mặc định `exam-generator.sqlite`.
* Bổ sung model và service Settings cho connection profile SQL Server; người dùng sẽ cấu hình khi đưa phần mềm vào sử dụng.
* Settings chỉ là cấu hình kết nối đọc catalog; không cho phép ứng dụng tự ghi HIS.
* Tách hoàn toàn source code ứng dụng mới vào `desktop/`; solution là `desktop/ExamGenerator.Desktop.sln`. Hệ thống Python/web cũ tại root được giữ nguyên để tham khảo, không dùng chung mã thực thi.
* Triển khai MVP WPF: SQLite tự khởi tạo cạnh phần mềm, Settings lưu profile SQL Server, kiểm tra kết nối `SELECT 1`, mapping một khoa-một kho và sinh DOCX/ZIP 8 câu thử nghiệm.
* Publish bản thử nghiệm framework-dependent tại `desktop/publish/ExamOperationsDesktop/`; chưa có đồng bộ catalog HIS hoặc phân bổ dữ liệu đề thực tế.
* Khảo sát đầy đủ webapp cũ gồm sáu tab, state frontend, API, manager backend và workflow sinh đề. Bổ sung đặc tả UX desktop kế thừa vào `project.md`; tạm dừng mở rộng các tab MVP rời rạc cho đến khi thiết kế sáu phân hệ được triển khai có hệ thống.

## [2026-08-24] Chuẩn hóa DKKCB 66232, Đơn vị tính Dược, Sửa Lọc Tồn kho & Không theo dõi Data trên Git

### 1. Quyết định nghiệp vụ & Thiết kế:
*   **Chuẩn hóa Dữ liệu Bệnh nhân BHYT:**
    - Mặc định nơi đăng ký KCB ban đầu (`DKKCB`) là `66232` (Bệnh viện Đa khoa Thiện Hạnh).
    - Hạn thẻ BHYT chuẩn hóa từ đầu năm đến cuối năm của năm thi (Ví dụ: thi năm 2027 thì hạn từ `01/01/2027` đến `31/12/2027`).
*   **Trình bày Cách dùng Thuốc theo Đơn vị tính (`DVTTinh`):**
    - Bỏ các từ hành động thừa như "uống", "tiêm".
    - Trình bày trực tiếp theo ĐVT của loại dược: Ví dụ `Sáng 1 Viên, chiều 1 Viên`, `Sáng 1 Gói`, `Sáng 1 Chai, chiều 1 Chai`.
*   **Sửa Lỗi Bộ lọc Tra cứu Tồn kho Dược (Tab 5):**
    - Đồng bộ tên tham số API giữa Frontend và Backend (`department`, `warehouse`, `source`, `query`).
    - Hỗ trợ lọc theo Khoa thông qua ánh xạ kho dược (`MappingManager`), tự động cập nhật danh sách kho theo Khoa được chọn.
    - Hỗ trợ nhấn phím `Enter` và tự động tìm kiếm khi thay đổi tiêu chí lọc.
*   **Nút Lưu cấu hình Phân quyền Dịch vụ & Kho (Tab 3):**
    - Bổ sung nút "Lưu cấu hình Dịch vụ & Kho Dược" nổi bật ở cả trên và dưới bảng chi tiết.
*   **Loại bỏ Dữ liệu Test khỏi Git Repo:**
    - Cập nhật `.gitignore` loại trừ toàn bộ `data/catalogs/*.xlsx` và `data/used_patients.json`, chỉ giữ `.gitkeep`.
    - Gỡ bỏ tracking toàn bộ file catalog test khỏi Git index.

### 2. Công việc đã thực hiện:
*   Cập nhật `backend/catalog_manager.py`: Mặc định `DKKCB = 66232`, chuẩn hóa hạn thẻ `01/01/{nam}` đến `31/12/{nam}`, nâng cấp `search_inventory` hỗ trợ lọc theo Khoa qua `mapped_warehouses`.
*   Cập nhật `backend/sql_generator.py`: Mặc định `hospital_code = 66232`.
*   Cập nhật `backend/exam_actions.py`: Ghi rõ `DKKCB: 66232`, hiển thị cách dùng thuốc dựa trên `DVTTinh` chuẩn xác.
*   Cập nhật `backend/app.py`: Truyền `mapped_warehouses` cho `search_inventory`.
*   Cập nhật `frontend/index.html` & `frontend/app.js`: Thêm nút lưu cấu hình Tab 3, sửa tham số tìm kiếm Tab 5, tự động cascade kho dược theo khoa.
*   Cập nhật `.gitignore` và chạy `git rm --cached data/catalogs/*.xlsx`.
*   Chạy toàn bộ 16 backend unit tests đạt 100% OK.

## [2026-08-23] Liên kết Nghiệp vụ Xuyên suốt trong Đề thi & Hoàn thiện Tạo Đề mới

### 1. Quyết định nghiệp vụ & Thiết kế:
*   **Liên kết nghiệp vụ lâm sàng trong cùng 1 đề:**
    - Toàn bộ các thao tác *Chỉ định Cận lâm sàng (CLS)*, *Lên y lệnh Thuốc & VTYT*, *Thay đổi/Bổ sung dịch vụ kỹ thuật*, *Hoàn trả thuốc thừa* đều được thực hiện trên **cùng Bệnh nhân BHYT** đã tiếp nhận/nhận bệnh ở câu đầu tiên.
    - **Câu Đổi dịch vụ (`YL_DOI_THEM_DICH_VU`):** Bốc chính xác 1 dịch vụ đã nằm trong danh sách đã chỉ định ở câu Chỉ định CLS (`YL_CHI_DINH_CLS`) để yêu cầu hủy/đổi sang dịch vụ mới.
    - **Câu Trả thuốc/VTYT (`YL_TRA_THUOC`):** Bốc chính xác 1 mặt hàng thuốc/VTYT đã được kê đơn ở câu Lên y lệnh (`YL_CHI_DINH_THUOC_VTYT`) để yêu cầu hoàn trả lại tủ trực/kho.
*   **Cải tiến thao tác Tạo đề & Sinh đề trên Web:**
    - Nếu người dùng nhập thông tin thí sinh nhưng chưa bấm "Thêm vào danh sách" mà bấm ngay "Sinh đề thi & Xuất file" $\rightarrow$ Hệ thống tự động thêm thí sinh và sinh đề ngay lập tức.
    - Modal "Tạo Mẫu Đề Mới" (Tab 2) tự động chọn khoa phòng mặc định, kiểm tra tổng điểm 10đ và lưu qua API `POST /api/templates`.

### 2. Công việc đã thực hiện:
*   Cập nhật `backend/exam_actions.py`: Cung cấp `CandidateContext` cho tất cả 14 nghiệp vụ, liên kết bệnh nhân BHYT, lưu `ordered_services`, `ordered_drugs`, `ordered_supplies` và render đề thi Word logic, mạch lạc.
*   Cập nhật `backend/app.py`: `prepare_modular_candidate_data` khởi tạo `candidate_context` với bệnh nhân BHYT (kèm mã thẻ BHYT ngẫu nhiên an toàn) và truyền vào từng action.
*   Cập nhật `frontend/app.js`: Tự động nạp thí sinh khi sinh đề trực tiếp, hoàn thiện Modal tạo mẫu đề mới.
*   Chuẩn hóa 6/6 cặp Script SQL $\leftrightarrow$ File mẫu Excel $\leftrightarrow$ Bộ nạp Backend.
*   Đồng bộ toàn bộ mã nguồn lên Git repository GitHub: `https://github.com/buinguyenhong/ThiBVTH`.

### 3. Xác minh:
*   Toàn bộ 16 kiểm thử đơn vị (`Ran 16 tests ... OK`).
*   Chạy kịch bản tích hợp sinh đề thi mẫu liên kết 5 câu hỏi: Kiểm tra file Word xuất ra chứa đúng tên bệnh nhân BHYT xuyên suốt, câu Đổi dịch vụ hủy đúng dịch vụ câu trước, câu Trả thuốc trả đúng thuốc đã kê.

*   Tạo `Scripts/07_export_phan_quyen_dich_vu_theo_khoa.sql`: Sửa lại câu truy vấn JOIN chuẩn xác qua `DM_NhomDichVu` / `DM_LoaiDichVu` để lấy đúng tên nhóm dịch vụ thay vì chữ 'X', đồng thời kiểm tra `CoGiaDichVu = 1` và `TamNgung = 0`.
*   Cập nhật `Scripts/05_export_danh_muc_dich_vu.sql`: Trích xuất toàn bộ dịch vụ thỏa `CoGiaDichVu = 1` và `TamNgung = 0`, lấy tên nhóm trực tiếp từ bảng nhóm/loại dịch vụ để không bị sót dịch vụ.
*   Cập nhật `frontend/app.js`: Tự động sao chép Script SQL trực tiếp vào clipboard (kèm fallback đa nền tảng) và hiện Toast thông báo khi bấm nút "Lấy script" trên mọi máy trạm.
*   Tạo 2 file mẫu Excel: `data/catalogs/service_mappings.xlsx` và `data/catalogs/pharmacy_mappings.xlsx` cùng nút "Tải file mẫu" trên giao diện.
*   Đồng bộ toàn bộ mã nguồn lên Git repository GitHub: `https://github.com/buinguyenhong/ThiBVTH`.

### 3. Xác minh:
*   Toàn bộ 16 kiểm thử đơn vị và tích hợp trong backend đều đạt (`Ran 16 tests ... OK`).
*   Kiểm tra import file Excel phân quyền dịch vụ và ánh xạ kho dược thành công.

### 4. Các tệp đã thêm hoặc sửa đổi:
*   `Scripts/07_export_phan_quyen_dich_vu_theo_khoa.sql` [NEW]
*   `Scripts/08_export_anh_xa_kho_duoc_theo_khoa.sql` [NEW]
*   `backend/mapping_manager.py`
*   `backend/app.py`
*   `backend/test_modular_system.py`
*   `frontend/index.html`
*   `frontend/style.css`
*   `frontend/app.js`
*   `project.md`
*   `agentlog.md`

---

## [2026-07-28] Bổ sung tra cứu tồn kho Dược/Vật tư theo khoa

### 1. Quyết định nghiệp vụ:
*   `SoLuongTon` là tổng số dư hiện hành trong `DuocTonKho` theo thuốc + kho + nguồn tại lúc chạy script, không phải tồn lịch sử theo ngày thi.
*   `drugs.xlsx` là ảnh chụp tĩnh; upload xong không tự đồng bộ tiếp với HIS.
*   Script mới dùng `READ COMMITTED`, ghi `ThoiDiemChotTon = SYSDATETIME()` và bổ sung `TenKho`, `KhoaPhong` từ quan hệ `DM_KhoDuoc.PhongBan_Id`.

### 2. Công việc đã thực hiện:
*   Mở rộng định dạng `drugs.xlsx` từ 7 lên 10 cột, đồng thời giữ khả năng đọc file 7 cột cũ.
*   Thêm API `/api/inventory/options` và `/api/inventory/search` để lọc ảnh chụp tồn kho theo khoa/phòng, kho, nguồn, tồn tối thiểu và mã/tên dược.
*   Thêm tab **Tra cứu tồn kho** trên giao diện, hiển thị rõ dữ liệu không phải thời gian thực và cảnh báo khi file cũ chưa có ánh xạ khoa.
*   Bộ đọc danh mục bỏ riêng dòng dược sai cấu trúc thay vì dừng và mất toàn bộ các dòng phía sau.
*   Cô lập thư mục output trong kiểm thử API để không xóa hoặc khóa kết quả thật đang được server sử dụng.
*   Bổ sung cơ chế xác minh dự phòng an toàn cho `stop-server.ps1` khi Windows không cho đọc command line: PID phải đồng thời sở hữu cổng 8000, trả đúng tiêu đề OpenAPI và xuất hiện trong log khởi động.

### 3. Xác minh:
*   6 kiểm thử `CatalogManager` đạt, gồm lọc theo khoa/từ khóa và tương thích file 7 cột.
*   Kiểm tra cú pháp JavaScript đạt.
*   Toàn bộ kiểm thử API, script danh mục, tra cứu tồn kho và sinh ZIP đạt.
*   `drugs.xlsx` hiện tại là định dạng cũ và có một dòng sai cấu trúc tại dòng 4551; hệ thống bỏ dòng này và nạp thành công 9.474 dòng còn lại. Cần xuất/upload lại bằng script mới để có `KhoaPhong` và `ThoiDiemChotTon`.
*   Server cũ PID `12808` chạy bằng quyền cao hơn nên Windows từ chối dừng từ phiên thường. Đã thêm helper có xác minh PID/cổng/OpenAPI, thực hiện khởi động lại qua UAC thành công và xác nhận server mới PID `28584` phục vụ API tồn kho cùng frontend phiên bản `20260728-1`.

### 4. Các tệp đã sửa đổi:
*   `Scripts/03_export_danh_muc_thuoc_vat_tu.sql`
*   `backend/catalog_manager.py`
*   `backend/app.py`
*   `backend/create_catalog_templates.py`
*   `backend/test_catalog_manager.py`
*   `backend/test_app.py`
*   `frontend/index.html`
*   `frontend/app.js`
*   `frontend/style.css`
*   `tools/stop-server.ps1`
*   `tools/restart-server-elevated.ps1`
*   `README.md`
*   `project.md`
*   `implementation_plan.md`
*   `agentlog.md`

---

## [2026-07-27] Chuẩn hóa tên khoa và tách luồng đề Tin học văn phòng

### 1. Quyết định nghiệp vụ:
*   `users.xlsx` là nguồn chuẩn để ánh xạ user cho các vị trí thi có sử dụng HIS.
*   Chuẩn hóa các tên khoa/phòng trong ma trận đề theo danh mục mới, gồm `Khoa Phụ Sản`, `Khoa Ngoại tổng hợp`, `Khoa Chấn thương chỉnh hình`, `Khoa Ngoại thần kinh`, `Khoa Phẫu thuật - GMHS`, `Khoa Xét Nghiệm`, `Thận nhân tạo`, `Phòng Tài chính kế toán` và `Phòng Tổ chức - Hành chính`.
*   Chỉ hiển thị vị trí HIS khi `users.xlsx` có ít nhất một user thuộc đúng khoa/phòng; tuyệt đối không lấy user của khoa khác.
*   Hai vị trí Chăm sóc khách hàng và Hành chính văn phòng là luồng `OFFICE_ADMIN` độc lập, chỉ thi Microsoft Word/Excel và không phụ thuộc user hoặc dữ liệu HIS.

### 2. Công việc đã thực hiện:
*   Đánh dấu hai mẫu `OFFICE_ADMIN` bằng `uses_his = False`.
*   Bỏ tra cứu user, phiên chọn bệnh nhân, SQL tổng hợp và script tra cứu HIS khỏi lô đề chỉ có vị trí văn phòng.
*   Đổi tên ZIP độc lập thành `De_Thi_Tin_Hoc_Van_Phong_<thời gian>.zip`.
*   Tạo riêng cho mỗi thí sinh một đề DOCX và một file XLSX gồm 20 dòng dữ liệu giả lập, có hai sheet `DuLieu` và `DanhMucKhoa` để thực hành SUM, AVERAGE, IF, VLOOKUP, lọc và vẽ biểu đồ.
*   Bỏ nội dung nạp CSDL/HIS khỏi đề văn phòng và sửa quy tắc tên file để xử lý an toàn ký tự `/` trên Windows.

### 3. Xác minh:
*   4 kiểm thử `CatalogManager` và 5 kiểm thử bộ sinh SQL đạt.
*   API trả đủ 36 mẫu, trong đó hai mẫu `OFFICE_ADMIN` không yêu cầu user.
*   Kiểm thử lô CSKH + Hành chính tạo đúng 2 DOCX và 2 XLSX, không tạo SQL, không kèm script HIS và không mở phiên chọn bệnh nhân.
*   Mã nguồn đã cập nhật; tiến trình server PID `2348` vẫn cần được khởi động lại bằng quyền đã dùng để chạy server.

### 4. Các tệp đã sửa đổi:
*   `backend/app.py`
*   `backend/catalog_manager.py`
*   `backend/exam_templates.py`
*   `backend/exam_generator.py`
*   `backend/sql_generator.py`
*   `backend/test_app.py`
*   `backend/test_catalog_manager.py`
*   `backend/test_sql_generator.py`
*   `README.md`
*   `project.md`
*   `implementation_plan.md`
*   `agentlog.md`

---

## [2026-07-25] Tích hợp tài liệu tiếp nhận/nhập khoa vào bộ sinh đề

### 1. Công việc đã thực hiện:
*   Đối chiếu tài liệu mới với schema và mã nguồn stored procedure tại `D:\CÔNG VIỆC\CAUTRUCHIS`.
*   Thay bộ sinh SQL mô phỏng bằng bộ render trực tiếp từ `Scripts/02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql`.
*   Tự phân giải ID cho user, khoa/phòng, bác sĩ, dân tộc, nghề nghiệp, tỉnh/thành, ICD, tuyến BHYT và bệnh viện ĐKKCB.
*   Đề nội trú sinh hai hàng chờ vào khoa cho bệnh nhân BHYT/viện phí; mặc định rollback và bắt buộc `BenhAn_Id` là `NULL`.
*   Đề tiếp nhận trực tiếp chỉ kiểm tra thẻ BHYT chưa tồn tại để thí sinh tự thực hiện nghiệp vụ.
*   Sinh số BHYT khảo thí duy nhất theo từng thí sinh, giữ nguyên tiền tố quyền lợi/tỉnh và không sử dụng số thẻ thật làm khóa tạo mới.
*   Thêm `Scripts/01_lookup_tiep_nhan_nhap_vien.sql` vào dự án và tự động đóng gói trong ZIP.
*   Loại bỏ hoàn toàn SQL sinh vào các bảng giả `HS_TIEPNHAN`, `CLS_YLENH` và `DM_DUOC_KHO`.

### 2. Xác minh:
*   4 kiểm thử đơn vị của bộ sinh SQL đã đạt.
*   Kiểm thử API tạo 7 đề, SQL tổng hợp và ZIP đã đạt.
*   Tất cả named parameters được sinh đều tồn tại trong 5 stored procedure đối chiếu từ snapshot `CAUTRUCHIS`.

### 3. Các tệp đã thêm hoặc sửa đổi:
*   `backend/sql_generator.py`
*   `backend/app.py`
*   `backend/catalog_manager.py`
*   `backend/exam_templates.py`
*   `backend/exam_generator.py`
*   `backend/test_sql_generator.py`
*   `backend/test_app.py`
*   `Scripts/01_lookup_tiep_nhan_nhap_vien.sql`
*   `README.md`
*   `project.md`
*   `implementation_plan.md`
*   `agentlog.md`

---

## [2026-07-25] Bổ sung tài liệu T-SQL tiếp nhận trực tiếp và chỉ định vào khoa

### 1. Công việc đã thực hiện:
*   Thêm kịch bản `Scripts/02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql`.
*   Kịch bản hỗ trợ hồ sơ viện phí và BHYT; tạo `DM_BenhNhan`, `DM_BenhNhan_BHYT` khi cần, `TiepNhan` và `NoiTru_NhapVien`.
*   Giữ giới hạn an toàn: mặc định `@Commit = 0`, không gọi `AddNewAndCreateBenhAn`, không tạo `BenhAn`, `NoiTru_LuuTru` hoặc số bệnh án.
*   Bổ sung hướng dẫn sử dụng ngắn trong `README.md` và đăng ký tệp trong bản đồ trạng thái của `project.md`.

### 2. Các tệp đã thêm hoặc sửa đổi:
*   `Scripts/02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql`
*   `README.md`
*   `project.md`
*   `agentlog.md`

---

## [2026-07-22] Chuẩn hóa 6 Loại Đề Thi theo Khoa Phòng & Tùy chỉnh Thang điểm

### 1. Công việc đã thực hiện:
*   **Chuẩn hóa 6 Loại Đề Thi:**
    1. `NURSE_INPATIENT` (Điều dưỡng / Nữ hộ sinh Nội trú - Khoa Nội, Ngoại TH, Ngoại CTCH, Ngoại TK, Hồi sức tích cực).
    2. `NURSE_DIRECT_RECEPTION` (Điều dưỡng / Nữ hộ sinh Tiếp nhận trực tiếp - Cấp cứu, Sản, Nhi).
    3. `NURSE_OUTPATIENT` (Điều dưỡng Ngoại trú - Mắt, TMH, RHM, Khám bệnh, Thận nhân tạo).
    4. `TECHNICIAN` (Kỹ thuật viên Cận lâm sàng - Xét nghiệm, CĐHA, Nội soi, Gây mê hồi sức, Tim mạch, Dược sĩ).
    5. `CASHIER_RECEPTION` (Lễ tân & Thu ngân - Lễ tân khoa, Quầy thu phí).
    6. `OFFICE_ADMIN` (Hành chính / Văn phòng - Chăm sóc khách hàng, Văn phòng).
*   **Lọc Danh mục Theo Khoa:** Nâng cấp [catalog_manager.py](file:///d:/Project/ThiBVTH/ExamGenerator/backend/catalog_manager.py) để ưu tiên chọn Thuốc và Dịch vụ CLS/thủ thuật phù hợp với từng chuyên khoa.
*   **Mốc thời gian động (30 ngày):** Tự động tính toán mốc thời gian lùi `exam_date - 30 ngày` cho các câu hỏi chuyển khoa hoặc cho ra viện ca bệnh cũ.
*   **In Mẫu Kết quả KTV:** Đề thi Word cho Kỹ thuật viên tự động in bảng/khung kết quả Huyết học, Hóa sinh, CĐHA, Nội soi mẫu trực tiếp lên đề thi.
*   **Tùy chỉnh Thang điểm & Bỏ câu:** Bổ sung giao diện UI [index.html](file:///d:/Project/ThiBVTH/ExamGenerator/frontend/index.html) và [app.js](file:///d:/Project/ThiBVTH/ExamGenerator/frontend/app.js) cho phép nhập điểm từng câu hoặc chọn "Bỏ qua câu" (0 điểm). Đề thi Word tự động ẩn các câu 0 điểm và re-index lại số thứ tự câu.

### 2. Các tệp đã sửa đổi:
*   [project.md](file:///d:/Project/ThiBVTH/ExamGenerator/project.md)
*   [catalog_manager.py](file:///d:/Project/ThiBVTH/ExamGenerator/backend/catalog_manager.py)
*   [exam_templates.py](file:///d:/Project/ThiBVTH/ExamGenerator/backend/exam_templates.py)
*   [exam_generator.py](file:///d:/Project/ThiBVTH/ExamGenerator/backend/exam_generator.py)
*   [app.py](file:///d:/Project/ThiBVTH/ExamGenerator/backend/app.py)
*   [index.html](file:///d:/Project/ThiBVTH/ExamGenerator/frontend/index.html)
*   [app.js](file:///d:/Project/ThiBVTH/ExamGenerator/frontend/app.js)
*   [test_app.py](file:///d:/Project/ThiBVTH/ExamGenerator/backend/test_app.py)

---

## [2026-07-16] Tái cấu trúc Giao diện Tabs & Bổ sung Quản lý Danh mục dữ liệu

### 1. Công việc đã thực hiện:
*   **Phân chia Tab:** Tách biệt giao diện thành hai Tab chính: "Thực thi tạo đề" (Mặc định) và "Cài đặt & Danh mục" trên frontend.
*   **Bộ chọn Khoa/Vị trí động:** Bỏ trường nhập liệu SBD. Thiết kế lại biểu mẫu thêm thí sinh gồm Họ tên, Khoa và Vị trí (dropdown lọc động danh mục vị trí theo khoa).
*   **Tải mẫu & Upload:** Bổ sung API tải xuống file Excel mẫu (`GET /api/catalogs/download/{filename}`) và tải lên trực tiếp ghi đè danh mục (`POST /api/catalogs/upload/{filename}`).
*   **Xem trước Danh mục:** Thêm tính năng "Xem dữ liệu" gọi API preview (`GET /api/catalogs/data/{filename}`) và kết xuất dưới dạng bảng cuộn tối đa 100 dòng bên trong Modal popup.
*   **Xử lý SBD trống:** Điều chỉnh logic xử lý ở cả frontend và backend (FastAPI, docx generator, sql generator) để cho phép sinh đề không có SBD và không in dòng SBD trống.
*   **Fix lỗi mã hóa:** Khắc phục lỗi `cp1252` / `UnicodeEncodeError` khi sinh tên file chứa dấu tiếng Việt trên Windows bằng cách cấu hình `stdout` sang UTF-8.

### 2. Các tệp đã sửa đổi:
*   [app.py](file:///d:/Project/ThiBVTH/ExamGenerator/backend/app.py)
*   [exam_generator.py](file:///d:/Project/ThiBVTH/ExamGenerator/backend/exam_generator.py)
*   [sql_generator.py](file:///d:/Project/ThiBVTH/ExamGenerator/backend/sql_generator.py)
*   [index.html](file:///d:/Project/ThiBVTH/ExamGenerator/frontend/index.html)
*   [style.css](file:///d:/Project/ThiBVTH/ExamGenerator/frontend/style.css)
*   [app.js](file:///d:/Project/ThiBVTH/ExamGenerator/frontend/app.js)

---

## [2026-07-16] Khởi tạo Dự án và Thiết lập Môi trường

### 1. Công việc đã thực hiện:
*   **Khảo sát đề thi cũ:** Đọc và phân loại 260+ đề thi trong thư mục `D:\CÔNG VIỆC\Thi`, bóc tách cấu trúc câu hỏi của 4 nhóm vị trí chính: Lễ tân/Thu phí, Lâm sàng (Nội/Ngoại/Cấp cứu/Sản...), Cận lâm sàng (Siêu âm/Xét nghiệm) và Dược sĩ.
*   **Thông qua Kế hoạch triển khai:** Đã lập [implementation_plan.md](file:///C:/Users/Admin/.gemini/antigravity/brain/3942aa94-1761-4858-b8f7-cd316036f375/implementation_plan.md) mô tả kiến trúc Local WebApp (FastAPI + HTML/CSS/JS) và định dạng Excel danh mục đầu vào.
*   **Thiết lập Task list:** Tạo [task.md](file:///C:/Users/Admin/.gemini/antigravity/brain/3942aa94-1761-4858-b8f7-cd316036f375/task.md) để theo dõi các đầu việc.
*   **Khởi tạo tệp tin dự án:**
    *   Tạo file thông tin dự án [project.md](file:///C:/Users/Admin/.gemini/antigravity/scratch/ExamGenerator/project.md) chỉ định các quy tắc Vibe Coding chuyên nghiệp.
    *   Tạo file nhật ký [agentlog.md](file:///C:/Users/Admin/.gemini/antigravity/scratch/ExamGenerator/agentlog.md) (File này).


---

## [2026-09-23] Hỗ trợ Mapping nhiều Kho Dược cho 1 Khoa & Chọn 1 Kho cụ thể khi Tạo Đề Thi

### 1. Công việc đã thực hiện:
*   **Domain & CSDL (`SqliteDatabase`, `ExamRules`):**
    *   Tạo bảng `DepartmentWarehouseMapping` (`DepartmentCode`, `DepartmentName`, `WarehouseCode`, `WarehouseName`, `CreatedAt`) với khoá chính kép `(DepartmentCode, WarehouseCode)`.
    *   Tự động di chuyển dữ liệu (Migration) từ bảng cũ `DepartmentConfiguration` sang `DepartmentWarehouseMapping`.
    *   Bổ sung phương thức `SaveDepartmentWarehouseMappingsAsync`, `GetDepartmentWarehouseMappingsAsync`, `GetWarehousesForDepartmentAsync`.
    *   Duy trì `GetDepartmentConfigurationsAsync` và `SaveDepartmentConfigurationAsync` để tương thích ngược.
    *   Điều chỉnh quy tắc Rule 3.3 trong `ExamBatchRules`: Một khoa có thể có $\ge 1$ kho thi. Bổ sung `ValidateCandidateWarehouseSelections`: Mỗi thí sinh khi thi phải chọn 1 kho cụ thể trong danh sách kho đã map của khoa mình.
*   **Thuật toán Phân bổ Đề thi (`ExamScenarioAllocator`):**
    *   Bổ sung `WarehouseCode` và `WarehouseName` vào `CandidateInput`.
    *   Tách biệt bộ nhớ đệm tồn kho (`warehouseStockMap`, `warehouseDrugsCache`) theo từng kho cụ thể.
    *   Thí sinh cùng khoa chọn các kho khác nhau (hoặc cùng kho) sẽ được cấp thuốc và kiểm tra tồn kho (Câu 6) tương ứng chính xác với kho đó mà không làm cạn kiệt lẫn nhau.
*   **Giao diện Desktop (`MainWindow.xaml`, `MainWindow.xaml.cs`):**
    *   **Tab 3 (Phân quyền Dịch vụ & Kho):** Chuyển từ ComboBox 1 kho sang danh sách CheckBox `WarehouseMappingsList` kèm ô tìm kiếm và 2 nút "Chọn tất cả" / "Bỏ chọn tất cả", cho phép tick chọn nhiều kho cho một khoa phòng.
    *   **Tab 1 (Thực thi tạo đề):** Bổ sung ComboBox `ExamWarehouseComboBox` (chọn kho dược thi). Khi đổi Khoa thi, tự động lọc và nạp danh sách các kho đã map của khoa đó. Thêm cột `Kho Dược thi` trong `CandidatesGrid` hiển thị kho đã chọn của từng thí sinh.
*   **Kiểm thử & Đóng gói:**
    *   Bổ sung 3 unit tests mới trong `ExamRulesTests`, `ExamScenarioAllocatorTests`, `CatalogSynchronizationTests`. Toàn bộ 30/30 unit tests PASSED.
    *   Chạy build Release và cập nhật cả 2 bản:

---

## [2026-09-23] Bổ sung Dữ liệu Bệnh nhân Tiếp nhận Trực tiếp & 4 Dạng Câu hỏi Mới vào Thư viện HIS

### 1. Công việc đã thực hiện:
*   **Tiếp nhận trực tiếp (`ExamScenarioAllocator`):**
    *   Cập nhật `InstructionForAction`: Khi tạo câu hỏi Tiếp nhận trực tiếp (`TN_TIEP_NHAN` hoặc `isDirectReception`), hệ thống tự động trích xuất đầy đủ thông tin hành chính của bệnh nhân (`patient`) gồm: Họ và tên (in hoa), Ngày sinh, Tuổi, Giới tính, Địa chỉ, Đối tượng (BHYT/Viện phí), Số thẻ BHYT, Hạn thẻ BHYT, Nơi ĐKKCB ban đầu, Chẩn đoán ban đầu.
    *   Bổ sung dòng điền: `- Số vào viện (SVV) / Mã tiếp nhận sau khi lưu: ………………………………` để thí sinh ghi nhận kết quả thao tác trên phần mềm HIS.
*   **Thêm 4 dạng câu hỏi chuẩn vào Thư viện Nghiệp vụ HIS (`SystemActionCatalog`, `SqliteDatabase`):**
    1.  `NT_CAP_GIUONG` (1.0 đ) - Nhóm *Điều trị Nội trú*: `"Cấp giường cho các bệnh nhân được tiếp nhận ở câu 1"`
    2.  `KB_CHI_DINH_CLS` (2.0 đ) - Nhóm *Khám bệnh & Ngoại trú*: Chỉ định CLS ngoại trú, tự động random 2 dịch vụ X-quang & Siêu âm khi đưa vào đề thi, kèm chỗ trống cho người chấm điền Tên bệnh nhân và Mã y tế.
    3.  `KB_BENH_AN_NGOAI_TRU` (2.0 đ) - Nhóm *Khám bệnh & Ngoại trú*: Chỉ định điều trị ngoại trú và lập bệnh án ngoại trú kèm dòng điền `Số bệnh án ngoại trú`.
    4.  `KB_CHI_DINH_NOI_TRU` (1.0 đ) - Nhóm *Khám bệnh & Ngoại trú*: Chỉ định nhập viện điều trị nội trú, tự động gán Khoa chuyển đến và dòng điền `Số bệnh án nội trú`.
*   **Cập nhật CSDL SQLite (`SqliteDatabase.InitializeAsync`):**
    *   Cơ chế `ON CONFLICT` tự động nạp bổ sung 4 dạng câu hỏi mới vào bảng `ActionDefinition` khi khởi chạy mà không ghi đè câu hỏi tự tạo của người dùng.
*   **Kiểm thử & Đóng gói:**
    *   Bổ sung 2 bài unit tests mới trong `ExamScenarioAllocatorTests`: kiểm tra in dữ liệu bệnh nhân tiếp nhận và phân bổ các câu hỏi ngoại trú / nội trú mới. Đạt **32/32 Tests PASSED 100%**.
    *   Build Release và phát hành:
        *   `desktop/publish/ExamOperationsDesktop/ExamGenerator.Desktop.exe`
        *   `desktop/publish/ExamOperationsDesktop-Standalone/ExamGenerator.Desktop.exe` (Standalone win-x64).



---

## [2026-09-23] Chuẩn hóa Bộ sinh Script SQL HIS: Stored Procedures Chuẩn, Tra cứu BHYT Đa tầng & Tái sử dụng Hồ sơ Bệnh nhân

### 1. Bối cảnh & Vấn đề:
* Khi chạy kịch bản nạp bệnh nhân vào hàng chờ tiếp nhận nội trú bằng file SQL tổng hợp (`SQL_TongHop_TatCaThiSinh_20260924.sql`), hệ thống gặp các lỗi dừng nghiệp vụ (`THROW 51000`):
  1. `Msg 51000: BHYT cần loại, hạn thẻ, tuyến và nơi đăng ký KCB.` (dòng 561): Do truy vấn tra cứu ID tuyến khám (`@TuyenKhamBenh_Id`) và nơi ĐKKCB ban đầu (`@BenhVien_KCB_Id`) lọc cứng theo chuỗi mã không khớp với thực tế dữ liệu trong `Lst_Dictionary` và `DM_BenhVien`.
  2. `Msg 51000: Số thẻ BHYT đã thuộc một bệnh nhân đang hoạt động; không tạo bệnh nhân mới trùng thẻ.` (dòng 704): Do các ca bệnh thi tuyển trích xuất từ dữ liệu thực tế/test (`patients.xlsx`) đã có sẵn thông tin thẻ trong `DM_BenhNhan_BHYT`, trong khi script mẫu cũ áp đặt quy tắc chặn cứng không cho phép trùng thẻ.

### 2. Các thay đổi và giải pháp đã thực hiện:
* **Chuẩn hóa quy trình Stored Procedure eHospital:**
  * Thay thế toàn bộ các câu lệnh `INSERT` bảng trực tiếp cũ bằng luồng gọi thủ tục lưu trữ chuẩn của HIS Thiện Hạnh:
    * `sp_DM_BENHNHAN @Action = 'AddNew'`
    * `sp_LST_KEYDATA` để cấp mã tiếp nhận và số thứ tự tự động
    * `sp_DM_BENHNHAN_BHYT @Action = 'AddNew'`
    * `sp_TIEPNHAN @Action = 'AddNew'`
    * `sp_NOITRU_NHAPVIEN @Action = 'AddNew'`
  * Đảm bảo không gọi `AddNewAndCreateBenhAn`, không sinh `BenhAn_Id` trước để thí sinh tự nhận bệnh vào khoa.
* **Cơ chế Phân giải Danh mục BHYT Đa tầng (Multi-tier Fallback):**
  * `@TuyenKhamBenh_Id`: Khớp mã `TuyenKhamChuaBenh_TrongTuyen` -> Khớp mã `TrongTuyen` / `DungTuyen` / `DT` / `TT` / `1` -> Khớp tên tiếng Việt `đúng tuyến` / `trong tuyến` -> Lấy giá trị từ lần tiếp nhận gần nhất trong `TiepNhan`.
  * `@BenhVien_KCB_Id`: Khớp chuẩn hóa bỏ dấu gạch/chấm (`REPLACE`) giữa `MaBenhVien` và `66232` -> Khớp `TenBenhVien_En` -> Khớp tên chứa `Thiện Hạnh` -> Lấy từ cấu hình hệ thống `Sys_AppSettings` (`MaNoiKCBBanDau`, `MaCSKCB`) -> Lấy từ lượt tiếp nhận gần nhất -> Lấy bệnh viện hoạt động đầu tiên.
  * `@LoaiBHYT`: Tra cứu qua `TiepNhanLoaiBHYT`, `LoaiBHYT`, `Dictionary_Type_Id = 101`, lịch sử tiếp nhận; cho phép linh hoạt không bắt buộc dừng script.
* **Cơ chế Tái tiếp nhận Bệnh nhân cũ (Re-admission):**
  * Nếu thẻ BHYT đã tồn tại trong `DM_BenhNhan_BHYT`: Hệ thống tự động tái sử dụng `BenhNhan_Id` và `BenhNhan_BHYT_Id` có sẵn, bỏ qua việc tạo mới bệnh nhân trùng lặp.
  * Tự động sinh `TiepNhan` mới và chỉ định `NoiTru_NhapVien` mới ở trạng thái `ChoTiepNhan`, đúng với thực tế tiếp nhận tái khám/nhập viện lại của bệnh viện.
* **Đồng bộ toàn diện Codebase:**
  * File tài liệu T-SQL nguồn: `Scripts/02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql`
  * Bộ sinh Backend Python: `backend/sql_generator.py`
  * Bộ sinh Desktop C#: `desktop/src/ExamGenerator.Infrastructure/SqlScriptGenerator.cs`

### 3. Kết quả Kiểm thử & Phát hành:
* Toàn bộ **34/34 tests** đơn vị trong `desktop/tests/ExamGenerator.Tests` đạt **100% Passed**.
* Biên dịch và đóng gói thành công cả 2 bản phát hành:
  * `desktop/publish/ExamOperationsDesktop` (Framework-dependent)
  * `desktop/publish/ExamOperationsDesktop-Standalone` (Self-contained win-x64)
* Đã cập nhật và sinh lại toàn bộ 8 file cá nhân và file gộp:
  `desktop/publish/DeThi_TuyenDung_2026_20260923_171935/SQL_TongHop_TatCaThiSinh_20260924.sql`
* Đã chạy kiểm thử thành công trên CSDL eHospital_ThienHanh với `@Commit = 0` (Rollback an toàn, không còn lỗi `Msg 51000`).

---

## [2026-09-23] Cáº­p nháº­t Báº£n quyá»n Giao diá»‡n NgÆ°á»i dÃ¹ng

### 1. CÃ´ng viá»‡c Ä‘Ã£ thá»±c hiá»‡n:
* **Giao diá»‡n Desktop WPF (MainWindow.xaml):**
  * TiÃªu Ä‘á» phá»¥ (Header): Äá»•i tá»« "Há»™i Äá»“ng Thi Tuyá»ƒn Dá»¥ng Â· Exam Operations Desktop (HIS eHospital)" thÃ nh "Copyright Â© 2026 BÃ¹i NguyÃªn Há»“ng - CNTT - Thiá»‡n Háº¡nh . All rights reserved.".
  * ChÃ¢n trang (Footer): Cáº­p nháº­t thÃ nh "PhiÃªn báº£n 2026.09 Â· Copyright Â© 2026 BÃ¹i NguyÃªn Há»“ng - CNTT - Thiá»‡n Háº¡nh . All rights reserved.".
* **Giao diá»‡n Web cÅ© (frontend/index.html):**
  * Äá»“ng bá»™ tiÃªu Ä‘á» phá»¥ thÃ nh "Copyright Â© 2026 BÃ¹i NguyÃªn Há»“ng - CNTT - Thiá»‡n Háº¡nh . All rights reserved.".

### 2. Kiá»ƒm thá»­ & ÄÃ³ng gÃ³i:
* ToÃ n bá»™ **34/34 Unit Tests PASSED (100%)**.
* ÄÃ³ng gÃ³i láº¡i thÃ nh cÃ´ng cáº£ 2 báº£n phÃ¡t hÃ nh:
  * `desktop/publish/ExamOperationsDesktop/`
  * `desktop/publish/ExamOperationsDesktop-Standalone/`


---

## [2026-09-27] Triá»ƒn khai Mapping User HIS theo Khoa, Tá»‘i Æ°u Báº£ng DÆ°á»£c vÃ  Cá»‘ Ä‘á»‹nh Thá»i gian Tá»“n kho

### 1. CÃ´ng viá»‡c Ä‘Ã£ thá»±c hiá»‡n:
* **Mapping User HIS theo Khoa (Tab 3 & SQLite):**
  * Bá»• sung báº£ng `DepartmentUserMapping` (DepartmentCode, DepartmentName, UserId, UserName, FullName, UpdatedAt) trong SQLite.
  * Cung cáº¥p cÃ¡c phÆ°Æ¡ng thá»©c `SaveDepartmentUserMappingsAsync`, `GetDepartmentUserMappingsAsync`, `GetMappedUsersForDepartmentAsync` trong `SqliteDatabase.cs`.
  * Bá»• sung giao diá»‡n trong Tab 3 (`MainWindow.xaml`): Danh sÃ¡ch CheckBox User HIS cá»§a khoa, Ã´ tÃ¬m kiáº¿m debounce theo Username/FullName, nÃºt "Chá»n táº¥t cáº£" / "Bá» chá»n táº¥t cáº£", vÃ  hiá»ƒn thá»‹ tá»•ng sá»‘ user Ä‘Ã£ chá»n.
  * Khi báº¥m "LÆ°u Cáº¥u HÃ¬nh Khoa NÃ y", há»‡ thá»‘ng lÆ°u Ä‘á»“ng thá»i Kho thi, NhÃ³m dá»‹ch vá»¥ vÃ  Danh sÃ¡ch User HIS kháº£o thÃ­ Ä‘Ã£ chá»n.
* **Preflight Check & PhÃ¢n bá»• Random User (`ExamScenarioAllocator.cs`):**
  * Cháº·n táº¡o Ä‘á» (Fail-fast) náº¿u khoa cÃ³ thÃ­ sinh Ä‘Äƒng kÃ½ nhÆ°ng chÆ°a Ä‘Æ°á»£c chá»n User HIS nÃ o trong Tab 3 (`Khoa '{deptName}' chÆ°a Ä‘Æ°á»£c cáº¥u hÃ¬nh danh sÃ¡ch User HIS thi trong Tab 3. Vui lÃ²ng vÃ o Tab 3 Ä‘á»ƒ chá»n user.`).
  * Cháº·n táº¡o Ä‘á» (Fail-fast) náº¿u sá»‘ thÃ­ sinh > sá»‘ user Ä‘Ã£ map cá»§a khoa (`Khoa '{deptName}' cÃ³ {group.Count()} thÃ­ sinh nhÆ°ng chá»‰ cáº¥u hÃ¬nh {deptUsers.Count} User HIS kháº£ dá»¥ng. KhÃ´ng Ä‘á»§ sá»‘ lÆ°á»£ng user Ä‘á»ƒ táº¡o Ä‘á».`).
  * XÃ¡o trá»™n ngáº«u nhiÃªn (random shuffle) cÃ¡c user Ä‘Ã£ mapping cá»§a khoa vÃ  cáº¥p cho tá»«ng thÃ­ sinh, Ä‘áº£m báº£o khÃ´ng trÃ¹ng láº·p user trong cÃ¹ng khoa.
* **Tá»‘i Æ°u Báº£ng DÆ°á»£c / VTYT ÄÃ­nh kÃ¨m (`ExamScenarioAllocator.cs`, `WordExamWriter.cs`):**
  * Äá» chá»‰ phÃ¢n bá»• dÆ°á»£c vÃ  in báº£ng DÆ°á»£c/VTYT khi máº«u Ä‘á» cÃ³ cÃ¢u lÃªn y lá»‡nh thuá»‘c (`YL_CHI_DINH_THUOC_VTYT`).
  * Vá»›i cÃ¡c Ä‘á» khÃ´ng cÃ³ cÃ¢u nÃ y (KTV XÃ©t nghiá»‡m, CÄHA, Lá»… tÃ¢n, Thu ngÃ¢n...), há»‡ thá»‘ng khÃ´ng phÃ¢n bá»• dÆ°á»£c, khÃ´ng trá»« tá»“n kho lÅ©y káº¿ cá»§a kho thi vÃ  áº©n hoÃ n toÃ n báº£ng DÆ°á»£c Ä‘Ã­nh kÃ¨m trong file Word.
* **Cá»‘ Ä‘á»‹nh Khoáº£ng Thá»i gian CÃ¢u há»i Tá»“n kho (`ExamScenarioAllocator.cs`):**
  * CÃ¢u há»i kiá»ƒm tra tá»“n kho (`TK_KIEM_TON_KHO`) Ä‘Æ°á»£c cá»‘ Ä‘á»‹nh khoáº£ng thá»i gian 1 thÃ¡ng: `tá»« ngÃ y 01/02/2026 Ä‘áº¿n ngÃ y 28/02/2026`.

### 2. Kiá»ƒm thá»­ & ÄÃ³ng gÃ³i:
* Bá»• sung 3 bÃ i unit tests má»›i, nÃ¢ng tá»•ng sá»‘ test lÃªn **37/37 Tests PASSED (100%)**.
* ÄÃ³ng gÃ³i thÃ nh cÃ´ng cáº£ 2 báº£n Release:
  * `desktop/publish/ExamOperationsDesktop/` (Báº£n tiÃªu chuáº©n)
  * `desktop/publish/ExamOperationsDesktop-Standalone/` (Báº£n Standalone win-x64)


---

## [2026-09-27] NÃ¢ng cáº¥p CÃ¢u Tiáº¿p Nháº­n Trá»±c Tiáº¿p: Äáº§y Ä‘á»§ 2 Bá»‡nh NhÃ¢n (BHYT & Viá»‡n PhÃ­)

### 1. CÃ´ng viá»‡c Ä‘Ã£ thá»±c hiá»‡n:
* **Má»Ÿ rá»™ng ná»™i dung CÃ¢u há»i Tiáº¿p nháº­n trá»±c tiáº¿p (`BuildDirectReceptionInstruction` trong `ExamScenarioAllocator.cs`):**
  * Hiá»ƒn thá»‹ Ä‘áº§y Ä‘á»§ thÃ´ng tin hÃ nh chÃ­nh cá»§a cáº£ 2 bá»‡nh nhÃ¢n:
    * **1. Bá»‡nh nhÃ¢n Báº£o hiá»ƒm y táº¿ (BHYT):** Há» tÃªn, NgÃ y sinh, Giá»›i tÃ­nh, Äá»‹a chá»‰, Äá»‘i tÆ°á»£ng BHYT, Sá»‘ tháº» BHYT, Háº¡n tháº», NÆ¡i ÄKKCB ban Ä‘áº§u, Cháº©n Ä‘oÃ¡n ban Ä‘áº§u.
    * **2. Bá»‡nh nhÃ¢n Viá»‡n phÃ­:** Há» tÃªn, NgÃ y sinh, Giá»›i tÃ­nh, Äá»‹a chá»‰, Äá»‘i tÆ°á»£ng Viá»‡n phÃ­, Cháº©n Ä‘oÃ¡n ban Ä‘áº§u.
  * Bá»• sung rÃµ 2 dÃ²ng trá»‘ng cÃ³ cháº¥m Ä‘iá»n káº¿t quáº£ riÃªng biá»‡t cho tá»«ng bá»‡nh nhÃ¢n:
    * `- Sá»‘ vÃ o viá»‡n bá»‡nh nhÃ¢n báº£o hiá»ƒm: â€¦â€¦â€¦â€¦â€¦â€¦â€¦â€¦â€¦â€¦â€¦â€¦`
    * `- Sá»‘ vÃ o viá»‡n bá»‡nh nhÃ¢n viá»‡n phÃ­: â€¦â€¦â€¦â€¦â€¦â€¦â€¦â€¦â€¦â€¦â€¦â€¦`

### 2. Kiá»ƒm thá»­ & ÄÃ³ng gÃ³i:
* Cáº­p nháº­t unit test `Direct_reception_instruction_includes_full_patient_demographics_and_insurance` kiá»ƒm tra sá»± hiá»‡n diá»‡n cá»§a cáº£ 2 bá»‡nh nhÃ¢n vÃ  2 dÃ²ng Ä‘iá»n SVV.
* ToÃ n bá»™ **37/37 Unit Tests PASSED (100%)**.
* ÄÃ³ng gÃ³i láº¡i thÃ nh cÃ´ng cáº£ 2 báº£n phÃ¡t hÃ nh:
  * `desktop/publish/ExamOperationsDesktop/` (Báº£n tiÃªu chuáº©n)
  * `desktop/publish/ExamOperationsDesktop-Standalone/` (Báº£n Standalone win-x64)


---

## [2026-09-28] Khắc phục Lỗi Tách Batch SQL do Lệnh `GO` và Khớp Biến Regex CRLF

### 1. Phân tích Nguyên nhân Gốc rễ:
* **Lỗi Regex trên tệp định dạng Windows (CRLF):**
  * Trong `SqlScriptGenerator.cs` (và `backend/sql_generator.py`), hàm `SetSqlVariable` dùng regex `(?m)^DECLARE\s+@{Regex.Escape(variableName)}\b[^\r\n]*$`.
  * Trên Windows, dòng kết thúc bằng `\r\n`. Toán tử `[^\r\n]*` dừng lại trước ký tự `\r`, trong khi ký hiệu `$` trong .NET Regex chỉ khớp trước `\n` hoặc cuối chuỗi, không khớp trước `\r`.
  * Do đó, `Regex.IsMatch` trả về `false` đối với **tất cả** các dòng khai báo trong tệp template!
* **Hậu quả tách Batch do lệnh `GO`:**
  * Do regex không match được dòng khai báo cũ để thay thế tại chỗ, `SetSqlVariable` chuyển sang cơ chế fallback: nối chuỗi `DECLARE @var ...` lên **đầu tệp tin** (trước cả khối `USE [eHospital_ThienHanh]; GO`).
  * Khi thực thi trên SQL Server Management Studio:
    1. **Batch 1 (trước `GO`):** Khai báo các biến với thông tin thật của thí sinh và bệnh nhân. Gặp lệnh `GO`, Batch 1 kết thúc -> **toàn bộ các biến trong Batch 1 bị hủy sạch khỏi bộ nhớ**.
    2. **Batch 2 (sau `GO`):** Bắt đầu thực thi và gặp lại toàn bộ các dòng `DECLARE` nguyên bản của template mẫu: `@TenBenhNhan = N'TEST TIEP NHAN VAO KHOA'`, `@LoaiHoSo = 'VIEN_PHI'`, `@NgayGioNghiepVu = GETDATE()`, `@TenDangNhap = N'ANHPH'`.
    3. Hậu quả: Dữ liệu thật không bao giờ được ghi nhận; hệ thống ghi nhận toàn bộ thông tin mẫu.
    4. Validation không phát hiện được vì các giá trị mẫu đều tồn tại trong danh mục CSDL thật (User ANHPH, tỉnh Đắk Lắk, ICD R69...).

### 2. Các Thay đổi & Giải pháp đã Thực hiện:
1. **Sửa biểu thức Regex hỗ trợ triệt để CRLF:**
   * Cập nhật pattern trong `SqlScriptGenerator.cs` và `backend/sql_generator.py` thành:
     `(?m)^DECLARE\s+@{Regex.Escape(variableName)}\b[^\r\n]*\r?$`
   * Đảm bảo thay thế chính xác 100% tại chỗ từng dòng `DECLARE` nằm trong phần `1. THAM SỐ CẦN ĐIỀN` sau lệnh `GO`.
2. **Bảo vệ Vị trí Fallback khi biến chưa tồn tại trong Template:**
   * Trong trường hợp một biến chưa có sẵn trong template, `SetSqlVariable` sẽ chèn biến vào ngay sau tiêu đề `/* 1. THAM SỐ CẦN ĐIỀN */` (sau `USE ... GO`), **tuyệt đối không chèn lên đầu tệp tin** trước lệnh `GO`.
3. **Bổ sung Unit Test Khảo sát & Chặn Tái diễn:**
   * Trong `ExamScenarioAllocatorTests.cs`, thêm kiểm tra khẳng định mã SQL sinh ra **không được phép chứa bất kỳ chuỗi giá trị mẫu nào**:
     * `Assert.DoesNotContain("TEST TIEP NHAN VAO KHOA", sql);`
     * `Assert.DoesNotContain("TEST CHỈ ĐỊNH VÀO KHOA", sql);`

### 3. Kết quả Kiểm thử & Đóng gói:
* Toàn bộ **37/37 Unit Tests PASSED (100%)**.
* Đóng gói lại thành công cả 2 bản Release (Framework-dependent và Standalone) tại:
  * `desktop/publish/ExamOperationsDesktop/`
  * `desktop/publish/ExamOperationsDesktop-Standalone/`
* Lịch sử build tự động cập nhật trong `BUILD_HISTORY.md`.