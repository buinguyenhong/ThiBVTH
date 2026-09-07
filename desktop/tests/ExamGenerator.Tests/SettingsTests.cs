using ExamGenerator.Application;
using ExamGenerator.Domain;
using ExamGenerator.Infrastructure;

namespace ExamGenerator.Tests;

public class SettingsTests
{
    [Fact]
    public void Sqlite_path_is_relative_to_application_directory()
    {
        var storage = new LocalApplicationStorage(Path.Combine(Path.GetTempPath(), "exam-generator-test"));

        Assert.Equal(
            Path.Combine(storage.ApplicationDirectory, "exam-generator.sqlite"),
            storage.SqliteDatabasePath());
    }

    [Fact]
    public void Settings_reject_absolute_sqlite_path()
    {
        var errors = ApplicationSettingsRules.Validate(new ApplicationSettings(
            Path.Combine("C:\\", "outside.sqlite"), Array.Empty<SqlServerConnectionProfile>()));

        Assert.Contains(errors, error => error.Contains("tương đối", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Settings_round_trip_to_local_json()
    {
        var directory = Path.Combine(Path.GetTempPath(), "exam-generator-settings-" + Guid.NewGuid().ToString("N"));
        var storage = new LocalApplicationStorage(directory);
        var settings = new ApplicationSettings(
            "exam.sqlite",
            new[] { new SqlServerConnectionProfile("HIS thi", "SERVER\\INSTANCE", "eHospital", true) },
            "HIS thi");

        var store = new JsonApplicationSettingsStore(storage);
        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal(settings.SqliteDatabaseFileName, loaded.SqliteDatabaseFileName);
        Assert.Equal(settings.ActiveSqlServerProfileName, loaded.ActiveSqlServerProfileName);
        Assert.Single(loaded.SqlServerProfiles);
        Assert.Equal(settings.SqlServerProfiles[0], loaded.SqlServerProfiles[0]);
    }
}
