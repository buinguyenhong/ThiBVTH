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

        await _database.SaveDepartmentUserMappingsAsync("NOI", "Khoa Nội", new[]
        {
            ("u1", "his_noi_1", "BS. Nguyễn Văn A"),
            ("u2", "his_noi_2", "ĐD. Trần Thị B"),
            ("u3", "his_noi_3", "ĐD. Lê Văn C")
        });
        await _database.SaveDepartmentUserMappingsAsync("NHI", "Khoa Nhi", new[]
        {
            ("u4", "his_nhi_1", "BS. Đỗ Thị D"),
            ("u5", "his_nhi_2", "ĐD. Hoàng Văn E")
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

        // 6. Synced Inpatient cases (for advance payment, transfer, discharge...)
        await _database.ReplaceSnapshotInpatientsAsync(new[]
        {
            new SnapshotInpatientRow("ba_noi_1", "25.001001", "25001001", "Trần Văn Nội Trú 1", "Nam", "1980", "Buôn Ma Thuột", "NOI", "Khoa Nội", "P101", "Phòng 101", "G01", "Tầng 1", "2026-01-10", "2026-01-10 08:00:00", "2026-01-10 08:30:00", "2026-01-10", 5, "GD4662321111111", "Viêm phổi", "BS. Nguyễn Văn A"),
            new SnapshotInpatientRow("ba_noi_2", "25.001002", "25001002", "Lê Thị Nội Trú 2", "Nữ", "1985", "Buôn Ma Thuột", "NOI", "Khoa Nội", "P102", "Phòng 102", "G02", "Tầng 1", "2026-01-12", "2026-01-12 08:00:00", "2026-01-12 08:30:00", "2026-01-12", 3, "GD4662322222222", "Viêm dạ dày", "BS. Nguyễn Văn A"),
            new SnapshotInpatientRow("ba_noi_3", "25.001003", "25001003", "Phạm Văn Nội Trú 3", "Nam", "1989", "Buôn Ma Thuột", "NOI", "Khoa Nội", "P204", "Phòng 204", "G08", "Tầng 2", "2026-02-01", "2026-02-01 08:00:00", "2026-02-01 08:30:00", "2026-02-01", 10, null, "Viêm ruột thừa", "BS. Nguyễn Văn A"),
            new SnapshotInpatientRow("ba_noi_4", "25.001004", "25001004", "Hoàng Văn Ra Viện", "Nam", "1975", "Buôn Ma Thuột", "NOI", "Khoa Nội", "P103", "Phòng 103", "G03", "Tầng 1", "2026-01-05", "2026-01-05 08:00:00", "2026-01-05 08:30:00", "2026-01-05", 15, null, "Khỏi bệnh", "BS. Nguyễn Văn A")
        });

        // 7. Synced Outpatient cases (for lookup, outpatient case...)
        await _database.ReplaceSnapshotOutpatientsAsync(new[]
        {
            new SnapshotOutpatientRow("ba_ngoai_1", "25001726/NG", "25001726", "25001726", "Đỗ Phúc Huy", "Nam", "1973", "23 Trần Phú, Buôn Ma Thuột", "NOI", "Khoa Nội", null, null, null, "10/01/2026", "15/02/2026", "10/01/2026", 35, null, "Viêm phế quản mãn", "BS. Nguyễn Văn A"),
            new SnapshotOutpatientRow("ba_ngoai_2", "25001727/NG", "25001727", "25001727", "Nguyễn Thị Ngoại Trú", "Nữ", "1985", "Buôn Ma Thuột", "NOI", "Khoa Nội", null, null, null, "12/01/2026", "20/01/2026", "12/01/2026", 3, null, "Điều trị ngoại trú", "BS. Nguyễn Văn A")
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

        Assert.Contains("User HIS khả dụng", ex.Message, StringComparison.OrdinalIgnoreCase);
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
        Assert.DoesNotContain("TEST TIEP NHAN VAO KHOA", sql);
        Assert.DoesNotContain("TEST CHỈ ĐỊNH VÀO KHOA", sql);

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

        // Seed 5 BHYT drugs and 3 VP supplies with specific stock limits
        await _database.ReplaceSnapshotDrugsAsync(new[]
        {
            new SnapshotDrugRow("bh1", "BH_DRUG_1", "Thuốc BHYT 1", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 1), // Only 1 in stock!
            new SnapshotDrugRow("bh2", "BH_DRUG_2", "Thuốc BHYT 2", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 2), // Only 2 in stock!
            new SnapshotDrugRow("bh3", "BH_DRUG_3", "Thuốc BHYT 3", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 100),
            new SnapshotDrugRow("bh4", "BH_DRUG_4", "Thuốc BHYT 4", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 100),
            new SnapshotDrugRow("bh5", "BH_DRUG_5", "Thuốc BHYT 5", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 100),
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
            ["BH_DRUG_5"] = 100,
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

    [Fact]
    public async Task Scenario_uses_custom_action_definition_description_in_questions()
    {
        await SetupSyncedCatalogAsync();

        // Save a custom action definition
        await _database.SaveActionDefinitionAsync(new SystemActionDefinition(
            "KD_NGOAI_TRU",
            "Kê đơn thuốc ngoại trú",
            2.0,
            "Khám bệnh & Ngoại trú",
            "Mở phân hệ Khám bệnh ngoại trú, chọn người bệnh và kê đơn 3 loại thuốc theo phác đồ.",
            IsBuiltIn: false
        ));

        // Save a template using this custom action
        await _database.SaveTemplateAsync(
            "tpl_custom_act",
            "Đề Khám Bệnh Nội Khoa",
            "Khoa Nội",
            "Bác sĩ",
            "DirectReception",
            new[]
            {
                ("TN_TIEP_NHAN", "Tiếp nhận trực tiếp", 2.0),
                ("KD_NGOAI_TRU", "Kê đơn thuốc ngoại trú", 2.0)
            }
        );

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Bác sĩ Nguyễn", "BS01", "Khoa Nội", "Đề Khám Bệnh Nội Khoa")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("Dot_Custom_Act", new DateOnly(2026, 9, 20), candidates);
        Assert.Single(scenarios);

        var sc = scenarios[0];
        Assert.Equal(2, sc.Questions.Count);

        var q2 = sc.Questions[1];
        Assert.Equal("KD_NGOAI_TRU", q2.ActionCode);
        Assert.Equal("Kê đơn thuốc ngoại trú", q2.Title);
        Assert.Contains("Mở phân hệ Khám bệnh ngoại trú, chọn người bệnh và kê đơn 3 loại thuốc theo phác đồ.", q2.Instruction);
        Assert.Contains("Số điểm đạt được:………(đ)", q2.Instruction);
    }

    [Fact]
    public async Task Candidates_in_same_department_can_select_different_warehouses_and_allocate_correct_drugs()
    {
        await SetupSyncedCatalogAsync();

        // Map Khoa Nội to two distinct warehouses
        await _database.SaveDepartmentWarehouseMappingsAsync("NOI", "Khoa Nội", new[]
        {
            ("KHO-NOI-A", "Kho trực Nội A"),
            ("KHO-NOI-B", "Kho tủ trực B")
        });

        // Add drugs for KHO-NOI-A
        var drugsA = new[]
        {
            new SnapshotDrugRow("da1", "DRUG_A1", "Thuốc A1", "Viên", "KHO-NOI-A", "Kho trực Nội A", "BHYT", 10, "NOI", "Khoa Nội"),
            new SnapshotDrugRow("da2", "DRUG_A2", "Thuốc A2", "Viên", "KHO-NOI-A", "Kho trực Nội A", "BHYT", 10, "NOI", "Khoa Nội"),
            new SnapshotDrugRow("da3", "DRUG_A3", "Thuốc A3", "Viên", "KHO-NOI-A", "Kho trực Nội A", "BHYT", 10, "NOI", "Khoa Nội"),
            new SnapshotDrugRow("da4", "DRUG_A4", "Thuốc A4", "Lọ", "KHO-NOI-A", "Kho trực Nội A", "Viện phí", 10, "NOI", "Khoa Nội"),
            new SnapshotDrugRow("da5", "DRUG_A5", "Thuốc A5", "Ống", "KHO-NOI-A", "Kho trực Nội A", "Viện phí", 10, "NOI", "Khoa Nội")
        };

        // Add drugs for KHO-NOI-B
        var drugsB = new[]
        {
            new SnapshotDrugRow("db1", "DRUG_B1", "Thuốc B1", "Viên", "KHO-NOI-B", "Kho tủ trực B", "BHYT", 10, "NOI", "Khoa Nội"),
            new SnapshotDrugRow("db2", "DRUG_B2", "Thuốc B2", "Viên", "KHO-NOI-B", "Kho tủ trực B", "BHYT", 10, "NOI", "Khoa Nội"),
            new SnapshotDrugRow("db3", "DRUG_B3", "Thuốc B3", "Viên", "KHO-NOI-B", "Kho tủ trực B", "BHYT", 10, "NOI", "Khoa Nội"),
            new SnapshotDrugRow("db4", "DRUG_B4", "Thuốc B4", "Lọ", "KHO-NOI-B", "Kho tủ trực B", "Viện phí", 10, "NOI", "Khoa Nội"),
            new SnapshotDrugRow("db5", "DRUG_B5", "Thuốc B5", "Ống", "KHO-NOI-B", "Kho tủ trực B", "Viện phí", 10, "NOI", "Khoa Nội")
        };

        await _database.ReplaceSnapshotDrugsAsync(drugsA.Concat(drugsB).ToList());

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Thí sinh Kho A", "TS_A", "Khoa Nội", "Đề Mẫu Điều Dưỡng Khoa Nội", 30, "KHO-NOI-A", "Kho trực Nội A"),
            new CandidateInput("Thí sinh Kho B", "TS_B", "Khoa Nội", "Đề Mẫu Điều Dưỡng Khoa Nội", 30, "KHO-NOI-B", "Kho tủ trực B")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("Dot_Multi_Wh", new DateOnly(2026, 9, 21), candidates);
        Assert.Equal(2, scenarios.Count);

        var scA = scenarios.First(s => s.CandidateId == "TS_A");
        var scB = scenarios.First(s => s.CandidateId == "TS_B");

        // Assert Candidate A only got drugs from KHO-NOI-A
        Assert.NotEmpty(scA.OrderedDrugs);
        Assert.All(scA.OrderedDrugs, d => Assert.Equal("KHO-NOI-A", d.WarehouseCode));
        Assert.Equal("KHO-NOI-A", scA.InventoryCheckDrug?.WarehouseCode);

        // Assert Candidate B only got drugs from KHO-NOI-B
        Assert.NotEmpty(scB.OrderedDrugs);
        Assert.All(scB.OrderedDrugs, d => Assert.Equal("KHO-NOI-B", d.WarehouseCode));
        Assert.Equal("KHO-NOI-B", scB.InventoryCheckDrug?.WarehouseCode);
    }

    [Fact]
    public async Task Direct_reception_instruction_includes_full_patient_demographics_and_insurance()
    {
        await SetupSyncedCatalogAsync();

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Thí sinh Tiếp Nhận", "TS_TN", "Khoa Nhi", "Đề Điều dưỡng Nhi (Tiếp nhận trực tiếp)")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("Dot_Direct_Reception", new DateOnly(2026, 9, 23), candidates);
        Assert.Single(scenarios);

        var sc = scenarios[0];
        var q1 = sc.Questions[0];

        // Must contain instruction to open reception module and input both BHYT and VP patient information
        Assert.Contains("Mở phân hệ Tiếp nhận trên phần mềm HIS, thực hiện tiếp nhận trực tiếp người bệnh với thông tin sau:", q1.Instruction);
        Assert.Contains("1. Bệnh nhân Bảo hiểm y tế (BHYT):", q1.Instruction);
        Assert.Contains(sc.Patient.FullName.ToUpperInvariant(), q1.Instruction);
        Assert.Contains(sc.Patient.Gender, q1.Instruction);
        Assert.Contains(sc.Patient.Address, q1.Instruction);
        Assert.Contains(sc.Patient.Diagnosis, q1.Instruction);

        Assert.Contains("2. Bệnh nhân Viện phí:", q1.Instruction);
        Assert.NotNull(sc.SecondPatient);
        Assert.Contains(sc.SecondPatient.FullName.ToUpperInvariant(), q1.Instruction);

        Assert.Contains("Số vào viện bệnh nhân bảo hiểm: ………………………………", q1.Instruction);
        Assert.Contains("Số vào viện bệnh nhân viện phí: ………………………………", q1.Instruction);
        Assert.Contains("Số điểm đạt được:………(đ)", q1.Instruction);
    }

    [Fact]
    public async Task New_inpatient_and_outpatient_actions_render_correct_instructions()
    {
        await SetupSyncedCatalogAsync();

        // Create a custom template testing the 4 new actions
        await _database.SaveTemplateAsync(
            "tpl_new_actions",
            "Đề Test Nghiệp Vụ Ngoại Trú & Cấp Giường",
            "Khoa Nội",
            "Bác sĩ",
            "WardAdmissionPreparation",
            new[]
            {
                ("NT_CAP_GIUONG", "Cấp giường cho bệnh nhân", 1.0),
                ("KB_CHI_DINH_CLS", "Chỉ định CLS ngoại trú", 2.0),
                ("KB_BENH_AN_NGOAI_TRU", "Tạo bệnh án ngoại trú", 2.0),
                ("KB_CHI_DINH_NOI_TRU", "Chỉ định điều trị nội trú", 1.0),
                ("HC_TRA_CUU_THANH_TOAN", "Tra cứu thông tin thanh toán", 2.0)
            }
        );

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Bác sĩ Ngoại Trú", "BS_NT", "Khoa Nội", "Đề Test Nghiệp Vụ Ngoại Trú & Cấp Giường")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("Dot_New_Actions", new DateOnly(2026, 9, 23), candidates);
        Assert.Single(scenarios);

        var sc = scenarios[0];
        Assert.Equal(5, sc.Questions.Count);

        // 1. Cấp giường
        var qCapGiuong = sc.Questions[0];
        Assert.Equal("NT_CAP_GIUONG", qCapGiuong.ActionCode);
        Assert.Contains("Cấp giường cho các bệnh nhân được tiếp nhận ở câu 1", qCapGiuong.Instruction);

        // 2. Chỉ định CLS ngoại trú
        var qChiDinhCls = sc.Questions[1];
        Assert.Equal("KB_CHI_DINH_CLS", qChiDinhCls.ActionCode);
        Assert.Contains("Trên màn hình khám bệnh Chỉ định CLS – Thủ thuật cho bệnh nhân : ……………………………………, Mã Y tế : ……………………", qChiDinhCls.Instruction);
        // Must contain 2 randomized services
        var clsLines = qChiDinhCls.Instruction.Split('\n').Where(l => l.StartsWith("+")).ToList();
        Assert.Equal(2, clsLines.Count);

        // 3. Tạo bệnh án ngoại trú
        var qBaNgoaiTru = sc.Questions[2];
        Assert.Equal("KB_BENH_AN_NGOAI_TRU", qBaNgoaiTru.ActionCode);
        Assert.Contains("Chỉ định điều trị ngoại trú và lập bệnh án ngoại trú cho bệnh nhân : ……………………………………, Mã Y tế : …………………… (2 đ)", qBaNgoaiTru.Instruction);
        Assert.Contains("Số bệnh án ngoại trú : …………………………………………………", qBaNgoaiTru.Instruction);

        // 4. Chỉ định điều trị nội trú
        var qChiDinhNoiTru = sc.Questions[3];
        Assert.Equal("KB_CHI_DINH_NOI_TRU", qChiDinhNoiTru.ActionCode);
        Assert.Contains("Chỉ định nhập viện điều trị nội trú cho bệnh nhân : ……………………………………, Mã y tế : …………………… (1 đ)", qChiDinhNoiTru.Instruction);
        Assert.Contains("Nhập khoa :", qChiDinhNoiTru.Instruction);
        Assert.Contains("Số bệnh án nội trú : ……………………………………………………", qChiDinhNoiTru.Instruction);

        // 5. Tra cứu thông tin thanh toán
        var qThanhToan = sc.Questions[4];
        Assert.Equal("HC_TRA_CUU_THANH_TOAN", qThanhToan.ActionCode);
        Assert.Contains("Kiểm tra thông tin thanh toán cho bệnh nhân : ", qThanhToan.Instruction);
        Assert.Contains("Mã y tế : ", qThanhToan.Instruction);
        Assert.Contains("Số bệnh án nội trú : ", qThanhToan.Instruction);
        Assert.NotNull(sc.InpatientLookupCase);
        Assert.Contains(sc.InpatientLookupCase.FullName, qThanhToan.Instruction);
        Assert.Contains(sc.InpatientLookupCase.MedicalCode, qThanhToan.Instruction);
        Assert.Contains("Số tiền người bệnh đã thanh toán : ……………………………………………………", qThanhToan.Instruction);
        Assert.Contains("Số tiền người bệnh đã tạm ứng : ……………………………………………………", qThanhToan.Instruction);
        Assert.Contains("Số tiền còn phải thu người bệnh : ……………………………………………………", qThanhToan.Instruction);
        Assert.Contains("Tổng số tiền người bệnh trả : ……………", qThanhToan.Instruction);
    }

    [Fact]
    public async Task Inventory_check_question_uses_current_exam_date_when_warehouse_has_no_closed_period_data()
    {
        await SetupSyncedCatalogAsync();

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Nguyễn Văn A", "SBD01", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        // Exam date in October 2026
        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_Test", new DateOnly(2026, 10, 20), candidates);
        var sc = scenarios.Single();

        var qInv = sc.Questions.FirstOrDefault(q => q.ActionCode == "TK_KIEM_TON_KHO");
        Assert.NotNull(qInv);
        Assert.Null(sc.InventoryCheckPeriod);
        Assert.NotNull(sc.InventoryCheckDrug);
        Assert.Contains("tại thời điểm hiện tại / ngày thi 20/10/2026", qInv.Instruction);
    }

    [Fact]
    public async Task Throws_preflight_when_template_requires_inpatient_cases_but_snapshot_is_empty()
    {
        await SetupSyncedCatalogAsync();

        // Clear inpatients from SQLite
        await _database.ReplaceSnapshotInpatientsAsync(Array.Empty<SnapshotInpatientRow>());

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Thu Ngân A", "TN01", "Khoa Nội", "tpl-lt-thuphi")
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            allocator.AllocateBatchScenariosAsync("DotThi_FailInpatient", new DateOnly(2026, 10, 30), candidates));

        Assert.Contains("Bệnh án nội trú", ex.Message);
    }

    [Fact]
    public async Task Throws_preflight_when_template_requires_outpatient_cases_but_snapshot_is_empty()
    {
        await SetupSyncedCatalogAsync();

        // Clear outpatients from SQLite
        await _database.ReplaceSnapshotOutpatientsAsync(Array.Empty<SnapshotOutpatientRow>());

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Lễ Tân B", "LT01", "Khoa Nội", "tpl-lt-thuphi")
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            allocator.AllocateBatchScenariosAsync("DotThi_FailOutpatient", new DateOnly(2026, 10, 30), candidates));

        Assert.Contains("Bệnh án ngoại trú", ex.Message);
    }

    [Fact]
    public async Task Inventory_check_question_allocates_from_defined_snapshot_periods_with_exact_closing_stock()
    {
        await SetupSyncedCatalogAsync();

        // Seed fixed inventory period
        await _database.ReplaceSnapshotInventoryPeriodsAsync(new[]
        {
            new SnapshotInventoryPeriodRow(
                "P1_2026_01A",
                "01/01/2026 đến 15/01/2026",
                "2026-01-01",
                "2026-01-15 23:59:59",
                "KHO-NOI",
                "Kho trực Nội",
                "NOI",
                "Khoa Nội",
                "AMOX500",
                "Amoxicillin 500mg",
                "Viên",
                125.0,
                "Viện phí"
            )
        });

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Nguyễn Văn A", "SBD01", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_PeriodTest", new DateOnly(2026, 10, 20), candidates);
        var sc = scenarios.Single();

        var qInv = sc.Questions.FirstOrDefault(q => q.ActionCode == "TK_KIEM_TON_KHO");
        Assert.NotNull(qInv);
        Assert.NotNull(sc.InventoryCheckPeriod);
        Assert.Equal("AMOX500", sc.InventoryCheckPeriod.DrugCode);
        Assert.Equal(125.0, sc.InventoryCheckPeriod.ClosingStock);
        Assert.Contains("01/01/2026 đến ngày 15/01/2026", qInv.Instruction);
        Assert.Contains("Amoxicillin 500mg", qInv.Instruction);
    }

    [Fact]
    public async Task Template_without_drug_order_action_does_not_allocate_ordered_drugs_or_render_drug_table()
    {
        await SetupSyncedCatalogAsync();

        // Template for Kỹ thuật viên Xét nghiệm does not order drugs
        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("KTV Xét Nghiệm 1", "XN01", "Khoa Nội", "Đề Kỹ thuật viên Xét nghiệm")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_TestXN", new DateOnly(2026, 9, 20), candidates);
        var sc = scenarios.Single();

        Assert.Empty(sc.OrderedDrugs);
        Assert.Null(sc.DrugReturn);

        // Render Word and verify no drug table
        var tempDocx = Path.Combine(Path.GetTempPath(), $"Test_NoDrug_{Guid.NewGuid():N}.docx");
        var writer = new WordExamWriter();
        writer.Create(tempDocx, sc);

        Assert.True(File.Exists(tempDocx));
        using var wordDoc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(tempDocx, false);
        var xmlContent = wordDoc.MainDocumentPart!.Document.InnerXml;
        Assert.DoesNotContain("Danh sách thuốc, VTYT cần lên y lệnh cho bệnh nhân:", xmlContent);

        try { File.Delete(tempDocx); } catch { }
    }

    [Fact]
    public async Task Throws_preflight_when_department_has_no_mapped_users_in_tab3()
    {
        await SetupSyncedCatalogAsync();

        // Clear mapped users for Khoa Nội
        await _database.SaveDepartmentUserMappingsAsync("NOI", "Khoa Nội", Array.Empty<(string, string, string)>());

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Nguyễn Văn A", "SBD01", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            allocator.AllocateBatchScenariosAsync("DotThi_Test", new DateOnly(2026, 9, 15), candidates));

        Assert.Contains("chưa được cấu hình danh sách User HIS thi trong Tab 3", ex.Message);
    }

    [Fact]
    public async Task Frontdesk_template_generates_7_questions_and_answer_key_document()
    {
        await SetupSyncedCatalogAsync();

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Nguyễn Thị Thu Phí", "LT01", "Khoa Nội", "tpl-lt-thuphi")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_ThuPhi_2026", new DateOnly(2026, 10, 16), candidates);
        Assert.Single(scenarios);

        var sc = scenarios[0];
        Assert.Equal(7, sc.Questions.Count);
        Assert.Equal(10.0, sc.TotalScore);

        // Check questions
        Assert.Equal("LT_BAN_PHIEU_KHAM", sc.Questions[0].ActionCode);
        Assert.Equal(1.0, sc.Questions[0].Score);
        Assert.Contains("Tiếp nhận bệnh nhân và cho chỉ định khám bệnh", sc.Questions[0].Instruction);
        Assert.Contains(sc.SecondPatient!.FullName.ToUpperInvariant(), sc.Questions[0].Instruction);
        Assert.Contains(sc.Patient.FullName.ToUpperInvariant(), sc.Questions[0].Instruction);
        Assert.Contains("Khám nội VP", sc.Questions[0].Instruction);
        Assert.Contains("Khám ngoại tổng hợp BHYT", sc.Questions[0].Instruction);

        Assert.Equal("LT_THU_PHI_VA_CLS", sc.Questions[1].ActionCode);
        Assert.Equal(2.0, sc.Questions[1].Score);
        Assert.Contains("THI", sc.Questions[1].Instruction);

        Assert.Equal("LT_THU_TAM_UNG_NOI_TRU", sc.Questions[2].ActionCode);
        Assert.Equal(2.0, sc.Questions[2].Score);
        Assert.NotNull(sc.AdvancePaymentCase1);
        Assert.NotNull(sc.AdvancePaymentOutpatientCase);
        Assert.Contains("Bệnh nhân nội trú có số bệnh án", sc.Questions[2].Instruction);
        Assert.Contains("Bệnh nhân bệnh án ngoại trú có số bệnh án", sc.Questions[2].Instruction);
        Assert.Contains("1.500.000", sc.Questions[2].Instruction);
        Assert.Contains("500.000", sc.Questions[2].Instruction);

        Assert.Equal("LT_DOI_DICH_VU_CLS", sc.Questions[3].ActionCode);
        Assert.Equal(1.0, sc.Questions[3].Score);

        Assert.Equal("LT_TRA_CUU_THONG_TIN", sc.Questions[4].ActionCode);
        Assert.Equal(2.0, sc.Questions[4].Score);
        Assert.NotNull(sc.InpatientLookupCase);
        Assert.NotNull(sc.OutpatientLookupCase);

        Assert.Equal("LT_THANH_TOAN_RA_VIEN", sc.Questions[5].ActionCode);
        Assert.Equal(1.0, sc.Questions[5].Score);
        Assert.NotNull(sc.DischargeCase);

        Assert.Equal("LT_TAO_THE_TAM_BHYT", sc.Questions[6].ActionCode);
        Assert.Equal(1.0, sc.Questions[6].Score);
        // Date of birth must be 1 month before examDate (examDate = 2026-10-16 -> birthDate = 16/09/2026)
        Assert.Contains("Ngày sinh: 16/09/2026", sc.Questions[6].Instruction);

        // Test Word writer creates exam and answer key docx
        var tempExamDocx = Path.Combine(Path.GetTempPath(), $"DeThi_LT_{Guid.NewGuid():N}.docx");
        var tempKeyDocx = Path.Combine(Path.GetTempPath(), $"DapAn_LT_{Guid.NewGuid():N}.docx");

        var writer = new WordExamWriter();
        writer.Create(tempExamDocx, sc);
        writer.CreateAnswerKeyDocument(tempKeyDocx, scenarios);

        Assert.True(File.Exists(tempExamDocx));
        Assert.True(File.Exists(tempKeyDocx));

        try { File.Delete(tempExamDocx); } catch { }
        try { File.Delete(tempKeyDocx); } catch { }
    }

    [Fact]
    public async Task Ktv_gmhs_template_allocates_inpatient_and_active_surgery_and_in_stock_supplies()
    {
        await SetupSyncedCatalogAsync();

        // 1. Setup GMHS department, warehouse, users
        await _database.SaveDepartmentConfigurationAsync("GMHS", "Khoa Gây mê hồi sức", "KHO-GMHS", "Kho Gây mê");
        await _database.ReplaceSnapshotUsersAsync(new[]
        {
            new SnapshotUserRow("u_gmhs", "gmhs_user", "KTV. Phan Thị Phương", "GMHS", "Khoa Gây mê hồi sức")
        });
        await _database.SaveDepartmentUserMappingsAsync("GMHS", "Khoa Gây mê hồi sức", new[]
        {
            ("u_gmhs", "gmhs_user", "KTV. Phan Thị Phương")
        });

        // 2. Active surgical services in catalog
        await _database.ReplaceCatalogAsync("Services", new (string Id, string Code, string Name, string? ParentId)[]
        {
            ("s_pt1", "PT_THAI", "Phẫu thuật lấy thai lần đầu [gây tê]", "Phẫu thuật"),
            ("s_pt2", "PT_RUOTTHUA", "Phẫu thuật cắt ruột thừa nội soi", "Phẫu thuật")
        });

        // 3. Inpatient currently in hospital
        await _database.ReplaceSnapshotInpatientsAsync(new[]
        {
            new SnapshotInpatientRow(
                "ba_gmhs_1",
                "22.000173",
                "22000173",
                "Lê Thị Hồng",
                "Nữ",
                "1990",
                "Buôn Ma Thuột",
                "GMHS",
                "Khoa Gây mê hồi sức",
                "P_MO",
                "Phòng mổ 1",
                "G01",
                "Tầng 2",
                "2026-06-20",
                "2026-06-20 08:00:00",
                "2026-06-20 08:30:00",
                "2026-06-20",
                4,
                "",
                "Hậu phẫu",
                "BS. Phẫu Thuật"
            )
        });

        // 4. In-stock drugs & supplies in GMHS warehouse
        await _database.ReplaceSnapshotDrugsAsync(new[]
        {
            new SnapshotDrugRow("d_dao", "LuDV14", "Lưỡi dao mổ số 10", "Cái", "KHO-GMHS", "Kho Gây mê", "Viện phí", 50),
            new SnapshotDrugRow("d_chi", "ChCV3", "Chỉ Chromic Catgut 2/0", "Tép", "KHO-GMHS", "Kho Gây mê", "Viện phí", 100),
            new SnapshotDrugRow("d_gac", "GaPV3", "Gạc PT 10x10x12 lớp", "Miếng", "KHO-GMHS", "Kho Gây mê", "Viện phí", 200),
            new SnapshotDrugRow("d_gang", "GaTV12", "Găng tay y tế khám", "Đôi", "KHO-GMHS", "Kho Gây mê", "Viện phí", 80),
            new SnapshotDrugRow("d_ao", "aoPV", "Áo phẫu thuật dùng một lần", "Cái", "KHO-GMHS", "Kho Gây mê", "Viện phí", 30),
            new SnapshotDrugRow("d_kim", "KimG18", "Kim tiêm nhựa G18", "Cái", "KHO-GMHS", "Kho Gây mê", "Viện phí", 500)
        });

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Phan Thị Phương", "GMHS01", "Khoa Gây mê hồi sức", "tpl-ktv-gmhs")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_GMHS_2026", new DateOnly(2026, 6, 24), candidates);
        Assert.Single(scenarios);

        var sc = scenarios[0];
        Assert.Equal(2, sc.Questions.Count);
        Assert.Equal(10.0, sc.TotalScore);

        // Bệnh nhân là bệnh nhân đang nằm trong khoa: Lê Thị Hồng
        Assert.Equal("Lê Thị Hồng", sc.Patient.FullName);

        // Câu 1 (8đ): Lên Dược - VTYT cho bệnh nhân phẫu thuật + Lĩnh Nhập Xuất
        var q1 = sc.Questions[0];
        Assert.Equal("YL_CHI_DINH_THUOC_VTYT", q1.ActionCode);
        Assert.Equal(8.0, q1.Score);
        Assert.Contains("Lê Thị Hồng", q1.Instruction);
        Assert.Contains("22.000173", q1.Instruction);
        Assert.Contains("sau phẫu thuật:", q1.Instruction);
        Assert.Contains("ngày yêu cầu 24/06/2026", q1.Instruction);
        Assert.Contains("Lập phiếu lĩnh dược", q1.Instruction);
        Assert.Contains("Nhập dược nội bộ", q1.Instruction);
        Assert.Contains("Xuất sử dụng thuốc - VTYT cho bệnh nhân", q1.Instruction);

        // Danh mục Dược - VTYT được cấp phát: có tồn kho trong kho Gây mê
        Assert.NotEmpty(sc.OrderedDrugs);
        foreach (var d in sc.OrderedDrugs)
        {
            Assert.Equal("Kho Gây mê", d.WarehouseName);
            Assert.True(d.Quantity >= 1);
        }

        // Câu 2 (2đ): Báo tồn kho
        var q2 = sc.Questions[1];
        Assert.Equal("TK_KIEM_TON_KHO", q2.ActionCode);
        Assert.Equal(2.0, q2.Score);
        Assert.Contains("Kho Gây mê", q2.Instruction);
        Assert.Contains("Viện phí", q2.Instruction);

        // Test Word document generation
        var tempDocx = Path.Combine(Path.GetTempPath(), $"DeThi_GMHS_{Guid.NewGuid():N}.docx");
        var writer = new WordExamWriter();
        writer.Create(tempDocx, sc);
        Assert.True(File.Exists(tempDocx));
        try { File.Delete(tempDocx); } catch { }
    }

    [Fact]
    public async Task LienChuyenKhoa_Mat_Tmh_Rhm_Allocate_And_Render_Two_Users()
    {
        await SetupSyncedCatalogAsync();

        // Setup Khoa Mắt
        await _database.SaveDepartmentConfigurationAsync("MAT", "Khoa Mắt", "KHO-MAT", "Kho Mắt");
        await _database.ReplaceSnapshotUsersAsync(new[]
        {
            new SnapshotUserRow("u_mat_1", "HOADT", "ĐD. Hoàng Đỗ Thư", "MAT", "Khoa Mắt"),
            new SnapshotUserRow("u_mat_2", "YENNTH_DD", "ĐD. Nguyễn Thị Yến", "MAT", "Khoa Mắt")
        });
        await _database.SaveDepartmentUserMappingsAsync("MAT", "Khoa Mắt", new[]
        {
            ("u_mat_1", "HOADT", "ĐD. Hoàng Đỗ Thư"),
            ("u_mat_2", "YENNTH_DD", "ĐD. Nguyễn Thị Yến")
        });

        // Setup CLS & Thủ thuật khoa Mắt
        await _database.ReplaceCatalogAsync("Services", new (string Id, string Code, string Name, string? ParentId)[]
        {
            ("s_mat_1", "CLS_DAYMAT", "Chụp đáy mắt không huỳnh quang", "Mắt"),
            ("s_mat_2", "TT_APXEMI", "Rạch áp xe mi", "Mắt"),
            ("s_mat_3", "CLS_DO_NHANAP", "Đo nhãn áp", "Mắt"),
            ("s_mat_4", "TT_CONG_DO", "Rửa cùng đồ bóc giả mạc", "Mắt")
        });

        // Setup Tồn kho Mắt (Nguồn Viện phí)
        await _database.ReplaceSnapshotDrugsAsync(new[]
        {
            new SnapshotDrugRow("d1", "GaTV21", "Găng tay kiểm tra có bột size S", "Đôi", "KHO-MAT", "Kho Mắt", "Viện phí", 100),
            new SnapshotDrugRow("d2", "BoTV19", "Bơm tiêm MPV 1ml 26G", "Cái", "KHO-MAT", "Kho Mắt", "Viện phí", 100),
            new SnapshotDrugRow("d3", "ChNV2", "Chỉ CARELON (Nylon) 7/0", "Tép", "KHO-MAT", "Kho Mắt", "Viện phí", 50),
            new SnapshotDrugRow("d4", "ChDV8", "Chỉ Dafilon 6/0 45cm", "Tép", "KHO-MAT", "Kho Mắt", "Viện phí", 50),
            new SnapshotDrugRow("d5", "DaGV3", "Dây garo VN", "Cái", "KHO-MAT", "Kho Mắt", "Viện phí", 30),
            new SnapshotDrugRow("d6", "KiLV1", "Kim Luồn VASOFIX G20", "Cái", "KHO-MAT", "Kho Mắt", "Viện phí", 80),
            new SnapshotDrugRow("d7", "LuDV4", "Lưỡi dao mổ số 11", "Cái", "KHO-MAT", "Kho Mắt", "Viện phí", 60),
            new SnapshotDrugRow("d8", "NACT23", "NATRI CLORID 0,9% 10ml", "Lọ", "KHO-MAT", "Kho Mắt", "Viện phí", 200)
        });

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Hoàng Ngọc Linh", "MAT01", "Khoa Mắt", "tpl-dd-mat")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_Mat_2026", new DateOnly(2026, 6, 25), candidates);
        Assert.Single(scenarios);

        var sc = scenarios[0];
        Assert.Equal(6, sc.Questions.Count);
        Assert.Equal(10.0, sc.TotalScore);

        // Câu 1 (2đ): Trên màn hình khám bệnh - Chỉ định CLS - Thủ thuật khoa Mắt
        var q1 = sc.Questions[0];
        Assert.Equal("KB_CHI_DINH_CLS", q1.ActionCode);
        Assert.Equal(2.0, q1.Score);
        Assert.Contains("Trên màn hình khám bệnh - Chỉ định CLS – Thủ thuật cho bệnh nhân", q1.Instruction);

        // Câu 2 (1đ): Viết tường trình thủ thuật
        var q2 = sc.Questions[1];
        Assert.Equal("TT_TUONG_TRINH_PTTT", q2.ActionCode);
        Assert.Equal(1.0, q2.Score);
        Assert.Contains("Viết  tường trình cho thủ thuật", q2.Instruction);

        // Câu 3 (2đ): Lập bệnh án ngoại trú
        var q3 = sc.Questions[2];
        Assert.Equal("KB_BENH_AN_NGOAI_TRU", q3.ActionCode);
        Assert.Equal(2.0, q3.Score);
        Assert.Contains("Chỉ định điều trị ngoại trú và lập bệnh án ngoại trú cho bệnh nhân", q3.Instruction);

        // Câu 4 (1đ): Nhập viện nội trú Khoa GMHS
        var q4 = sc.Questions[3];
        Assert.Equal("KB_CHI_DINH_NOI_TRU", q4.ActionCode);
        Assert.Equal(1.0, q4.Score);
        Assert.Contains("Nhập khoa : Khoa phẫu thuật GMHS", q4.Instruction);

        // Câu 5 (3đ): Lên phiếu lĩnh cơ số Dược / VTYT
        var q5 = sc.Questions[4];
        Assert.Equal("YL_CHI_DINH_THUOC_VTYT", q5.ActionCode);
        Assert.Equal(3.0, q5.Score);
        Assert.Contains("Lên phiếu lĩnh cơ số Dược/ VTYT", q5.Instruction);
        Assert.Contains("Lập phiếu", q5.Instruction);
        Assert.Contains("Nhập Nội Bộ", q5.Instruction);
        Assert.Contains("Xuất sử dụng", q5.Instruction);

        // Dược được cấp phát có 8 món Viện phí
        Assert.Equal(8, sc.OrderedDrugs.Count);
        foreach (var d in sc.OrderedDrugs)
        {
            Assert.Equal("Viện phí", d.FundingSource);
            Assert.Equal(1, d.Quantity);
        }

        // Câu 6 (1đ): Tra cứu lịch sử KCB/Bệnh sử
        var q6 = sc.Questions[5];
        Assert.Equal("TC_TRA_CUU_BENH_SU", q6.ActionCode);
        Assert.Equal(1.0, q6.Score);
        Assert.Contains("Tra cứu lịch sử KCB/ Bệnh sử của bệnh nhân", q6.Instruction);
        Assert.Contains("Đợt khám bệnh gần nhất", q6.Instruction);
        Assert.Contains("CLS thực hiện gần nhất", q6.Instruction);

        // Test Word rendering: Footnote with 2 users (BS để trống, ĐD có username)
        var tempDocx = Path.Combine(Path.GetTempPath(), $"DeThi_Mat_{Guid.NewGuid():N}.docx");
        var writer = new WordExamWriter();
        writer.Create(tempDocx, sc);
        Assert.True(File.Exists(tempDocx));

        // Read XML from generated DOCX to verify footer text
        using (var archive = System.IO.Compression.ZipFile.OpenRead(tempDocx))
        {
            var docEntry = archive.GetEntry("word/document.xml");
            Assert.NotNull(docEntry);
            using var reader = new StreamReader(docEntry.Open());
            var xmlContent = await reader.ReadToEndAsync();
            Assert.Contains("sử dụng user ………………; password: 123 để thi", xmlContent);
            Assert.Contains($"sử dụng user {sc.HisUserCode}; Password: 123 để làm câu 5", xmlContent);
            Assert.Contains("Danh mục Dược – VTYT:", xmlContent);
        }

        try { File.Delete(tempDocx); } catch { }
    }

    [Fact]
    public async Task Reception_questions_truncate_insurance_number_longer_than_15_characters()
    {
        await SetupSyncedCatalogAsync();

        // Seed patient with 18-character BHYT card: te1666699999999999 -> te1666699999999
        await _database.ReplaceSnapshotPatientsAsync(new[]
        {
            new SnapshotPatientRow("p_long_1", "26901", "Nguyễn Bảo Hiểm Dài", "2022-05-10", "Nam", "Buôn Ma Thuột", "te1666699999999999", "Viêm phế quản", "BHYT"),
            new SnapshotPatientRow("p_long_2", "26902", "Trần Bảo Hiểm Dài", "1990-02-20", "Nữ", "Buôn Ma Thuột", "GD4662321234567890", "Viêm dạ dày", "BHYT"),
            new SnapshotPatientRow("p_vp_1", "26903", "Lê Viện Phí 1", "1988-01-01", "Nam", "Buôn Ma Thuột", null, "Đau đầu", "Viện phí"),
            new SnapshotPatientRow("p_vp_2", "26904", "Phạm Viện Phí 2", "1995-03-03", "Nữ", "Buôn Ma Thuột", null, "Đau bụng", "Viện phí")
        });

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Thí sinh Nhi", "NHI01", "Khoa Nhi", "tpl-nhi"),
            new CandidateInput("Thí sinh Lễ Tân", "LT01", "Khoa Nội", "tpl-lt-thuphi")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_TruncateBhyt", new DateOnly(2026, 10, 29), candidates);
        Assert.Equal(2, scenarios.Count);

        var nhiScenario = scenarios.First(s => s.CandidateId == "NHI01");
        Assert.Equal(15, nhiScenario.Patient.InsuranceNumber!.Length);
        Assert.Equal("te1666699999999", nhiScenario.Patient.InsuranceNumber);
        Assert.Contains("+ Số thẻ BHYT: te1666699999999", nhiScenario.Questions[0].Instruction);
        Assert.DoesNotContain("te1666699999999999", nhiScenario.Questions[0].Instruction);

        var ltScenario = scenarios.First(s => s.CandidateId == "LT01");
        Assert.Equal(15, ltScenario.Patient.InsuranceNumber!.Length);
        Assert.Equal("GD4662321234567", ltScenario.Patient.InsuranceNumber);
        Assert.Contains("+ Số thẻ BHYT: GD4662321234567", ltScenario.Questions[0].Instruction);
        Assert.DoesNotContain("GD4662321234567890", ltScenario.Questions[0].Instruction);
    }

    [Fact]
    public async Task Discharge_payment_question_generates_benh_an_update_sql_with_so_vao_vien()
    {
        await SetupSyncedCatalogAsync();

        await _database.ReplaceSnapshotInpatientsAsync(new[]
        {
            new SnapshotInpatientRow("ba_1", "25.019281", "25019281", "Trần Văn Nội Trú 1", "Nam", "1978", "Buôn Ma Thuột", "NOI", "Khoa Nội", "P101", "Phòng 101", "G01", "Tầng 1", "20/10/2026", "2026-10-20 08:00:00", "2026-10-20 08:30:00", "20/10/2026", 9, "GD4662321111111", "Viêm phổi ổn định", "BS. Nguyễn Văn A"),
            new SnapshotInpatientRow("ba_2", "25.019282", "25019282", "Lê Thị Nội Trú 2", "Nữ", "1982", "Buôn Ma Thuột", "NOI", "Khoa Nội", "P102", "Phòng 102", "G02", "Tầng 1", "21/10/2026", "2026-10-21 08:00:00", "2026-10-21 08:30:00", "21/10/2026", 8, "GD4662322222222", "Viêm dạ dày", "BS. Nguyễn Văn A"),
            new SnapshotInpatientRow("ba_3", "25.019283", "25019283", "Phạm Văn Nội Trú 3", "Nam", "1975", "Buôn Ma Thuột", "NOI", "Khoa Nội", "P103", "Phòng 103", "G03", "Tầng 1", "22/10/2026", "2026-10-22 08:00:00", "2026-10-22 08:30:00", "22/10/2026", 7, "GD4662323333333", "Tăng huyết áp", "BS. Nguyễn Văn A"),
            new SnapshotInpatientRow("ba_4", "25.019284", "25019284", "Hoàng Thị Nội Trú 4", "Nữ", "1990", "Buôn Ma Thuột", "NOI", "Khoa Nội", "P104", "Phòng 104", "G04", "Tầng 1", "23/10/2026", "2026-10-23 08:00:00", "2026-10-23 08:30:00", "23/10/2026", 6, "GD4662324444444", "Sốt xuất huyết", "BS. Nguyễn Văn A"),
            new SnapshotInpatientRow("ba_5", "25.019285", "25019285", "Đỗ Văn Nội Trú 5", "Nam", "1968", "Buôn Ma Thuột", "NOI", "Khoa Nội", "P105", "Phòng 105", "G05", "Tầng 1", "24/10/2026", "2026-10-24 08:00:00", "2026-10-24 08:30:00", "24/10/2026", 5, "GD4662325555555", "Đái tháo đường", "BS. Nguyễn Văn A"),
            new SnapshotInpatientRow("ba_6", "25.019286", "25019286", "Vũ Thị Nội Trú 6", "Nữ", "1985", "Buôn Ma Thuột", "NOI", "Khoa Nội", "P106", "Phòng 106", "G06", "Tầng 1", "25/10/2026", "2026-10-25 08:00:00", "2026-10-25 08:30:00", "25/10/2026", 4, "GD4662326666666", "Rối loạn tiền đình", "BS. Nguyễn Văn A"),
            new SnapshotInpatientRow("ba_7", "25.019287", "25019287", "Bùi Văn Nội Trú 7", "Nam", "1972", "Buôn Ma Thuột", "NOI", "Khoa Nội", "P107", "Phòng 107", "G07", "Tầng 1", "26/10/2026", "2026-10-26 08:00:00", "2026-10-26 08:30:00", "26/10/2026", 3, "GD4662327777777", "Viêm phế quản", "BS. Nguyễn Văn A"),
            new SnapshotInpatientRow("ba_8", "25.019288", "25019288", "Ngô Thị Nội Trú 8", "Nữ", "1993", "Buôn Ma Thuột", "NOI", "Khoa Nội", "P108", "Phòng 108", "G08", "Tầng 1", "27/10/2026", "2026-10-27 08:00:00", "2026-10-27 08:30:00", "27/10/2026", 2, "GD4662328888888", "Viêm họng cấp", "BS. Nguyễn Văn A")
        });

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Thu Ngân 01", "TN01", "Khoa Nội", "tpl-lt-thuphi"),
            new CandidateInput("Thu Ngân 02", "TN02", "Khoa Nội", "tpl-lt-thuphi")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_RaVienSql", new DateOnly(2026, 10, 29), candidates);
        Assert.Equal(2, scenarios.Count);

        var allOtherSoBenhAnInBatch = scenarios
            .SelectMany(s => new[] { s.AdvancePaymentCase1?.SoBenhAn, s.AdvancePaymentCase2?.SoBenhAn, s.InpatientLookupCase?.SoBenhAn })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sqlGen = new SqlScriptGenerator();
        var dischargeSoBenhAnSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sc in scenarios)
        {
            Assert.NotNull(sc.DischargeCase);
            var soBenhAnCau6 = sc.DischargeCase.SoBenhAn;
            Assert.False(string.IsNullOrWhiteSpace(soBenhAnCau6));

            // Số bệnh án câu 6 không trùng với bất kỳ số bệnh án nào khác (tạm ứng, tra cứu, hoặc câu 6 của thí sinh khác) trong cùng đợt thi
            Assert.DoesNotContain(soBenhAnCau6, allOtherSoBenhAnInBatch);
            Assert.True(dischargeSoBenhAnSet.Add(soBenhAnCau6));

            var q6 = sc.Questions.First(q => q.OrderIndex == 6);
            Assert.Equal("LT_THANH_TOAN_RA_VIEN", q6.ActionCode);
            Assert.Contains(soBenhAnCau6, q6.Instruction);

            var sql = sqlGen.Generate(sc);
            Assert.Contains($"DECLARE @SoVaoVien VARCHAR(50) = '{soBenhAnCau6}';", sql);
            Assert.Contains("DECLARE @ThoiDiemRaVien DATETIME = GETDATE();", sql);
            Assert.Contains("UPDATE BenhAn", sql);
            Assert.Contains("TrangThai = N'daxuatvien',", sql);
            Assert.Contains("NgayRaVien = CAST(@ThoiDiemRaVien AS DATE),", sql);
            Assert.Contains("ThoiGianRaVien = @ThoiDiemRaVien", sql);
            Assert.Contains("WHERE SoVaoVien = @SoVaoVien", sql);
            Assert.Contains("AND TrangThai = N'dangdieutri';", sql);

            // Verify GetActuallyUsedPatients only includes patients referenced in this template's questions
            var usedPatients = sc.GetActuallyUsedPatients();
            Assert.Contains(usedPatients, p => p.MedicalCode == sc.DischargeCase.MedicalCode);
            if (sc.AdvancePaymentOutpatientCase is not null && sc.AdvancePaymentCase2 is not null)
            {
                Assert.DoesNotContain(usedPatients, p => p.MedicalCode == sc.AdvancePaymentCase2.MedicalCode);
            }

            // Verify scenario persistence in SQLite
            await _database.SaveGeneratedScenarioAsync(sc);
        }

        var mergedSql = sqlGen.GenerateMerged(scenarios);
        Assert.Contains("SCRIPT TỔNG HỢP TOÀN ĐỢT THI:", mergedSql);
        Assert.Contains("TỔNG SỐ THÍ SINH: 2", mergedSql);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(mergedSql, "\r?\nGO(\r?\n|$)").Count >= 2);

        var loadedScenarios = await _database.GetLatestGeneratedScenariosAsync();
        Assert.Equal(2, loadedScenarios.Count);
        Assert.All(scenarios, s => Assert.Contains(loadedScenarios, l => l.ScenarioId == s.ScenarioId && l.DischargeCase?.SoBenhAn == s.DischargeCase?.SoBenhAn));
    }

    [Fact]
    public async Task Clinical_services_randomization_excludes_deprecated_and_spreads_across_candidates()
    {
        await SetupSyncedCatalogAsync();

        await _database.ReplaceCatalogAsync("Services", new (string Id, string Code, string Name, string? ParentId)[]
        {
            ("s_ngung", "SA_OLD", "[NGƯNG] Siêu âm ổ bụng cũ", "Siêu âm"),
            ("s_kdung", "XQ_OLD", "Chụp Xquang ngực (KHÔNG DÙNG)", "X-quang"),
            ("s_kham", "KB_NOI", "Khám Nội tổng quát", "Khám bệnh"),
            ("s_pt", "PT_RT", "Phẫu thuật cắt ruột thừa", "Phẫu thuật"),
            ("sa1", "SA_BUNG", "Siêu âm ổ bụng tổng quát", "Siêu âm"),
            ("sa2", "SA_TIM", "Siêu âm Doppler tim", "Siêu âm"),
            ("sa3", "SA_GIAP", "Siêu âm tuyến giáp", "Siêu âm"),
            ("xq1", "XQ_PHOI", "Chụp Xquang tim phổi thẳng", "X-quang"),
            ("xq2", "XQ_COTSONG", "Chụp Xquang cột sống thắt lưng", "X-quang"),
            ("xq3", "XQ_KHOPGOI", "Chụp Xquang khớp gối", "X-quang"),
            ("ct1", "CT_SO", "Chụp cắt lớp vi tính sọ não", "Cắt lớp"),
            ("ct2", "CT_NGUC", "Chụp cắt lớp vi tính lồng ngực", "Cắt lớp"),
            ("xn1", "XN_GLU", "Định lượng Glucose [Máu]", "Xét nghiệm"),
            ("xn2", "XN_URE", "Định lượng Ure [Máu]", "Xét nghiệm"),
            ("xn3", "XN_CRE", "Định lượng Creatinin [Máu]", "Xét nghiệm"),
            ("xn4", "XN_AST", "Đo hoạt độ AST (GOT) [Máu]", "Xét nghiệm")
        });

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Thí sinh 1", "TS01", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)"),
            new CandidateInput("Thí sinh 2", "TS02", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_ClsSpread", new DateOnly(2026, 10, 29), candidates);
        Assert.Equal(2, scenarios.Count);

        foreach (var sc in scenarios)
        {
            Assert.DoesNotContain(sc.OrderedServices, s => s.Code is "SA_OLD" or "XQ_OLD" or "KB_NOI" or "PT_RT");
            Assert.NotNull(sc.ServiceChange);
            Assert.DoesNotContain(sc.ServiceChange.CanceledService.Code, new[] { "SA_OLD", "XQ_OLD", "KB_NOI", "PT_RT" });
            Assert.DoesNotContain(sc.ServiceChange.NewService.Code, new[] { "SA_OLD", "XQ_OLD", "KB_NOI", "PT_RT" });
        }

        // Consecutive candidates in the same batch must receive distinct CT/MRI and distinct Siêu âm / X-quang
        var sc1Codes = scenarios[0].OrderedServices.Select(s => s.Code).ToHashSet();
        var sc2Codes = scenarios[1].OrderedServices.Select(s => s.Code).ToHashSet();
        Assert.NotEqual(
            scenarios[0].OrderedServices.First(s => s.Code.StartsWith("CT_")).Code,
            scenarios[1].OrderedServices.First(s => s.Code.StartsWith("CT_")).Code);
    }

    [Fact]
    public void Transfer_inpatient_department_strictly_picks_from_7_allowed_departments_and_excludes_current_department()
    {
        var rng = new Random(42);
        var allowedList = ExamScenarioAllocator.AllowedInpatientTransferDepartments;

        Assert.Equal(7, allowedList.Count);
        Assert.Contains("Khoa Nội", allowedList);
        Assert.Contains("Khoa Ngoại tổng hợp", allowedList);
        Assert.Contains("Khoa Chấn thương chỉnh hình", allowedList);
        Assert.Contains("Khoa Thần kinh", allowedList);
        Assert.Contains("Gây mê hồi sức", allowedList);
        Assert.Contains("Hồi sức tích cực", allowedList);
        Assert.Contains("Khoa Tim mạch", allowedList);

        // Test with different department names
        var testCurrentDepartments = new[]
        {
            "Khoa Nội",
            "Khoa Ngoại tổng hợp",
            "Khoa Chấn thương chỉnh hình",
            "Khoa Thần kinh",
            "Khoa Gây mê hồi sức",
            "Khoa Hồi sức tích cực",
            "Khoa Tim mạch",
            "Nội khoa",
            "Ngoại tổng hợp"
        };

        foreach (var curDept in testCurrentDepartments)
        {
            for (var i = 0; i < 50; i++)
            {
                var target = ExamScenarioAllocator.PickTargetTransferDepartment(curDept, rng);
                Assert.Contains(target, allowedList);
                Assert.False(
                    ExamScenarioAllocator.IsSameInpatientDepartment(curDept, target),
                    $"Khoa chuyển đến '{target}' không được trùng với khoa đang ở '{curDept}'");
            }
        }
    }

    [Fact]
    public async Task Inpatient_transfer_and_inventory_check_output_exact_actual_stock_in_answer_keys()
    {
        await SetupSyncedCatalogAsync();

        // Setup distinct warehouses for Khoa Nội and Khoa Ngoại
        await _database.SaveDepartmentConfigurationAsync("NOI", "Khoa Nội", "KHO-NOI", "Kho trực Nội");
        await _database.SaveDepartmentConfigurationAsync("NGOAI", "Khoa Ngoại tổng hợp", "KHO-NGOAI", "Kho trực Ngoại");

        await _database.ReplaceSnapshotDrugsAsync(new[]
        {
            new SnapshotDrugRow("d_noi_1", "MED_NOI_1", "Thuốc Nội A", "Viên", "KHO-NOI", "Kho trực Nội", "Viện phí", 350, "NOI", "Khoa Nội"),
            new SnapshotDrugRow("d_noi_2", "MED_NOI_2", "Thuốc Nội B", "Viên", "KHO-NOI", "Kho trực Nội", "Viện phí", 200, "NOI", "Khoa Nội"),
            new SnapshotDrugRow("d_ngoai_1", "SUPP_NGOAI_1", "Chỉ Ngoại C", "Tép", "KHO-NGOAI", "Kho trực Ngoại", "Viện phí", 45, "NGOAI", "Khoa Ngoại tổng hợp")
        });

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Nguyễn Thí Sinh", "TS01", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_TransferAndInvStock", new DateOnly(2026, 10, 30), candidates);
        var sc = scenarios.Single();

        // 1. Requirement 1: Chuyển khoa nội trú
        Assert.Contains(sc.TargetTransferDepartment, ExamScenarioAllocator.AllowedInpatientTransferDepartments);
        Assert.False(ExamScenarioAllocator.IsSameInpatientDepartment(sc.DepartmentName, sc.TargetTransferDepartment));

        var qTransfer = sc.Questions.FirstOrDefault(q => q.ActionCode == "CK_CHUYEN_KHOA");
        Assert.NotNull(qTransfer);
        Assert.NotNull(sc.TargetTransferDepartment);
        Assert.Contains(sc.TargetTransferDepartment!, qTransfer.Instruction);
        Assert.Contains($"từ khoa {sc.DepartmentName}", qTransfer.Instruction);

        // 2. Requirement 2: Check tồn kho dược/VTYT tại kho của khoa và ghi rõ số lượng tồn trong đáp án
        var qInv = sc.Questions.FirstOrDefault(q => q.ActionCode == "TK_KIEM_TON_KHO");
        Assert.NotNull(qInv);
        Assert.Equal("KHO-NOI", sc.InventoryCheckDrug?.WarehouseCode);
        Assert.True(sc.InventoryCheckDrug?.Quantity >= 200);

        // Verify Answer Key text in WordExamWriter and ExamPackageService
        var wordSummaryTransfer = WordExamWriterTestHelper(sc, qTransfer);
        Assert.Contains(sc.TargetTransferDepartment, wordSummaryTransfer);

        var wordSummaryInv = WordExamWriterTestHelper(sc, qInv);
        Assert.True(wordSummaryInv.Contains("ĐÁP ÁN TỒN KHO CUỐI KỲ:") || wordSummaryInv.Contains("ĐÁP ÁN TỒN KHO HIỆN TẠI:") || wordSummaryInv.Contains("ĐÁP ÁN TỒN KHO"));
        Assert.Contains(sc.InventoryCheckDrug!.Name, wordSummaryInv);

        var clientAnswer = ExamPackageService.BuildExpectedAnswer(sc, qInv);
        Assert.True(clientAnswer.Contains("TỒN CUỐI KỲ CHUẨN:") || clientAnswer.Contains("TỒN KHO HIỆN TẠI CHUẨN:") || clientAnswer.Contains("TỒN KHO CHUẨN:"));
        Assert.Contains(sc.InventoryCheckDrug.Quantity.ToString(), clientAnswer);
    }

    [Fact]
    public async Task Drug_order_strictly_checks_kho_le_thuoc_and_kho_le_vat_tu_stock_and_deducts_cumulatively()
    {
        await SetupSyncedCatalogAsync();

        // Seed central warehouses and department warehouse
        await _database.ReplaceSnapshotDrugsAsync(new[]
        {
            // Kho lẻ thuốc nội trú (Thuốc)
            new SnapshotDrugRow("d_klt_1", "MED_KLT_1", "Thuốc Kháng Sinh 1", "Viên", "KL_THUOC", "Kho lẻ thuốc nội trú", "BHYT", 3), // Only 3 in stock!
            new SnapshotDrugRow("d_klt_2", "MED_KLT_2", "Thuốc Hạ Sốt 2", "Viên", "KL_THUOC", "Kho lẻ thuốc nội trú", "BHYT", 100),
            new SnapshotDrugRow("d_klt_3", "MED_KLT_3", "Thuốc Tiêu Hóa 3", "Viên", "KL_THUOC", "Kho lẻ thuốc nội trú", "BHYT", 100),

            // Kho lẻ vật tư (VTYT)
            new SnapshotDrugRow("d_klv_1", "VTYT_KLV_1", "Bơm tiêm 5ml", "Cái", "KL_VATTU", "Kho lẻ vật tư", "Viện phí", 4), // Only 4 in stock!
            new SnapshotDrugRow("d_klv_2", "VTYT_KLV_2", "Dây truyền dịch", "Bộ", "KL_VATTU", "Kho lẻ vật tư", "Viện phí", 100),

            // Kho của khoa (targetWh)
            new SnapshotDrugRow("d_noi_1", "MED_KLT_1", "Thuốc Kháng Sinh 1", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 50),
            new SnapshotDrugRow("d_noi_2", "MED_KLT_2", "Thuốc Hạ Sốt 2", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 50),
            new SnapshotDrugRow("d_noi_3", "MED_KLT_3", "Thuốc Tiêu Hóa 3", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 50),
            new SnapshotDrugRow("d_noi_4", "VTYT_KLV_1", "Bơm tiêm 5ml", "Cái", "KHO-NOI", "Kho trực Nội", "Viện phí", 50),
            new SnapshotDrugRow("d_noi_5", "VTYT_KLV_2", "Dây truyền dịch", "Bộ", "KHO-NOI", "Kho trực Nội", "Viện phí", 50)
        });

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Thí sinh 1", "TS01", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)"),
            new CandidateInput("Thí sinh 2", "TS02", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_CentralStockCheck", new DateOnly(2026, 10, 30), candidates);
        Assert.Equal(2, scenarios.Count);

        // Total allocated across candidates must not exceed Kho lẻ thuốc / Kho lẻ vật tư
        var totalKlt1 = scenarios.SelectMany(s => s.OrderedDrugs).Where(d => d.Code == "MED_KLT_1").Sum(d => d.Quantity);
        var totalKlv1 = scenarios.SelectMany(s => s.OrderedDrugs).Where(d => d.Code == "VTYT_KLV_1").Sum(d => d.Quantity);

        Assert.True(totalKlt1 <= 3, $"MED_KLT_1 không được vượt quá tồn kho Kho lẻ thuốc nội trú (Đã xuất: {totalKlt1}, Tồn: 3)");
        Assert.True(totalKlv1 <= 4, $"VTYT_KLV_1 không được vượt quá tồn kho Kho lẻ vật tư (Đã xuất: {totalKlv1}, Tồn: 4)");
    }

    [Fact]
    public void IsMedicalSupplyItem_strictly_distinguishes_by_ItemType_from_DM_LoaiDuoc()
    {
        // 1. Items with explicit ItemType from eHospital DM_LoaiDuoc.LoaiVatTu_Id
        var supplyRow = new SnapshotDrugRow("d1", "V01", "Catheter tĩnh mạch", "Cái", "KHO01", "Kho Ngoại", "Viện phí", 10, ItemType: "VTYT");
        Assert.True(ExamScenarioAllocator.IsMedicalSupplyItem(supplyRow));

        var drugRow = new SnapshotDrugRow("d2", "T01", "Paracetamol 500mg", "Viên", "KHO01", "Kho Ngoại", "BHYT", 10, ItemType: "THUOC");
        Assert.False(ExamScenarioAllocator.IsMedicalSupplyItem(drugRow));

        // Even if drug has a generic or confusing name, ItemType takes precedence
        var drugSpecialName = new SnapshotDrugRow("d3", "T02", "Dung dịch sát khuẩn xịt", "Chai", "KHO01", "Kho Ngoại", "BHYT", 10, ItemType: "THUOC");
        Assert.False(ExamScenarioAllocator.IsMedicalSupplyItem(drugSpecialName));

        var chemicalRow = new SnapshotDrugRow("d4", "H01", "Hóa chất X-Quang", "Can", "KHO01", "Kho Ngoại", "Viện phí", 5, ItemType: "HOACHAT");
        Assert.True(ExamScenarioAllocator.IsMedicalSupplyItem(chemicalRow));

        var ttbytRow = new SnapshotDrugRow("d5", "N01", "Trang thiết bị y tế", "Bộ", "KHO01", "Kho Ngoại", "Viện phí", 5, ItemType: "TTBYT");
        Assert.True(ExamScenarioAllocator.IsMedicalSupplyItem(ttbytRow));

        var vaccineRow = new SnapshotDrugRow("d6", "I01", "Vắc xin Uốn ván", "Lọ", "KHO01", "Kho Ngoại", "Viện phí", 5, ItemType: "VACCINE");
        Assert.False(ExamScenarioAllocator.IsMedicalSupplyItem(vaccineRow));

        // 2. Legacy fallback when ItemType is null
        var fallbackSupply = new SnapshotDrugRow("d7", "F01", "Bơm tiêm 10ml", "Cái", "KHO01", "Kho Ngoại", "Viện phí", 10, ItemType: null);
        Assert.True(ExamScenarioAllocator.IsMedicalSupplyItem(fallbackSupply));

        var fallbackDrug = new SnapshotDrugRow("d8", "F02", "Cefotaxim 1g", "Lọ", "KHO01", "Kho Ngoại", "BHYT", 10, ItemType: null);
        Assert.False(ExamScenarioAllocator.IsMedicalSupplyItem(fallbackDrug));
    }

    [Fact]
    public async Task Drug_order_allocation_strictly_respects_ItemType_for_drug_and_supply_slots()
    {
        await SetupSyncedCatalogAsync();

        // Seed with explicit ItemType
        await _database.ReplaceSnapshotDrugsAsync(new[]
        {
            // Kho lẻ thuốc nội trú (Thuốc)
            new SnapshotDrugRow("klt_1", "THUOC_01", "Kháng sinh A", "Lọ", "KL_THUOC", "Kho lẻ thuốc nội trú", "BHYT", 100, ItemType: "THUOC"),
            new SnapshotDrugRow("klt_2", "THUOC_02", "Kháng sinh B", "Viên", "KL_THUOC", "Kho lẻ thuốc nội trú", "BHYT", 100, ItemType: "THUOC"),
            new SnapshotDrugRow("klt_3", "THUOC_03", "Hạ sốt C", "Viên", "KL_THUOC", "Kho lẻ thuốc nội trú", "BHYT", 100, ItemType: "THUOC"),

            // Kho lẻ vật tư (VTYT)
            new SnapshotDrugRow("klv_1", "VTYT_01", "Dụng cụ tiêm", "Cái", "KL_VATTU", "Kho lẻ vật tư", "Viện phí", 100, ItemType: "VTYT"),
            new SnapshotDrugRow("klv_2", "VTYT_02", "Dụng cụ truyền", "Bộ", "KL_VATTU", "Kho lẻ vật tư", "Viện phí", 100, ItemType: "VTYT"),

            // Kho tại khoa
            new SnapshotDrugRow("noi_1", "THUOC_01", "Kháng sinh A", "Lọ", "KHO-NOI", "Kho trực Nội", "BHYT", 100, ItemType: "THUOC"),
            new SnapshotDrugRow("noi_2", "THUOC_02", "Kháng sinh B", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 100, ItemType: "THUOC"),
            new SnapshotDrugRow("noi_3", "THUOC_03", "Hạ sốt C", "Viên", "KHO-NOI", "Kho trực Nội", "BHYT", 100, ItemType: "THUOC"),
            new SnapshotDrugRow("noi_4", "VTYT_01", "Dụng cụ tiêm", "Cái", "KHO-NOI", "Kho trực Nội", "Viện phí", 100, ItemType: "VTYT"),
            new SnapshotDrugRow("noi_5", "VTYT_02", "Dụng cụ truyền", "Bộ", "KHO-NOI", "Kho trực Nội", "Viện phí", 100, ItemType: "VTYT")
        });

        // Verify SQLite persisted ItemType
        var storedDrugs = await _database.GetWarehouseDrugsAsync("KHO-NOI");
        Assert.All(storedDrugs.Where(d => d.DrugCode.StartsWith("THUOC")), d => Assert.Equal("THUOC", d.ItemType));
        Assert.All(storedDrugs.Where(d => d.DrugCode.StartsWith("VTYT")), d => Assert.Equal("VTYT", d.ItemType));

        var allocator = new ExamScenarioAllocator(_database);
        var candidates = new[]
        {
            new CandidateInput("Thí sinh Test ItemType", "TS_ITEMTYPE", "Khoa Nội", "Đề Điều dưỡng nội trú chuẩn (8 câu)")
        };

        var scenarios = await allocator.AllocateBatchScenariosAsync("DotThi_ItemType", new DateOnly(2026, 10, 30), candidates);
        Assert.Single(scenarios);
        var sc = scenarios[0];

        // Standard inpatient template: 5 items total (3 Thuốc + 2 VTYT)
        Assert.Equal(5, sc.OrderedDrugs.Count);

        // First 3 items must be Thuốc
        for (int i = 0; i < 3; i++)
        {
            Assert.StartsWith("THUOC", sc.OrderedDrugs[i].Code);
        }

        // Remaining 2 items must be VTYT
        for (int i = 3; i < 5; i++)
        {
            Assert.StartsWith("VTYT", sc.OrderedDrugs[i].Code);
        }
    }

    private static string WordExamWriterTestHelper(ExamScenario s, ScenarioQuestion q)
    {
        // Invoke BuildQuestionAnswerSummary via private reflection or test assertion
        var method = typeof(WordExamWriter).GetMethod("BuildQuestionAnswerSummary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (string)method!.Invoke(null, new object[] { s, q })!;
    }
}
