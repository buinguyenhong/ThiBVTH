/*
    XUẤT 1.000 BỆNH NHÂN VIỆN PHÍ VÀ BHYT DÙNG TẠO ĐỀ
    -------------------------------------------------------------------------
    Script chỉ đọc dữ liệu, không INSERT/UPDATE/DELETE.

    Mặc định lấy tối đa 500 bệnh nhân BHYT còn hạn và 500 bệnh nhân viện phí.
    Kết quả trả đúng thứ tự cột của data/catalogs/patients.xlsx:
      MaYTe, TenBenhNhan, NgaySinh, GioiTinh, DiaChiLienHe,
      SoBHYT, BHYTTuNgay, BHYTDenNgay, DKKCB

    Cách dùng:
      1. Chạy script trên SQL Server của HIS.
      2. Trong SSMS, lưu lưới kết quả thành CSV hoặc sao chép vào Excel.
      3. Giữ nguyên tên và thứ tự 9 cột, lưu thành patients.xlsx.
      4. Tải patients.xlsx lên ứng dụng và bấm "Làm mới từ máy chủ".

    Có thể đổi @TongSoBenhNhan nhưng nên giữ số chẵn để chia đều hai nhóm.
*/

USE [eHospital_ThienHanh];
GO

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

DECLARE @TongSoBenhNhan int = 1000;
DECLARE @SoLuongBHYT int = @TongSoBenhNhan / 2;
DECLARE @SoLuongVienPhi int = @TongSoBenhNhan - @SoLuongBHYT;

