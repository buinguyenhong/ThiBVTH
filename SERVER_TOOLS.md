# Công cụ quản lý server

Trên Windows, chạy các file tại thư mục gốc của dự án:

- `start-server.cmd`: khởi động server nền tại http://127.0.0.1:8000.
- `stop-server.cmd`: dừng đúng tiến trình server đã được công cụ khởi động.
- `server-status.cmd`: kiểm tra trạng thái server và API.

Có thể nhấp đúp file `.cmd` trong File Explorer hoặc chạy từ PowerShell:

```powershell
.\start-server.cmd
.\server-status.cmd
.\stop-server.cmd
```

PID và log được lưu trong thư mục `.server/`. Công cụ không dừng một tiến
trình khác nếu PID đã bị tái sử dụng, và không khởi động nếu cổng 8000 đang
được chương trình khác sử dụng.

Hãy chạy lệnh dừng/khởi động bằng cùng mức quyền Windows đã dùng khi mở
server. Nếu server được mở bằng quyền Administrator nhưng công cụ được chạy
bằng quyền thường, Windows có thể trả lỗi `Access is denied`.
