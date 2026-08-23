/*
    TẠO BỆNH NHÂN MỚI, TIẾP NHẬN TRỰC TIẾP VÀ CHỈ ĐỊNH VÀO KHOA
    -------------------------------------------------------------------------
    Hỗ trợ:
      @LoaiHoSo = 'VIEN_PHI' : bệnh nhân không có BHYT.
      @LoaiHoSo = 'BHYT'     : bệnh nhân có đầy đủ thông tin BHYT bắt buộc.

    Phạm vi:
      - Tạo DM_BenhNhan.
      - Nếu là BHYT, tạo DM_BenhNhan_BHYT.
      - Tạo TiepNhan.
      - Tạo NoiTru_NhapVien bằng action AddNew.
      - KHÔNG gọi AddNewAndCreateBenhAn.
      - KHÔNG tạo BenhAn, NoiTru_LuuTru hoặc số bệnh án.

    An toàn:
      @Commit = 0 là mặc định: chạy toàn bộ rồi ROLLBACK.
      Chỉ đặt @Commit = 1 sau khi đã kiểm tra kết quả chạy thử.

    Cách dùng:
      1. Chạy Scripts/01_lookup_tiep_nhan_nhap_vien.sql để lấy ID.
      2. Điền phần "THAM SỐ CẦN ĐIỀN" bên dưới.
      3. Chạy lần đầu với @Commit = 0.
      4. Kiểm tra BenhAn_Id phải luôn là NULL.
*/

USE [eHospital_ThienHanh];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* =========================================================================
   1. THAM SỐ CẦN ĐIỀN
   ========================================================================= */

DECLARE @LoaiHoSo varchar(20) = 'VIEN_PHI'; -- VIEN_PHI | BHYT
DECLARE @Commit bit = 0;                    -- 0 = ROLLBACK | 1 = COMMIT
DECLARE @NgayGioNghiepVu datetime = GETDATE();

/* Người thực hiện và nơi tiếp nhận. */
DECLARE @User_Id int = NULL;                -- User đăng nhập, không phải NhanVien_Id
DECLARE @NoiTiepNhan_Id int = NULL;         -- DM_PhongBan.PhongBan_Id
DECLARE @HinhThucDenKham_Id int = NULL;     -- Lst_Dictionary/HinhThucDenKhamBenh
DECLARE @NoiGioiThieu_Id int = NULL;        -- bắt buộc nếu hình thức là GioiThieu_*
DECLARE @LyDoTiepNhan_Id int = NULL;        -- để NULL: tự lấy LyDoTiepNhan code 3

/* Bệnh nhân mới: các trường bắt buộc trên màn hình tiếp nhận. */
DECLARE @TenBenhNhan nvarchar(40) = N'TEST TIEP NHAN VAO KHOA';
DECLARE @GioiTinh char(1) = 'T';            -- T = nam | G = nữ
DECLARE @NgaySinh smalldatetime = '1990-01-01';
DECLARE @DanToc_Id int = NULL;               -- Lst_Dictionary/DanToc
DECLARE @QuocTich_Id int = NULL;             -- để NULL: tự lấy QuocGia/VN
DECLARE @NgheNghiep_Id int = NULL;           -- danh mục nghề nghiệp
DECLARE @TinhThanh_Id int = NULL;            -- DM_DonViHanhChinh cấp tỉnh

/* Địa chỉ chi tiết không bắt buộc; điền nếu cần. */
DECLARE @QuanHuyen_Id int = NULL;
DECLARE @XaPhuong_Id int = NULL;
DECLARE @SoNha nvarchar(150) = NULL;
DECLARE @DiaChiThuongTru nvarchar(150) = NULL;
DECLARE @SoDienThoai varchar(30) = NULL;

/* Chỉ định vào khoa, nhưng chưa nhận vào khoa và chưa tạo bệnh án. */
DECLARE @NoiNhapVien_Id int = NULL;          -- để NULL: dùng @NoiTiepNhan_Id
DECLARE @KhoaDieuTri_Id int = NULL;          -- khoa nội trú đích
DECLARE @BacSiChiDinh_Id int = NULL;         -- NhanVien.NhanVien_Id
DECLARE @LyDoNhapVien_Id int = NULL;         -- Lst_Dictionary/LyDoNhapVien
DECLARE @ICD_Id int = NULL;                  -- DM_ICD.ICD_Id
DECLARE @ChanDoan nvarchar(200) = N'TEST CHỈ ĐỊNH VÀO KHOA';
DECLARE @CapCuu bit = 0;

