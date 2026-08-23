/*
    XUẤT DANH MỤC DỊCH VỤ ĐANG HOẠT ĐỘNG VÀ CÓ GIÁ TRÊN HIS EHOSPITAL
    -------------------------------------------------------------------------
    Script chỉ đọc dữ liệu, không INSERT/UPDATE/DELETE.

    Lấy toàn bộ dịch vụ:
      - Đang hoạt động (TamNgung = 0).
      - Có giá dịch vụ (CoGiaDichVu = 1).
      - Nhóm Dịch Vụ lấy từ DM_NhomDichVu / DM_LoaiDichVu.

    Kết quả trả đúng thứ tự cột của data/catalogs/services.xlsx:
      MaDichVu, TenDichVu, NhomDichVu, KhoaThucHien, TrangThai

    Cách dùng:
      1. Chạy script trên SQL Server của HIS.
      2. Trong SSMS, lưu kết quả hoặc sao chép vào Excel.
      3. Lưu thành services.xlsx và tải lên ứng dụng.
*/

USE [eHospital_ThienHanh];
GO

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT DISTINCT
    MaDichVu = LTRIM(RTRIM(dv.MaDichVu)),
    TenDichVu = LTRIM(RTRIM(dv.TenDichVu)),
    NhomDichVu = LTRIM(RTRIM(ISNULL(NULLIF(nhom.TenNhomDichVu, ''), ISNULL(NULLIF(loai.TenLoaiDichVu, ''), N'Khác')))),
    KhoaThucHien = LTRIM(RTRIM(ISNULL(phong.TenPhongBan, N''))),
    TrangThai = CAST(1 AS bit)
FROM dbo.DM_DichVu AS dv WITH (NOLOCK)
LEFT JOIN dbo.DM_NhomDichVu AS nhom WITH (NOLOCK)
  ON nhom.NhomDichVu_Id = dv.NhomDichVu_Id
LEFT JOIN dbo.DM_LoaiDichVu AS loai WITH (NOLOCK)
  ON loai.LoaiDichVu_Id = nhom.LoaiDichVu_Id
LEFT JOIN dbo.DM_PhongBan_DichVu AS noiThucHien WITH (NOLOCK)
  ON noiThucHien.DichVu_Id = dv.DichVu_Id
LEFT JOIN dbo.DM_PhongBan AS phong WITH (NOLOCK)
  ON phong.PhongBan_Id = noiThucHien.PhongBan_Id
WHERE ISNULL(dv.TamNgung, 0) = 0
  AND ISNULL(dv.CoGiaDichVu, 0) = 1
  AND NULLIF(LTRIM(RTRIM(dv.MaDichVu)), '') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(dv.TenDichVu)), N'') IS NOT NULL
ORDER BY
    NhomDichVu,
    MaDichVu;
GO
