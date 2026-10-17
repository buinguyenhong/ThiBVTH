using ExamGenerator.Domain;
using ExamGenerator.Infrastructure;
using Xunit;

namespace ExamGenerator.Tests;

public sealed class ExamClientPackageAndGradingTests
{
    private static ExamScenario CreateSampleScenario(int durationMinutes = 45)
    {
        var patient = new ScenarioPatient(
            "BN001",
            "Trần Thị B",
            new DateOnly(1990, 5, 12),
            36,
            "Nữ",
            "Buôn Ma Thuột",
            "DN4660000000001",
            new InsurancePeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "66001",
            "Viêm dạ dày",
            PatientPaymentType.Insurance
        );

        var questions = new List<ScenarioQuestion>
        {
            new(1, "LT_BAN_PHIEU_KHAM", "Tiếp nhận bệnh nhân", "Hướng dẫn tiếp nhận trên HIS...", 2.0),
            new(2, "TK_KIEM_TON_KHO", "Kiểm tra tồn kho", "Tra cứu tồn kho dược...", 3.0),
            new(3, "LT_THU_TAM_UNG_NOI_TRU", "Thu tạm ứng viện phí", "Thu tạm ứng cho bệnh nhân...", 5.0)
        };

        return new ExamScenario(
            ScenarioId: "SCENARIO_12345",
            BatchName: "TuyenDung_KhoaDuoc",
            ExamDate: new DateOnly(2026, 10, 16),
            CandidateId: "SBD_99",
            CandidateName: "Lê Văn C",
            DepartmentCode: "KDUOC",
            DepartmentName: "Khoa Dược",
            TemplateId: "tpl-duoc",
            TemplateName: "Mẫu Dược sĩ Lâm sàng",
            HisUserCode: "USER_DUOC99",
            HisUserName: "User Dược 99",
            Patient: patient,
            Questions: questions,
            OrderedServices: Array.Empty<ScenarioService>(),
            OrderedDrugs: Array.Empty<ScenarioDrug>(),
            ServiceChange: null,
            DrugReturn: null,
            RequiresDirectReception: true,
            RequiresExamSetupSql: false,
            ExamDurationMinutes: durationMinutes
        );
    }

    [Fact]
    public void ExamPackage_PreservesExamDurationAndQuestions_WithoutExposingAnswers()
    {
        var scenario = CreateSampleScenario(45);
        var pkg = ExamPackageService.CreatePackage(scenario, "secret_proctor_123");

        Assert.Equal("SBD_99", pkg.CandidateId);
        Assert.Equal("Lê Văn C", pkg.CandidateName);
        Assert.Equal(45, pkg.DurationMinutes);
        Assert.Equal(3, pkg.Questions.Count);

        // Verify Question 2 is FillIn (TK_KIEM_TON_KHO)
        var q2 = pkg.Questions.First(q => q.ActionCode == "TK_KIEM_TON_KHO");
        Assert.True(q2.IsFillIn);
        Assert.NotNull(q2.FillInPrompt);

        // Verify Question 1 is not FillIn (Practical HIS)
        var q1 = pkg.Questions.First(q => q.ActionCode == "LT_BAN_PHIEU_KHAM");
        Assert.False(q1.IsFillIn);

        // Verify supervisor password verification
        Assert.True(ExamPackageService.VerifySupervisorPassword("secret_proctor_123", pkg.SupervisorPasswordHash));
        Assert.True(ExamPackageService.VerifySupervisorPassword("bvth@2026", pkg.SupervisorPasswordHash));
        Assert.False(ExamPackageService.VerifySupervisorPassword("wrong_pass", pkg.SupervisorPasswordHash));
    }

