-- =========================================================================
-- SCRIPT 07: TRÍCH XUẤT PHÂN QUYỀN NHÓM DỊCH VỤ THEO KHOA PHÒNG
-- Hệ thống: HIS eHospital - Bệnh viện Đa khoa Thiện Hạnh
-- Kết quả xuất ra file Excel: 'service_mappings.xlsx'
-- =========================================================================

USE [eHospital_ThienHanh]
GO

SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SET NOCOUNT ON;

SELECT DISTINCT
    RTRIM(LTRIM(pb.TenPhongBan)) AS [TenKhoaPhong],
    RTRIM(LTRIM(ISNULL(NULLIF(nhom.TenNhomDichVu, ''), ISNULL(NULLIF(loai.TenLoaiDichVu, ''), N'Khác')))) AS [NhomDichVu]
FROM DM_PhongBan_DichVu pbdv WITH (NOLOCK)
INNER JOIN DM_PhongBan pb WITH (NOLOCK) 
    ON pbdv.PhongBan_Id = pb.PhongBan_Id
INNER JOIN DM_DichVu dv WITH (NOLOCK) 
    ON pbdv.DichVu_Id = dv.DichVu_Id
LEFT JOIN DM_NhomDichVu nhom WITH (NOLOCK)
    ON dv.NhomDichVu_Id = nhom.NhomDichVu_Id
LEFT JOIN DM_LoaiDichVu loai WITH (NOLOCK)
    ON nhom.LoaiDichVu_Id = loai.LoaiDichVu_Id
WHERE ISNULL(pb.TamNgung, 0) = 0
  AND ISNULL(dv.TamNgung, 0) = 0
  AND ISNULL(dv.CoGiaDichVu, 0) = 1
  AND pb.TenPhongBan IS NOT NULL
  AND LEN(RTRIM(LTRIM(pb.TenPhongBan))) > 0
  AND NULLIF(RTRIM(LTRIM(ISNULL(NULLIF(nhom.TenNhomDichVu, ''), ISNULL(NULLIF(loai.TenLoaiDichVu, ''), '')))), '') IS NOT NULL
ORDER BY [TenKhoaPhong], [NhomDichVu];
GO