/*
    Chỉ điền khối này khi @LoaiHoSo = 'BHYT'.
    Số thẻ dùng định dạng hiện hành 15 ký tự, không có dấu cách/gạch nối.
*/
DECLARE @SoBHYT varchar(30) = NULL;
DECLARE @LoaiBHYT int = NULL;                -- Lst_Dictionary/TiepNhanLoaiBHYT
DECLARE @BHYTTuNgay smalldatetime = NULL;
DECLARE @BHYTDenNgay smalldatetime = NULL;
DECLARE @TuyenKhamBenh_Id int = NULL;        -- Lst_Dictionary/TuyenKhamChuaBenh
DECLARE @BenhVien_KCB_Id int = NULL;          -- DM_BenhVien.BenhVien_Id

/* Các trường BHYT sau không bắt buộc trên màn hình; điền khi thẻ có dữ liệu. */
DECLARE @TinhThanh_CapThe_Id int = NULL;
DECLARE @NoiSinhSong_Id int = NULL;          -- Lst_Dictionary/DoiTuong_NoiSinhSong
DECLARE @NgayMienDongChiTra smalldatetime = NULL;

/* =========================================================================
   2. BIẾN NỘI BỘ
   ========================================================================= */

DECLARE @FreePara nvarchar(1000) = NULL;
DECLARE @BenhNhan_Id int = NULL;
DECLARE @BenhNhan_BHYT_Id int = NULL;
DECLARE @TiepNhan_Id int = NULL;
DECLARE @NhapVien_Id int = NULL;
DECLARE @BenhAn_Id int = NULL;

DECLARE @DoiTuong_Id int = NULL;
DECLARE @MaDoiTuong nvarchar(1000) = NULL;
DECLARE @SoTiepNhan nvarchar(20) = NULL;
DECLARE @SoThuTu nvarchar(20) = NULL;
DECLARE @MaNoiNhapVien nvarchar(50) = NULL;
DECLARE @HinhThucDenKham_Code nvarchar(100) = NULL;
DECLARE @NamSinh smallint = YEAR(@NgaySinh);
DECLARE @NamTiepNhan smallint = YEAR(@NgayGioNghiepVu);
DECLARE @ThangTiepNhan tinyint = MONTH(@NgayGioNghiepVu);
DECLARE @ErrorMessage nvarchar(2048);

/* Giá trị snapshot BHYT truyền vào TiepNhan. */
DECLARE @TN_SoBHYT varchar(30) = NULL;
DECLARE @TN_LoaiBHYT int = 0;
DECLARE @TN_BHYTTuNgay smalldatetime = NULL;
DECLARE @TN_BHYTDenNgay smalldatetime = NULL;
DECLARE @TN_TuyenKhamBenh_Id int = 0;
DECLARE @TN_BenhVien_KCB_Id int = NULL;
DECLARE @TN_NoiSinhSong_Id int = NULL;
DECLARE @TN_NgayMienDongChiTra smalldatetime = NULL;

SET @LoaiHoSo = UPPER(LTRIM(RTRIM(@LoaiHoSo)));
SET @GioiTinh = UPPER(LTRIM(RTRIM(@GioiTinh)));
SET @NoiNhapVien_Id = ISNULL(@NoiNhapVien_Id, @NoiTiepNhan_Id);

/* =========================================================================
   3. KIỂM TRA TRƯỚC KHI GHI DỮ LIỆU
   ========================================================================= */

IF @@TRANCOUNT <> 0
    THROW 51000, N'Script phải được chạy khi không có transaction đang mở.', 1;

IF ISNULL(@LoaiHoSo, '') NOT IN ('VIEN_PHI', 'BHYT')
    THROW 51000, N'@LoaiHoSo chỉ nhận VIEN_PHI hoặc BHYT.', 1;

