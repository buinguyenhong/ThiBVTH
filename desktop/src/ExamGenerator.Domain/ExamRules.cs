namespace ExamGenerator.Domain;

public enum ReceptionMode
{
    DirectReception,
    WardAdmissionPreparation
}

public sealed record InsurancePeriod(DateOnly From, DateOnly To)
{
    public static InsurancePeriod ForExamYear(int examYear, bool isTe1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(examYear, 2000);

        var endYear = isTe1 ? examYear + 4 : examYear;
        return new InsurancePeriod(
            new DateOnly(examYear, 1, 1),
            new DateOnly(endYear, 12, 31));
    }
}

public sealed record ExamTemplate(
    string Id,
    string Name,
    string DepartmentCode,
    string RoleCode,
    ReceptionMode ReceptionMode)
{
    public bool RequiresDirectReception => ReceptionMode == ReceptionMode.DirectReception;
    public bool RequiresExamSetupSql => ReceptionMode == ReceptionMode.WardAdmissionPreparation;
}

public sealed record DepartmentWarehouseMapping(string DepartmentCode, string WarehouseCode);

public sealed record CandidateAssignment(
    string CandidateId,
    string DepartmentCode,
    string HisUserCode);

public static class ExamBatchRules
{
    public static IReadOnlyList<string> ValidateTemplate(ExamTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(template.Id)) errors.Add("Template phải có mã.");
        if (string.IsNullOrWhiteSpace(template.DepartmentCode)) errors.Add("Template phải có khoa/phòng.");
        if (string.IsNullOrWhiteSpace(template.RoleCode)) errors.Add("Template phải có vị trí.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateWarehouseMappings(
        IEnumerable<DepartmentWarehouseMapping> mappings,
        IEnumerable<string> enabledDepartments)
    {
        var mappingList = mappings.ToList();
        var errors = new List<string>();

        foreach (var department in enabledDepartments.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var warehouses = mappingList
                .Where(x => string.Equals(x.DepartmentCode, department, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.WarehouseCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (warehouses.Count == 0)
                errors.Add($"Khoa '{department}' chưa được map kho thi.");
            else if (warehouses.Count > 1)
                errors.Add($"Khoa '{department}' chỉ được map đúng một kho thi.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateUserAssignments(
        IEnumerable<CandidateAssignment> assignments)
    {
        var list = assignments.ToList();
        var errors = new List<string>();

        var duplicateUsers = list
            .Where(x => !string.IsNullOrWhiteSpace(x.HisUserCode))
            .GroupBy(x => x.HisUserCode, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key);

        foreach (var user in duplicateUsers)
            errors.Add($"User HIS '{user}' bị cấp trùng trong cùng đợt.");

        foreach (var assignment in list.Where(x => string.IsNullOrWhiteSpace(x.HisUserCode)))
            errors.Add($"Thí sinh '{assignment.CandidateId}' chưa được cấp user HIS.");

        return errors;
    }
}
