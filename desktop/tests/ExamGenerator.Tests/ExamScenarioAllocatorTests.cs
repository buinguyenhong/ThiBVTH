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

        // 5. Synced Patients (3 BHYT + 3 Viện phí)
        await _database.ReplaceSnapshotPatientsAsync(new[]
        {
            new SnapshotPatientRow("p1", "26001", "Bệnh nhân Một", "1990-01-01", "Nam", "Buôn Ma Thuột", "GD4662321111111", "Viêm phổi", "BHYT"),
            new SnapshotPatientRow("p2", "26002", "Bệnh nhân Hai", "1985-05-15", "Nữ", "Buôn Ma Thuột", "GD4662322222222", "Viêm dạ dày", "BHYT"),
            new SnapshotPatientRow("p3", "26003", "Bệnh nhân Ba", "2022-03-10", "Nam", "Buôn Ma Thuột", "TE1662323333333", "Viêm phế quản", "BHYT"),
            new SnapshotPatientRow("p4", "26004", "Bệnh nhân Bốn", "1992-06-20", "Nữ", "Buôn Ma Thuột", null, "Sốt xuất huyết", "Viện phí"),
            new SnapshotPatientRow("p5", "26005", "Bệnh nhân Năm", "1988-11-12", "Nam", "Buôn Ma Thuột", null, "Chấn thương phần mềm", "Viện phí"),
            new SnapshotPatientRow("p6", "26006", "Bệnh nhân Sáu", "1975-04-05", "Nam", "Buôn Ma Thuột", null, "Tăng huyết áp", "Viện phí")
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

        // 1. Test Scenario details
        Assert.Equal(30, scenario.ExamDurationMinutes);
        Assert.NotNull(scenario.SecondPatient);
        Assert.NotEqual(scenario.Patient.MedicalCode, scenario.SecondPatient.MedicalCode);
        Assert.Equal(new DateOnly(2026, 9, 5), scenario.CutoffDate); // 10 days before exam date 2026-09-15

        // Verify Question 1 has both patients & dotted fill-in lines
        var q1 = scenario.Questions.First(q => q.OrderIndex == 1);
        Assert.Contains("SVV Bn VP", q1.Instruction);
        Assert.Contains("SVV Bn BHYT", q1.Instruction);
        Assert.Contains("……………", q1.Instruction);

        // Verify Question 7 & 8 specify cutoff date 10 days prior
        var q7 = scenario.Questions.First(q => q.OrderIndex == 7);
        Assert.Contains("05/09/2026", q7.Instruction);
        Assert.Contains("Số vào viện của BN: ……………", q7.Instruction);

        var q8 = scenario.Questions.First(q => q.OrderIndex == 8);
        Assert.Contains("05/09/2026", q8.Instruction);
        Assert.Contains("Số vào viện của BN: ……………", q8.Instruction);

        // Verify Question 3 ordered drugs have enough stock
        var warehouseDrugs = await _database.GetWarehouseDrugsAsync("KHO-NOI");
        var drugStockMap = warehouseDrugs.ToDictionary(d => d.DrugCode, d => d.QuantityOnHand);
        foreach (var drug in scenario.OrderedDrugs)
        {
            Assert.True(drugStockMap.ContainsKey(drug.Code));
            Assert.True(drugStockMap[drug.Code] >= drug.Quantity, $"Thuốc {drug.Name} phải đủ tồn kho trong kho thi ({drugStockMap[drug.Code]} >= {drug.Quantity})");
        }

        // 2. Test SQL generation
        var sqlGen = new SqlScriptGenerator();
        var sql = sqlGen.Generate(scenario);
        Assert.Contains("Phan Thu Hà", sql);
        Assert.Contains("SERVER THI", sql);
        Assert.Contains("66232", sql);
        Assert.Contains(scenario.SecondPatient.FullName, sql);

        // 3. Test Word generation
        var wordGen = new WordExamWriter();
        var docxPath = Path.Combine(_tempDir, "Test_Exam.docx");
        wordGen.Create(docxPath, scenario);

        Assert.True(File.Exists(docxPath));
        var fileInfo = new FileInfo(docxPath);
        Assert.True(fileInfo.Length > 2000, "File DOCX phải có kích thước hợp lệ và đầy đủ nội dung bảng.");

        var sampleOutDir = Path.Combine("d:\\Project\\ThiBVTH\\ExamGenerator\\desktop\\publish");
        if (Directory.Exists(sampleOutDir))
        {
            try
            {
                File.Copy(docxPath, Path.Combine(sampleOutDir, "Sample_Exam_Generated.docx"), true);
            }
            catch
            {
                // Ignore lock if file is open in Microsoft Word
            }
        }

        // Verify Word XML content contains exact legacy headers and tables
        using var wordDoc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(docxPath, false);
        var xmlContent = wordDoc.MainDocumentPart!.Document.InnerXml;
        Assert.Contains("Điểm:", xmlContent);
        Assert.Contains("Chữ ký của cán bộ chấm thi:", xmlContent);
        Assert.Contains("Chữ ký của cán bộ coi thi:", xmlContent);
        Assert.Contains("BÀI THI VI TÍNH", xmlContent);
        Assert.Contains("Thời gian làm bài  :  30 phút", xmlContent);
        Assert.Contains("MaDuoc", xmlContent);
    }

    [Fact]
    public async Task Supports_custom_exam_duration_and_renders_to_word()
    {
        await SetupSyncedCatalogAsync();

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Trần Lễ Tân", "SBD15", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)", DurationMinutes: 15)
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_15Phut", new DateOnly(2026, 9, 15), candidates);
        var scenario = scenarios.Single();
        Assert.Equal(15, scenario.ExamDurationMinutes);

        var wordGen = new WordExamWriter();
        var docxPath = Path.Combine(_tempDir, "Test_Exam_15p.docx");
        wordGen.Create(docxPath, scenario);

        using var wordDoc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(docxPath, false);
        var xmlContent = wordDoc.MainDocumentPart!.Document.InnerXml;
        Assert.Contains("Thời gian làm bài  :  15 phút", xmlContent);
    }

    [Fact]
    public async Task Categorizes_clinical_services_by_modality_and_renders_structured_word_document()
    {
        await SetupSyncedCatalogAsync();

        // Seed diverse modalities
        await _database.ReplaceCatalogAsync("Services", new (string Id, string Code, string Name, string? ParentId)[]
        {
            ("s1", "SA_TIM", "Siêu âm Doppler tim", "Siêu âm"),
            ("s2", "SA_BUNG", "Siêu âm ổ bụng tổng quát", "Siêu âm"),
            ("s3", "XQ_NGUC", "Chụp Xquang ngực thẳng [Số hóa 1 phim]", "X-quang"),
            ("s4", "XQ_COCHAN", "Chụp Xquang xương cổ chân [Số hóa 2 phim]", "X-quang"),
            ("s5", "CT_NGUC", "Chụp cắt lớp vi tính lồng ngực", "CT"),
            ("s6", "XN_MAU", "Tổng phân tích tế bào máu ngoại vi", "Xét nghiệm"),
            ("s7", "XN_GLU", "Định lượng Glucose [Máu]", "Xét nghiệm"),
            ("s8", "XN_URE", "Định lượng Ure [Máu]", "Xét nghiệm"),
            ("s9", "XN_DIEN_GIAI", "Điện giải đồ (Na, K, Cl) [Máu]", "Xét nghiệm")
        });

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Vũ Thị Mai", "SBD88", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_Modalities", new DateOnly(2026, 9, 15), candidates);
        var scenario = scenarios.Single();

        // Question 2 should have structured sections
        var q2 = scenario.Questions.First(q => q.OrderIndex == 2);
        Assert.Contains("+ Chẩn đoán hình ảnh:", q2.Instruction);
        Assert.Contains("+ Cắt lớp vi tính (CT) / MRI:", q2.Instruction);
        Assert.Contains("+ Các dịch vụ xét nghiệm:", q2.Instruction);

        // Word output verification
        var wordGen = new WordExamWriter();
        var docxPath = Path.Combine(_tempDir, "Test_Exam_Structured.docx");
        wordGen.Create(docxPath, scenario);

        using var wordDoc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(docxPath, false);
        var xmlContent = wordDoc.MainDocumentPart!.Document.InnerXml;
        Assert.Contains("Times New Roman", xmlContent);
        Assert.Contains("Chẩn đoán hình ảnh:", xmlContent);
        Assert.Contains("Cắt lớp vi tính (CT) / MRI:", xmlContent);
        Assert.Contains("Các dịch vụ xét nghiệm:", xmlContent);
    }

    [Fact]
    public async Task Merges_all_candidate_exams_into_single_printable_word_document()
    {
        await SetupSyncedCatalogAsync();

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Nguyễn Thị Hoa", "SBD01", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)"),
            new CandidateInput("Trần Văn Nam", "SBD02", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_Merged", new DateOnly(2026, 9, 15), candidates);
        Assert.Equal(2, scenarios.Count);

        var wordGen = new WordExamWriter();
        var mergedDocxPath = Path.Combine(_tempDir, "_InGop_TatCaDeThi_Test.docx");
        wordGen.CreateMerged(mergedDocxPath, scenarios);

        Assert.True(File.Exists(mergedDocxPath));

        using var wordDoc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(mergedDocxPath, false);
        var xmlContent = wordDoc.MainDocumentPart!.Document.InnerXml;

        // Both candidates present in single file
        Assert.Contains("Nguyễn Thị Hoa", xmlContent);
        Assert.Contains("Trần Văn Nam", xmlContent);
        // Page break present separating the candidates
        Assert.Contains("type=\"page\"", xmlContent);
        // Unified font
        Assert.Contains("Times New Roman", xmlContent);
    }

    [Fact]
    public async Task Multiple_candidates_strictly_respect_cumulative_warehouse_stock_and_bh_vp_slots()
    {
        await SetupSyncedCatalogAsync();

        // Seed 4 BHYT drugs and 3 VP supplies with specific stock limits
        await _database.ReplaceSnapshotDrugsAsync(new[]
        {
            new SnapshotDrugRow("bh1", "BH_DRUG_1", "Thuốc BHYT 1", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 1), // Only 1 in stock!
            new SnapshotDrugRow("bh2", "BH_DRUG_2", "Thuốc BHYT 2", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 2), // Only 2 in stock!
            new SnapshotDrugRow("bh3", "BH_DRUG_3", "Thuốc BHYT 3", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 100),
            new SnapshotDrugRow("bh4", "BH_DRUG_4", "Thuốc BHYT 4", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 100),
            new SnapshotDrugRow("vp1", "VP_SUPP_1", "Bơm tiêm 10ml", "Cái", "KHO-NOI", "Kho trực Nội", "Viện phí", 2), // Only 2 in stock!
            new SnapshotDrugRow("vp2", "VP_SUPP_2", "Kim tiêm G18", "Cái", "KHO-NOI", "Kho trực Nội", "Viện phí", 100),
            new SnapshotDrugRow("vp3", "VP_SUPP_3", "Dây truyền dịch", "Bộ", "KHO-NOI", "Kho trực Nội", "Viện phí", 100)
        });

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Thí sinh A", "SBD01", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)"),
            new CandidateInput("Thí sinh B", "SBD02", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_StockCheck", new DateOnly(2026, 9, 15), candidates);
        Assert.Equal(2, scenarios.Count);

        var originalStocks = new Dictionary<string, double>
        {
            ["BH_DRUG_1"] = 1,
            ["BH_DRUG_2"] = 2,
            ["BH_DRUG_3"] = 100,
            ["BH_DRUG_4"] = 100,
            ["VP_SUPP_1"] = 2,
            ["VP_SUPP_2"] = 100,
            ["VP_SUPP_3"] = 100
        };

        var totalAllocated = new Dictionary<string, int>();

        foreach (var scenario in scenarios)
        {
            Assert.Equal(5, scenario.OrderedDrugs.Count);

            // First 3 items should be BHYT
            Assert.Equal("BHYT", scenario.OrderedDrugs[0].FundingSource);
            Assert.Equal("BHYT", scenario.OrderedDrugs[1].FundingSource);
            Assert.Equal("BHYT", scenario.OrderedDrugs[2].FundingSource);

            // Last 2 items should be Viện phí
            Assert.Equal("Viện phí", scenario.OrderedDrugs[3].FundingSource);
            Assert.Equal("Viện phí", scenario.OrderedDrugs[4].FundingSource);

            foreach (var d in scenario.OrderedDrugs)
            {
                totalAllocated[d.Code] = totalAllocated.GetValueOrDefault(d.Code) + d.Quantity;
            }
        }

        // Verify total allocated quantity across all candidates NEVER exceeds warehouse stock
        foreach (var (code, qty) in totalAllocated)
        {
            var maxStock = originalStocks[code];
            Assert.True(qty <= maxStock, $"Dược '{code}' bị xuất quá số lượng tồn kho! (Đã xuất: {qty}, Tồn kho: {maxStock})");
        }
    }
}