;WITH BenhNhanBHYT AS
(
    SELECT TOP (@SoLuongBHYT)
        bn.BenhNhan_Id,
        bn.MaYTe,
        bn.TenBenhNhan,
        bn.NgaySinh,
        bn.GioiTinh,
        DiaChiLienHe = COALESCE(
            NULLIF(LTRIM(RTRIM(bn.DiaChiLienLac)), N''),
            NULLIF(LTRIM(RTRIM(bn.DiaChiThuongTru)), N''),
            NULLIF(LTRIM(RTRIM(bn.DiaChi)), N''),
            NULLIF(LTRIM(RTRIM(bn.SoNha)), N''),
            N''
        ),
        SoBHYT = bhyt.SoThe,
        BHYTTuNgay = bhyt.NgayHieuLuc,
        BHYTDenNgay = bhyt.NgayHetHieuLuc,
        DKKCB = COALESCE(
            NULLIF(LTRIM(RTRIM(bv.MaBenhVien)), ''),
            NULLIF(LTRIM(RTRIM(bv.TenBenhVien_En)), ''),
            ''
        ),
        LoaiHoSo = CAST('BHYT' AS varchar(10))
    FROM dbo.DM_BenhNhan AS bn
    CROSS APPLY
    (
        SELECT TOP (1)
            the.SoThe,
            the.NgayHieuLuc,
            the.NgayHetHieuLuc,
            the.BenhVien_KCB_Id
        FROM dbo.DM_BenhNhan_BHYT AS the
        WHERE the.BenhNhan_Id = bn.BenhNhan_Id
          AND ISNULL(the.TamNgung, 0) = 0
          AND NULLIF(LTRIM(RTRIM(the.SoThe)), '') IS NOT NULL
          AND LEN(LTRIM(RTRIM(the.SoThe))) = 15
          AND the.NgayHieuLuc IS NOT NULL
          AND the.NgayHetHieuLuc >= CAST(GETDATE() AS date)
        ORDER BY
            the.NgayHetHieuLuc DESC,
            the.BenhNhan_BHYT_Id DESC
    ) AS bhyt
    LEFT JOIN dbo.DM_BenhVien AS bv
      ON bv.BenhVien_Id = bhyt.BenhVien_KCB_Id
     AND ISNULL(bv.TamNgung, 0) = 0
    WHERE ISNULL(bn.TamNgung, 0) = 0
      AND NULLIF(LTRIM(RTRIM(bn.MaYTe)), '') IS NOT NULL
      AND NULLIF(LTRIM(RTRIM(bn.TenBenhNhan)), N'') IS NOT NULL
      AND bn.NgaySinh IS NOT NULL
      AND NULLIF(
            COALESCE(
                NULLIF(LTRIM(RTRIM(bv.MaBenhVien)), ''),
                NULLIF(LTRIM(RTRIM(bv.TenBenhVien_En)), ''),
                ''
            ),
            ''
          ) IS NOT NULL
    ORDER BY bn.BenhNhan_Id DESC
),
BenhNhanVienPhi AS
(
    SELECT TOP (@SoLuongVienPhi)
        bn.BenhNhan_Id,
        bn.MaYTe,
        bn.TenBenhNhan,
        bn.NgaySinh,
        bn.GioiTinh,
        DiaChiLienHe = COALESCE(
            NULLIF(LTRIM(RTRIM(tn.DiaChiLienHe)), N''),
            NULLIF(LTRIM(RTRIM(bn.DiaChiLienLac)), N''),
            NULLIF(LTRIM(RTRIM(bn.DiaChiThuongTru)), N''),
            NULLIF(LTRIM(RTRIM(bn.DiaChi)), N''),
            NULLIF(LTRIM(RTRIM(bn.SoNha)), N''),
            N''
        ),
        SoBHYT = CAST('' AS varchar(30)),
        BHYTTuNgay = CAST(NULL AS smalldatetime),
        BHYTDenNgay = CAST(NULL AS smalldatetime),
        DKKCB = CAST('' AS varchar(20)),
        LoaiHoSo = CAST('VIEN_PHI' AS varchar(10))
    FROM dbo.DM_BenhNhan AS bn
    CROSS APPLY
    (
        SELECT TOP (1)
            tiepNhan.DiaChiLienHe,
            tiepNhan.ThoiGianTiepNhan
        FROM dbo.TiepNhan AS tiepNhan
        JOIN dbo.DM_DoiTuong AS doiTuong
          ON doiTuong.DoiTuong_Id = tiepNhan.DoiTuong_Id
        WHERE tiepNhan.BenhNhan_Id = bn.BenhNhan_Id
          AND doiTuong.MaDoiTuong = 'VP'
          AND ISNULL(doiTuong.TamNgung, 0) = 0
        ORDER BY
            tiepNhan.ThoiGianTiepNhan DESC,
            tiepNhan.TiepNhan_Id DESC
    ) AS tn
    WHERE ISNULL(bn.TamNgung, 0) = 0
      AND NULLIF(LTRIM(RTRIM(bn.MaYTe)), '') IS NOT NULL
      AND NULLIF(LTRIM(RTRIM(bn.TenBenhNhan)), N'') IS NOT NULL
      AND bn.NgaySinh IS NOT NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.DM_BenhNhan_BHYT AS the
          WHERE the.BenhNhan_Id = bn.BenhNhan_Id
            AND ISNULL(the.TamNgung, 0) = 0
            AND the.NgayHetHieuLuc >= CAST(GETDATE() AS date)
      )
    ORDER BY
        tn.ThoiGianTiepNhan DESC,
        bn.BenhNhan_Id DESC
),
DanhSach AS
(
    SELECT * FROM BenhNhanBHYT
    UNION ALL
    SELECT * FROM BenhNhanVienPhi
)
SELECT
    MaYTe = LTRIM(RTRIM(MaYTe)),
    TenBenhNhan = LTRIM(RTRIM(TenBenhNhan)),
    NgaySinh = CONVERT(char(10), NgaySinh, 23),
    GioiTinh = CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(GioiTinh, '')))) IN ('T', 'M', 'NAM', '1')
            THEN N'Nam'
        WHEN UPPER(LTRIM(RTRIM(ISNULL(GioiTinh, '')))) IN ('G', 'F', N'NỮ', '0')
            THEN N'Nữ'
        ELSE N'Khác'
    END,
    DiaChiLienHe,
    SoBHYT = LTRIM(RTRIM(SoBHYT)),
    BHYTTuNgay = CASE
        WHEN BHYTTuNgay IS NULL THEN ''
        ELSE CONVERT(char(10), BHYTTuNgay, 23)
    END,
    BHYTDenNgay = CASE
        WHEN BHYTDenNgay IS NULL THEN ''
        ELSE CONVERT(char(10), BHYTDenNgay, 23)
    END,
    DKKCB = LTRIM(RTRIM(DKKCB))
FROM DanhSach
ORDER BY
    LoaiHoSo,
    MaYTe;
GO
