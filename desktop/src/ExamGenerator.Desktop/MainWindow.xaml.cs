using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using ExamGenerator.Domain;
using ExamGenerator.Infrastructure;

namespace ExamGenerator.Desktop;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public partial class MainWindow : Window
{
    private readonly LocalApplicationStorage _storage = new();
    private readonly SqliteDatabase _database;
    private readonly JsonApplicationSettingsStore _settingsStore;
    private readonly ProtectedCredentialStore _credentialStore;
    private readonly SqlScriptGenerator _sqlGenerator = new();
    private readonly WordExamWriter _wordWriter = new();

    private readonly List<CandidateRow> _candidates = [];
    private readonly List<TemplateRow> _templates = [];
    private readonly List<DepartmentRow> _departments = [];
    private readonly List<WarehouseRow> _warehouses = [];
    private readonly List<WarehouseItemViewModel> _allWarehouseItems = [];
    private readonly List<ServiceGroupItemViewModel> _allServiceGroups = [];
    private List<(string Code, string Name, string? DepartmentId)> _allWarehousesWithDept = [];

    private readonly ObservableCollection<TemplateQuestionItemViewModel> _currentQuestions = [];
    private string? _selectedTemplateId;
    private bool _isNewTemplateMode;

    private string? _selectedMappingDepartment;

