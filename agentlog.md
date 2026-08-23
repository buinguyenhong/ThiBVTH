# Nhật ký Agent (Agent Log) - Exam Generator & HIS Data Populator

Nhật ký ghi lại các thay đổi, quyết định thiết kế và tiến trình thực thi của Antigravity Coding Assistant trong suốt phiên làm việc.

## [2026-08-22] Tái cấu trúc tổng thể sang Kiến trúc Mô-đun Nghiệp vụ & Trình tạo Mẫu đề (Template Builder)

### 1. Quyết định nghiệp vụ & Thiết kế:
*   **Module hóa câu hỏi:** Tách rời 14 loại nghiệp vụ độc lập (`NT_NHAN_BENH_KHOA`, `TN_TIEP_NHAN`, `YL_CHI_DINH_CLS`, `YL_CHI_DINH_THUOC_VTYT`, `TK_KIEM_TON_KHO`...) trong `backend/exam_actions.py`.
*   **Phân định rõ tiếp nhận và nhập khoa:** `NT_NHAN_BENH_KHOA` sinh T-SQL nạp sẵn bệnh nhân vào hàng chờ `NoiTru_NhapVien`. `TN_TIEP_NHAN` in đủ thông tin 1 ca BHYT và 1 ca Viện phí lên đề Word để thí sinh tự nhập tay vào HIS, SQL chỉ validate kiểm tra thẻ chưa trùng.
*   **Dynamic Template Builder & Clone:** Cho phép người dùng tự do định nghĩa mẫu đề thi, tích chọn nghiệp vụ, đặt điểm từng câu (tổng = 10đ), lưu vào `data/config/exam_templates.json` và hỗ trợ nhân bản (clone) đề sang khoa khác.
*   **Phân quyền Dịch vụ & Ánh xạ Kho Dược theo Khoa (Master-Detail & Select2 UI):** Thiết kế lại giao diện Tab Phân quyền theo mô hình Master-Detail (danh sách Khoa bên trái kèm tìm kiếm, bảng cấu hình chi tiết bên phải). Sử dụng thanh tìm kiếm autocomplete đa mục (Select2 style) kèm danh sách tag chips có nút `×` để gỡ bỏ trực quan, không bị tràn màn hình.
*   **Format Dược 7 cột chuẩn:** Trở lại định dạng 7 cột gốc xuất trực tiếp từ HIS (`MaDuoc, TenDuoc, DVTTinh, MaKho, Nguon, SoLuongTon, TrangThai`).
*   **Nâng cấp UI 5 Tab:** Thiết kế lại toàn bộ giao diện Web (Thực thi tạo đề, Quản lý Mẫu đề, Phân quyền Dịch vụ & Kho, Cài đặt danh mục, Tra cứu tồn kho) với modal tạo/sửa/clone đề trực quan.

### 2. Công việc đã thực hiện:
*   Tạo `backend/mapping_manager.py`: Quản lý phân quyền Nhóm Dịch Vụ và Kho Dược theo Khoa.
*   Tạo `backend/exam_actions.py`: Registry 14 nghiệp vụ nguyên tử (data generator, docx renderer, sql flag).
*   Tạo `backend/template_manager.py`: Engine quản lý CRUD & Clone Mẫu đề thi kèm bộ Seed mẫu mặc định cho tất cả các khoa.
*   Cập nhật `backend/catalog_manager.py`: Đọc chuẩn 7 cột cho thuốc, cung cấp `get_service_groups()`, `get_warehouses()`, và bộ lọc kết hợp `MappingManager`.
*   Cập nhật `backend/exam_generator.py` & `backend/sql_generator.py`: Hỗ trợ kết xuất đề DOCX, file Excel và script T-SQL từ các actions trong Template.
*   Cập nhật `backend/app.py`: Bổ sung toàn bộ REST APIs (`/api/actions`, `/api/templates`, `/api/mappings/*`, `/api/metadata/*`), nâng cấp endpoint sinh đề `/api/generate`.
*   Tạo `backend/test_modular_system.py`: Bộ kiểm thử toàn diện cho module mapping, templates, actions và API.
*   Nâng cấp `frontend/index.html`, `frontend/style.css`, `frontend/app.js`: Giao diện 5 Tab Glassmorphism, Template Builder modal, Matrix checkbox table, và Pharmacy chips selector.

*   Tạo `Scripts/07_export_phan_quyen_dich_vu_theo_khoa.sql`: Script trích xuất quan hệ `DM_PhongBan_DichVu` JOIN `DM_DichVu` JOIN `DM_PhongBan`.
*   Tạo `Scripts/08_export_anh_xa_kho_duoc_theo_khoa.sql`: Script trích xuất quan hệ `dm_khoduoc` (`PhongBan_Id`) và `DM_Duoc_TheoNoiSuDung`.
*   Bổ sung API `POST /api/mappings/upload/services`, `POST /api/mappings/upload/pharmacies` và `GET /api/mappings/script/*` để lấy script và nạp trực tiếp kết quả Excel vào cấu hình.
*   Nâng cấp giao diện Tab Cài đặt danh mục (Card 5 & Card 6) và Tab Phân quyền với các nút Lấy Script SQL và Tải lên Excel tiện lợi.

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
