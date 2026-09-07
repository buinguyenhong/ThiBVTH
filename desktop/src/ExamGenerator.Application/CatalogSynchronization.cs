using ExamGenerator.Domain;

namespace ExamGenerator.Application;

public interface IReadOnlyCatalogProvider
{
    Task<CatalogDataSet> ReadAsync(CancellationToken cancellationToken = default);
}

public interface ICatalogSnapshotStore
{
    Task SaveAsync(CatalogDataSet dataSet, CancellationToken cancellationToken = default);
}

public sealed record CatalogDataSet(
    IReadOnlyList<Department> Departments,
    IReadOnlyList<Warehouse> Warehouses,
    IReadOnlyList<ServiceGroup> ServiceGroups,
    IReadOnlyList<ClinicalService> Services,
    IReadOnlyList<InventoryItem> Inventory,
    IReadOnlyList<HisUser> Users,
    IReadOnlyList<PatientSource> Patients,
    CatalogSnapshot Snapshot);

public sealed class CatalogSynchronizationService
{
    private readonly IReadOnlyCatalogProvider _provider;
    private readonly ICatalogSnapshotStore _store;

    public CatalogSynchronizationService(IReadOnlyCatalogProvider provider, ICatalogSnapshotStore store)
    {
        _provider = provider;
        _store = store;
    }

    public async Task<CatalogSnapshot> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        var dataSet = await _provider.ReadAsync(cancellationToken);
        var errors = Validate(dataSet).ToList();
        var snapshot = dataSet.Snapshot with { ValidationErrors = errors };

        if (errors.Count > 0)
            return snapshot;

        await _store.SaveAsync(dataSet with { Snapshot = snapshot }, cancellationToken);
        return snapshot;
    }

    private static IEnumerable<string> Validate(CatalogDataSet dataSet)
    {
        foreach (var error in CatalogRules.ValidateDepartments(dataSet.Departments)) yield return error;
        foreach (var error in CatalogRules.ValidateWarehouses(dataSet.Warehouses)) yield return error;
        foreach (var error in CatalogRules.ValidateServices(dataSet.Services, dataSet.ServiceGroups)) yield return error;
        foreach (var error in CatalogRules.ValidateInventory(dataSet.Inventory, dataSet.Warehouses)) yield return error;
    }
}
