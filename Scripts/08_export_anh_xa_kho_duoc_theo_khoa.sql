-- =========================================================================
-- SCRIPT 08: TRÍCH XUẤT ÁNH XẠ KHO DƯỢC & TỦ TRỰC THEO KHOA PHÒNG
-- Hệ thống: HIS eHospital - Bệnh viện Đa khoa Thiện Hạnh
-- Kết quả xuất ra file Excel: 'pharmacy_mappings.xlsx'
-- =========================================================================

USE [eHospital_ThienHanh]
GO

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
SET NOCOUNT ON;

-- 1. Lấy ánh xạ từ Danh mục Kho Dược (dm_khoduoc) liên kết PhongBan_Id
SELECT DISTINCT
    RTRIM(LTRIM(pb.TenPhongBan)) AS [TenKhoaPhong],
    RTRIM(LTRIM(kd.MaKho)) AS [MaKho],
    RTRIM(LTRIM(kd.TenKho)) AS [TenKho]
FROM dm_khoduoc kd WITH (NOLOCK)
INNER JOIN DM_PhongBan pb WITH (NOLOCK)
    ON kd.PhongBan_Id = pb.PhongBan_Id
WHERE ISNULL(kd.TamNgung, 0) = 0
  AND ISNULL(pb.TamNgung, 0) = 0
  AND kd.MaKho IS NOT NULL
  AND LEN(RTRIM(LTRIM(kd.MaKho))) > 0

UNION

-- 2. Kết hợp ánh xạ từ Dược theo nơi sử dụng (DM_Duoc_TheoNoiSuDung) nếu có
SELECT DISTINCT
    RTRIM(LTRIM(pb.TenPhongBan)) AS [TenKhoaPhong],
    RTRIM(LTRIM(kd.MaKho)) AS [MaKho],
    RTRIM(LTRIM(kd.TenKho)) AS [TenKho]
FROM DM_Duoc_TheoNoiSuDung dsd WITH (NOLOCK)
INNER JOIN DM_PhongBan pb WITH (NOLOCK)
    ON dsd.PhongBan_Id = pb.PhongBan_Id
INNER JOIN dm_khoduoc kd WITH (NOLOCK)
    ON dsd.MaNoiSuDung = kd.MaKho OR kd.PhongBan_Id = pb.PhongBan_Id
WHERE ISNULL(kd.TamNgung, 0) = 0
  AND ISNULL(pb.TamNgung, 0) = 0
  AND kd.MaKho IS NOT NULL
ORDER BY [TenKhoaPhong], [MaKho];
