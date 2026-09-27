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
        var templateMap = templates.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);

        // Pre-fetch available patients from SQLite snapshot
        var availablePatients = (await _database.GetAvailablePatientsAsync(cancellationToken)).ToList();
        if (availablePatients.Count == 0)
        {
            throw new InvalidOperationException("Chưa có hồ sơ Bệnh nhân nào trong CSDL (hoặc toàn bộ bệnh nhân đã thi). Vui lòng vào phân hệ 'Cài đặt & Danh mục HIS' và bấm 'Đồng bộ Catalog HIS'.");
        }

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
                var (orderedServices, q2Instruction, serviceChange, pickedSieuAm, pickedXquang) = AllocateClinicalServices(allServicesWithGroup, rng);

                // Check if template contains question for ordering drugs & supplies
                var hasDrugOrderAction = actions.Any(a => a.Code.Equals("YL_CHI_DINH_THUOC_VTYT", StringComparison.OrdinalIgnoreCase));

                IReadOnlyList<ScenarioDrug> orderedDrugs;
                ScenarioDrugReturn? drugReturn = null;

                if (hasDrugOrderAction)
                {
                    // Allocate Drugs: Standard 3 BH (SL: 1, 2, 1) + 2 VP (SL: 2, 2), strictly checking remaining stock
                    orderedDrugs = AllocateCandidateDrugs(inStockCandidateDrugs, remainingStock, targetWh.WarehouseName, deptName, group.Count(), rng);

                    if (orderedDrugs.Count > 0)
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
                        isDirectReception && aIdx == 0,
                        actionDescMap,
                        pickedSieuAm,
                        pickedXquang);

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
        ScenarioService? pickedXquang = null) => code switch
    {
        "TN_TIEP_NHAN" or _ when isDirectReception => BuildDirectReceptionInstruction(patient, secondPatient, examDate),
        "NT_NHAN_BENH_KHOA" => $"* {secondPatient.FullName} ; Mã y tế: {secondPatient.MedicalCode}\n* {patient.FullName} ; Mã y tế: {patient.MedicalCode}\n- SVV Bn VP       : ………………………………\n- SVV Bn BHYT : ……………………………….\nSố điểm đạt được:………(đ)",
        "NT_CAP_GIUONG" => "Cấp giường cho các bệnh nhân được tiếp nhận ở câu 1.\nSố điểm đạt được:………(đ)",
        "KB_CHI_DINH_CLS" => BuildKbChiDinhClsInstruction(pickedXquang, pickedSieuAm),
        "KB_BENH_AN_NGOAI_TRU" => "Chỉ định điều trị ngoại trú và lập bệnh án ngoại trú cho bệnh nhân : ……………………………………, Mã Y tế : …………………… (2 đ)\n- Số bệnh án ngoại trú : …………………………………………………\nSố điểm đạt được:………(đ)",
        "KB_CHI_DINH_NOI_TRU" => $"Chỉ định nhập viện điều trị nội trú cho bệnh nhân : ……………………………………, Mã y tế : …………………… (1 đ)\n- Nhập khoa : {targetTransferDept}\n- Số bệnh án nội trú : ……………………………………………………\nSố điểm đạt được:………(đ)",
        "HC_TRA_CUU_THANH_TOAN" => "Kiểm tra thông tin thanh toán cho bệnh nhân : ……………………………………, Mã y tế : ……………………\n- Số bệnh án nội trú : ……………………………………………………\n- Số tiền người bệnh đã thanh toán : ……………………………………………………\n- Số tiền người bệnh đã tạm ứng : ……………………………………………………\n- Số tiền còn phải thu người bệnh : ……………………………………………………\n- Tổng số tiền người bệnh trả : ……………\nSố điểm đạt được:………(đ)",
        "YL_CHI_DINH_CLS" => q2Instruction,
        "YL_CHI_DINH_THUOC_VTYT" => "Lên y lệnh(có D/S Thuốc, VTYT đính kèm)(1.5đ)\nLập  phiếu lĩnh dược(0.5đ)\nNhập nội bộ(0.5đ)\nXuất sử dụng thuốc cho bệnh nhân(0.5đ)\nSố điểm đạt được:………(đ)",
        "YL_TRA_THUOC" => $"Bệnh nhân trả thuốc và tổng hợp phiếu trả.(Chỉ thao tác trên bệnh nhân BHYT có số vào viện ở câu 1), thuốc cần trả {drugReturn?.ReturnedDrug.Name ?? "thuốc"}, số lượng trả : {drugReturn?.ReturnQuantity ?? 1} {drugReturn?.ReturnedDrug.Unit ?? "viên"}.\nSố điểm đạt được:………(đ)",
        "YL_DOI_THEM_DICH_VU" => $"Đổi dịch vụ {serviceChange?.CanceledService.Name ?? "Dịch vụ CLS 1"} thành dịch vụ {serviceChange?.NewService.Name ?? "Dịch vụ CLS 2"} thêm 01 dịch vụ: {(serviceChange?.AddedService?.Name ?? "Chụp Xquang ngực thẳng [Số hóa 1 phim]")}. (Bệnh nhân BHYT câu 1)\nSố điểm đạt được:………(đ)",
        "TK_KIEM_TON_KHO" => $"Báo số tồn kho của một loại dược trong kho cơ số (Nguồn {inventoryDrug?.FundingSource ?? "Viện phí"}) từ ngày 01/02/2026 đến ngày 28/02/2026 : Tên dược: {inventoryDrug?.Name ?? "Thuốc/VTYT"}  số lượng tồn cuối : …………….\nSố điểm đạt được:………(đ)",
        "CK_CHUYEN_KHOA" => $"Chuyển một bệnh nhân bất kỳ từ khoa {deptName} đến {targetTransferDept}(bệnh nhân nhập viện trước {cutoffDate:dd/MM/yyyy}), Số vào viện của BN: …………………, Mã y tế :…………………., họ tên bệnh nhân:……………………………………\nSố điểm đạt được:………(đ)",
        "RV_CHO_RA_VIEN" => $"Thực hiện thao tác cho bệnh nhân ra viện(bệnh nhân nhập viện trước {cutoffDate:dd/MM/yyyy}) : Số vào viện của BN: ………………………\nSố điểm đạt được:………(đ)",
        _ => actionDescMap.TryGetValue(code, out var customDesc) && !string.IsNullOrWhiteSpace(customDesc)
            ? $"{customDesc}\nSố điểm đạt được:………(đ)"
            : $"Thực hiện nghiệp vụ chuyên môn '{name}' trên phần mềm HIS theo quy định của Bệnh viện Đa khoa Thiện Hạnh.\nSố điểm đạt được:………(đ)"
    };

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
