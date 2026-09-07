namespace ExamGenerator.Infrastructure;

/// <summary>Marker for the future SQL Server catalog adapter. It must expose SELECT-only operations.</summary>
public interface IReadOnlyCatalogConnection
{
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed class ReadOnlyCatalogConnection : IReadOnlyCatalogConnection
{
    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("SQL Server adapter chưa được cấu hình; không được tự động ghi vào HIS.");
    }
}
