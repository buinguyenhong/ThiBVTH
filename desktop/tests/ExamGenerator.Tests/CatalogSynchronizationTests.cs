using ExamGenerator.Application;
using ExamGenerator.Domain;

namespace ExamGenerator.Tests;

public class CatalogSynchronizationTests
{
    [Fact]
    public async Task Invalid_catalog_is_not_saved()
    {
        var data = CreateDataSet(
            new[] { new Warehouse("w1", "KHO-01", "Kho 1", null) });
        var store = new MemorySnapshotStore();

        var snapshot = await new CatalogSynchronizationService(
            new MemoryCatalogProvider(data), store).SynchronizeAsync();

        Assert.False(snapshot.IsValid);
        Assert.Empty(store.Saved);
        Assert.Contains(snapshot.ValidationErrors, error => error.Contains("chưa có khoa", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Valid_catalog_is_saved_with_validation_result()
    {
        var data = CreateDataSet(
            new[] { new Warehouse("w1", "KHO-01", "Kho 1", "NOI") });
        var store = new MemorySnapshotStore();

        var snapshot = await new CatalogSynchronizationService(
            new MemoryCatalogProvider(data), store).SynchronizeAsync();

        Assert.True(snapshot.IsValid);
        Assert.Single(store.Saved);
        Assert.Equal(snapshot.Id, store.Saved[0].Snapshot.Id);
    }

    private static CatalogDataSet CreateDataSet(IReadOnlyList<Warehouse> warehouses)
    {
        var snapshot = new CatalogSnapshot(
            "snapshot-1", DateTimeOffset.UtcNow, "test", "v1",
            new Dictionary<string, int>(), Array.Empty<string>());

        return new CatalogDataSet(
            new[] { new Department("d1", "NOI", "Khoa Nội") },
            warehouses,
            new[] { new ServiceGroup("g1", "CLS", "Cận lâm sàng") },
            new[] { new ClinicalService("s1", "SA-01", "Siêu âm", "g1") },
            new[] { new InventoryItem("i1", "D-01", "Thuốc thử", "Viên", "w1", "BHYT", 10, InventoryItemType.Drug) },
            new[] { new HisUser("u1", "user01", "User 01", "d1") },
            new[] { new PatientSource("p1", "BN-01", "Bệnh nhân 01", new DateOnly(1990, 1, 1), "Nam", "Địa chỉ", PatientPaymentType.Insurance, "TE1001") },
            snapshot);
    }

    private sealed class MemoryCatalogProvider(CatalogDataSet data) : IReadOnlyCatalogProvider
    {
        public Task<CatalogDataSet> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(data);
    }

    private sealed class MemorySnapshotStore : ICatalogSnapshotStore
    {
        public List<CatalogDataSet> Saved { get; } = [];

        public Task SaveAsync(CatalogDataSet dataSet, CancellationToken cancellationToken = default)
        {
            Saved.Add(dataSet);
            return Task.CompletedTask;
        }
    }
}
