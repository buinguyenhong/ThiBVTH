/*
    XUẤT DANH MỤC THUỐC/VẬT TƯ ĐANG HOẠT ĐỘNG VÀ CÒN TỒN TẠI KHO
    -------------------------------------------------------------------------
    Script chỉ đọc dữ liệu, không INSERT/UPDATE/DELETE.

    Kết quả trả đúng thứ tự cột của data/catalogs/drugs.xlsx:
      MaDuoc, TenDuoc, DVTTinh, MaKho, TenKho, KhoaPhong,
      Nguon, SoLuongTon, TrangThai, ThoiDiemChotTon

    SoLuongTon là số dư hiện hành được đọc từ dbo.DuocTonKho tại thời điểm
    chạy script. ThoiDiemChotTon giúp ứng dụng hiển thị rõ thời điểm chốt dữ
    liệu; file Excel không tự cập nhật sau khi xuất.

    Cách dùng:
      1. Chạy script trên SQL Server của HIS.
      2. Trong SSMS, lưu lưới kết quả thành CSV hoặc sao chép vào Excel.
      3. Giữ nguyên tên và thứ tự 10 cột, lưu thành drugs.xlsx.
      4. Tải drugs.xlsx lên ứng dụng và bấm "Làm mới từ máy chủ".
*/

USE [eHospital_ThienHanh];
GO

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

DECLARE @ThoiDiemChotTon datetime2(0) = SYSDATETIME();

;WITH TonTheoKho AS
(
    SELECT
        ton.Duoc_Id,
        ton.KhoDuoc_Id,
        ton.NguonNhapHang_Id,
        SoLuongTon = SUM(ISNULL(ton.SoLuong, 0))
    FROM dbo.DuocTonKho AS ton
    GROUP BY
        ton.Duoc_Id,
        ton.KhoDuoc_Id,
        ton.NguonNhapHang_Id
    HAVING SUM(ISNULL(ton.SoLuong, 0)) > 0
)
SELECT
    MaDuoc = LTRIM(RTRIM(duoc.MaDuoc)),
    TenDuoc = COALESCE(
        NULLIF(LTRIM(RTRIM(duoc.TenDuocDayDu)), N''),
        NULLIF(
            LTRIM(RTRIM(
                ISNULL(ten.TenDuoc, N'')
                + CASE
                    WHEN NULLIF(LTRIM(RTRIM(duoc.HamLuong)), N'') IS NULL
                        THEN N''
                    ELSE N' ' + LTRIM(RTRIM(duoc.HamLuong))
                  END
            )),
            N''
        ),
        NULLIF(LTRIM(RTRIM(duoc.TenHang)), N''),
        LTRIM(RTRIM(duoc.MaDuoc))
    ),
    DVTTinh = COALESCE(
        NULLIF(LTRIM(RTRIM(duoc.DonViTinh)), N''),
        NULLIF(LTRIM(RTRIM(dvt.TenDonViTinh)), N''),
        N''
    ),
    MaKho = LTRIM(RTRIM(kho.MaKho)),
    TenKho = COALESCE(
        NULLIF(LTRIM(RTRIM(kho.TenKho)), N''),
        LTRIM(RTRIM(kho.MaKho))
    ),
    KhoaPhong = COALESCE(
        NULLIF(LTRIM(RTRIM(phong.TenPhongBan)), N''),
        N''
    ),
    Nguon = CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(nguon.MaNguonDuoc, ''))))
                 IN ('BH', 'BHYT')
          OR UPPER(ISNULL(nguon.MaNguonDuoc, '')) LIKE '%BHYT%'
          OR UPPER(ISNULL(nguon.TenKhongDau, '')) LIKE '%BAO HIEM%'
          OR UPPER(ISNULL(nguon.TenNguonDuoc, N'')) LIKE N'%BẢO HIỂM%'
          OR ISNULL(duoc.BHYT, 0) = 1
            THEN 'BH'
        ELSE 'VP'
    END,
    SoLuongTon = CAST(ton.SoLuongTon AS decimal(18, 3)),
    TrangThai = CAST(1 AS bit),
    ThoiDiemChotTon = @ThoiDiemChotTon
FROM TonTheoKho AS ton
JOIN dbo.DM_Duoc AS duoc
  ON duoc.Duoc_Id = ton.Duoc_Id
JOIN dbo.DM_KhoDuoc AS kho
  ON kho.KhoDuoc_Id = ton.KhoDuoc_Id
LEFT JOIN dbo.DM_PhongBan AS phong
  ON phong.PhongBan_Id = kho.PhongBan_Id
LEFT JOIN dbo.DM_TenDuoc AS ten
  ON ten.TenDuoc_Id = duoc.TenDuoc_Id
LEFT JOIN dbo.DM_DonViTinh AS dvt
  ON dvt.DonViTinh_Id = duoc.DonViTinh_Id
LEFT JOIN dbo.DM_NguonHang AS nguon
  ON nguon.NguonDuoc_Id = ton.NguonNhapHang_Id
WHERE ISNULL(duoc.TamNgung, 0) = 0
  AND ISNULL(duoc.TamNgungDuTru, 0) = 0
  AND ISNULL(kho.TamNgung, 0) = 0
  AND NULLIF(LTRIM(RTRIM(duoc.MaDuoc)), N'') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(kho.MaKho)), '') IS NOT NULL
ORDER BY
    KhoaPhong,
    kho.MaKho,
    duoc.MaDuoc,
    Nguon;
GO
