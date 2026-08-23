# Hướng dẫn Khởi chạy & Sử dụng - Exam Generator & HIS Data Populator

Hệ thống hỗ trợ Hội đồng tuyển dụng Bệnh viện Đa khoa Thiện Hạnh tự động hóa việc tạo đề thi và sinh script nạp dữ liệu SQL Server.

---

## 1. Chuẩn bị Môi trường

Hệ thống yêu cầu cài đặt Python 3.8+ (đã được cài đặt sẵn Python 3.14.0 trên máy tính khảo thí).

Cài đặt các thư viện cần thiết bằng cách mở PowerShell/CMD tại thư mục dự án và chạy:
```powershell
pip install -r backend/requirements.txt
```

---

## 2. Khởi chạy Ứng dụng

Chạy lệnh sau trong thư mục `backend/` để khởi động máy chủ Web cục bộ:
```powershell
python backend/app.py
```
Máy chủ sẽ chạy tại địa chỉ: **[http://127.0.0.1:8000](http://127.0.0.1:8000)**

Mở trình duyệt Web (Chrome, Edge...) và truy cập địa chỉ trên để sử dụng giao diện phần mềm.

---

## 3. Quy trình Sử dụng

### Bước 1: Chuẩn bị Danh mục Dữ liệu
Các file danh mục Excel được lưu trữ tại thư mục: `data/catalogs/`
*   `patients.xlsx`: Danh sách thông tin bệnh nhân dùng để sinh đề và tiếp nhận.
*   `drugs.xlsx`: Danh mục thuốc, vật tư y tế, kho, khoa/phòng quản lý, nguồn
    (BH/VP), số lượng tồn kho và thời điểm chốt dữ liệu.
*   `services.xlsx`: Danh mục dịch vụ kỹ thuật cận lâm sàng (Siêu âm, X-quang, Xét nghiệm...).
*   `users.xlsx`: Danh mục tài khoản người dùng tương ứng với từng phòng ban.

*Lưu ý:* Nếu bạn cập nhật dữ liệu mới vào các file Excel này, hãy nhấn nút **"Làm mới Danh mục"** trên giao diện Web để phần mềm tải lại dữ liệu mới vào bộ nhớ.

### Bước 2: Nhập danh sách thí sinh và cấu hình ngày thi
1.  Chọn ngày thi thực tế trên giao diện.
2.  Nhập **Họ tên thí sinh**, **Số báo danh (SBD)** và chọn **Vị trí thi** (Nội khoa, Cấp cứu, Lễ tân Thu phí, Dược sĩ, Siêu âm, Xét nghiệm...).
3.  Nhấn **"Thêm Thí sinh"** để đưa vào danh sách chờ.

### Bước 3: Sinh Đề thi và Script SQL
1.  Mỗi thí sinh mới được tích chọn mặc định. Có thể tích/bỏ từng dòng hoặc
    dùng **Chọn tất cả/Bỏ chọn**.
2.  Chọn một trong ba cách sinh:
    * **Sinh cả đợt thi (.zip)**: tạo đề và script cho toàn bộ danh sách,
      không phụ thuộc trạng thái checkbox.
    * **Sinh phần đã chọn (.zip)**: chỉ tạo cho các thí sinh đang được tích.
    * Nút tải màu tím trên từng dòng: tạo riêng cho đúng một thí sinh.
3.  Trình duyệt sẽ tải xuống tệp tin ZIP chứa:
    *   Các file đề thi Word (`.docx`) theo đúng họ tên, SBD của từng thí sinh, với các thông tin bệnh nhân, y lệnh và câu hỏi tồn kho được trộn ngẫu nhiên.
    *   Tệp T-SQL tổng hợp dùng stored procedure thực tế của `eHospital_ThienHanh`.
    *   `01_lookup_tiep_nhan_nhap_vien.sql` để tra cứu các ID danh mục khi khối tự động không phân giải được cấu hình HIS.

Đề Word và script bệnh nhân trong mỗi ZIP luôn được sinh từ cùng một lần chọn
dữ liệu, tránh trường hợp thông tin bệnh nhân giữa đề và script không khớp.

---

## 4. Lấy dữ liệu danh mục từ HIS

Trong tab **Cài đặt & Danh mục**, cả bốn danh mục có nút **Lấy script**. Nút
này mở popup hiển thị SQL và có nút **Sao chép script**, không tải file SQL
về máy:

* `patients.xlsx`: hiện script chỉ đọc để lấy tối đa 1.000 bệnh nhân, mặc định
  gồm 500 hồ sơ BHYT còn hạn và 500 hồ sơ viện phí.
* `drugs.xlsx`: hiện script chỉ đọc để lấy thuốc/vật tư đang hoạt động, thuộc
  kho đang hoạt động và có số lượng tồn lớn hơn 0. `SoLuongTon` là tổng
  `DuocTonKho.SoLuong` theo thuốc + kho + nguồn tại lúc chạy script; kết quả
  còn có `TenKho`, `KhoaPhong` và `ThoiDiemChotTon`.
* `services.xlsx`: tải dịch vụ đang hoạt động và đã được ánh xạ đến đúng
  khoa/phòng thực hiện đang hoạt động.
* `users.xlsx`: tải tài khoản đang hoạt động theo ánh xạ user → nhân viên →
  khoa/phòng. Script dùng biến `@MatKhauThi` thay vì xuất mật khẩu HIS.

Chạy script trong SQL Server Management Studio, xuất lưới kết quả sang Excel
đúng tên file tương ứng, sau đó dùng nút **Tải lên**. Các cột của kết quả đã
được sắp theo đúng thứ tự mà ứng dụng yêu cầu.

Khi sinh đề HIS, ứng dụng chỉ chọn user có khoa/phòng khớp với khoa của vị trí
thi. Giao diện chỉ hiển thị những vị trí HIS có ít nhất một user phù hợp trong
`users.xlsx`. Nếu gọi API trực tiếp với vị trí HIS chưa có user, hệ thống dừng
và yêu cầu cập nhật danh mục; hệ thống không chọn ngẫu nhiên user của khoa
khác. Hai đề Chăm sóc khách hàng và Hành chính văn phòng là đề Word/Excel độc
lập, không cần user; hệ thống tạo kèm file Excel dữ liệu thực hành nhưng không
tạo SQL và không kèm script HIS.

Khi tải `patients.xlsx` lên, ứng dụng dùng năm của **Ngày thi** đang chọn để
chuẩn hóa hạn thẻ BHYT:

* Thẻ thông thường: từ `01/01/<năm thi>` đến `31/12/<năm thi>`.
* Thẻ trẻ em có mã bắt đầu bằng `TE1`: từ `01/01/<năm thi>` đến
  `31/12/<năm thi + 4>`, tương ứng 5 năm liên tục.

Quy tắc này được áp dụng lại lúc sinh đề, vì vậy dữ liệu cũ hoặc file được
chép trực tiếp vào `data/catalogs` vẫn nhận đúng hạn theo năm thi.

Khi một gói đề được tạo thành công, mã y tế của mọi bệnh nhân đã xuất hiện
trong đề được lưu tại `data/used_patients.json`. Những bệnh nhân này sẽ không
được chọn lại trong các lần sinh đề sau. Nếu quá trình sinh đề lỗi, danh sách
đã dùng không bị cập nhật.

## 5. Tra cứu tồn kho Dược và Vật tư theo khoa

Tab **Tra cứu tồn kho** lọc trực tiếp dữ liệu từ `drugs.xlsx` đã upload theo:

* Khoa/phòng quản lý kho.
* Kho dược.
* Nguồn BH/VP và số lượng tồn tối thiểu.
* Mã hoặc tên thuốc/vật tư.

Đây là ảnh chụp dữ liệu, không phải kết nối tồn kho trực tiếp với HIS. File
không tự thay đổi sau khi upload. Giao diện ưu tiên hiển thị
`ThoiDiemChotTon` do script ghi vào file; với file 7 cột cũ, giao diện chỉ có
thể hiển thị thời điểm cập nhật file và lọc theo mã kho.

Để có bộ lọc khoa/phòng chính xác:

1. Vào **Cài đặt & Danh mục**, tại `drugs.xlsx` chọn **Lấy script**.
2. Chạy script trong SSMS và xuất đủ 10 cột theo đúng thứ tự.
3. Upload lại `drugs.xlsx`.
4. Mở tab **Tra cứu tồn kho** và chọn khoa/phòng cần xem.

Script dùng mức cô lập `READ COMMITTED`, chốt `SYSDATETIME()` khi bắt đầu
truy vấn và chỉ xuất các dòng có tổng tồn lớn hơn 0. Vì vậy số liệu phản ánh
trạng thái đã commit quanh thời điểm chạy script, không phải tồn lịch sử theo
ngày thi.

## 6. Tùy chỉnh Cấu trúc Bảng CSDL (Dành cho Quản trị viên CNTT)

Nếu hệ thống HIS thay đổi cấu trúc bảng, stored procedure hoặc tên trường, quản trị viên có thể cập nhật `backend/sql_generator.py` và tài liệu nguồn trong `Scripts/`.

---

## 7. Tài liệu T-SQL Nghiệp vụ HIS

Thư mục `Scripts/` lưu các kịch bản T-SQL nghiệp vụ dùng với CSDL HIS thực tế:

* `01_lookup_tiep_nhan_nhap_vien.sql`: Tra cứu chỉ đọc đối với user, khoa, bác sĩ, từ điển nghiệp vụ, tỉnh, bệnh viện ĐKKCB, ICD và đối tượng.
* `02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql`: Tạo bệnh nhân mới theo hồ sơ viện phí hoặc BHYT, tiếp nhận trực tiếp và tạo dòng chờ vào khoa nhưng không tạo bệnh án hay lưu trú.

Ứng dụng dùng trực tiếp tài liệu `02` làm nguồn sinh SQL:

* Với `NURSE_INPATIENT` và `RECEPTIONIST_INPATIENT`, mỗi thí sinh nhận hai khối dữ liệu BHYT/viện phí. Script tự tìm ID và tạo `DM_BenhNhan`, `TiepNhan`, `NoiTru_NhapVien` bằng action `AddNew`.
* Với đề yêu cầu thí sinh tự tiếp nhận, script chỉ chạy kiểm tra thẻ BHYT chưa tồn tại; không tạo trước hồ sơ và không làm hộ bài thi.
* Những loại đề chưa được tài liệu `02` bao phủ chỉ nhận ghi chú rõ ràng; hệ thống không còn sinh lệnh vào các bảng giả.

Số BHYT dùng trong đề được biến đổi thành mã khảo thí duy nhất nhưng giữ nguyên 5 ký tự đầu để HIS suy ra nhóm quyền lợi/tỉnh. Điều này hạn chế trùng thẻ với CSDL sao chép từ hệ thống thật.

Mọi khối tạo dữ liệu mặc định đặt `@Commit = 0` và `ROLLBACK`. Chạy thử toàn bộ, xác nhận các ID được phân giải đúng và `BenhAn_Id` luôn là `NULL`; chỉ sau đó mới đổi `@Commit = 1`.

> Cấu trúc stored procedure và bảng đã được đối chiếu với snapshot tại `D:\CÔNG VIỆC\CAUTRUCHIS`. Thư mục này không bắt buộc khi chạy ứng dụng.
