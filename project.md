# Dự án: Exam Generator & HIS Data Populator

Dự án phát triển phần mềm hỗ trợ Hội đồng tuyển dụng Bệnh viện Đa khoa Thiện Hạnh tự động hóa việc tạo đề thi thực hành vi tính và đồng bộ sinh mã T-SQL nạp dữ liệu vào cơ sở dữ liệu SQL Server của hệ thống HIS.

---

## 1. Thông tin Chung
*   **Tên dự án:** Exam Generator & HIS Data Populator (Bộ sinh đề thi và CSDL khảo thí HIS)
*   **Môi trường chạy:** Cục bộ (Localhost) chạy trên nền tảng Windows.
*   **Công nghệ:**
    *   **Backend:** Python 3.14, FastAPI, `python-docx` (sinh file Word), `openpyxl` (xử lý danh mục Excel).
    *   **Frontend:** HTML5, Vanilla CSS3 (Premium Glassmorphism, Responsive Grid, Dark/Light Mode), Vanilla JavaScript.
    *   **Cơ sở dữ liệu thi:** Microsoft SQL Server (T-SQL script đầu ra).

---

## 2. Quy tắc Thực thi và Phát triển (Vibe Coding Rules)
Để đảm bảo dự án được xây dựng với chất lượng tốt nhất, dễ bảo trì và mang lại trải nghiệm người dùng tuyệt vời:
1.  **Phát triển Hướng Thành phần Nghiệp vụ (Modular Action Architecture):**
    *   Mỗi câu hỏi trong đề thi là một đơn vị nghiệp vụ độc lập (`ACTION_REGISTRY` trong `backend/exam_actions.py`), tự quản lý sinh dữ liệu, in đề Word và sinh SQL HIS.
2.  **Quản lý Mẫu đề Động (Template Engine & Clone):**
    *   Người dùng có thể tự định nghĩa mẫu đề thi theo Khoa/Phòng + Vị trí, tùy biến chọn câu hỏi/nghiệp vụ, đặt điểm từng câu (tổng điểm 10) và nhân bản (clone) đề sang các khoa khác thông qua `TemplateManager`.
3.  **Phân quyền Nhóm Dịch vụ & Ánh xạ Kho Dược:**
    *   Ma trận phân quyền Nhóm Dịch vụ Cận lâm sàng theo Khoa và Ánh xạ Mã Kho Dược (`MappingManager`) giúp đề thi luôn bốc đúng dịch vụ và thuốc/VTYT của từng khoa phòng.
4.  **Dữ liệu Thực tế & Dược 7 Cột Chuẩn:**
    *   Dùng danh mục Dược format 7 cột chuẩn (`MaDuoc, TenDuoc, DVTTinh, MaKho, Nguon, SoLuongTon, TrangThai`) đồng bộ thực tế từ HIS.
5.  **Nhật ký Hoạt động (Audit Trail):**
    *   Ghi lại chi tiết mọi thay đổi cấu trúc, logic hoặc giao diện vào file `agentlog.md`.

---

## 3. Thư viện Các Nghiệp vụ Câu hỏi Độc lập (Question Actions)

Hệ thống cung cấp danh mục các nghiệp vụ nguyên tử chuẩn:

1. **`NT_NHAN_BENH_KHOA` (Nhận bệnh nhân vào khoa điều trị):**
   * T-SQL HIS tự động tạo hồ sơ bệnh nhân BHYT và Viện phí vào hàng chờ nhập khoa `NoiTru_NhapVien`.
2. **`TN_TIEP_NHAN` (Tự tiếp nhận mới thông tin bệnh nhân):**
   * Đề in đầy đủ thông tin hành chính, thẻ BHYT, địa chỉ của 1 ca BHYT và 1 ca Viện phí để thí sinh tự nhập tay vào HIS. T-SQL chỉ kiểm tra tính hợp lệ của số thẻ BHYT.
3. **`YL_CHI_DINH_CLS` (Chỉ định dịch vụ Cận lâm sàng):**
   * Tự động lấy dịch vụ theo đúng các Nhóm Dịch Vụ được phân quyền cho Khoa (Siêu âm, X-quang, CT Scan, Xét nghiệm, Nội soi...).
4. **`YL_CHI_DINH_THUOC_VTYT` (Lên y lệnh Thuốc & VTYT):**
   * Lấy thuốc/vật tư từ đúng Mã Kho Dược đã ánh xạ với Khoa phòng, lọc theo nguồn BH/VP và tồn kho khả dụng.
5. **`YL_TRA_THUOC` (Trả thuốc thừa / Hủy y lệnh):**
   * Hướng dẫn trả lại thuốc hoặc hủy y lệnh trên phần mềm HIS.
