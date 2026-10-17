using ExamGenerator.Domain;
using Microsoft.Data.SqlClient;

namespace ExamGenerator.Infrastructure;

public sealed class HisCatalogSynchronizer
{
    private static string SafeGetString(SqlDataReader reader, int index, string defaultValue = "")
    {
        if (reader.IsDBNull(index)) return defaultValue;
        var val = reader.GetValue(index);
        return val switch
        {
            null => defaultValue,
            string s => s,
            _ => val.ToString() ?? defaultValue
        };
    }

    private static int SafeGetInt32(SqlDataReader reader, int index, int defaultValue = 0)
    {
        if (reader.IsDBNull(index)) return defaultValue;
        try
        {
            return Convert.ToInt32(reader.GetValue(index));
        }
        catch
        {
            return defaultValue;
        }
    }

    private static double SafeGetDouble(SqlDataReader reader, int index, double defaultValue = 0.0)
    {
        if (reader.IsDBNull(index)) return defaultValue;
        try
        {
            var val = reader.GetValue(index);
            return Convert.ToDouble(val);
        }
        catch
        {
            return defaultValue;
        }
    }

    public async Task<IReadOnlyDictionary<string, int>> SynchronizeAsync(
        SqlServerConnectionProfile profile,
        string? password,
        SqliteDatabase sqlite,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.Server,
            InitialCatalog = profile.Database,
            IntegratedSecurity = profile.UseWindowsAuthentication,
            Encrypt = false,
            TrustServerCertificate = profile.TrustServerCertificate,
            ConnectTimeout = 15
        };

        if (!profile.UseWindowsAuthentication)
        {
            builder.UserID = profile.UserName;
            builder.Password = password ?? throw new InvalidOperationException("Chưa có password SQL Server.");
        }

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Thiết lập chế độ đọc không khóa (READ UNCOMMITTED) để không bị nghẽn (deadlock/lock-wait) với các transaction đang chạy của eHospital
        await using (var initCmd = connection.CreateCommand())
        {
            initCmd.CommandText = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SET NOCOUNT ON;";
            initCmd.CommandTimeout = 120;
            await initCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // 1. Synchronize Departments (Additive: Chỉ thêm mới, bảo vệ tuyệt đối danh mục hiện có, mapping và mẫu đề)
        onProgress?.Invoke("Đang đồng bộ Khoa/Phòng (Cơ chế bảo vệ danh mục & mapping)...");
        var deptQuery = "SELECT CAST(PhongBan_Id AS varchar(50)), ISNULL(MaPhongBan, CAST(PhongBan_Id AS varchar(50))), ISNULL(TenPhongBan, N'Khoa phòng'), NULL FROM dbo.DM_PhongBan WITH (NOLOCK) WHERE ISNULL(TamNgung, 0) = 0 AND Cap = 1 ORDER BY TenPhongBan";
        var deptRows = new List<(string Id, string Code, string Name, string? ParentId)>();
        await using (var deptCmd = new SqlCommand(deptQuery, connection))
        {
            deptCmd.CommandTimeout = 120;
            await using var reader = await deptCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                deptRows.Add((
                    SafeGetString(reader, 0),
                    SafeGetString(reader, 1),
                    SafeGetString(reader, 2),
                    reader.IsDBNull(3) ? null : SafeGetString(reader, 3)
                ));
            }
        }
        var (deptTotal, deptAdded, deptPreserved) = await sqlite.SyncDepartmentsAdditiveAsync(deptRows, cancellationToken);
        counts["Departments"] = deptTotal;
        counts["Departments_Added"] = deptAdded;
        counts["Departments_Preserved"] = deptPreserved;

        // 2. Synchronize Other Standard Catalogs (Warehouses, ServiceGroups, Services)
        onProgress?.Invoke("Đang đồng bộ Kho Dược, Nhóm Dịch Vụ và Dịch Vụ kỹ thuật...");
        var basicDefinitions = new Dictionary<string, string>
        {
            ["Warehouses"] = "SELECT CAST(KhoDuoc_Id AS varchar(50)), ISNULL(MaKho, CAST(KhoDuoc_Id AS varchar(50))), ISNULL(TenKho, N'Kho dược'), CAST(PhongBan_Id AS varchar(50)) FROM dbo.DM_KhoDuoc WITH (NOLOCK) WHERE ISNULL(TamNgung, 0) = 0 ORDER BY TenKho",
            ["ServiceGroups"] = "SELECT CAST(NhomDichVu_Id AS varchar(50)), ISNULL(MaNhomDichVu, CAST(NhomDichVu_Id AS varchar(50))), ISNULL(TenNhomDichVu, N'Nhóm dịch vụ'), NULL FROM dbo.DM_NhomDichVu WITH (NOLOCK) WHERE ISNULL(TamNgung, 0) = 0 ORDER BY TenNhomDichVu",
            ["Services"] = "SELECT CAST(d.DichVu_Id AS varchar(50)), ISNULL(d.MaDichVu, CAST(d.DichVu_Id AS varchar(50))), ISNULL(d.TenDichVu, N'Dịch vụ kỹ thuật'), CAST(d.NhomDichVu_Id AS varchar(50)) FROM dbo.DM_DichVu d WITH (NOLOCK) WHERE ISNULL(d.TamNgung, 0) = 0 AND (d.Cap = 1 OR d.Cap IS NULL) ORDER BY d.TenDichVu"
        };

