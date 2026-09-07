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

    private async Task SetupSyncedCatalogAsync()
    {
        await _database.InitializeAsync();
        await _database.EnsureDefaultTemplatesAsync();

        // 1. Mappings
        await _database.SaveDepartmentConfigurationAsync("NOI", "Khoa Nội", "KHO-NOI", "Kho trực Nội");
        await _database.SaveDepartmentConfigurationAsync("NHI", "Khoa Nhi", "KHO-NHI", "Kho trực Nhi");

        // 2. Synced Services
        await _database.ReplaceCatalogAsync("Services", new (string Id, string Code, string Name, string? ParentId)[]
        {
            ("s1", "XQ_NGUC", "Chụp X-quang tim phổi thẳng", "X-quang"),
            ("s2", "CT_MAU", "Tổng phân tích tế bào máu", "Xét nghiệm"),
            ("s3", "SH_URE", "Định lượng Ure máu", "Xét nghiệm"),
            ("s4", "SH_CRE", "Định lượng Creatinin máu", "Xét nghiệm")
        });

        // 3. Synced Users (3 for Noi, 2 for Nhi)
        await _database.ReplaceSnapshotUsersAsync(new[]
        {
            new SnapshotUserRow("u1", "his_noi_1", "BS. Nguyễn Văn A", "NOI", "Khoa Nội"),
            new SnapshotUserRow("u2", "his_noi_2", "ĐD. Trần Thị B", "NOI", "Khoa Nội"),
            new SnapshotUserRow("u3", "his_noi_3", "ĐD. Lê Văn C", "NOI", "Khoa Nội"),
            new SnapshotUserRow("u4", "his_nhi_1", "BS. Đỗ Thị D", "NHI", "Khoa Nhi"),
            new SnapshotUserRow("u5", "his_nhi_2", "ĐD. Hoàng Văn E", "NHI", "Khoa Nhi")
        });

        // 4. Synced Drugs with stock
        await _database.ReplaceSnapshotDrugsAsync(new[]
        {
            new SnapshotDrugRow("d1", "AMOX500", "Amoxicillin 500mg", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 500),
            new SnapshotDrugRow("d2", "PARA500", "Paracetamol 500mg", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 1000),
            new SnapshotDrugRow("d3", "SALBU", "Salbutamol 2.5mg", "Ống", "KHO-NHI", "Kho trực Nhi", "BHYT", 300)
        });

        // 5. Synced Patients
        await _database.ReplaceSnapshotPatientsAsync(new[]
        {
            new SnapshotPatientRow("p1", "26001", "Bệnh nhân Một", "1990-01-01", "Nam", "Buôn Ma Thuột", "GD4662321111111", "Viêm phổi", "BHYT"),
            new SnapshotPatientRow("p2", "26002", "Bệnh nhân Hai", "1985-05-15", "Nữ", "Buôn Ma Thuột", "GD4662322222222", "Viêm dạ dày", "BHYT"),
            new SnapshotPatientRow("p3", "26003", "Bệnh nhân Ba", "2022-03-10", "Nam", "Buôn Ma Thuột", "TE1662323333333", "Viêm phế quản", "BHYT")
        });
    }

    [Fact]
    public async Task Throws_preflight_when_catalog_has_not_been_synced_from_his()
    {
        await _database.InitializeAsync();
        await _database.EnsureDefaultTemplatesAsync();

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Thí sinh 1", "SBD01", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            allocator.AllocateBatchScenariosAsync("DotThi_Unsynced", new DateOnly(2026, 9, 15), candidates));

        Assert.Contains("Đồng bộ Catalog HIS", ex.Message);
    }

    [Fact]
    public async Task Allocates_distinct_his_users_for_candidates_in_same_department()
    {
        await SetupSyncedCatalogAsync();

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
        Assert.Equal(3, users.Count); // Rule 3.2: Mỗi thí sinh có đúng 1 user HIS riêng biệt
    }

    [Fact]
    public async Task Throws_fail_fast_when_candidates_exceed_available_his_users()
    {
        await SetupSyncedCatalogAsync();

        var allocator = new ExamScenarioAllocator(_database);
        // Khoa Nhi in synced setup has only 2 users (u4, u5). Registering 3 candidates must fail fast.
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
        await SetupSyncedCatalogAsync();

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
