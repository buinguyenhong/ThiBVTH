using System.Windows;

namespace ExamGenerator.Desktop;

public partial class CloneTemplateWindow : Window
{
    public string NewName { get; private set; } = string.Empty;
    public string NewDepartment { get; private set; } = string.Empty;
    public string NewPosition { get; private set; } = string.Empty;
    public bool IsDirectReception { get; private set; }

    public CloneTemplateWindow(
        string sourceName,
        string sourceDepartment,
        string sourcePosition,
        bool isDirectReception,
        IReadOnlyList<string> departments)
    {
        InitializeComponent();

        SourceInfoText.Text = $"Đề gốc: {sourceName} (Khoa: {sourceDepartment} · Vị trí: {sourcePosition})";
        NewNameTextBox.Text = $"{sourceName} (Bản sao)";

        DepartmentComboBox.ItemsSource = departments;
        if (departments.Contains(sourceDepartment))
        {
            DepartmentComboBox.SelectedItem = sourceDepartment;
        }
        else if (departments.Count > 0)
        {
            DepartmentComboBox.SelectedIndex = 0;
        }

        PositionComboBox.ItemsSource = new[]
        {
            "Điều dưỡng",
            "Bác sĩ",
            "Lễ tân",
            "Kỹ thuật viên",
            "Thu ngân",
            "Dược sĩ",
            "Hành chính"
        };
        PositionComboBox.Text = sourcePosition;

        ReceptionModeComboBox.ItemsSource = new[]
        {
            "Tiếp nhận trực tiếp (DirectReception)",
            "Hàng chờ nhận khoa (WardAdmissionPreparation)"
        };
        ReceptionModeComboBox.SelectedIndex = isDirectReception ? 0 : 1;

        NewNameTextBox.Focus();
        NewNameTextBox.SelectAll();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var name = NewNameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Vui lòng nhập tên cho mẫu đề mới.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            NewNameTextBox.Focus();
            return;
        }

        var dept = DepartmentComboBox.SelectedItem as string ?? DepartmentComboBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(dept))
        {
            MessageBox.Show("Vui lòng chọn Khoa / Phòng áp dụng cho mẫu đề mới.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            DepartmentComboBox.Focus();
            return;
        }

        NewName = name;
        NewDepartment = dept;
        NewPosition = string.IsNullOrWhiteSpace(PositionComboBox.Text) ? "Điều dưỡng" : PositionComboBox.Text.Trim();
        IsDirectReception = ReceptionModeComboBox.SelectedIndex == 0;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
