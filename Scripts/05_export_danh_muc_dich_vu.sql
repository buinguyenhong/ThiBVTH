/*
    XUẤT DANH MỤC DỊCH VỤ ĐANG HOẠT ĐỘNG THEO KHOA/PHÒNG THỰC HIỆN
    -------------------------------------------------------------------------
    Script chỉ đọc dữ liệu, không INSERT/UPDATE/DELETE.

    Chỉ lấy dịch vụ:
      - Đang hoạt động.
      - Thuộc nhóm/loại dịch vụ đang hoạt động.
      - Đã được ánh xạ tới khoa/phòng thực hiện đang hoạt động.
      - Có cấu hình giá dịch vụ và không phải dữ liệu test.

    Kết quả trả đúng thứ tự cột của data/catalogs/services.xlsx:
      MaDichVu, TenDichVu, NhomDichVu, KhoaThucHien, TrangThai

    Cách dùng:
      1. Chạy script trên SQL Server của HIS.
      2. Trong SSMS, lưu lưới kết quả thành CSV hoặc sao chép vào Excel.
      3. Giữ nguyên tên và thứ tự 5 cột, lưu thành services.xlsx.
      4. Tải services.xlsx lên ứng dụng và bấm "Làm mới từ máy chủ".
*/

USE [eHospital_ThienHanh];
GO

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT DISTINCT
    MaDichVu = LTRIM(RTRIM(dv.MaDichVu)),
    TenDichVu = LTRIM(RTRIM(dv.TenDichVu)),
    NhomDichVu = CASE
        WHEN UPPER(ISNULL(nhom.TenKhongDau, N'')) LIKE N'%SIEU AM%'
          OR UPPER(ISNULL(loai.TenKhongDau, N'')) LIKE N'%SIEU AM%'
          OR UPPER(ISNULL(nhom.MaNhomDichVu, '')) LIKE '%SIEUAM%'
            THEN N'Siêu âm'
        WHEN UPPER(ISNULL(nhom.TenKhongDau, N'')) LIKE N'%X QUANG%'
          OR UPPER(ISNULL(nhom.TenKhongDau, N'')) LIKE N'%XQUANG%'
          OR UPPER(ISNULL(loai.TenKhongDau, N'')) LIKE N'%X QUANG%'
          OR UPPER(ISNULL(nhom.MaNhomDichVu, '')) LIKE '%XQUANG%'
            THEN N'X-quang'
        WHEN UPPER(ISNULL(nhom.TenKhongDau, N'')) LIKE N'%XET NGHIEM%'
          OR UPPER(ISNULL(loai.TenKhongDau, N'')) LIKE N'%XET NGHIEM%'
          OR UPPER(ISNULL(loai.MaLoaiDichVu, '')) LIKE '%XETNGHIEM%'
            THEN N'Xét nghiệm'
        WHEN UPPER(ISNULL(nhom.TenKhongDau, N'')) LIKE N'%CT SCAN%'
          OR UPPER(ISNULL(nhom.TenKhongDau, N'')) LIKE N'%CTSCAN%'
            THEN N'CT Scan'
        WHEN UPPER(ISNULL(nhom.TenKhongDau, N'')) LIKE N'%NOI SOI%'
          OR UPPER(ISNULL(loai.TenKhongDau, N'')) LIKE N'%NOI SOI%'
            THEN N'Nội soi'
        WHEN UPPER(ISNULL(loai.TenKhongDau, N'')) LIKE N'%KHAM BENH%'
          OR UPPER(ISNULL(loai.MaLoaiDichVu, '')) LIKE '%KHAMBENH%'
            THEN N'Khám bệnh'
        WHEN UPPER(ISNULL(loai.TenKhongDau, N'')) LIKE N'%THU THUAT%'
          OR UPPER(ISNULL(loai.MaLoaiDichVu, '')) LIKE '%THUTHUAT%'
            THEN N'Thủ thuật'
        ELSE COALESCE(
            NULLIF(LTRIM(RTRIM(nhom.TenNhomDichVu)), N''),
            NULLIF(LTRIM(RTRIM(loai.TenLoaiDichVu)), N''),
            N'Khác'
        )
    END,
    KhoaThucHien = LTRIM(RTRIM(phong.TenPhongBan)),
    TrangThai = CAST(1 AS bit)
FROM dbo.DM_DichVu AS dv
JOIN dbo.DM_NhomDichVu AS nhom
  ON nhom.NhomDichVu_Id = dv.NhomDichVu_Id
JOIN dbo.DM_LoaiDichVu AS loai
  ON loai.LoaiDichVu_Id = nhom.LoaiDichVu_Id
JOIN dbo.DM_PhongBan_DichVu AS noiThucHien
  ON noiThucHien.DichVu_Id = dv.DichVu_Id
JOIN dbo.DM_PhongBan AS phong
  ON phong.PhongBan_Id = noiThucHien.PhongBan_Id
WHERE ISNULL(dv.TamNgung, 0) = 0
  AND ISNULL(nhom.TamNgung, 0) = 0
  AND ISNULL(loai.TamNgung, 0) = 0
  AND ISNULL(phong.TamNgung, 0) = 0
  AND ISNULL(dv.Test, 0) = 0
  AND ISNULL(dv.CoGiaDichVu, 0) = 1
  AND NULLIF(LTRIM(RTRIM(dv.MaDichVu)), '') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(dv.TenDichVu)), N'') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(phong.TenPhongBan)), N'') IS NOT NULL
ORDER BY
    KhoaThucHien,
    NhomDichVu,
    MaDichVu;
GO
