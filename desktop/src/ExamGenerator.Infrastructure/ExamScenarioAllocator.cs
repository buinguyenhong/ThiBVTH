using System.Globalization;
using System.Text;
using ExamGenerator.Domain;

namespace ExamGenerator.Infrastructure;

public sealed record CandidateInput(string Name, string Id, string Department, string Template, int DurationMinutes = 30, string? WarehouseCode = null, string? WarehouseName = null);

public sealed class ExamScenarioAllocator
{
    public static readonly IReadOnlyList<string> AllowedInpatientTransferDepartments = new[]
    {
        "Khoa Nội",
        "Khoa Ngoại tổng hợp",
        "Khoa Chấn thương chỉnh hình",
        "Khoa Thần kinh",
        "Gây mê hồi sức",
        "Hồi sức tích cực",
        "Khoa Tim mạch"
    };

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
        var allSnapshotDrugs = (await _database.GetAllSnapshotDrugsAsync(cancellationToken)).ToList();
        var allCatalogWarehouses = (await _database.GetWarehousesWithDepartmentAsync(cancellationToken)).ToList();

        // Detect Kho lẻ thuốc nội trú (for Thuốc) and Kho lẻ vật tư (for Vật tư y tế)
        var allKhoLeThuocRows = allSnapshotDrugs.Where(d => IsKhoLeThuocNoiTru(d.WarehouseName, d.WarehouseCode)).ToList();
        var allKhoLeVatTuRows = allSnapshotDrugs.Where(d => IsKhoLeVatTu(d.WarehouseName, d.WarehouseCode)).ToList();

        var hasKhoLeThuocConfig = allKhoLeThuocRows.Count > 0 ||
                                  allCatalogWarehouses.Any(w => IsKhoLeThuocNoiTru(w.Name, w.Code));
        var hasKhoLeVatTuConfig = allKhoLeVatTuRows.Count > 0 ||
                                  allCatalogWarehouses.Any(w => IsKhoLeVatTu(w.Name, w.Code));

        var khoLeThuocDrugs = allKhoLeThuocRows.Where(d => d.QuantityOnHand >= 1).ToList();
        var khoLeVatTuDrugs = allKhoLeVatTuRows.Where(d => d.QuantityOnHand >= 1).ToList();

