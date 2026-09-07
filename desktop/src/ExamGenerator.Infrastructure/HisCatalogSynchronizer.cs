using ExamGenerator.Domain;
using Microsoft.Data.SqlClient;

namespace ExamGenerator.Infrastructure;

public sealed class HisCatalogSynchronizer
{
    public async Task<IReadOnlyDictionary<string, int>> SynchronizeAsync(SqlServerConnectionProfile profile, string? password, SqliteDatabase sqlite, CancellationToken cancellationToken = default)
    {
        var builder = new SqlConnectionStringBuilder { DataSource = profile.Server, InitialCatalog = profile.Database, IntegratedSecurity = profile.UseWindowsAuthentication, Encrypt = false, TrustServerCertificate = profile.TrustServerCertificate, ConnectTimeout = 12 };
        if (!profile.UseWindowsAuthentication) { builder.UserID = profile.UserName; builder.Password = password ?? throw new InvalidOperationException("Chưa có password SQL Server."); }
        await using var connection = new SqlConnection(builder.ConnectionString); await connection.OpenAsync(cancellationToken);
        var definitions = new Dictionary<string, string>
        {
            ["Departments"] = "SELECT CAST(PhongBan_Id AS varchar(50)), ISNULL(MaPhongBan, CAST(PhongBan_Id AS varchar(50))), TenPhongBan, NULL FROM dbo.DM_PhongBan WHERE ISNULL(TamNgung, 0) = 0",
            ["Warehouses"] = "SELECT CAST(KhoDuoc_Id AS varchar(50)), ISNULL(MaKho, CAST(KhoDuoc_Id AS varchar(50))), TenKho, CAST(PhongBan_Id AS varchar(50)) FROM dbo.DM_KhoDuoc WHERE ISNULL(TamNgung, 0) = 0",
            ["ServiceGroups"] = "SELECT CAST(NhomDichVu_Id AS varchar(50)), ISNULL(MaNhomDichVu, CAST(NhomDichVu_Id AS varchar(50))), TenNhomDichVu, NULL FROM dbo.DM_NhomDichVu WHERE ISNULL(TamNgung, 0) = 0",
            ["Services"] = "SELECT CAST(DichVu_Id AS varchar(50)), ISNULL(MaDichVu, CAST(DichVu_Id AS varchar(50))), TenDichVu, CAST(NhomDichVu_Id AS varchar(50)) FROM dbo.DM_DichVu WHERE ISNULL(TamNgung, 0) = 0"
        };
        var counts = new Dictionary<string, int>();
        foreach (var definition in definitions)
        {
            var rows = new List<(string, string, string, string?)>();
            await using var command = new SqlCommand(definition.Value, connection); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
            await sqlite.ReplaceCatalogAsync(definition.Key, rows, cancellationToken); counts[definition.Key] = rows.Count;
        }
        return counts;
    }
}
