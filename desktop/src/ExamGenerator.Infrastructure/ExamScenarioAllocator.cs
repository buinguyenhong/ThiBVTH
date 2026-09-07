using ExamGenerator.Domain;

namespace ExamGenerator.Infrastructure;

public sealed record CandidateInput(string Name, string Id, string Department, string Template);

public sealed class ExamScenarioAllocator
{
    private readonly SqliteDatabase _database;

    public ExamScenarioAllocator(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<ExamScenario>> AllocateBatchScenariosAsync(
        string batchName,
        DateOnly examDate,
        IReadOnlyList<CandidateInput> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0) return Array.Empty<ExamScenario>();

        var departmentConfigs = await _database.GetDepartmentConfigurationsAsync(cancellationToken);
        var configMap = departmentConfigs.ToDictionary(x => x.DepartmentName, x => x, StringComparer.OrdinalIgnoreCase);

        var templates = await _database.GetTemplatesAsync(cancellationToken);
        var templateMap = templates.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);

        // Pre-fetch available patients from SQLite snapshot
        var availablePatients = (await _database.GetAvailablePatientsAsync(cancellationToken)).ToList();
        if (availablePatients.Count == 0)
        {
            throw new InvalidOperationException("Chưa có hồ sơ Bệnh nhân nào trong CSDL (hoặc toàn bộ bệnh nhân đã thi). Vui lòng vào phân hệ 'Cài đặt & Danh mục HIS' và bấm 'Đồng bộ Catalog HIS'.");
        }
        var patientIndex = 0;

        // Group candidates by department to allocate distinct HIS users
        var candidatesByDept = candidates.GroupBy(c => c.Department, StringComparer.OrdinalIgnoreCase);
        var scenarios = new List<ExamScenario>();

        foreach (var group in candidatesByDept)
        {
            var deptName = group.Key;
            if (!configMap.TryGetValue(deptName, out var deptConfig))
            {
                throw new InvalidOperationException($"Khoa '{deptName}' chưa được cấu hình một kho thi duy nhất tại phân hệ 'Phân quyền Dịch vụ & Kho'.");
            }

            // Get available users for this department from SQLite snapshot
            var deptUsers = await _database.GetDepartmentUsersAsync(deptName, cancellationToken);
            if (deptUsers.Count == 0)
            {
                throw new InvalidOperationException($"Khoa '{deptName}' chưa có tài khoản User HIS nào được đồng bộ từ HIS. Vui lòng đồng bộ danh mục User trước khi sinh đề.");
            }
            if (deptUsers.Count < group.Count())
            {
                throw new InvalidOperationException($"Khoa '{deptName}' có {group.Count()} thí sinh nhưng chỉ có {deptUsers.Count} tài khoản HIS khả dụng. Vui lòng bổ sung User HIS vào danh mục trước khi sinh đề.");
            }

            // Get warehouse drugs from SQLite snapshot
            var warehouseDrugs = await _database.GetWarehouseDrugsAsync(deptConfig.WarehouseCode, cancellationToken);
            if (warehouseDrugs.Count == 0)
            {
                throw new InvalidOperationException($"Kho thi '{deptConfig.WarehouseName}' của khoa '{deptName}' chưa có thuốc/vật tư tồn kho khả dụng được đồng bộ từ HIS.");
            }

            // Get clinical services from SQLite snapshot
            var allServices = await _database.GetCatalogItemsAsync("Services", cancellationToken);
            if (allServices.Count == 0)
            {
                throw new InvalidOperationException("Chưa có danh mục Dịch vụ kỹ thuật / CLS nào được đồng bộ từ HIS. Vui lòng đồng bộ danh mục trước khi sinh đề.");
            }

            var userIndex = 0;
            foreach (var candidate in group)
            {
                var assignedUser = deptUsers[userIndex++];

                if (!templateMap.TryGetValue(candidate.Template, out var tpl))
                {
                    tpl = templates.FirstOrDefault(t => t.Department.Equals(deptName, StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrEmpty(tpl.Id))
                    {
                        tpl = templates.First();
                    }
                }

                var actions = await _database.GetTemplateActionDetailsAsync(tpl.Id, cancellationToken);
                var isDirectReception = tpl.ReceptionMode.Contains("DirectReception", StringComparison.OrdinalIgnoreCase);
                var isWardAdmission = tpl.ReceptionMode.Contains("WardAdmissionPreparation", StringComparison.OrdinalIgnoreCase);

                // Allocate patient from synced snapshot
                ScenarioPatient patient;
                if (patientIndex < availablePatients.Count)
                {
                    var pRow = availablePatients[patientIndex++];
                    var isTe1 = pRow.InsuranceNumber?.StartsWith("TE1", StringComparison.OrdinalIgnoreCase) == true;
                    var insurancePeriod = InsurancePeriod.ForExamYear(examDate.Year, isTe1);
                    var age = 35;
                    DateOnly? birthDate = null;
                    if (DateOnly.TryParse(pRow.DateOfBirth, out var parsedBirth))
                    {
                        birthDate = parsedBirth;
                        age = examDate.Year - parsedBirth.Year;
                    }

                    patient = new ScenarioPatient(
                        pRow.MedicalCode,
                        pRow.FullName,
                        birthDate,
                        age,
                        pRow.Gender,
                        pRow.Address,
                        pRow.InsuranceNumber,
                        insurancePeriod,
                        "66232",
                        pRow.Diagnosis,
                        pRow.PaymentType.Equals("BHYT", StringComparison.OrdinalIgnoreCase) ? PatientPaymentType.Insurance : PatientPaymentType.SelfPay
                    );
                }
                else
                {
                    // If patient pool is exhausted
                    throw new InvalidOperationException($"Số lượng hồ sơ Bệnh nhân trong snapshot không đủ cho đợt thi ({availablePatients.Count} bệnh nhân). Vui lòng đồng bộ thêm từ HIS.");
                }

                // Allocate Clinical Services from synced snapshot
                var orderedServices = allServices.Take(3).Select(s => new ScenarioService(s.Code, s.Name, "Cận lâm sàng")).ToList();
                ScenarioServiceChange? serviceChange = null;
                if (orderedServices.Count > 0 && allServices.Count > 3)
                {
                    var extraService = allServices.Skip(3).First();
                    serviceChange = new ScenarioServiceChange(orderedServices[0], new ScenarioService(extraService.Code, extraService.Name, "Cận lâm sàng"));
                }

                // Allocate Drugs from mapped warehouse snapshot
                var orderedDrugs = new List<ScenarioDrug>();
                for (var dIdx = 0; dIdx < Math.Min(3, warehouseDrugs.Count); dIdx++)
                {
                    var d = warehouseDrugs[dIdx];
                    var qty = (dIdx + 1) * 2;
                    var usage = d.Unit.Contains("Chai", StringComparison.OrdinalIgnoreCase) 
                        ? "Truyền tĩnh mạch XL giọt/phút" 
                        : (d.Unit.Contains("Lọ", StringComparison.OrdinalIgnoreCase) || d.Unit.Contains("Ống", StringComparison.OrdinalIgnoreCase)
                            ? "Tiêm bắp 01 ống lúc 08h00"
                            : "Sáng 1 viên, chiều 1 viên sau ăn");
                    orderedDrugs.Add(new ScenarioDrug(d.DrugCode, d.DrugName, d.Unit, qty, usage, d.WarehouseCode, d.WarehouseName, d.FundingSource));
                }

                ScenarioDrugReturn? drugReturn = null;
                if (orderedDrugs.Count > 0)
                {
                    drugReturn = new ScenarioDrugReturn(orderedDrugs[0], 1, "Người bệnh xuất hiện phản ứng phụ nhẹ / đổi thuốc theo y lệnh bác sĩ");
                }

                // Build Questions
                var questions = new List<ScenarioQuestion>();
                for (var aIdx = 0; aIdx < actions.Count; aIdx++)
                {
                    var act = actions[aIdx];
                    var instruction = InstructionForAction(act.Code, act.Name, isDirectReception && aIdx == 0);
                    questions.Add(new ScenarioQuestion(aIdx + 1, act.Code, act.Name, instruction, act.Score));
                }

                var scenario = new ExamScenario(
                    Guid.NewGuid().ToString("N"),
                    batchName,
                    examDate,
                    candidate.Id,
                    candidate.Name,
                    deptConfig.DepartmentCode,
                    deptConfig.DepartmentName,
                    tpl.Id,
                    tpl.Name,
                    assignedUser.UserName,
                    assignedUser.FullName,
                    patient,
                    questions,
                    orderedServices,
                    orderedDrugs,
                    serviceChange,
                    drugReturn,
                    isDirectReception,
                    isWardAdmission
                );

                scenarios.Add(scenario);
            }
        }

        return scenarios;
    }

