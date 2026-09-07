# Nhật ký Agent (Agent Log) - Exam Generator & HIS Data Populator

Nhật ký ghi lại các thay đổi, quyết định thiết kế và tiến trình thực thi của Antigravity Coding Assistant trong suốt phiên làm việc.

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

### 2. Các bước tiếp theo:
1.  Tạo các file Excel danh mục mẫu (`patients.xlsx`, `drugs.xlsx`, `services.xlsx`, `users.xlsx`) trong thư mục `data/catalogs/`.
2.  Viết file `backend/requirements.txt` và cài đặt các dependencies.
3.  Lập trình bộ quản lý danh mục `backend/catalog_manager.py`.
