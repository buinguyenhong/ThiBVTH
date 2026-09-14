using ExamGenerator.Domain;

namespace ExamGenerator.Infrastructure;

public sealed record CandidateInput(string Name, string Id, string Department, string Template, int DurationMinutes = 30);

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

            // Ensure sufficient stock in warehouse for exam
            var inStockDrugs = warehouseDrugs.Where(d => d.QuantityOnHand >= 1).ToList();
            if (inStockDrugs.Count == 0)
            {
                throw new InvalidOperationException($"Kho thi '{deptConfig.WarehouseName}' của khoa '{deptName}' không có thuốc/vật tư nào còn tồn kho > 0.");
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
            var otherDept = configMap.Keys.FirstOrDefault(k => !k.Equals(deptName, StringComparison.OrdinalIgnoreCase));
            var targetTransferDept = otherDept ?? (deptName.Contains("Nội", StringComparison.OrdinalIgnoreCase) ? "Khoa Ngoại tổng hợp" : "Khoa Nội");

            // Cutoff date for Q7 & Q8: default 10 days before examDate
            var cutoffDate = examDate.AddDays(-10);

            var userIndex = 0;
            var seed = Math.Abs(examDate.GetHashCode() ^ deptName.GetHashCode());
            var rng = new Random(seed);

            // Track cumulative remaining stock per drug across all candidates taking the exam in this department
            // to ensure total allocated quantity across all candidates NEVER exceeds warehouse QuantityOnHand.
            var remainingStock = inStockDrugs.ToDictionary(d => d.DrugCode, d => d.QuantityOnHand);

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

                // Allocate Clinical Services categorized by modality (Siêu âm, Xquang, CT/MRI, Xét nghiệm)
                var (orderedServices, q2Instruction, serviceChange) = AllocateClinicalServices(allServicesWithGroup, rng);

                // Allocate Drugs: Standard 3 BH (SL: 1, 2, 1) + 2 VP (SL: 2, 2), strictly checking remaining stock
                var orderedDrugs = AllocateCandidateDrugs(inStockDrugs, remainingStock, deptConfig.WarehouseName, deptName, group.Count(), rng);

                ScenarioDrugReturn? drugReturn = null;
                if (orderedDrugs.Count > 0)
                {
                    var returnItem = orderedDrugs.Count > 1 ? orderedDrugs[1] : orderedDrugs[0];
                    drugReturn = new ScenarioDrugReturn(returnItem, 1, "Người bệnh xuất hiện phản ứng phụ nhẹ / đổi thuốc theo y lệnh bác sĩ");
                }

                // Inventory check drug for Q6 (preferably Viện phí source, rotated across candidates)
                var vpDrugsForCheck = warehouseDrugs.Where(d => d.FundingSource.Equals("Viện phí", StringComparison.OrdinalIgnoreCase)).ToList();
                var invPool = vpDrugsForCheck.Count > 0 ? vpDrugsForCheck : warehouseDrugs;
                var invDrugRow = invPool[rng.Next(invPool.Count)];
                var inventoryCheckDrug = new ScenarioDrug(invDrugRow.DrugCode, invDrugRow.DrugName, invDrugRow.Unit, 0, "", invDrugRow.WarehouseCode, invDrugRow.WarehouseName, invDrugRow.FundingSource);

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
                        isDirectReception && aIdx == 0);

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
                    isWardAdmission,
                    candidate.DurationMinutes,
                    secondPatient,
                    cutoffDate,
                    inventoryCheckDrug,
                    targetTransferDept
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
        Random rng)
    {
        var orderedDrugs = new List<ScenarioDrug>();
        var alreadyPickedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Standard exam pattern:
        // Slot 0: BH, qty 1
        // Slot 1: BH, qty 2
        // Slot 2: BH, qty 1
        // Slot 3: VP, qty 2
        // Slot 4: VP, qty 2
        var preferredSlots = new (string PreferredSource, int DesiredQty)[]
        {
            ("BHYT", 1),
            ("BHYT", 2),
            ("BHYT", 1),
            ("Viện phí", 2),
            ("Viện phí", 2)
        };

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

            var usage = chosen.Unit.Contains("Chai", StringComparison.OrdinalIgnoreCase)
                ? "Truyền tĩnh mạch XL giọt/phút"
                : (chosen.Unit.Contains("Lọ", StringComparison.OrdinalIgnoreCase) || chosen.Unit.Contains("Ống", StringComparison.OrdinalIgnoreCase)
                    ? "Tiêm bắp 01 ống lúc 08h00"
                    : "Sáng 1 viên, chiều 1 viên sau ăn");

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

    private static (List<ScenarioService> OrderedServices, string Q2Instruction, ScenarioServiceChange ServiceChange) AllocateClinicalServices(
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
        return (orderedServices, q2Instruction, serviceChange);
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
        bool isDirectReception) => code switch
    {
        _ when isDirectReception => "Mở phân hệ Tiếp nhận trên phần mềm HIS, thực hiện nhập đầy đủ thông tin hành chính, số thẻ BHYT và chẩn đoán của người bệnh theo bảng thông tin trên.\nSố điểm đạt được:………(đ)",
        "NT_NHAN_BENH_KHOA" => $"* {secondPatient.FullName} ; Mã y tế: {secondPatient.MedicalCode}\n* {patient.FullName} ; Mã y tế: {patient.MedicalCode}\n- SVV Bn VP       : ………………………………\n- SVV Bn BHYT : ……………………………….\nSố điểm đạt được:………(đ)",
        "YL_CHI_DINH_CLS" => q2Instruction,
        "YL_CHI_DINH_THUOC_VTYT" => "Lên y lệnh(có D/S Thuốc, VTYT đính kèm)(1.5đ)\nLập  phiếu lĩnh dược(0.5đ)\nNhập nội bộ(0.5đ)\nXuất sử dụng thuốc cho bệnh nhân(0.5đ)\nSố điểm đạt được:………(đ)",
        "YL_TRA_THUOC" => $"Bệnh nhân trả thuốc và tổng hợp phiếu trả.(Chỉ thao tác trên bệnh nhân BHYT có số vào viện ở câu 1), thuốc cần trả {drugReturn?.ReturnedDrug.Name ?? "thuốc"}, số lượng trả : {drugReturn?.ReturnQuantity ?? 1} {drugReturn?.ReturnedDrug.Unit ?? "viên"}.\nSố điểm đạt được:………(đ)",
        "YL_DOI_THEM_DICH_VU" => $"Đổi dịch vụ {serviceChange?.CanceledService.Name ?? "Dịch vụ CLS 1"} thành dịch vụ {serviceChange?.NewService.Name ?? "Dịch vụ CLS 2"} thêm 01 dịch vụ: {(serviceChange?.AddedService?.Name ?? "Chụp Xquang ngực thẳng [Số hóa 1 phim]")}. (Bệnh nhân BHYT câu 1)\nSố điểm đạt được:………(đ)",
        "TK_KIEM_TON_KHO" => $"Báo số tồn kho của một loại dược trong kho cơ số (Nguồn {inventoryDrug?.FundingSource ?? "Viện phí"}) từ ngày {examDate.AddMonths(-1).AddDays(1):dd/MM/yyyy} đến ngày {examDate:dd/MM/yyyy} : Tên dược: {inventoryDrug?.Name ?? "Thuốc/VTYT"}  số lượng tồn cuối : …………….\nSố điểm đạt được:………(đ)",
        "CK_CHUYEN_KHOA" => $"Chuyển một bệnh nhân bất kỳ từ khoa {deptName} đến {targetTransferDept}(bệnh nhân nhập viện trước {cutoffDate:dd/MM/yyyy}), Số vào viện của BN: …………………, Mã y tế :…………………., họ tên bệnh nhân:……………………………………\nSố điểm đạt được:………(đ)",
        "RV_CHO_RA_VIEN" => $"Thực hiện thao tác cho bệnh nhân ra viện(bệnh nhân nhập viện trước {cutoffDate:dd/MM/yyyy}) : Số vào viện của BN: ………………………\nSố điểm đạt được:………(đ)",
        _ => $"Thực hiện nghiệp vụ chuyên môn '{name}' trên phần mềm HIS theo quy định của Bệnh viện Đa khoa Thiện Hạnh.\nSố điểm đạt được:………(đ)"
    };
}
