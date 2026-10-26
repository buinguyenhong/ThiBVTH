using ExamGenerator.Domain;

namespace ExamGenerator.Infrastructure;

public sealed record CandidateInput(string Name, string Id, string Department, string Template, int DurationMinutes = 30, string? WarehouseCode = null, string? WarehouseName = null);

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

        var allMappings = await _database.GetDepartmentWarehouseMappingsAsync(cancellationToken);
        var mappingsByDeptName = allMappings
            .GroupBy(x => x.DepartmentName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var templates = await _database.GetTemplatesAsync(cancellationToken);
        var templateMap = new Dictionary<string, (string Id, string Name, string Department, string Position, int QuestionCount, double TotalScore, string ReceptionMode)>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in templates)
        {
            templateMap[t.Id] = t;
            templateMap[t.Name] = t;
        }

        // Pre-fetch available patients from SQLite snapshot
        var availablePatients = (await _database.GetAvailablePatientsAsync(cancellationToken)).ToList();
        if (availablePatients.Count == 0)
        {
            throw new InvalidOperationException("Chưa có hồ sơ Bệnh nhân nào trong CSDL (hoặc toàn bộ bệnh nhân đã thi). Vui lòng vào phân hệ 'Cài đặt & Danh mục HIS' và bấm 'Đồng bộ Catalog HIS'.");
        }

        var allInpatients = (await _database.GetAvailableInpatientsAsync(cancellationToken: cancellationToken)).ToList();
        var allOutpatients = (await _database.GetAvailableOutpatientsAsync(cancellationToken: cancellationToken)).ToList();
        var allInventoryPeriods = (await _database.GetSnapshotInventoryPeriodsAsync(cancellationToken: cancellationToken)).ToList();

        // Cache warehouse drugs and remaining stock across candidates
        var warehouseDrugsCache = new Dictionary<string, IReadOnlyList<SnapshotDrugRow>>(StringComparer.OrdinalIgnoreCase);
        var warehouseStockMap = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

        // Group candidates by department to allocate distinct HIS users
        var candidatesByDept = candidates.GroupBy(c => c.Department, StringComparer.OrdinalIgnoreCase);
        var scenarios = new List<ExamScenario>();

        foreach (var group in candidatesByDept)
        {
            var deptName = group.Key;
            if (!mappingsByDeptName.TryGetValue(deptName, out var deptMappings) || deptMappings.Count == 0)
            {
                throw new InvalidOperationException($"Khoa '{deptName}' chưa được cấu hình kho thi nào tại phân hệ 'Phân quyền Dịch vụ & Kho'.");
            }

            var deptCode = deptMappings[0].DepartmentCode;

            // Get mapped users for this department from Tab 3 configuration
            var deptUsers = await _database.GetMappedUsersForDepartmentAsync(deptCode, cancellationToken);
            if (deptUsers.Count == 0 && !string.IsNullOrWhiteSpace(deptName))
            {
                deptUsers = await _database.GetMappedUsersForDepartmentAsync(deptName, cancellationToken);
            }

            if (deptUsers.Count == 0)
            {
                throw new InvalidOperationException($"Khoa '{deptName}' chưa được cấu hình danh sách User HIS thi trong Tab 3. Vui lòng vào Tab 3 để chọn user.");
            }
            if (deptUsers.Count < group.Count())
            {
                throw new InvalidOperationException($"Khoa '{deptName}' có {group.Count()} thí sinh nhưng chỉ cấu hình {deptUsers.Count} User HIS khả dụng. Không đủ số lượng user để tạo đề.");
            }

            // Get clinical services from SQLite snapshot with group name
            var allServicesWithGroup = (await _database.GetCatalogServicesWithGroupAsync(cancellationToken)).ToList();
            if (allServicesWithGroup.Count == 0)
            {
                var fallback = await _database.GetCatalogItemsAsync("Services", cancellationToken);
                allServicesWithGroup = fallback.Select(x => (x.Code, x.Name, "Cận lâm sàng")).ToList();
            }
            if (allServicesWithGroup.Count == 0)
            {
                throw new InvalidOperationException("Chưa có danh mục Dịch vụ kỹ thuật / CLS nào được đồng bộ từ HIS. Vui lòng đồng bộ danh mục trước khi sinh đề.");
            }

            // Find target department for Question 7 transfer
            var otherDept = mappingsByDeptName.Keys.FirstOrDefault(k => !k.Equals(deptName, StringComparison.OrdinalIgnoreCase));
            var targetTransferDept = otherDept ?? (deptName.Contains("Nội", StringComparison.OrdinalIgnoreCase) ? "Khoa Ngoại tổng hợp" : "Khoa Nội");

            // Cutoff date for Q7 & Q8: default 10 days before examDate
            var cutoffDate = examDate.AddDays(-10);

            var actionDefs = await _database.GetActionDefinitionsAsync(cancellationToken);
            var actionDescMap = actionDefs
                .Where(a => !string.IsNullOrWhiteSpace(a.Description))
                .ToDictionary(a => a.Code, a => a.Description, StringComparer.OrdinalIgnoreCase);

            var userIndex = 0;
            var seed = Math.Abs(examDate.DayNumber ^ GetDeterministicHashCode(deptName));
            var rng = new Random(seed);
            // Randomly shuffle mapped users so candidates receive random distinct users
            var shuffledUsers = deptUsers.OrderBy(_ => rng.Next()).ToList();

            foreach (var candidate in group)
            {
                var assignedUser = shuffledUsers[userIndex++];

                // Resolve candidate's warehouse
                (string WarehouseCode, string WarehouseName) targetWh;
                if (!string.IsNullOrWhiteSpace(candidate.WarehouseCode))
                {
                    var matchedWh = deptMappings.FirstOrDefault(m => m.WarehouseCode.Equals(candidate.WarehouseCode, StringComparison.OrdinalIgnoreCase));
                    targetWh = matchedWh.WarehouseCode != null
                        ? (matchedWh.WarehouseCode, matchedWh.WarehouseName)
                        : (candidate.WarehouseCode, candidate.WarehouseName ?? candidate.WarehouseCode);
                }
                else
                {
                    targetWh = (deptMappings[0].WarehouseCode, deptMappings[0].WarehouseName);
                }

                // Load drugs for candidate's warehouse if not cached
                if (!warehouseDrugsCache.TryGetValue(targetWh.WarehouseCode, out var warehouseDrugs))
                {
                    var loadedDrugs = await _database.GetWarehouseDrugsAsync(targetWh.WarehouseCode, cancellationToken);
                    if (loadedDrugs.Count == 0)
                    {
                        throw new InvalidOperationException($"Kho thi '{targetWh.WarehouseName}' của khoa '{deptName}' chưa có thuốc/vật tư tồn kho khả dụng được đồng bộ từ HIS.");
                    }
                    var inStock = loadedDrugs.Where(d => d.QuantityOnHand >= 1).ToList();
                    if (inStock.Count == 0)
                    {
                        throw new InvalidOperationException($"Kho thi '{targetWh.WarehouseName}' của khoa '{deptName}' không có thuốc/vật tư nào còn tồn kho > 0.");
                    }

                    warehouseDrugs = loadedDrugs;
                    warehouseDrugsCache[targetWh.WarehouseCode] = warehouseDrugs;
                    warehouseStockMap[targetWh.WarehouseCode] = inStock.ToDictionary(d => d.DrugCode, d => d.QuantityOnHand, StringComparer.OrdinalIgnoreCase);
                }

                var inStockCandidateDrugs = warehouseDrugs.Where(d => d.QuantityOnHand >= 1).ToList();
                var remainingStock = warehouseStockMap[targetWh.WarehouseCode];

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

                var isGmhs = deptName.Contains("Gây mê", StringComparison.OrdinalIgnoreCase) || tpl.Id.Equals("tpl-ktv-gmhs", StringComparison.OrdinalIgnoreCase);

                // Allocate 2 patients from synced snapshot (1 BHYT, 1 Viện phí)
                var bhytRow = availablePatients.FirstOrDefault(p => p.PaymentType.Equals("BHYT", StringComparison.OrdinalIgnoreCase));
                if (bhytRow != null)
                {
                    availablePatients.Remove(bhytRow);
                }
                else if (availablePatients.Count > 0)
                {
                    bhytRow = availablePatients[0];
                    availablePatients.RemoveAt(0);
                }
                else
                {
                    throw new InvalidOperationException($"Số lượng hồ sơ Bệnh nhân trong snapshot không đủ cho đợt thi. Vui lòng đồng bộ thêm từ HIS.");
                }

                var vpRow = availablePatients.FirstOrDefault(p => !p.PaymentType.Equals("BHYT", StringComparison.OrdinalIgnoreCase));
                if (vpRow != null)
                {
                    availablePatients.Remove(vpRow);
                }
                else if (availablePatients.Count > 0)
                {
                    vpRow = availablePatients[0];
                    availablePatients.RemoveAt(0);
                }
                else
                {
                    vpRow = new SnapshotPatientRow(bhytRow.PatientId + "_vp", bhytRow.MedicalCode + "1", bhytRow.FullName + " (VP)", bhytRow.DateOfBirth, bhytRow.Gender, bhytRow.Address, null, bhytRow.Diagnosis, "Viện phí");
                }

                var patient = MapPatientRow(bhytRow, examDate, PatientPaymentType.Insurance);
                var secondPatient = MapPatientRow(vpRow, examDate, PatientPaymentType.SelfPay);

                // For GMHS: Bệnh nhân đang nằm trong khoa, phẫu thuật được chọn nằm trong danh mục phẫu thuật đang hoạt động
                SnapshotInpatientRow? gmhsInpatientRow = null;
                if (isGmhs && allInpatients.Count > 0)
                {
                    gmhsInpatientRow = allInpatients.FirstOrDefault(ip => !string.IsNullOrWhiteSpace(ip.DepartmentName) && ip.DepartmentName.Contains(deptName, StringComparison.OrdinalIgnoreCase));
                    if (gmhsInpatientRow == null)
                    {
                        gmhsInpatientRow = allInpatients.FirstOrDefault(ip => !string.IsNullOrWhiteSpace(ip.DepartmentName) &&
                            (ip.DepartmentName.Contains("Ngoại", StringComparison.OrdinalIgnoreCase) ||
                             ip.DepartmentName.Contains("Sản", StringComparison.OrdinalIgnoreCase) ||
                             ip.DepartmentName.Contains("Chấn thương", StringComparison.OrdinalIgnoreCase)));
                    }
                    gmhsInpatientRow ??= allInpatients[rng.Next(allInpatients.Count)];
                }

                if (isGmhs && gmhsInpatientRow != null)
                {
                    var age = 35;
                    DateOnly? birthDate = null;
                    if (DateOnly.TryParse(gmhsInpatientRow.DateOfBirth, out var parsedBirth))
                    {
                        birthDate = parsedBirth;
                        age = examDate.Year - parsedBirth.Year;
                    }
                    patient = new ScenarioPatient(
                        gmhsInpatientRow.MedicalCode,
                        gmhsInpatientRow.FullName,
                        birthDate,
                        age,
                        gmhsInpatientRow.Gender,
                        gmhsInpatientRow.Address ?? "",
                        gmhsInpatientRow.InsuranceNumber,
                        InsurancePeriod.ForExamYear(examDate.Year, false),
                        "66232",
                        gmhsInpatientRow.Diagnosis ?? "Hậu phẫu",
                        string.IsNullOrWhiteSpace(gmhsInpatientRow.InsuranceNumber) ? PatientPaymentType.SelfPay : PatientPaymentType.Insurance
                    );
                }

                var gmhsAdmissionNumber = gmhsInpatientRow?.SoBenhAn ?? (isGmhs ? "22.000173" : patient.MedicalCode);

                // Phẫu thuật được chọn nằm trong danh mục phẫu thuật đang hoạt động
                var surgeryServices = allServicesWithGroup
                    .Where(s => s.GroupName.Contains("Phẫu thuật", StringComparison.OrdinalIgnoreCase)
                             || s.GroupName.Contains("PT-TT", StringComparison.OrdinalIgnoreCase)
                             || s.GroupName.Contains("PTTT", StringComparison.OrdinalIgnoreCase)
                             || s.GroupName.Contains("Thủ thuật", StringComparison.OrdinalIgnoreCase)
                             || s.Name.StartsWith("Phẫu thuật", StringComparison.OrdinalIgnoreCase)
                             || s.Name.Contains("Cắt ", StringComparison.OrdinalIgnoreCase)
                             || s.Name.Contains("Mổ ", StringComparison.OrdinalIgnoreCase)
                             || s.Name.Contains("Nội soi ", StringComparison.OrdinalIgnoreCase)
                             || s.Name.Contains("Tạo hình ", StringComparison.OrdinalIgnoreCase)
                             || s.Name.Contains("Khâu ", StringComparison.OrdinalIgnoreCase)
                             || s.Name.Contains("Thay khớp", StringComparison.OrdinalIgnoreCase)
                             || s.Name.Contains("Kết hợp xương", StringComparison.OrdinalIgnoreCase)
                             || s.Name.Contains("Nạo ", StringComparison.OrdinalIgnoreCase)
                             || s.Name.Contains("Bóc u", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var gmhsSurgeryName = surgeryServices.Count > 0
                    ? surgeryServices[rng.Next(surgeryServices.Count)].Name
                    : "Phẫu thuật lấy thai lần đầu [gây tê]";

                // Allocate Clinical Services categorized by modality (Siêu âm, Xquang, CT/MRI, Xét nghiệm)
                var (orderedServices, q2Instruction, serviceChange, pickedSieuAm, pickedXquang) = AllocateClinicalServices(allServicesWithGroup, rng);

                // Check if template contains question for ordering drugs & supplies
                var hasDrugOrderAction = actions.Any(a => a.Code.Equals("YL_CHI_DINH_THUOC_VTYT", StringComparison.OrdinalIgnoreCase));

                IReadOnlyList<ScenarioDrug> orderedDrugs;
                ScenarioDrugReturn? drugReturn = null;

                if (hasDrugOrderAction)
                {
                    // Allocate Drugs: Standard 3 BH + 2 VP or GMHS surgical supply list, strictly checking remaining stock
                    orderedDrugs = AllocateCandidateDrugs(inStockCandidateDrugs, remainingStock, targetWh.WarehouseName, deptName, group.Count(), rng, isGmhs);

                    if (orderedDrugs.Count > 0 && !isGmhs)
                    {
                        var returnItem = orderedDrugs.Count > 1 ? orderedDrugs[1] : orderedDrugs[0];
                        drugReturn = new ScenarioDrugReturn(returnItem, 1, "Người bệnh xuất hiện phản ứng phụ nhẹ / đổi thuốc theo y lệnh bác sĩ");
                    }
                }
                else
                {
                    // Template does not order drugs (e.g. KTV XN, CDHA, Le tan, Thu ngan...): do not allocate drugs and do not consume warehouse stock
                    orderedDrugs = Array.Empty<ScenarioDrug>();
                    drugReturn = null;
                }

                // Inventory check period item for Q6 / TK_KIEM_TON_KHO (Pre-calculated fixed periods before March 2026)
                var deptPeriods = allInventoryPeriods
                    .Where(p => (!string.IsNullOrWhiteSpace(p.WarehouseCode) && p.WarehouseCode.Equals(targetWh.WarehouseCode, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(p.DepartmentCode) && p.DepartmentCode.Equals(deptCode, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (deptPeriods.Count == 0 && allInventoryPeriods.Count > 0)
                {
                    deptPeriods = allInventoryPeriods.ToList();
                }

                InventoryCheckPeriodItem? inventoryPeriodItem = null;
                ScenarioDrug inventoryCheckDrug;

                if (deptPeriods.Count > 0)
                {
                    var vpPeriods = deptPeriods.Where(p => p.FundingSource.Equals("Viện phí", StringComparison.OrdinalIgnoreCase)).ToList();
                    var pool = vpPeriods.Count > 0 ? vpPeriods : deptPeriods;
                    var picked = pool[rng.Next(pool.Count)];

                    DateOnly.TryParse(picked.FromDate.Length >= 10 ? picked.FromDate[..10] : picked.FromDate, out var pFrom);
                    DateOnly.TryParse(picked.ToDate.Length >= 10 ? picked.ToDate[..10] : picked.ToDate, out var pTo);
                    if (pFrom == default) pFrom = new DateOnly(2026, 2, 1);
                    if (pTo == default) pTo = new DateOnly(2026, 2, 28);

                    inventoryPeriodItem = new InventoryCheckPeriodItem(
                        picked.PeriodCode,
                        picked.PeriodName,
                        pFrom,
                        pTo,
                        picked.WarehouseCode,
                        picked.WarehouseName,
                        picked.DepartmentCode,
                        picked.DepartmentName,
                        picked.DrugCode,
                        picked.DrugName,
                        picked.Unit,
                        picked.ClosingStock,
                        picked.FundingSource
                    );

                    inventoryCheckDrug = new ScenarioDrug(
                        picked.DrugCode,
                        picked.DrugName,
                        picked.Unit,
                        0,
                        "",
                        picked.WarehouseCode,
                        picked.WarehouseName,
                        picked.FundingSource
                    );
                }
                else
                {
                    // Fallback to warehouse stock if periods not populated yet
                    var vpDrugsForCheck = warehouseDrugs.Where(d => d.FundingSource.Equals("Viện phí", StringComparison.OrdinalIgnoreCase)).ToList();
                    var invPool = vpDrugsForCheck.Count > 0 ? vpDrugsForCheck : warehouseDrugs;
                    var invDrugRow = invPool[rng.Next(invPool.Count)];
                    inventoryCheckDrug = new ScenarioDrug(invDrugRow.DrugCode, invDrugRow.DrugName, invDrugRow.Unit, 0, "", invDrugRow.WarehouseCode, invDrugRow.WarehouseName, invDrugRow.FundingSource);
                }

                // Inpatient and outpatient cases for lookup / advance payment / discharge
                InpatientCase? advanceCase1 = null;
                InpatientCase? advanceCase2 = null;
                InpatientCase? inLookupCase = null;
                InpatientCase? dischargeCase = null;
                OutpatientCase? outLookupCase = null;
                OutpatientCase? advanceOutpatientCase = null;

                if (allInpatients.Count > 0)
                {
                    var inPool = allInpatients.OrderBy(_ => rng.Next()).ToList();
                    advanceCase1 = MapInpatientRow(inPool[0 % inPool.Count]);
                    advanceCase2 = inPool.Count > 1 ? MapInpatientRow(inPool[1 % inPool.Count]) : advanceCase1;
                    var withBed = inPool.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.BedCode) && !string.IsNullOrWhiteSpace(c.RoomName));
                    inLookupCase = MapInpatientRow(withBed ?? inPool[2 % inPool.Count]);
                    dischargeCase = MapInpatientRow(inPool[3 % inPool.Count]);
                }
                else
                {
                    advanceCase1 = new InpatientCase("mock_ba1", "21.004865", "21004865", "Bệnh nhân Tạm Ứng 1", "Nam", "1980", "Buôn Ma Thuột", deptCode, deptName, "P101", "Phòng 101", "G01", "Tầng 1", $"{examDate.Year}-01-10", null, null, null, 5, null, "Điều trị nội trú", null);
                    advanceCase2 = new InpatientCase("mock_ba2", "21001726/NG", "21001726", "Bệnh nhân Tạm Ứng 2", "Nữ", "1985", "Buôn Ma Thuột", deptCode, deptName, "P102", "Phòng 102", "G02", "Tầng 1", $"{examDate.Year}-01-12", null, null, null, 3, null, "Điều trị ngoại trú", null);
                    inLookupCase = new InpatientCase("mock_ba3", "21.001989", "21001989", "Lê Văn Dũng", "Nam", "1989", "Buôn Ma Thuột", deptCode, deptName, "P204", "Phòng 204", "G08", "Tầng 2", $"{examDate.Year}-02-01", null, null, null, 10, null, "Viêm ruột thừa", null);
                    dischargeCase = new InpatientCase("mock_ba5", "21.004266", "21004266", "Bệnh nhân Ra Viện", "Nam", "1975", "Buôn Ma Thuột", deptCode, deptName, "P103", "Phòng 103", "G03", "Tầng 1", $"{examDate.Year}-01-05", null, null, null, 15, null, "Khỏi bệnh", null);
                }

                if (allOutpatients.Count > 0)
                {
                    var outPool = allOutpatients.OrderBy(_ => rng.Next()).ToList();
                    outLookupCase = MapOutpatientRow(outPool[0 % outPool.Count]);
                    advanceOutpatientCase = outPool.Count > 1
                        ? MapOutpatientRow(outPool[1 % outPool.Count])
                        : outLookupCase;
                }
                else
                {
                    outLookupCase = new OutpatientCase("mock_ba4", "21.001973", "21.0012", "21001973", "Đỗ Phúc Huy", "Nam", "1973", "23 Trần Phú , P.Thắng Lợi, Tp.BMT, Đắk Lắk", deptCode, deptName, null, null, null, "10/01/2026", "15/02/2026", "10/01/2026", 35, null, "Viêm phế quản mãn", null);
                    advanceOutpatientCase = new OutpatientCase("mock_bant_adv", "21001726/NG", "21001726", "21001726", "Bệnh nhân Tạm Ứng Ngoại Trú", "Nữ", "1985", "Buôn Ma Thuột", deptCode, deptName, null, null, null, "12/01/2026", "20/01/2026", "12/01/2026", 3, null, "Điều trị ngoại trú", null);
                }

                // Build Questions with detailed instructions and dotted lines
                var questions = new List<ScenarioQuestion>();
                for (var aIdx = 0; aIdx < actions.Count; aIdx++)
                {
                    var act = actions[aIdx];
                    var instruction = InstructionForAction(
                        act.Code,
                        act.Name,
                        act.Score,
                        patient,
                        secondPatient,
                        q2Instruction,
                        orderedDrugs,
                        serviceChange,
                        drugReturn,
                        inventoryCheckDrug,
                        deptName,
                        targetTransferDept,
                        cutoffDate,
                        examDate,
                        isDirectReception && aIdx == 0,
                        actionDescMap,
                        pickedSieuAm,
                        pickedXquang,
                        advanceCase1,
                        advanceCase2,
                        inLookupCase,
                        dischargeCase,
                        outLookupCase,
                        advanceOutpatientCase,
                        allServicesWithGroup,
                        rng,
                        inventoryPeriodItem,
                        targetWh.WarehouseName,
                        gmhsSurgeryName,
                        gmhsAdmissionNumber,
                        isGmhs);

                    questions.Add(new ScenarioQuestion(aIdx + 1, act.Code, act.Name, instruction, act.Score));
                }

                var scenario = new ExamScenario(
                    Guid.NewGuid().ToString("N"),
                    batchName,
                    examDate,
                    candidate.Id,
                    candidate.Name,
                    deptCode,
                    deptName,
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
                    isWardAdmission,
                    candidate.DurationMinutes,
                    secondPatient,
                    cutoffDate,
                    inventoryCheckDrug,
                    targetTransferDept,
                    inLookupCase,
                    outLookupCase,
                    advanceCase1,
                    advanceCase2,
                    dischargeCase,
                    advanceOutpatientCase,
                    inventoryPeriodItem
                );

                scenarios.Add(scenario);
            }
        }

        return scenarios;
    }

    private static List<ScenarioDrug> AllocateCandidateDrugs(
        IReadOnlyList<SnapshotDrugRow> allWarehouseDrugs,
        Dictionary<string, double> remainingStock,
        string warehouseName,
        string departmentName,
        int totalCandidates,
        Random rng,
        bool isGmhs = false)
    {
        var orderedDrugs = new List<ScenarioDrug>();
        var alreadyPickedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pattern for allocation: GMHS uses surgical consumables (primarily Viện phí VP)
        (string PreferredSource, int DesiredQty)[] preferredSlots;
        if (isGmhs)
        {
            preferredSlots = new (string PreferredSource, int DesiredQty)[]
            {
                ("Viện phí", 1),
                ("Viện phí", 1),
                ("Viện phí", 2),
                ("Viện phí", 5),
                ("Viện phí", 2),
                ("Viện phí", 2)
            };
        }
        else
        {
            preferredSlots = new (string PreferredSource, int DesiredQty)[]
            {
                ("BHYT", 1),
                ("BHYT", 2),
                ("BHYT", 1),
                ("Viện phí", 2),
                ("Viện phí", 2)
            };
        }

        var targetCount = Math.Min(preferredSlots.Length, allWarehouseDrugs.Count);

        for (var slotIdx = 0; slotIdx < targetCount; slotIdx++)
        {
            var (prefSource, desiredQty) = preferredSlots[slotIdx];

            // 1. Try finding available items in preferred funding source with remaining stock >= desiredQty
            var candidateItems = allWarehouseDrugs
                .Where(d => !alreadyPickedCodes.Contains(d.DrugCode)
                            && d.FundingSource.Equals(prefSource, StringComparison.OrdinalIgnoreCase)
                            && remainingStock.TryGetValue(d.DrugCode, out var rem) && rem >= desiredQty)
                .ToList();

            // 2. If no item has >= desiredQty, try items in preferred source with remaining stock >= 1
            if (candidateItems.Count == 0)
            {
                candidateItems = allWarehouseDrugs
                    .Where(d => !alreadyPickedCodes.Contains(d.DrugCode)
                                && d.FundingSource.Equals(prefSource, StringComparison.OrdinalIgnoreCase)
                                && remainingStock.TryGetValue(d.DrugCode, out var rem) && rem >= 1)
                    .ToList();
            }

            // 3. If still empty in preferred source, fallback to any item not yet picked with remaining stock >= desiredQty
            if (candidateItems.Count == 0)
            {
                candidateItems = allWarehouseDrugs
                    .Where(d => !alreadyPickedCodes.Contains(d.DrugCode)
                                && remainingStock.TryGetValue(d.DrugCode, out var rem) && rem >= desiredQty)
                    .ToList();
            }

            // 4. If still empty, fallback to any item not yet picked with remaining stock >= 1
            if (candidateItems.Count == 0)
            {
                candidateItems = allWarehouseDrugs
                    .Where(d => !alreadyPickedCodes.Contains(d.DrugCode)
                                && remainingStock.TryGetValue(d.DrugCode, out var rem) && rem >= 1)
                    .ToList();
            }

            // 5. If all warehouse items were already picked by this candidate, allow reuse if remaining stock >= 1
            if (candidateItems.Count == 0)
            {
                candidateItems = allWarehouseDrugs
                    .Where(d => remainingStock.TryGetValue(d.DrugCode, out var rem) && rem >= 1)
                    .ToList();
            }

            if (candidateItems.Count == 0)
            {
                throw new InvalidOperationException($"Kho thi '{warehouseName}' của khoa '{departmentName}' không đủ tồn kho khả dụng để phân bổ cho {totalCandidates} thí sinh (Tổng số lượng tồn bị thiếu hụt khi nhiều thí sinh cùng xuất y lệnh). Vui lòng cập nhật/bổ sung thêm tồn kho trên HIS.");
            }

            // Pick an item (randomized to spread among candidates if multiple items exist)
            var chosen = candidateItems[rng.Next(candidateItems.Count)];
            var avail = remainingStock[chosen.DrugCode];
            var actualQty = avail >= desiredQty ? desiredQty : (int)Math.Max(1, avail);

            // Deduct from remaining stock so subsequent candidates will never exceed QuantityOnHand
            remainingStock[chosen.DrugCode] -= actualQty;
            alreadyPickedCodes.Add(chosen.DrugCode);

            var usage = isGmhs
                ? "Dùng trong phẫu thuật"
                : (chosen.Unit.Contains("Chai", StringComparison.OrdinalIgnoreCase)
                    ? "Truyền tĩnh mạch XL giọt/phút"
                    : (chosen.Unit.Contains("Lọ", StringComparison.OrdinalIgnoreCase) || chosen.Unit.Contains("Ống", StringComparison.OrdinalIgnoreCase)
                        ? "Tiêm bắp 01 ống lúc 08h00"
                        : "Sáng 1 viên, chiều 1 viên sau ăn"));

            orderedDrugs.Add(new ScenarioDrug(
                chosen.DrugCode,
                chosen.DrugName,
                chosen.Unit,
                actualQty,
                usage,
                chosen.WarehouseCode,
                chosen.WarehouseName,
                chosen.FundingSource));
        }

        return orderedDrugs;
    }

    private static (List<ScenarioService> OrderedServices, string Q2Instruction, ScenarioServiceChange ServiceChange, ScenarioService? PickedSieuAm, ScenarioService? PickedXquang) AllocateClinicalServices(
        IReadOnlyList<(string Code, string Name, string GroupName)> allServices,
        Random rng)
    {
        var sieuAmPool = new List<ScenarioService>();
        var xquangPool = new List<ScenarioService>();
        var ctMriPool = new List<ScenarioService>();
        var xetNghiemPool = new List<ScenarioService>();
        var otherPool = new List<ScenarioService>();

        foreach (var s in allServices)
        {
            var name = s.Name.Trim();
            var grp = s.GroupName.Trim();
            var item = new ScenarioService(s.Code, name, grp);

            if (name.Contains("Siêu âm", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Sieu am", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Doppler", StringComparison.OrdinalIgnoreCase) ||
                grp.Contains("Siêu âm", StringComparison.OrdinalIgnoreCase))
            {
                sieuAmPool.Add(item);
            }
            else if (name.Contains("Xquang", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("X-quang", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("X quang", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Chụp X", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Chụp phim", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Số hóa", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("X-quang", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("Xquang", StringComparison.OrdinalIgnoreCase))
            {
                xquangPool.Add(item);
            }
            else if (name.Contains("Cắt lớp", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Cat lop", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("CT ", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("CT-", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("CT,", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Cộng hưởng từ", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("MRI", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("Cắt lớp", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("MRI", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("CT", StringComparison.OrdinalIgnoreCase))
            {
                ctMriPool.Add(item);
            }
            else if (name.Contains("Định lượng", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Tổng phân tích", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Nhóm máu", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Điện giải", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Xét nghiệm", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Đo hoạt độ", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Huyết đồ", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Kháng sinh đồ", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("Xét nghiệm", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("Huyết học", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("Sinh hóa", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("Vi sinh", StringComparison.OrdinalIgnoreCase))
            {
                xetNghiemPool.Add(item);
            }
            else
            {
                otherPool.Add(item);
            }
        }

        // Pick random items per category
        var pickedSieuAm = sieuAmPool.Count > 0 ? sieuAmPool[rng.Next(sieuAmPool.Count)] : null;
        var pickedXquang = xquangPool.Count > 0 ? xquangPool[rng.Next(xquangPool.Count)] : null;
        var pickedCtMri = ctMriPool.Count > 0 ? ctMriPool[rng.Next(ctMriPool.Count)] : null;
        var pickedXetNghiem = xetNghiemPool.OrderBy(_ => rng.Next()).Take(Math.Min(6, xetNghiemPool.Count)).ToList();

        var orderedServices = new List<ScenarioService>();
        if (pickedSieuAm is not null) orderedServices.Add(pickedSieuAm);
        if (pickedXquang is not null) orderedServices.Add(pickedXquang);
        if (pickedCtMri is not null) orderedServices.Add(pickedCtMri);
        orderedServices.AddRange(pickedXetNghiem);

        // If total ordered services < 3 (e.g. limited test catalog), fill from otherPool or all
        while (orderedServices.Count < 3 && allServices.Count > orderedServices.Count)
        {
            var extra = allServices.FirstOrDefault(s => !orderedServices.Any(o => o.Code == s.Code));
            if (extra.Code is null) break;
            orderedServices.Add(new ScenarioService(extra.Code, extra.Name, extra.GroupName));
        }

        // Build Q2 instruction with clear categorized bullet points
        var q2Lines = new List<string>();
        q2Lines.Add("Chỉ định các dịch vụ CLS, thủ thuật, phẫu thuật cho bệnh nhân (Bệnh nhân BHYT câu 1):");

        var cdha = new List<string>();
        if (pickedSieuAm is not null) cdha.Add(pickedSieuAm.Name);
        if (pickedXquang is not null) cdha.Add(pickedXquang.Name);
        if (cdha.Count > 0)
        {
            q2Lines.Add("+ Chẩn đoán hình ảnh: " + string.Join(", ", cdha) + ".");
        }

        if (pickedCtMri is not null)
        {
            q2Lines.Add("+ Cắt lớp vi tính (CT) / MRI: " + pickedCtMri.Name + ".");
        }

        if (pickedXetNghiem.Count > 0)
        {
            q2Lines.Add("+ Các dịch vụ xét nghiệm: " + string.Join("; ", pickedXetNghiem.Select(x => x.Name)) + ".");
        }

        var others = orderedServices.Where(s => s != pickedSieuAm && s != pickedXquang && s != pickedCtMri && !pickedXetNghiem.Contains(s)).ToList();
        if (others.Count > 0)
        {
            q2Lines.Add("+ Dịch vụ kỹ thuật khác: " + string.Join(", ", others.Select(x => x.Name)) + ".");
        }

        q2Lines.Add("Số điểm đạt được:………(đ)");
        var q2Instruction = string.Join("\n", q2Lines);

        // Build Q5 Service Change
        ScenarioService canceledService;
        if (pickedXquang is not null) canceledService = pickedXquang;
        else if (pickedSieuAm is not null) canceledService = pickedSieuAm;
        else canceledService = orderedServices[0];

        ScenarioService newService;
        var availSieuAm = sieuAmPool.Where(s => !orderedServices.Any(o => o.Code == s.Code)).ToList();
        if (availSieuAm.Count > 0)
        {
            newService = availSieuAm[rng.Next(availSieuAm.Count)];
        }
        else
        {
            var availOther = allServices.Where(s => !orderedServices.Any(o => o.Code == s.Code)).ToList();
            newService = availOther.Count > 0
                ? new ScenarioService(availOther[0].Code, availOther[0].Name, availOther[0].GroupName)
                : new ScenarioService("SA_BUNG", "Siêu âm bụng tổng quát", "Chẩn đoán hình ảnh");
        }

        ScenarioService addedService;
        var availXquang = xquangPool.Where(s => !orderedServices.Any(o => o.Code == s.Code) && s.Code != newService.Code).ToList();
        if (availXquang.Count > 0)
        {
            addedService = availXquang[rng.Next(availXquang.Count)];
        }
        else
        {
            var availOther = allServices.Where(s => !orderedServices.Any(o => o.Code == s.Code) && s.Code != newService.Code).ToList();
            addedService = availOther.Count > 0
                ? new ScenarioService(availOther[0].Code, availOther[0].Name, availOther[0].GroupName)
                : new ScenarioService("XQ_COCHAN", "Chụp Xquang xương cổ chân thẳng, nghiêng [Số hóa 2 phim]", "Chẩn đoán hình ảnh");
        }

        var serviceChange = new ScenarioServiceChange(canceledService, newService, addedService);
        return (orderedServices, q2Instruction, serviceChange, pickedSieuAm, pickedXquang);
    }

    private static ScenarioPatient MapPatientRow(SnapshotPatientRow pRow, DateOnly examDate, PatientPaymentType defaultPayment)
    {
        var isTe1 = pRow.InsuranceNumber?.StartsWith("TE1", StringComparison.OrdinalIgnoreCase) == true;
        var insurancePeriod = InsurancePeriod.ForExamYear(examDate.Year, isTe1);
        var age = 35;
        DateOnly? birthDate = null;
        if (DateOnly.TryParse(pRow.DateOfBirth, out var parsedBirth))
        {
            birthDate = parsedBirth;
            age = examDate.Year - parsedBirth.Year;
        }

        var paymentType = pRow.PaymentType.Equals("BHYT", StringComparison.OrdinalIgnoreCase)
            ? PatientPaymentType.Insurance
            : (pRow.PaymentType.Equals("Viện phí", StringComparison.OrdinalIgnoreCase) ? PatientPaymentType.SelfPay : defaultPayment);

        return new ScenarioPatient(
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
            paymentType
        );
    }

    private static string InstructionForAction(
        string code,
        string name,
        double score,
        ScenarioPatient patient,
        ScenarioPatient secondPatient,
        string q2Instruction,
        IReadOnlyList<ScenarioDrug> orderedDrugs,
        ScenarioServiceChange? serviceChange,
        ScenarioDrugReturn? drugReturn,
        ScenarioDrug? inventoryDrug,
        string deptName,
        string targetTransferDept,
        DateOnly cutoffDate,
        DateOnly examDate,
        bool isDirectReception,
        IReadOnlyDictionary<string, string> actionDescMap,
        ScenarioService? pickedSieuAm = null,
        ScenarioService? pickedXquang = null,
        InpatientCase? advanceCase1 = null,
        InpatientCase? advanceCase2 = null,
        InpatientCase? inLookupCase = null,
        InpatientCase? dischargeCase = null,
        OutpatientCase? outLookupCase = null,
        OutpatientCase? advanceOutpatientCase = null,
        IReadOnlyList<(string Code, string Name, string GroupName)>? allServices = null,
        Random? rng = null,
        InventoryCheckPeriodItem? inventoryPeriodItem = null,
        string? targetWarehouseName = null,
        string? surgeryName = null,
        string? surgeryPatientAdmissionNumber = null,
        bool isGmhs = false) => code switch
    {
        "LT_BAN_PHIEU_KHAM" => BuildLtBanPhieuKhamInstruction(patient, secondPatient, examDate),
        "LT_THU_PHI_VA_CLS" => BuildLtThuPhiClsInstruction(pickedSieuAm, allServices),
        "LT_THU_TAM_UNG_NOI_TRU" => $"Thu tạm ứng cho các bệnh nhân sau:\n© Bệnh nhân nội trú có số bệnh án : {advanceCase1?.SoBenhAn ?? "21.004865"}, Số tiền tạm ứng : 1.500.000 đồng (1đ)\n© Bệnh nhân bệnh án ngoại trú có số bệnh án : {advanceOutpatientCase?.SoBenhAn ?? "21001726/NG"}, Số tiền tạm ứng : 500.000 đồng (1đ)\nSố điểm đạt được : ………",
        "LT_DOI_DICH_VU_CLS" => $"Chọn quyển hóa đơn ký hiệu:  THI để thu\n© Huỷ và đổi dịch vụ {pickedSieuAm?.Name ?? "Siêu âm ổ bụng"} thành dịch vụ : {(pickedXquang?.Name ?? "Chụp Xquang cột sống cổ chếch hai bên [Số hoá 2 phim]")} và thu tiền lại (1đ)\nSố điểm đạt được : ………",
        "LT_TRA_CUU_THONG_TIN" => $"Tìm thông tin của bệnh nhân\n© Đang điều trị nội trú có họ và tên : {inLookupCase?.FullName ?? "Lê Văn Dũng"} sinh năm {GetBirthYear(inLookupCase?.DateOfBirth ?? "1989")} (1đ)\nKhoa điều trị : ………………………………………………………………………...\nPhòng bệnh   : ………………………………………………………………………...\nSố giường      : ………………………………………………………………………..;\n© Tìm bệnh nhân có thông tin như sau: {outLookupCase?.FullName ?? "Đỗ Phúc Huy"}, Năm sinh: {GetBirthYear(outLookupCase?.DateOfBirth ?? "1973")} {outLookupCase?.Address ?? "23 Trần Phú , P.Thắng Lợi, Tp.BMT, Đắk Lắk"} và ghi lại các thông tin sau :(1đ)\nSố lượt khám (nếu có):………………………………………………………………………\nNgày điều trị Nội trú gần nhất(nếu có):……………………………………………\nNgày điều trị Ngoại trú gần nhất (nếu có):………………………………………...........\nSố điểm đạt được : ………",
        "LT_THANH_TOAN_RA_VIEN" => $"Thu Thanh Toán Ra viện cho bệnh nhân nội trú có số bệnh án: {dischargeCase?.SoBenhAn ?? "21.004266"} (1đ)\n(Dùng quyển hóa đơn VAT_THI)\nSố điểm đạt được : ………",
        "LT_TAO_THE_TAM_BHYT" => BuildLtTaoTheTamInstruction(examDate, rng),
        "TC_THU_TAM_UNG" => $"Thu tạm ứng viện phí cho bệnh nhân có số bệnh án nội trú : {advanceCase1?.SoBenhAn ?? "21.004865"}, số tiền tạm ứng : 1.500.000 đồng.\nSố điểm đạt được:………(đ)",
        "TC_THANH_TOAN_RA_VIEN" => $"Thực hiện quyết toán và in hóa đơn thanh toán viện phí ra viện cho bệnh nhân nội trú có số bệnh án: {dischargeCase?.SoBenhAn ?? "21.004266"} (Dùng quyển hóa đơn VAT_THI).\nSố điểm đạt được:………(đ)",
        "TC_TRA_CUU_BENH_SU" => $"Tra cứu lịch sử khám chữa bệnh của bệnh nhân: {outLookupCase?.FullName ?? "Đỗ Phúc Huy"}, Mã y tế : {outLookupCase?.MedicalCode ?? "23000059"}.\n- Tổng số lượt khám : …………………………………\n- Ngày điều trị nội trú gần nhất : …………………………………\n- Ngày khám ngoại trú gần nhất : …………………………………\nSố điểm đạt được:………(đ)",
        "TN_TIEP_NHAN" or _ when isDirectReception => BuildDirectReceptionInstruction(patient, secondPatient, examDate),
        "NT_NHAN_BENH_KHOA" => $"* {secondPatient.FullName} ; Mã y tế: {secondPatient.MedicalCode}\n* {patient.FullName} ; Mã y tế: {patient.MedicalCode}\n- SVV Bn VP       : ………………………………\n- SVV Bn BHYT : ……………………………….\nSố điểm đạt được:………(đ)",
        "NT_CAP_GIUONG" => "Cấp giường cho các bệnh nhân được tiếp nhận ở câu 1.\nSố điểm đạt được:………(đ)",
        "KB_CHI_DINH_CLS" => BuildKbChiDinhClsInstruction(pickedXquang, pickedSieuAm),
        "KB_BENH_AN_NGOAI_TRU" => name.Contains("Nhận bệnh", StringComparison.OrdinalIgnoreCase) || name.Contains("bệnh án", StringComparison.OrdinalIgnoreCase) && name.Contains("nội dung", StringComparison.OrdinalIgnoreCase)
            ? $"Nhận bệnh nhân vào khoa - tạo Bệnh án ngoại trú, nhập nội dung bệnh án ({score:0}đ):\n\t* {patient.FullName}; Mã y tế: {patient.MedicalCode}\n- Số bệnh án : ………………………………………………………………………………………\nSố điểm đạt được:………(đ)"
            : "Chỉ định điều trị ngoại trú và lập bệnh án ngoại trú cho bệnh nhân : ……………………………………, Mã Y tế : …………………… (2 đ)\n- Số bệnh án ngoại trú : …………………………………………………\nSố điểm đạt được:………(đ)",
        "KB_CHI_DINH_NOI_TRU" => $"Chỉ định nhập viện điều trị nội trú cho bệnh nhân : ……………………………………, Mã y tế : …………………… (1 đ)\n- Nhập khoa : {targetTransferDept}\n- Số bệnh án nội trú : ……………………………………………………\nSố điểm đạt được:………(đ)",
        "HC_TRA_CUU_THANH_TOAN" => name.Contains("đang nằm viện", StringComparison.OrdinalIgnoreCase) || name.Contains("viện phí", StringComparison.OrdinalIgnoreCase)
            ? $"Kiểm tra viện phí của bệnh nhân đang nằm viện ({score:0}đ):\n- Tìm 01 bệnh nhân bất kỳ đang nằm điều ngoại trú thuộc {deptName}, có thời gian nhập viện trong khoảng từ ngày 01/03/{examDate.Year} đến ngày 05/03/{examDate.Year}.\n- Tra cứu chi tiết viện phí và ghi lại các thông tin sau:\n+ Họ và tên bệnh nhân                : ………………………………………………………………………………………\n+ Mã y tế / Số bệnh án               : ………………………………………………………………………………………\n+ Tổng chi phí điều trị đến hiện tại  : ………………………………………………………………………………… đồng\n+ Số tiền đã tạm ứng                  : ………………………………………………………………………………… đồng\n+ Số tiền còn phải thanh toán/hoàn trả: ………………………………………………………………………………… đồng\nSố điểm đạt được:………(đ)"
            : $"Kiểm tra thông tin thanh toán cho bệnh nhân : {inLookupCase?.FullName ?? "................"}, Mã y tế : {inLookupCase?.MedicalCode ?? "................"}\n- Số bệnh án nội trú : {inLookupCase?.SoBenhAn ?? "…………………………………………"}\n- Số tiền người bệnh đã thanh toán : ……………………………………………………\n- Số tiền người bệnh đã tạm ứng : ……………………………………………………\n- Số tiền còn phải thu người bệnh : ……………………………………………………\n- Tổng số tiền người bệnh trả : ……………\nSố điểm đạt được:………(đ)",
        "TT_TUONG_TRINH_PTTT" => $"Nhập nội dung tường trình PT-TT cho các dịch vụ đã chỉ định cho bệnh nhân {patient.FullName} ({patient.MedicalCode}/NG) ({score:0}đ)\nSố điểm đạt được:………(đ)",
        "YL_CHI_DINH_CLS" => q2Instruction,
        "YL_CHI_DINH_THUOC_VTYT" => isGmhs || deptName.Contains("Gây mê", StringComparison.OrdinalIgnoreCase) || score >= 5
            ? (isGmhs || deptName.Contains("Gây mê", StringComparison.OrdinalIgnoreCase)
                ? $"Thực hiện thao tác lên các loại Dược – VTYT cần thiết cho bệnh nhân {patient.FullName} có  Số vào viện :{surgeryPatientAdmissionNumber ?? patient.MedicalCode}  sau phẫu thuật:  {surgeryName ?? "Phẫu thuật lấy thai lần đầu [gây tê]"}, ngày yêu cầu {examDate:dd/MM/yyyy} (có danh mục Dược – VTYT đính kèm)(3 đ)\nThực hiện thao tác:\n- Lập phiếu lĩnh dược (2đ)  …………\n- Nhập dược nội bộ (2đ) ……………\n- Xuất sử dụng thuốc - VTYT cho bệnh nhân (số vào viện ở câu 1) (1đ)\n(Lưu ý : khi thực hiện phải yêu cầu nhân viên vi tính hỗ trợ thao tác xuất dược nội bộ)\nSố điểm đạt được:………(đ)"
                : "Thực hiện thao tác lên các loại Dược – VTYT cần thiết cho bệnh nhân sau phẫu thuật (có danh mục Dược – VTYT đính kèm) (3đ)\nThực hiện thao tác:\n- Lập phiếu lĩnh dược (2đ)  …………\n- Nhập dược nội bộ (2đ) ……………\n- Xuất sử dụng thuốc - VTYT cho bệnh nhân (số vào viện ở câu 1) (1đ)\n(Lưu ý : khi thực hiện phải yêu cầu nhân viên vi tính hỗ trợ thao tác xuất dược nội bộ)\nSố điểm đạt được:………(đ)")
            : "Lên y lệnh(có D/S Thuốc, VTYT đính kèm)(1.5đ)\nLập  phiếu lĩnh dược(0.5đ)\nNhập nội bộ(0.5đ)\nXuất sử dụng thuốc cho bệnh nhân(0.5đ)\nSố điểm đạt được:………(đ)",
        "YL_TRA_THUOC" => $"Bệnh nhân trả thuốc và tổng hợp phiếu trả.(Chỉ thao tác trên bệnh nhân BHYT có số vào viện ở câu 1), thuốc cần trả {drugReturn?.ReturnedDrug.Name ?? "thuốc"}, số lượng trả : {drugReturn?.ReturnQuantity ?? 1} {drugReturn?.ReturnedDrug.Unit ?? "viên"}.\nSố điểm đạt được:………(đ)",
        "YL_DOI_THEM_DICH_VU" => $"Đổi dịch vụ {serviceChange?.CanceledService.Name ?? "Dịch vụ CLS 1"} thành dịch vụ {serviceChange?.NewService.Name ?? "Dịch vụ CLS 2"} thêm 01 dịch vụ: {(serviceChange?.AddedService?.Name ?? "Chụp Xquang ngực thẳng [Số hóa 1 phim]")}. (Bệnh nhân BHYT câu 1)\nSố điểm đạt được:………(đ)",
        "TK_KIEM_TON_KHO" => isGmhs
            ? (inventoryPeriodItem is not null
                ? $"Báo số tồn kho của {inventoryPeriodItem.DrugName} trong {targetWarehouseName ?? "kho Gây mê"} từ ngày {inventoryPeriodItem.FromDate:dd/MM/yyyy} đến ngày {inventoryPeriodItem.ToDate:dd/MM/yyyy} (Nguồn {inventoryPeriodItem.FundingSource}), số lượng tồn : ………… (2 đ)\nSố điểm đạt được:………(đ)"
                : $"Báo số tồn kho của {inventoryDrug?.Name ?? "Kim tiêm nhựa G18; G26"} trong {targetWarehouseName ?? "kho Gây mê"} từ ngày 01/01/{examDate.Year} đến ngày {examDate:dd/MM/yyyy} (Nguồn {inventoryDrug?.FundingSource ?? "Viện phí"}), số lượng tồn : ………… (2 đ)\nSố điểm đạt được:………(đ)")
            : (inventoryPeriodItem is not null
                ? $"Báo số tồn kho của một loại dược trong kho cơ số (Nguồn {inventoryPeriodItem.FundingSource}) từ ngày {inventoryPeriodItem.FromDate:dd/MM/yyyy} đến ngày {inventoryPeriodItem.ToDate:dd/MM/yyyy} : Tên dược: {inventoryPeriodItem.DrugName}  số lượng tồn cuối : …………….\nSố điểm đạt được:………(đ)"
                : $"Báo số tồn kho của một loại dược trong kho cơ số (Nguồn {inventoryDrug?.FundingSource ?? "Viện phí"}) từ ngày 01/02/2026 đến ngày 28/02/2026 : Tên dược: {inventoryDrug?.Name ?? "Thuốc/VTYT"}  số lượng tồn cuối : …………….\nSố điểm đạt được:………(đ)"),
        "CK_CHUYEN_KHOA" => $"Chuyển một bệnh nhân bất kỳ từ khoa {deptName} đến {targetTransferDept}(bệnh nhân nhập viện trước {cutoffDate:dd/MM/yyyy}), Số vào viện của BN: …………………, Mã y tế :…………………., họ tên bệnh nhân:……………………………………\nSố điểm đạt được:………(đ)",
        "RV_CHO_RA_VIEN" => $"Thực hiện thao tác cho bệnh nhân ra viện(bệnh nhân nhập viện trước {cutoffDate:dd/MM/yyyy}) : Số vào viện của BN: ………………………\nSố điểm đạt được:………(đ)",
        _ => actionDescMap.TryGetValue(code, out var customDesc) && !string.IsNullOrWhiteSpace(customDesc)
            ? $"{customDesc}\nSố điểm đạt được:………(đ)"
            : $"Thực hiện nghiệp vụ chuyên môn '{name}' trên phần mềm HIS theo quy định của Bệnh viện Đa khoa Thiện Hạnh.\nSố điểm đạt được:………(đ)"
    };

    private static InpatientCase MapInpatientRow(SnapshotInpatientRow r) => new(
        r.BenhAnId,
        r.SoBenhAn,
        r.MedicalCode,
        r.FullName,
        r.Gender,
        r.DateOfBirth,
        r.Address,
        r.DepartmentCode,
        r.DepartmentName,
        r.RoomCode,
        r.RoomName,
        r.BedCode,
        r.Floor,
        r.AdmissionDate,
        r.AdmissionTime,
        r.BedInTime,
        r.DepartmentInDate,
        r.DaysInHospital,
        r.InsuranceNumber,
        r.Diagnosis,
        r.DoctorName
    );

    private static OutpatientCase MapOutpatientRow(SnapshotOutpatientRow r) => new(
        r.BenhAnNgoaiTruId,
        r.SoBenhAn,
        r.SoLuuTru,
        r.MedicalCode,
        r.FullName,
        r.Gender,
        r.DateOfBirth,
        r.Address,
        r.DepartmentCode,
        r.DepartmentName,
        r.RoomCode,
        r.RoomName,
        r.BedCode,
        r.TreatmentFromDate,
        r.TreatmentToDate,
        r.AdmissionDate,
        r.DaysInTreatment,
        r.InsuranceNumber,
        r.Diagnosis,
        r.DoctorName
    );

    private static string GetBirthYear(string dobOrYear)
    {
        if (string.IsNullOrWhiteSpace(dobOrYear)) return "1980";
        var trimmed = dobOrYear.Trim();
        if (trimmed.Length == 4 && int.TryParse(trimmed, out _)) return trimmed;
        if (trimmed.Contains('/'))
        {
            var parts = trimmed.Split('/');
            return parts[^1].Trim();
        }
        if (trimmed.Contains('-'))
        {
            var parts = trimmed.Split('-');
            return parts[0].Length == 4 ? parts[0].Trim() : parts[^1].Trim();
        }
        return trimmed;
    }

    private static string BuildLtBanPhieuKhamInstruction(ScenarioPatient patient, ScenarioPatient secondPatient, DateOnly examDate)
    {
        var dobStrBh = patient.DateOfBirth.HasValue
            ? patient.DateOfBirth.Value.ToString("dd/MM/yyyy")
            : $"Năm sinh: {examDate.Year - patient.Age}";

        var dobStrVp = secondPatient.DateOfBirth.HasValue
            ? secondPatient.DateOfBirth.Value.ToString("dd/MM/yyyy")
            : $"Năm sinh: {examDate.Year - secondPatient.Age}";

        var lines = new List<string>
        {
            "Tiếp nhận bệnh nhân và cho chỉ định khám bệnh (01 bệnh nhân viện phí, 01 bệnh nhân BHYT).(1đ)",
            "© 1. Bệnh nhân Viện phí:",
            $"+ Họ và tên: {secondPatient.FullName.ToUpperInvariant()}",
            $"+ Ngày sinh: {dobStrVp} ({secondPatient.Age} tuổi) - Giới tính: {secondPatient.Gender}",
            $"+ Địa chỉ: {secondPatient.Address}",
            $"+ Đối tượng: Viện phí",
            $"+ Phòng khám / Bán phiếu: Khám nội VP",
            "",
            "© 2. Bệnh nhân Bảo hiểm y tế (BHYT):",
            $"+ Họ và tên: {patient.FullName.ToUpperInvariant()}",
            $"+ Ngày sinh: {dobStrBh} ({patient.Age} tuổi) - Giới tính: {patient.Gender}",
            $"+ Địa chỉ: {patient.Address}",
            $"+ Đối tượng: BHYT"
        };

        if (!string.IsNullOrWhiteSpace(patient.InsuranceNumber))
        {
            lines.Add($"+ Số thẻ BHYT: {patient.InsuranceNumber}");
            if (patient.InsurancePeriod is not null)
            {
                lines.Add($"+ Hạn thẻ: Từ ngày {patient.InsurancePeriod.From:dd/MM/yyyy} đến ngày {patient.InsurancePeriod.To:dd/MM/yyyy}");
            }
            lines.Add($"+ Nơi ĐKKCB: {patient.InitialRegistrationCode} - BVĐK Thiện Hạnh");
        }

        lines.Add("+ Phòng khám / Bán phiếu: Khám ngoại tổng hợp BHYT");
        lines.Add("");
        lines.Add("- Mã y tế Bn VP       : ……………………………………; - Bán phiếu :  Khám nội VP");
        lines.Add("- Mã y tế  Bn BHYT : ……………………………………; - Bán phiếu :  Khám ngoại tổng hợp BHYT");
        lines.Add("Số điểm đạt được : ………");

        return string.Join("\n", lines);
    }

    private static string BuildLtTaoTheTamInstruction(DateOnly examDate, Random? rng)
    {
        var motherNames = new[] { "Lê Thị Thảo", "Trần Thị Hạnh", "Nguyễn Thị Mai", "Phạm Thị Loan", "Hoàng Thị Cúc" };
        var mother = rng is not null ? motherNames[rng.Next(motherNames.Length)] : "Lê Thị Thảo";
        var birthDate = examDate.AddMonths(-1);
        return $"""
Truy cập cổng giám định BHYT và tạo mã thẻ tạm cho:
Họ tên: Bé gái mẹ {mother}
Ngày sinh: {birthDate:dd/MM/yyyy}
Các thông tin khác tự nhập
Sau đó điền các thông tin sau:
Mã số thẻ BHYT:…………………………………….
Hạn sử dụng thẻ:……………………………………..
(Dùng quyển hóa đơn VAT_THI) (1đ)
Số điểm đạt được : ………
""";
    }

    private static string BuildLtThuPhiClsInstruction(
        ScenarioService? pickedSieuAm,
        IReadOnlyList<(string Code, string Name, string GroupName)>? allServices)
    {
        var sa = pickedSieuAm?.Name ?? "Siêu âm ổ bụng";
        var xn1 = "Định lượng Vitamin B12 [Máu]";
        var xn2 = "Tìm ký sinh trùng sốt rét trong máu (bằng phương pháp thủ công)";
        if (allServices is not null && allServices.Count > 0)
        {
            var match1 = allServices.FirstOrDefault(s => s.Name.Contains("Vitamin", StringComparison.OrdinalIgnoreCase));
            if (match1.Name != null) xn1 = match1.Name;
            var match2 = allServices.FirstOrDefault(s => s.Name.Contains("sốt rét", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Ký sinh trùng", StringComparison.OrdinalIgnoreCase));
            if (match2.Name != null) xn2 = match2.Name;
        }

        return $"""
Chọn quyển hóa đơn ký hiệu:  THI để thu
© Thu phí khám bệnh cho 2 bệnh nhân trên (1đ)
© Tạo các CLS sau (Bệnh nhân VP có phiếu khám nội):
{sa}, {xn1}, {xn2}. Thu phí cho các CLS vừa tạo (1đ)
Số điểm đạt được : ………
""";
    }

    private static string BuildDirectReceptionInstruction(ScenarioPatient patient, ScenarioPatient secondPatient, DateOnly examDate)
    {
        var isBhyt = patient.PaymentType == PatientPaymentType.Insurance && !string.IsNullOrWhiteSpace(patient.InsuranceNumber);
        var dobStr = patient.DateOfBirth.HasValue
            ? patient.DateOfBirth.Value.ToString("dd/MM/yyyy")
            : $"Năm sinh: {examDate.Year - patient.Age}";

        var dobStrVp = secondPatient.DateOfBirth.HasValue
            ? secondPatient.DateOfBirth.Value.ToString("dd/MM/yyyy")
            : $"Năm sinh: {examDate.Year - secondPatient.Age}";

        var lines = new List<string>
        {
            "Mở phân hệ Tiếp nhận trên phần mềm HIS, thực hiện tiếp nhận trực tiếp người bệnh với thông tin sau:",
            "* 1. Bệnh nhân Bảo hiểm y tế (BHYT):",
            $"+ Họ và tên: {patient.FullName.ToUpperInvariant()}",
            $"+ Ngày sinh: {dobStr} ({patient.Age} tuổi) - Giới tính: {patient.Gender}",
            $"+ Địa chỉ: {patient.Address}",
            $"+ Đối tượng: {(isBhyt ? "BHYT" : "Viện phí")}"
        };

        if (isBhyt)
        {
            lines.Add($"+ Số thẻ BHYT: {patient.InsuranceNumber}");
            if (patient.InsurancePeriod is not null)
            {
                lines.Add($"+ Hạn thẻ BHYT: Từ ngày {patient.InsurancePeriod.From:dd/MM/yyyy} đến ngày {patient.InsurancePeriod.To:dd/MM/yyyy}");
            }
            lines.Add($"+ Nơi ĐKKCB ban đầu: {patient.InitialRegistrationCode} - BVĐK Thiện Hạnh");
        }

        lines.Add($"+ Chẩn đoán ban đầu: {patient.Diagnosis}");

        lines.Add("* 2. Bệnh nhân Viện phí:");
        lines.Add($"+ Họ và tên: {secondPatient.FullName.ToUpperInvariant()}");
        lines.Add($"+ Ngày sinh: {dobStrVp} ({secondPatient.Age} tuổi) - Giới tính: {secondPatient.Gender}");
        lines.Add($"+ Địa chỉ: {secondPatient.Address}");
        lines.Add($"+ Đối tượng: Viện phí");
        lines.Add($"+ Chẩn đoán ban đầu: {secondPatient.Diagnosis}");

        lines.Add("- Số vào viện bệnh nhân bảo hiểm: ………………………………");
        lines.Add("- Số vào viện bệnh nhân viện phí: ………………………………");
        lines.Add("Số điểm đạt được:………(đ)");

        return string.Join("\n", lines);
    }

    private static string BuildKbChiDinhClsInstruction(ScenarioService? xq, ScenarioService? sa)
    {
        var xqName = xq?.Name ?? "Chụp Xquang tim phổi thẳng [Số hóa 1 phim]";
        var saName = sa?.Name ?? "Siêu âm Doppler màu ổ bụng tổng quát";
        return $"""
Trên màn hình khám bệnh Chỉ định CLS – Thủ thuật cho bệnh nhân : ……………………………………, Mã Y tế : ……………………
+ {xqName}
+ {saName}
Số điểm đạt được:………(đ)
""";
    }

    private static int GetDeterministicHashCode(string str)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in str)
            {
                hash = hash * 31 + c;
            }
            return hash;
        }
    }
}