        var khoLeThuocStockMap = khoLeThuocDrugs
            .GroupBy(d => d.DrugCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityOnHand), StringComparer.OrdinalIgnoreCase);

        var khoLeVatTuStockMap = khoLeVatTuDrugs
            .GroupBy(d => d.DrugCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityOnHand), StringComparer.OrdinalIgnoreCase);

        // Cache warehouse drugs and remaining stock across candidates
        var warehouseDrugsCache = new Dictionary<string, IReadOnlyList<SnapshotDrugRow>>(StringComparer.OrdinalIgnoreCase);
        var warehouseStockMap = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        var usedInpatientSoBenhAn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            // Cutoff date for Q7 & Q8: default 10 days before examDate
            var cutoffDate = examDate.AddDays(-10);

            var actionDefs = await _database.GetActionDefinitionsAsync(cancellationToken);
            var actionDescMap = actionDefs
                .Where(a => !string.IsNullOrWhiteSpace(a.Description))
                .ToDictionary(a => a.Code, a => a.Description, StringComparer.OrdinalIgnoreCase);

            var userIndex = 0;
            var seed = Math.Abs(examDate.DayNumber ^ GetDeterministicHashCode(deptName) ^ GetDeterministicHashCode(batchName));
            var rng = new Random(seed);
            // Randomly shuffle mapped users so candidates receive random distinct users
            var shuffledUsers = deptUsers.OrderBy(_ => rng.Next()).ToList();
            var usedClsCodesInDept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in group)
            {
                var assignedUser = shuffledUsers[userIndex++];

                // Find target inpatient department for Question 7 transfer (must be in AllowedInpatientTransferDepartments and different from current deptName)
                var targetTransferDept = PickTargetTransferDepartment(deptName, rng);

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
                var isMat = deptName.Contains("Mắt", StringComparison.OrdinalIgnoreCase) || tpl.Id.Equals("tpl-dd-mat", StringComparison.OrdinalIgnoreCase);
                var isTmh = deptName.Contains("Tai Mũi Họng", StringComparison.OrdinalIgnoreCase) || deptName.Contains("TMH", StringComparison.OrdinalIgnoreCase) || tpl.Id.Equals("tpl-dd-tmh", StringComparison.OrdinalIgnoreCase);
                var isRhm = deptName.Contains("Răng Hàm Mặt", StringComparison.OrdinalIgnoreCase) || deptName.Contains("RHM", StringComparison.OrdinalIgnoreCase) || tpl.Id.Equals("tpl-dd-rhm", StringComparison.OrdinalIgnoreCase);
                var isLienChuyenKhoa = isMat || isTmh || isRhm;

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
                    var gmhsPool = allInpatients.Where(ip => !string.IsNullOrWhiteSpace(ip.SoBenhAn) && !usedInpatientSoBenhAn.Contains(ip.SoBenhAn)).ToList();
                    if (gmhsPool.Count == 0) gmhsPool = allInpatients;

                    gmhsInpatientRow = gmhsPool.FirstOrDefault(ip => !string.IsNullOrWhiteSpace(ip.DepartmentName) && ip.DepartmentName.Contains(deptName, StringComparison.OrdinalIgnoreCase));
                    if (gmhsInpatientRow == null)
                    {
                        gmhsInpatientRow = gmhsPool.FirstOrDefault(ip => !string.IsNullOrWhiteSpace(ip.DepartmentName) &&
                            (ip.DepartmentName.Contains("Ngoại", StringComparison.OrdinalIgnoreCase) ||
                             ip.DepartmentName.Contains("Sản", StringComparison.OrdinalIgnoreCase) ||
                             ip.DepartmentName.Contains("Chấn thương", StringComparison.OrdinalIgnoreCase)));
                    }
                    gmhsInpatientRow ??= gmhsPool[rng.Next(gmhsPool.Count)];
                    if (!string.IsNullOrWhiteSpace(gmhsInpatientRow.SoBenhAn))
                    {
                        usedInpatientSoBenhAn.Add(gmhsInpatientRow.SoBenhAn);
                    }
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

                var gmhsAdmissionNumber = gmhsInpatientRow?.SoBenhAn ?? patient.MedicalCode;

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

                // Lọc CLS và Thủ thuật chuyên khoa cho Mắt - Tai Mũi Họng - Răng Hàm Mặt (Câu 1 & Câu 2)
                ScenarioService? pickedSpecialtyCls = null;
                ScenarioService? pickedSpecialtyThuThuat = null;
                if (isLienChuyenKhoa)
                {
                    if (isMat)
                    {
                        var matCls = allServicesWithGroup
                            .Where(s => s.GroupName.Contains("Mắt", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("đáy mắt", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("nhãn áp", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("khúc xạ", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("thị lực", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("giác mạc", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("kết mạc", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        var matTt = allServicesWithGroup
                            .Where(s => s.GroupName.Contains("Mắt", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("áp xe mi", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("giả mạc", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("cùng đồ", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("lệ đạo", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("cắt chỉ mắt", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("nặn tuyến", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("thông lệ đạo", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("bơm rửa lệ đạo", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        var cCls = matCls.Count > 0 ? matCls[rng.Next(matCls.Count)] : (Code: "CDHA_DAYMAT", Name: "Chụp đáy mắt không huỳnh quang", GroupName: "Mắt");
                        var cTt = matTt.Count > 0 ? matTt[rng.Next(matTt.Count)] : (Code: "TT_APXEMI", Name: "Rạch áp xe mi", GroupName: "Mắt");
                        pickedSpecialtyCls = new ScenarioService(cCls.Code, cCls.Name, cCls.GroupName);
                        pickedSpecialtyThuThuat = new ScenarioService(cTt.Code, cTt.Name, cTt.GroupName);
                    }
                    else if (isTmh)
                    {
                        var tmhCls = allServicesWithGroup
                            .Where(s => s.GroupName.Contains("Tai Mũi Họng", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("nội soi tai", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("nội soi mũi", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("thanh quản", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("thính lực", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("nhĩ lượng", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        var tmhTt = allServicesWithGroup
                            .Where(s => s.GroupName.Contains("Tai Mũi Họng", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("rửa mũi", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("thuốc tai", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("khí dung", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("dị vật tai", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("dị vật mũi", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("đốt cuốn mũi", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        var cCls = tmhCls.Count > 0 ? tmhCls[rng.Next(tmhCls.Count)] : (Code: "CLS_NSTMH", Name: "Nội soi tai mũi họng", GroupName: "Tai Mũi Họng");
                        var cTt = tmhTt.Count > 0 ? tmhTt[rng.Next(tmhTt.Count)] : (Code: "TT_HUTMUI", Name: "Hút rửa mũi xoang", GroupName: "Tai Mũi Họng");
                        pickedSpecialtyCls = new ScenarioService(cCls.Code, cCls.Name, cCls.GroupName);
                        pickedSpecialtyThuThuat = new ScenarioService(cTt.Code, cTt.Name, cTt.GroupName);
                    }
                    else // isRhm
                    {
                        var rhmCls = allServicesWithGroup
                            .Where(s => s.GroupName.Contains("Răng", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("X-quang răng", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("Panorama", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("toàn cảnh", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("Cephalo", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        var rhmTt = allServicesWithGroup
                            .Where(s => s.GroupName.Contains("Răng", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("cao răng", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("nhổ răng", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("trám răng", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("hàn răng", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Contains("tủy răng", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        var cCls = rhmCls.Count > 0 ? rhmCls[rng.Next(rhmCls.Count)] : (Code: "CLS_XQRANG", Name: "Chụp X-quang răng cận chóp", GroupName: "Răng Hàm Mặt");
                        var cTt = rhmTt.Count > 0 ? rhmTt[rng.Next(rhmTt.Count)] : (Code: "TT_CAORANG", Name: "Lấy cao răng và đánh bóng hai hàm", GroupName: "Răng Hàm Mặt");
                        pickedSpecialtyCls = new ScenarioService(cCls.Code, cCls.Name, cCls.GroupName);
                        pickedSpecialtyThuThuat = new ScenarioService(cTt.Code, cTt.Name, cTt.GroupName);
                    }
                }

                // Allocate Clinical Services categorized by modality (Siêu âm, Xquang, CT/MRI, Xét nghiệm)
                var (orderedServices, q2Instruction, serviceChange, pickedSieuAm, pickedXquang) = AllocateClinicalServices(allServicesWithGroup, rng, usedClsCodesInDept);

                // Check if template contains question for ordering drugs & supplies
                var hasDrugOrderAction = actions.Any(a => a.Code.Equals("YL_CHI_DINH_THUOC_VTYT", StringComparison.OrdinalIgnoreCase));

                IReadOnlyList<ScenarioDrug> orderedDrugs;
                ScenarioDrugReturn? drugReturn = null;

                if (hasDrugOrderAction)
                {
                    // Allocate Drugs: Standard 3 BH + 2 VP, or GMHS surgical supply list, or Specialty clinic supply list
                    // Checks Kho lẻ thuốc nội trú (for Thuốc) and Kho lẻ vật tư (for Vật tư y tế) when configured
                    orderedDrugs = AllocateCandidateDrugs(
                        inStockCandidateDrugs,
                        remainingStock,
                        targetWh.WarehouseCode,
                        targetWh.WarehouseName,
                        deptName,
                        group.Count(),
                        rng,
                        isGmhs,
                        isLienChuyenKhoa,
                        hasKhoLeThuocConfig,
                        khoLeThuocDrugs,
                        khoLeThuocStockMap,
                        hasKhoLeVatTuConfig,
                        khoLeVatTuDrugs,
                        khoLeVatTuStockMap);

                    if (orderedDrugs.Count > 0 && !isGmhs && !isLienChuyenKhoa)
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

                // Inventory check period item for Q6 / TK_KIEM_TON_KHO:
                // Bắt buộc phải lấy số liệu thực tế ở đúng kho của khoa dự thi (targetWh), không lấy nhầm kho của khoa khác
                var orderedDrugCodes = orderedDrugs.Select(d => d.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var deptWarehouseDrugMap = inStockCandidateDrugs
                    .GroupBy(d => d.DrugCode, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var deptPeriods = allInventoryPeriods
                    .Where(p => !string.IsNullOrWhiteSpace(p.WarehouseCode) &&
                                p.WarehouseCode.Equals(targetWh.WarehouseCode, StringComparison.OrdinalIgnoreCase) &&
                                p.ClosingStock > 0 &&
                                (deptWarehouseDrugMap.Count == 0 || deptWarehouseDrugMap.ContainsKey(p.DrugCode)))
                    .ToList();

                InventoryCheckPeriodItem? inventoryPeriodItem = null;
                ScenarioDrug inventoryCheckDrug;

                if (deptPeriods.Count > 0)
                {
                    // Ưu tiên dược/VTYT không nằm trong danh sách vừa lên y lệnh ở Câu 3 để tránh biến động tồn kho trong lúc thi
                    var nonOrderedPeriods = deptPeriods.Where(p => !orderedDrugCodes.Contains(p.DrugCode)).ToList();
                    var candidatePeriods = nonOrderedPeriods.Count > 0 ? nonOrderedPeriods : deptPeriods;
                    var vpPeriods = candidatePeriods.Where(p => p.FundingSource.Equals("Viện phí", StringComparison.OrdinalIgnoreCase)).ToList();
                    var pool = vpPeriods.Count > 0 ? vpPeriods : candidatePeriods;
                    var picked = pool[rng.Next(pool.Count)];

                    DateOnly.TryParse(picked.FromDate.Length >= 10 ? picked.FromDate[..10] : picked.FromDate, out var pFrom);
                    DateOnly.TryParse(picked.ToDate.Length >= 10 ? picked.ToDate[..10] : picked.ToDate, out var pTo);
                    if (pFrom == default) pFrom = new DateOnly(2026, 2, 1);
                    if (pTo == default) pTo = new DateOnly(2026, 2, 28);

                    var actualStock = picked.ClosingStock;
                    var actualQtyInt = (int)Math.Max(1, Math.Round(actualStock));

                    inventoryPeriodItem = new InventoryCheckPeriodItem(
                        picked.PeriodCode,
                        picked.PeriodName,
                        pFrom,
                        pTo,
                        targetWh.WarehouseCode,
                        targetWh.WarehouseName,
                        deptCode,
                        deptName,
                        picked.DrugCode,
                        picked.DrugName,
                        picked.Unit,
                        actualStock,
                        picked.FundingSource
                    );

                    inventoryCheckDrug = new ScenarioDrug(
                        picked.DrugCode,
                        picked.DrugName,
                        picked.Unit,
                        actualQtyInt,
                        "",
                        targetWh.WarehouseCode,
                        targetWh.WarehouseName,
                        picked.FundingSource
                    );
                }
                else
                {
                    // Lấy trực tiếp số liệu tồn kho thực tế từ kho của khoa (targetWh) tại thời điểm thi
                    var basePool = inStockCandidateDrugs.Count > 0 ? inStockCandidateDrugs : warehouseDrugs;
                    var nonOrderedDrugs = basePool.Where(d => !orderedDrugCodes.Contains(d.DrugCode)).ToList();
                    var candidatePool = nonOrderedDrugs.Count > 0 ? nonOrderedDrugs : basePool;
                    var vpDrugsForCheck = candidatePool.Where(d => d.FundingSource.Equals("Viện phí", StringComparison.OrdinalIgnoreCase)).ToList();
                    var invPool = vpDrugsForCheck.Count > 0 ? vpDrugsForCheck : candidatePool;
                    var invDrugRow = invPool[rng.Next(invPool.Count)];

                    var actualStock = invDrugRow.QuantityOnHand;
                    var actualQtyInt = (int)Math.Max(1, Math.Round(actualStock));

                    // Không gán kỳ giả mạo khi kho chưa có số liệu kỳ, để đề thi và đáp án thống nhất hỏi tồn kho hiện tại tại ngày thi
                    inventoryPeriodItem = null;

                    inventoryCheckDrug = new ScenarioDrug(
                        invDrugRow.DrugCode,
                        invDrugRow.DrugName,
                        invDrugRow.Unit,
                        actualQtyInt,
                        "",
                        targetWh.WarehouseCode,
                        targetWh.WarehouseName,
                        invDrugRow.FundingSource
                    );
                }

                // Inpatient and outpatient cases for lookup / advance payment / discharge
                InpatientCase? advanceCase1 = null;
                InpatientCase? advanceCase2 = null;
                InpatientCase? inLookupCase = null;
                InpatientCase? dischargeCase = null;
                OutpatientCase? outLookupCase = null;
                OutpatientCase? advanceOutpatientCase = null;

                var needsAdvanceCase = actions.Any(a =>
                    a.Code.StartsWith("LT_THU_TAM_UNG", StringComparison.OrdinalIgnoreCase) ||
                    a.Code.StartsWith("TC_THU_TAM_UNG", StringComparison.OrdinalIgnoreCase) ||
                    (isLienChuyenKhoa && a.Code.StartsWith("KB_CHI_DINH_NOI_TRU", StringComparison.OrdinalIgnoreCase)));
                var needsInLookupCase = actions.Any(a =>
                    a.Code.StartsWith("LT_TRA_CUU_THONG_TIN", StringComparison.OrdinalIgnoreCase) ||
                    a.Code.StartsWith("HC_TRA_CUU_THANH_TOAN", StringComparison.OrdinalIgnoreCase));
                var needsDischargeCase = actions.Any(a =>
                    a.Code.StartsWith("LT_THANH_TOAN_RA_VIEN", StringComparison.OrdinalIgnoreCase) ||
                    a.Code.StartsWith("TC_THANH_TOAN_RA_VIEN", StringComparison.OrdinalIgnoreCase));

                var needsInpatientCases = isGmhs || needsAdvanceCase || needsInLookupCase || needsDischargeCase;
                var needsOutpatientCases = actions.Any(a =>
                    a.Code.StartsWith("LT_THU_TAM_UNG_NOI_TRU", StringComparison.OrdinalIgnoreCase) ||
                    a.Code.StartsWith("LT_TRA_CUU_THONG_TIN", StringComparison.OrdinalIgnoreCase) ||
                    a.Code.StartsWith("TC_TRA_CUU_BENH_SU", StringComparison.OrdinalIgnoreCase) ||
                    (isLienChuyenKhoa && a.Code.StartsWith("KB_BENH_AN_NGOAI_TRU", StringComparison.OrdinalIgnoreCase)));

                if (needsInpatientCases && allInpatients.Count == 0)
                {
                    throw new InvalidOperationException($"Mẫu đề '{tpl.Name}' có câu hỏi yêu cầu dữ liệu Bệnh án nội trú (Thu tạm ứng / Thanh toán ra viện / Tra cứu buồng giường / GMHS), nhưng CSDL chưa có dữ liệu Bệnh án nội trú nào. Vui lòng vào phân hệ 'Cài đặt & Danh mục HIS' và bấm 'Đồng bộ Catalog HIS'.");
                }

                if (needsOutpatientCases && allOutpatients.Count == 0)
                {
                    throw new InvalidOperationException($"Mẫu đề '{tpl.Name}' có câu hỏi yêu cầu dữ liệu Bệnh án ngoại trú (Tạm ứng ngoại trú / Tra cứu lịch sử KCB / Bệnh án ngoại trú), nhưng CSDL chưa có dữ liệu Bệnh án ngoại trú nào. Vui lòng vào phân hệ 'Cài đặt & Danh mục HIS' và bấm 'Đồng bộ Catalog HIS'.");
                }

                if (allInpatients.Count > 0)
                {
                    var inPool = allInpatients.OrderBy(_ => rng.Next()).ToList();
                    var candidateUsedSoBenhAn = new HashSet<string>(usedInpatientSoBenhAn, StringComparer.OrdinalIgnoreCase);

                    // Ưu tiên chọn bệnh án cho Câu 6 (Thanh toán ra viện) trước để đảm bảo hoàn toàn độc lập với mọi số bệnh án khác trong đợt thi
                    var distinctDischargeRow = PickDistinctInpatient(inPool, candidateUsedSoBenhAn, markGlobal: needsDischargeCase ? usedInpatientSoBenhAn : null);
                    dischargeCase = MapInpatientRow(distinctDischargeRow);

                    var advRow1 = PickDistinctInpatient(inPool, candidateUsedSoBenhAn, markGlobal: needsAdvanceCase ? usedInpatientSoBenhAn : null);
                    advanceCase1 = MapInpatientRow(advRow1);

                    var advRow2 = inPool.Count > 1
                        ? PickDistinctInpatient(inPool, candidateUsedSoBenhAn, markGlobal: needsAdvanceCase ? usedInpatientSoBenhAn : null)
                        : advRow1;
                    advanceCase2 = MapInpatientRow(advRow2);

                    var lookupRow = PickDistinctInpatient(
                        inPool,
                        candidateUsedSoBenhAn,
                        c => !string.IsNullOrWhiteSpace(c.BedCode) && !string.IsNullOrWhiteSpace(c.RoomName),
                        markGlobal: needsInLookupCase ? usedInpatientSoBenhAn : null);
                    inLookupCase = MapInpatientRow(lookupRow);
                }

                if (allOutpatients.Count > 0)
                {
                    var outPool = allOutpatients.OrderBy(_ => rng.Next()).ToList();
                    outLookupCase = MapOutpatientRow(outPool[0 % outPool.Count]);
                    advanceOutpatientCase = outPool.Count > 1
                        ? MapOutpatientRow(outPool[1 % outPool.Count])
                        : outLookupCase;
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
                        isGmhs,
                        isLienChuyenKhoa,
                        pickedSpecialtyCls,
                        pickedSpecialtyThuThuat,
                        orderedServices);

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
        string warehouseCode,
        string warehouseName,
        string departmentName,
        int totalCandidates,
        Random rng,
        bool isGmhs = false,
        bool isLienChuyenKhoa = false,
        bool hasKhoLeThuocConfig = false,
        IReadOnlyList<SnapshotDrugRow>? khoLeThuocDrugs = null,
        Dictionary<string, double>? khoLeThuocStockMap = null,
        bool hasKhoLeVatTuConfig = false,
        IReadOnlyList<SnapshotDrugRow>? khoLeVatTuDrugs = null,
        Dictionary<string, double>? khoLeVatTuStockMap = null)
    {
        var orderedDrugs = new List<ScenarioDrug>();
        var alreadyPickedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pattern for allocation:
        // - Standard inpatient/clinical: 3 BHYT (Thuốc - Kho lẻ thuốc nội trú) + 2 Viện phí (VTYT - Kho lẻ vật tư)
        // - GMHS: 3 Thuốc + 3 VTYT (hoặc theo kho lẻ khả dụng)
        // - Liên chuyên khoa: 4 Thuốc + 4 VTYT (hoặc theo kho lẻ khả dụng)
        (string PreferredSource, int DesiredQty, bool IsSupplySlot)[] preferredSlots;
        if (isGmhs)
        {
            preferredSlots = new (string PreferredSource, int DesiredQty, bool IsSupplySlot)[]
            {
                ("Viện phí", 1, false),
                ("Viện phí", 1, false),
                ("Viện phí", 2, false),
                ("Viện phí", 5, true),
                ("Viện phí", 2, true),
                ("Viện phí", 2, true)
            };
        }
        else if (isLienChuyenKhoa)
        {
            preferredSlots = new (string PreferredSource, int DesiredQty, bool IsSupplySlot)[]
            {
                ("Viện phí", 1, false),
                ("Viện phí", 1, false),
                ("Viện phí", 1, false),
                ("Viện phí", 1, false),
                ("Viện phí", 1, true),
                ("Viện phí", 1, true),
                ("Viện phí", 1, true),
                ("Viện phí", 1, true)
            };
        }
        else
        {
            preferredSlots = new (string PreferredSource, int DesiredQty, bool IsSupplySlot)[]
            {
                ("BHYT", 1, false),
                ("BHYT", 2, false),
                ("BHYT", 1, false),
                ("Viện phí", 2, true),
                ("Viện phí", 2, true)
            };
        }

        var availablePoolCount = allWarehouseDrugs.Count;
        if (hasKhoLeThuocConfig && khoLeThuocDrugs is not null)
        {
            availablePoolCount = Math.Max(availablePoolCount, khoLeThuocDrugs.Count + (khoLeVatTuDrugs?.Count ?? 0));
        }
        else if (hasKhoLeVatTuConfig && khoLeVatTuDrugs is not null)
        {
            availablePoolCount = Math.Max(availablePoolCount, khoLeVatTuDrugs.Count + allWarehouseDrugs.Count);
        }

        var targetCount = Math.Min(preferredSlots.Length, Math.Max(1, availablePoolCount));

        for (var slotIdx = 0; slotIdx < targetCount; slotIdx++)
        {
            var (prefSource, desiredQty, isSupplySlot) = preferredSlots[slotIdx];

            // Determine if this slot must be verified against Kho lẻ thuốc nội trú (Thuốc) or Kho lẻ vật tư (VTYT)
            var checkKhoLeThuoc = !isSupplySlot && hasKhoLeThuocConfig;
            var checkKhoLeVatTu = isSupplySlot && hasKhoLeVatTuConfig;

            if (checkKhoLeThuoc)
            {
                if (khoLeThuocDrugs == null || khoLeThuocStockMap == null || khoLeThuocDrugs.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Kho lẻ thuốc nội trú không có tồn kho thuốc khả dụng để xuất phiếu lĩnh cho khoa '{departmentName}'. Vui lòng kiểm tra/bổ sung tồn kho tại Kho lẻ thuốc nội trú trên HIS.");
                }

                var chosen = PickDrugWithCentralWarehouseCheck(
                    allWarehouseDrugs,
                    remainingStock,
                    khoLeThuocDrugs,
                    khoLeThuocStockMap,
                    alreadyPickedCodes,
                    prefSource,
                    desiredQty,
                    expectSupply: false,
                    rng);

                if (chosen is null)
                {
                    throw new InvalidOperationException(
                        $"Kho lẻ thuốc nội trú không đủ tồn kho thuốc khả dụng để xuất phiếu lĩnh cho {totalCandidates} thí sinh khoa '{departmentName}'. Vui lòng cập nhật/bổ sung thêm tồn kho tại Kho lẻ thuốc nội trú trên HIS.");
                }

                var centralAvail = khoLeThuocStockMap.GetValueOrDefault(chosen.DrugCode, 0);
                var localAvail = remainingStock.TryGetValue(chosen.DrugCode, out var lRem) ? lRem : centralAvail;
                var avail = Math.Min(centralAvail, localAvail > 0 ? localAvail : centralAvail);
                var actualQty = avail >= desiredQty ? desiredQty : (int)Math.Max(1, avail);

                khoLeThuocStockMap[chosen.DrugCode] = Math.Max(0, centralAvail - actualQty);
                if (remainingStock.ContainsKey(chosen.DrugCode))
                {
                    remainingStock[chosen.DrugCode] = Math.Max(0, remainingStock[chosen.DrugCode] - actualQty);
                }
                alreadyPickedCodes.Add(chosen.DrugCode);

                orderedDrugs.Add(CreateScenarioDrugItem(chosen, actualQty, warehouseCode, warehouseName, isGmhs, isLienChuyenKhoa));
                continue;
            }

            if (checkKhoLeVatTu)
            {
                if (khoLeVatTuDrugs == null || khoLeVatTuStockMap == null || khoLeVatTuDrugs.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Kho lẻ vật tư không có tồn kho vật tư y tế khả dụng để xuất phiếu lĩnh cho khoa '{departmentName}'. Vui lòng kiểm tra/bổ sung tồn kho tại Kho lẻ vật tư trên HIS.");
                }

                var chosen = PickDrugWithCentralWarehouseCheck(
                    allWarehouseDrugs,
                    remainingStock,
                    khoLeVatTuDrugs,
                    khoLeVatTuStockMap,
                    alreadyPickedCodes,
                    prefSource,
                    desiredQty,
                    expectSupply: true,
                    rng);

                if (chosen is null)
                {
                    throw new InvalidOperationException(
                        $"Kho lẻ vật tư không đủ tồn kho vật tư y tế khả dụng để xuất phiếu lĩnh cho {totalCandidates} thí sinh khoa '{departmentName}'. Vui lòng cập nhật/bổ sung thêm tồn kho tại Kho lẻ vật tư trên HIS.");
                }

                var centralAvail = khoLeVatTuStockMap.GetValueOrDefault(chosen.DrugCode, 0);
                var localAvail = remainingStock.TryGetValue(chosen.DrugCode, out var lRem) ? lRem : centralAvail;
                var avail = Math.Min(centralAvail, localAvail > 0 ? localAvail : centralAvail);
                var actualQty = avail >= desiredQty ? desiredQty : (int)Math.Max(1, avail);

                khoLeVatTuStockMap[chosen.DrugCode] = Math.Max(0, centralAvail - actualQty);
                if (remainingStock.ContainsKey(chosen.DrugCode))
                {
                    remainingStock[chosen.DrugCode] = Math.Max(0, remainingStock[chosen.DrugCode] - actualQty);
                }
                alreadyPickedCodes.Add(chosen.DrugCode);

                orderedDrugs.Add(CreateScenarioDrugItem(chosen, actualQty, warehouseCode, warehouseName, isGmhs, isLienChuyenKhoa));
                continue;
            }

            // Fallback when central Kho lẻ thuốc nội trú / Kho lẻ vật tư is not configured for this slot:
            // Still verify if an item in targetWh belongs to a configured central warehouse
            bool MatchesSlotType(SnapshotDrugRow d) => IsMedicalSupplyItem(d) == isSupplySlot;

            bool IsEligibleInTargetWh(SnapshotDrugRow d, int minQty, bool enforceType = true)
            {
                if (enforceType && !MatchesSlotType(d))
                    return false;

                if (!remainingStock.TryGetValue(d.DrugCode, out var rem) || rem < minQty)
                    return false;

                var isSupply = IsMedicalSupplyItem(d);
                if (isSupply && hasKhoLeVatTuConfig)
                {
                    return khoLeVatTuStockMap != null && khoLeVatTuStockMap.TryGetValue(d.DrugCode, out var cRem) && cRem >= minQty;
                }
                if (!isSupply && hasKhoLeThuocConfig)
                {
                    return khoLeThuocStockMap != null && khoLeThuocStockMap.TryGetValue(d.DrugCode, out var cRem) && cRem >= minQty;
                }
                return true;
            }

            // 1. Try finding available items in preferred funding source with remaining stock >= desiredQty matching slot type
            var candidateItems = allWarehouseDrugs
                .Where(d => !alreadyPickedCodes.Contains(d.DrugCode)
                            && d.FundingSource.Equals(prefSource, StringComparison.OrdinalIgnoreCase)
                            && IsEligibleInTargetWh(d, desiredQty, enforceType: true))
                .ToList();

            // 2. If no item has >= desiredQty, try items in preferred source with remaining stock >= 1
            if (candidateItems.Count == 0)
            {
                candidateItems = allWarehouseDrugs
                    .Where(d => !alreadyPickedCodes.Contains(d.DrugCode)
                                && d.FundingSource.Equals(prefSource, StringComparison.OrdinalIgnoreCase)
                                && IsEligibleInTargetWh(d, 1, enforceType: true))
                    .ToList();
            }

            // 3. If still empty in preferred source, fallback to any item not yet picked with remaining stock >= desiredQty
            if (candidateItems.Count == 0)
            {
                candidateItems = allWarehouseDrugs
                    .Where(d => !alreadyPickedCodes.Contains(d.DrugCode)
                                && IsEligibleInTargetWh(d, desiredQty, enforceType: true))
                    .ToList();
            }

            // 4. If still empty, fallback to any item not yet picked with remaining stock >= 1
            if (candidateItems.Count == 0)
            {
                candidateItems = allWarehouseDrugs
                    .Where(d => !alreadyPickedCodes.Contains(d.DrugCode)
                                && IsEligibleInTargetWh(d, 1, enforceType: true))
                    .ToList();
            }

            // 5. If all warehouse items matching type were already picked by this candidate, allow reuse if remaining stock >= 1
            if (candidateItems.Count == 0)
            {
                candidateItems = allWarehouseDrugs
                    .Where(d => IsEligibleInTargetWh(d, 1, enforceType: true))
                    .ToList();
            }

            // 6. Relax type matching if pool has no items of that specific type (backward compatibility fallback)
            if (candidateItems.Count == 0)
            {
                candidateItems = allWarehouseDrugs
                    .Where(d => IsEligibleInTargetWh(d, 1, enforceType: false))
                    .ToList();
            }

            if (candidateItems.Count == 0)
            {
                throw new InvalidOperationException($"Kho thi '{warehouseName}' của khoa '{departmentName}' không đủ tồn kho khả dụng để phân bổ cho {totalCandidates} thí sinh (Tổng số lượng tồn bị thiếu hụt khi nhiều thí sinh cùng xuất y lệnh). Vui lòng cập nhật/bổ sung thêm tồn kho trên HIS.");
            }

            // Pick an item (randomized to spread among candidates if multiple items exist)
            var fallbackChosen = candidateItems[rng.Next(candidateItems.Count)];
            var fallbackAvail = remainingStock[fallbackChosen.DrugCode];
            if (hasKhoLeThuocConfig && khoLeThuocStockMap != null && khoLeThuocStockMap.TryGetValue(fallbackChosen.DrugCode, out var ktRem))
            {
                fallbackAvail = Math.Min(fallbackAvail, ktRem);
            }
            if (hasKhoLeVatTuConfig && khoLeVatTuStockMap != null && khoLeVatTuStockMap.TryGetValue(fallbackChosen.DrugCode, out var kvRem))
            {
                fallbackAvail = Math.Min(fallbackAvail, kvRem);
            }

            var fallbackActualQty = fallbackAvail >= desiredQty ? desiredQty : (int)Math.Max(1, fallbackAvail);

            // Deduct from remaining stock so subsequent candidates will never exceed QuantityOnHand
            remainingStock[fallbackChosen.DrugCode] -= fallbackActualQty;
            if (khoLeThuocStockMap != null && khoLeThuocStockMap.ContainsKey(fallbackChosen.DrugCode))
            {
                khoLeThuocStockMap[fallbackChosen.DrugCode] = Math.Max(0, khoLeThuocStockMap[fallbackChosen.DrugCode] - fallbackActualQty);
            }
            if (khoLeVatTuStockMap != null && khoLeVatTuStockMap.ContainsKey(fallbackChosen.DrugCode))
            {
                khoLeVatTuStockMap[fallbackChosen.DrugCode] = Math.Max(0, khoLeVatTuStockMap[fallbackChosen.DrugCode] - fallbackActualQty);
            }
            alreadyPickedCodes.Add(fallbackChosen.DrugCode);

            orderedDrugs.Add(CreateScenarioDrugItem(fallbackChosen, fallbackActualQty, warehouseCode, warehouseName, isGmhs, isLienChuyenKhoa));
        }

        return orderedDrugs;
    }

    private static SnapshotDrugRow? PickDrugWithCentralWarehouseCheck(
        IReadOnlyList<SnapshotDrugRow> departmentWarehouseDrugs,
        Dictionary<string, double> departmentStockMap,
        IReadOnlyList<SnapshotDrugRow> centralWarehouseDrugs,
        Dictionary<string, double> centralStockMap,
        HashSet<string> alreadyPickedCodes,
        string preferredSource,
        int desiredQty,
        bool expectSupply,
        Random rng)
    {
        bool MatchesType(SnapshotDrugRow d) => IsMedicalSupplyItem(d) == expectSupply;

        bool HasCentralAndDeptStock(SnapshotDrugRow d, int minQty, bool enforceType = true) =>
            (!enforceType || MatchesType(d)) &&
            centralStockMap.TryGetValue(d.DrugCode, out var cRem) && cRem >= minQty &&
            departmentStockMap.TryGetValue(d.DrugCode, out var dRem) && dRem >= minQty;

        bool HasCentralStock(SnapshotDrugRow d, int minQty, bool enforceType = true) =>
            (!enforceType || MatchesType(d)) &&
            centralStockMap.TryGetValue(d.DrugCode, out var cRem) && cRem >= minQty;

        // 1. Prefer items present in BOTH department warehouse and central Kho lẻ with preferred source and stock >= desiredQty
        var candidates = departmentWarehouseDrugs
            .Where(d => !alreadyPickedCodes.Contains(d.DrugCode) &&
                        d.FundingSource.Equals(preferredSource, StringComparison.OrdinalIgnoreCase) &&
                        HasCentralAndDeptStock(d, desiredQty, enforceType: true))
            .ToList();

        // 2. Items in central Kho lẻ with preferred source and stock >= desiredQty
        if (candidates.Count == 0)
        {
            candidates = centralWarehouseDrugs
                .Where(d => !alreadyPickedCodes.Contains(d.DrugCode) &&
                            d.FundingSource.Equals(preferredSource, StringComparison.OrdinalIgnoreCase) &&
                            HasCentralStock(d, desiredQty, enforceType: true))
                .ToList();
        }

        // 3. Items in BOTH department warehouse and central Kho lẻ with stock >= desiredQty (any source)
        if (candidates.Count == 0)
        {
            candidates = departmentWarehouseDrugs
                .Where(d => !alreadyPickedCodes.Contains(d.DrugCode) &&
                            HasCentralAndDeptStock(d, desiredQty, enforceType: true))
                .ToList();
        }

        // 4. Items in central Kho lẻ with stock >= desiredQty (any source)
        if (candidates.Count == 0)
        {
            candidates = centralWarehouseDrugs
                .Where(d => !alreadyPickedCodes.Contains(d.DrugCode) &&
                            HasCentralStock(d, desiredQty, enforceType: true))
                .ToList();
        }

        // 5. Items in BOTH or central Kho lẻ with stock >= 1 (preferred source, then any source)
        if (candidates.Count == 0)
        {
            candidates = departmentWarehouseDrugs
                .Where(d => !alreadyPickedCodes.Contains(d.DrugCode) &&
                            d.FundingSource.Equals(preferredSource, StringComparison.OrdinalIgnoreCase) &&
                            HasCentralAndDeptStock(d, 1, enforceType: true))
                .ToList();
        }
        if (candidates.Count == 0)
        {
            candidates = centralWarehouseDrugs
                .Where(d => !alreadyPickedCodes.Contains(d.DrugCode) &&
                            d.FundingSource.Equals(preferredSource, StringComparison.OrdinalIgnoreCase) &&
                            HasCentralStock(d, 1, enforceType: true))
                .ToList();
        }
        if (candidates.Count == 0)
        {
            candidates = centralWarehouseDrugs
                .Where(d => !alreadyPickedCodes.Contains(d.DrugCode) &&
                            HasCentralStock(d, 1, enforceType: true))
                .ToList();
        }

        // 6. Allow reuse if all distinct items in central Kho lẻ were already picked by this candidate
        if (candidates.Count == 0)
        {
            candidates = centralWarehouseDrugs
                .Where(d => HasCentralStock(d, 1, enforceType: true))
                .ToList();
        }

        // 7. Fallback relaxation if central warehouse has stock but no items strictly matching expectSupply
        if (candidates.Count == 0)
        {
            candidates = centralWarehouseDrugs
                .Where(d => HasCentralStock(d, 1, enforceType: false))
                .ToList();
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates[rng.Next(candidates.Count)];
    }

    private static ScenarioDrug CreateScenarioDrugItem(
        SnapshotDrugRow chosen,
        int actualQty,
        string targetWarehouseCode,
        string targetWarehouseName,
        bool isGmhs,
        bool isLienChuyenKhoa)
    {
        var usage = isGmhs
            ? "Dùng trong phẫu thuật"
            : (isLienChuyenKhoa
                ? "Cơ số tủ trực"
                : (chosen.Unit.Contains("Chai", StringComparison.OrdinalIgnoreCase)
                    ? "Truyền tĩnh mạch XL giọt/phút"
                    : (chosen.Unit.Contains("Lọ", StringComparison.OrdinalIgnoreCase) || chosen.Unit.Contains("Ống", StringComparison.OrdinalIgnoreCase)
                        ? "Tiêm bắp 01 ống lúc 08h00"
                        : "Sáng 1 viên, chiều 1 viên sau ăn")));

        var whCode = !string.IsNullOrWhiteSpace(targetWarehouseCode) ? targetWarehouseCode : chosen.WarehouseCode;
        var whName = !string.IsNullOrWhiteSpace(targetWarehouseName) ? targetWarehouseName : chosen.WarehouseName;

        return new ScenarioDrug(
            chosen.DrugCode,
            chosen.DrugName,
            chosen.Unit,
            actualQty,
            usage,
            whCode,
            whName,
            chosen.FundingSource);
    }

    public static string PickTargetTransferDepartment(string currentDeptName, Random rng)
    {
        var validTargets = AllowedInpatientTransferDepartments
            .Where(target => !IsSameInpatientDepartment(currentDeptName, target))
            .ToList();

        if (validTargets.Count == 0)
        {
            validTargets = AllowedInpatientTransferDepartments.ToList();
        }

        return validTargets[rng.Next(validTargets.Count)];
    }

    public static bool IsSameInpatientDepartment(string? currentDeptName, string? targetDeptName)
    {
        if (string.IsNullOrWhiteSpace(currentDeptName) || string.IsNullOrWhiteSpace(targetDeptName))
            return false;

        var cur = NormalizeComparableText(currentDeptName);
        var tgt = NormalizeComparableText(targetDeptName);

        if (cur.Equals(tgt, StringComparison.Ordinal))
            return true;

        var curCore = StripDepartmentPrefix(cur);
        var tgtCore = StripDepartmentPrefix(tgt);

        if (curCore.Equals(tgtCore, StringComparison.Ordinal))
            return true;

        // Check specific inpatient department aliases
        return tgtCore switch
        {
            "noi" => curCore is "noi" or "noi tong hop" or "noi khoa" || curCore.StartsWith("noi ", StringComparison.Ordinal) && !curCore.Contains("than kinh", StringComparison.Ordinal) && !curCore.Contains("tim mach", StringComparison.Ordinal),
            "ngoai tong hop" => curCore is "ngoai" or "ngoai tong hop" or "ngoai chung" || (curCore.StartsWith("ngoai ", StringComparison.Ordinal) && !curCore.Contains("than kinh", StringComparison.Ordinal) && !curCore.Contains("chan thuong", StringComparison.Ordinal)),
            "chan thuong chinh hinh" => curCore.Contains("chan thuong", StringComparison.Ordinal) || curCore.Contains("chinh hinh", StringComparison.Ordinal) || curCore is "ctch",
            "than kinh" => curCore.Contains("than kinh", StringComparison.Ordinal),
            "gay me hoi suc" => curCore.Contains("gay me", StringComparison.Ordinal) || curCore is "gmhs",
            "hoi suc tich cuc" => curCore.Contains("hoi suc tich cuc", StringComparison.Ordinal) || curCore.Contains("tich cuc", StringComparison.Ordinal) || curCore is "hstc" or "hs tc" or "hs-tc",
            "tim mach" => curCore.Contains("tim mach", StringComparison.Ordinal),
            _ => false
        };
    }

    public static bool IsKhoLeThuocNoiTru(string? warehouseName, string? warehouseCode = null)
    {
        var name = NormalizeComparableText(warehouseName);
        var code = NormalizeComparableText(warehouseCode);

        if (name.Contains("kho le thuoc noi tru", StringComparison.Ordinal) ||
            name.Contains("le thuoc noi tru", StringComparison.Ordinal) ||
            name.Contains("thuoc le noi tru", StringComparison.Ordinal) ||
            (name.Contains("kho le", StringComparison.Ordinal) && name.Contains("thuoc", StringComparison.Ordinal) && name.Contains("noi tru", StringComparison.Ordinal)))
        {
            return true;
        }

        return code is "klnt" or "kltnt" or "kho_le_thuoc_noi_tru" or "kholethuocnoitru" ||
               code.Contains("kholethuocnoitru", StringComparison.Ordinal) ||
               code.Contains("lethuocnoitru", StringComparison.Ordinal);
    }

    public static bool IsKhoLeVatTu(string? warehouseName, string? warehouseCode = null)
    {
        var name = NormalizeComparableText(warehouseName);
        var code = NormalizeComparableText(warehouseCode);

        if (name.Contains("kho le vat tu", StringComparison.Ordinal) ||
            name.Contains("le vat tu", StringComparison.Ordinal) ||
            name.Contains("kho le vtyt", StringComparison.Ordinal) ||
            name.Contains("vat tu le", StringComparison.Ordinal) ||
            (name.Contains("kho le", StringComparison.Ordinal) && (name.Contains("vat tu", StringComparison.Ordinal) || name.Contains("vtyt", StringComparison.Ordinal))))
        {
            return true;
        }

        return code is "klvt" or "klvtyt" or "kho_le_vat_tu" or "kholevattu" ||
               code.Contains("kholevattu", StringComparison.Ordinal) ||
               code.Contains("levattu", StringComparison.Ordinal) ||
               code.Contains("kholevtyt", StringComparison.Ordinal);
    }

    public static bool IsMedicalSupplyItem(SnapshotDrugRow item)
    {
        // 1. Deterministic classification via eHospital DM_LoaiDuoc.LoaiVatTu_Id
        if (!string.IsNullOrWhiteSpace(item.ItemType))
        {
            if (item.ItemType.Equals("VTYT", StringComparison.OrdinalIgnoreCase) ||
                item.ItemType.Equals("TTBYT", StringComparison.OrdinalIgnoreCase) ||
                item.ItemType.Equals("HOACHAT", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (item.ItemType.Equals("THUOC", StringComparison.OrdinalIgnoreCase) ||
                item.ItemType.Equals("VACCINE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 2. Warehouse identification
        if (IsKhoLeVatTu(item.WarehouseName, item.WarehouseCode))
            return true;
        if (IsKhoLeThuocNoiTru(item.WarehouseName, item.WarehouseCode))
            return false;

        // 3. Heuristic fallback by Item Name and Unit (for backward compatibility / mock data)
        var name = NormalizeComparableText(item.DrugName);
        var unit = NormalizeComparableText(item.Unit);

        if (name.Contains("bom tiem", StringComparison.Ordinal) ||
            name.Contains("kim tiem", StringComparison.Ordinal) ||
            name.Contains("kim luon", StringComparison.Ordinal) ||
            name.Contains("day truyen", StringComparison.Ordinal) ||
            name.Contains("day dich truyen", StringComparison.Ordinal) ||
            name.Contains("day garo", StringComparison.Ordinal) ||
            name.Contains("gang tay", StringComparison.Ordinal) ||
            name.Contains("bong ", StringComparison.Ordinal) ||
            name.StartsWith("gac ", StringComparison.Ordinal) ||
            name.Contains(" gac ", StringComparison.Ordinal) ||
            name.Contains("bang cuon", StringComparison.Ordinal) ||
            name.Contains("bang dinh", StringComparison.Ordinal) ||
            name.Contains("chi phau thuat", StringComparison.Ordinal) ||
            name.StartsWith("chi ", StringComparison.Ordinal) ||
            name.Contains("luoi dao", StringComparison.Ordinal) ||
            name.Contains("ao phau thuat", StringComparison.Ordinal) ||
            name.Contains("catheter", StringComparison.Ordinal) ||
            name.Contains("sonde", StringComparison.Ordinal) ||
            name.Contains("ong thong", StringComparison.Ordinal) ||
            name.Contains("ong hut", StringComparison.Ordinal) ||
            name.Contains("ong noi khi quan", StringComparison.Ordinal) ||
            name.Contains("mask ", StringComparison.Ordinal) ||
            name.Contains("mat na", StringComparison.Ordinal) ||
            name.Contains("dien cuc", StringComparison.Ordinal) ||
            name.Contains("tui nuoc tieu", StringComparison.Ordinal) ||
            name.Contains("vat tu", StringComparison.Ordinal) ||
            name.Contains("vtyt", StringComparison.Ordinal))
        {
            return true;
        }

        return unit is "cai" or "bo" or "doi" or "cuon" or "mieng" or "tep" or "soi";
    }

    private static string StripDepartmentPrefix(string normalizedDept)
    {
        var s = normalizedDept.Trim();
        if (s.StartsWith("khoa phau thuat ", StringComparison.Ordinal))
            return s["khoa phau thuat ".Length..].Trim();
        if (s.StartsWith("khoa ", StringComparison.Ordinal))
            return s["khoa ".Length..].Trim();
        return s;
    }

    private static string NormalizeComparableText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var normalized = input.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            var mapped = c switch
            {
                'đ' => 'd',
                'Đ' => 'd',
                _ => char.ToLowerInvariant(c)
            };
            sb.Append(mapped);
        }

        return string.Join(' ', sb.ToString().Split(new[] { ' ', '\t', '\r', '\n', '_', '-' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static (List<ScenarioService> OrderedServices, string Q2Instruction, ScenarioServiceChange ServiceChange, ScenarioService? PickedSieuAm, ScenarioService? PickedXquang) AllocateClinicalServices(
        IReadOnlyList<(string Code, string Name, string GroupName)> allServices,
        Random rng,
        HashSet<string>? usedClsCodesInDept = null)
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

            if (IsExcludedFromRoutineCls(name, grp))
            {
                continue;
            }

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
                     name.Contains("CLVT", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("CT ", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("CT-", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("CT,", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Cộng hưởng từ", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("MRI", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("Cắt lớp", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("CLVT", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("Cộng hưởng từ", StringComparison.OrdinalIgnoreCase) ||
                     grp.Contains("MRI", StringComparison.OrdinalIgnoreCase) ||
                     grp.Equals("CT", StringComparison.OrdinalIgnoreCase) ||
                     grp.StartsWith("CT ", StringComparison.OrdinalIgnoreCase) ||
                     grp.StartsWith("CT/", StringComparison.OrdinalIgnoreCase) ||
                     grp.StartsWith("CT-", StringComparison.OrdinalIgnoreCase))
            {
                ctMriPool.Add(item);
            }
            else if (IsLaboratoryService(name, grp))
            {
                xetNghiemPool.Add(item);
            }
            else
            {
                otherPool.Add(item);
            }
        }

        // Pick random items per category (preferring services not yet assigned to previous candidates in the same batch/department)
        var pickedSieuAm = PickDistinctFromPool(sieuAmPool, rng, usedClsCodesInDept);
        var pickedXquang = PickDistinctFromPool(xquangPool, rng, usedClsCodesInDept);
        var pickedCtMri = PickDistinctFromPool(ctMriPool, rng, usedClsCodesInDept);

        var targetXnCount = Math.Min(6, xetNghiemPool.Count);
        var pickedXetNghiem = PickMultipleDistinctFromPool(xetNghiemPool, targetXnCount, rng, usedClsCodesInDept);

        var orderedServices = new List<ScenarioService>();
        if (pickedSieuAm is not null) orderedServices.Add(pickedSieuAm);
        if (pickedXquang is not null) orderedServices.Add(pickedXquang);
        if (pickedCtMri is not null) orderedServices.Add(pickedCtMri);
        orderedServices.AddRange(pickedXetNghiem);

        // If total ordered services < 3 (e.g. limited test catalog), fill from otherPool or allServices
        if (orderedServices.Count < 3)
        {
            var fallbackCandidates = otherPool
                .Concat(allServices.Select(s => new ScenarioService(s.Code, s.Name.Trim(), s.GroupName.Trim())))
                .Where(s => !orderedServices.Any(o => o.Code.Equals(s.Code, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            while (orderedServices.Count < 3 && fallbackCandidates.Count > 0)
            {
                var idx = rng.Next(fallbackCandidates.Count);
                var extra = fallbackCandidates[idx];
                orderedServices.Add(extra);
                fallbackCandidates.RemoveAt(idx);
            }
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
        var availSieuAm = sieuAmPool.Where(s => !orderedServices.Any(o => o.Code.Equals(s.Code, StringComparison.OrdinalIgnoreCase))).ToList();
        if (availSieuAm.Count > 0)
        {
            newService = availSieuAm[rng.Next(availSieuAm.Count)];
        }
        else
        {
            var availOther = otherPool
                .Concat(allServices.Select(s => new ScenarioService(s.Code, s.Name.Trim(), s.GroupName.Trim())))
                .Where(s => !orderedServices.Any(o => o.Code.Equals(s.Code, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            newService = availOther.Count > 0
                ? availOther[rng.Next(availOther.Count)]
                : new ScenarioService("SA_BUNG", "Siêu âm bụng tổng quát", "Chẩn đoán hình ảnh");
        }

        ScenarioService addedService;
        var availXquang = xquangPool.Where(s => !orderedServices.Any(o => o.Code.Equals(s.Code, StringComparison.OrdinalIgnoreCase)) && !s.Code.Equals(newService.Code, StringComparison.OrdinalIgnoreCase)).ToList();
        if (availXquang.Count > 0)
        {
            addedService = availXquang[rng.Next(availXquang.Count)];
        }
        else
        {
            var availOther = otherPool
                .Concat(allServices.Select(s => new ScenarioService(s.Code, s.Name.Trim(), s.GroupName.Trim())))
                .Where(s => !orderedServices.Any(o => o.Code.Equals(s.Code, StringComparison.OrdinalIgnoreCase)) && !s.Code.Equals(newService.Code, StringComparison.OrdinalIgnoreCase))
                .ToList();
            addedService = availOther.Count > 0
                ? availOther[rng.Next(availOther.Count)]
                : new ScenarioService("XQ_COCHAN", "Chụp Xquang xương cổ chân thẳng, nghiêng [Số hóa 2 phim]", "Chẩn đoán hình ảnh");
        }

        var serviceChange = new ScenarioServiceChange(canceledService, newService, addedService);
        return (orderedServices, q2Instruction, serviceChange, pickedSieuAm, pickedXquang);
    }

    private static bool IsExcludedFromRoutineCls(string name, string groupName)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;

        if (name.Contains("[NGƯNG]", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("(NGƯNG)", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("[KHÔNG DÙNG]", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("(KHÔNG DÙNG)", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("(K DÙNG)", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("[BỎ]", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("[HỦY]", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("*NGƯNG*", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (groupName.Contains("Phẫu thuật", StringComparison.OrdinalIgnoreCase) ||
            groupName.Contains("Công khám", StringComparison.OrdinalIgnoreCase) ||
            groupName.Contains("Khám bệnh", StringComparison.OrdinalIgnoreCase) ||
            groupName.Contains("Giường", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Phẫu thuật ", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Khám ", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Giường ", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Ngày giường", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsLaboratoryService(string name, string grp)
    {
        return name.Contains("Định lượng", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Tổng phân tích", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Nhóm máu", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Điện giải", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Xét nghiệm", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Đo hoạt độ", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Huyết đồ", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Kháng sinh đồ", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Tìm ký sinh trùng", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Ký sinh trùng", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Nước tiểu", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Đông máu", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Miễn dịch", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Soi tươi", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Nhuộm soi", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Nuôi cấy", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Thời gian máu chảy", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Thời gian máu đông", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Prothrombin", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Fibrinogen", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Glucose", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("HbA1c", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Creatinin", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Ure", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Cholesterol", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Triglycerid", StringComparison.OrdinalIgnoreCase) ||
               grp.Contains("Xét nghiệm", StringComparison.OrdinalIgnoreCase) ||
               grp.Contains("Huyết học", StringComparison.OrdinalIgnoreCase) ||
               grp.Contains("Sinh hóa", StringComparison.OrdinalIgnoreCase) ||
               grp.Contains("Hóa sinh", StringComparison.OrdinalIgnoreCase) ||
               grp.Contains("Vi sinh", StringComparison.OrdinalIgnoreCase) ||
               grp.Contains("Miễn dịch", StringComparison.OrdinalIgnoreCase) ||
               grp.Contains("Ký sinh trùng", StringComparison.OrdinalIgnoreCase);
    }

    private static ScenarioService? PickDistinctFromPool(
        IReadOnlyList<ScenarioService> pool,
        Random rng,
        HashSet<string>? usedCodes)
    {
        if (pool.Count == 0) return null;

        if (usedCodes is not null)
        {
            var unused = pool.Where(s => !usedCodes.Contains(s.Code)).ToList();
            if (unused.Count > 0)
            {
                var chosen = unused[rng.Next(unused.Count)];
                usedCodes.Add(chosen.Code);
                return chosen;
            }
        }

        var fallback = pool[rng.Next(pool.Count)];
        usedCodes?.Add(fallback.Code);
        return fallback;
    }

    private static List<ScenarioService> PickMultipleDistinctFromPool(
        IReadOnlyList<ScenarioService> pool,
        int count,
        Random rng,
        HashSet<string>? usedCodes)
    {
        if (pool.Count == 0 || count <= 0) return new List<ScenarioService>();

        var result = new List<ScenarioService>(count);
        var chosenInThisCall = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (usedCodes is not null)
        {
            var unused = pool
                .Where(s => !usedCodes.Contains(s.Code))
                .OrderBy(_ => rng.Next())
                .ToList();

            foreach (var item in unused)
            {
                if (result.Count >= count) break;
                if (chosenInThisCall.Add(item.Code))
                {
                    result.Add(item);
                    usedCodes.Add(item.Code);
                }
            }
        }

        if (result.Count < count)
        {
            var remaining = pool
                .Where(s => !chosenInThisCall.Contains(s.Code))
                .OrderBy(_ => rng.Next())
                .ToList();

            foreach (var item in remaining)
            {
                if (result.Count >= count) break;
                if (chosenInThisCall.Add(item.Code))
                {
                    result.Add(item);
                    usedCodes?.Add(item.Code);
                }
            }
        }

        return result;
    }

    private static ScenarioPatient MapPatientRow(SnapshotPatientRow pRow, DateOnly examDate, PatientPaymentType defaultPayment)
    {
        var normalizedInsurance = ScenarioPatient.NormalizeInsuranceNumber(pRow.InsuranceNumber);
        var isTe1 = normalizedInsurance?.StartsWith("TE1", StringComparison.OrdinalIgnoreCase) == true;
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
            normalizedInsurance,
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
        bool isGmhs = false,
        bool isLienChuyenKhoa = false,
        ScenarioService? pickedSpecialtyCls = null,
        ScenarioService? pickedSpecialtyThuThuat = null,
        IReadOnlyList<ScenarioService>? orderedServices = null) => code switch
    {
        "LT_BAN_PHIEU_KHAM" => BuildLtBanPhieuKhamInstruction(patient, secondPatient, examDate),
        "LT_THU_PHI_VA_CLS" => BuildLtThuPhiClsInstruction(pickedSieuAm, orderedServices, allServices, rng),
        "LT_THU_TAM_UNG_NOI_TRU" => $"Thu tạm ứng cho các bệnh nhân sau:\n© Bệnh nhân nội trú có số bệnh án : {advanceCase1?.SoBenhAn ?? "CHƯA_CÓ_SỐ_BA"}, Số tiền tạm ứng : 1.500.000 đồng (1đ)\n© Bệnh nhân bệnh án ngoại trú có số bệnh án : {(advanceOutpatientCase?.SoBenhAn ?? advanceCase2?.SoBenhAn ?? "CHƯA_CÓ_SỐ_BA")}, Số tiền tạm ứng : 500.000 đồng (1đ)\nSố điểm đạt được : ………",
        "LT_DOI_DICH_VU_CLS" => $"Chọn quyển hóa đơn ký hiệu:  THI để thu\n© Huỷ và đổi dịch vụ {pickedSieuAm?.Name ?? "Siêu âm ổ bụng"} thành dịch vụ : {(pickedXquang?.Name ?? "Chụp Xquang cột sống cổ chếch hai bên [Số hoá 2 phim]")} và thu tiền lại (1đ)\nSố điểm đạt được : ………",
        "LT_TRA_CUU_THONG_TIN" => $"Tìm thông tin của bệnh nhân\n© Đang điều trị nội trú có họ và tên : {inLookupCase?.FullName ?? patient.FullName} sinh năm {GetBirthYear(inLookupCase?.DateOfBirth ?? "1989")} (1đ)\nKhoa điều trị : ………………………………………………………………………...\nPhòng bệnh   : ………………………………………………………………………...\nSố giường      : ………………………………………………………………………..;\n© Tìm bệnh nhân có thông tin như sau: {outLookupCase?.FullName ?? (secondPatient ?? patient).FullName}, Năm sinh: {GetBirthYear(outLookupCase?.DateOfBirth ?? "1973")} {outLookupCase?.Address ?? (secondPatient ?? patient).Address} và ghi lại các thông tin sau :(1đ)\nSố lượt khám (nếu có):………………………………………………………………………\nNgày điều trị Nội trú gần nhất(nếu có):……………………………………………\nNgày điều trị Ngoại trú gần nhất (nếu có):………………………………………...........\nSố điểm đạt được : ………",
        "LT_THANH_TOAN_RA_VIEN" => $"Thu Thanh Toán Ra viện cho bệnh nhân nội trú có số bệnh án: {dischargeCase?.SoBenhAn ?? "CHƯA_CÓ_SỐ_BA"} (1đ)\n(Dùng quyển hóa đơn VAT_THI)\nSố điểm đạt được : ………",
        "LT_TAO_THE_TAM_BHYT" => BuildLtTaoTheTamInstruction(examDate, rng),
        "TC_THU_TAM_UNG" => $"Thu tạm ứng viện phí cho bệnh nhân có số bệnh án nội trú : {advanceCase1?.SoBenhAn ?? "CHƯA_CÓ_SỐ_BA"}, số tiền tạm ứng : 1.500.000 đồng.\nSố điểm đạt được:………(đ)",
        "TC_THANH_TOAN_RA_VIEN" => $"Thực hiện quyết toán và in hóa đơn thanh toán viện phí ra viện cho bệnh nhân nội trú có số bệnh án: {dischargeCase?.SoBenhAn ?? "CHƯA_CÓ_SỐ_BA"} (Dùng quyển hóa đơn VAT_THI).\nSố điểm đạt được:………(đ)",
        "TC_TRA_CUU_BENH_SU" => isLienChuyenKhoa
            ? $"Tra cứu lịch sử KCB/ Bệnh sử của bệnh nhân  {outLookupCase?.FullName ?? patient.FullName}, Mã y tế : {outLookupCase?.MedicalCode ?? patient.MedicalCode}:  ({score:0} đ)\nĐợt khám bệnh gần nhất: ngày thực hiện:............ Nội dung:.....................\nCLS thực hiện gần nhất: ngày thực hiện:............ Nội dung:......................\nSố điểm đạt được : ……………"
            : $"Tra cứu lịch sử khám chữa bệnh của bệnh nhân: {outLookupCase?.FullName ?? patient.FullName}, Mã y tế : {outLookupCase?.MedicalCode ?? patient.MedicalCode}.\n- Tổng số lượt khám : …………………………………\n- Ngày điều trị nội trú gần nhất : …………………………………\n- Ngày khám ngoại trú gần nhất : …………………………………\nSố điểm đạt được:………(đ)",
        "TN_TIEP_NHAN" or _ when isDirectReception && code.StartsWith("TN_", StringComparison.OrdinalIgnoreCase) => BuildDirectReceptionInstruction(patient, secondPatient, examDate),
        "NT_NHAN_BENH_KHOA" => $"* {secondPatient.FullName} ; Mã y tế: {secondPatient.MedicalCode}\n* {patient.FullName} ; Mã y tế: {patient.MedicalCode}\n- SVV Bn VP       : ………………………………\n- SVV Bn BHYT : ……………………………….\nSố điểm đạt được:………(đ)",
        "NT_CAP_GIUONG" => "Cấp giường cho các bệnh nhân được tiếp nhận ở câu 1.\nSố điểm đạt được:………(đ)",
        "KB_CHI_DINH_CLS" => isLienChuyenKhoa
            ? $"Trên màn hình khám bệnh - Chỉ định CLS – Thủ thuật cho bệnh nhân : {patient.FullName}, Mã Y tế : {patient.MedicalCode} ({score:0} đ)\n{pickedSpecialtyCls?.Name ?? "Chụp đáy mắt không huỳnh quang"}   -   {pickedSpecialtyThuThuat?.Name ?? "Rạch áp xe mi"}\nSố điểm đạt được : ……………"
            : BuildKbChiDinhClsInstruction(pickedXquang, pickedSieuAm),
        "KB_BENH_AN_NGOAI_TRU" => isLienChuyenKhoa
            ? $"Chỉ định điều trị ngoại trú và lập bệnh án ngoại trú cho bệnh nhân {secondPatient.FullName}, Mã Y tế : {secondPatient.MedicalCode}. ({score:0} đ)\nSố bệnh án ngoại trú : …………………………………………………\nSố điểm đạt được : ……………"
            : (name.Contains("Nhận bệnh", StringComparison.OrdinalIgnoreCase) || name.Contains("bệnh án", StringComparison.OrdinalIgnoreCase) && name.Contains("nội dung", StringComparison.OrdinalIgnoreCase)
                ? $"Nhận bệnh nhân vào khoa - tạo Bệnh án ngoại trú, nhập nội dung bệnh án ({score:0}đ):\n\t* {patient.FullName}; Mã y tế: {patient.MedicalCode}\n- Số bệnh án : ………………………………………………………………………………………\nSố điểm đạt được:………(đ)"
                : "Chỉ định điều trị ngoại trú và lập bệnh án ngoại trú cho bệnh nhân : ……………………………………, Mã Y tế : …………………… (2 đ)\n- Số bệnh án ngoại trú : …………………………………………………\nSố điểm đạt được:………(đ)"),
        "KB_CHI_DINH_NOI_TRU" => isLienChuyenKhoa
            ? $"Chỉ định nhập viện điều trị nội trú cho bệnh nhân : {advanceCase1?.FullName ?? patient.FullName}, Mã y tế : {advanceCase1?.MedicalCode ?? patient.MedicalCode} ({score:0} đ)\nNhập khoa : Khoa phẫu thuật GMHS\nSố bệnh án nội trú : ……………………………………………………\nSố điểm đạt được : ……………"
            : $"Chỉ định nhập viện điều trị nội trú cho bệnh nhân : ……………………………………, Mã y tế : …………………… (1 đ)\n- Nhập khoa : {targetTransferDept}\n- Số bệnh án nội trú : ……………………………………………………\nSố điểm đạt được:………(đ)",
        "HC_TRA_CUU_THANH_TOAN" => name.Contains("đang nằm viện", StringComparison.OrdinalIgnoreCase) || name.Contains("viện phí", StringComparison.OrdinalIgnoreCase)
            ? $"Kiểm tra viện phí của bệnh nhân đang nằm viện ({score:0}đ):\n- Tìm 01 bệnh nhân bất kỳ đang nằm điều ngoại trú thuộc {deptName}, có thời gian nhập viện trong khoảng từ ngày 01/03/{examDate.Year} đến ngày 05/03/{examDate.Year}.\n- Tra cứu chi tiết viện phí và ghi lại các thông tin sau:\n+ Họ và tên bệnh nhân                : ………………………………………………………………………………………\n+ Mã y tế / Số bệnh án               : ………………………………………………………………………………………\n+ Tổng chi phí điều trị đến hiện tại  : ………………………………………………………………………………… đồng\n+ Số tiền đã tạm ứng                  : ………………………………………………………………………………… đồng\n+ Số tiền còn phải thanh toán/hoàn trả: ………………………………………………………………………………… đồng\nSố điểm đạt được:………(đ)"
            : $"Kiểm tra thông tin thanh toán cho bệnh nhân : {inLookupCase?.FullName ?? patient.FullName}, Mã y tế : {inLookupCase?.MedicalCode ?? patient.MedicalCode}\n- Số bệnh án nội trú : {inLookupCase?.SoBenhAn ?? "…………………………………………"}\n- Số tiền người bệnh đã thanh toán : ……………………………………………………\n- Số tiền người bệnh đã tạm ứng : ……………………………………………………\n- Số tiền còn phải thu người bệnh : ……………………………………………………\n- Tổng số tiền người bệnh trả : ……………\nSố điểm đạt được:………(đ)",
        "TT_TUONG_TRINH_PTTT" => isLienChuyenKhoa
            ? $"Viết  tường trình cho thủ thuật {pickedSpecialtyThuThuat?.Name ?? "Rạch áp xe mi"} cho bệnh nhân ở câu 1 ({score:0}đ)\nSố điểm đạt được : ……………"
            : $"Nhập nội dung tường trình PT-TT cho các dịch vụ đã chỉ định cho bệnh nhân {patient.FullName} ({patient.MedicalCode}/NG) ({score:0}đ)\nSố điểm đạt được:………(đ)",
        "YL_CHI_DINH_CLS" => q2Instruction,
        "YL_CHI_DINH_THUOC_VTYT" => isLienChuyenKhoa
            ? $"Lên phiếu lĩnh cơ số Dược/ VTYT:  ({score:0} đ)\nLập phiếu\nNhập Nội Bộ\nXuất sử dụng (Xuất khác toàn bộ nội dung phiếu lĩnh vừa nhận)\nSố điểm đạt được : ……………"
            : (isGmhs || deptName.Contains("Gây mê", StringComparison.OrdinalIgnoreCase) || score >= 5
                ? (isGmhs || deptName.Contains("Gây mê", StringComparison.OrdinalIgnoreCase)
                    ? $"Thực hiện thao tác lên các loại Dược – VTYT cần thiết cho bệnh nhân {patient.FullName} có  Số vào viện :{surgeryPatientAdmissionNumber ?? patient.MedicalCode}  sau phẫu thuật:  {surgeryName ?? "Phẫu thuật lấy thai lần đầu [gây tê]"}, ngày yêu cầu {examDate:dd/MM/yyyy} (có danh mục Dược – VTYT đính kèm)(3 đ)\nThực hiện thao tác:\n- Lập phiếu lĩnh dược (2đ)  …………\n- Nhập dược nội bộ (2đ) ……………\n- Xuất sử dụng thuốc - VTYT cho bệnh nhân (số vào viện ở câu 1) (1đ)\n(Lưu ý : khi thực hiện phải yêu cầu nhân viên vi tính hỗ trợ thao tác xuất dược nội bộ)\nSố điểm đạt được:………(đ)"
                    : "Thực hiện thao tác lên các loại Dược – VTYT cần thiết cho bệnh nhân sau phẫu thuật (có danh mục Dược – VTYT đính kèm) (3đ)\nThực hiện thao tác:\n- Lập phiếu lĩnh dược (2đ)  …………\n- Nhập dược nội bộ (2đ) ……………\n- Xuất sử dụng thuốc - VTYT cho bệnh nhân (số vào viện ở câu 1) (1đ)\n(Lưu ý : khi thực hiện phải yêu cầu nhân viên vi tính hỗ trợ thao tác xuất dược nội bộ)\nSố điểm đạt được:………(đ)")
                : "Lên y lệnh(có D/S Thuốc, VTYT đính kèm)(1.5đ)\nLập  phiếu lĩnh dược(0.5đ)\nNhập nội bộ(0.5đ)\nXuất sử dụng thuốc cho bệnh nhân(0.5đ)\nSố điểm đạt được:………(đ)"),
        "YL_TRA_THUOC" => $"Bệnh nhân trả thuốc và tổng hợp phiếu trả.(Chỉ thao tác trên bệnh nhân BHYT có số vào viện ở câu 1), thuốc cần trả {drugReturn?.ReturnedDrug.Name ?? "thuốc"}, số lượng trả : {drugReturn?.ReturnQuantity ?? 1} {drugReturn?.ReturnedDrug.Unit ?? "viên"}.\nSố điểm đạt được:………(đ)",
        "YL_DOI_THEM_DICH_VU" => $"Đổi dịch vụ {serviceChange?.CanceledService.Name ?? "Dịch vụ CLS 1"} thành dịch vụ {serviceChange?.NewService.Name ?? "Dịch vụ CLS 2"} thêm 01 dịch vụ: {(serviceChange?.AddedService?.Name ?? "Chụp Xquang ngực thẳng [Số hóa 1 phim]")}. (Bệnh nhân BHYT câu 1)\nSố điểm đạt được:………(đ)",
        "TK_KIEM_TON_KHO" => isGmhs
            ? (inventoryPeriodItem is not null
                ? $"Báo số tồn kho của {inventoryPeriodItem.DrugName} trong {targetWarehouseName ?? "kho Gây mê"} từ ngày {inventoryPeriodItem.FromDate:dd/MM/yyyy} đến ngày {inventoryPeriodItem.ToDate:dd/MM/yyyy} (Nguồn {inventoryPeriodItem.FundingSource}), số lượng tồn : ………… (2 đ)\nSố điểm đạt được:………(đ)"
                : $"Báo số lượng tồn kho của {inventoryDrug?.Name ?? "Kim tiêm nhựa G18; G26"} trong {targetWarehouseName ?? "kho Gây mê"} tại thời điểm hiện tại / ngày thi {examDate:dd/MM/yyyy} (Nguồn {inventoryDrug?.FundingSource ?? "Viện phí"}), số lượng tồn : ………… (2 đ)\nSố điểm đạt được:………(đ)")
            : (inventoryPeriodItem is not null
                ? $"Báo số tồn kho của một loại dược trong kho cơ số (Nguồn {inventoryPeriodItem.FundingSource}) từ ngày {inventoryPeriodItem.FromDate:dd/MM/yyyy} đến ngày {inventoryPeriodItem.ToDate:dd/MM/yyyy} : Tên dược: {inventoryPeriodItem.DrugName}  số lượng tồn cuối : …………….\nSố điểm đạt được:………(đ)"
                : $"Báo số lượng tồn kho của một loại dược trong kho cơ số (Nguồn {inventoryDrug?.FundingSource ?? "Viện phí"}) tại thời điểm hiện tại / ngày thi {examDate:dd/MM/yyyy} : Tên dược: {inventoryDrug?.Name ?? "Thuốc/VTYT"}  số lượng tồn hiện tại : …………….\nSố điểm đạt được:………(đ)"),
        "CK_CHUYEN_KHOA" => $"Chuyển một bệnh nhân bất kỳ từ khoa {deptName} đến {targetTransferDept}(bệnh nhân nhập viện trước {cutoffDate:dd/MM/yyyy}), Số vào viện của BN: …………………, Mã y tế :…………………., họ tên bệnh nhân:……………………………………\nSố điểm đạt được:………(đ)",
        "RV_CHO_RA_VIEN" => $"Thực hiện thao tác cho bệnh nhân ra viện(bệnh nhân nhập viện trước {cutoffDate:dd/MM/yyyy}) : Số vào viện của BN: ………………………\nSố điểm đạt được:………(đ)",
        _ => actionDescMap.TryGetValue(code, out var customDesc) && !string.IsNullOrWhiteSpace(customDesc)
            ? $"{customDesc}\nSố điểm đạt được:………(đ)"
            : $"Thực hiện nghiệp vụ chuyên môn '{name}' trên phần mềm HIS theo quy định của Bệnh viện Đa khoa Thiện Hạnh.\nSố điểm đạt được:………(đ)"
    };

    private static SnapshotInpatientRow PickDistinctInpatient(
        IReadOnlyList<SnapshotInpatientRow> pool,
        HashSet<string> candidateUsedSoBenhAn,
        Func<SnapshotInpatientRow, bool>? predicate = null,
        HashSet<string>? markGlobal = null)
    {
        SnapshotInpatientRow? picked = null;

        if (predicate is not null)
        {
            picked = pool.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(c.SoBenhAn) &&
                !candidateUsedSoBenhAn.Contains(c.SoBenhAn) &&
                predicate(c));
        }

        picked ??= pool.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(c.SoBenhAn) &&
            !candidateUsedSoBenhAn.Contains(c.SoBenhAn));

        if (picked is null && predicate is not null)
        {
            picked = pool.FirstOrDefault(predicate);
        }

        picked ??= pool[0];

        if (!string.IsNullOrWhiteSpace(picked.SoBenhAn))
        {
            candidateUsedSoBenhAn.Add(picked.SoBenhAn);
            markGlobal?.Add(picked.SoBenhAn);
        }

        return picked;
    }

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

        var bhytNumber = ScenarioPatient.NormalizeInsuranceNumber(patient.InsuranceNumber);
        if (!string.IsNullOrWhiteSpace(bhytNumber))
        {
            lines.Add($"+ Số thẻ BHYT: {bhytNumber}");
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
        IReadOnlyList<ScenarioService>? orderedServices,
        IReadOnlyList<(string Code, string Name, string GroupName)>? allServices,
        Random? rng = null)
    {
        var sa = pickedSieuAm?.Name ?? "Siêu âm ổ bụng";
        var xn1 = "Định lượng Vitamin B12 [Máu]";
        var xn2 = "Tìm ký sinh trùng sốt rét trong máu (bằng phương pháp thủ công)";

        var labFromOrdered = orderedServices?
            .Where(s => (pickedSieuAm is null || !s.Code.Equals(pickedSieuAm.Code, StringComparison.OrdinalIgnoreCase)) &&
                        IsLaboratoryService(s.Name, s.GroupName))
            .ToList() ?? new List<ScenarioService>();

        if (labFromOrdered.Count >= 2)
        {
            xn1 = labFromOrdered[0].Name;
            xn2 = labFromOrdered[1].Name;
        }
        else if (allServices is not null && allServices.Count > 0)
        {
            var labPool = allServices
                .Where(s => !IsExcludedFromRoutineCls(s.Name, s.GroupName) && IsLaboratoryService(s.Name, s.GroupName))
                .ToList();

            if (labPool.Count >= 2)
            {
                var idx1 = rng is not null ? rng.Next(labPool.Count) : 0;
                var idx2 = rng is not null ? rng.Next(labPool.Count - 1) : 1;
                if (idx2 >= idx1) idx2++;
                xn1 = labPool[idx1].Name;
                xn2 = labPool[idx2].Name;
            }
            else if (labPool.Count == 1)
            {
                xn1 = labPool[0].Name;
            }
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
        var bhytNumber = ScenarioPatient.NormalizeInsuranceNumber(patient.InsuranceNumber);
        var isBhyt = patient.PaymentType == PatientPaymentType.Insurance && !string.IsNullOrWhiteSpace(bhytNumber);
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
            lines.Add($"+ Số thẻ BHYT: {bhytNumber}");
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
