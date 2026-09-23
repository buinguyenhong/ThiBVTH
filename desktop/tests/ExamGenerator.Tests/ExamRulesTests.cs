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
    public void Batch_allows_multiple_warehouses_per_department_and_requires_at_least_one()
    {
        var errors = ExamBatchRules.ValidateWarehouseMappings(
            new[]
            {
                new DepartmentWarehouseMapping("NOI", "KHO-01"),
                new DepartmentWarehouseMapping("NOI", "KHO-02"),
                new DepartmentWarehouseMapping("NHI", "KHO-03")
            },
            new[] { "NOI", "NHI", "SAN" });

        // NOI has 2 warehouses, NHI has 1 => both are valid
        Assert.DoesNotContain(errors, error => error.Contains("NOI", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(errors, error => error.Contains("NHI", StringComparison.OrdinalIgnoreCase));
        // SAN has 0 warehouses => invalid
        Assert.Contains(errors, error => error.Contains("SAN", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Candidate_must_select_warehouse_mapped_to_department()
    {
        var mappings = new[]
        {
            new DepartmentWarehouseMapping("NOI", "KHO-01"),
            new DepartmentWarehouseMapping("NOI", "KHO-02")
        };

        var validCandidates = new (string CandidateName, string DepartmentCode, string? WarehouseCode)[]
        {
            ("TS-01", "NOI", "KHO-01"),
            ("TS-02", "NOI", "KHO-02")
        };
        var validErrors = ExamBatchRules.ValidateCandidateWarehouseSelections(validCandidates, mappings);
        Assert.Empty(validErrors);

        var invalidCandidates = new (string CandidateName, string DepartmentCode, string? WarehouseCode)[]
        {
            ("TS-03", "NOI", "KHO-KHAC"),
            ("TS-04", "NOI", "")
        };
        var invalidErrors = ExamBatchRules.ValidateCandidateWarehouseSelections(invalidCandidates, mappings);
        Assert.Equal(2, invalidErrors.Count);
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

    [Fact]
    public void System_action_catalog_contains_standard_his_actions()
    {
        var actions = SystemActionCatalog.All;
        Assert.NotEmpty(actions);
        Assert.Contains(actions, a => a.Code == "TN_TIEP_NHAN");
        Assert.Contains(actions, a => a.Code == "NT_NHAN_BENH_KHOA");
        Assert.Contains(actions, a => a.Code == "YL_CHI_DINH_CLS");
        Assert.Contains(actions, a => a.Code == "YL_CHI_DINH_THUOC_VTYT");
        Assert.Contains(actions, a => a.Code == "TK_KIEM_TON_KHO");
        Assert.Contains(actions, a => a.Code == "CK_CHUYEN_KHOA");
        Assert.Contains(actions, a => a.Code == "RV_CHO_RA_VIEN");
    }
}
