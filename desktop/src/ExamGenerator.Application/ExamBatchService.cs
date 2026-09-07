using ExamGenerator.Domain;

namespace ExamGenerator.Application;

public sealed record ExamBatchValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class ExamBatchService
{
    public ExamBatchValidationResult Validate(
        ExamTemplate template,
        IEnumerable<DepartmentWarehouseMapping> warehouseMappings,
        IEnumerable<string> enabledDepartments,
        IEnumerable<CandidateAssignment> assignments)
    {
        var errors = new List<string>();
        errors.AddRange(ExamBatchRules.ValidateTemplate(template));
        errors.AddRange(ExamBatchRules.ValidateWarehouseMappings(warehouseMappings, enabledDepartments));
        errors.AddRange(ExamBatchRules.ValidateUserAssignments(assignments));
        return new ExamBatchValidationResult(errors);
    }
}