IF @User_Id IS NULL
    THROW 51000, N'Chưa điền @User_Id.', 1;

IF NULLIF(LTRIM(RTRIM(@TenBenhNhan)), N'') IS NULL
    THROW 51000, N'Chưa điền @TenBenhNhan.', 1;

IF ISNULL(@GioiTinh, '') NOT IN ('T', 'G')
    THROW 51000, N'@GioiTinh chỉ nhận T (nam) hoặc G (nữ).', 1;

IF @NgaySinh IS NULL OR @NgaySinh >= @NgayGioNghiepVu
    THROW 51000, N'@NgaySinh phải nhỏ hơn thời gian tiếp nhận.', 1;

IF @DanToc_Id IS NULL OR @NgheNghiep_Id IS NULL OR @TinhThanh_Id IS NULL
    THROW 51000, N'Chưa đủ @DanToc_Id, @NgheNghiep_Id và @TinhThanh_Id.', 1;

IF @NoiTiepNhan_Id IS NULL OR @HinhThucDenKham_Id IS NULL
    THROW 51000, N'Chưa đủ @NoiTiepNhan_Id và @HinhThucDenKham_Id.', 1;

IF @KhoaDieuTri_Id IS NULL OR @BacSiChiDinh_Id IS NULL
   OR @LyDoNhapVien_Id IS NULL OR @ICD_Id IS NULL
    THROW 51000, N'Chưa đủ khoa, bác sĩ, lý do nhập viện và ICD.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.DM_PhongBan
    WHERE PhongBan_Id = @NoiTiepNhan_Id
      AND ISNULL(TamNgung, 0) = 0
)
    THROW 51000, N'@NoiTiepNhan_Id không tồn tại hoặc đang tạm ngưng.', 1;

SELECT @MaNoiNhapVien = MaPhongBan
FROM dbo.DM_PhongBan
WHERE PhongBan_Id = @NoiNhapVien_Id
  AND ISNULL(TamNgung, 0) = 0;

IF @MaNoiNhapVien IS NULL
    THROW 51000, N'@NoiNhapVien_Id không tồn tại hoặc đang tạm ngưng.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.DM_PhongBan AS pb
    JOIN dbo.Lst_Dictionary AS loai
      ON loai.Dictionary_Id = pb.LoaiPhongBan_Id
    WHERE pb.PhongBan_Id = @KhoaDieuTri_Id
      AND loai.Dictionary_Code = 'KhoaNoi'
      AND ISNULL(pb.TamNgung, 0) = 0
)
    THROW 51000, N'@KhoaDieuTri_Id không phải khoa nội trú hoạt động.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.NhanVien
    WHERE NhanVien_Id = @BacSiChiDinh_Id
      AND ISNULL(TamNgung, 0) = 0
)
    THROW 51000, N'@BacSiChiDinh_Id không tồn tại hoặc đang tạm ngưng.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.DM_ICD
    WHERE ICD_Id = @ICD_Id
      AND ISNULL(TamNgung, 0) = 0
)
    THROW 51000, N'@ICD_Id không tồn tại hoặc đang tạm ngưng.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.Lst_Dictionary
    WHERE Dictionary_Id = @DanToc_Id
      AND Dictionary_Type_Code = 'DanToc'
      AND ISNULL(Enabled, 1) = 1
)
    THROW 51000, N'@DanToc_Id không thuộc danh mục DanToc đang hiệu lực.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.Lst_Dictionary
    WHERE Dictionary_Id = @NgheNghiep_Id
      AND Dictionary_Type_Code = 'NgheNghiep'
      AND ISNULL(Enabled, 1) = 1
)
    THROW 51000, N'@NgheNghiep_Id không thuộc danh mục NgheNghiep đang hiệu lực.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.DM_DonViHanhChinh
    WHERE DonViHanhChinh_Id = @TinhThanh_Id
      AND CapDonVi = 2
      AND ISNULL(TamNgung, 0) = 0
)
    THROW 51000, N'@TinhThanh_Id không phải đơn vị hành chính cấp tỉnh đang hiệu lực.', 1;

