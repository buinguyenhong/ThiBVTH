using ExamGenerator.Domain;
using Microsoft.Data.SqlClient;

namespace ExamGenerator.Infrastructure;

public sealed class HisCatalogSynchronizer
{
    public async Task<IReadOnlyDictionary<string, int>> SynchronizeAsync(
        SqlServerConnectionProfile profile,
        string? password,
        SqliteDatabase sqlite,
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

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // 1. Synchronize Standard CatalogItems (Departments, Warehouses, ServiceGroups, Services)
        var basicDefinitions = new Dictionary<string, string>
        {
            ["Departments"] = "SELECT CAST(PhongBan_Id AS varchar(50)), ISNULL(MaPhongBan, CAST(PhongBan_Id AS varchar(50))), TenPhongBan, NULL FROM dbo.DM_PhongBan WHERE ISNULL(TamNgung, 0) = 0 ORDER BY TenPhongBan",
            ["Warehouses"] = "SELECT CAST(KhoDuoc_Id AS varchar(50)), ISNULL(MaKho, CAST(KhoDuoc_Id AS varchar(50))), TenKho, CAST(PhongBan_Id AS varchar(50)) FROM dbo.DM_KhoDuoc WHERE ISNULL(TamNgung, 0) = 0 ORDER BY TenKho",
            ["ServiceGroups"] = "SELECT CAST(NhomDichVu_Id AS varchar(50)), ISNULL(MaNhomDichVu, CAST(NhomDichVu_Id AS varchar(50))), TenNhomDichVu, NULL FROM dbo.DM_NhomDichVu WHERE ISNULL(TamNgung, 0) = 0 ORDER BY TenNhomDichVu",
            ["Services"] = "SELECT CAST(d.DichVu_Id AS varchar(50)), ISNULL(d.MaDichVu, CAST(d.DichVu_Id AS varchar(50))), d.TenDichVu, CAST(d.NhomDichVu_Id AS varchar(50)) FROM dbo.DM_DichVu d WHERE ISNULL(d.TamNgung, 0) = 0 ORDER BY d.TenDichVu"
        };

        foreach (var (catalogType, query) in basicDefinitions)
        {
            var rows = new List<(string Id, string Code, string Name, string? ParentId)>();
            await using var command = new SqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)
                ));
            }
            await sqlite.ReplaceCatalogAsync(catalogType, rows, cancellationToken);
            counts[catalogType] = rows.Count;
        }

        // 2. Synchronize Active Users by Department (Sys_Users -> NhanVien_User_Mapping -> NhanVien -> DM_PhongBan)
        var usersQuery = """
            SELECT DISTINCT
                UserId = CAST(u.User_Id AS varchar(50)),
                UserName = LTRIM(RTRIM(u.User_Code)),
                FullName = COALESCE(NULLIF(LTRIM(RTRIM(nv.TenNhanVien)), N''), NULLIF(LTRIM(RTRIM(u.User_Name)), N''), LTRIM(RTRIM(u.User_Code))),
                DepartmentCode = ISNULL(pb.MaPhongBan, CAST(pb.PhongBan_Id AS varchar(50))),
                DepartmentName = LTRIM(RTRIM(pb.TenPhongBan))
            FROM dbo.Sys_Users u
            JOIN dbo.NhanVien_User_Mapping m ON m.User_Id = u.User_Id
            JOIN dbo.NhanVien nv ON nv.NhanVien_Id = m.NhanVien_Id
            JOIN dbo.DM_PhongBan pb ON pb.PhongBan_Id = nv.PhongBan_Id
            WHERE ISNULL(u.Suspend, 0) = 0
              AND (u.Expiration_Date IS NULL OR u.Expiration_Date >= CAST(GETDATE() AS date))
              AND ISNULL(nv.TamNgung, 0) = 0
              AND ISNULL(pb.TamNgung, 0) = 0
              AND NULLIF(LTRIM(RTRIM(u.User_Code)), '') IS NOT NULL
              AND NULLIF(LTRIM(RTRIM(pb.TenPhongBan)), '') IS NOT NULL
            ORDER BY DepartmentName, FullName, UserName;
            """;

        var userRows = new List<SnapshotUserRow>();
        await using (var userCmd = new SqlCommand(usersQuery, connection))
        await using (var reader = await userCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                userRows.Add(new SnapshotUserRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)
                ));
            }
        }
        await sqlite.ReplaceSnapshotUsersAsync(userRows, cancellationToken);
        counts["Users"] = userRows.Count;

        // 3. Synchronize Active Drugs with Positive Stock (DM_Duoc + DuocTonKho + DM_KhoDuoc)
        var drugsQuery = """
            ;WITH TonTheoKho AS
            (
                SELECT
                    ton.Duoc_Id,
                    ton.KhoDuoc_Id,
                    ton.NguonNhapHang_Id,
                    SoLuongTon = SUM(ISNULL(ton.SoLuong, 0))
                FROM dbo.DuocTonKho ton
                GROUP BY ton.Duoc_Id, ton.KhoDuoc_Id, ton.NguonNhapHang_Id
                HAVING SUM(ISNULL(ton.SoLuong, 0)) > 0
            )
            SELECT
                DrugId = CAST(duoc.Duoc_Id AS varchar(50)) + '_' + CAST(kho.KhoDuoc_Id AS varchar(50)),
                DrugCode = LTRIM(RTRIM(duoc.MaDuoc)),
                DrugName = COALESCE(NULLIF(LTRIM(RTRIM(duoc.TenDuocDayDu)), N''), NULLIF(LTRIM(RTRIM(duoc.TenHang)), N''), LTRIM(RTRIM(duoc.MaDuoc))),
                Unit = COALESCE(NULLIF(LTRIM(RTRIM(duoc.DonViTinh)), N''), N'Đơn vị'),
                WarehouseCode = LTRIM(RTRIM(kho.MaKho)),
                WarehouseName = COALESCE(NULLIF(LTRIM(RTRIM(kho.TenKho)), N''), LTRIM(RTRIM(kho.MaKho))),
                FundingSource = CASE WHEN duoc.BHYT = 1 THEN 'BHYT' ELSE 'Viện phí' END,
                QuantityOnHand = CAST(ton.SoLuongTon AS float)
            FROM TonTheoKho ton
            JOIN dbo.DM_Duoc duoc ON duoc.Duoc_Id = ton.Duoc_Id
            JOIN dbo.DM_KhoDuoc kho ON kho.KhoDuoc_Id = ton.KhoDuoc_Id
            WHERE ISNULL(duoc.TamNgung, 0) = 0
              AND ISNULL(kho.TamNgung, 0) = 0
            ORDER BY WarehouseName, DrugName;
            """;

        var drugRows = new List<SnapshotDrugRow>();
        await using (var drugCmd = new SqlCommand(drugsQuery, connection))
        await using (var reader = await drugCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                drugRows.Add(new SnapshotDrugRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetDouble(7)
                ));
            }
        }
        await sqlite.ReplaceSnapshotDrugsAsync(drugRows, cancellationToken);
        counts["Drugs"] = drugRows.Count;

        // 4. Synchronize Patients (DM_BenhNhan + DM_BenhNhan_BHYT)
        var patientsQuery = """
            SELECT TOP (500)
                PatientId = CAST(bn.BenhNhan_Id AS varchar(50)),
                MedicalCode = ISNULL(bn.MaYTe, CAST(bn.BenhNhan_Id AS varchar(50))),
                FullName = LTRIM(RTRIM(bn.TenBenhNhan)),
                DateOfBirth = CONVERT(varchar(10), bn.NgaySinh, 120),
                Gender = CASE WHEN bn.GioiTinh = 'T' THEN N'Nam' ELSE N'Nữ' END,
                Address = COALESCE(NULLIF(LTRIM(RTRIM(bn.DiaChiLienLac)), N''), NULLIF(LTRIM(RTRIM(bn.DiaChiThuongTru)), N''), N'Đắk Lắk'),
                InsuranceNumber = the.SoThe,
                Diagnosis = N'Khám bệnh, chẩn đoán ban đầu',
                PaymentType = CASE WHEN the.SoThe IS NOT NULL THEN 'BHYT' ELSE 'Viện phí' END
            FROM dbo.DM_BenhNhan bn
            OUTER APPLY
            (
                SELECT TOP (1) the.SoThe
                FROM dbo.DM_BenhNhan_BHYT the
                WHERE the.BenhNhan_Id = bn.BenhNhan_Id
                  AND ISNULL(the.TamNgung, 0) = 0
                  AND the.NgayHetHieuLuc >= CAST(GETDATE() AS date)
                ORDER BY the.NgayHetHieuLuc DESC
            ) the
            WHERE NULLIF(LTRIM(RTRIM(bn.TenBenhNhan)), '') IS NOT NULL
            ORDER BY bn.BenhNhan_Id DESC;
            """;

        var patientRows = new List<SnapshotPatientRow>();
        await using (var patCmd = new SqlCommand(patientsQuery, connection))
        await using (var reader = await patCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                patientRows.Add(new SnapshotPatientRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8)
                ));
            }
        }
        await sqlite.ReplaceSnapshotPatientsAsync(patientRows, cancellationToken);
        counts["Patients"] = patientRows.Count;

        return counts;
    }
}
