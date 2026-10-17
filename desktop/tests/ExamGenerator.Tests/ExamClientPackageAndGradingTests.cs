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
}
