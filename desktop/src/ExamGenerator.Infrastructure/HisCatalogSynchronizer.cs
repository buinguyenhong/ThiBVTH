using ExamGenerator.Domain;
using Microsoft.Data.SqlClient;

namespace ExamGenerator.Infrastructure;

public sealed class HisCatalogSynchronizer
{
    private static string SafeGetString(SqlDataReader reader, int index, string defaultValue = "")
    {
        return reader.IsDBNull(index) ? defaultValue : reader.GetString(index);
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

        // 1. Synchronize Standard CatalogItems (Departments, Warehouses, ServiceGroups, Services)
        onProgress?.Invoke("Đang đồng bộ Khoa/Phòng, Kho Dược, Nhóm Dịch Vụ và Dịch Vụ kỹ thuật...");
        var basicDefinitions = new Dictionary<string, string>
        {
            ["Departments"] = "SELECT CAST(PhongBan_Id AS varchar(50)), ISNULL(MaPhongBan, CAST(PhongBan_Id AS varchar(50))), ISNULL(TenPhongBan, N'Khoa phòng'), NULL FROM dbo.DM_PhongBan WITH (NOLOCK) WHERE ISNULL(TamNgung, 0) = 0 AND Cap = 1 ORDER BY TenPhongBan",
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
                SELECT TOP (300)
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

                SELECT TOP (300)
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

        return counts;
    }
}
