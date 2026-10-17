# KẾ HOẠCH KIẾN TRÚC & XÂY DỰNG ỨNG DỤNG EXAMCLIENT & PHÂN HỆ CHẤM THI

> **Dự án:** Hệ thống Khảo thí & Tạo Đề thi BVĐK Thiện Hạnh (ExamGenerator)  
> **Tài liệu:** Thiết kế Chi tiết Phân hệ `ExamClient` & Phân hệ Chấm thi Đối soát (Assisted Grading)  
> **Cập nhật:** 17/10/2026 (Phiên bản 2.0 - Bổ sung cơ chế Bù giờ sự cố, Gửi bài 1-Click và Chấm thi Đối soát)  

---

## 1. TỔNG QUAN & BỐI CẢNH

Hệ thống quản lý kỳ thi tay nghề BVĐK Thiện Hạnh bước vào giai đoạn số hóa phòng thi:
* **`ExamOperationsDesktop` (Máy Quản trị / Giám khảo):** Đảm nhiệm cấu hình, sync HIS, sinh đề thi, lưu trữ đáp án chuẩn và **Chấm thi đối soát**.
* **`ExamClient` (Máy trạm Thí sinh):** Ứng dụng độc lập cài trên các máy tính phòng thi, tối giản, trực quan, hỗ trợ thi thực hành trên HIS kết hợp trả lời câu tra cứu.

---

## 2. NGUYÊN TẮC BẢO MẬT & PHÂN TÁCH RANH GIỚI

