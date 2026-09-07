# Dự án: Exam Operations Desktop

Ứng dụng Windows cục bộ hỗ trợ Hội đồng tuyển dụng Bệnh viện Đa khoa Thiện Hạnh chuẩn bị, sinh và quản lý đề thi thực hành trên HIS. Dự án mới kế thừa quy tắc nghiệp vụ, kho đề và hiểu biết HIS của hệ thống cũ, nhưng không bị ràng buộc bởi mã nguồn hay nền tảng web hiện tại.

---

## 1. Mục tiêu và phạm vi

* **Mục tiêu:** Sinh đề Word chuẩn, rõ ràng và nhất quán cho từng thí sinh; chuẩn bị dữ liệu khảo thí đúng phạm vi; quản lý danh mục, mapping, lịch sử và khả năng tái sinh đề.
* **Môi trường:** Windows cục bộ, phục vụ tạo đề và thi trong môi trường nội bộ/không có Internet.
* **Nền tảng mục tiêu:** C# / .NET 8 LTS / WPF theo MVVM.
* **Dữ liệu vận hành:** SQLite cục bộ, file database đặt trong thư mục cài đặt của phần mềm. Excel chỉ dùng để import dự phòng, import danh sách thí sinh và export đối chiếu.
* **Kết nối SQL Server:** Có màn hình Settings để người dùng nhập/lưu một hoặc nhiều connection profile khi triển khai thực tế; ứng dụng chỉ dùng profile để đọc catalog HIS.
* **Đầu ra bắt buộc:** DOCX theo template Word; ZIP theo đợt thi; manifest nội bộ để truy vết. Không yêu cầu PDF, ký số hoặc mã hóa.
* **HIS:** Ứng dụng chỉ kết nối đọc dữ liệu danh mục bằng `SELECT`. Ứng dụng không tự thực thi câu lệnh làm thay đổi dữ liệu HIS.

## 2. Nguồn nghiệp vụ tham chiếu

* **Kho đề thực tế:** `D:\CÔNG VIỆC\Thi\Thi_2021` chứa các đề thực hành từ nhiều năm. Đây là nguồn chuẩn để chuẩn hóa họ đề, câu hỏi, phân điểm và bố cục Word.
* **Cấu trúc HIS:** `D:\CÔNG VIỆC\CAUTRUCHIS\schema_output_markdown` là nguồn tham chiếu schema, bảng và stored procedure HIS.
* **Hệ thống cũ:** Nằm tại root repository: `backend/`, `frontend/`, `Scripts/` và `data/`. Hệ thống này chỉ được giữ để tham khảo quy tắc đã có, script SQL và dữ liệu mẫu; không phải ràng buộc thiết kế cho ứng dụng mới.
* **Ứng dụng mới:** Nằm hoàn toàn trong `desktop/`. Solution mới là `desktop/ExamGenerator.Desktop.sln`; không chia sẻ mã thực thi với hệ thống cũ.

## 3. Quy tắc nghiệp vụ đã chốt

### 3.1. Bệnh nhân và BHYT

1. Không dùng lại bệnh nhân giữa các đề và các đợt thi, trừ khi người quản trị cho phép rõ ràng.
2. Hệ thống có thể tạo **bệnh nhân khảo thí biến thể** từ dữ liệu nguồn để tránh trùng: thay đổi có kiểm soát tên, tuổi/ngày sinh, số thẻ BHYT và dữ liệu hành chính; vẫn phải giữ giới tính, nhóm tuổi và ngữ cảnh phù hợp đề.
3. Không dùng số điện thoại, định danh cá nhân hoặc dữ liệu nhạy cảm thật không cần thiết trong đề khảo thí.
4. Hạn BHYT chuẩn hóa theo năm thi:
   * Thẻ thông thường: `01/01/<năm thi>` đến `31/12/<năm thi>`.
   * Thẻ có mã bắt đầu bằng `TE1`: `01/01/<năm thi>` đến `31/12/<năm thi + 4>`.
   * Ví dụ thi năm 2026, thẻ `TE1`: `01/01/2026` đến `31/12/2030`.
