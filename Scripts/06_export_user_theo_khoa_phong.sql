/*
    XUẤT USER ĐANG HOẠT ĐỘNG THEO ĐÚNG KHOA/PHÒNG
    -------------------------------------------------------------------------
    Script chỉ đọc dữ liệu, không INSERT/UPDATE/DELETE.

    Ánh xạ sử dụng:
      Sys_Users
        -> NhanVien_User_Mapping
        -> NhanVien
        -> DM_PhongBan

    Kết quả trả đúng thứ tự cột của data/catalogs/users.xlsx:
      TenDangNhap, MatKhau, HoTen, KhoaPhong

    LƯU Ý VỀ MẬT KHẨU:
      - Không xuất Sys_Users.User_Password vì mật khẩu HIS có thể đã mã hóa
        và không nên đưa vào file danh mục.
      - Hãy đặt @MatKhauThi bằng mật khẩu thực tế đã cấu hình cho các tài
        khoản dùng trong kỳ thi.

    Cách dùng:
      1. Sửa @MatKhauThi nếu mật khẩu tài khoản thi không phải 123.
      2. Chạy script trên SQL Server của HIS.
      3. Trong SSMS, lưu lưới kết quả thành CSV hoặc sao chép vào Excel.
      4. Giữ nguyên tên và thứ tự 4 cột, lưu thành users.xlsx.
      5. Tải users.xlsx lên ứng dụng và bấm "Làm mới từ máy chủ".
*/

USE [eHospital_ThienHanh];
GO

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

DECLARE @MatKhauThi nvarchar(100) = N'123';

SELECT DISTINCT
    TenDangNhap = LTRIM(RTRIM(users.User_Code)),
    MatKhau = @MatKhauThi,
    HoTen = COALESCE(
        NULLIF(LTRIM(RTRIM(nhanVien.TenNhanVien)), N''),
        NULLIF(LTRIM(RTRIM(users.User_Name)), N''),
        LTRIM(RTRIM(users.User_Code))
    ),
    KhoaPhong = LTRIM(RTRIM(phong.TenPhongBan))
FROM dbo.Sys_Users AS users
JOIN dbo.NhanVien_User_Mapping AS mapping
  ON mapping.User_Id = users.User_Id
JOIN dbo.NhanVien AS nhanVien
  ON nhanVien.NhanVien_Id = mapping.NhanVien_Id
JOIN dbo.DM_PhongBan AS phong
  ON phong.PhongBan_Id = nhanVien.PhongBan_Id
WHERE ISNULL(users.Suspend, 0) = 0
  AND (
      users.Expiration_Date IS NULL
      OR users.Expiration_Date >= CAST(GETDATE() AS date)
  )
  AND ISNULL(nhanVien.TamNgung, 0) = 0
  AND ISNULL(phong.TamNgung, 0) = 0
  AND NULLIF(LTRIM(RTRIM(users.User_Code)), N'') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(phong.TenPhongBan)), N'') IS NOT NULL
ORDER BY
    KhoaPhong,
    HoTen,
    TenDangNhap;
GO