IF @QuanHuyen_Id IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM dbo.DM_DonViHanhChinh
       WHERE DonViHanhChinh_Id = @QuanHuyen_Id
         AND CapTren_Id = @TinhThanh_Id
         AND ISNULL(TamNgung, 0) = 0
   )
    THROW 51000, N'@QuanHuyen_Id không thuộc @TinhThanh_Id.', 1;

IF @XaPhuong_Id IS NOT NULL AND @QuanHuyen_Id IS NULL
    THROW 51000, N'Có @XaPhuong_Id nhưng chưa điền @QuanHuyen_Id.', 1;

IF @XaPhuong_Id IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM dbo.DM_DonViHanhChinh
       WHERE DonViHanhChinh_Id = @XaPhuong_Id
         AND CapTren_Id = @QuanHuyen_Id
         AND ISNULL(TamNgung, 0) = 0
   )
    THROW 51000, N'@XaPhuong_Id không thuộc @QuanHuyen_Id.', 1;

IF @QuocTich_Id IS NULL
BEGIN
    SELECT TOP (1) @QuocTich_Id = Dictionary_Id
    FROM dbo.Lst_Dictionary
    WHERE Dictionary_Type_Code = 'QuocGia'
      AND Dictionary_Code = 'VN'
      AND ISNULL(Enabled, 1) = 1
    ORDER BY Dictionary_Id;
END;

IF @QuocTich_Id IS NULL
   OR NOT EXISTS (
       SELECT 1
       FROM dbo.Lst_Dictionary
       WHERE Dictionary_Id = @QuocTich_Id
         AND Dictionary_Type_Code = 'QuocGia'
         AND ISNULL(Enabled, 1) = 1
   )
    THROW 51000, N'@QuocTich_Id không hợp lệ và không tự tìm được quốc gia VN.', 1;

SELECT @HinhThucDenKham_Code = Dictionary_Code
FROM dbo.Lst_Dictionary
WHERE Dictionary_Id = @HinhThucDenKham_Id
  AND Dictionary_Type_Code = 'HinhThucDenKhamBenh'
  AND ISNULL(Enabled, 1) = 1;

IF @HinhThucDenKham_Code IS NULL
    THROW 51000, N'@HinhThucDenKham_Id không thuộc HinhThucDenKhamBenh.', 1;

IF @HinhThucDenKham_Code LIKE 'GioiThieu[_]%'
   AND @NoiGioiThieu_Id IS NULL
    THROW 51000, N'Hình thức đến khám là giới thiệu nhưng thiếu @NoiGioiThieu_Id.', 1;

IF @LyDoTiepNhan_Id IS NULL
BEGIN
    SELECT TOP (1) @LyDoTiepNhan_Id = Dictionary_Id
    FROM dbo.Lst_Dictionary
    WHERE Dictionary_Type_Code = 'LyDoTiepNhan'
      AND Dictionary_Code = '3'
      AND ISNULL(Enabled, 1) = 1
    ORDER BY Dictionary_Id;
END;

IF @LyDoTiepNhan_Id IS NULL
   OR NOT EXISTS (
       SELECT 1
       FROM dbo.Lst_Dictionary
       WHERE Dictionary_Id = @LyDoTiepNhan_Id
         AND Dictionary_Type_Code = 'LyDoTiepNhan'
         AND ISNULL(Enabled, 1) = 1
   )
    THROW 51000, N'Không tìm thấy lý do tiếp nhận trực tiếp hợp lệ.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.Lst_Dictionary
    WHERE Dictionary_Id = @LyDoNhapVien_Id
      AND Dictionary_Type_Code = 'LyDoNhapVien'
      AND ISNULL(Enabled, 1) = 1
)
    THROW 51000, N'@LyDoNhapVien_Id không thuộc LyDoNhapVien.', 1;

/* Xác định đối tượng viện phí hoặc BHYT. */
IF @LoaiHoSo = 'VIEN_PHI'
BEGIN
    SELECT TOP (1) @DoiTuong_Id = DoiTuong_Id
    FROM dbo.DM_DoiTuong
    WHERE MaDoiTuong = 'VP'
      AND ISNULL(TamNgung, 0) = 0
    ORDER BY DoiTuong_Id;

    IF @DoiTuong_Id IS NULL
        THROW 51000, N'Không tìm thấy đối tượng viện phí MaDoiTuong=VP.', 1;
