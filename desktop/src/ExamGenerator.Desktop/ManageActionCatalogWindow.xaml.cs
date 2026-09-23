using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ExamGenerator.Domain;
using ExamGenerator.Infrastructure;

namespace ExamGenerator.Desktop;

public partial class ManageActionCatalogWindow : Window
{
    private readonly SqliteDatabase _database;
    public string? LastCreatedOrSelectedActionCode { get; private set; }

    public ManageActionCatalogWindow(SqliteDatabase database)
    {
        InitializeComponent();
        _database = database;
        Loaded += async (s, e) => await LoadActionsAsync();
    }

    private async Task LoadActionsAsync()
    {
        try
        {
            var actions = await _database.GetActionDefinitionsAsync();
            ActionsGrid.ItemsSource = actions;
            TotalActionsCountBadge.Text = $"{actions.Count} dạng câu hỏi";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi tải thư viện nghiệp vụ: {ex.Message}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveAction_Click(object sender, RoutedEventArgs e)
    {
        var rawCode = ActionCodeTextBox.Text?.Trim().ToUpperInvariant() ?? "";
        // Tự động thay khoảng trắng hoặc ký tự đặc biệt thành dấu gạch dưới
        var code = System.Text.RegularExpressions.Regex.Replace(rawCode, @"\s+", "_");

        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show("Vui lòng nhập Mã hành động (viết hoa không dấu, ví dụ: XN_HUYET_HOC).", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            ActionCodeTextBox.Focus();
            return;
        }

        var name = ActionNameTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Vui lòng nhập Tên dạng câu hỏi / thao tác nghiệp vụ.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            ActionNameTextBox.Focus();
            return;
        }

        var scoreText = DefaultScoreTextBox.Text?.Trim().Replace(',', '.') ?? "1.0";
        if (!double.TryParse(scoreText, NumberStyles.Any, CultureInfo.InvariantCulture, out var score) || score <= 0)
        {
            MessageBox.Show("Số điểm mẫu phải là một số lớn hơn 0 (ví dụ: 1.0 hoặc 2.5).", "Điểm số không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            DefaultScoreTextBox.Focus();
            return;
        }

        var category = CategoryComboBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "Nghiệp vụ chung";
        }

        var description = DescriptionTextBox.Text?.Trim() ?? "";

        try
        {
            var newDef = new SystemActionDefinition(code, name, score, category, description, IsBuiltIn: false);
            await _database.SaveActionDefinitionAsync(newDef);

            LastCreatedOrSelectedActionCode = code;

            MessageBox.Show($"Đã lưu dạng câu hỏi nghiệp vụ [{code}] '{name}' vào thư viện thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);

            // Reset form
            ActionCodeTextBox.Text = "";
            ActionNameTextBox.Text = "";
            DefaultScoreTextBox.Text = "1.0";
            DescriptionTextBox.Text = "";

            await LoadActionsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi lưu dạng câu hỏi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SystemActionDefinition action)
        {
            if (action.IsBuiltIn)
            {
                MessageBox.Show("Không thể xóa dạng câu hỏi mặc định của hệ thống.", "Bảo vệ hệ thống", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa dạng câu hỏi [{action.Code}] '{action.Name}' khỏi Thư viện Nghiệp vụ HIS?",
                "Xác nhận xóa câu hỏi",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    await _database.DeleteActionDefinitionAsync(action.Code);
                    await LoadActionsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi xóa dạng câu hỏi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
