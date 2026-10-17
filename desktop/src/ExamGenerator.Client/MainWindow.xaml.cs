using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ExamGenerator.Domain;
using ExamGenerator.Infrastructure;
using Microsoft.Win32;

namespace ExamGenerator.Client;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer;
    private ClientExamPackage? _currentPackage;
    private DateTime _startedAt;
    private DateTime _submittedAt;
    private int _remainingSeconds;
    private int _totalExtraMinutes;
    private bool _isExamActive;
    private readonly List<ExtraTimeIncidentLog> _incidentLogs = new();
    private readonly ObservableCollection<QuestionItemViewModel> _questionViewModels = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsExamActive
    {
        get => _isExamActive;
        set
        {
            if (_isExamActive != value)
            {
                _isExamActive = value;
                OnPropertyChanged();
            }
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        QuestionsItemsControl.ItemsSource = _questionViewModels;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += Timer_Tick;

        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ScanAndLoadExamPackages();
    }

    private void ScanAndLoadExamPackages()
    {
        var candidates = new List<CandidatePackageItem>();
        var searchDirs = new List<string>
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exams"),
            AppDomain.CurrentDomain.BaseDirectory,
            Path.Combine(Directory.GetCurrentDirectory(), "exams"),
            Directory.GetCurrentDirectory()
        };

        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.GetFiles(dir, "*.exam_pkg"))
            {
                if (!seenFiles.Add(file)) continue;

                try
                {
                    var pkg = ExamPackageService.LoadPackage(file);
                    candidates.Add(new CandidatePackageItem(file, pkg));
                }
                catch
                {
                    // Ignore corrupted or incompatible file
                }
            }
        }

        CandidatePackageComboBox.ItemsSource = candidates;
        if (candidates.Count > 0)
        {
            CandidatePackageComboBox.SelectedIndex = 0;
        }
    }

    private void BrowseExamPackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn file đề thi cho máy trạm",
            Filter = "Gói đề thi ExamClient (*.exam_pkg)|*.exam_pkg|Tất cả tệp (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var pkg = ExamPackageService.LoadPackage(dialog.FileName);
                var item = new CandidatePackageItem(dialog.FileName, pkg);

                var list = CandidatePackageComboBox.ItemsSource as List<CandidatePackageItem> ?? new List<CandidatePackageItem>();
                var newList = new List<CandidatePackageItem>(list);
                if (!newList.Any(x => string.Equals(x.FilePath, dialog.FileName, StringComparison.OrdinalIgnoreCase)))
                {
                    newList.Insert(0, item);
                }
                CandidatePackageComboBox.ItemsSource = newList;
                CandidatePackageComboBox.SelectedItem = item;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể nạp file đề thi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void CandidatePackageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CandidatePackageComboBox.SelectedItem is CandidatePackageItem item)
        {
            _currentPackage = item.Package;
            PackageInfoBorder.Visibility = Visibility.Visible;
            StartExamButton.IsEnabled = true;

            InfoCandidateIdTextBlock.Text = string.IsNullOrWhiteSpace(_currentPackage.CandidateId) ? "(Chưa có SBD)" : _currentPackage.CandidateId;
            InfoCandidateNameTextBlock.Text = _currentPackage.CandidateName;
            InfoDepartmentTextBlock.Text = _currentPackage.DepartmentName;
            InfoDurationTextBlock.Text = $"{_currentPackage.DurationMinutes} phút";
            InfoHisUserTextBlock.Text = $"{_currentPackage.HisUserCode} (Mật khẩu: 123)";
        }
        else
        {
            _currentPackage = null;
            PackageInfoBorder.Visibility = Visibility.Collapsed;
            StartExamButton.IsEnabled = false;
        }
    }

    private void StartExamButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPackage is null) return;

        var confirm = MessageBox.Show(
            $"Bạn chuẩn bị bắt đầu làm bài thi cho thí sinh:\n\n" +
            $"• SBD: {_currentPackage.CandidateId}\n" +
            $"• Họ tên: {_currentPackage.CandidateName}\n" +
            $"• Khoa: {_currentPackage.DepartmentName}\n" +
            $"• Thời gian làm bài: {_currentPackage.DurationMinutes} phút\n\n" +
            "Khi bấm 'Đồng ý', đồng hồ đếm ngược sẽ bắt đầu chạy.",
            "Xác nhận vào thi",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        // Populate Workstation Info
        _startedAt = DateTime.Now;
        _remainingSeconds = _currentPackage.DurationMinutes * 60;
        _totalExtraMinutes = 0;
        _incidentLogs.Clear();

        HeaderCandidateIdTextBlock.Text = $"SBD: {_currentPackage.CandidateId}";
        HeaderCandidateNameTextBlock.Text = _currentPackage.CandidateName;
        HeaderDepartmentTextBlock.Text = $"Khoa: {_currentPackage.DepartmentName}";
        HeaderHisUserTextBlock.Text = $"User HIS: {_currentPackage.HisUserCode} (PW: 123)";

        // Build Questions
        _questionViewModels.Clear();
        foreach (var q in _currentPackage.Questions.OrderBy(x => x.OrderIndex))
        {
            _questionViewModels.Add(new QuestionItemViewModel
            {
                OrderIndex = q.OrderIndex,
                ActionCode = q.ActionCode,
                HeaderTitle = $"Câu {q.OrderIndex}: {q.Title}",
                ScoreText = $"{q.MaxScore:0.#} điểm",
                Instruction = q.Instruction,
                DetailText = q.DetailText,
                DetailVisibility = string.IsNullOrWhiteSpace(q.DetailText) ? Visibility.Collapsed : Visibility.Visible,
                FillInPrompt = q.FillInPrompt ?? "Nhập kết quả:",
                FillInVisibility = q.IsFillIn ? Visibility.Visible : Visibility.Collapsed,
                PracticalHisVisibility = !q.IsFillIn ? Visibility.Visible : Visibility.Collapsed,
                CandidateAnswer = string.Empty
            });
        }

        IsExamActive = true;
        TimeoutWarningBanner.Visibility = Visibility.Collapsed;
        ExtraTimeBadge.Visibility = Visibility.Collapsed;

        UpdateTimerDisplay();
        _timer.Start();

        LoginPanel.Visibility = Visibility.Collapsed;
        WorkstationPanel.Visibility = Visibility.Visible;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_remainingSeconds > 0)
        {
            _remainingSeconds--;
            UpdateTimerDisplay();

            if (_remainingSeconds == 0)
            {
                OnExamTimeExpired();
            }
        }
    }

    private void UpdateTimerDisplay()
    {
        var ts = TimeSpan.FromSeconds(_remainingSeconds);
        CountdownTimerTextBlock.Text = ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";

        if (_remainingSeconds > 300)
        {
            CountdownTimerTextBlock.Foreground = (Brush)FindResource("SuccessGreen");
        }
        else if (_remainingSeconds > 60)
        {
            CountdownTimerTextBlock.Foreground = (Brush)FindResource("WarningAmber");
        }
        else
        {
            CountdownTimerTextBlock.Foreground = (Brush)FindResource("DangerRed");
        }
    }

    private void OnExamTimeExpired()
    {
        IsExamActive = false;
        TimeoutWarningBanner.Visibility = Visibility.Visible;
        CountdownTimerTextBlock.Text = "00:00";
        CountdownTimerTextBlock.Foreground = (Brush)FindResource("DangerRed");
    }

    private void AddExtraTimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPackage is null) return;

        var dialog = new ExtraTimeDialog(_currentPackage.SupervisorPasswordHash)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            var addedMin = dialog.GrantedExtraMinutes;
            var note = dialog.SupervisorNote;

            _totalExtraMinutes += addedMin;
            _remainingSeconds += addedMin * 60;

            _incidentLogs.Add(new ExtraTimeIncidentLog
            {
                GrantedAt = DateTime.Now,
                ExtraMinutes = addedMin,
                SupervisorNote = note
            });

            // Unlock workstation
            IsExamActive = true;
            TimeoutWarningBanner.Visibility = Visibility.Collapsed;

            ExtraTimeBadgeText.Text = $"+{_totalExtraMinutes}p bù giờ";
            ExtraTimeBadge.Visibility = Visibility.Visible;

            UpdateTimerDisplay();
            if (!_timer.IsEnabled) _timer.Start();

            MessageBox.Show(
                $"Đã cộng thêm {addedMin} phút làm bài do sự cố thành công!\n\n" +
                $"• Tổng thời gian bù giờ: {_totalExtraMinutes} phút\n" +
                $"• Ghi chú: {note}\n\n" +
                "Bài làm đã được mở khóa để thí sinh tiếp tục thao tác.",
                "Cộng giờ sự cố thành công",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void SubmitExamButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPackage is null) return;

        var confirm = MessageBox.Show(
            "Bạn có chắc chắn muốn NỘP BÀI THI ngay bây giờ?\n\n" +
            "Sau khi nộp, kết quả bài làm và các vết thao tác trên HIS sẽ được đóng gói gửi về Hội đồng chấm thi và không thể sửa đổi.",
            "Xác nhận nộp bài thi",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        _timer.Stop();
        _submittedAt = DateTime.Now;
        IsExamActive = false;

        var submission = new ClientSubmissionPackage
        {
            SubmissionId = Guid.NewGuid().ToString("N"),
            ExamId = _currentPackage.ExamId,
            CandidateId = _currentPackage.CandidateId,
            CandidateName = _currentPackage.CandidateName,
            PaperCode = _currentPackage.PaperCode,
            HisUserCode = _currentPackage.HisUserCode,
            DepartmentName = _currentPackage.DepartmentName,
            ExamDate = _currentPackage.ExamDate,
            StartedAt = _startedAt,
            SubmittedAt = _submittedAt,
            OriginalDurationMinutes = _currentPackage.DurationMinutes,
            TotalExtraMinutes = _totalExtraMinutes,
            ExtraTimeIncidents = new List<ExtraTimeIncidentLog>(_incidentLogs),
            Answers = _questionViewModels.Select(q => new ClientQuestionAnswer
            {
                OrderIndex = q.OrderIndex,
                ActionCode = q.ActionCode,
                FillInValue = q.CandidateAnswer?.Trim(),
                AnsweredAt = DateTime.Now
            }).ToList(),
            MachineName = Environment.MachineName,
            Status = "Submitted"
        };

        // Save submission locally in ./submissions/
        var localDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "submissions");
        var safeCandidate = string.IsNullOrWhiteSpace(submission.CandidateId) ? "NO_SBD" : submission.CandidateId.Trim();
        var fileName = $"{safeCandidate}_{submission.PaperCode}_{DateTime.Now:yyyyMMdd_HHmmss}.sub_pkg";
        var localPath = Path.Combine(localDir, fileName);

        try
        {
            ExamPackageService.SaveSubmission(submission, localPath);

            // Also copy to parent shared submissions folder if exists
            var sharedDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "submissions");
            if (Directory.Exists(sharedDir))
            {
                var sharedPath = Path.Combine(sharedDir, fileName);
                File.Copy(localPath, sharedPath, true);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cảnh báo lưu file: {ex.Message}", "Lỗi lưu file", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Show Summary
        SummaryCandidateTextBlock.Text = $"{submission.CandidateName} (SBD: {submission.CandidateId})";
        SummaryStartedAtTextBlock.Text = _startedAt.ToString("HH:mm:ss dd/MM/yyyy");
        SummarySubmittedAtTextBlock.Text = _submittedAt.ToString("HH:mm:ss dd/MM/yyyy");
        SummaryExtraMinutesTextBlock.Text = _totalExtraMinutes > 0 ? $"+{_totalExtraMinutes} phút" : "Không có";
        SummarySubmissionIdTextBlock.Text = submission.SubmissionId[..12].ToUpperInvariant();

        WorkstationPanel.Visibility = Visibility.Collapsed;
        SubmittedPanel.Visibility = Visibility.Visible;
    }

    private void ExitApplication_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class CandidatePackageItem
{
    public string FilePath { get; }
    public ClientExamPackage Package { get; }
    public string DisplayText { get; }

    public CandidatePackageItem(string filePath, ClientExamPackage package)
    {
        FilePath = filePath;
        Package = package;
        var sbd = string.IsNullOrWhiteSpace(package.CandidateId) ? "Chưa có SBD" : package.CandidateId;
        DisplayText = $"[SBD: {sbd}] {package.CandidateName} - Khoa {package.DepartmentName} ({package.DurationMinutes} phút)";
    }
}

public sealed class QuestionItemViewModel : INotifyPropertyChanged
{
    private string? _candidateAnswer;

    public int OrderIndex { get; set; }
    public string ActionCode { get; set; } = string.Empty;
    public string HeaderTitle { get; set; } = string.Empty;
    public string ScoreText { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
    public string? DetailText { get; set; }
    public Visibility DetailVisibility { get; set; }
    public string FillInPrompt { get; set; } = string.Empty;
    public Visibility FillInVisibility { get; set; }
    public Visibility PracticalHisVisibility { get; set; }

    public string? CandidateAnswer
    {
        get => _candidateAnswer;
        set
        {
            if (_candidateAnswer != value)
            {
                _candidateAnswer = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
