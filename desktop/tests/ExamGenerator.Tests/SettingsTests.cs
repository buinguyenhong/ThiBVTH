using ExamGenerator.Application;
using ExamGenerator.Domain;
using ExamGenerator.Infrastructure;

namespace ExamGenerator.Tests;

public class SettingsTests
{
    [Fact]
    public void Sqlite_path_is_relative_to_data_directory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "exam-generator-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LocalApplicationStorage(tempDir);

            Assert.Equal(
                Path.Combine(storage.DataDirectory, "exam-generator.sqlite"),
                storage.SqliteDatabasePath());
            Assert.Equal(
                Path.Combine(storage.DataDirectory, "appsettings.local.json"),
                storage.SettingsPath);
            Assert.Equal(
                Path.Combine(storage.DataDirectory, "credentials.local.bin"),
                storage.CredentialsPath);
            Assert.Equal(
                Path.Combine(storage.DataDirectory, "output"),
                storage.OutputDirectory);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Legacy_files_in_root_are_automatically_migrated_to_data_folder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "exam-gen-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var oldDb = Path.Combine(tempDir, "exam-generator.sqlite");
            File.WriteAllText(oldDb, "dummy-sqlite");
            var oldSettings = Path.Combine(tempDir, "appsettings.local.json");
            File.WriteAllText(oldSettings, "{}");
            var oldCreds = Path.Combine(tempDir, "credentials.local.bin");
            File.WriteAllText(oldCreds, "dummy-creds");

            var storage = new LocalApplicationStorage(tempDir);

            Assert.True(File.Exists(storage.SqliteDatabasePath()));
            Assert.True(File.Exists(storage.SettingsPath));
            Assert.True(File.Exists(storage.CredentialsPath));
            Assert.False(File.Exists(oldDb));
            Assert.False(File.Exists(oldSettings));
            Assert.False(File.Exists(oldCreds));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
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
