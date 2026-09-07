using ExamGenerator.Domain;
using Microsoft.Data.SqlClient;

namespace ExamGenerator.Infrastructure;

public static class SqlServerConnectionTester
{
    public static async Task TestAsync(SqlServerConnectionProfile profile, string? password = null, CancellationToken cancellationToken = default)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.Server,
            InitialCatalog = profile.Database,
            IntegratedSecurity = profile.UseWindowsAuthentication,
            Encrypt = false,
            TrustServerCertificate = profile.TrustServerCertificate,
            ConnectTimeout = 8
        };

        if (!profile.UseWindowsAuthentication)
        {
            builder.UserID = profile.UserName;
            builder.Password = password ?? throw new InvalidOperationException("Chưa có mật khẩu SQL Server cho profile này.");
        }

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
