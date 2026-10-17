using System.Windows;
using System.Windows.Controls;
using ExamGenerator.Infrastructure;

namespace ExamGenerator.Client;

public partial class ExtraTimeDialog : Window
{
    private readonly string _supervisorPasswordHash;

    public int GrantedExtraMinutes { get; private set; }
    public string SupervisorNote { get; private set; } = string.Empty;

    public ExtraTimeDialog(string supervisorPasswordHash)
    {
        InitializeComponent();
        _supervisorPasswordHash = supervisorPasswordHash;
        SupervisorPasswordBox.Focus();
    }

    private void QuickMinute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string minStr)
        {
            ExtraMinutesTextBox.Text = minStr;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        ErrorMessageTextBlock.Visibility = Visibility.Collapsed;

        var pwd = SupervisorPasswordBox.Password?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(pwd))
        {
            ShowError("Vui lòng nhập mật khẩu Giám thị để xác thực.");
            SupervisorPasswordBox.Focus();
            return;
        }

        if (!ExamPackageService.VerifySupervisorPassword(pwd, _supervisorPasswordHash))
        {
            ShowError("Mật khẩu Giám thị không đúng. Vui lòng kiểm tra lại.");
            SupervisorPasswordBox.SelectAll();
            SupervisorPasswordBox.Focus();
            return;
        }

        if (!int.TryParse(ExtraMinutesTextBox.Text.Trim(), out var minutes) || minutes <= 0 || minutes > 120)
        {
            ShowError("Số phút bù thêm phải là số nguyên dương hợp lệ từ 1 đến 120 phút.");
            ExtraMinutesTextBox.Focus();
            return;
        }

        GrantedExtraMinutes = minutes;
        SupervisorNote = IncidentReasonTextBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string msg)
    {
        ErrorMessageTextBlock.Text = msg;
        ErrorMessageTextBlock.Visibility = Visibility.Visible;
    }
}