6. **`YL_DOI_THEM_DICH_VU` (Đổi và bổ sung dịch vụ kỹ thuật):**
   * Thao tác đổi chỉ định dịch vụ và thêm dịch vụ mới.
7. **`TK_KIEM_TON_KHO` (Kiểm tra tồn kho Dược / Tủ trực):**
   * Tra cứu số lượng tồn khả dụng trong kho tủ trực khoa.
8. **`CK_CHUYEN_KHOA` (Chuyển khoa điều trị ca cũ):**
   * Mốc thời gian tự động lùi 30 ngày so với ngày thi ($\text{CutoffDate} = \text{ExamDate} - 30\text{ ngày}$).
9. **`RV_CHO_RA_VIEN` (Cho ra viện ca cũ):**
   * Tìm bệnh nhân điều trị nội trú trong khoảng thời gian -30 ngày và thực hiện thủ tục ra viện.
10. **`TC_THU_TAM_UNG` (Thu tạm ứng viện phí):**
    * Thu tiền tạm ứng nội trú / ngoại trú theo mã đợt khám.
11. **`TC_THANH_TOAN_RA_VIEN` (Thanh toán viện phí ra viện):**
    * Quyết toán và in bảng kê chi phí thanh toán xuất viện.
12. **`KQ_TRA_KET_QUA_CLS` (Nhập trả kết quả Cận lâm sàng):**
    * In sẵn bảng chỉ số xét nghiệm huyết học 18 thông số hoặc mẫu mô tả siêu âm/nội soi trên đề Word.
13. **`VP_SOAN_THAO_WORD` (Soạn thảo văn bản NĐ 30):**
    * Dành cho khối văn phòng / CSKH, thực hành thể thức văn bản hành chính theo Nghị định 30/2020/NĐ-CP.
14. **`VP_XU_LY_EXCEL` (Xử lý bảng tính Excel):**
    * Tự động sinh file Excel giả lập `.xlsx` kèm theo đề thi để thí sinh thực hành hàm (`VLOOKUP`, `IF`, `SUMIF`...) và vẽ biểu đồ.

---

## 4. Bản đồ Trạng thái Tệp tin
*   `project.md`: Tài liệu tổng quan kiến trúc và nghiệp vụ hệ thống (File này).
*   `agentlog.md`: Nhật ký cập nhật sửa đổi của Agent theo thời gian.
*   `backend/app.py`: Backend chính, chứa các API FastAPI quản lý Mẫu đề, Phân quyền, Danh mục và Sinh đề thi.
*   `backend/exam_actions.py`: Registry thư viện các nghiệp vụ câu hỏi độc lập (chuẩn bị dữ liệu, in DOCX).
*   `backend/template_manager.py`: Quản lý lưu trữ JSON, CRUD và Clone các Mẫu đề thi (`data/config/exam_templates.json`).
*   `backend/mapping_manager.py`: Quản lý phân quyền Nhóm Dịch Vụ và Ánh xạ Kho Dược theo Khoa (`data/config/`), hỗ trợ nạp trực tiếp file Excel xuất từ HIS.
*   `backend/catalog_manager.py`: Quản lý đọc danh mục Excel (Dược 7 cột, Bệnh nhân, Dịch vụ, Users), khử trùng lặp bệnh nhân (`used_patients.json`).
*   `backend/exam_generator.py`: Sinh tài liệu Word `.docx` theo các nghiệp vụ trong Template và sinh file Excel `.xlsx`.
*   `backend/sql_generator.py`: Render tài liệu nghiệp vụ `Scripts/02` thành T-SQL theo từng thí sinh, tự tra cứu ID HIS và giữ mặc định rollback an toàn.
*   `Scripts/07_export_phan_quyen_dich_vu_theo_khoa.sql`: Script SQL trích xuất phân quyền Nhóm dịch vụ theo Khoa từ `DM_PhongBan_DichVu` trên HIS.
*   `Scripts/08_export_anh_xa_kho_duoc_theo_khoa.sql`: Script SQL trích xuất ánh xạ Kho Dược / Tủ trực theo Khoa từ `dm_khoduoc` (`PhongBan_Id`) trên HIS.
*   `frontend/index.html`: Giao diện Web 5 Tab hiện đại (Thực thi tạo đề, Quản lý Mẫu đề, Phân quyền Dịch vụ & Kho Master-Detail, Cài đặt danh mục, Tra cứu tồn kho).
*   `frontend/style.css`: CSS Glassmorphism, Responsive Grid, Dark/Light Mode, Select2 styling.
*   `frontend/app.js`: Logic frontend xử lý toàn bộ tương tác người dùng, Template Builder, Phân quyền Dịch vụ/Kho, Copy Script SQL và tải lên Excel.