5. Bệnh nhân Nhi chỉ cần là trẻ em theo khoảng tuổi cấu hình. Không xây dựng nghiệp vụ mẹ-con, tạo thẻ trẻ hay luồng Nhi chuyên sâu nếu đề không yêu cầu.

### 3.2. User HIS và đợt thi

1. Mỗi thí sinh sử dụng đúng một user HIS trong một đợt thi.
2. Không cấp trùng user HIS cho hai thí sinh trong cùng đợt.
3. User phải thuộc đúng khoa/phòng theo cấu hình đề; thiếu user đúng khoa là lỗi chặn sinh đề, không fallback sang khoa khác.
4. Server thi dùng user HIS thật và mật khẩu mặc định `123`. Không lấy hoặc lưu mật khẩu thật từ danh mục HIS; mật khẩu là cấu hình môi trường server thi, không phải dữ liệu catalog.

### 3.3. Khoa, kho và dịch vụ

1. Đồng bộ đầy đủ danh mục khoa/phòng và kho từ HIS để người quản trị tự cấu hình.
2. Mỗi khoa/phòng được sử dụng cho khảo thí chỉ map đúng **một kho thi**.
3. Dịch vụ trong đề chỉ được chọn từ dịch vụ đang hoạt động, thuộc nhóm dịch vụ được phép của khoa/phòng đó.
4. Thuốc/VTYT trong đề chỉ được chọn từ kho thi đã map, có tồn phù hợp và đúng nguồn cần dùng.
5. Thiếu mapping khoa-kho, khoa-dịch vụ, dữ liệu tồn hoặc user HIS là lỗi preflight; không chọn dữ liệu từ khoa/kho khác để tiếp tục sinh đề.

### 3.4. Tiếp nhận trực tiếp và SQL chuẩn bị dữ liệu

1. Nếu đề kiểm tra **tiếp nhận trực tiếp**, thí sinh tự tạo bệnh nhân/lượt tiếp nhận trên server thi. Hệ thống chỉ in đầy đủ dữ liệu hành chính trong DOCX và **không sinh SQL** chuẩn bị ca.
2. Tiếp nhận trực tiếp là yêu cầu bắt buộc cho các đề Sản, Nhi, Cấp cứu và Khám bệnh, áp dụng cho cả điều dưỡng và lễ tân khi dùng template tương ứng.
3. Nếu đề bắt đầu từ **nhận bệnh vào khoa** và không đánh giá tiếp nhận trực tiếp, hệ thống được sinh file SQL để người vận hành chạy thủ công trên server thi, chuẩn bị ca cho bước nhận vào khoa.
4. Ứng dụng desktop không tự chạy file SQL. File SQL chỉ là đầu ra được kiểm tra và thực thi thủ công trên server thi.
5. SQL chuẩn bị dữ liệu chỉ được dùng cho server thi; đầu file phải ghi rõ mã đợt, thí sinh, mục đích và cảnh báo không chạy trên HIS nguồn.
6. Cờ nghiệp vụ của template:
   * `requires_direct_reception`: đề bắt buộc thí sinh tự tiếp nhận.
   * `requires_exam_setup_sql`: đề cần SQL chuẩn bị ca nhận vào khoa.
   * Hai cờ loại trừ nhau.

## 4. Họ đề chuẩn

Mỗi họ đề có Word template riêng; không dùng một template tổng quát với nhiều cờ điều kiện.

