namespace ExamGenerator.Domain;

/// <summary>
/// Gói đề thi chuyển giao xuống máy trạm ExamClient cho thí sinh làm bài (.exam_pkg).
/// Không chứa đáp án chuẩn hoặc công thức chấm, đảm bảo tính bảo mật.
/// </summary>
public sealed class ClientExamPackage
{
    public string ExamId { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public string PaperCode { get; set; } = string.Empty;
    public string CandidateId { get; set; } = string.Empty;
    public string CandidateName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string HisUserCode { get; set; } = string.Empty;
    public string HisUserName { get; set; } = string.Empty;
    public DateOnly ExamDate { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public string Title { get; set; } = "BÀI THI TÁC NGHIỆP TRÊN HỆ THỐNG HIS";
    public List<ClientExamQuestion> Questions { get; set; } = new();
    public string SupervisorPasswordHash { get; set; } = string.Empty;
}

public sealed class ClientExamQuestion
{
    public int OrderIndex { get; set; }
    public string ActionCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
    public double MaxScore { get; set; }
    public bool IsFillIn { get; set; }
    public string? FillInPrompt { get; set; }
    public string? DetailText { get; set; }
}

/// <summary>
/// Nhật ký bù giờ sự cố do giám thị cấp phát
/// </summary>
public sealed class ExtraTimeIncidentLog
{
    public DateTime GrantedAt { get; set; } = DateTime.Now;
    public int ExtraMinutes { get; set; }
    public string SupervisorNote { get; set; } = string.Empty;
}

public sealed class ClientQuestionAnswer
{
    public int OrderIndex { get; set; }
    public string ActionCode { get; set; } = string.Empty;
    public string? FillInValue { get; set; }
    public DateTime? AnsweredAt { get; set; }
}

/// <summary>
/// Gói bài làm nộp từ ExamClient về máy chấm thi (.sub_pkg)
/// </summary>
public sealed class ClientSubmissionPackage
{
    public string SubmissionId { get; set; } = Guid.NewGuid().ToString("N");
    public string ExamId { get; set; } = string.Empty;
    public string CandidateId { get; set; } = string.Empty;
    public string CandidateName { get; set; } = string.Empty;
    public string PaperCode { get; set; } = string.Empty;
    public string HisUserCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public DateOnly ExamDate { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime SubmittedAt { get; set; }
    public int OriginalDurationMinutes { get; set; }
    public int TotalExtraMinutes { get; set; }
    public List<ExtraTimeIncidentLog> ExtraTimeIncidents { get; set; } = new();
    public List<ClientQuestionAnswer> Answers { get; set; } = new();
    public string MachineName { get; set; } = Environment.MachineName;
    public string Status { get; set; } = "Submitted";
}

/// <summary>
/// Mục đối soát chấm thi từng câu (Assisted Grading)
/// </summary>
public sealed class ExamGradingItem
{
    public int OrderIndex { get; set; }
    public string ActionCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
    public double MaxScore { get; set; }
    public string ExpectedAnswer { get; set; } = string.Empty;
    public string CandidateAnswer { get; set; } = string.Empty;
    public string HisAuditTrail { get; set; } = string.Empty;
    public double? ScoreAwarded { get; set; }
    public string ExaminerRemark { get; set; } = string.Empty;
}

/// <summary>
/// Biên bản chấm bài thi của 1 thí sinh
/// </summary>
public sealed class CandidateGradingRecord
{
    public string SubmissionId { get; set; } = string.Empty;
    public string CandidateId { get; set; } = string.Empty;
    public string CandidateName { get; set; } = string.Empty;
    public string PaperCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string HisUserCode { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime SubmittedAt { get; set; }
    public int TotalExtraMinutes { get; set; }
    public double TotalScore { get; set; }
    public double MaxTotalScore { get; set; }
    public string ExaminerName { get; set; } = string.Empty;
    public string GeneralRemark { get; set; } = string.Empty;
    public DateTime? GradedAt { get; set; }
    public List<ExamGradingItem> Items { get; set; } = new();
}
