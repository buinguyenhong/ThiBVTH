using System.Data;
using System.Text;
using ExamGenerator.Domain;
using Microsoft.Data.SqlClient;

namespace ExamGenerator.Infrastructure;

public sealed class HisAuditTrailService
{
    private static string BuildConnectionString(SqlServerConnectionProfile profile, string? password)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.Server,
            InitialCatalog = profile.Database,
            IntegratedSecurity = profile.UseWindowsAuthentication,
            Encrypt = false,
            TrustServerCertificate = profile.TrustServerCertificate,
            ConnectTimeout = 4
        };

        if (!profile.UseWindowsAuthentication && !string.IsNullOrWhiteSpace(profile.UserName))
        {
            builder.UserID = profile.UserName;
            builder.Password = password ?? string.Empty;
        }

        return builder.ConnectionString;
    }

    public async Task<string> QueryAuditTrailAsync(
        SqlServerConnectionProfile? profile,
        string? password,
        string hisUserCode,
        DateTime startedAt,
        DateTime submittedAt,
        string actionCode,
        CancellationToken cancellationToken = default)
    {
        if (profile is null || string.IsNullOrWhiteSpace(hisUserCode))
        {
            return "(Chưa kết nối CSDL HIS)";
        }

        try
        {
            var connStr = BuildConnectionString(profile, password);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);

            // 1. Resolve NhanVien_Id and User_Id (supports both User_Code from Sys_Users and MaNhanVien from NhanVien)
            int? nhanVienId = null;
            int? sysUserId = null;
            await using (var cmdNv = conn.CreateCommand())
            {
                cmdNv.CommandText = @"
                    SELECT TOP 1
                        COALESCE(nv.NhanVien_Id, map.NhanVien_Id) AS NhanVien_Id,
                        u.User_Id
                    FROM dbo.Sys_Users u
                    LEFT JOIN dbo.NhanVien_User_Mapping map ON u.User_Id = map.User_Id
                    LEFT JOIN dbo.NhanVien nv ON map.NhanVien_Id = nv.NhanVien_Id
                    WHERE u.User_Code = @code OR nv.MaNhanVien = @code
                    UNION ALL
                    SELECT TOP 1
                        nv2.NhanVien_Id,
                        map2.User_Id
                    FROM dbo.NhanVien nv2
                    LEFT JOIN dbo.NhanVien_User_Mapping map2 ON nv2.NhanVien_Id = map2.NhanVien_Id
                    WHERE nv2.MaNhanVien = @code";
                cmdNv.Parameters.Add(new SqlParameter("@code", SqlDbType.VarChar) { Value = hisUserCode.Trim() });
                await using var nvReader = await cmdNv.ExecuteReaderAsync(cancellationToken);
                if (await nvReader.ReadAsync(cancellationToken))
                {
                    if (!nvReader.IsDBNull(0)) nhanVienId = Convert.ToInt32(nvReader.GetValue(0));
                    if (!nvReader.IsDBNull(1)) sysUserId = Convert.ToInt32(nvReader.GetValue(1));
                }
            }

            if (nhanVienId is null && sysUserId is null)
            {
                return $"(Không tìm thấy tài khoản / nhân viên HIS có mã: {hisUserCode})";
            }

            // Extend start and end buffer slightly by 5 minutes for safety
            var windowStart = startedAt.AddMinutes(-5);
            var windowEnd = submittedAt.AddMinutes(5);
            var effectiveActorId = nhanVienId ?? sysUserId!.Value;
            var effectiveUserId = sysUserId ?? nhanVienId!.Value;

            switch (actionCode)
            {
                case "LT_BAN_PHIEU_KHAM":
                case "TK_TIEP_NHAN":
                case "TN_TIEP_NHAN":
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT tn.SoTiepNhan, tn.NgayTiepNhan, bn.MaBenhNhan, bn.TenBenhNhan, tn.LoaiKham_Id
                        FROM dbo.TiepNhan tn
                        LEFT JOIN dbo.BenhNhan bn ON tn.BenhNhan_Id = bn.BenhNhan_Id
                        WHERE (tn.NguoiTao_Id = @nvId OR tn.NguoiTao_Id = @userId)
                          AND tn.NgayTiepNhan BETWEEN @start AND @end
                        ORDER BY tn.NgayTiepNhan ASC";
                    cmd.Parameters.Add(new SqlParameter("@nvId", SqlDbType.Int) { Value = effectiveActorId });
                    cmd.Parameters.Add(new SqlParameter("@userId", SqlDbType.Int) { Value = effectiveUserId });
                    cmd.Parameters.Add(new SqlParameter("@start", SqlDbType.DateTime) { Value = windowStart });
                    cmd.Parameters.Add(new SqlParameter("@end", SqlDbType.DateTime) { Value = windowEnd });

                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    var sb = new StringBuilder();
                    var count = 0;
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        count++;
                        var stn = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        var time = reader.IsDBNull(1) ? DateTime.MinValue : reader.GetDateTime(1);
                        var maBn = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        var tenBn = reader.IsDBNull(3) ? "" : reader.GetString(3);
                        sb.AppendLine($"  + [{time:HH:mm:ss}] Tiếp nhận: {tenBn} (Mã: {maBn}, Số TN: {stn})");
                    }

                    return count > 0
                        ? $"Đã thực hiện {count} lượt tiếp nhận trên HIS:\n" + sb.ToString().TrimEnd()
                        : "(Chưa tìm thấy lượt tiếp nhận nào trong khoảng thời gian thi)";
                }

                case "LT_THU_PHI_VA_CLS":
                case "LT_THU_TAM_UNG_NOI_TRU":
                case "TC_THU_TAM_UNG":
                case "LT_THANH_TOAN_RA_VIEN":
                case "TC_THANH_TOAN_RA_VIEN":
                case "LT_DOI_DICH_VU_CLS":
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT hd.SoHoaDon, hd.NgayTao, bn.TenBenhNhan, hd.GiaTriThucThu, hd.HuyHoaDon
                        FROM dbo.HoaDon hd
                        LEFT JOIN dbo.BenhNhan bn ON hd.BenhNhan_Id = bn.BenhNhan_Id
                        WHERE (hd.NguoiTao_Id = @nvId OR hd.NguoiTao_Id = @userId)
                          AND hd.NgayTao BETWEEN @start AND @end
                        ORDER BY hd.NgayTao ASC";
                    cmd.Parameters.Add(new SqlParameter("@nvId", SqlDbType.Int) { Value = effectiveActorId });
                    cmd.Parameters.Add(new SqlParameter("@userId", SqlDbType.Int) { Value = effectiveUserId });
                    cmd.Parameters.Add(new SqlParameter("@start", SqlDbType.DateTime) { Value = windowStart });
                    cmd.Parameters.Add(new SqlParameter("@end", SqlDbType.DateTime) { Value = windowEnd });

                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    var sb = new StringBuilder();
                    var count = 0;
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        count++;
                        var soHd = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        var time = reader.IsDBNull(1) ? DateTime.MinValue : reader.GetDateTime(1);
                        var tenBn = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        var tien = reader.IsDBNull(3) ? 0.0 : Convert.ToDouble(reader.GetValue(3));
                        var huy = !reader.IsDBNull(4) && Convert.ToBoolean(reader.GetValue(4));
                        var status = huy ? "[ĐÃ HỦY]" : "[HỢP LỆ]";
                        sb.AppendLine($"  + [{time:HH:mm:ss}] HĐ: {soHd} | BN: {tenBn} | Thực thu: {tien:N0} đ {status}");
                    }

                    return count > 0
                        ? $"Đã thực hiện {count} giao dịch thu viện phí trên HIS:\n" + sb.ToString().TrimEnd()
                        : "(Chưa tìm thấy hóa đơn thu phí nào trong khoảng thời gian thi)";
                }

                default:
                    return "(Thí sinh thực hiện thao tác tác nghiệp trực tiếp trên giao diện HIS)";
            }
        }
        catch (Exception ex)
        {
            return $"(Không thể truy vấn vết HIS: {ex.Message})";
        }
    }
}