    public MainWindow()
    {
        InitializeComponent();
        _database = new SqliteDatabase(_storage);
        _settingsStore = new JsonApplicationSettingsStore(_storage);
        _credentialStore = new ProtectedCredentialStore(_storage);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _database.InitializeAsync();
            await _database.EnsureDefaultTemplatesAsync();

            var settings = await _settingsStore.LoadAsync();
            var profile = settings.SqlServerProfiles.FirstOrDefault();
            if (profile is not null)
            {
                ProfileNameTextBox.Text = profile.Name;
                ServerTextBox.Text = profile.Server;
                DatabaseTextBox.Text = profile.Database;
                WindowsAuthenticationCheckBox.IsChecked = profile.UseWindowsAuthentication;
                SqlUserTextBox.Text = profile.UserName;
                SqlPasswordBox.Password = _credentialStore.Load(profile.Name) ?? string.Empty;
            }
            AuthenticationChanged(this, null!);

            await ReloadActionLibraryAsync();

            EditorPositionComboBox.ItemsSource = new[]
            {
                "Điều dưỡng",
                "Bác sĩ",
                "Lễ tân",
                "Kỹ thuật viên",
                "Thu ngân",
                "Dược sĩ",
                "Hành chính"
            };

            EditorReceptionModeComboBox.ItemsSource = new[]
            {
                "Tiếp nhận trực tiếp (DirectReception)",
                "Hàng chờ nhận khoa (WardAdmissionPreparation)"
            };

            EditorQuestionsGrid.ItemsSource = _currentQuestions;

            ExamDatePicker.SelectedDate = DateTime.Today;
            await LoadStaticViewsAsync();
            await LoadMappingsAsync();
            await RefreshDashboardCardsAsync();
            await ReloadHistoryDataAsync();
            HeaderStatusText.Text = "Hệ thống sẵn sàng";
            FooterStatusText.Text = "Khởi tạo thành công CSDL SQLite cục bộ.";
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = "Lỗi khởi tạo: " + ex.Message;
        }
    }

    private async Task LoadStaticViewsAsync()
    {
        _departments.Clear();
        var depts = await _database.GetDepartmentsWithStatusAsync();
        _departments.AddRange(depts.Select(d => new DepartmentRow(d.Name, d.Code, d.IsDisabled)));

        _warehouses.Clear();
        var whs = await _database.GetWarehousesWithDepartmentAsync();
        _allWarehousesWithDept = whs.ToList();
        _warehouses.AddRange(whs.Select(w => new WarehouseRow(w.Name, w.Code)));

        _allWarehouseItems.Clear();
        _allWarehouseItems.AddRange(whs.Select(w => new WarehouseItemViewModel(w.Name, w.Code, w.DepartmentId)));

        _allServiceGroups.Clear();
        var sgs = await _database.GetCatalogItemsAsync("ServiceGroups");
        _allServiceGroups.AddRange(sgs.Select(s => new ServiceGroupItemViewModel(s.Name, s.Code)));

        _templates.Clear();
        var storedTemplates = await _database.GetTemplatesAsync();
        _templates.AddRange(storedTemplates.Select(x => new TemplateRow(
            x.Id,
            x.Name,
            x.Department,
            x.Position,
            x.QuestionCount,
            x.TotalScore,
            x.ReceptionMode.Contains("DirectReception") ? "Tiếp nhận trực tiếp" : "Hàng chờ nhận khoa"
        )));

        CandidatesGrid.ItemsSource = null;
        CandidatesGrid.ItemsSource = _candidates;

        var allKnownDepts = _departments.Select(d => d.Name)
            .Concat(_templates.Select(t => t.Department))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

        EditorDepartmentComboBox.ItemsSource = null;
        EditorDepartmentComboBox.ItemsSource = allKnownDepts;

        var prevFilter = TemplateDeptFilterComboBox.SelectedItem as string;
        TemplateDeptFilterComboBox.ItemsSource = null;
        TemplateDeptFilterComboBox.ItemsSource = new[] { "Tất cả khoa" }.Concat(allKnownDepts).ToList();
        if (!string.IsNullOrEmpty(prevFilter) && TemplateDeptFilterComboBox.Items.Contains(prevFilter))
        {
            TemplateDeptFilterComboBox.SelectedItem = prevFilter;
        }
        else
        {
            TemplateDeptFilterComboBox.SelectedIndex = 0;
        }
        ApplyTemplateFilter();

        // Chỉ nạp các Khoa đang kích hoạt thi vào ComboBox chọn khoa của Tab 1 (Tạo đề thi)
        var activeDepts = _departments.Where(x => !x.IsDisabled).Select(x => x.Name).ToList();
        if (activeDepts.Count == 0 && _departments.Count == 0)
        {
            activeDepts = allKnownDepts;
        }

        ExamDepartmentComboBox.ItemsSource = null;
        ExamDepartmentComboBox.ItemsSource = activeDepts;
        if (activeDepts.Count > 0)
        {
            ExamDepartmentComboBox.SelectedIndex = 0;
        }
        else
        {
            ExamDepartmentComboBox.Text = _departments.Count > 0 ? "(Tất cả khoa đang tạm ngưng thi)" : "(Chưa đồng bộ khoa từ HIS)";
        }

        MappingDepartmentList.ItemsSource = null;
        MappingDepartmentList.ItemsSource = _departments;
        if (_departments.Count > 0 && MappingDepartmentList.SelectedIndex < 0)
        {
            MappingDepartmentList.SelectedIndex = 0;
        }

        InventoryDepartmentComboBox.ItemsSource = null;
        InventoryDepartmentComboBox.ItemsSource = new[] { "Tất cả khoa" }.Concat(_departments.Select(x => x.Name)).ToList();
        InventoryDepartmentComboBox.SelectedIndex = 0;

        UpdateInventoryWarehousesForDepartment("Tất cả khoa");

        InventorySourceComboBox.ItemsSource = new[] { "Tất cả nguồn", "BHYT", "Viện phí" };
        InventorySourceComboBox.SelectedIndex = 0;

        RefreshWarehouseMappingsList();
        RefreshServiceGroupsList();

        await RefreshCatalogSummaryAsync();
        await SearchInventoryCoreAsync();

        if (_departments.Count == 0)
        {
            HeaderStatusText.Text = "Chưa có danh mục từ HIS";
            FooterStatusText.Text = "CSDL SQLite chưa có danh mục. Vui lòng vào phân hệ 'Cài đặt & Danh mục HIS' và bấm 'Đồng bộ Catalog HIS'.";
        }
        else
        {
            HeaderStatusText.Text = "Hệ thống sẵn sàng";
            FooterStatusText.Text = $"Đã nạp {_departments.Count} khoa và {_warehouses.Count} kho từ snapshot SQLite.";
        }
    }

    private void RefreshServiceGroupsList()
    {
        var kw = ServiceSearchTextBox.Text?.Trim() ?? string.Empty;
        ServiceGroupsList.ItemsSource = null;
        ServiceGroupsList.ItemsSource = _allServiceGroups
            .Where(x => string.IsNullOrWhiteSpace(kw) || x.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void UpdateInventoryWarehousesForDepartment(string? deptName)
    {
        if (string.IsNullOrWhiteSpace(deptName) || deptName == "Tất cả khoa")
        {
            InventoryWarehouseComboBox.ItemsSource = new[] { new WarehouseRow("Tất cả kho", "ALL") }.Concat(_warehouses).ToList();
            InventoryWarehouseComboBox.DisplayMemberPath = "Name";
            InventoryWarehouseComboBox.SelectedIndex = 0;
            return;
        }

        var matchDept = _departments.FirstOrDefault(d => d.Name.Equals(deptName, StringComparison.OrdinalIgnoreCase));
        var deptCode = matchDept?.Code;

        var deptWhs = _allWarehousesWithDept
            .Where(w => !string.IsNullOrEmpty(deptCode) && string.Equals(w.DepartmentId, deptCode, StringComparison.OrdinalIgnoreCase))
            .Select(w => new WarehouseRow(w.Name, w.Code))
            .ToList();

        if (deptWhs.Count == 0)
        {
            deptWhs = _warehouses.ToList();
        }

        InventoryWarehouseComboBox.ItemsSource = new[] { new WarehouseRow("Tất cả kho của khoa này", "ALL_DEPT") }.Concat(deptWhs).ToList();
        InventoryWarehouseComboBox.DisplayMemberPath = "Name";
        InventoryWarehouseComboBox.SelectedIndex = 0;
    }

    private async void ExamDepartmentChanged(object sender, SelectionChangedEventArgs e)
    {
        var deptName = ExamDepartmentComboBox.SelectedItem as string ?? ExamDepartmentComboBox.Text;
        if (string.IsNullOrWhiteSpace(deptName)) return;

        var matchingTemplates = _templates.Where(x => x.Department.Equals(deptName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matchingTemplates.Count == 0)
        {
            matchingTemplates = _templates.ToList();
        }

        ExamTemplateComboBox.ItemsSource = matchingTemplates;
        ExamTemplateComboBox.DisplayMemberPath = "Name";
        if (matchingTemplates.Count > 0) ExamTemplateComboBox.SelectedIndex = 0;

        // Load mapped warehouses for this department
        var deptObj = _departments.FirstOrDefault(d => d.Name.Equals(deptName, StringComparison.OrdinalIgnoreCase));
        var deptCode = deptObj?.Code ?? string.Empty;

        var mappedWhs = await _database.GetWarehousesForDepartmentAsync(deptCode);
        var whList = mappedWhs.Select(w => new WarehouseRow(w.WarehouseName, w.WarehouseCode)).ToList();

        if (whList.Count == 0)
        {
            var fallback = _allWarehousesWithDept
                .Where(w => !string.IsNullOrEmpty(deptCode) && string.Equals(w.DepartmentId, deptCode, StringComparison.OrdinalIgnoreCase))
                .Select(w => new WarehouseRow(w.Name, w.Code))
                .ToList();
            whList = fallback.Count > 0 ? fallback : _warehouses.ToList();
        }

        ExamWarehouseComboBox.ItemsSource = whList;
        ExamWarehouseComboBox.DisplayMemberPath = "Name";
        if (whList.Count > 0)
        {
            ExamWarehouseComboBox.SelectedIndex = 0;
        }
    }

    private void ExamTemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExamTemplateComboBox.SelectedItem is TemplateRow template)
        {
            ScoreSummaryText.Text = $"{template.Name} · {template.QuestionCount} câu · Tổng {template.TotalScore:0.0} điểm · Chế độ: {template.Status}";
            if (template.Position.Contains("Lễ tân", StringComparison.OrdinalIgnoreCase) ||
                template.Name.Contains("Lễ tân", StringComparison.OrdinalIgnoreCase) ||
                template.Name.Contains("KKB", StringComparison.OrdinalIgnoreCase) ||
                template.Department.Contains("Khám bệnh", StringComparison.OrdinalIgnoreCase) ||
                template.Department.Contains("Xét Nghiệm", StringComparison.OrdinalIgnoreCase))
            {
                ExamDurationTextBox.Text = "15";
            }
            else
            {
                ExamDurationTextBox.Text = "30";
            }
        }
    }

    private void AddCandidate_Click(object sender, RoutedEventArgs e)
    {
        var name = CandidateNameTextBox.Text?.Trim();
        var id = CandidateIdTextBox.Text?.Trim() ?? string.Empty;
        var dept = ExamDepartmentComboBox.SelectedItem as string ?? ExamDepartmentComboBox.Text;

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Vui lòng nhập họ tên thí sinh.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ExamTemplateComboBox.SelectedItem is not TemplateRow template)
        {
            MessageBox.Show("Vui lòng chọn mẫu đề thi cho thí sinh.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var warehouse = ExamWarehouseComboBox.SelectedItem as WarehouseRow;
        if (warehouse is null)
        {
            var whText = ExamWarehouseComboBox.Text?.Trim();
            warehouse = _warehouses.FirstOrDefault(w => w.Name.Equals(whText, StringComparison.OrdinalIgnoreCase));
        }

        if (warehouse is null)
        {
            MessageBox.Show("Vui lòng chọn Kho Dược thi cho thí sinh.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int.TryParse(ExamDurationTextBox.Text?.Trim(), out var duration);
        if (duration <= 0) duration = 30;

        _candidates.Add(new CandidateRow(true, name, id, dept, template.Name, "Tự động cấp User riêng không trùng", duration, warehouse.Code, warehouse.Name));
        CandidatesGrid.Items.Refresh();
        UpdateCandidatesCount();
        CandidateNameTextBox.Clear();
        CandidateIdTextBox.Clear();
        FooterStatusText.Text = $"Đã thêm thí sinh {name} (Kho: {warehouse.Name}, {duration} phút) vào danh sách.";
    }

    private void SelectAllCandidates_Click(object sender, RoutedEventArgs e)
    {
        _candidates.ForEach(x => x.Selected = true);
        CandidatesGrid.Items.Refresh();
    }

    private void ClearCandidateSelection_Click(object sender, RoutedEventArgs e)
    {
        _candidates.ForEach(x => x.Selected = false);
        CandidatesGrid.Items.Refresh();
    }

    private void RemoveSelectedCandidates_Click(object sender, RoutedEventArgs e)
    {
        _candidates.RemoveAll(x => x.Selected);
        CandidatesGrid.Items.Refresh();
        UpdateCandidatesCount();
    }

    private void UpdateCandidatesCount()
    {
        CandidatesCountBadge.Text = $"{_candidates.Count} thí sinh";
    }

    private async void GenerateAll_Click(object sender, RoutedEventArgs e) => await GenerateAsync(_candidates);
    private async void GenerateSelected_Click(object sender, RoutedEventArgs e) => await GenerateAsync(_candidates.Where(x => x.Selected).ToList());

    private async Task GenerateAsync(IReadOnlyList<CandidateRow> candidates)
    {
        try
        {
            if (candidates.Count == 0)
            {
                MessageBox.Show("Chưa có thí sinh nào được chọn để sinh đề.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            GenerationStatusText.Text = "Đang chạy Preflight check và phân bổ kịch bản khao thí...";
            var examDate = DateOnly.FromDateTime(ExamDatePicker.SelectedDate ?? DateTime.Today);
            var batchName = Sanitize(BatchNameTextBox.Text);
            if (string.IsNullOrWhiteSpace(batchName)) batchName = "DotThi_ThiTuyen";

            // Run Scenario Allocation (Rule 3.2 distinct user per candidate, drug stock, patient)
            var allocator = new ExamScenarioAllocator(_database);
            var candidateInputs = candidates.Select(c => new CandidateInput(c.Name, c.Id, c.Department, c.Template, c.DurationMinutes, c.WarehouseCode, c.WarehouseName)).ToList();
            var scenarios = await allocator.AllocateBatchScenariosAsync(batchName, examDate, candidateInputs);

            var outputDir = _storage.OutputDirectory;
            Directory.CreateDirectory(outputDir);

            var zipPath = Path.Combine(outputDir, $"DeThi_{batchName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var scenario in scenarios)
                {
                    // 1. Generate Word Document (.docx)
                    var sbd = string.IsNullOrWhiteSpace(scenario.CandidateId) ? "TD" : Sanitize(scenario.CandidateId);
                    var docxName = $"De_{sbd}_{Sanitize(scenario.CandidateName)}_{examDate:yyyyMMdd}.docx";
                    var docxPath = Path.Combine(outputDir, docxName);
                    _wordWriter.Create(docxPath, scenario);
                    archive.CreateEntryFromFile(docxPath, docxName);

                    // 2. Generate SQL script (.sql)
                    var sqlContent = _sqlGenerator.Generate(scenario);
                    var sqlName = $"SQL_{sbd}_{Sanitize(scenario.CandidateName)}_{examDate:yyyyMMdd}.sql";
                    var sqlPath = Path.Combine(outputDir, sqlName);
                    await File.WriteAllTextAsync(sqlPath, sqlContent);
                    archive.CreateEntryFromFile(sqlPath, sqlName);

                    // Mark patient used in database
                    if (!string.IsNullOrWhiteSpace(scenario.Patient.MedicalCode))
                    {
                        await _database.MarkPatientUsedAsync(scenario.Patient.MedicalCode, scenario.Patient.FullName, batchName);
                    }
                    if (scenario.SecondPatient is not null && !string.IsNullOrWhiteSpace(scenario.SecondPatient.MedicalCode))
                    {
                        await _database.MarkPatientUsedAsync(scenario.SecondPatient.MedicalCode, scenario.SecondPatient.FullName, batchName);
                    }
                }

                // 3. Generate Merged Word Document (.docx) for one-click printing of all candidates
                if (scenarios.Count >= 1)
                {
                    var mergedDocxName = $"_InGop_TatCaDeThi_{batchName}_{examDate:yyyyMMdd}.docx";
                    var mergedDocxPath = Path.Combine(outputDir, mergedDocxName);
                    _wordWriter.CreateMerged(mergedDocxPath, scenarios);
                    archive.CreateEntryFromFile(mergedDocxPath, mergedDocxName);
                }
            }

            await _database.SaveGeneratedFileAsync(Guid.NewGuid().ToString("N"), batchName, examDate, zipPath);
            GenerationStatusText.Text = $"Sinh thành công {scenarios.Count} bộ đề thi, tệp in gộp và script SQL! File ZIP: {zipPath}";
            FooterStatusText.Text = $"Đã xuất {scenarios.Count} đề thi (kèm tệp in gộp) vào tệp tin: {Path.GetFileName(zipPath)}";

            await ReloadHistoryDataAsync();
            await RefreshDashboardCardsAsync();

            var res = MessageBox.Show($"Sinh hoàn tất {scenarios.Count} bộ đề thi và script SQL!\n\nĐã tạo sẵn tệp in gộp: _InGop_TatCaDeThi_{batchName}_{examDate:yyyyMMdd}.docx (in 1 lần ra toàn bộ đề).\n\nBạn có muốn mở thư mục chứa tệp tin ZIP ngay không?", "Thành công", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (res == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{zipPath}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            GenerationStatusText.Text = "Lỗi sinh đề: " + ex.Message;
            MessageBox.Show(ex.Message, "Preflight Chặn Sinh Đề", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveMapping_Click(object sender, RoutedEventArgs e)
    {
        if (MappingDepartmentList.SelectedItem is not DepartmentRow department)
        {
            MappingStatusText.Text = "Vui lòng chọn Khoa khảo thí từ danh sách bên trái.";
            return;
        }

        var selectedWarehouses = _allWarehouseItems.Where(x => x.IsSelected).Select(x => (x.Code, x.Name)).ToList();
        if (selectedWarehouses.Count == 0)
        {
            MappingStatusText.Text = "Vui lòng chọn ít nhất một Kho Dược / Tủ trực thi cho khoa này.";
            MessageBox.Show("Vui lòng chọn ít nhất một Kho Dược / Tủ trực thi cho khoa này.", "Thiếu cấu hình Kho", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _database.SaveDepartmentWarehouseMappingsAsync(department.Code, department.Name, selectedWarehouses);
        var selectedGroups = _allServiceGroups.Where(x => x.IsSelected).Select(x => (x.Code, x.Name)).ToList();
        await _database.ReplaceDepartmentServiceGroupsAsync(department.Code, selectedGroups);
        MappingStatusText.Text = $"Đã lưu cấu hình: {department.Name} -> {selectedWarehouses.Count} kho thi ({selectedGroups.Count} nhóm dịch vụ).";
        await LoadMappingsAsync();
        await RefreshDashboardCardsAsync();
        MessageBox.Show($"Đã lưu thành công cấu hình cho {department.Name}:\n• Số lượng kho thi: {selectedWarehouses.Count} kho ({string.Join(", ", selectedWarehouses.Select(w => w.Name))})\n• Số nhóm dịch vụ CLS được phép: {selectedGroups.Count}", "Lưu Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void MappingDepartmentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MappingDepartmentList.SelectedItem is not DepartmentRow d) return;
        _selectedMappingDepartment = d.Code;
        MappingTitleText.Text = $"Cấu hình: {d.Name}";
        DepartmentActiveCheckBox.IsChecked = !d.IsDisabled;

        var mappedWarehouses = await _database.GetWarehousesForDepartmentAsync(d.Code);
        var mappedWhCodes = mappedWarehouses.Select(x => x.WarehouseCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // If no warehouses are currently mapped, auto-suggest warehouses that match DepartmentId if available
        if (mappedWhCodes.Count == 0)
        {
            var matchedByDept = _allWarehouseItems
                .Where(w => !string.IsNullOrEmpty(d.Code) && string.Equals(w.DepartmentId, d.Code, StringComparison.OrdinalIgnoreCase))
                .Select(w => w.Code)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (matchedByDept.Count > 0)
            {
                mappedWhCodes = matchedByDept;
            }
        }

        foreach (var wh in _allWarehouseItems)
        {
            wh.IsSelected = mappedWhCodes.Contains(wh.Code);
        }
        RefreshWarehouseMappingsList();

        var codes = await _database.GetDepartmentServiceGroupCodesAsync(d.Code);
        var codeSet = codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in _allServiceGroups)
        {
            group.IsSelected = codeSet.Contains(group.Code);
        }
        RefreshServiceGroupsList();
    }

    private void RefreshWarehouseMappingsList()
    {
        var kw = WarehouseSearchTextBox.Text?.Trim() ?? string.Empty;
        WarehouseMappingsList.ItemsSource = null;
        WarehouseMappingsList.ItemsSource = _allWarehouseItems
            .Where(x => string.IsNullOrWhiteSpace(kw) || x.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) || x.Code.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void WarehouseSearchChanged(object sender, TextChangedEventArgs e)
    {
        RefreshWarehouseMappingsList();
    }

    private void SelectAllWarehouses_Click(object sender, RoutedEventArgs e)
    {
        var kw = WarehouseSearchTextBox.Text?.Trim() ?? string.Empty;
        var targetList = _allWarehouseItems
            .Where(x => string.IsNullOrWhiteSpace(kw) || x.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) || x.Code.Contains(kw, StringComparison.OrdinalIgnoreCase));
        foreach (var wh in targetList)
        {
            wh.IsSelected = true;
        }
    }

    private void ClearAllWarehouses_Click(object sender, RoutedEventArgs e)
    {
        var kw = WarehouseSearchTextBox.Text?.Trim() ?? string.Empty;
        var targetList = _allWarehouseItems
            .Where(x => string.IsNullOrWhiteSpace(kw) || x.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) || x.Code.Contains(kw, StringComparison.OrdinalIgnoreCase));
        foreach (var wh in targetList)
        {
            wh.IsSelected = false;
        }
    }

    private void MappingSearchChanged(object sender, TextChangedEventArgs e)
    {
        var kw = MappingSearchTextBox.Text.Trim();
        MappingDepartmentList.ItemsSource = _departments
            .Where(x => x.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void ServiceSearchChanged(object sender, TextChangedEventArgs e)
    {
        RefreshServiceGroupsList();
    }

    private async void DepartmentActive_Click(object sender, RoutedEventArgs e)
    {
        if (MappingDepartmentList.SelectedItem is not DepartmentRow dept) return;
        bool isActive = DepartmentActiveCheckBox.IsChecked == true;
        bool isDisabled = !isActive;

        await _database.SetDepartmentExamStatusAsync(dept.Code, isDisabled);
        MappingStatusText.Text = isActive
            ? $"Đã mở lại trạng thái khảo thí cho {dept.Name}."
            : $"Đã tạm ngưng khảo thí cho {dept.Name} (Cấu hình mapping & đề thi vẫn được bảo toàn nguyên vẹn).";

        var currentCode = dept.Code;
        await LoadStaticViewsAsync();

        var reselect = _departments.FirstOrDefault(x => x.Code == currentCode);
        if (reselect is not null)
        {
            MappingDepartmentList.SelectedItem = reselect;
        }
    }

    private void SelectAllServices_Click(object sender, RoutedEventArgs e)
    {
        var kw = ServiceSearchTextBox.Text?.Trim() ?? string.Empty;
        var targetList = _allServiceGroups
            .Where(x => string.IsNullOrWhiteSpace(kw) || x.Name.Contains(kw, StringComparison.OrdinalIgnoreCase));
        foreach (var sg in targetList)
        {
            sg.IsSelected = true;
        }
    }

    private void ClearAllServices_Click(object sender, RoutedEventArgs e)
    {
        var kw = ServiceSearchTextBox.Text?.Trim() ?? string.Empty;
        var targetList = _allServiceGroups
            .Where(x => string.IsNullOrWhiteSpace(kw) || x.Name.Contains(kw, StringComparison.OrdinalIgnoreCase));
        foreach (var sg in targetList)
        {
            sg.IsSelected = false;
        }
    }

    private async void CopyMapping_Click(object sender, RoutedEventArgs e)
    {
        if (MappingDepartmentList.SelectedItem is not DepartmentRow target)
        {
            MappingStatusText.Text = "Vui lòng chọn khoa đích trước khi sao chép.";
            return;
        }

        var source = _departments.FirstOrDefault(x => x.Code != target.Code);
        if (source is null) return;

        var groups = await _database.GetDepartmentServiceGroupCodesAsync(source.Code);
        var groupSet = groups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sg in _allServiceGroups)
        {
            sg.IsSelected = groupSet.Contains(sg.Code);
        }
        RefreshServiceGroupsList();
        MappingStatusText.Text = $"Đã sao chép {groups.Count} nhóm dịch vụ từ {source.Name}; hãy lưu kho thi cho {target.Name}.";
    }

    private async Task LoadMappingsAsync()
    {
        var mappings = await _database.GetDepartmentWarehouseMappingsAsync();
        var deptCount = mappings.Select(m => m.DepartmentCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        MappingStatusText.Text = mappings.Count == 0
            ? "Chưa có mapping kho thi được lưu."
            : $"Đã lưu {mappings.Count} liên kết kho thi cho {deptCount} khoa phòng.";
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = new SqlServerConnectionProfile(
                ProfileNameTextBox.Text.Trim(),
                ServerTextBox.Text.Trim(),
                DatabaseTextBox.Text.Trim(),
                WindowsAuthenticationCheckBox.IsChecked == true,
                SqlUserTextBox.Text.Trim());

            await _settingsStore.SaveAsync(new ApplicationSettings("exam-generator.sqlite", new[] { p }, p.Name));
            if (!p.UseWindowsAuthentication) _credentialStore.Save(p.Name, SqlPasswordBox.Password);
            ConnectionStatusText.Text = "Đã lưu Profile kết nối SQL Server thành công.";
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = "Lỗi lưu cấu hình: " + ex.Message;
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = new SqlServerConnectionProfile(
                ProfileNameTextBox.Text,
                ServerTextBox.Text,
                DatabaseTextBox.Text,
                WindowsAuthenticationCheckBox.IsChecked == true,
                SqlUserTextBox.Text);

            await SqlServerConnectionTester.TestAsync(p, SqlPasswordBox.Password);
            ConnectionStatusText.Text = "Kết nối SQL Server THÀNH CÔNG (SELECT 1 OK).";
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = "Kết nối THẤT BẠI: " + ex.Message;
        }
    }

    private async void SynchronizeCatalogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConnectionStatusText.Text = "Đang kết nối và đồng bộ dữ liệu danh mục từ SQL Server HIS...";
            FooterStatusText.Text = "Đang thực hiện đồng bộ snapshot danh mục HIS...";
            var p = new SqlServerConnectionProfile(
                ProfileNameTextBox.Text.Trim(),
                ServerTextBox.Text.Trim(),
                DatabaseTextBox.Text.Trim(),
                WindowsAuthenticationCheckBox.IsChecked == true,
                SqlUserTextBox.Text.Trim());

            var counts = await new HisCatalogSynchronizer().SynchronizeAsync(
                p,
                SqlPasswordBox.Password,
                _database,
                progress =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ConnectionStatusText.Text = progress;
                        FooterStatusText.Text = progress;
                    });
                });
            await LoadStaticViewsAsync();
            await LoadMappingsAsync();
            await RefreshDashboardCardsAsync();

            var deptTotal = counts.GetValueOrDefault("Departments", 0);
            var deptAdded = counts.GetValueOrDefault("Departments_Added", 0);
            var deptPreserved = counts.GetValueOrDefault("Departments_Preserved", 0);

            var summaryText =
                $"• Khoa / Phòng (Cấp 1): {deptTotal:N0} khoa (Bảo lưu: {deptPreserved:N0} khoa & mapping/mẫu đề; Bổ sung mới: {deptAdded:N0} khoa)\n" +
                $"• Kho Dược / Tủ trực: {counts.GetValueOrDefault("Warehouses", 0):N0} bản ghi\n" +
                $"• Nhóm Dịch vụ CLS: {counts.GetValueOrDefault("ServiceGroups", 0):N0} bản ghi\n" +
                $"• Dịch vụ Kỹ thuật / CLS: {counts.GetValueOrDefault("Services", 0):N0} bản ghi\n" +
                $"• Tài khoản User HIS: {counts.GetValueOrDefault("Users", 0):N0} tài khoản\n" +
                $"• Thuốc / VTYT có tồn dương: {counts.GetValueOrDefault("Drugs", 0):N0} mặt hàng\n" +
                $"• Hồ sơ Bệnh nhân nguồn: {counts.GetValueOrDefault("Patients", 0):N0} hồ sơ";

            ConnectionStatusText.Text = "Đồng bộ THÀNH CÔNG toàn bộ danh mục từ HIS!";
            FooterStatusText.Text = "Đồng bộ snapshot HIS thành công.";

            MessageBox.Show(
                "ĐỒNG BỘ DANH MỤC TỪ SQL SERVER HIS THÀNH CÔNG!\n\n" +
                "Chi tiết số lượng dữ liệu snapshot đã nạp vào SQLite:\n" +
                summaryText + "\n\n" +
                "Dữ liệu này là nguồn chuẩn duy nhất phục vụ phân bổ kịch bản và sinh đề thi.",
                "Đồng Bộ Thành Công",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = "Đồng bộ THẤT BẠI: " + ex.Message;
            FooterStatusText.Text = "Lỗi đồng bộ danh mục HIS: " + ex.Message;
            MessageBox.Show(
                "ĐỒNG BỘ DANH MỤC THẤT BẠI!\n\n" +
                $"Chi tiết lỗi:\n{ex.Message}\n\n" +
                "Vui lòng kiểm tra lại thông tin kết nối SQL Server (Địa chỉ máy chủ, Tên database, Tài khoản/Mật khẩu hoặc quyền truy cập) rồi thử lại.",
                "Lỗi Đồng Bộ Catalog HIS",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AuthenticationChanged(object sender, RoutedEventArgs e)
    {
        if (SqlUserLabel is null) return;
        var v = WindowsAuthenticationCheckBox.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        SqlUserLabel.Visibility = v;
        SqlUserTextBox.Visibility = v;
        SqlPasswordLabel.Visibility = v;
        SqlPasswordBox.Visibility = v;
    }

    private void ApplyTemplateFilter()
    {
        var filterDept = TemplateDeptFilterComboBox.SelectedItem as string ?? "Tất cả khoa";
        var list = (filterDept == "Tất cả khoa" || string.IsNullOrWhiteSpace(filterDept))
            ? _templates.ToList()
            : _templates.Where(x => x.Department.Equals(filterDept, StringComparison.OrdinalIgnoreCase)).ToList();

        TemplatesGrid.ItemsSource = null;
        TemplatesGrid.ItemsSource = list;

        // Try to select active template or first in list
        if (!string.IsNullOrEmpty(_selectedTemplateId))
        {
            var match = list.FirstOrDefault(x => x.Id == _selectedTemplateId);
            if (match is not null)
            {
                TemplatesGrid.SelectedItem = match;
                return;
            }
        }

        if (list.Count > 0 && TemplatesGrid.SelectedIndex < 0)
        {
            TemplatesGrid.SelectedIndex = 0;
        }
    }

    private void TemplateDeptFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyTemplateFilter();
    }

    private async void TemplateSelected(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (TemplatesGrid.SelectedItem is not TemplateRow template) return;

            _selectedTemplateId = template.Id;
            _isNewTemplateMode = false;

            TemplateEditorTitle.Text = $"Biên tập: {template.Name}";
            TemplateEditorModeBadge.Text = "Đang chỉnh sửa";
            TemplateEditorModeBadge.Foreground = (TryFindResource("BrandTeal") as System.Windows.Media.Brush) ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0D, 0x94, 0x88));
            TemplateEditorModeBadgeBorder.Background = (TryFindResource("BrandTealSoft") as System.Windows.Media.Brush) ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xFB, 0xF1));
            TemplateEditorModeBadgeBorder.BorderBrush = (TryFindResource("BrandTeal") as System.Windows.Media.Brush) ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0D, 0x94, 0x88));

            EditorTemplateNameTextBox.Text = template.Name;

            if (EditorDepartmentComboBox.Items.Contains(template.Department))
            {
                EditorDepartmentComboBox.SelectedItem = template.Department;
            }
            else
            {
                EditorDepartmentComboBox.Text = template.Department;
            }

            EditorPositionComboBox.Text = template.Position;
            EditorReceptionModeComboBox.SelectedIndex = template.Status.Contains("Tiếp nhận") ? 0 : 1;

            var actions = await _database.GetTemplateActionDetailsAsync(template.Id);
            _currentQuestions.Clear();
            for (var i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                var qVm = new TemplateQuestionItemViewModel(i + 1, a.Code, a.Name, a.Score);
                qVm.ScoreChanged += (_, _) => UpdateTotalScoreDisplay();
                _currentQuestions.Add(qVm);
            }

            QuestionsCountText.Text = $"({_currentQuestions.Count} câu)";
            UpdateTotalScoreDisplay();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi tải chi tiết mẫu đề: {ex.Message}", "Lỗi tải mẫu đề", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NewTemplate_Click(object sender, RoutedEventArgs e)
    {
        _selectedTemplateId = null;
        _isNewTemplateMode = true;

        TemplatesGrid.SelectedIndex = -1;

        TemplateEditorTitle.Text = "Tạo Mẫu Đề Thi Mới";
        TemplateEditorModeBadge.Text = "Tạo mới";
        TemplateEditorModeBadge.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x02, 0x84, 0xC7));
        TemplateEditorModeBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xF2, 0xFE));
        TemplateEditorModeBadgeBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x02, 0x84, 0xC7));

        EditorTemplateNameTextBox.Text = "Mẫu đề thực hành mới";

        var activeDepts = _departments.Where(x => !x.IsDisabled).Select(x => x.Name).ToList();
        if (activeDepts.Count > 0)
        {
            EditorDepartmentComboBox.SelectedItem = activeDepts[0];
        }
        else if (_departments.Count > 0)
        {
            EditorDepartmentComboBox.SelectedIndex = 0;
        }

        EditorPositionComboBox.Text = "Điều dưỡng";
        EditorReceptionModeComboBox.SelectedIndex = 1; // Hàng chờ nhận khoa

        _currentQuestions.Clear();

        // Gợi ý khung 4 câu hỏi chuẩn ban đầu
        var defaultCodes = new[] { "NT_NHAN_BENH_KHOA", "YL_CHI_DINH_CLS", "YL_CHI_DINH_THUOC_VTYT", "TK_KIEM_TON_KHO" };
        var order = 1;
        foreach (var c in defaultCodes)
        {
            var def = SystemActionCatalog.All.FirstOrDefault(x => x.Code == c);
            if (def != null)
            {
                var qVm = new TemplateQuestionItemViewModel(order++, def.Code, def.Name, def.DefaultScore);
                qVm.ScoreChanged += (_, _) => UpdateTotalScoreDisplay();
                _currentQuestions.Add(qVm);
            }
        }

        QuestionsCountText.Text = $"({_currentQuestions.Count} câu)";
        UpdateTotalScoreDisplay();
        EditorTemplateNameTextBox.Focus();
        EditorTemplateNameTextBox.SelectAll();
    }

    private async void CloneTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplatesGrid.SelectedItem is not TemplateRow source)
        {
            MessageBox.Show("Vui lòng chọn một mẫu đề từ danh sách bên trái để nhân bản (clone).", "Chưa chọn mẫu đề", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var allDepts = _departments.Select(d => d.Name)
            .Concat(_templates.Select(t => t.Department))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

        var dialog = new CloneTemplateWindow(
            source.Name,
            source.Department,
            source.Position,
            source.Status.Contains("Tiếp nhận"),
            allDepts)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            var actions = await _database.GetTemplateActionDetailsAsync(source.Id);
            var newId = "tpl_" + Guid.NewGuid().ToString("N")[..8];
            var receptionMode = dialog.IsDirectReception ? "DirectReception" : "WardAdmissionPreparation";

            await _database.SaveTemplateAsync(
                newId,
                dialog.NewName,
                dialog.NewDepartment,
                dialog.NewPosition,
                receptionMode,
                actions
            );

            _selectedTemplateId = newId;
            await ReloadTemplatesAsync();
            ExamDepartmentChanged(this, null!);

            FooterStatusText.Text = $"Đã nhân bản mẫu đề thành công: '{dialog.NewName}' ({dialog.NewDepartment}).";
            MessageBox.Show($"Đã nhân bản thành công mẫu đề thi:\n• Tên đề mới: {dialog.NewName}\n• Khoa áp dụng: {dialog.NewDepartment}\n• Vị trí: {dialog.NewPosition}\n\nMẫu đề mới đã được mở trong khung biên tập bên phải để bạn có thể tiếp tục tùy chỉnh.", "Nhân Bản Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplatesGrid.SelectedItem is not TemplateRow template)
        {
            MessageBox.Show("Vui lòng chọn mẫu đề cần xóa từ danh sách bên trái.", "Chưa chọn mẫu đề", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Kiểm tra xem mẫu đề có đang được chọn bởi thí sinh nào trong danh sách chờ thi không
        var usingCandidate = _candidates.FirstOrDefault(c => c.Template.Equals(template.Name, StringComparison.OrdinalIgnoreCase));
        if (usingCandidate != null)
        {
            MessageBox.Show($"Không thể xóa mẫu đề '{template.Name}' vì đang có thí sinh '{usingCandidate.Name}' trong danh sách thi đăng ký mẫu đề này.\n\nVui lòng xóa hoặc đổi mẫu đề của thí sinh trước.", "Mẫu đề đang được sử dụng", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Bạn có chắc chắn muốn xóa mẫu đề '{template.Name}' (Khoa: {template.Department})?\n\nLưu ý: Hành động này không thể hoàn tác.", "Xác nhận xóa mẫu đề", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await _database.DeleteTemplateAsync(template.Id);
        _selectedTemplateId = null;
        await ReloadTemplatesAsync();
        ExamDepartmentChanged(this, null!);
        FooterStatusText.Text = $"Đã xóa mẫu đề '{template.Name}' thành công.";
    }

    private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        var name = EditorTemplateNameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Vui lòng nhập tên cho mẫu đề thi.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            EditorTemplateNameTextBox.Focus();
            return;
        }

        var dept = EditorDepartmentComboBox.SelectedItem as string ?? EditorDepartmentComboBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(dept))
        {
            MessageBox.Show("Vui lòng chọn Khoa / Phòng áp dụng cho mẫu đề thi.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            EditorDepartmentComboBox.Focus();
            return;
        }

        var position = EditorPositionComboBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(position))
        {
            position = "Điều dưỡng";
        }

        var receptionMode = EditorReceptionModeComboBox.SelectedIndex == 0 ? "DirectReception" : "WardAdmissionPreparation";

        if (_currentQuestions.Count == 0)
        {
            MessageBox.Show("Mẫu đề thi phải có ít nhất 1 câu hỏi. Vui lòng thêm câu hỏi từ Thư viện Nghiệp vụ HIS bên dưới.", "Thiếu câu hỏi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var totalScore = _currentQuestions.Sum(q => q.Score);
        if (Math.Abs(totalScore - 10.0) > 0.001)
        {
            var confirmScore = MessageBox.Show(
                $"Tổng điểm của mẫu đề hiện tại là {totalScore:0.0} điểm (khác chuẩn 10.0 điểm theo quy chế khảo thí).\n\nBạn có chắc chắn muốn lưu mẫu đề này không?",
                "Xác nhận lưu điểm khác chuẩn",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmScore != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var id = _isNewTemplateMode || string.IsNullOrWhiteSpace(_selectedTemplateId)
            ? "tpl_" + Guid.NewGuid().ToString("N")[..8]
            : _selectedTemplateId;

        var actions = _currentQuestions.Select(q => (q.ActionCode, q.ActionName, q.Score)).ToList();

        await _database.SaveTemplateAsync(id, name, dept, position, receptionMode, actions);

        _selectedTemplateId = id;
        _isNewTemplateMode = false;

        await ReloadTemplatesAsync();
        ExamDepartmentChanged(this, null!);

        FooterStatusText.Text = $"Đã lưu thành công mẫu đề '{name}' ({_currentQuestions.Count} câu, {totalScore:0.0} điểm).";
        MessageBox.Show($"ĐÃ LƯU THÀNH CÔNG MẪU ĐỀ THI!\n\n• Tên đề: {name}\n• Khoa áp dụng: {dept}\n• Vị trí: {position}\n• Số câu hỏi: {_currentQuestions.Count}\n• Tổng điểm: {totalScore:0.0} điểm\n• Hình thức nhận ca: {(receptionMode == "DirectReception" ? "Tiếp nhận trực tiếp" : "Hàng chờ nhận khoa")}", "Lưu Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CancelEditTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_templates.Count > 0)
        {
            if (!string.IsNullOrEmpty(_selectedTemplateId))
            {
                var match = _templates.FirstOrDefault(x => x.Id == _selectedTemplateId);
                if (match != null)
                {
                    TemplatesGrid.SelectedItem = match;
                    TemplateSelected(this, null!);
                    return;
                }
            }
            TemplatesGrid.SelectedIndex = 0;
        }
        else
        {
            NewTemplate_Click(this, null!);
        }
    }

    private async Task ReloadActionLibraryAsync(string? selectCode = null)
    {
        try
        {
            var actions = await _database.GetActionDefinitionsAsync();
            ActionLibraryComboBox.ItemsSource = actions;

            if (!string.IsNullOrEmpty(selectCode))
            {
                var match = actions.FirstOrDefault(a => a.Code.Equals(selectCode, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    ActionLibraryComboBox.SelectedItem = match;
                    return;
                }
            }

            if (actions.Count > 0 && ActionLibraryComboBox.SelectedItem == null)
            {
                ActionLibraryComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            HeaderStatusText.Text = $"Lỗi tải thư viện nghiệp vụ: {ex.Message}";
        }
    }

    private async void ManageActionLibrary_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ManageActionCatalogWindow(_database)
            {
                Owner = this
            };

            dialog.ShowDialog();
            await ReloadActionLibraryAsync(dialog.LastCreatedOrSelectedActionCode);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi mở Thư viện dạng câu hỏi: {ex.Message}", "Lỗi Thư viện", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddActionToTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (ActionLibraryComboBox.SelectedItem is not SystemActionDefinition actionDef)
        {
            MessageBox.Show("Vui lòng chọn một dạng câu hỏi từ Thư viện Nghiệp vụ HIS.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newOrder = _currentQuestions.Count + 1;
        var qVm = new TemplateQuestionItemViewModel(newOrder, actionDef.Code, actionDef.Name, actionDef.DefaultScore);
        qVm.ScoreChanged += (_, _) => UpdateTotalScoreDisplay();

        _currentQuestions.Add(qVm);
        QuestionsCountText.Text = $"({_currentQuestions.Count} câu)";
        UpdateTotalScoreDisplay();

        EditorQuestionsGrid.SelectedItem = qVm;
        EditorQuestionsGrid.ScrollIntoView(qVm);
    }

    private void QuestionMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TemplateQuestionItemViewModel item) return;
        var index = _currentQuestions.IndexOf(item);
        if (index <= 0) return;

        _currentQuestions.Move(index, index - 1);
        ReindexQuestions();
        EditorQuestionsGrid.SelectedItem = item;
    }

    private void QuestionMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TemplateQuestionItemViewModel item) return;
        var index = _currentQuestions.IndexOf(item);
        if (index < 0 || index >= _currentQuestions.Count - 1) return;

        _currentQuestions.Move(index, index + 1);
        ReindexQuestions();
        EditorQuestionsGrid.SelectedItem = item;
    }

    private void QuestionRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TemplateQuestionItemViewModel item) return;

        if (MessageBox.Show($"Bạn có chắc chắn muốn xóa câu '{item.ActionName}' khỏi mẫu đề này?", "Xác nhận xóa câu hỏi", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _currentQuestions.Remove(item);
        ReindexQuestions();
        QuestionsCountText.Text = $"({_currentQuestions.Count} câu)";
        UpdateTotalScoreDisplay();
    }

    private void ReindexQuestions()
    {
        for (var i = 0; i < _currentQuestions.Count; i++)
        {
            _currentQuestions[i].OrderIndex = i + 1;
        }
    }

    private void UpdateTotalScoreDisplay()
    {
        var total = _currentQuestions.Sum(q => q.Score);
        TotalScoreText.Text = $"Tổng điểm: {total:0.0} / 10.0 đ";

        if (Math.Abs(total - 10.0) < 0.001)
        {
            TotalScoreBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDC, 0xFC, 0xE7));
            TotalScoreBadgeBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x86, 0xEF, 0xAC));
            TotalScoreText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x65, 0x34));
            TotalScoreStatusText.Text = "✓ Đạt chuẩn 10.0 điểm theo quy chế khảo thí BV Thiện Hạnh.";
            TotalScoreStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x65, 0x34));
        }
        else
        {
            TotalScoreBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFE, 0xF3, 0xC7));
            TotalScoreBadgeBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFD, 0xBA, 0x74));
            TotalScoreText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0x34, 0x12));
            TotalScoreStatusText.Text = $"⚠️ Tổng điểm hiện tại là {total:0.0}đ (Quy chuẩn tuyển dụng là 10.0đ).";
            TotalScoreStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0x34, 0x12));
        }
    }

    private async Task ReloadTemplatesAsync()
    {
        _templates.Clear();
        var rows = await _database.GetTemplatesAsync();
        _templates.AddRange(rows.Select(x => new TemplateRow(
            x.Id,
            x.Name,
            x.Department,
            x.Position,
            x.QuestionCount,
            x.TotalScore,
            x.ReceptionMode.Contains("DirectReception") ? "Tiếp nhận trực tiếp" : "Hàng chờ nhận khoa"
        )));

        ApplyTemplateFilter();
    }

    private void InventoryDepartmentChanged(object sender, SelectionChangedEventArgs e)
    {
        var deptName = InventoryDepartmentComboBox.SelectedItem as string ?? InventoryDepartmentComboBox.Text;
        UpdateInventoryWarehousesForDepartment(deptName);
        _ = SearchInventoryCoreAsync();
    }

    private async void InventorySearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            await SearchInventoryCoreAsync();
        }
    }

    private async void SearchInventory_Click(object sender, RoutedEventArgs e) => await SearchInventoryCoreAsync();

    private async Task SearchInventoryCoreAsync()
    {
        var dept = InventoryDepartmentComboBox.SelectedItem as string;
        var wh = (InventoryWarehouseComboBox.SelectedItem as WarehouseRow)?.Code;
        var source = InventorySourceComboBox.SelectedItem as string;
        var kw = InventorySearchTextBox.Text?.Trim();

        var rows = await _database.SearchInventoryAsync(dept, wh, source, kw);
        InventoryGrid.ItemsSource = rows;
    }

    private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl && MainTabs.SelectedItem is TabItem tab)
        {
            var header = tab.Header?.ToString() ?? string.Empty;
            if (header.Contains("Lịch sử"))
            {
                await ReloadHistoryDataAsync();
            }
        }
    }

    private async void ReloadHistory_Click(object sender, RoutedEventArgs e) => await ReloadHistoryDataAsync();
    private async void HistorySearchChanged(object sender, TextChangedEventArgs e) => await ReloadHistoryDataAsync();

    private void HistoryGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenHistoryFile_Click(sender, e);

    private void OpenHistoryFile_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row)
        {
            MessageBox.Show("Chọn một đợt thi trong danh sách để mở tệp ZIP.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var path = row.FilePath;
        if (!File.Exists(path))
        {
            var fallback = Path.Combine(_storage.OutputDirectory, Path.GetFileName(path));
            if (!File.Exists(fallback))
            {
                fallback = Path.Combine(_storage.ApplicationDirectory, "output", Path.GetFileName(path));
            }
            if (File.Exists(fallback))
            {
                path = fallback;
            }
            else
            {
                MessageBox.Show($"Không tìm thấy tệp tin ZIP tại:\n{row.FilePath}\n\nCó thể tệp tin đã bị xóa hoặc chuyển sang thư mục khác.", "Không tìm thấy tệp", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private async Task ReloadHistoryDataAsync()
    {
        try
        {
            if (HistoryGrid == null) return;
            var history = await _database.GetGeneratedFilesAsync();
            var filter = HistorySearchTextBox?.Text?.Trim() ?? string.Empty;
            HistoryGrid.ItemsSource = history
                .Where(x => string.IsNullOrEmpty(filter) ||
                            x.BatchName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            x.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(x.FilePath).Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Select(x => new HistoryRow(x.BatchName, x.ExamDate, x.FilePath, x.CreatedAt))
                .ToList();
        }
        catch
        {
            // Do not crash during startup/background refresh
        }
    }

    private async Task RefreshCatalogSummaryAsync()
    {
        var summary = await _database.GetCatalogSummaryAsync();
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Departments"] = "Khoa/phòng",
            ["Warehouses"] = "Kho dược",
            ["ServiceGroups"] = "Nhóm dịch vụ CLS",
            ["Services"] = "Dịch vụ kỹ thuật",
            ["Patients"] = "Hồ sơ Bệnh nhân",
            ["Drugs"] = "Thuốc & Vật tư y tế",
            ["Users"] = "Tài khoản HIS khảo thí"
        };

        CatalogsGrid.ItemsSource = summary.Select(x => new CatalogRow(
            labels.GetValueOrDefault(x.CatalogType, x.CatalogType),
            x.Count > 0 ? "Sẵn sàng" : "Chưa có dữ liệu",
            $"{x.Count:N0} bản ghi"
        )).ToList();
    }

    private async Task RefreshDashboardCardsAsync()
    {
        var summary = (await _database.GetCatalogSummaryAsync()).ToDictionary(x => x.CatalogType, x => x.Count, StringComparer.OrdinalIgnoreCase);
        CardDeptCount.Text = summary.GetValueOrDefault("Departments", 0).ToString("N0");
        CardWhCount.Text = summary.GetValueOrDefault("Warehouses", 0).ToString("N0");
        CardDrugCount.Text = summary.GetValueOrDefault("Drugs", 0).ToString("N0");
        CardUserCount.Text = summary.GetValueOrDefault("Users", 0).ToString("N0");
    }

    private static string Sanitize(string value) =>
        string.Concat((value ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

    private sealed class CandidateRow(bool selected, string name, string id, string department, string template, string userStatus, int durationMinutes = 30, string? warehouseCode = null, string? warehouseName = null)
    {
        public bool Selected { get; set; } = selected;
        public string Name { get; } = name;
        public string Id { get; } = id;
        public string Department { get; } = department;
        public string Template { get; } = template;
        public string WarehouseCode { get; } = warehouseCode ?? string.Empty;
        public string WarehouseName { get; } = warehouseName ?? string.Empty;
        public string UserStatus { get; } = userStatus;
        public int DurationMinutes { get; } = durationMinutes;
        public string DurationText => $"{DurationMinutes}p";
    }

    private sealed record TemplateRow(string Id, string Name, string Department, string Position, int QuestionCount, double TotalScore, string Status);
    private sealed record DepartmentRow(string Name, string Code, bool IsDisabled = false)
    {
        public Visibility DisabledBadgeVisibility => IsDisabled ? Visibility.Visible : Visibility.Collapsed;
        public string DisplayText => IsDisabled ? $"{Name} (Tạm ngưng)" : Name;
    }
    private sealed record WarehouseRow(string Name, string Code);
    private sealed record CatalogRow(string Catalog, string Status, string Count);
    private sealed record HistoryRow(string Batch, string ExamDate, string FilePath, string CreatedAt)
    {
        public string FileName => Path.GetFileName(FilePath);
        public string FormattedExamDate => DateTime.TryParse(ExamDate, out var d) ? d.ToString("dd/MM/yyyy") : ExamDate;
        public string FormattedCreatedAt => DateTimeOffset.TryParse(CreatedAt, out var dt) ? dt.ToString("dd/MM/yyyy HH:mm:ss") : CreatedAt;
    }
}

public sealed class WarehouseItemViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected;
    public string Name { get; }
    public string Code { get; }
    public string? DepartmentId { get; }
    public string DisplayText => string.IsNullOrWhiteSpace(Code) ? Name : $"{Name} ({Code})";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public WarehouseItemViewModel(string name, string code, string? departmentId = null, bool isSelected = false)
    {
        Name = name;
        Code = code;
        DepartmentId = departmentId;
        _isSelected = isSelected;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ServiceGroupItemViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected;
    public string Name { get; }
    public string Code { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public ServiceGroupItemViewModel(string name, string code, bool isSelected = false)
    {
        Name = name;
        Code = code;
        _isSelected = isSelected;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public sealed class TemplateQuestionItemViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private int _orderIndex;
    private string _actionCode;
    private string _actionName;
    private double _score;
    private string _scoreText;

    public int OrderIndex
    {
        get => _orderIndex;
        set
        {
            if (_orderIndex != value)
            {
                _orderIndex = value;
                OnPropertyChanged(nameof(OrderIndex));
            }
        }
    }

    public string ActionCode
    {
        get => _actionCode;
        set
        {
            if (_actionCode != value)
            {
                _actionCode = value;
                OnPropertyChanged(nameof(ActionCode));
            }
        }
    }

    public string ActionName
    {
        get => _actionName;
        set
        {
            if (_actionName != value)
            {
                _actionName = value;
                OnPropertyChanged(nameof(ActionName));
            }
        }
    }

    public double Score
    {
        get => _score;
        set
        {
            if (Math.Abs(_score - value) > 0.001)
            {
                _score = value;
                _scoreText = value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                OnPropertyChanged(nameof(Score));
                OnPropertyChanged(nameof(ScoreText));
                ScoreChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string ScoreText
    {
        get => _scoreText;
        set
        {
            if (_scoreText != value)
            {
                _scoreText = value;
                OnPropertyChanged(nameof(ScoreText));
                if (double.TryParse(value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var s) && s >= 0)
                {
                    _score = s;
                    OnPropertyChanged(nameof(Score));
                    ScoreChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    public TemplateQuestionItemViewModel(int orderIndex, string code, string name, double score)
    {
        _orderIndex = orderIndex;
        _actionCode = code;
        _actionName = name;
        _score = score;
        _scoreText = score.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    public event EventHandler? ScoreChanged;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(prop));
}