    [Fact]
    public void ExamPackage_SerializationRoundtrip_Succeeds()
    {
        var scenario = CreateSampleScenario(35);
        var pkg = ExamPackageService.CreatePackage(scenario);

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_exam_{Guid.NewGuid():N}.exam_pkg");
        try
        {
            ExamPackageService.SavePackage(pkg, tempPath);
            Assert.True(File.Exists(tempPath));

            var loaded = ExamPackageService.LoadPackage(tempPath);
            Assert.Equal(pkg.ExamId, loaded.ExamId);
            Assert.Equal(pkg.CandidateId, loaded.CandidateId);
            Assert.Equal(pkg.CandidateName, loaded.CandidateName);
            Assert.Equal(pkg.DurationMinutes, loaded.DurationMinutes);
            Assert.Equal(pkg.Questions.Count, loaded.Questions.Count);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void SubmissionPackage_SerializationRoundtrip_Succeeds()
    {
        var sub = new ClientSubmissionPackage
        {
            SubmissionId = Guid.NewGuid().ToString("N"),
            ExamId = "SCENARIO_12345",
            CandidateId = "SBD_99",
            CandidateName = "Lê Văn C",
            PaperCode = "SBD_99_KDUOC",
            HisUserCode = "USER_DUOC99",
            DepartmentName = "Khoa Dược",
            ExamDate = new DateOnly(2026, 10, 16),
            StartedAt = DateTime.Now.AddMinutes(-40),
            SubmittedAt = DateTime.Now,
            OriginalDurationMinutes = 35,
            TotalExtraMinutes = 5,
            ExtraTimeIncidents = new List<ExtraTimeIncidentLog>
            {
                new() { ExtraMinutes = 5, SupervisorNote = "Máy trạm đứt cáp mạng LAN 5 phút" }
            },
            Answers = new List<ClientQuestionAnswer>
            {
                new() { OrderIndex = 1, ActionCode = "LT_BAN_PHIEU_KHAM", FillInValue = null },
                new() { OrderIndex = 2, ActionCode = "TK_KIEM_TON_KHO", FillInValue = "150 viên" },
                new() { OrderIndex = 3, ActionCode = "LT_THU_TAM_UNG_NOI_TRU", FillInValue = "Đã thu 1.500.000đ" }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_sub_{Guid.NewGuid():N}.sub_pkg");
        try
        {
            ExamPackageService.SaveSubmission(sub, tempPath);
            Assert.True(File.Exists(tempPath));

            var loaded = ExamPackageService.LoadSubmission(tempPath);
            Assert.Equal(sub.SubmissionId, loaded.SubmissionId);
            Assert.Equal(sub.CandidateId, loaded.CandidateId);
            Assert.Equal(sub.TotalExtraMinutes, loaded.TotalExtraMinutes);
            Assert.Single(loaded.ExtraTimeIncidents);
            Assert.Equal("Máy trạm đứt cáp mạng LAN 5 phút", loaded.ExtraTimeIncidents[0].SupervisorNote);
            Assert.Equal(3, loaded.Answers.Count);
            Assert.Equal("150 viên", loaded.Answers[1].FillInValue);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task SqliteDatabase_CandidateGrading_PersistenceAndRetrieval()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_grading_dir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var storage = new LocalApplicationStorage(tempDir);
        var db = new SqliteDatabase(storage);
        await db.InitializeAsync();

        var submissionId = Guid.NewGuid().ToString("N");
        var record = new CandidateGradingRecord
        {
            SubmissionId = submissionId,
            CandidateId = "SBD_88",
            CandidateName = "Hoàng Thị M",
            PaperCode = "SBD_88_KNOI",
            DepartmentName = "Khoa Nội",
            HisUserCode = "USER_NOI88",
            StartedAt = DateTime.Now.AddMinutes(-30),
            SubmittedAt = DateTime.Now,
            TotalExtraMinutes = 10,
            TotalScore = 8.5,
            MaxTotalScore = 10.0,
            ExaminerName = "BS. Phạm Văn Giám Khảo",
            GeneralRemark = "Thực hiện đúng quy trình, câu 2 tính tồn kho chính xác",
            GradedAt = DateTime.Now,
            Items = new List<ExamGradingItem>
            {
                new()
                {
                    OrderIndex = 1,
                    ActionCode = "TK_TIEP_NHAN",
                    Title = "Tiếp nhận bệnh nhân",
                    MaxScore = 4.0,
                    ExpectedAnswer = "Tiếp nhận 2 BN",
                    CandidateAnswer = "Đã tiếp nhận 2 lượt",
                    ScoreAwarded = 3.5,
                    ExaminerRemark = "Nhập thiếu dấu địa chỉ"
                },
                new()
                {
                    OrderIndex = 2,
                    ActionCode = "TK_KIEM_TON_KHO",
                    Title = "Kiểm tồn kho",
                    MaxScore = 6.0,
                    ExpectedAnswer = "Tồn cuối kỳ = 250",
                    CandidateAnswer = "250",
                    ScoreAwarded = 5.0,
                    ExaminerRemark = "Chính xác tuyệt đối"
                }
            }
        };

        await db.SaveCandidateGradingAsync(record);

        var retrieved = await db.GetCandidateGradingAsync(submissionId);
        Assert.NotNull(retrieved);
        Assert.Equal("SBD_88", retrieved.CandidateId);
        Assert.Equal("Hoàng Thị M", retrieved.CandidateName);
        Assert.Equal(8.5, retrieved.TotalScore);
        Assert.Equal(10, retrieved.TotalExtraMinutes);
        Assert.Equal("BS. Phạm Văn Giám Khảo", retrieved.ExaminerName);
        Assert.Equal(2, retrieved.Items.Count);
        Assert.Equal(3.5, retrieved.Items[0].ScoreAwarded);
        Assert.Equal("Nhập thiếu dấu địa chỉ", retrieved.Items[0].ExaminerRemark);

        var all = await db.GetAllCandidateGradingsAsync();
        Assert.Contains(all, x => x.SubmissionId == submissionId);

        try
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
        catch
        {
            // Ignore temp cleanup failure
        }
    }

    [Fact]
    public void GenerateSamplePackages_ForExamClientTesting()
    {
        var pkg1 = new ClientExamPackage
        {
            ExamId = Guid.NewGuid().ToString("N"),
            CandidateId = "101",
            CandidateName = "Nguyễn Văn An",
            DepartmentName = "Khoa Khám Bệnh",
            HisUserCode = "USER_THI01",
            DurationMinutes = 30,
            Title = "BÀI THI TÁC NGHIỆP TRÊN HỆ THỐNG HIS - KHOA KHÁM BỆNH",
            SupervisorPasswordHash = ExamPackageService.ComputeSha256("bvth@2026"),
            Questions = new List<ClientExamQuestion>
            {
                new()
                {
                    OrderIndex = 1,
                    ActionCode = "LT_BAN_PHIEU_KHAM",
                    Title = "Bán phiếu khám bệnh và tiếp nhận",
                    Instruction = "Thực hiện tiếp nhận 02 bệnh nhân trên phần mềm HIS bằng tài khoản được cấp:\n- BN 1 (Viện phí): Lê Thị Mai (SN: 1985, Đ/c: TP. Buôn Ma Thuột) - PK Nội tổng quát\n- BN 2 (BHYT): Trần Văn Hùng (Mã thẻ: GD4660000000001) - PK Ngoại tổng hợp",
                    MaxScore = 3.0,
                    IsFillIn = false
                },
                new()
                {
                    OrderIndex = 2,
                    ActionCode = "TK_KIEM_TON_KHO",
                    Title = "Kiểm tra tồn kho dược theo kỳ",
                    Instruction = "Tra cứu báo cáo tồn kho dược trên phần mềm HIS theo thông số bên dưới và điền số lượng tồn cuối kỳ vào ô trả lời.",
                    MaxScore = 3.0,
                    IsFillIn = true,
                    FillInPrompt = "Nhập số lượng tồn kho cuối kỳ (sau khi tra cứu trên HIS):",
                    DetailText = "• Tên thuốc: Paracetamol 500mg (Mã: DUOC_001)\n• Kho dược: Kho Chẵn Bệnh Viện\n• Nguồn: BHYT\n• Kỳ kiểm tra: Từ 01/02/2026 đến hết ngày 15/02/2026"
                },
                new()
                {
                    OrderIndex = 3,
                    ActionCode = "LT_THU_TAM_UNG_NOI_TRU",
                    Title = "Thu tạm ứng viện phí",
                    Instruction = "Thực hiện thu tạm ứng viện phí trên HIS cho 02 bệnh nhân:\n• BN nội trú: Phạm Văn Nam (Số BA: BA2026012, Khoa: Nội tổng hợp, Giường: G01) - Số tiền: 1.500.000 đ\n• BN ngoại trú: Hoàng Thị Lan (Số BA: BA2026099) - Số tiền: 500.000 đ",
                    MaxScore = 4.0,
                    IsFillIn = false
                }
            }
        };

        var pkg2 = new ClientExamPackage
        {
            ExamId = Guid.NewGuid().ToString("N"),
            CandidateId = "102",
            CandidateName = "Trần Thị Bích",
            DepartmentName = "Khoa Dược",
            HisUserCode = "USER_THI02",
            DurationMinutes = 45,
            Title = "BÀI THI TÁC NGHIỆP TRÊN HỆ THỐNG HIS - KHOA DƯỢC",
            SupervisorPasswordHash = ExamPackageService.ComputeSha256("bvth@2026"),
            Questions = new List<ClientExamQuestion>
            {
                new()
                {
                    OrderIndex = 1,
                    ActionCode = "TK_KIEM_TON_KHO",
                    Title = "Kiểm tra số lượng tồn kho dược cơ số",
                    Instruction = "Đăng nhập HIS tra cứu số lượng tồn cuối kỳ của mặt hàng theo mốc thời gian quy định và ghi kết quả vào ô trả lời.",
                    MaxScore = 5.0,
                    IsFillIn = true,
                    FillInPrompt = "Số lượng tồn kho cuối kỳ:",
                    DetailText = "• Tên thuốc: Cefotaxim 1g (Mã: DUOC_CEF1G)\n• Kho dược: Kho Lẻ Bệnh Viện\n• Nguồn: Viện phí\n• Kỳ kiểm tra: Từ 16/01/2026 đến hết ngày 31/01/2026"
                },
                new()
                {
                    OrderIndex = 2,
                    ActionCode = "YL_CHI_DINH_THUOC_VTYT",
                    Title = "Lên y lệnh thuốc & vật tư y tế",
                    Instruction = "Thực hiện lên y lệnh thuốc trên HIS cho bệnh nhân Nguyễn Văn A (Mã BN: BN000123) theo danh sách chỉ định.",
                    MaxScore = 5.0,
                    IsFillIn = false,
                    DetailText = "1. [DUOC_001] Paracetamol 500mg - SL: 10 viên (BHYT)\n2. [VTYT_002] Bơm tiêm 5ml - SL: 2 cái (Viện phí)"
                }
            }
        };

        var pkg3 = new ClientExamPackage
        {
            ExamId = Guid.NewGuid().ToString("N"),
            CandidateId = "103",
            CandidateName = "Lê Hoàng Cường",
            DepartmentName = "Lễ Tân - Thu Phí",
            HisUserCode = "USER_THI03",
            DurationMinutes = 35,
            Title = "BÀI THI TÁC NGHIỆP TRÊN HỆ THỐNG HIS - LỄ TÂN THU PHÍ",
            SupervisorPasswordHash = ExamPackageService.ComputeSha256("bvth@2026"),
            Questions = new List<ClientExamQuestion>
            {
                new()
                {
                    OrderIndex = 1,
                    ActionCode = "LT_TAO_THE_TAM_BHYT",
                    Title = "Cấp thẻ tạm BHYT cho trẻ em dưới 6 tuổi",
                    Instruction = "Thực hiện cấp mã thẻ tạm BHYT trên phần mềm HIS và nhập mã thẻ tạm đã tạo vào ô bên dưới:\n- Bé gái, sinh trước ngày thi 1 tháng\n- Loại đối tượng: Trẻ em dưới 6 tuổi (Mã TE1...)\n- Hạn thẻ: 30 ngày kể từ ngày thi",
                    MaxScore = 5.0,
                    IsFillIn = true,
                    FillInPrompt = "Nhập mã thẻ tạm BHYT đã tạo trên HIS (VD: TE1...):"
                },
                new()
                {
                    OrderIndex = 2,
                    ActionCode = "LT_TRA_CUU_THONG_TIN",
                    Title = "Tra cứu thông tin bệnh nhân nội trú",
                    Instruction = "Tra cứu thông tin bệnh nhân nội trú trên HIS và nhập kết quả (Số bệnh án, Khoa, Giường, Ngày vào viện) vào ô trả lời bên dưới.",
                    MaxScore = 5.0,
                    IsFillIn = true,
                    FillInPrompt = "Nhập kết quả tra cứu (Số BA, Giường, Ngày vào viện,...):",
                    DetailText = "• Tên bệnh nhân cần tra cứu: Võ Văn Hậu (Mã BN: BN009876)"
                }
            }
        };

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "ExamGenerator.Desktop.sln")) && !Directory.Exists(Path.Combine(current.FullName, "desktop")))
        {
            current = current.Parent;
        }
        var repoRoot = current?.FullName ?? AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(repoRoot, "ExamGenerator.Desktop.sln")))
        {
            repoRoot = current!.Parent?.FullName ?? repoRoot;
        }

        var targetDirs = new[]
        {
            Path.Combine(repoRoot, "exams"),
            Path.Combine(repoRoot, "desktop", "publish", "ExamClient", "exams"),
            Path.Combine(repoRoot, "desktop", "publish", "ExamClient-Standalone", "exams"),
            Path.Combine(repoRoot, "desktop", "publish", "ExamOperationsDesktop", "exams"),
            Path.Combine(repoRoot, "desktop", "publish", "ExamOperationsDesktop-Standalone", "exams"),
            Path.Combine(repoRoot, "desktop", "src", "ExamGenerator.Client", "bin", "Debug", "net8.0-windows", "exams")
        };

        foreach (var dir in targetDirs)
        {
            Directory.CreateDirectory(dir);
            ExamPackageService.SavePackage(pkg1, Path.Combine(dir, "101_KKHAM_001.exam_pkg"));
            ExamPackageService.SavePackage(pkg2, Path.Combine(dir, "102_KDUOC_002.exam_pkg"));
            ExamPackageService.SavePackage(pkg3, Path.Combine(dir, "103_LTTP_003.exam_pkg"));
        }
    }