    private static string InstructionForAction(string code, string name, bool isDirectReception) => code switch
    {
        _ when isDirectReception => "Mở phân hệ Tiếp nhận trên phần mềm HIS, thực hiện nhập đầy đủ thông tin hành chính, số thẻ BHYT và chẩn đoán của người bệnh theo bảng thông tin trên.",
        "NT_NHAN_BENH_KHOA" => "Mở danh sách người bệnh chờ vào khoa trên phần mềm HIS, tìm đúng người bệnh theo Mã Y tế/Họ tên và thực hiện thao tác nhận bệnh vào buồng điều trị.",
        "YL_CHI_DINH_CLS" => "Vào phiếu chỉ định dịch vụ kỹ thuật của người bệnh, tìm và chỉ định đầy đủ danh mục các dịch vụ cận lâm sàng theo bảng chi tiết bên dưới.",
        "YL_CHI_DINH_THUOC_VTYT" => "Vào phần kê đơn/y lệnh thuốc điều trị nội trú, chọn đúng tủ trực/kho thi đã được phân quyền và kê đơn theo danh mục thuốc/vật tư bên dưới.",
        "YL_DOI_THEM_DICH_VU" => "Thực hiện thao tác hủy 01 dịch vụ cận lâm sàng đã chỉ định sai sót và thực hiện chỉ định thay thế sang dịch vụ kỹ thuật mới theo yêu cầu.",
        "YL_TRA_THUOC" => "Thực hiện nghiệp vụ lập phiếu hoàn trả thuốc thừa về tủ trực/kho dược theo đúng mặt hàng và số lượng quy định.",
        "TK_KIEM_TON_KHO" => "Mở phân hệ Quản lý dược/tồn kho, tra cứu số lượng tồn thực tế của thuốc/vật tư tại tủ trực khoa và ghi nhận số lượng tồn vào bài làm.",
        "CK_CHUYEN_KHOA" => "Thực hiện thủ tục chuyển khoa điều trị cho người bệnh sang khoa chuyên môn tiếp theo trên phần mềm HIS.",
        "RV_CHO_RA_VIEN" => "Kiểm tra toàn bộ chi phí khám chữa bệnh của người bệnh và thực hiện thao tác in bảng kê chi phí / duyệt cho người bệnh xuất viện.",
        _ => $"Thực hiện nghiệp vụ chuyên môn '{name}' trên phần mềm HIS theo quy định của Bệnh viện Đa khoa Thiện Hạnh."
    };
}
