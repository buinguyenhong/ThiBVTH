using ExamGenerator.Domain;

namespace ExamGenerator.Application;

public interface IApplicationSettingsStore
{
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}

public sealed class SettingsService
{
    private readonly IApplicationSettingsStore _store;

    public SettingsService(IApplicationSettingsStore store) => _store = store;

    public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) => _store.LoadAsync(cancellationToken);

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        var errors = ApplicationSettingsRules.Validate(settings);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

        await _store.SaveAsync(settings, cancellationToken);
    }
}