END
ELSE
BEGIN
    IF NULLIF(LTRIM(RTRIM(@SoBHYT)), '') IS NULL
       OR LEN(LTRIM(RTRIM(@SoBHYT))) <> 15
       OR LTRIM(RTRIM(@SoBHYT)) LIKE '% %'
       OR LTRIM(RTRIM(@SoBHYT)) LIKE '%-%'
        THROW 51000, N'@SoBHYT phải có đúng 15 ký tự, không có khoảng trắng/gạch nối.', 1;

    SET @SoBHYT = LTRIM(RTRIM(@SoBHYT));

    IF @LoaiBHYT IS NULL OR @BHYTTuNgay IS NULL OR @BHYTDenNgay IS NULL
       OR @TuyenKhamBenh_Id IS NULL OR @BenhVien_KCB_Id IS NULL
        THROW 51000, N'BHYT cần loại, hạn thẻ, tuyến và nơi đăng ký KCB.', 1;

    IF @BHYTTuNgay >= @BHYTDenNgay
        THROW 51000, N'Ngày hết hạn BHYT phải sau ngày hiệu lực.', 1;

    IF CAST(@NgayGioNghiepVu AS date) < CAST(@BHYTTuNgay AS date)
       OR CAST(@NgayGioNghiepVu AS date) > CAST(@BHYTDenNgay AS date)
        THROW 51000, N'Thẻ BHYT không có hiệu lực tại ngày tiếp nhận.', 1;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.Lst_Dictionary
        WHERE Dictionary_Id = @LoaiBHYT
          AND Dictionary_Type_Code = 'TiepNhanLoaiBHYT'
          AND ISNULL(Enabled, 1) = 1
    )
        THROW 51000, N'@LoaiBHYT không thuộc TiepNhanLoaiBHYT.', 1;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.Lst_Dictionary
        WHERE Dictionary_Id = @TuyenKhamBenh_Id
          AND Dictionary_Type_Code = 'TuyenKhamChuaBenh'
          AND ISNULL(Enabled, 1) = 1
    )
        THROW 51000, N'@TuyenKhamBenh_Id không thuộc TuyenKhamChuaBenh.', 1;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.DM_BenhVien
        WHERE BenhVien_Id = @BenhVien_KCB_Id
          AND ISNULL(TamNgung, 0) = 0
    )
        THROW 51000, N'@BenhVien_KCB_Id không tồn tại hoặc đang tạm ngưng.', 1;

    IF @NoiSinhSong_Id IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM dbo.Lst_Dictionary
           WHERE Dictionary_Id = @NoiSinhSong_Id
             AND Dictionary_Type_Code = 'DoiTuong_NoiSinhSong'
             AND ISNULL(Enabled, 1) = 1
       )
        THROW 51000, N'@NoiSinhSong_Id không thuộc DoiTuong_NoiSinhSong.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.DM_BenhNhan_BHYT
        WHERE SoThe = @SoBHYT
          AND ISNULL(TamNgung, 0) = 0
    )
        THROW 51000, N'Số thẻ BHYT đã thuộc một bệnh nhân đang hoạt động; không tạo bệnh nhân mới trùng thẻ.', 1;

    /*
        Màn hình tiếp nhận dùng 3 ký tự đầu thẻ + tuyến để suy ra
        MaDoiTuong (P1...P8 hoặc mã cấu hình tương ứng).
        Action này sử dụng @BenhNhan_Id để nhận TuyenKhamBenh_Id.
    */
    SET @MaDoiTuong = LEFT(@SoBHYT, 3);

    EXEC dbo.sp_TIEPNHAN
        @Action = N'GetDMDoiTuong_By_MaThe_TuyenKB',
        @LanguageID = N'VI',
        @UserID = @User_Id,
        @FreePara = @MaDoiTuong OUTPUT,
        @BenhNhan_Id = @TuyenKhamBenh_Id;

    SELECT TOP (1) @DoiTuong_Id = DoiTuong_Id
    FROM dbo.DM_DoiTuong
    WHERE MaDoiTuong = @MaDoiTuong
      AND ISNULL(TamNgung, 0) = 0
    ORDER BY DoiTuong_Id;

    IF @DoiTuong_Id IS NULL
        THROW 51000, N'Không suy ra được DM_DoiTuong từ mã thẻ BHYT và tuyến khám.', 1;

    SET @TN_SoBHYT = @SoBHYT;
    SET @TN_LoaiBHYT = @LoaiBHYT;
    SET @TN_BHYTTuNgay = @BHYTTuNgay;
    SET @TN_BHYTDenNgay = @BHYTDenNgay;
    SET @TN_TuyenKhamBenh_Id = @TuyenKhamBenh_Id;
    SET @TN_BenhVien_KCB_Id = @BenhVien_KCB_Id;
    SET @TN_NoiSinhSong_Id = @NoiSinhSong_Id;
    SET @TN_NgayMienDongChiTra = @NgayMienDongChiTra;