```
┌─────────────────────────────────────────────────────────────────┐
│     ỨNG DỤNG 1: ExamOperationsDesktop (MÁY GIÁM KHẢO / ADMIN)   │
│                                                                 │
│  • Kết nối trực tiếp SQL Server HIS (eHospital).                │
│  • Lưu trữ toàn bộ Đáp án chuẩn & Dữ liệu gốc trong SQLite.     │
│  • Xuất "Gói đề thi phòng thi" (.exam_pkg) KHÔNG CHỨA ĐÁP ÁN.    │
│  • [MỚI] Màn hình CHẤM THI ĐỐI SOÁT:                           │
│    - Tự động nạp bài nộp của thí sinh.                          │
│    - Hiển thị song song: Kết quả thí sinh làm vs Đáp án chuẩn.   │
│    - Có ô nhập Điểm từng câu & Ô nhập Chú thích từng câu.       │
│    - Tự động tính tổng điểm và xuất Biên bản chấm thi.          │
└───────────────────────────┬─────────────────────────────────────┘
                            │ Gửi Gói đề thi (.exam_pkg)
                            │ Nhận Gói bài nộp 1-Click (.sub_pkg)
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│     ỨNG DỤNG 2: ExamClient.exe (MÁY TRẠM PHÒNG THI THÍ SINH)     │
│                                                                 │
│  • Giao diện CỰC KỲ ĐƠN GIẢN, RÕ RÀNG, phông chữ to, dễ đọc.    │
│  • Đăng nhập nhanh bằng Mã thí sinh / SBD.                      │
│  • Thời gian đếm ngược tuân theo THỜI GIAN RIÊNG CỦA MỖI ĐỀ.   │
│  • Cơ chế BÙ GIỜ SỰ CỐ: Giám thị nhập Pass + Số phút bù thêm.  │
│  • Ô nhập liệu câu tra cứu (Tự động lưu real-time chống mất).  │
│  • GỬI KẾT QUẢ ĐẾN MÁY CHẤM VỚI 1 NÚT BẤM DUY NHẤT.             │
│  • TUYỆT ĐỐI KHÔNG chứa: Chuỗi kết nối HIS hay Đáp án thi.     │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. THIẾT KẾ CHI TIẾT ỨNG DỤNG EXAMCLIENT (MÁY TRẠM THÍ SINH)

### 3.1. Giao diện Đơn giản & Rõ ràng
* Thiết kế phông chữ rõ nét (`Segoe UI` / `Inter`, cỡ chữ 14-16pt), phân chia khối trắng - xám thanh lịch, độ tương phản cao để thí sinh không bị mỏi mắt.
* Chỉ có 2 trạng thái màn hình:
  1. **Màn hình Đăng nhập (Login Screen):** Nhập Số báo danh (SBD) $\rightarrow$ Nút **"VÀO THI"**.
  2. **Màn hình Làm bài (Exam Workspace):** Chia 2 vùng chính:
     * **Bảng điều khiển trên cùng (Header Panel):**
       - Họ tên thí sinh, SBD, Khoa dự thi.
       - Tài khoản HIS được cấp: `User HIS: [THI_NOI_01]` (kèm nút Copy).
       - **Đồng hồ đếm ngược:** Hiển thị dạng thẻ đồng hồ số lớn `MM:SS`.
       - Nút **"⏰ Bù giờ sự cố"** (dành cho Giám thị).
       - Nút **"📤 NỘP BÀI & GỬI KẾT QUẢ"** (Nổi bật, xác nhận 1-Click).
     * **Danh sách Câu hỏi:**
       - Câu thực hành trên HIS: Hiển thị yêu cầu rõ ràng, nhắc nhở quyển hóa đơn `THI` / `VAT_THI`.
       - Câu tra cứu (buồng, giường, tồn kho, bệnh sử...): Có các ô nhập liệu (Text box) rõ ràng ngay dưới câu hỏi để thí sinh điền.

### 3.2. Quản lý Thời gian & Cơ chế Bù giờ Sự cố (Extra Time)
1. **Thời gian ban đầu:**
   * Không cố định cứng, mà **tuân thủ nghiêm ngặt thời lượng của đề bài** (`DurationMinutes` trong gói đề thi: ví dụ 20 phút, 30 phút, 45 phút tùy vị trí dự thi).
2. **Quy trình Bù giờ khi gặp sự cố:**
   * Trong lúc thi, nếu máy trạm gặp sự cố (máy đơ, mạng chập chờn, khởi động lại, sự cố điện...), Giám thị bấm nút **"⏰ Bù giờ sự cố"** trên màn hình ExamClient.
   * **Hộp thoại bảo vệ (Proctor Modal):**
     * Yêu cầu nhập **Mật khẩu giám thị** (Mật khẩu được Ban tổ chức cấp cho kỳ thi).
     * Nhập **Số phút được bù thêm** (ví dụ: `+5` phút, `+10` phút).
     * Ô nhập **Lý do bù giờ** (ví dụ: *"Khởi động lại máy do lỗi màn hình"*).
   * **Xử lý sau khi bù giờ:**
     * Đồng hồ đếm ngược lập tức được cộng thêm số phút tương ứng.
     * Mở khóa để thí sinh tiếp tục làm bài bình thường.
     * Thông tin bù giờ (Số phút, Giờ bù, Lý do) được ghi vết vào gói bài thi gửi về giám khảo.

### 3.3. Nộp bài & Gửi kết quả 1-Click
* Thí sinh làm bài xong bấm nút **"📤 Nộp bài & Gửi kết quả"** (hoặc hệ thống tự động kích hoạt khi hết giờ).
* **Cơ chế 1-Click:**
  - Phần mềm tự động đóng gói bài làm (bao gồm câu trả lời tra cứu, thời điểm bắt đầu/kết thúc, lịch sử bù giờ, mã máy).
  - Tự động đẩy file kết quả (`Candidate_[SBD]_[Batch].sub_pkg`) vào thư mục nộp bài chung trên mạng LAN nội bộ (Shared Folder) hoặc gửi qua HTTP POST tới máy giám khảo.
  - Khóa màn hình làm bài, hiển thị thông báo thành công: *"Đã gửi kết quả bài thi thành công lúc HH:mm:ss. Điểm và thao tác của bạn đã được lưu trữ an toàn."*

---

## 4. THIẾT KẾ PHÂN HỆ CHẤM THI ĐỐI SOÁT (ASSISTED GRADING) TRÊN DỰ ÁN CŨ

> **NGUYÊN TẮC QUAN TRỌNG:**  
> Hệ thống **KHÔNG tự động chấm áp đặt điểm số**, mà đóng vai trò là **Trợ lý Giám khảo thông minh**:  
> Trích xuất và hiển thị toàn bộ kết quả thực tế của thí sinh đối chiếu với đáp án chuẩn, để **Giám khảo toàn quyền quyết định điểm số và ghi chú**.

### 4.1. Giao diện Màn hình Chấm thi (Tab 7 trên `ExamOperationsDesktop`)
Màn hình được thiết kế trực quan, khoa học:
* **Phía trên:** Danh sách thí sinh của đợt thi (SBD, Họ tên, Khoa, Trạng thái bài nộp: *Đã nộp lúc HH:mm:ss, Đã bù giờ +5p, v.v.*).
* **Phía dưới:** Bảng đối soát chi tiết từng câu của thí sinh đang chọn:

| Câu | Nội dung yêu cầu & Thang điểm | Đáp án chuẩn / Tiêu chí | Kết quả thực tế thí sinh làm được | Điểm chấm (Giám khảo nhập) | Chú thích / Nhận xét (Giám khảo nhập) |
|:---:|---|---|---|:---:|---|
| **Câu 1** | Tiếp nhận BN Viện phí & BHYT *(1.0đ)* | Tiếp nhận đúng 2 BN: Trần Văn A (VP), Lê Thị B (BHYT) | **HIS Audit:**<br>• User `THI_NOI_01` đã tạo 02 tiếp nhận: `TN260012` (08:05:12), `TN260015` (08:07:30). Đúng đối tượng. | `[ 1.0 ]` *(1.0đ)* | `[ Thao tác chuẩn, nhanh ]` |
| **Câu 2** | Chỉ định CLS Siêu âm & Xquang *(2.0đ)* | Chỉ định: Siêu âm bụng, Xquang ngực thẳng | **HIS Audit:**<br>• Đã tạo phiếu CLS `CLS260089` (08:12:00) gồm 2 dịch vụ đúng danh mục. | `[ 2.0 ]` *(2.0đ)* | `[ Đạt ]` |
| **Câu 3** | Thu tạm ứng Nội trú & Ngoại trú *(2.0đ)* | • BN nội trú 21.004865: 1.500.000đ<br>• BN ngoại trú 21001726/NG: 500.000đ | **HIS Audit:**<br>• Hóa đơn `HD01`: 1.500.000đ (08:15:20, HĐ THI)<br>• Hóa đơn `HD02`: 500.000đ (08:18:10, HĐ THI) | `[ 2.0 ]` *(2.0đ)* | `[ Đúng quyển hóa đơn THI ]` |
| **Câu 5** | Kiểm tra tồn kho cơ số *(1.0đ)* | Paracetamol 500mg (01/01-15/01/2026): **125 Viên** | **Thí sinh điền trên Client:**<br>`125` *(Khớp 100% số tồn cuối kỳ)* | `[ 1.0 ]` *(1.0đ)* | `[ Chính xác ]` |
| **Câu 6** | Tra cứu buồng giường *(1.0đ)* | Lê Văn Dũng (1989):<br>• Khoa: Nội tổng hợp<br>• Phòng: 204<br>• Giường: G08 | **Thí sinh điền trên Client:**<br>• Khoa: Nội TH<br>• Phòng: P204<br>• Giường: Giường 8 | `[ 1.0 ]` *(1.0đ)* | `[ Đúng thông tin ]` |

### 4.2. Tiện ích hỗ trợ Giám khảo chấm thi
1. **Nút gợi ý điểm nhanh (Quick-fill Suggestion):**
   * Nếu câu hỏi khớp hoàn toàn (câu điền khớp chuỗi, câu thực hành có đầy đủ chứng từ trên HIS), cạnh ô nhập điểm có nút nhỏ `[=]` để giám khảo bấm 1 click là điền điểm tối đa, không cần gõ phím.
   * Giám khảo có thể điều chỉnh giảm điểm bất kỳ lúc nào nếu thí sinh thao tác chậm hoặc vi phạm quy chế.
2. **Tự động tính tổng điểm:**
   * Cột Tổng điểm tự động cập nhật ngay khi giám khảo gõ điểm từng câu.
3. **Xuất Báo cáo Kết quả (Excel / Word):**
   * Bấm nút **"Xuất Bảng Điểm Tổng Hợp"**: Xuất file Excel/Word bảng điểm toàn bộ thí sinh trong đợt thi, có chữ ký của Trưởng ban chấm thi và Giám khảo 1, Giám khảo 2.

---

## 5. ĐẶC TẢ CẤU TRÚC DỮ LIỆU TRAO ĐỔI

### 5.1. Gói Đề thi gửi Client (`[BatchName]_[ExamDate].exam_pkg`)
```json
{
  "BatchId": "DOT_20261017_01",
  "BatchName": "Kỳ thi Tay nghề Khối Nội trú Đợt 1",
  "ExamDate": "2026-10-17",
  "ProctorPasswordHash": "SHA256_PROCTOR_PASSWORD",
  "Candidates": [
    {
      "CandidateId": "SBD01",
      "CandidateName": "Nguyễn Thị Mai",
      "Department": "Khoa Nội",
      "TemplateName": "Đề Điều dưỡng nội trú chuẩn",
      "DurationMinutes": 30,
      "HisUserCode": "THI_NOI_01",
      "Questions": [
        {
          "OrderIndex": 1,
          "ActionCode": "NT_NHAN_BENH_KHOA",
          "Title": "Tiếp nhận người bệnh vào khoa",
          "Instruction": "* Trần Văn B ; Mã y tế: 21004865...",
          "MaxScore": 1.0,
          "QuestionType": "PracticalHis"
        },
        {
          "OrderIndex": 5,
          "ActionCode": "TK_KIEM_TON_KHO",
          "Title": "Kiểm tra tồn kho dược cơ số",
          "Instruction": "Báo số tồn kho của dược trong kho cơ số (Nguồn Viện phí) từ ngày 01/01/2026 đến ngày 15/01/2026 : Tên dược: Paracetamol 500mg  số lượng tồn cuối : …………….",
          "MaxScore": 1.0,
          "QuestionType": "FillInBlank",
          "Fields": [
            { "FieldKey": "SoLuongTonCuoi", "FieldLabel": "Số lượng tồn cuối kỳ", "Unit": "Viên" }
          ]
        }
      ]
    }
  ]
}
```

### 5.2. Gói Bài thi gửi về Chấm (`Candidate_[SBD]_[Batch].sub_pkg`)
```json
{
  "BatchId": "DOT_20261017_01",
  "CandidateId": "SBD01",
  "CandidateName": "Nguyễn Thị Mai",
  "HisUserCode": "THI_NOI_01",
  "DurationMinutes": 30,
  "StartedAt": "2026-10-17T08:00:15+07:00",
  "SubmittedAt": "2026-10-17T08:33:10+07:00",
  "ExtraTimeMinutes": 5,
  "ExtraTimeReason": "Khởi động lại máy trạm do mất tín hiệu màn hình",
  "Answers": [
    {
      "OrderIndex": 5,
      "ActionCode": "TK_KIEM_TON_KHO",
      "FieldValues": {
        "SoLuongTonCuoi": "125"
      }
    }
  ],
  "ClientMachine": "PHONGTHI_MAY03",
  "Checksum": "A1B2C3D4..."
}
```

---

## 6. CÁC ĐIỂM LIÊN QUAN CẦN ĐIỀU CHỈNH TRÊN DỰ ÁN CŨ

| STT | Vị trí trên `ExamOperationsDesktop` | Nội dung chỉnh sửa | Tác động |
|:---:|---|---|---|
| **1** | **`ExamGenerator.Domain`** | Thêm enum `QuestionType` (PracticalHis, FillInBlank), danh sách `Fields` vào `ScenarioQuestion`, và model `CandidateSubmission`. | Hoàn toàn tương thích ngược, không ảnh hưởng chức năng cũ. |
| **2** | **Tab 1: Sinh đề thi** | Thêm nút **"📦 Xuất gói đề thi ExamClient (.exam_pkg)"** (Lưu file JSON mã hóa ra thư mục chỉ định). | Thêm 1 handler độc lập, không sửa logic xuất Word. |
| **3** | **Tab 7 (Mới): Chấm thi & Đối soát** | Thêm màn hình nạp bài làm `.sub_pkg`, đối soát dữ liệu với CSDL HIS và cung cấp lưới nhập điểm/nhận xét kèm nút Xuất Bảng điểm. | Tab độc lập mới 100%, không can thiệp Tab 1-6. |

---

## 7. KẾ HOẠCH TRIỂN KHAI TIẾP THEO

1. **Bước 1:** Bổ sung trường `QuestionType`, `FieldDefinitions` vào Domain của solution hiện tại.
2. **Bước 2:** Xây dựng project mới `ExamGenerator.Client` (WPF .NET 8) với giao diện tối giản: Màn hình Đăng nhập $\rightarrow$ Màn hình Làm bài $\rightarrow$ Đồng hồ đếm ngược theo đề $\rightarrow$ Dialog Bù giờ $\rightarrow$ Nộp bài 1-Click.
3. **Bước 3:** Thêm tính năng "Xuất gói đề thi ExamClient" tại Tab 1 và xây dựng "Tab 7: Chấm thi Đối soát" trên `ExamOperationsDesktop`.
4. **Bước 4:** Kiểm thử toàn diện quy trình khép kín: Sinh đề $\rightarrow$ Thí sinh làm bài trên Client $\rightarrow$ Bù giờ sự cố $\rightarrow$ Gửi bài 1-Click $\rightarrow$ Giám khảo đối soát, nhập điểm, ghi chú và xuất bảng tổng hợp.