| Họ đề | Nhóm khoa/vai trò tiêu biểu | Đặc trưng |
|---|---|---|
| `inpatient-workflow-8q` | Nội, Ngoại, Sản, Cấp cứu, GMHS, CTCH, Nhi | Chuỗi nghiệp vụ nội trú 8 câu, thường 10 điểm |
| `frontdesk-outpatient` | Lễ tân thu phí/phòng khám | Tiếp nhận, thu phí, tạm ứng, thanh toán, tra cứu |
| `frontdesk-inpatient` | Lễ tân nội trú/Sản | Tiếp nhận trực tiếp, nhập viện/giường, CLS, tra cứu |
| `frontdesk-pediatric` | Lễ tân Nhi | Tiếp nhận trực tiếp bệnh nhân trẻ; không mặc định có nghiệp vụ mẹ-con |
| `imaging-result-and-supply` | Siêu âm, Nội soi, X-quang | Trả kết quả và nghiệp vụ VTYT/tồn kho |
| `laboratory-results` | Xét nghiệm | Trả kết quả theo nhóm xét nghiệm |
| `pharmacy-inventory` | Dược, dụng cụ GMHS | Lĩnh, nhập nội bộ, xuất, trả và tồn kho |
| `administrative-search-office` | Khám bệnh, KHTH, hành chính | Tiếp nhận/tra cứu hoặc Word/Excel tùy vai trò |
| `specialty-procedure` | PHCN, RHM và mẫu chuyên biệt | Nghiệp vụ cấu hình theo chuyên khoa |

Khung `inpatient-workflow-8q` chuẩn gồm: nhận bệnh vào khoa hoặc tiếp nhận trực tiếp, chỉ định CLS, y lệnh thuốc/VTYT, trả thuốc, đổi/thêm dịch vụ, tra cứu tồn, chuyển khoa và cho ra viện. Khoa Nhi dùng cùng khung này, với bệnh nhân trẻ em và nhóm dịch vụ đã map cho Khoa Nhi.

## 5. Kiến trúc mục tiêu

```text
desktop/
|- ExamGenerator.Desktop.sln
|- src/
|  |- ExamGenerator.Desktop        # WPF Views, ViewModels, dialogs
|  |- ExamGenerator.Application    # Use cases: sync, preflight, generate, history
|  |- ExamGenerator.Domain         # Entities, rules, templates, scenario allocation
|  `- ExamGenerator.Infrastructure # SQLite, SQL Server, Excel, DOCX, file storage
|- tests/ExamGenerator.Tests       # Unit and integration tests
`- sql
   |- catalog-exports              # Query SELECT versioned
   `- exam-data-setup              # SQL template chuẩn bị ca nhận vào khoa
