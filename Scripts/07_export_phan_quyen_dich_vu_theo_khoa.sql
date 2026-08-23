-- =========================================================================
-- SCRIPT 07: TRÍCH XUẤT MA TRẬN PHÂN QUYỀN NHÓM DỊCH VỤ THEO KHOA PHÒNG
-- Hệ thống: HIS eHospital - Bệnh viện Đa khoa Thiện Hạnh
-- Kết quả xuất ra file Excel: 'service_mappings.xlsx'
-- Cấu trúc 2 cột: [TenKhoaPhong], [NhomDichVu]
-- =========================================================================

USE [eHospital_ThienHanh]
GO

SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SET NOCOUNT ON;

-- Trích xuất danh sách các Khoa phòng lâm sàng và các Nhóm Dịch Vụ Cận Lâm Sàng
SELECT DISTINCT
    TenKhoaPhong = RTRIM(LTRIM(pb.TenPhongBan)),
    NhomDichVu = RTRIM(LTRIM(ISNULL(NULLIF(nhom.TenNhomDichVu, ''), ISNULL(NULLIF(loai.TenLoaiDichVu, ''), N'Khác'))))
FROM dbo.DM_PhongBan pb WITH (NOLOCK)
CROSS JOIN dbo.DM_NhomDichVu nhom WITH (NOLOCK)
LEFT JOIN dbo.DM_LoaiDichVu loai WITH (NOLOCK)
    ON nhom.LoaiDichVu_Id = loai.LoaiDichVu_Id
WHERE ISNULL(pb.TamNgung, 0) = 0
  AND ISNULL(nhom.TamNgung, 0) = 0
  AND (pb.LoaiPhongBan_Id = 568 OR pb.CoThucHienDichVu = 1)
  AND pb.TenPhongBan NOT LIKE N'%Kho Dược%'
  AND pb.TenPhongBan NOT LIKE N'%Khoa Dược%'
  AND NULLIF(RTRIM(LTRIM(pb.TenPhongBan)), N'') IS NOT NULL
  AND NULLIF(RTRIM(LTRIM(nhom.TenNhomDichVu)), N'') IS NOT NULL
ORDER BY TenKhoaPhong, NhomDichVu;
GO
