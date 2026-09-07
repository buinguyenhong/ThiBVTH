using System.IO;
using ExamGenerator.Domain;
using ExamGenerator.Infrastructure;
using Xunit;

namespace ExamGenerator.Tests;

public class ExamScenarioAllocatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalApplicationStorage _storage;
    private readonly SqliteDatabase _database;

    public ExamScenarioAllocatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExamTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new LocalApplicationStorage(_tempDir);
        _database = new SqliteDatabase(_storage);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors
        }
    }

    [Fact]
    public async Task Allocates_distinct_his_users_for_candidates_in_same_department()
    {
        await _database.InitializeAsync();
        await _database.EnsureDefaultTemplatesAsync();

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Nguyễn Văn A", "SBD01", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)"),
            new CandidateInput("Trần Thị B", "SBD02", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)"),
            new CandidateInput("Lê Văn C", "SBD03", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_Test", new DateOnly(2026, 9, 15), candidates);

        Assert.Equal(3, scenarios.Count);
        var users = scenarios.Select(s => s.HisUserCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(3, users.Count); // Rule 3.2: Tất cả thí sinh phải có User HIS khác nhau
    }

    [Fact]
    public async Task Throws_fail_fast_when_candidates_exceed_available_his_users()
    {
        await _database.InitializeAsync();
        await _database.EnsureDefaultTemplatesAsync();

        var allocator = new ExamScenarioAllocator(_database);
        // Khoa Nhi in default seed has 2 users: u_nhi_1 and u_nhi_2. Registering 3 candidates should trigger fail-fast preflight.
        var candidates = new[]
        {
            new CandidateInput("Thí sinh 1", "SBD01", "Khoa Nhi", "Đề Điều dưỡng Nhi (Tiếp nhận trực tiếp)"),
            new CandidateInput("Thí sinh 2", "SBD02", "Khoa Nhi", "Đề Điều dưỡng Nhi (Tiếp nhận trực tiếp)"),
            new CandidateInput("Thí sinh 3", "SBD03", "Khoa Nhi", "Đề Điều dưỡng Nhi (Tiếp nhận trực tiếp)")
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            allocator.AllocateBatchScenariosAsync("DotThi_Test", new DateOnly(2026, 9, 15), candidates));

        Assert.Contains("tài khoản HIS khả dụng", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generates_valid_sql_and_word_docx_for_scenario()
    {
        await _database.InitializeAsync();
        await _database.EnsureDefaultTemplatesAsync();

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Phan Thu Hà", "SBD99", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_DocxTest", new DateOnly(2026, 9, 15), candidates);
        var scenario = scenarios.Single();

        // 1. Test SQL generation
        var sqlGen = new SqlScriptGenerator();
        var sql = sqlGen.Generate(scenario);
        Assert.Contains("Phan Thu Hà", sql);
        Assert.Contains("SERVER THI", sql);
        Assert.Contains("66232", sql);

        // 2. Test Word generation
        var wordGen = new WordExamWriter();
        var docxPath = Path.Combine(_tempDir, "Test_Exam.docx");
        wordGen.Create(docxPath, scenario);

        Assert.True(File.Exists(docxPath));
        var fileInfo = new FileInfo(docxPath);
        Assert.True(fileInfo.Length > 2000, "File DOCX phải có kích thước hợp lệ và đầy đủ nội dung bảng.");
    }
}