        foreach (var (catalogType, query) in basicDefinitions)
        {
            var rows = new List<(string Id, string Code, string Name, string? ParentId)>();
            await using var command = new SqlCommand(query, connection);
            command.CommandTimeout = 120;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    SafeGetString(reader, 0),
                    SafeGetString(reader, 1),
                    SafeGetString(reader, 2),
                    reader.IsDBNull(3) ? null : SafeGetString(reader, 3)
                ));
            }
            await sqlite.ReplaceCatalogAsync(catalogType, rows, cancellationToken);
            counts[catalogType] = rows.Count;
        }

        // 2. Synchronize Active Users by Department (Sys_Users -> NhanVien_User_Mapping -> NhanVien -> DM_PhongBan)
        onProgress?.Invoke("Đang đồng bộ tài khoản User eHospital theo Khoa/Phòng...");
        var usersQuery = """
            ;WITH UserRanked AS
            (
                SELECT
                    UserId = CAST(u.User_Id AS varchar(50)),
                    UserName = COALESCE(NULLIF(LTRIM(RTRIM(u.User_Code)), ''), CAST(u.User_Id AS varchar(50))),
                    FullName = COALESCE(NULLIF(LTRIM(RTRIM(nv.TenNhanVien)), N''), NULLIF(LTRIM(RTRIM(u.User_Name)), N''), LTRIM(RTRIM(u.User_Code)), N'Cán bộ y tế'),
                    DepartmentCode = COALESCE(NULLIF(LTRIM(RTRIM(pb.MaPhongBan)), ''), CAST(pb.PhongBan_Id AS varchar(50)), 'CHUA_RO'),
                    DepartmentName = COALESCE(NULLIF(LTRIM(RTRIM(pb.TenPhongBan)), N''), N'Khoa phòng khác'),
                    RowNum = ROW_NUMBER() OVER (PARTITION BY u.User_Id ORDER BY nv.NhanVien_Id)
                FROM dbo.Sys_Users u WITH (NOLOCK)
                JOIN dbo.NhanVien_User_Mapping m WITH (NOLOCK) ON m.User_Id = u.User_Id
                JOIN dbo.NhanVien nv WITH (NOLOCK) ON nv.NhanVien_Id = m.NhanVien_Id
                JOIN dbo.DM_PhongBan pb WITH (NOLOCK) ON pb.PhongBan_Id = nv.PhongBan_Id
                WHERE ISNULL(u.Suspend, 0) = 0
                  AND (u.Expiration_Date IS NULL OR u.Expiration_Date >= CAST(GETDATE() AS date))
                  AND ISNULL(nv.TamNgung, 0) = 0
                  AND ISNULL(pb.TamNgung, 0) = 0
                  AND NULLIF(LTRIM(RTRIM(u.User_Code)), '') IS NOT NULL
                  AND NULLIF(LTRIM(RTRIM(pb.TenPhongBan)), '') IS NOT NULL
            )
            SELECT UserId, UserName, FullName, DepartmentCode, DepartmentName
            FROM UserRanked
            WHERE RowNum = 1
            ORDER BY DepartmentName, FullName, UserName;
            """;

        var userRows = new List<SnapshotUserRow>();
        await using (var userCmd = new SqlCommand(usersQuery, connection))
        {
            userCmd.CommandTimeout = 120;
            await using var reader = await userCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                userRows.Add(new SnapshotUserRow(
                    SafeGetString(reader, 0),
                    SafeGetString(reader, 1),
                    SafeGetString(reader, 2),
                    SafeGetString(reader, 3),
                    SafeGetString(reader, 4)
                ));
            }
        }
        await sqlite.ReplaceSnapshotUsersAsync(userRows, cancellationToken);
        counts["Users"] = userRows.Count;

        // 3. Synchronize Active Drugs with Positive Stock (DM_Duoc + DuocTonKho + DM_KhoDuoc + DM_PhongBan)
        onProgress?.Invoke("Đang trích xuất danh mục Thuốc & Tồn kho khả dụng thực tế...");
        var drugsQuery = """
            ;WITH TonTheoKho AS
            (
                SELECT
                    ton.Duoc_Id,
                    ton.KhoDuoc_Id,
                    SoLuongTon = SUM(ton.SoLuong)
                FROM dbo.DuocTonKho ton WITH (NOLOCK)
                WHERE ton.SoLuong > 0
                GROUP BY ton.Duoc_Id, ton.KhoDuoc_Id
            )
            SELECT
                DrugId = CAST(duoc.Duoc_Id AS varchar(50)) + '_' + CAST(kho.KhoDuoc_Id AS varchar(50)),
                DrugCode = COALESCE(NULLIF(LTRIM(RTRIM(duoc.MaDuoc)), ''), CAST(duoc.Duoc_Id AS varchar(50))),
                DrugName = COALESCE(NULLIF(LTRIM(RTRIM(duoc.TenDuocDayDu)), N''), NULLIF(LTRIM(RTRIM(duoc.TenHang)), N''), LTRIM(RTRIM(duoc.MaDuoc)), N'Thuốc/VTYT'),
                Unit = COALESCE(NULLIF(LTRIM(RTRIM(duoc.DonViTinh)), N''), N'Đơn vị'),
                WarehouseCode = COALESCE(NULLIF(LTRIM(RTRIM(kho.MaKho)), ''), CAST(kho.KhoDuoc_Id AS varchar(50))),
                WarehouseName = COALESCE(NULLIF(LTRIM(RTRIM(kho.TenKho)), N''), LTRIM(RTRIM(kho.MaKho)), N'Kho Dược'),
                FundingSource = CASE WHEN duoc.BHYT = 1 THEN 'BHYT' ELSE 'Viện phí' END,
                QuantityOnHand = CAST(ton.SoLuongTon AS float),
                DepartmentCode = ISNULL(phong.MaPhongBan, CAST(phong.PhongBan_Id AS varchar(50))),
                DepartmentName = LTRIM(RTRIM(phong.TenPhongBan))
            FROM TonTheoKho ton
            JOIN dbo.DM_Duoc duoc WITH (NOLOCK) ON duoc.Duoc_Id = ton.Duoc_Id
            JOIN dbo.DM_KhoDuoc kho WITH (NOLOCK) ON kho.KhoDuoc_Id = ton.KhoDuoc_Id
            LEFT JOIN dbo.DM_PhongBan phong WITH (NOLOCK) ON phong.PhongBan_Id = kho.PhongBan_Id
            WHERE ISNULL(duoc.TamNgung, 0) = 0
              AND ISNULL(kho.TamNgung, 0) = 0
            ORDER BY WarehouseName, DrugName;
            """;

        var drugRows = new List<SnapshotDrugRow>();
        await using (var drugCmd = new SqlCommand(drugsQuery, connection))
        {
            drugCmd.CommandTimeout = 120;
            await using var reader = await drugCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                drugRows.Add(new SnapshotDrugRow(
                    SafeGetString(reader, 0),
                    SafeGetString(reader, 1),
                    SafeGetString(reader, 2),
                    SafeGetString(reader, 3),
                    SafeGetString(reader, 4),
                    SafeGetString(reader, 5),
                    SafeGetString(reader, 6, "Viện phí"),
                    SafeGetDouble(reader, 7),
                    reader.IsDBNull(8) ? null : SafeGetString(reader, 8),
                    reader.IsDBNull(9) ? null : SafeGetString(reader, 9)
                ));
            }
        }
        await sqlite.ReplaceSnapshotDrugsAsync(drugRows, cancellationToken);
        counts["Drugs"] = drugRows.Count;

        // 4. Synchronize Patients (DM_BenhNhan + DM_BenhNhan_BHYT)
        onProgress?.Invoke("Đang lấy snapshot hồ sơ Bệnh nhân (BHYT & Viện phí)...");
        var patientsQuery = """
            ;WITH BhytRecent AS
            (
                SELECT TOP (2500)
                    the.BenhNhan_Id,
                    SoThe = ISNULL(the.SoThe, '')
                FROM dbo.DM_BenhNhan_BHYT the WITH (NOLOCK)
                WHERE ISNULL(the.TamNgung, 0) = 0
                  AND the.NgayHetHieuLuc >= CAST(GETDATE() AS date)
                ORDER BY the.BenhNhan_BHYT_Id DESC
            ),
            BenhNhanTop AS
            (
                SELECT
                    PatientId = CAST(bn.BenhNhan_Id AS varchar(50)),
                    MedicalCode = ISNULL(bn.MaYTe, CAST(bn.BenhNhan_Id AS varchar(50))),
                    FullName = LTRIM(RTRIM(bn.TenBenhNhan)),
                    DateOfBirth = COALESCE(CONVERT(varchar(10), bn.NgaySinh, 120), '1990-01-01'),
                    Gender = CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(bn.GioiTinh, '')))) IN ('T', 'M', 'NAM', '1') THEN N'Nam' ELSE N'Nữ' END,
                    Address = COALESCE(NULLIF(LTRIM(RTRIM(bn.DiaChiLienLac)), N''), NULLIF(LTRIM(RTRIM(bn.DiaChiThuongTru)), N''), N'Đắk Lắk'),
                    InsuranceNumber = NULLIF(bh.SoThe, ''),
                    Diagnosis = N'Khám bệnh, chẩn đoán ban đầu',
                    PaymentType = 'BHYT'
                FROM BhytRecent bh
                JOIN dbo.DM_BenhNhan bn WITH (NOLOCK) ON bn.BenhNhan_Id = bh.BenhNhan_Id
                WHERE ISNULL(bn.TamNgung, 0) = 0
                  AND NULLIF(LTRIM(RTRIM(bn.TenBenhNhan)), '') IS NOT NULL

                UNION ALL

                SELECT TOP (2500)
                    PatientId = CAST(bn.BenhNhan_Id AS varchar(50)),
                    MedicalCode = ISNULL(bn.MaYTe, CAST(bn.BenhNhan_Id AS varchar(50))),
                    FullName = LTRIM(RTRIM(bn.TenBenhNhan)),
                    DateOfBirth = COALESCE(CONVERT(varchar(10), bn.NgaySinh, 120), '1990-01-01'),
                    Gender = CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(bn.GioiTinh, '')))) IN ('T', 'M', 'NAM', '1') THEN N'Nam' ELSE N'Nữ' END,
                    Address = COALESCE(NULLIF(LTRIM(RTRIM(bn.DiaChiLienLac)), N''), NULLIF(LTRIM(RTRIM(bn.DiaChiThuongTru)), N''), N'Đắk Lắk'),
                    InsuranceNumber = NULL,
                    Diagnosis = N'Khám bệnh, chẩn đoán ban đầu',
                    PaymentType = 'Viện phí'
                FROM dbo.DM_BenhNhan bn WITH (NOLOCK)
                WHERE ISNULL(bn.TamNgung, 0) = 0
                  AND NULLIF(LTRIM(RTRIM(bn.TenBenhNhan)), '') IS NOT NULL
                ORDER BY bn.BenhNhan_Id DESC
            )
            SELECT DISTINCT * FROM BenhNhanTop;
            """;

        var patientRows = new List<SnapshotPatientRow>();
        await using (var patCmd = new SqlCommand(patientsQuery, connection))
        {
            patCmd.CommandTimeout = 120;
            await using var reader = await patCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                patientRows.Add(new SnapshotPatientRow(
                    SafeGetString(reader, 0),
                    SafeGetString(reader, 1),
                    SafeGetString(reader, 2),
                    SafeGetString(reader, 3, "1990-01-01"),
                    SafeGetString(reader, 4, "Nam"),
                    SafeGetString(reader, 5, "Đắk Lắk"),
                    reader.IsDBNull(6) ? null : SafeGetString(reader, 6),
                    SafeGetString(reader, 7, "Khám bệnh"),
                    SafeGetString(reader, 8, "Viện phí")
                ));
            }
        }
        await sqlite.ReplaceSnapshotPatientsAsync(patientRows, cancellationToken);
        counts["Patients"] = patientRows.Count;

        // 5. Synchronize Active Inpatient Cases (BenhAn with NgayRaVien IS NULL + LuuTru + Giuong/Phong)
        onProgress?.Invoke("Đang trích xuất danh sách Bệnh nhân Nội trú đang nằm viện...");
        var inpatientsQuery = """
            ;WITH ViTriManHinh AS (
                SELECT
                    ct.LuuTru_Id,
                    ct.LuuTruChiTiet_Id,
                    ct.GiuongBenh_Id,
                    ct.PhongBenh_Id,
                    ct.ThoiGianVao,
                    ct.BaoPhong,
                    ROW_NUMBER() OVER (
                        PARTITION BY ct.LuuTru_Id
                        ORDER BY ct.LuuTruChiTiet_Id DESC
                    ) AS rn
                FROM dbo.NoiTru_LuuTruChiTiet AS ct WITH (NOLOCK)
            ),
            LuuTruCuoi AS (
                SELECT
                    lt.LuuTru_Id,
                    lt.BenhAn_Id,
                    lt.PhongBan_Id,
                    lt.NgayVao,
                    lt.BacSiDieuTriChinh_Id,
                    ROW_NUMBER() OVER (
                        PARTITION BY lt.BenhAn_Id
                        ORDER BY lt.LuuTru_Id DESC
                    ) AS rn
                FROM dbo.NoiTru_LuuTru AS lt WITH (NOLOCK)
            )
            SELECT
                SoBenhAn = ISNULL(ba.SoBenhAn, ''),
                MaYTe = ISNULL(bn.MaYTe, ''),
                TenBenhNhan = ISNULL(bn.TenBenhNhan, ''),
                GioiTinh = CASE bn.GioiTinh WHEN 'T' THEN N'Nam' WHEN 'G' THEN N'Nữ' ELSE N'' END,
                NgaySinh = COALESCE(CONVERT(varchar(10), bn.NgaySinh, 103), CAST(bn.NamSinh AS varchar(4)), '1990'),
                DiaChi = ISNULL(bn.DiaChi, ''),
                Khoa = ISNULL(pbKhoa.TenPhongBan, ''),
                MaKhoa = ISNULL(pbKhoa.MaPhongBan, ''),
                Phong = ISNULL(pbPhong.TenPhongBan, ''),
                MaPhong = ISNULL(pbPhong.MaPhongBan, ''),
                Giuong = ISNULL(gb.MaGiuong, ''),
                Tang = ISNULL(CAST(gb.Tang AS varchar(20)), ''),
                NgayVaoVien = CONVERT(varchar(10), ba.NgayVaoVien, 103),
                ThoiGianVaoVien = CONVERT(varchar(19), ba.ThoiGianVaoVien, 120),
                VaoGiuongLuc = CONVERT(varchar(19), vt.ThoiGianVao, 120),
                NgayVaoKhoa = CONVERT(varchar(10), lt.NgayVao, 103),
                SoNgayNam = DATEDIFF(DAY, ba.ThoiGianVaoVien, GETDATE()),
                SoBHYT = ISNULL(tn.SoBHYT, ''),
                TenICD = ISNULL(icd.TenICD, ''),
                BacSiDieuTriChinh = ISNULL(nv.TenNhanVien, ''),
                BenhAn_Id = CAST(ba.BenhAn_Id AS varchar(50))
            FROM LuuTruCuoi AS lt
            INNER JOIN dbo.BenhAn AS ba WITH (NOLOCK) ON ba.BenhAn_Id = lt.BenhAn_Id
            LEFT JOIN dbo.DM_BenhNhan AS bn WITH (NOLOCK) ON bn.BenhNhan_Id = ba.BenhNhan_Id
            LEFT JOIN ViTriManHinh AS vt ON vt.LuuTru_Id = lt.LuuTru_Id AND vt.rn = 1
            LEFT JOIN dbo.DM_PhongBan AS pbKhoa WITH (NOLOCK) ON pbKhoa.PhongBan_Id = lt.PhongBan_Id
            LEFT JOIN dbo.DM_GiuongBenh AS gb WITH (NOLOCK) ON gb.GiuongBenh_Id = vt.GiuongBenh_Id
            LEFT JOIN dbo.DM_PhongBan AS pbPhong WITH (NOLOCK) ON pbPhong.PhongBan_Id = gb.PhongBenh_Id
            LEFT JOIN dbo.TiepNhan AS tn WITH (NOLOCK) ON tn.TiepNhan_Id = ba.TiepNhan_Id
            LEFT JOIN dbo.DM_ICD AS icd WITH (NOLOCK) ON icd.ICD_Id = ba.ICD_BenhChinh
            LEFT JOIN dbo.NhanVien AS nv WITH (NOLOCK) ON nv.NhanVien_Id = lt.BacSiDieuTriChinh_Id
            WHERE lt.rn = 1
              AND ba.NgayRaVien IS NULL
            ORDER BY ba.NgayVaoVien DESC, ba.BenhAn_Id DESC;
            """;

        var inpatientRows = new List<SnapshotInpatientRow>();
        await using (var inCmd = new SqlCommand(inpatientsQuery, connection))
        {
            inCmd.CommandTimeout = 120;
            await using var reader = await inCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                inpatientRows.Add(new SnapshotInpatientRow(
                    SafeGetString(reader, 20), // BenhAn_Id
                    SafeGetString(reader, 0), // SoBenhAn
                    SafeGetString(reader, 1), // MaYTe
                    SafeGetString(reader, 2), // TenBenhNhan
                    SafeGetString(reader, 3), // GioiTinh
                    SafeGetString(reader, 4), // NgaySinh
                    reader.IsDBNull(5) ? null : SafeGetString(reader, 5), // DiaChi
                    SafeGetString(reader, 7), // MaKhoa
                    SafeGetString(reader, 6), // TenKhoa
                    reader.IsDBNull(9) ? null : SafeGetString(reader, 9), // MaPhong
                    reader.IsDBNull(8) ? null : SafeGetString(reader, 8), // TenPhong
                    reader.IsDBNull(10) ? null : SafeGetString(reader, 10), // Giuong
                    reader.IsDBNull(11) ? null : SafeGetString(reader, 11), // Tang
                    SafeGetString(reader, 12, "01/01/2026"), // NgayVaoVien
                    reader.IsDBNull(13) ? null : SafeGetString(reader, 13), // ThoiGianVaoVien
                    reader.IsDBNull(14) ? null : SafeGetString(reader, 14), // VaoGiuongLuc
                    reader.IsDBNull(15) ? null : SafeGetString(reader, 15), // NgayVaoKhoa
                    SafeGetInt32(reader, 16), // SoNgayNam
                    reader.IsDBNull(17) ? null : SafeGetString(reader, 17), // SoBHYT
                    reader.IsDBNull(18) ? null : SafeGetString(reader, 18), // Chẩn đoán
                    reader.IsDBNull(19) ? null : SafeGetString(reader, 19)  // Bác sĩ
                ));
            }
        }
        await sqlite.ReplaceSnapshotInpatientsAsync(inpatientRows, cancellationToken);
        counts["Inpatients"] = inpatientRows.Count;

        // 6. Synchronize Active Outpatient Cases (BenhAnNgoaiTru with NgayRaVien IS NULL)
        onProgress?.Invoke("Đang trích xuất Bệnh án Ngoại trú đang điều trị...");
        var outpatientsQuery = """
            ;WITH GiuongNgoaiTru AS (
                SELECT
                    gb.BenhAnNgoaiTru_Id,
                    gb.GiuongBenh_Id,
                    gb.PhongBenh_Id,
                    gb.BaoPhong,
                    gb.ThoiGianVao,
                    ROW_NUMBER() OVER (
                        PARTITION BY gb.BenhAnNgoaiTru_Id
                        ORDER BY gb.NgoaiTru_GiuongBenh_Id DESC
                    ) AS rn
                FROM dbo.NgoaiTru_GiuongBenh AS gb WITH (NOLOCK)
                WHERE gb.ThoiGianRa IS NULL
                  AND ISNULL(gb.Huy, 0) = 0
            )
            SELECT
                SoBenhAn = ISNULL(bant.SoBenhAn, ''),
                SoLuuTru = ISNULL(bant.SoLuuTru, ''),
                MaYTe = ISNULL(bn.MaYTe, ''),
                TenBenhNhan = ISNULL(bn.TenBenhNhan, ''),
                GioiTinh = CASE bn.GioiTinh WHEN 'T' THEN N'Nam' WHEN 'G' THEN N'Nữ' ELSE N'' END,
                NgaySinh = COALESCE(CONVERT(varchar(10), bn.NgaySinh, 103), CAST(bn.NamSinh AS varchar(4)), '1990'),
                DiaChi = ISNULL(bn.DiaChi, ''),
                Khoa = ISNULL(pbKhoa.TenPhongBan, ''),
                MaKhoa = ISNULL(pbKhoa.MaPhongBan, ''),
                Phong = ISNULL(pbPhong.TenPhongBan, ''),
                MaPhong = ISNULL(pbPhong.MaPhongBan, ''),
                Giuong = ISNULL(gb.MaGiuong, ''),
                DieuTriTuNgay = CONVERT(varchar(10), bant.DieuTriTuNgay, 103),
                DieuTriDenNgay = CONVERT(varchar(10), bant.DieuTriDenNgay, 103),
                NgayNhapVien = CONVERT(varchar(10), bant.NgayNhapVien, 103),
                SoNgay = DATEDIFF(DAY, bant.ThoiGianNhapVien, GETDATE()),
                SoBHYT = ISNULL(tn.SoBHYT, ''),
                TenICD = ISNULL(icd.TenICD, ''),
                BacSiDieuTri = ISNULL(nv.TenNhanVien, ''),
                BenhAnNgoaiTru_Id = CAST(bant.BenhAnNgoaiTru_Id AS varchar(50))
            FROM dbo.BenhAnNgoaiTru AS bant WITH (NOLOCK)
            INNER JOIN dbo.DM_BenhNhan AS bn WITH (NOLOCK) ON bn.BenhNhan_Id = bant.BenhNhan_Id
            LEFT JOIN GiuongNgoaiTru AS gnt ON gnt.BenhAnNgoaiTru_Id = bant.BenhAnNgoaiTru_Id AND gnt.rn = 1
            LEFT JOIN dbo.DM_PhongBan AS pbKhoa WITH (NOLOCK) ON pbKhoa.PhongBan_Id = bant.KhoaDieuTri_Id
            LEFT JOIN dbo.DM_GiuongBenh AS gb WITH (NOLOCK) ON gb.GiuongBenh_Id = gnt.GiuongBenh_Id
            LEFT JOIN dbo.DM_PhongBan AS pbPhong WITH (NOLOCK) ON pbPhong.PhongBan_Id = gnt.PhongBenh_Id
            LEFT JOIN dbo.TiepNhan AS tn WITH (NOLOCK) ON tn.TiepNhan_Id = bant.TiepNhan_Id
            LEFT JOIN dbo.DM_ICD AS icd WITH (NOLOCK) ON icd.ICD_Id = bant.ICD_BenhChinh
            LEFT JOIN dbo.NhanVien AS nv WITH (NOLOCK) ON nv.NhanVien_Id = bant.BacSiDieuTri_Id
            WHERE bant.NgayRaVien IS NULL
            ORDER BY bant.NgayNhapVien DESC, bant.BenhAnNgoaiTru_Id DESC;
            """;

        var outpatientRows = new List<SnapshotOutpatientRow>();
        await using (var outCmd = new SqlCommand(outpatientsQuery, connection))
        {
            outCmd.CommandTimeout = 120;
            await using var reader = await outCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                outpatientRows.Add(new SnapshotOutpatientRow(
                    SafeGetString(reader, 19), // BenhAnNgoaiTru_Id
                    SafeGetString(reader, 0), // SoBenhAn
                    reader.IsDBNull(1) ? null : SafeGetString(reader, 1), // SoLuuTru
                    SafeGetString(reader, 2), // MaYTe
                    SafeGetString(reader, 3), // TenBenhNhan
                    SafeGetString(reader, 4), // GioiTinh
                    SafeGetString(reader, 5), // NgaySinh
                    reader.IsDBNull(6) ? null : SafeGetString(reader, 6), // DiaChi
                    SafeGetString(reader, 8), // MaKhoa
                    SafeGetString(reader, 7), // TenKhoa
                    reader.IsDBNull(10) ? null : SafeGetString(reader, 10), // MaPhong
                    reader.IsDBNull(9) ? null : SafeGetString(reader, 9), // TenPhong
                    reader.IsDBNull(11) ? null : SafeGetString(reader, 11), // Giuong
                    reader.IsDBNull(12) ? null : SafeGetString(reader, 12), // DieuTriTuNgay
                    reader.IsDBNull(13) ? null : SafeGetString(reader, 13), // DieuTriDenNgay
                    SafeGetString(reader, 14, "01/01/2026"), // NgayNhapVien
                    SafeGetInt32(reader, 15), // SoNgay
                    reader.IsDBNull(16) ? null : SafeGetString(reader, 16), // SoBHYT
                    reader.IsDBNull(17) ? null : SafeGetString(reader, 17), // Chẩn đoán
                    reader.IsDBNull(18) ? null : SafeGetString(reader, 18)  // Bác sĩ
                ));
            }
        }
        await sqlite.ReplaceSnapshotOutpatientsAsync(outpatientRows, cancellationToken);
        counts["Outpatients"] = outpatientRows.Count;

        // 7. Synchronize Inventory Periods for Stock Verification Exam Questions (Pre-calculated closing stock for fixed periods before March 2026)
        onProgress?.Invoke("Đang trích xuất Báo cáo Tồn kho theo các mốc ngày thi cố định...");
        var periodRows = new List<SnapshotInventoryPeriodRow>();

        var definedPeriods = new (string Code, string Name, string FromDate, string ToDate, string FirstOfMonth, int Month, int Year)[]
        {
            ("P1_2026_01A", "01/01/2026 đến 15/01/2026", "2026-01-01", "2026-01-15 23:59:59", "2026-01-01", 1, 2026),
            ("P2_2026_01B", "16/01/2026 đến 31/01/2026", "2026-01-16", "2026-01-31 23:59:59", "2026-01-01", 1, 2026),
            ("P3_2026_02A", "01/02/2026 đến 15/02/2026", "2026-02-01", "2026-02-15 23:59:59", "2026-02-01", 2, 2026),
            ("P4_2026_02B", "16/02/2026 đến 28/02/2026", "2026-02-16", "2026-02-28 23:59:59", "2026-02-01", 2, 2026)
        };

        foreach (var p in definedPeriods)
        {
            var periodQuery = """
                ;WITH DauKy AS (
                    SELECT 
                        DTDK.KhoDuoc_Id,
                        DTDK.Duoc_Id,
                        SoLuongDK = SUM(DTDK.SoLuong)
                    FROM dbo.DuocTonDauKy DTDK WITH (NOLOCK)
                    JOIN dbo.DuocKyTonKho DKTK WITH (NOLOCK) ON DKTK.DuocKyTonKho_ID = DTDK.DuocKyTonKho_ID
                    WHERE DKTK.Thang = @Thang AND DKTK.Nam = @Nam
                    GROUP BY DTDK.KhoDuoc_Id, DTDK.Duoc_Id
                ),
                PhatSinh AS (
                    SELECT 
                        Kho_Id = CASE WHEN CTU.LoaiChungTu = 'N' THEN CTU.KhoNhap_Id ELSE CTU.KhoXuat_Id END,
                        CT.Duoc_Id,
                        SoLuongPS = SUM(CASE WHEN CTU.LoaiChungTu = 'N' THEN CT.SoLuongThucTe * ISNULL(QD.GiaTriQuiDoi, 1)
                                             ELSE -CT.SoLuongThucTe * ISNULL(QD.GiaTriQuiDoi, 1) END)
                    FROM dbo.ChungTuChiTiet CT WITH (NOLOCK)
                    JOIN dbo.ChungTu CTU WITH (NOLOCK) ON CTU.ChungTu_Id = CT.ChungTu_Id
                    LEFT JOIN dbo.DM_DonViTinh QD WITH (NOLOCK) ON QD.DonViTinh_Id = CT.DonViTinh_Id
                    WHERE CTU.TrangThai IN ('Duoc_DTL', 'PNDNK')
                      AND CTU.NgayChungTu >= @DauThang AND CTU.NgayChungTu <= @DenNgay
                    GROUP BY (CASE WHEN CTU.LoaiChungTu = 'N' THEN CTU.KhoNhap_Id ELSE CTU.KhoXuat_Id END), CT.Duoc_Id
                ),
                Combined AS (
                    SELECT 
                        KhoDuoc_Id = COALESCE(dk.KhoDuoc_Id, ps.Kho_Id),
                        Duoc_Id = COALESCE(dk.Duoc_Id, ps.Duoc_Id),
                        ClosingStock = ISNULL(dk.SoLuongDK, 0) + ISNULL(ps.SoLuongPS, 0)
                    FROM DauKy dk
                    FULL OUTER JOIN PhatSinh ps ON ps.Kho_Id = dk.KhoDuoc_Id AND ps.Duoc_Id = dk.Duoc_Id
                )
                SELECT 
                    DrugCode = COALESCE(NULLIF(LTRIM(RTRIM(d.MaDuoc)), ''), CAST(d.Duoc_Id AS varchar(50))),
                    DrugName = COALESCE(NULLIF(LTRIM(RTRIM(d.TenDuocDayDu)), N''), NULLIF(LTRIM(RTRIM(d.TenHang)), N''), LTRIM(RTRIM(d.MaDuoc)), N'Thuốc/VTYT'),
                    Unit = COALESCE(NULLIF(LTRIM(RTRIM(d.DonViTinh)), N''), N'Đơn vị'),
                    WarehouseCode = COALESCE(NULLIF(LTRIM(RTRIM(k.MaKho)), ''), CAST(k.KhoDuoc_Id AS varchar(50))),
                    WarehouseName = COALESCE(NULLIF(LTRIM(RTRIM(k.TenKho)), N''), LTRIM(RTRIM(k.MaKho)), N'Kho Dược'),
                    DepartmentCode = ISNULL(pb.MaPhongBan, CAST(pb.PhongBan_Id AS varchar(50))),
                    DepartmentName = LTRIM(RTRIM(pb.TenPhongBan)),
                    ClosingStock = CAST(c.ClosingStock AS float),
                    FundingSource = CASE WHEN d.BHYT = 1 THEN 'BHYT' ELSE 'Viện phí' END
                FROM Combined c
                JOIN dbo.DM_Duoc d WITH (NOLOCK) ON d.Duoc_Id = c.Duoc_Id
                JOIN dbo.DM_KhoDuoc k WITH (NOLOCK) ON k.KhoDuoc_Id = c.KhoDuoc_Id
                LEFT JOIN dbo.DM_PhongBan pb WITH (NOLOCK) ON pb.PhongBan_Id = k.PhongBan_Id
                WHERE c.ClosingStock > 0
                  AND ISNULL(d.TamNgung, 0) = 0
                  AND ISNULL(k.TamNgung, 0) = 0
                ORDER BY WarehouseName, DrugName;
                """;

            try
            {
                await using var periodCmd = new SqlCommand(periodQuery, connection);
                periodCmd.CommandTimeout = 120;
                periodCmd.Parameters.AddWithValue("@Thang", p.Month);
                periodCmd.Parameters.AddWithValue("@Nam", p.Year);
                periodCmd.Parameters.AddWithValue("@DauThang", p.FirstOfMonth);
                periodCmd.Parameters.AddWithValue("@DenNgay", p.ToDate);

                await using var reader = await periodCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    periodRows.Add(new SnapshotInventoryPeriodRow(
                        p.Code,
                        p.Name,
                        p.FromDate,
                        p.ToDate,
                        SafeGetString(reader, 3), // WarehouseCode
                        SafeGetString(reader, 4), // WarehouseName
                        SafeGetString(reader, 5), // DepartmentCode
                        SafeGetString(reader, 6), // DepartmentName
                        SafeGetString(reader, 0), // DrugCode
                        SafeGetString(reader, 1), // DrugName
                        SafeGetString(reader, 2), // Unit
                        SafeGetDouble(reader, 7), // ClosingStock
                        SafeGetString(reader, 8, "Viện phí") // FundingSource
                    ));
                }
            }
            catch
            {
                // Fallback nếu CSDL staging chưa khóa sổ hoặc thiếu bảng DuocTonDauKy
            }
        }

        // Fallback an toàn nếu DB test chưa có dữ liệu giao dịch kỳ
        if (periodRows.Count == 0 && drugRows.Count > 0)
        {
            foreach (var p in definedPeriods)
            {
                foreach (var d in drugRows.Take(100))
                {
                    periodRows.Add(new SnapshotInventoryPeriodRow(
                        p.Code,
                        p.Name,
                        p.FromDate,
                        p.ToDate,
                        d.WarehouseCode,
                        d.WarehouseName,
                        d.DepartmentCode ?? "",
                        d.DepartmentName ?? "",
                        d.DrugCode,
                        d.DrugName,
                        d.Unit,
                        d.QuantityOnHand,
                        d.FundingSource
                    ));
                }
            }
        }

        await sqlite.ReplaceSnapshotInventoryPeriodsAsync(periodRows, cancellationToken);
        counts["InventoryPeriods"] = periodRows.Count;

        return counts;
    }
}
