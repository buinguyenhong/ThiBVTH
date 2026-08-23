-- =========================================================================
-- SCRIPT 07: TRÍCH XUẤT DANH MỤC NHÓM DỊCH VỤ CẬN LÂM SÀNG TỪ HIS
-- Hệ thống: HIS eHospital - Bệnh viện Đa khoa Thiện Hạnh
-- Mục đích: Lấy danh sách tên Nhóm Dịch Vụ chuẩn trên HIS để cấu hình
--           phân quyền chỉ định cho các Khoa phòng lâm sàng.
-- =========================================================================

USE [eHospital_ThienHanh]
GO

SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SET NOCOUNT ON;

SELECT DISTINCT
    MaNhom = RTRIM(LTRIM(nhom.MaNhomDichVu)),
    TenNhomDichVu = RTRIM(LTRIM(nhom.TenNhomDichVu)),
    LoaiDichVu = RTRIM(LTRIM(ISNULL(loai.TenLoaiDichVu, N'Khác'))),
    TrangThai = N'Đang sử dụng'
FROM dbo.DM_NhomDichVu nhom WITH (NOLOCK)
LEFT JOIN dbo.DM_LoaiDichVu loai WITH (NOLOCK)
    ON nhom.LoaiDichVu_Id = loai.LoaiDichVu_Id
WHERE ISNULL(nhom.TamNgung, 0) = 0
  AND NULLIF(RTRIM(LTRIM(nhom.TenNhomDichVu)), N'') IS NOT NULL
ORDER BY LoaiDichVu, TenNhomDichVu;
GO
