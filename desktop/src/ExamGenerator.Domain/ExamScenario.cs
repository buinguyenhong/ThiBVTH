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
    PatientPaymentType PaymentType);

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
    string? TargetTransferDepartment = null)
{
    public double TotalScore => Questions.Sum(q => q.Score);
}