```

* `desktop/` không chia sẻ mã thực thi với hệ thống web cũ tại root repository.
* `Domain` không phụ thuộc WPF, SQL Server, Excel hoặc Word.
* `Application` điều phối luồng nghiệp vụ và kiểm tra điều kiện.
* `Infrastructure` triển khai lưu trữ, đồng bộ HIS, import/export và sinh tệp.
* `Desktop` chỉ hiển thị giao diện và gọi use case.
* File SQLite mặc định là `exam-generator.sqlite` nằm cạnh file thực thi/phần mềm; tên file có thể cấu hình nhưng luôn phải là đường dẫn tương đối.
* Settings local lưu connection profile SQL Server và các tùy chọn ứng dụng trong `appsettings.local.json` cạnh phần mềm. Mật khẩu SQL Authentication không được lưu dạng rõ trong settings; khi cần triển khai phải dùng Windows Credential Manager/DPAPI.

## 6. Danh mục HIS và đồng bộ

Nguồn dữ liệu HIS là read-only. Mỗi lần đồng bộ được lưu thành snapshot SQLite gồm thời điểm, profile nguồn, phiên bản query, số dòng hợp lệ/bị loại và lỗi validation.

| Danh mục | Bảng nguồn tham chiếu |
|---|---|
| Khoa/phòng | `eHospital_ThienHanh_Dictionary.dbo.DM_PhongBan` |
| Kho dược | `DM_KhoDuoc` |
| Gợi ý mapping khoa-kho | `DM_PhongBan_KhoDuoc` |
| Thuốc/VTYT | `DM_Duoc` |
| Tồn kho hiện hành | `eHospital_ThienHanh.dbo.DuocTonKho` |
| Nguồn dược | `DM_NguonDuoc` hoặc `DM_NguonHang`, cần kiểm chứng dữ liệu thực tế |
| Dịch vụ và nhóm dịch vụ | `DM_DichVu`, `DM_NhomDichVu` |
| Dịch vụ theo khoa | `DM_PhongBan_DichVu` |
| Nhân viên/user | `NhanVien`, `NhanVien_User_Mapping`, `eHospital_ThienHanh_System.dbo.Sys_Users` |
| Bệnh nhân nguồn/BHYT | `DM_BenhNhan`, `DM_BenhNhan_BHYT` |

Script danh mục phải được version hóa, chỉ dùng `SELECT`, có schema đầu ra được kiểm tra trước khi ghi snapshot. Không dùng `CROSS JOIN` để suy diễn mọi khoa được dùng mọi nhóm dịch vụ; mapping dịch vụ phải đi qua `DM_PhongBan_DichVu`.

## 7. Luồng tạo đề chuẩn

1. Đồng bộ hoặc import danh mục, sau đó kiểm tra chất lượng dữ liệu.
2. Cấu hình khoa, một kho thi duy nhất, nhóm dịch vụ và user HIS khả dụng.
3. Tạo đợt thi, import/nhập thí sinh và gán khoa, vai trò, template.
4. Chạy preflight toàn đợt: user không trùng, mapping đầy đủ, catalog đủ mới, dữ liệu đủ và template hợp lệ.
5. Phân bổ dữ liệu và tạo `ExamScenario` bất biến cho từng thí sinh.
6. Nếu template cần, sinh SQL chuẩn bị ca nhận vào khoa từ chính scenario; không thực thi SQL.
7. Sinh DOCX từ Word template, sinh các file hỗ trợ nếu template yêu cầu, đóng gói ZIP và manifest.
8. Lưu lịch sử đợt thi, snapshot danh mục, dữ liệu đã cấp và file đầu ra để có thể tái sinh không bốc ngẫu nhiên lại.

`ExamScenario` là nguồn dữ liệu duy nhất cho DOCX, SQL và manifest của một thí sinh. Không được bốc dữ liệu riêng trong từng bước render.

## 8. Word template và chất lượng đầu ra

* Dùng DOCX template theo họ đề, thiết kế trực tiếp bằng Word và render bằng Open XML SDK.
* Template chuẩn gồm header bệnh viện/hội đồng, tiêu đề bài thi, thông tin thí sinh, điểm, câu hỏi, bảng phụ lục, vùng chữ ký và footer.
* Dữ liệu thay thế qua placeholder/content control như `{{CANDIDATE_NAME}}`, `{{EXAM_DATE}}`, `{{QUESTION_BLOCKS}}`, `{{PATIENT_TABLE}}` và `{{DRUG_SUPPLY_TABLE}}`.
* Chuẩn hóa A4, font Times New Roman, margin, spacing và bảng tại template thay vì hard-code định dạng rải rác trong mã.

## 9. Quy tắc phát triển

1. Ưu tiên validation fail-fast: dữ liệu hoặc mapping thiếu phải báo rõ nguyên nhân và chặn sinh đề.
2. Mọi quy tắc đã chốt, đặc biệt TE1, user không trùng, một khoa-một kho và phạm vi SQL, phải có unit test.
3. Không xây dựng UI trước khi domain model, query contract và preflight được chốt.
4. Không tự động ghi vào HIS từ ứng dụng.
5. Mọi thay đổi kiến trúc hoặc quy tắc nghiệp vụ phải được ghi vào `agentlog.md`.

## 10. Đặc tả UX desktop kế thừa webapp cũ

Trước khi tiếp tục phát triển giao diện Windows, phải coi webapp cũ tại `frontend/index.html` và `frontend/app.js` là đặc tả vận hành đầy đủ. Desktop không chỉ là form Settings, mapping và sinh một DOCX đơn lẻ; nó phải tái tạo sáu phân hệ dưới đây bằng WPF/MVVM, đồng thời thay các giới hạn file-based bằng SQLite snapshot.

### 10.1. Khung điều hướng và trạng thái chung

1. Header hiển thị tên hệ thống, Hội đồng tuyển dụng và nút đổi sáng/tối.
2. Sáu phân hệ chính theo thứ tự vận hành:
   * Thực thi tạo đề.
   * Quản lý mẫu đề.
   * Phân quyền Dịch vụ & Kho.
   * Cài đặt & Danh mục HIS.
   * Tra cứu tồn kho.
   * Lịch sử sinh đề.
3. Mỗi view có ViewModel riêng, `OnActivatedAsync`, trạng thái loading/error rành mạch và dữ liệu bind qua `ObservableCollection`; không tái tạo cách render DOM của webapp.
4. Dữ liệu dùng chung gồm khoa/phòng, nhóm dịch vụ, kho, action, template, mapping, catalog status và history. Khi catalog thay đổi, các view phụ thuộc phải được refresh có chủ đích.

### 10.2. Phân hệ Thực thi tạo đề

Mục tiêu là xây dựng đợt thi và sinh toàn bộ hoặc một phần thí sinh. Đây là màn hình trung tâm, không phải form sinh một đề thử.

| Khu vực | Trường/chức năng bắt buộc |
|---|---|
| Cấu hình đợt thi | Ngày thi, tên đợt thi, trạng thái preflight |
| Form thí sinh | Họ tên, SBD tùy chọn, khoa/phòng, mẫu đề phụ thuộc khoa |
| Điểm cá nhân | Danh sách câu từ template, điểm từng câu, bỏ câu, badge tổng điểm |
| Danh sách thí sinh | Chọn/bỏ chọn, họ tên, SBD, khoa, template, xóa dòng, tổng số đã chọn |
| Sinh đề | Sinh phần đã chọn, sinh cả đợt, progress, hủy khi chưa ghi output |
| Kết quả mới nhất | Tải ZIP, xem SQL nếu có, tải lẻ DOCX/XLSX và chi tiết từng thí sinh |

Quy tắc UI bắt buộc:

1. Chọn khoa mới hiển thị các mẫu đề thuộc khoa đó.
2. Mẫu HIS thiếu user đúng khoa phải bị vô hiệu hóa và nêu lý do.
3. Tổng điểm khác 10 phải cảnh báo rõ; quyết định có chặn hay không do rule nghiệp vụ ở Application layer.
4. `Sinh phần đã chọn` chỉ dùng các thí sinh đã tích; `Sinh cả đợt` bỏ qua trạng thái tích.
5. Preflight phải chạy trước generate và hiển thị lỗi theo thí sinh/khoa/template, không fallback dữ liệu ngoài khoa.

### 10.3. Phân hệ Quản lý mẫu đề

Mục tiêu là quản lý template động, không hard-code đề vào giao diện.

| Chức năng | Hành vi cần có |
|---|---|
| Lọc | Lọc template theo khoa/phòng |
| Danh sách | Hiển thị tên, khoa, vị trí, action, tổng điểm, trạng thái user HIS |
| Tạo/sửa | Tên mẫu, khoa áp dụng, vị trí, tiếp nhận trực tiếp/chuẩn bị nhận vào khoa, danh sách action và điểm |
| Action editor | Checkbox action, mô tả, category, điểm theo bước 0.5, params chuyên biệt khi action có hỗ trợ |
| Clone | Chọn khoa đích, tên mới, sao chép action/điểm/flags |
| Xóa | Dialog xác nhận |

Mỗi template phải lưu rõ `requires_direct_reception` và `requires_exam_setup_sql`; hai cờ loại trừ nhau. Template editor phải thể hiện các điều kiện catalog/mapping cần có cho từng action.

### 10.4. Phân hệ Phân quyền Dịch vụ và Kho

Desktop phải kế thừa layout master-detail của webapp, không chỉ dùng hai combobox độc lập.

| Vùng | Hành vi cần có |
|---|---|
| Cột trái | Tìm khoa/phòng, danh sách khoa, số nhóm dịch vụ, kho thi và trạng thái sẵn sàng |
| Detail khoa | Tên khoa, user HIS khả dụng, kho thi duy nhất, nhóm dịch vụ đã chọn |
| Nhóm dịch vụ | Search, chọn nhiều, tags/list selected, chọn tất cả, bỏ toàn bộ |
| Kho thi | Searchable ComboBox từ catalog kho; chỉ chọn một kho theo rule mới |
| Sao chép | Chọn khoa nguồn và sao chép nhóm dịch vụ + kho thi sang khoa đích |
| Lưu | Lưu khoa đang chọn; nêu rõ thay đổi chưa lưu |

Khác biệt chủ đích với webapp cũ: webapp cho phép nhiều kho dược một khoa, còn desktop chỉ cho một kho thi. Mapping dịch vụ vẫn là nhiều nhóm cho một khoa. Đồng bộ/import mapping Excel là cơ chế hỗ trợ, không thay thế cấu hình kiểm soát trên màn hình này.

### 10.5. Phân hệ Cài đặt và Danh mục HIS

Gộp connection profile và quản trị catalog thành một phân hệ, kế thừa ý tưởng sáu card catalog của webapp cũ.

| Card/catalog | Chỉ số và hành động |
|---|---|
| Bệnh nhân | Số dòng, đã dùng/còn dùng được, snapshot time, sync SQL, import/export Excel |
| Thuốc & VTYT | Số dòng, snapshot time, sync SQL, import/export Excel |
| Dịch vụ CLS | Số dòng, snapshot time, sync SQL, import/export Excel |
| User HIS | Số dòng, số user theo khoa, snapshot time, sync SQL, import/export Excel |
| Mapping dịch vụ | Số liên kết, sync/import/export |
| Mapping kho | Số liên kết, sync/import/export |

Settings SQL Server phải hỗ trợ profile name, server/instance, database, Windows/SQL Authentication, username, password lưu bằng DPAPI, active profile, test connection và trạng thái lỗi có thể copy. Mọi thao tác đồng bộ HIS chỉ dùng query `SELECT` versioned. Mỗi card phải hiển thị số record hợp lệ, record lỗi, phiên bản query và thời điểm snapshot SQLite.

### 10.6. Phân hệ Tra cứu tồn kho

Đây là màn hình tra cứu snapshot, không phải tồn HIS live.

| Bộ lọc | Yêu cầu |
|---|---|
| Khoa/phòng | Chọn từ catalog; khi chọn khoa, kho mặc định/khả dụng phải theo mapping khoa-kho |
| Kho | Chọn từ catalog kho |
| Nguồn | Lấy từ snapshot thực tế, không hard-code chỉ BH/VP |
| Mã/tên thuốc | Tìm không dấu, debounce |
| Tồn tối thiểu | Có trường lọc |

DataGrid kết quả cần hiển thị mã, tên, đơn vị tính, khoa, kho, nguồn, tồn và thời điểm snapshot; header phải nêu tổng kết quả, tổng tồn, thời điểm catalog và nhãn “Dữ liệu snapshot, không phải tồn kho thời gian thực”.

### 10.7. Phân hệ Lịch sử sinh đề

Mỗi batch phải có Expander/card hiển thị ngày sinh, ngày thi, số thí sinh, khoa, snapshot/template version, ZIP và SQL nếu có. Bên trong là DataGrid cho từng thí sinh: SBD, khoa, template, tổng điểm, DOCX, XLSX và SQL liên quan.

Phải hỗ trợ tìm theo batch/thí sinh/SBD/khoa/template, refresh, preview SQL, copy SQL, tải lại file, xóa metadata và tùy chọn xóa cả output files. Output không được tự xóa khi history còn tham chiếu mà không cảnh báo rõ.

### 10.8. Trình tự phát triển giao diện

1. Hoàn chỉnh Application/SQLite repositories cho template, mapping, batch, candidate, history và catalog snapshots.
2. Xây dựng Shell + sáu ViewModel/View theo đặc tả này; dùng dữ liệu demo/local để xác nhận UX trước.
3. Hoàn chỉnh template/action editor và Execution View vì đây là trọng tâm tạo đề.
4. Hoàn chỉnh Catalog View + SQL Server query contracts, rồi thay demo data bằng snapshot HIS.
5. Hoàn chỉnh engine `Preflight -> ExamScenario -> DOCX/XLSX/SQL -> ZIP -> History`.
6. Chỉ publish lại khi sáu phân hệ đã có luồng rõ ràng; không tiếp tục phát hành các bản chỉ thay một form MVP rời rạc.

---

## 11. Trạng thái Triển khai Hiện hành (Tháng 9/2026)

Hệ thống **Exam Operations Desktop (C# .NET 8 LTS / WPF / SQLite)** đã hoàn thành các cấu phần trọng yếu sau:

1. **Phân bổ Kịch bản Khảo thí (`ExamScenarioAllocator`):**
   * Đảm bảo tuân thủ nghiêm ngặt **Quy tắc Rule 3.2**: Mỗi thí sinh được tự động cấp đúng một User HIS riêng biệt thuộc khoa đăng ký thi.
   * Cơ chế **Preflight Fail-Fast**: Tự động phát hiện và chặn sinh đề nếu số lượng thí sinh đăng ký trong khoa vượt quá số User HIS khả dụng.
   * Tự động bốc bệnh nhân nguồn hoặc sinh bệnh nhân biến thể khảo thí (hạn thẻ BHYT chuẩn hóa, mã TE1 5 năm, nơi ĐKKCB `66232`).
   * Phân bổ thuốc tồn kho dương từ đúng kho thi đã map và phân bổ dịch vụ kỹ thuật theo phân quyền của khoa.
   * Liên kết câu hỏi: Câu đổi dịch vụ hủy đúng dịch vụ đã chỉ định, câu trả thuốc trả đúng thuốc đã kê.

2. **Bộ sinh T-SQL Khảo thí (`SqlScriptGenerator`):**
   * Tích hợp trực tiếp logic từ `Scripts/02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql`.
   * Đối với đề Tiếp nhận trực tiếp (`requires_direct_reception`): In cảnh báo hướng dẫn thí sinh tự tiếp nhận ca, tuyệt đối không sinh SQL nạp sẵn.
   * Đối với đề Nhận bệnh vào khoa (`requires_exam_setup_sql`): Sinh script T-SQL đầy đủ tham số để chạy thủ công trên server thi, không sinh câu lệnh giả.

3. **Bộ sinh Tài liệu Word (`WordExamWriter`):**
   * Render trực tiếp tệp tin DOCX chuẩn A4 bằng OpenXML SDK.
   * Khung tiêu đề Bệnh viện Đa khoa Thiện Hạnh và Hội đồng tuyển dụng.
   * Bảng thông tin thí sinh và tài khoản HIS riêng biệt (mật khẩu mặc định `123`).
   * Bảng thông tin hành chính bệnh nhân (PID, BHYT, DKKCB 66232, Chẩn đoán sơ bộ).
   * Bảng chỉ định dịch vụ CLS và bảng y lệnh thuốc & VTYT (liều dùng, đường dùng theo ĐVT).
   * Thang điểm chi tiết từng câu và khung chữ ký 2 Cán bộ chấm thi & Thí sinh xác nhận.

4. **Giao diện Modern Healthcare UI:**
   * Design System `ModernTheme.xaml` với bảng màu chuẩn y tế: Deep Navy (`#0F172A`), Slate (`#1E293B`), Medical Teal (`#0D9488`), Cyan (`#0284C7`), nền sáng `#F8FAFC`.
   * Thẻ Card bo góc mềm mại (`CornerRadius="8"`), viền mỏng `#E2E8F0`.
   * Dashboard thống kê trực quan 4 chỉ số (Khoa, Kho, Thuốc có tồn, User HIS).
   * 6 Phân hệ hoàn chỉnh: Thực thi tạo đề, Quản lý mẫu đề, Phân quyền dịch vụ & kho, Cài đặt & Danh mục HIS, Tra cứu tồn kho, Lịch sử sinh đề.

5. **Lưu trữ & Khảo thí Offline:**
   * Database SQLite cục bộ `exam-generator.sqlite` cạnh phần mềm.
   * Quản lý bảng `UsedPatient` chống tái sử dụng bệnh nhân.
   * Tích hợp bộ dữ liệu mẫu đa khoa phòng mặc định để người dùng trải nghiệm ngay lập tức mà không cần mạng SQL Server.

6. **Kiểm thử & Đóng gói:**
   * Toàn bộ 14/14 tests đơn vị trong `desktop/tests/ExamGenerator.Tests` đều đạt (`Passed: 14, Failed: 0`).
   * Đóng gói bản Release sẵn sàng vận hành tại `desktop/publish/ExamOperationsDesktop/`.

