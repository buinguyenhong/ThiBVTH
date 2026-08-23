# Kế hoạch triển khai - Phần mềm Tạo Đề thi và Tự động sinh dữ liệu tiếp nhận (HIS SQL Server)

Dự án này nhằm xây dựng một ứng dụng Web cục bộ (Local Web App) giúp Hội đồng tuyển dụng Bệnh viện Đa khoa Thiện Hạnh:
1. Tự động tạo đề thi thực hành vi tính cho các vị trí (Lễ tân, Điều dưỡng, Bác sĩ, Kỹ thuật viên CLS, Dược sĩ...) theo đúng mẫu đề thi truyền thống.
2. Tự động sinh mã SQL Server để chèn dữ liệu bệnh nhân, chỉ định dịch vụ, y lệnh thuốc/vật tư ngẫu nhiên vào hệ thống HIS tương ứng với thông tin trong đề thi của từng ứng viên.
3. Đồng bộ hóa toàn bộ thuốc, vật tư, dịch vụ CLS, tài khoản người dùng thông qua các tệp danh mục Excel (sử dụng các tệp mẫu do phần mềm cung cấp).

---

## Các Tính năng Chính

1. **Quản lý Danh mục (Catalogs):**
   * Cho phép tải xuống các file Excel mẫu cho 4 danh mục: Bệnh nhân, Thuốc & Vật tư, Dịch vụ kỹ thuật, Tài khoản người dùng.
   * Đọc và xác thực tính hợp lệ của dữ liệu danh mục khi người dùng cập nhật/tải lên.
2. **Cấu hình Đề thi theo Vị trí (Exam Configurations):**
   * Quản lý các mẫu đề thi (Templates) tương ứng với từng phòng ban và vị trí công tác (Lễ tân, Nội, Ngoại, Cấp cứu, Siêu âm, Xét nghiệm, Dược...).
   * Các mẫu đề thi định nghĩa sẵn cấu trúc các câu hỏi và các biến cần ngẫu nhiên hóa (ví dụ: Tên bệnh nhân, mã CLS, tên thuốc y lệnh, thuốc kiểm tồn kho).
3. **Sinh đề thi & SQL Script (Batch Generation):**
   * Nhập danh sách thí sinh, ngày thi và chọn vị trí thi.
   * Sinh ngẫu nhiên dữ liệu cho từng đề thi dựa trên danh mục (chọn bệnh nhân chưa tiếp nhận, chọn thuốc còn tồn kho, chọn dịch vụ đang hoạt động).
   * Tạo tệp đề thi Word (`.docx`) chuyên nghiệp theo đúng form chuẩn của bệnh viện (có khung chữ ký, điểm số).
   * Tạo tệp mã SQL Server (`.sql`) để chạy trực tiếp trên CSDL HIS để tiếp nhận bệnh nhân và nạp sẵn trạng thái y lệnh/chỉ định CLS tương ứng cho phòng máy thi.
4. **Giao diện người dùng hiện đại:**
   * Thiết kế giao diện Glassmorphism cao cấp, trực quan, hỗ trợ chuyển đổi giao diện Sáng/Tối (Light/Dark mode).
   * Quản lý tiến trình tạo đề, tải về file ZIP chứa toàn bộ đề thi và mã SQL.

---

## Kiến trúc Hệ thống đề xuất

Ứng dụng sẽ được xây dựng dưới dạng **Local Web Application** bằng Python:
* **Backend:** FastAPI (siêu nhanh, nhẹ, hỗ trợ sinh tài liệu Swagger trực tiếp).
  * `python-docx` để đọc mẫu đề thi và tạo file đề thi Word.
  * `openpyxl` để đọc/ghi các file Excel danh mục.
* **Frontend:** Single Page Application (SPA) viết bằng HTML5, CSS3 (Vanilla CSS với giao diện hiện đại, mượt mà, chuyển động micro-animations) và Vanilla JS, được phục vụ trực tiếp bởi FastAPI.
* **Database Script:** Tự động tạo mã T-SQL (SQL Server) với cấu trúc mô-đun được tách biệt trong `sql_generator.py` để người dùng dễ dàng cấu hình lại tên bảng/tên cột phù hợp với hệ thống HIS thực tế.

