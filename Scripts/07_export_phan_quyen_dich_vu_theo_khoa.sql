-- =========================================================================
-- SCRIPT 07: TRÍCH XUẤT PHÂN QUYỀN NHÓM DỊCH VỤ THEO KHOA PHÒNG
-- Hệ thống: HIS eHospital - Bệnh viện Đa khoa Thiện Hạnh
-- Kết quả xuất ra file Excel: 'service_mappings.xlsx'
-- =========================================================================

USE [eHospital_ThienHanh]
GO

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
SET NOCOUNT ON;

SELECT DISTINCT
    RTRIM(LTRIM(pb.TenPhongBan)) AS [TenKhoaPhong],
    RTRIM(LTRIM(ISNULL(NULLIF(dv.Attribute2, ''), ISNULL(NULLIF(dv.Attribute1, ''), N'Khác')))) AS [NhomDichVu]
FROM DM_PhongBan_DichVu pbdv WITH (NOLOCK)
INNER JOIN DM_PhongBan pb WITH (NOLOCK) 
    ON pbdv.PhongBan_Id = pb.PhongBan_Id
INNER JOIN DM_DichVu dv WITH (NOLOCK) 
    ON pbdv.DichVu_Id = dv.DichVu_Id
WHERE ISNULL(pb.TamNgung, 0) = 0
  AND ISNULL(dv.TamNgung, 0) = 0
  AND pb.TenPhongBan IS NOT NULL
  AND LEN(RTRIM(LTRIM(pb.TenPhongBan))) > 0
ORDER BY [TenKhoaPhong], [NhomDichVu];
