/*
    TRA CỨU CẤU HÌNH TIẾP NHẬN VÀ CHỈ ĐỊNH VÀO KHOA
    -------------------------------------------------------------------------
    Script chỉ đọc dữ liệu, không INSERT/UPDATE/DELETE.

    Dùng khi kịch bản nạp dữ liệu thi không tự phân giải được một ID:
      1. Điền @TenDangNhap, @TenKhoa, @TenTinhThanh, @MaDKKCB và @MaICD.
      2. Chạy toàn bộ script.
      3. Kiểm tra mỗi nhóm kết quả có đúng một bản ghi phù hợp.
*/

USE [eHospital_ThienHanh];
GO

SET NOCOUNT ON;

DECLARE @TenDangNhap nvarchar(100) = N'LYCTK';
DECLARE @TenKhoa nvarchar(200) = N'Khoa Nội';
DECLARE @TenTinhThanh nvarchar(150) = N'Đắk Lắk';
DECLARE @MaDKKCB varchar(20) = '66232';
DECLARE @MaICD varchar(20) = 'R69';

/* 1. User đăng nhập và nhân viên được ánh xạ. */
SELECT
    N'USER_DANG_NHAP' AS Nhom,
    u.User_Id,
    u.User_Code,
    u.User_Name,
    u.Suspend,
    map.NhanVien_Id,
    nv.MaNhanVien,
    nv.TenNhanVien,
    nv.PhongBan_Id
FROM dbo.Sys_Users AS u
LEFT JOIN dbo.NhanVien_User_Mapping AS map
  ON map.User_Id = u.User_Id
LEFT JOIN dbo.NhanVien AS nv
  ON nv.NhanVien_Id = map.NhanVien_Id
WHERE UPPER(LTRIM(RTRIM(u.User_Code))) =
          UPPER(LTRIM(RTRIM(@TenDangNhap)))
   OR UPPER(LTRIM(RTRIM(u.User_Name))) =
          UPPER(LTRIM(RTRIM(@TenDangNhap)))
ORDER BY u.User_Id, map.NhanVien_Id;

/* 2. Khoa/phòng nội trú đích. */
SELECT
    N'KHOA_NOI_TRU' AS Nhom,
    pb.PhongBan_Id,
    pb.MaPhongBan,
    pb.TenPhongBan,
    pb.TenKhongDau,
    loai.Dictionary_Code AS LoaiPhongBan_Code,
    loai.Dictionary_Name AS LoaiPhongBan,
    pb.TamNgung
FROM dbo.DM_PhongBan AS pb
LEFT JOIN dbo.Lst_Dictionary AS loai
  ON loai.Dictionary_Id = pb.LoaiPhongBan_Id
WHERE pb.TenPhongBan LIKE N'%' + REPLACE(@TenKhoa, N'Khoa ', N'') + N'%'
   OR ISNULL(pb.TenKhongDau, N'') LIKE
      N'%' + REPLACE(@TenKhoa, N'Khoa ', N'') + N'%'
ORDER BY pb.TamNgung, pb.PhongBan_Id;

/* 3. Bác sĩ có thể dùng làm bác sĩ chỉ định. */
SELECT
    N'BAC_SI_CHI_DINH' AS Nhom,
    nv.NhanVien_Id,
    nv.MaNhanVien,
    nv.TenNhanVien,
    nv.PhongBan_Id,
    pb.TenPhongBan,
    cd.Dictionary_Code AS ChucDanh_Code,
    cd.Dictionary_Name AS ChucDanh,
    nv.TamNgung
FROM dbo.NhanVien AS nv
LEFT JOIN dbo.DM_PhongBan AS pb
  ON pb.PhongBan_Id = nv.PhongBan_Id
LEFT JOIN dbo.Lst_Dictionary AS cd
  ON cd.Dictionary_Id = nv.ChucDanh_Id
 AND cd.Dictionary_Type_Code = 'ChucDanh'