END;

/* =========================================================================
   4. TẠO BỆNH NHÂN, TIẾP NHẬN VÀ DÒNG CHỜ VÀO KHOA
   ========================================================================= */

BEGIN TRY
    BEGIN TRANSACTION;

    /* 4.1 Tạo bệnh nhân mới; MaYTe được procedure tự sinh. */
    SET @FreePara = N'1'; -- họ đứng trước tên theo cách lưu của màn hình

    EXEC dbo.sp_DM_BENHNHAN
        @Action = N'AddNew',
        @LanguageID = N'VI',
        @UserID = @User_Id,
        @FreePara = @FreePara OUTPUT,
        @BenhNhan_Id = @BenhNhan_Id OUTPUT,
        @MaYTe = NULL,
        @TenBenhNhan = @TenBenhNhan,
        @GioiTinh = @GioiTinh,
        @NgaySinh = @NgaySinh,
        @NamSinh = @NamSinh,
        @SoNha = @SoNha,
        @QuocTich_Id = @QuocTich_Id,
        @TinhThanh_Id = @TinhThanh_Id,
        @QuanHuyen_Id = @QuanHuyen_Id,
        @XaPhuong_Id = @XaPhuong_Id,
        @DanToc_Id = @DanToc_Id,
        @NgheNghiep_Id = @NgheNghiep_Id,
        @VietKieu = 0,
        @NguoiNuocNgoai = 0,
        @NgayTao = @NgayGioNghiepVu,
        @NguoiTao_Id = @User_Id,
        @SoDienThoai = @SoDienThoai,
        @DiaChiThuongTru = @DiaChiThuongTru,
        @TuVong = 0,
        @TamNgung = 0;

    IF @BenhNhan_Id IS NULL
        THROW 51000, N'Không tạo được DM_BenhNhan.', 1;

    /* 4.2 Lưu thẻ bệnh nhân nếu là hồ sơ BHYT. */
    IF @LoaiHoSo = 'BHYT'
    BEGIN
        SET @FreePara = NULL;

        EXEC dbo.sp_DM_BENHNHAN_BHYT
            @Action = N'AddNew',
            @LanguageID = N'VI',
            @UserID = @User_Id,
            @FreePara = @FreePara OUTPUT,
            @BenhNhan_BHYT_Id = @BenhNhan_BHYT_Id OUTPUT,
            @BenhNhan_Id = @BenhNhan_Id,
            @LoaiBHYT = @LoaiBHYT,
            @SoThe = @SoBHYT,
            @NgayCap = NULL,
            @NgayHieuLuc = @BHYTTuNgay,
            @NgayHetHieuLuc = @BHYTDenNgay,
            @TinhThanh_CapThe_Id = @TinhThanh_CapThe_Id,
            @BenhVien_KCB_Id = @BenhVien_KCB_Id,
            @TamNgung = 0,
            @NgayTao = @NgayGioNghiepVu,
            @NguoiTao_Id = @User_Id,
            @NgayCapNhat = NULL,
            @NguoiCapNhat_Id = NULL;

        IF @BenhNhan_BHYT_Id IS NULL
            THROW 51000, N'Không tạo được DM_BenhNhan_BHYT.', 1;
    END;

    /* 4.3 Sinh số tiếp nhận và số thứ tự. */
    EXEC dbo.sp_LST_KEYDATA
        @pKeyCode = @SoTiepNhan OUTPUT,
        @pKeyType = N'MaTiepNhan',
        @pGetDate = @NgayGioNghiepVu;

    EXEC dbo.sp_LST_KEYDATA
        @pKeyCode = @SoThuTu OUTPUT,
        @pKeyType = N'SoThuTuTiepNhan',
        @pGetDate = @NgayGioNghiepVu;

    IF NULLIF(@SoTiepNhan, N'') IS NULL OR NULLIF(@SoThuTu, N'') IS NULL
        THROW 51000, N'Không sinh được số tiếp nhận hoặc số thứ tự.', 1;

    /* 4.4 Tạo tiếp nhận; thông tin BHYT được snapshot trên lần tiếp nhận. */
    SET @FreePara = NULL;

    EXEC dbo.sp_TIEPNHAN
        @Action = N'AddNew',
        @LanguageID = N'VI',
        @UserID = @User_Id,
        @FreePara = @FreePara OUTPUT,
        @TiepNhan_Id = @TiepNhan_Id OUTPUT,
        @SoTiepNhan = @SoTiepNhan,
        @SoThuTu = @SoThuTu,
        @UuTien = 0,
        @BenhNhan_Id = @BenhNhan_Id,
        @NoiTiepNhan_Id = @NoiTiepNhan_Id,
        @NgayTiepNhan = @NgayGioNghiepVu,
        @ThoiGianTiepNhan = @NgayGioNghiepVu,
        @NamTiepNhan = @NamTiepNhan,
        @ThangTiepNhan = @ThangTiepNhan,
        @DoiTuong_Id = @DoiTuong_Id,
        @HinhThucDenKham_Id = @HinhThucDenKham_Id,
        @NoiGioiThieu_Id = @NoiGioiThieu_Id,
        @LyDoTiepNhan_Id = @LyDoTiepNhan_Id,
        @SoBHYT = @TN_SoBHYT,
        @BHYTTuNgay = @TN_BHYTTuNgay,
        @BHYTDenNgay = @TN_BHYTDenNgay,
        @ThuTienTruoc = 1,
        @TrangThai = NULL,
        @NgayTao = @NgayGioNghiepVu,
        @NguoiTao_Id = @User_Id,
        @TaiKham = 0,
        @TuyenKhamBenh_Id = @TN_TuyenKhamBenh_Id,
        @TinhThanh_Id = @TinhThanh_Id,
        @QuanHuyen_Id = @QuanHuyen_Id,
        @XaPhuong_Id = @XaPhuong_Id,
        @LoaiBHYT = @TN_LoaiBHYT,
        @BenhVien_KCB_Id = @TN_BenhVien_KCB_Id,
        @NoiSinhSong_Id = @TN_NoiSinhSong_Id,
        @NgayMienDongChiTra = @TN_NgayMienDongChiTra;

    IF @TiepNhan_Id IS NULL
        THROW 51000, N'Không tạo được TiepNhan.', 1;

    /*
        4.5 Chỉ tạo lệnh vào khoa.
        Cố định Action=AddNew để không thể vô tình sinh bệnh án.
    */
    SET @FreePara = @MaNoiNhapVien;

    EXEC dbo.sp_NOITRU_NHAPVIEN
        @Action = N'AddNew',
        @LanguageID = N'VI',
        @UserID = @User_Id,
        @FreePara = @FreePara OUTPUT,
        @NhapVien_Id = @NhapVien_Id OUTPUT,
        @TiepNhan_Id = @TiepNhan_Id,
        @NgayNhapVien = @NgayGioNghiepVu,
        @ThoiGianNhapVien = @NgayGioNghiepVu,
        @NoiNhapVien_Id = @NoiNhapVien_Id,
        @BacSiChiDinh_Id = @BacSiChiDinh_Id,
        @LyDoNhapVien_Id = @LyDoNhapVien_Id,
        @KhoaDieuTri_Id = @KhoaDieuTri_Id,
        @BenhAn_Id = NULL,
        @ChanDoan = @ChanDoan,
        @ICD_ChanDoan = @ICD_Id,
        @TrangThai = NULL,
        @NgayTao = @NgayGioNghiepVu,
        @NguoiTao_Id = @User_Id,
        @KhamBenh_Id = NULL,
        @CapCuu = @CapCuu;

    IF @NhapVien_Id IS NULL
        THROW 51000, N'Không tạo được NoiTru_NhapVien.', 1;

    SELECT @BenhAn_Id = BenhAn_Id
    FROM dbo.NoiTru_NhapVien
    WHERE NhapVien_Id = @NhapVien_Id;

    IF @BenhAn_Id IS NOT NULL
        THROW 51000, N'Sai phạm vi: NoiTru_NhapVien đã phát sinh BenhAn_Id.', 1;

    /* Kết quả kiểm tra trước COMMIT/ROLLBACK. */
    SELECT
        N'KET_QUA_TRUOC_KHI_KET_THUC_TRANSACTION' AS NhomKetQua,
        @LoaiHoSo AS LoaiHoSo,
        @Commit AS SeCommit,
        @BenhNhan_Id AS BenhNhan_Id,
        @BenhNhan_BHYT_Id AS BenhNhan_BHYT_Id,
        @TiepNhan_Id AS TiepNhan_Id,
        @NhapVien_Id AS NhapVien_Id,
        @BenhAn_Id AS BenhAn_Id;

    SELECT
        bn.BenhNhan_Id,
        bn.MaYTe,
        bn.TenBenhNhan,
        bn.GioiTinh,
        bn.NgaySinh,
        tn.TiepNhan_Id,
        tn.SoTiepNhan,
        tn.DoiTuong_Id,
        dt.MaDoiTuong,
        tn.SoBHYT,
        tn.BHYTTuNgay,
        tn.BHYTDenNgay,
        tn.TuyenKhamBenh_Id,
        tn.BenhVien_KCB_Id,
        nv.NhapVien_Id,
        nv.NoiNhapVien_Id,
        nv.KhoaDieuTri_Id,
        nv.BenhAn_Id,
        nv.TrangThai
    FROM dbo.DM_BenhNhan AS bn
    JOIN dbo.TiepNhan AS tn
      ON tn.BenhNhan_Id = bn.BenhNhan_Id
    JOIN dbo.DM_DoiTuong AS dt
      ON dt.DoiTuong_Id = tn.DoiTuong_Id
    JOIN dbo.NoiTru_NhapVien AS nv
      ON nv.TiepNhan_Id = tn.TiepNhan_Id
    WHERE bn.BenhNhan_Id = @BenhNhan_Id
      AND tn.TiepNhan_Id = @TiepNhan_Id
      AND nv.NhapVien_Id = @NhapVien_Id;

    IF @LoaiHoSo = 'BHYT'
    BEGIN
        SELECT
            BenhNhan_BHYT_Id,
            BenhNhan_Id,
            SoThe,
            LoaiBHYT,
            NgayHieuLuc,
            NgayHetHieuLuc,
            BenhVien_KCB_Id,
            TamNgung
        FROM dbo.DM_BenhNhan_BHYT
        WHERE BenhNhan_BHYT_Id = @BenhNhan_BHYT_Id;
    END;

    IF @Commit = 1
        COMMIT TRANSACTION;
    ELSE
        ROLLBACK TRANSACTION;

    SELECT
        CASE WHEN @Commit = 1
             THEN N'COMMIT - đã lưu bệnh nhân, tiếp nhận và dòng chờ vào khoa'
             ELSE N'ROLLBACK - chỉ chạy thử, không lưu dữ liệu'
        END AS KetQuaCuoi,
        @LoaiHoSo AS LoaiHoSo,
        @BenhNhan_Id AS BenhNhan_Id,
        @BenhNhan_BHYT_Id AS BenhNhan_BHYT_Id,
        @TiepNhan_Id AS TiepNhan_Id,
        @NhapVien_Id AS NhapVien_Id,
        @BenhAn_Id AS BenhAn_Id;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    SET @ErrorMessage = CONCAT(
        N'Lỗi tại dòng ', ERROR_LINE(),
        N', procedure ', COALESCE(ERROR_PROCEDURE(), N'(script)'),
        N': ', ERROR_MESSAGE()
    );

    THROW 51000, @ErrorMessage, 1;
END CATCH;
