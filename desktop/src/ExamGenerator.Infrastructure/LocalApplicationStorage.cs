using System.Text.Json;
using ExamGenerator.Application;
using ExamGenerator.Domain;

namespace ExamGenerator.Infrastructure;

public sealed class LocalApplicationStorage
{
    public LocalApplicationStorage(string? applicationDirectory = null)
    {
        ApplicationDirectory = applicationDirectory ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(ApplicationDirectory);
    }

    public string ApplicationDirectory { get; }

    public string SqliteDatabasePath(string fileName = "exam-generator.sqlite")
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
            throw new ArgumentException("SQLite database phải là tên file tương đối.", nameof(fileName));

        return Path.Combine(ApplicationDirectory, fileName);
    }

    public string SettingsPath => Path.Combine(ApplicationDirectory, "appsettings.local.json");
    public string CredentialsPath => Path.Combine(ApplicationDirectory, "credentials.local.bin");
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ProtectedCredentialStore
{
    private readonly LocalApplicationStorage _storage;
    public ProtectedCredentialStore(LocalApplicationStorage storage) => _storage = storage;

    public void Save(string profileName, string password)
    {
        var value = System.Text.Encoding.UTF8.GetBytes($"{profileName}\n{password}");
        var protectedBytes = System.Security.Cryptography.ProtectedData.Protect(value, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_storage.CredentialsPath, protectedBytes);
    }

    public string? Load(string profileName)
    {
        if (!File.Exists(_storage.CredentialsPath)) return null;
        var bytes = System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(_storage.CredentialsPath), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        var parts = System.Text.Encoding.UTF8.GetString(bytes).Split('\n', 2);
        return parts.Length == 2 && string.Equals(parts[0], profileName, StringComparison.Ordinal) ? parts[1] : null;
    }
}

public sealed class JsonApplicationSettingsStore : IApplicationSettingsStore
{
    private readonly LocalApplicationStorage _storage;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public JsonApplicationSettingsStore(LocalApplicationStorage storage) => _storage = storage;

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_storage.SettingsPath))
            return ApplicationSettings.Default;

        await using var stream = File.OpenRead(_storage.SettingsPath);
        return await JsonSerializer.DeserializeAsync<ApplicationSettings>(stream, _options, cancellationToken)
            ?? ApplicationSettings.Default;
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        var temporaryPath = _storage.SettingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, settings, _options, cancellationToken);

        File.Move(temporaryPath, _storage.SettingsPath, overwrite: true);
    }
}
