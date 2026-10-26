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
    public async Task Inventory_check_question_uses_fixed_february_2026_date_range()
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
        Assert.Contains("từ ngày 01/02/2026 đến ngày 28/02/2026", qInv.Instruction);
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
}
