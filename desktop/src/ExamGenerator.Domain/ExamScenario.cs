namespace ExamGenerator.Domain;

public sealed record ScenarioService(
    string Code,
    string Name,
    string GroupName);

public sealed record ScenarioDrug(
    string Code,
    string Name,
    string Unit,
    int Quantity,
    string UsageInstructions,
    string WarehouseCode,
    string WarehouseName,
    string FundingSource);

public sealed record ScenarioServiceChange(
    ScenarioService CanceledService,
    ScenarioService NewService,
    ScenarioService? AddedService = null);

public sealed record ScenarioDrugReturn(
    ScenarioDrug ReturnedDrug,
    int ReturnQuantity,
    string Reason);

public sealed record ScenarioPatient(
    string MedicalCode,
    string FullName,
    DateOnly? DateOfBirth,
    int Age,
    string Gender,
    string Address,
    string? InsuranceNumber,
    InsurancePeriod? InsurancePeriod,
    string InitialRegistrationCode,
    string Diagnosis,
    PatientPaymentType PaymentType)
{
    private readonly string? _insuranceNumber = NormalizeInsuranceNumber(InsuranceNumber);

    public string? InsuranceNumber
    {
        get => _insuranceNumber;
        init => _insuranceNumber = NormalizeInsuranceNumber(value);
    }

    public static string? NormalizeInsuranceNumber(string? insuranceNumber)
    {
        if (string.IsNullOrWhiteSpace(insuranceNumber)) return insuranceNumber;
        var trimmed = insuranceNumber.Trim();
        return trimmed.Length > 15 ? trimmed[..15] : trimmed;
    }
}

public sealed record ScenarioQuestion(
    int OrderIndex,
    string ActionCode,
    string Title,
    string Instruction,
    double Score,
    string? DetailHtmlOrText = null);

public sealed record ExamScenario(
    string ScenarioId,
    string BatchName,
    DateOnly ExamDate,
    string CandidateId,
    string CandidateName,
    string DepartmentCode,
    string DepartmentName,
    string TemplateId,
    string TemplateName,
    string HisUserCode,
    string HisUserName,
    ScenarioPatient Patient,
    IReadOnlyList<ScenarioQuestion> Questions,
    IReadOnlyList<ScenarioService> OrderedServices,
    IReadOnlyList<ScenarioDrug> OrderedDrugs,
    ScenarioServiceChange? ServiceChange,
    ScenarioDrugReturn? DrugReturn,
    bool RequiresDirectReception,
    bool RequiresExamSetupSql,
    int ExamDurationMinutes = 30,
    ScenarioPatient? SecondPatient = null,
    DateOnly? CutoffDate = null,
    ScenarioDrug? InventoryCheckDrug = null,
    string? TargetTransferDepartment = null,
    InpatientCase? InpatientLookupCase = null,
    OutpatientCase? OutpatientLookupCase = null,
    InpatientCase? AdvancePaymentCase1 = null,
    InpatientCase? AdvancePaymentCase2 = null,
    InpatientCase? DischargeCase = null,
    OutpatientCase? AdvancePaymentOutpatientCase = null,
    InventoryCheckPeriodItem? InventoryCheckPeriod = null)
{
    public double TotalScore => Questions.Sum(q => q.Score);

    public IReadOnlyList<(string MedicalCode, string FullName)> GetActuallyUsedPatients()
    {
        var result = new List<(string MedicalCode, string FullName)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfValid(string? code, string? name)
        {
            if (!string.IsNullOrWhiteSpace(code) && seen.Add(code.Trim()))
            {
                result.Add((code.Trim(), string.IsNullOrWhiteSpace(name) ? code.Trim() : name.Trim()));
            }
        }

        bool HasAction(params string[] prefixes) =>
            Questions.Any(q => prefixes.Any(p => q.ActionCode.StartsWith(p, StringComparison.OrdinalIgnoreCase)));

        if (HasAction("TN_", "NT_NHAN_BENH_KHOA", "LT_BAN_PHIEU_KHAM", "KB_", "TT_TUONG_TRINH_PTTT", "YL_"))
        {
            AddIfValid(Patient.MedicalCode, Patient.FullName);
        }

        if (SecondPatient is not null && HasAction("TN_", "NT_NHAN_BENH_KHOA", "LT_BAN_PHIEU_KHAM", "LT_THU_PHI_VA_CLS", "LT_DOI_DICH_VU_CLS", "KB_BENH_AN_NGOAI_TRU"))
        {
            AddIfValid(SecondPatient.MedicalCode, SecondPatient.FullName);
        }

        if (InpatientLookupCase is not null && HasAction("LT_TRA_CUU_THONG_TIN", "HC_TRA_CUU_THANH_TOAN"))
        {
            AddIfValid(InpatientLookupCase.MedicalCode, InpatientLookupCase.FullName);
        }

        if (OutpatientLookupCase is not null && HasAction("LT_TRA_CUU_THONG_TIN", "TC_TRA_CUU_BENH_SU"))
        {
            AddIfValid(OutpatientLookupCase.MedicalCode, OutpatientLookupCase.FullName);
        }

        if (AdvancePaymentCase1 is not null && HasAction("LT_THU_TAM_UNG_NOI_TRU", "TC_THU_TAM_UNG", "KB_CHI_DINH_NOI_TRU"))
        {
            AddIfValid(AdvancePaymentCase1.MedicalCode, AdvancePaymentCase1.FullName);
        }

        if (HasAction("LT_THU_TAM_UNG_NOI_TRU"))
        {
            if (AdvancePaymentOutpatientCase is not null)
            {
                AddIfValid(AdvancePaymentOutpatientCase.MedicalCode, AdvancePaymentOutpatientCase.FullName);
            }
            else if (AdvancePaymentCase2 is not null)
            {
                AddIfValid(AdvancePaymentCase2.MedicalCode, AdvancePaymentCase2.FullName);
            }
        }

        if (DischargeCase is not null && HasAction("LT_THANH_TOAN_RA_VIEN", "TC_THANH_TOAN_RA_VIEN"))
        {
            AddIfValid(DischargeCase.MedicalCode, DischargeCase.FullName);
        }

        return result;
    }
}
