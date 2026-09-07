namespace ExamGenerator.Domain;

public sealed record SqlServerConnectionProfile(
    string Name,
    string Server,
    string Database,
    bool UseWindowsAuthentication,
    string? UserName = null,
    bool TrustServerCertificate = true);

public sealed record ApplicationSettings(
    string SqliteDatabaseFileName,
    IReadOnlyList<SqlServerConnectionProfile> SqlServerProfiles,
    string? ActiveSqlServerProfileName = null)
{
    public static ApplicationSettings Default => new(
        "exam-generator.sqlite",
        Array.Empty<SqlServerConnectionProfile>());
}

public static class ApplicationSettingsRules
{
    public static IReadOnlyList<string> Validate(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.SqliteDatabaseFileName))
            errors.Add("Tên file SQLite không được để trống.");
        else if (Path.IsPathRooted(settings.SqliteDatabaseFileName))
            errors.Add("SQLite phải là file tương đối trong thư mục phần mềm.");

        var duplicateNames = settings.SqlServerProfiles
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Where(x => string.IsNullOrWhiteSpace(x.Key) || x.Count() > 1);
        errors.AddRange(duplicateNames.Select(x => $"Tên connection profile SQL Server bị trùng hoặc để trống: '{x.Key}'."));

        foreach (var profile in settings.SqlServerProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Server)) errors.Add($"Profile '{profile.Name}' thiếu server.");
            if (string.IsNullOrWhiteSpace(profile.Database)) errors.Add($"Profile '{profile.Name}' thiếu database.");
            if (!profile.UseWindowsAuthentication && string.IsNullOrWhiteSpace(profile.UserName))
                errors.Add($"Profile '{profile.Name}' dùng SQL authentication phải có username.");
        }

        if (settings.ActiveSqlServerProfileName is not null &&
            !settings.SqlServerProfiles.Any(x => string.Equals(x.Name, settings.ActiveSqlServerProfileName, StringComparison.OrdinalIgnoreCase)))
            errors.Add("Profile SQL Server đang chọn không tồn tại.");

        return errors;
    }
}
