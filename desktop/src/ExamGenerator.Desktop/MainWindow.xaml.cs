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
    private readonly List<DepartmentRow> _departments =
    [
        new("Khoa Nội", "NOI"),
        new("Khoa Ngoại tổng hợp", "NGOAI"),
        new("Khoa Phụ Sản", "SAN"),
        new("Khoa Nhi", "NHI"),
        new("Khoa Cấp cứu", "CC"),
        new("Khoa Khám bệnh", "KKB")
    ];
    private readonly List<WarehouseRow> _warehouses =
    [
        new("Kho trực Nội", "KHO-NOI"),
        new("Kho trực Ngoại", "KHO-NGOAI"),
        new("Kho trực Sản", "KHO-SAN"),
        new("Kho trực Nhi", "KHO-NHI"),
        new("Kho trực Cấp cứu", "KHO-CC"),
        new("Kho dược chính", "KHO-01")
    ];
    private readonly List<ServiceGroupRow> _serviceGroups =
    [
        new("Xét nghiệm", "XN"),
        new("Siêu âm", "SA"),
        new("X-quang", "XQ"),
        new("CT Scan", "CT"),
        new("Thủ thuật", "TT"),
        new("Phẫu thuật", "PT"),
        new("Thăm dò chức năng", "TDCN")
    ];

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

            ExamDatePicker.SelectedDate = DateTime.Today;
            await LoadStaticViewsAsync();
            await LoadMappingsAsync();
            await RefreshDashboardCardsAsync();
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

        CandidatesGrid.ItemsSource = _candidates;
        TemplatesGrid.ItemsSource = _templates;

        ExamDepartmentComboBox.ItemsSource = _departments.Select(x => x.Name).ToList();
        if (_departments.Count > 0) ExamDepartmentComboBox.SelectedIndex = 0;

        MappingDepartmentList.ItemsSource = _departments;
        InventoryDepartmentComboBox.ItemsSource = new[] { "Tất cả khoa" }.Concat(_departments.Select(x => x.Name)).ToList();
        InventoryDepartmentComboBox.SelectedIndex = 0;

        InventoryWarehouseComboBox.ItemsSource = new[] { new WarehouseRow("Tất cả kho", "ALL") }.Concat(_warehouses).ToList();
        InventoryWarehouseComboBox.DisplayMemberPath = "Name";
        InventoryWarehouseComboBox.SelectedIndex = 0;

        InventorySourceComboBox.ItemsSource = new[] { "Tất cả nguồn", "BHYT", "Viện phí" };
        InventorySourceComboBox.SelectedIndex = 0;

        MappingWarehouseComboBox.ItemsSource = _warehouses;
        MappingWarehouseComboBox.DisplayMemberPath = "Name";

        ServiceGroupsList.ItemsSource = _serviceGroups;

        await RefreshCatalogSummaryAsync();
        await SearchInventoryCoreAsync();
    }

    private void ExamDepartmentChanged(object sender, SelectionChangedEventArgs e)
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
    }

    private void ExamTemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExamTemplateComboBox.SelectedItem is TemplateRow template)
        {
            ScoreSummaryText.Text = $"{template.Name} · {template.QuestionCount} câu · Tổng {template.TotalScore:0.0} điểm · Chế độ: {template.Status}";
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

        _candidates.Add(new CandidateRow(true, name, id, dept, template.Name, "Tự động cấp User riêng không trùng"));
        CandidatesGrid.Items.Refresh();
        UpdateCandidatesCount();
        CandidateNameTextBox.Clear();
        CandidateIdTextBox.Clear();
        FooterStatusText.Text = $"Đã thêm thí sinh {name} vào danh sách.";
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
            var candidateInputs = candidates.Select(c => new CandidateInput(c.Name, c.Id, c.Department, c.Template)).ToList();
            var scenarios = await allocator.AllocateBatchScenariosAsync(batchName, examDate, candidateInputs);

            var outputDir = Path.Combine(_storage.ApplicationDirectory, "output");
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
                }
            }

            await _database.SaveGeneratedFileAsync(Guid.NewGuid().ToString("N"), batchName, examDate, zipPath);
            GenerationStatusText.Text = $"Sinh thành công {scenarios.Count} bộ đề thi và script SQL! File ZIP: {zipPath}";
            FooterStatusText.Text = $"Đã xuất {scenarios.Count} đề thi vào tệp tin: {Path.GetFileName(zipPath)}";

            await ReloadHistoryDataAsync();
            await RefreshDashboardCardsAsync();

            var res = MessageBox.Show($"Sinh hoàn tất {scenarios.Count} bộ đề thi và script SQL!\nBạn có muốn mở thư mục chứa tệp tin ZIP ngay không?", "Thành công", MessageBoxButton.YesNo, MessageBoxImage.Information);
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
        if (MappingDepartmentList.SelectedItem is not DepartmentRow department ||
            MappingWarehouseComboBox.SelectedItem is not WarehouseRow warehouse)
        {
            MappingStatusText.Text = "Vui lòng chọn Khoa và chọn một Kho thi duy nhất.";
            return;
        }

        await _database.SaveDepartmentConfigurationAsync(department.Code, department.Name, warehouse.Code, warehouse.Name);
        var selectedGroups = ServiceGroupsList.SelectedItems.OfType<ServiceGroupRow>().Select(x => (x.Code, x.Name)).ToList();
        await _database.ReplaceDepartmentServiceGroupsAsync(department.Code, selectedGroups);
        MappingStatusText.Text = $"Đã lưu cấu hình: {department.Name} -> {warehouse.Name} ({selectedGroups.Count} nhóm dịch vụ).";
        await LoadMappingsAsync();
        await RefreshDashboardCardsAsync();
    }

    private async void MappingDepartmentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MappingDepartmentList.SelectedItem is not DepartmentRow d) return;
        _selectedMappingDepartment = d.Code;
        MappingTitleText.Text = $"Cấu hình: {d.Name}";

        var configs = await _database.GetDepartmentConfigurationsAsync();
        var current = configs.FirstOrDefault(x => x.DepartmentCode == d.Code);
        if (current.WarehouseCode is not null)
        {
            var matchWh = _warehouses.FirstOrDefault(w => w.Code == current.WarehouseCode);
            if (matchWh is not null) MappingWarehouseComboBox.SelectedItem = matchWh;
        }

        var codes = await _database.GetDepartmentServiceGroupCodesAsync(d.Code);
        ServiceGroupsList.SelectedItems.Clear();
        foreach (var group in _serviceGroups.Where(x => codes.Contains(x.Code)))
        {
            ServiceGroupsList.SelectedItems.Add(group);
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
        var kw = ServiceSearchTextBox.Text.Trim();
        ServiceGroupsList.ItemsSource = _serviceGroups
            .Where(x => x.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .ToList();
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
        await _database.ReplaceDepartmentServiceGroupsAsync(target.Code, _serviceGroups.Where(x => groups.Contains(x.Code)).Select(x => (x.Code, x.Name)).ToList());
        MappingStatusText.Text = $"Đã sao chép nhóm dịch vụ từ {source.Name}; hãy lưu kho thi cho {target.Name}.";
    }

    private async Task LoadMappingsAsync()
    {
        var mappings = await _database.GetDepartmentConfigurationsAsync();
        MappingStatusText.Text = mappings.Count == 0 ? "Chưa có mapping kho thi được lưu." : $"Đã lưu {mappings.Count} mapping khoa - kho thi.";
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
            ConnectionStatusText.Text = "Đang đồng bộ dữ liệu danh mục từ HIS...";
            var p = new SqlServerConnectionProfile(
                ProfileNameTextBox.Text,
                ServerTextBox.Text,
                DatabaseTextBox.Text,
                WindowsAuthenticationCheckBox.IsChecked == true,
                SqlUserTextBox.Text);

            var counts = await new HisCatalogSynchronizer().SynchronizeAsync(p, SqlPasswordBox.Password, _database);
            await RefreshCatalogSummaryAsync();
            await RefreshDashboardCardsAsync();
            ConnectionStatusText.Text = "Đồng bộ thành công: " + string.Join(" | ", counts.Select(x => $"{x.Key}: {x.Value} bản ghi"));
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = "Đồng bộ không thành công: " + ex.Message;
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

    private async void NewTemplate_Click(object sender, RoutedEventArgs e)
    {
        var department = _departments.First().Name;
        var id = "tpl_" + Guid.NewGuid().ToString("N")[..8];
        var actions = new[]
        {
            ("TN_TIEP_NHAN", "Tiếp nhận trực tiếp người bệnh", 2.0),
            ("YL_CHI_DINH_CLS", "Chỉ định dịch vụ Cận lâm sàng", 2.0),
            ("YL_CHI_DINH_THUOC_VTYT", "Lên y lệnh thuốc & vật tư y tế", 3.0),
            ("TK_KIEM_TON_KHO", "Kiểm tra tồn kho dược tại khoa", 3.0)
        };

        await _database.SaveTemplateAsync(id, "Mẫu đề thực hành " + DateTime.Now.ToString("HHmmss"), department, "Điều dưỡng", "DirectReception", actions);
        await ReloadTemplatesAsync();
        FooterStatusText.Text = "Đã tạo mới một mẫu đề thi.";
    }

    private async void CloneTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplatesGrid.SelectedItem is not TemplateRow source)
        {
            MessageBox.Show("Vui lòng chọn một mẫu đề để clone.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var actions = await _database.GetTemplateActionDetailsAsync(source.Id);
        var target = _departments.FirstOrDefault(x => x.Name != source.Department) ?? _departments.First();

        await _database.SaveTemplateAsync(
            "tpl_" + Guid.NewGuid().ToString("N")[..8],
            source.Name + " (Bản sao)",
            target.Name,
            source.Position,
            source.Status.Contains("Tiếp nhận") ? "DirectReception" : "WardAdmissionPreparation",
            actions
        );
        await ReloadTemplatesAsync();
    }

    private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplatesGrid.SelectedItem is not TemplateRow template)
        {
            MessageBox.Show("Chọn mẫu đề để xóa.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"Bạn có chắc chắn muốn xóa mẫu đề '{template.Name}'?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await _database.DeleteTemplateAsync(template.Id);
        await ReloadTemplatesAsync();
        TemplateActionsText.Text = "Đã xóa mẫu đề thành công.";
    }

    private async void TemplateSelected(object sender, SelectionChangedEventArgs e)
    {
        if (TemplatesGrid.SelectedItem is not TemplateRow template) return;
        TemplateDetailTitle.Text = $"{template.Name} ({template.Department})";
        var actions = await _database.GetTemplateActionDetailsAsync(template.Id);
        if (actions.Count == 0)
        {
            TemplateActionsText.Text = "Mẫu đề chưa có danh sách câu hỏi.";
            return;
        }

        var lines = actions.Select((x, i) => $"Câu {i + 1} ({x.Score:0.0} điểm): [{x.Code}] {x.Name}");
        TemplateActionsText.Text = string.Join("\n\n", lines);
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

        TemplatesGrid.ItemsSource = null;
        TemplatesGrid.ItemsSource = _templates;
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

    private async void ReloadHistory_Click(object sender, RoutedEventArgs e) => await ReloadHistoryDataAsync();
    private async void HistorySearchChanged(object sender, TextChangedEventArgs e) => await ReloadHistoryDataAsync();

    private void OpenHistoryFile_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row || !File.Exists(row.FilePath))
        {
            MessageBox.Show("Chọn một tệp ZIP hợp lệ trong danh sách để mở.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{row.FilePath}\"") { UseShellExecute = true });
    }

    private async Task ReloadHistoryDataAsync()
    {
        var history = await _database.GetGeneratedFilesAsync();
        var filter = HistorySearchTextBox.Text.Trim();
        HistoryGrid.ItemsSource = history
            .Where(x => string.IsNullOrEmpty(filter) || x.BatchName.Contains(filter, StringComparison.OrdinalIgnoreCase) || x.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(x => new HistoryRow(x.BatchName, x.ExamDate, x.FilePath, x.CreatedAt))
            .ToList();
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
        CardDeptCount.Text = (summary.GetValueOrDefault("Departments", 6)).ToString();
        CardWhCount.Text = (summary.GetValueOrDefault("Warehouses", 6)).ToString();
        CardDrugCount.Text = (summary.GetValueOrDefault("Drugs", 16)).ToString();
        CardUserCount.Text = (summary.GetValueOrDefault("Users", 16)).ToString();
    }

    private static string Sanitize(string value) =>
        string.Concat((value ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

    private sealed class CandidateRow(bool selected, string name, string id, string department, string template, string userStatus)
    {
        public bool Selected { get; set; } = selected;
        public string Name { get; } = name;
        public string Id { get; } = id;
        public string Department { get; } = department;
        public string Template { get; } = template;
        public string UserStatus { get; } = userStatus;
    }

    private sealed record TemplateRow(string Id, string Name, string Department, string Position, int QuestionCount, double TotalScore, string Status);
    private sealed record DepartmentRow(string Name, string Code);
    private sealed record WarehouseRow(string Name, string Code);
    private sealed record ServiceGroupRow(string Name, string Code);
    private sealed record CatalogRow(string Catalog, string Status, string Count);
    private sealed record HistoryRow(string Batch, string ExamDate, string FilePath, string CreatedAt);
}