WHERE ISNULL(nv.TamNgung, 0) = 0
  AND (
      pb.TenPhongBan LIKE N'%' + REPLACE(@TenKhoa, N'Khoa ', N'') + N'%'
      OR cd.Dictionary_Code LIKE 'BS%'
      OR cd.Dictionary_Name LIKE N'%bác sĩ%'
  )
ORDER BY
    CASE WHEN pb.TenPhongBan LIKE
                   N'%' + REPLACE(@TenKhoa, N'Khoa ', N'') + N'%'
         THEN 0 ELSE 1 END,
    nv.NhanVien_Id;

/* 4. Các từ điển bắt buộc của tiếp nhận/nhập viện. */
SELECT
    N'TU_DIEN_NGHIEP_VU' AS Nhom,
    d.Dictionary_Type_Code,
    d.Dictionary_Id,
    d.Dictionary_Code,
    d.Dictionary_Name,
    d.Idx,
    d.Enabled
FROM dbo.Lst_Dictionary AS d
WHERE d.Dictionary_Type_Code IN (
    'DanToc',
    'NgheNghiep',
    'HinhThucDenKhamBenh',
    'LyDoTiepNhan',
    'LyDoNhapVien',
    'TiepNhanLoaiBHYT',
    'TuyenKhamChuaBenh',
    'DoiTuong_NoiSinhSong',
    'QuocGia'
)
  AND ISNULL(d.Enabled, 1) = 1
ORDER BY d.Dictionary_Type_Code, ISNULL(d.Idx, 2147483647), d.Dictionary_Id;

/* 5. Tỉnh/thành từ địa chỉ bệnh nhân. */
SELECT
    N'TINH_THANH' AS Nhom,
    dv.DonViHanhChinh_Id,
    dv.MaDonVi,
    dv.TenDonVi,
    dv.TenKhongDau,
    dv.CapDonVi,
    dv.TamNgung
FROM dbo.DM_DonViHanhChinh AS dv
WHERE dv.CapDonVi = 2
  AND (
      dv.TenDonVi LIKE N'%' + @TenTinhThanh + N'%'
      OR ISNULL(dv.TenKhongDau, N'') LIKE N'%' + @TenTinhThanh + N'%'
  )
ORDER BY dv.TamNgung, dv.DonViHanhChinh_Id;

/* 6. Nơi đăng ký khám chữa bệnh ban đầu. */
SELECT
    N'BENH_VIEN_DKKCB' AS Nhom,
    bv.BenhVien_Id,
    bv.MaBenhVien,
    bv.TenBenhVien,
    bv.TenBenhVien_En,
    bv.TinhThanhPho_Id,
    bv.TamNgung
FROM dbo.DM_BenhVien AS bv
WHERE LTRIM(RTRIM(ISNULL(bv.MaBenhVien, ''))) = LTRIM(RTRIM(@MaDKKCB))
   OR LTRIM(RTRIM(ISNULL(bv.TenBenhVien_En, ''))) = LTRIM(RTRIM(@MaDKKCB))
ORDER BY bv.TamNgung, bv.BenhVien_Id;

/* 7. ICD mặc định dùng cho dòng chờ nhập khoa. */
SELECT
    N'ICD_CHAN_DOAN' AS Nhom,
    icd.ICD_Id,
    icd.MaICD,
    icd.TenICD,
    icd.TamNgung
FROM dbo.DM_ICD AS icd
WHERE UPPER(LTRIM(RTRIM(icd.MaICD))) = UPPER(LTRIM(RTRIM(@MaICD)))
ORDER BY icd.TamNgung, icd.ICD_Id;

/* 8. Đối tượng viện phí và ánh xạ mức hưởng BHYT hiện hành. */
SELECT
    N'DOI_TUONG' AS Nhom,
    dt.DoiTuong_Id,
    dt.MaDoiTuong,
    dt.TenDoiTuong,
    dt.MucHuong,
    dt.TraiTuyen,
    dt.TamNgung
FROM dbo.DM_DoiTuong AS dt
WHERE dt.MaDoiTuong = 'VP'
   OR dt.MaDoiTuong LIKE 'P%'
ORDER BY dt.TamNgung, dt.MaDoiTuong, dt.DoiTuong_Id;