---

## Đề xuất Cấu trúc Thư mục Dự án

Toàn bộ mã nguồn dự án sẽ được đặt tại thư mục: `C:\Users\Admin\.gemini\antigravity\scratch\ExamGenerator\`

```
ExamGenerator/
├── backend/
│   ├── app.py                # Điểm khởi chạy FastAPI, API routes
│   ├── catalog_manager.py    # Xử lý đọc/ghi/kiểm tra file danh mục Excel
│   ├── exam_generator.py     # Logic sinh dữ liệu ngẫu nhiên và tạo file Word (.docx)
│   ├── sql_generator.py      # Logic tạo script T-SQL chèn dữ liệu vào SQL Server
│   ├── exam_templates.py     # Cấu trúc câu hỏi và luật sinh đề cho các vị trí
│   └── requirements.txt      # Thư viện Python cần thiết
├── frontend/
│   ├── index.html            # Giao diện chính của WebApp
│   ├── app.js                # Xử lý tương tác, gọi API và tải file
│   └── style.css             # Vanilla CSS giao diện premium (Glassmorphism, Darkmode)
├── data/
│   ├── catalogs/             # Thư mục lưu các file danh mục Excel người dùng tải lên
│   │   ├── patients.xlsx     # Danh mục bệnh nhân mẫu
│   │   ├── drugs.xlsx        # Danh mục thuốc & vật tư mẫu
│   │   ├── services.xlsx     # Danh mục dịch vụ CLS mẫu
│   │   └── users.xlsx        # Danh mục tài khoản người dùng mẫu
│   └── output/               # Nơi lưu trữ đề thi và SQL tạo ra trước khi ZIP
└── README.md                 # Hướng dẫn khởi chạy và sử dụng
```

---

## Thiết kế Mẫu Danh mục (Templates)

Chúng ta sẽ tạo trước 4 file Excel mẫu trong thư mục `data/catalogs/` để người dùng có thể tải về và đổ dữ liệu từ database HIS của họ vào:

### 1. Bệnh nhân (`patients.xlsx`)
* Các cột: `MaYTe` (Mã y tế), `TenBenhNhan` (Họ tên), `NgaySinh` (Ngày sinh), `GioiTinh` (Giới tính: Nam/Nữ), `DiaChiLienHe` (Địa chỉ), `SoBHYT` (Số thẻ BHYT), `BHYTTuNgay` (Hạn từ), `BHYTDenNgay` (Hạn đến), `DKKCB` (Mã nơi ĐKKCB ban đầu).

### 2. Thuốc & Vật tư (`drugs.xlsx`)
* Các cột: `MaDuoc` (Mã thuốc/vật tư), `TenDuoc` (Tên, nồng độ, hàm lượng, biệt dược), `DVTTinh` (Đơn vị tính), `MaKho`, `TenKho`, `KhoaPhong`, `Nguon` (BHYT / Viện phí), `SoLuongTon` (số dư hiện hành lúc chạy script), `TrangThai` và `ThoiDiemChotTon`.
* Ứng dụng vẫn đọc được file 7 cột cũ, nhưng cần xuất lại bằng script mới để tra cứu chính xác theo khoa/phòng và hiển thị thời điểm chốt tồn.

### 3. Dịch vụ kỹ thuật (`services.xlsx`)
* Các cột: `MaDichVu` (Mã dịch vụ CLS/thủ thuật/phẫu thuật), `TenDichVu` (Tên dịch vụ kỹ thuật), `NhomDichVu` (Nhóm: Siêu âm, X-quang, Xét nghiệm, Nội soi...), `KhoaThucHien` (Khoa phụ trách), `TrangThai` (Trạng thái sử dụng: 1 = Đang dùng, 0 = Ngưng).

### 4. Tài khoản người dùng (`users.xlsx`)
* Các cột: `TenDangNhap` (Tên đăng nhập), `MatKhau` (Mật khẩu), `HoTen` (Họ tên nhân viên), `KhoaPhong` (Khoa phòng làm việc, ví dụ: Lễ tân, Nội, Sản, Dược...).
* Chỉ các vị trí sử dụng HIS mới bắt buộc có user thuộc đúng `KhoaPhong`.
* Hai vị trí `OFFICE_ADMIN` (Chăm sóc khách hàng và Hành chính văn phòng) không đọc hoặc phụ thuộc file này.

---

## Kế hoạch Xác minh & Kiểm thử (Verification Plan)

### Trạng thái tích hợp HIS thực tế (2026-07-25)

* Bộ sinh SQL không còn dùng các bảng mô phỏng `HS_TIEPNHAN`, `CLS_YLENH` hoặc `DM_DUOC_KHO`.
* Đề nội trú dùng stored procedure thực tế `sp_DM_BENHNHAN`, `sp_DM_BENHNHAN_BHYT`, `sp_LST_KEYDATA`, `sp_TIEPNHAN` và `sp_NOITRU_NHAPVIEN`.
* Khối nạp dữ liệu tự tra cứu user, khoa, bác sĩ, dân tộc, nghề nghiệp, tỉnh, lý do nhập viện, ICD, tuyến và nơi ĐKKCB.
* Chỉ action `sp_NOITRU_NHAPVIEN/AddNew` được phép dùng; không gọi `AddNewAndCreateBenhAn`.
* Phạm vi hiện tại của tài liệu nghiệp vụ `02` là tạo hàng chờ nhận khoa cho đề nội trú. Các quy trình CLS, dược, viện phí và kết quả kỹ thuật viên cần tài liệu nghiệp vụ riêng trước khi sinh lệnh ghi dữ liệu.

### Luồng Tin học văn phòng độc lập (2026-07-27)

* Hai đề Chăm sóc khách hàng và Hành chính văn phòng chỉ kiểm tra kỹ năng Microsoft Word/Excel.
* Không tra user, không chọn bệnh nhân, không tạo SQL và không kèm tài liệu tra cứu HIS.
* Mỗi thí sinh nhận một DOCX đề thi và một XLSX dữ liệu giả lập 20 dòng với hai sheet `DuLieu`, `DanhMucKhoa`.
* Lô chỉ có vị trí văn phòng dùng tên ZIP `De_Thi_Tin_Hoc_Van_Phong_<thời gian>.zip`.

### Kiểm thử Tự động (Automated Tests)
* Chạy code kiểm thử đơn vị (`pytest`) cho:
  * Bộ máy sinh đề ngẫu nhiên: Kiểm tra xem các thuốc/vật tư được chọn có đảm bảo còn tồn kho và đang hoạt động hay không.
  * Bộ đọc Excel: Kiểm tra việc đọc file có bỏ qua các dòng lỗi hay không.
  * Bộ xuất Word: Đảm bảo file `.docx` được xuất ra đúng định dạng và không bị lỗi cấu trúc.

### Kiểm thử Thủ công (Manual Verification)
1. Khởi chạy ứng dụng WebApp cục bộ, truy cập qua trình duyệt.
2. Tải về các file danh mục mẫu, điền dữ liệu chạy thử và tải lên lại hệ thống.
3. Thử tạo đề thi cho 3 ứng viên thi các vị trí khác nhau (ví dụ: 1 Lễ tân, 1 Nội khoa, 1 Siêu âm).
4. Kiểm tra các file Word đề thi sinh ra: Đảm bảo thông tin bệnh nhân, thuốc, dịch vụ được thay đổi ngẫu nhiên chính xác.
5. Kiểm tra file SQL sinh ra: Đảm bảo các câu lệnh `INSERT` có đầy đủ thông tin tương ứng với đề thi của từng ứng viên và cú pháp SQL Server (`T-SQL`) chuẩn xác.