    [Theory]
    [InlineData("TK_KIEM_TON_KHO", "Kiểm tra tồn kho")]
    [InlineData("LT_TRA_CUU_THONG_TIN", "Tra cứu buồng giường")]
    [InlineData("LT_TAO_THE_TAM_BHYT", "Tạo mã thẻ tạm")]
    [InlineData("KB_BENH_AN_NGOAI_TRU", "Tạo bệnh án ngoại trú")]
    [InlineData("KB_CHI_DINH_NOI_TRU", "Chỉ định nội trú")]
    [InlineData("HC_TRA_CUU_THANH_TOAN", "Tra cứu thanh toán")]
    [InlineData("TC_TRA_CUU_BENH_SU", "Tra cứu bệnh sử")]
    [InlineData("NT_NHAN_BENH_KHOA", "Nhận bệnh vào khoa")]
    public void AllFillInQuestions_AreProperlyIdentifiedAndPrompted(string actionCode, string title)
    {
        Assert.True(ExamPackageService.IsQuestionFillIn(actionCode, null, title));
        var prompt = ExamPackageService.GetFillInPrompt(actionCode, null, title);
        Assert.NotNull(prompt);
        Assert.NotEmpty(prompt);
    }

    [Theory]
    [InlineData("CUSTOM_ACTION", "Ghi lại số vào viện của người bệnh sau khi tiếp nhận")]
    [InlineData("CUSTOM_ACTION", "Điền số bệnh án vào ô bên dưới")]
    [InlineData("CUSTOM_ACTION", "Nhập kết quả tra cứu tồn kho cuối kỳ")]
    public void CustomInstruction_WithFillKeywords_AutoIdentifiedAsFillIn(string actionCode, string instruction)
    {
        Assert.True(ExamPackageService.IsQuestionFillIn(actionCode, instruction, null));
        var prompt = ExamPackageService.GetFillInPrompt(actionCode, instruction, null);
        Assert.NotNull(prompt);
    }
}
