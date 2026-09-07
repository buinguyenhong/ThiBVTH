namespace ExamGenerator.Domain;

public sealed record Department(
    string Id,
    string Code,
    string Name,
    bool IsActive = true);

public sealed record Warehouse(
    string Id,
    string Code,
    string Name,
    string? DepartmentId,
    bool IsActive = true);

public sealed record ServiceGroup(
    string Id,
    string Code,
    string Name,
    bool IsActive = true);

public sealed record ClinicalService(
    string Id,
    string Code,
    string Name,
    string ServiceGroupId,
    bool IsActive = true);

public enum InventoryItemType
{
    Drug,
    MedicalSupply
}

public sealed record InventoryItem(
    string Id,
    string Code,
    string Name,
    string Unit,
    string WarehouseId,
    string FundingSource,
    decimal QuantityOnHand,
    InventoryItemType ItemType,
    bool IsActive = true);

public sealed record HisUser(
    string Id,
    string Code,
    string Name,
    string DepartmentId,
    bool IsActive = true);

public enum PatientPaymentType
{
    Insurance,
    SelfPay
}

public sealed record PatientSource(
    string Id,
    string MedicalCode,
    string FullName,
    DateOnly? DateOfBirth,
    string Gender,
    string Address,
    PatientPaymentType PaymentType,
    string? InsuranceNumber,
    bool IsActive = true);

public sealed record CatalogSnapshot(
    string Id,
    DateTimeOffset ImportedAt,
    string SourceProfile,
    string QueryVersion,
    IReadOnlyDictionary<string, int> RecordCounts,
    IReadOnlyList<string> ValidationErrors)
{
    public bool IsValid => ValidationErrors.Count == 0;
}

public static class CatalogRules
{
    public static IReadOnlyList<string> ValidateDepartments(IEnumerable<Department> departments)
    {
        var list = departments.ToList();
        return DuplicateErrors(list.Select(x => (x.Code, x.Name)), "khoa/phòng").ToList();
    }

    public static IReadOnlyList<string> ValidateWarehouses(IEnumerable<Warehouse> warehouses)
    {
        var list = warehouses.ToList();
        var errors = DuplicateErrors(list.Select(x => (x.Code, x.Name)), "kho").ToList();
        errors.AddRange(list.Where(x => string.IsNullOrWhiteSpace(x.DepartmentId) && x.IsActive)
            .Select(x => $"Kho '{x.Code}' chưa có khoa/phòng quản lý."));
        return errors;
    }

    public static IReadOnlyList<string> ValidateServices(
        IEnumerable<ClinicalService> services,
        IEnumerable<ServiceGroup> groups)
    {
        var groupIds = groups.Where(x => x.IsActive).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = DuplicateErrors(services.Select(x => (x.Code, x.Name)), "dịch vụ").ToList();
        errors.AddRange(services.Where(x => x.IsActive && !groupIds.Contains(x.ServiceGroupId))
            .Select(x => $"Dịch vụ '{x.Code}' tham chiếu nhóm dịch vụ không tồn tại hoặc đã ngừng hoạt động."));
        return errors;
    }

    public static IReadOnlyList<string> ValidateInventory(
        IEnumerable<InventoryItem> items,
        IEnumerable<Warehouse> warehouses)
    {
        var warehouseIds = warehouses.Where(x => x.IsActive).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = items.Where(x => x.IsActive && !warehouseIds.Contains(x.WarehouseId))
            .Select(x => $"Mặt hàng '{x.Code}' tham chiếu kho không tồn tại hoặc đã ngừng hoạt động.")
            .ToList();
        errors.AddRange(items.Where(x => x.IsActive && x.QuantityOnHand < 0)
            .Select(x => $"Mặt hàng '{x.Code}' có tồn kho âm."));
        return errors;
    }

    private static IEnumerable<string> DuplicateErrors(
        IEnumerable<(string Code, string Name)> values,
        string entityName)
    {
        return values.Where(x => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name))
            .Select(_ => $"Danh mục {entityName} có mã hoặc tên trống.")
            .Concat(values.Where(x => !string.IsNullOrWhiteSpace(x.Code))
                .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1)
                .Select(x => $"Danh mục {entityName} bị trùng mã '{x.Key}'."));
    }
}
