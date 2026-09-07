using ExamGenerator.Application;
using ExamGenerator.Domain;

namespace ExamGenerator.Tests;

public class ExamRulesTests
{
    [Fact]
    public void Te1_period_covers_five_calendar_years()
    {
        var period = InsurancePeriod.ForExamYear(2026, isTe1: true);

        Assert.Equal(new DateOnly(2026, 1, 1), period.From);
        Assert.Equal(new DateOnly(2030, 12, 31), period.To);
    }

    [Fact]
    public void Regular_insurance_period_covers_exam_year_only()
    {
        var period = InsurancePeriod.ForExamYear(2026, isTe1: false);

        Assert.Equal(new DateOnly(2026, 1, 1), period.From);
        Assert.Equal(new DateOnly(2026, 12, 31), period.To);
    }

    [Fact]
    public void Batch_rejects_duplicate_his_users()
    {
        var errors = ExamBatchRules.ValidateUserAssignments(new[]
        {
            new CandidateAssignment("A", "NOI", "user01"),
            new CandidateAssignment("B", "NOI", "USER01")
        });

        Assert.Contains(errors, error => error.Contains("user01", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Batch_requires_exactly_one_warehouse_per_enabled_department()
    {
        var errors = ExamBatchRules.ValidateWarehouseMappings(
            new[]
            {
                new DepartmentWarehouseMapping("NOI", "KHO-01"),
                new DepartmentWarehouseMapping("NOI", "KHO-02"),
                new DepartmentWarehouseMapping("NHI", "KHO-03")
            },
            new[] { "NOI", "NHI", "SAN" });

        Assert.Contains(errors, error => error.Contains("NOI", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("SAN", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Direct_reception_template_does_not_require_setup_sql()
    {
        var direct = new ExamTemplate("NHI-DIRECT", "Nhi", "NHI", "DIEU-DUONG", ReceptionMode.DirectReception);
        var prepared = new ExamTemplate("NOI-WARD", "Nội trú", "NOI", "DIEU-DUONG", ReceptionMode.WardAdmissionPreparation);

        Assert.True(direct.RequiresDirectReception);
        Assert.False(direct.RequiresExamSetupSql);
        Assert.False(prepared.RequiresDirectReception);
        Assert.True(prepared.RequiresExamSetupSql);
    }

    [Fact]
    public void Application_service_combines_preflight_rules()
    {
        var template = new ExamTemplate("NOI-WARD", "Nội trú", "NOI", "DIEU-DUONG", ReceptionMode.WardAdmissionPreparation);
        var result = new ExamBatchService().Validate(
            template,
            new[] { new DepartmentWarehouseMapping("NOI", "KHO-01") },
            new[] { "NOI" },
            new[] { new CandidateAssignment("A", "NOI", "user01") });

        Assert.True(result.IsValid);
    }
}
