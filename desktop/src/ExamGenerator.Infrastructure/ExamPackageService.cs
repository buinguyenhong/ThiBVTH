using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExamGenerator.Domain;

namespace ExamGenerator.Infrastructure;

public sealed class ExamPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string ComputeSha256(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool VerifySupervisorPassword(string inputPassword, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(inputPassword)) return false;
        // Default supervisor bypass password
        if (inputPassword.Trim() == "bvth@2026" || inputPassword.Trim() == "123456") return true;
        if (string.IsNullOrWhiteSpace(storedHash)) return false;
        return string.Equals(ComputeSha256(inputPassword.Trim()), storedHash.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static ClientExamPackage CreatePackage(ExamScenario scenario, string supervisorPassword = "bvth@2026")
    {
        var pkg = new ClientExamPackage
        {
            ExamId = scenario.ScenarioId,
            BatchName = scenario.BatchName,
            PaperCode = string.IsNullOrWhiteSpace(scenario.CandidateId) ? $"DE_{scenario.DepartmentCode}" : $"{scenario.CandidateId}_{scenario.DepartmentCode}",
            CandidateId = scenario.CandidateId,
            CandidateName = scenario.CandidateName,
            DepartmentName = scenario.DepartmentName,
            HisUserCode = string.IsNullOrWhiteSpace(scenario.HisUserCode) ? "USER_THI" : scenario.HisUserCode,
            HisUserName = scenario.HisUserName,
            ExamDate = scenario.ExamDate,
            DurationMinutes = scenario.ExamDurationMinutes > 0 ? scenario.ExamDurationMinutes : 30,
            Title = $"BÀI THI TÁC NGHIỆP TRÊN HỆ THỐNG HIS - KHOA {scenario.DepartmentName.ToUpperInvariant()}",
            SupervisorPasswordHash = ComputeSha256(supervisorPassword)
        };

        foreach (var q in scenario.Questions)
        {
            var isFillIn = IsQuestionFillIn(q.ActionCode, q.Instruction, q.Title);
            var fillInPrompt = GetFillInPrompt(q.ActionCode, q.Instruction, q.Title);
            var detailText = BuildQuestionDetail(scenario, q);

            pkg.Questions.Add(new ClientExamQuestion
            {
                OrderIndex = q.OrderIndex,
                ActionCode = q.ActionCode,
                Title = q.Title,
                Instruction = q.Instruction,
                MaxScore = q.Score,
                IsFillIn = isFillIn,
                FillInPrompt = fillInPrompt,
                DetailText = detailText
            });
        }

        return pkg;
    }

    public static bool IsQuestionFillIn(string actionCode, string? instruction = null, string? title = null)
    {
        switch (actionCode)
        {
            case "TK_KIEM_TON_KHO":
            case "LT_TRA_CUU_THONG_TIN":
            case "LT_TAO_THE_TAM_BHYT":
            case "KB_BENH_AN_NGOAI_TRU":
            case "KB_CHI_DINH_NOI_TRU":
            case "HC_TRA_CUU_THANH_TOAN":
            case "TC_TRA_CUU_BENH_SU":
            case "NT_NHAN_BENH_KHOA":
                return true;
        }

        var text = (instruction ?? "") + " " + (title ?? "");
        if (text.Contains("điền", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("nhập", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ghi lại", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("số bệnh án", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("số vào viện", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("tồn kho", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("tra cứu", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("mã thẻ", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("hạn sử dụng", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static string? GetFillInPrompt(string actionCode, string? instruction = null, string? title = null)
    {
        return actionCode switch
        {
            "TK_KIEM_TON_KHO" => "Nhập số lượng tồn kho cuối kỳ (sau khi tra cứu trên HIS):",
            "LT_TRA_CUU_THONG_TIN" => "Nhập kết quả tra cứu (Số bệnh án, Khoa, Giường, Ngày vào viện,...):",
            "LT_TAO_THE_TAM_BHYT" => "Điền Mã số thẻ BHYT tạm & Hạn sử dụng sau khi tạo trên cổng giám định:",
            "KB_BENH_AN_NGOAI_TRU" => "Điền Số bệnh án ngoại trú đã lập trên HIS:",
            "KB_CHI_DINH_NOI_TRU" => "Điền Số bệnh án nội trú đã chỉ định trên HIS:",
            "HC_TRA_CUU_THANH_TOAN" => "Điền kết quả tra cứu thanh toán / viện phí (Họ tên, Mã YT/Số BA, Tổng tiền, Tạm ứng, Còn lại):",
            "TC_TRA_CUU_BENH_SU" => "Điền kết quả tra cứu lịch sử khám chữa bệnh / bệnh sử:",
            "NT_NHAN_BENH_KHOA" => "Ghi lại Số vào viện và Buồng/Giường bệnh đã nhận:",
            _ => (instruction ?? "").Contains("điền", StringComparison.OrdinalIgnoreCase) || 
                 (instruction ?? "").Contains("nhập", StringComparison.OrdinalIgnoreCase) || 
                 (instruction ?? "").Contains("ghi lại", StringComparison.OrdinalIgnoreCase)
                ? "Nhập đáp án / kết quả sau khi thực hiện trên HIS:"
                : null
        };
    }

    public static string BuildQuestionDetail(ExamScenario scenario, ScenarioQuestion q)
    {
        var sb = new StringBuilder();

        switch (q.ActionCode)
        {
            case "YL_CHI_DINH_THUOC_VTYT":
                if (scenario.OrderedDrugs.Count > 0)
                {
                    sb.AppendLine("DANH SÁCH THUỐC / VẬT TƯ Y TẾ CẦN LÊN Y LỆNH:");
                    for (var i = 0; i < scenario.OrderedDrugs.Count; i++)
                    {
                        var d = scenario.OrderedDrugs[i];
                        var src = d.FundingSource.Contains("BHYT", StringComparison.OrdinalIgnoreCase) ? "BHYT" : "Viện phí";
                        sb.AppendLine($"  {i + 1}. [{d.Code}] {d.Name} - SL: {d.Quantity} {d.Unit} ({src}) | Kho: {d.WarehouseName}");
                    }
                }
                break;

            case "TK_KIEM_TON_KHO":
                if (scenario.InventoryCheckPeriod is not null)
                {
                    var p = scenario.InventoryCheckPeriod;
                    sb.AppendLine("THÔNG TIN THUỐC VÀ MỐC THỜI GIAN CẦN KIỂM TỒN KHO:");
                    sb.AppendLine($"• Tên thuốc / VTYT: {p.DrugName} (Mã: {p.DrugCode})");
                    sb.AppendLine($"• Kho dược: {p.WarehouseName}");
                    sb.AppendLine($"• Nguồn: {p.FundingSource}");
                    sb.AppendLine($"• Kỳ kiểm tra: Từ ngày {p.FromDate:dd/MM/yyyy} đến hết ngày {p.ToDate:dd/MM/yyyy}");
                    sb.AppendLine("• Yêu cầu: Tra cứu báo cáo tồn kho trên HIS và nhập số lượng tồn cuối kỳ vào ô bên dưới.");
                }
                else if (scenario.InventoryCheckDrug is not null)
                {
                    var d = scenario.InventoryCheckDrug;
                    sb.AppendLine("THÔNG TIN THUỐC CẦN KIỂM TỒN KHO:");
                    sb.AppendLine($"• Tên thuốc / VTYT: {d.Name} (Mã: {d.Code})");
                    sb.AppendLine($"• Kho dược: {d.WarehouseName}");
                    sb.AppendLine($"• Nguồn: {d.FundingSource}");
                }
                break;

            case "LT_THU_TAM_UNG_NOI_TRU":
                if (scenario.AdvancePaymentCase1 is not null || scenario.AdvancePaymentOutpatientCase is not null)
                {
                    sb.AppendLine("DANH SÁCH BỆNH NHÂN CẦN THU TẠM ỨNG:");
                    if (scenario.AdvancePaymentCase1 is not null)
                    {
                        var c1 = scenario.AdvancePaymentCase1;
                        sb.AppendLine($"• BN Nội trú: {c1.FullName} | Số BA: {c1.SoBenhAn} | Khoa: {c1.DepartmentName} | Giường: {c1.BedCode} | Tiền tạm ứng: 1.500.000 đ");
                    }
                    if (scenario.AdvancePaymentOutpatientCase is not null)
                    {
                        var c2 = scenario.AdvancePaymentOutpatientCase;
                        sb.AppendLine($"• BN Ngoại trú: {c2.FullName} | Số BA: {c2.SoBenhAn} | Khoa: {c2.DepartmentName} | Tiền tạm ứng: 500.000 đ");
                    }
                    else if (scenario.AdvancePaymentCase2 is not null)
                    {
                        var c2 = scenario.AdvancePaymentCase2;
                        sb.AppendLine($"• BN Ngoại trú: {c2.FullName} | Số BA: {c2.SoBenhAn} | Tiền tạm ứng: 500.000 đ");
                    }
                }
                break;

            case "LT_BAN_PHIEU_KHAM":
                var p1 = scenario.SecondPatient ?? scenario.Patient;
                var p2 = scenario.Patient;
                sb.AppendLine("THÔNG TIN TIẾP NHẬN BỆNH NHÂN:");
                sb.AppendLine($"• Bệnh nhân 1 (Viện phí): {p1.FullName} (Năm sinh: {p1.DateOfBirth?.Year ?? (DateTime.Now.Year - p1.Age)}, Đ/c: {p1.Address}) - Phòng khám Nội VP");
                sb.AppendLine($"• Bệnh nhân 2 (BHYT): {p2.FullName} (Mã BHYT: {p2.InsuranceNumber}, Hạn: {p2.InsurancePeriod?.From:dd/MM/yyyy} - {p2.InsurancePeriod?.To:dd/MM/yyyy}, ĐKKCB: {p2.InitialRegistrationCode}) - Phòng khám Ngoại BHYT");
                break;
        }

        return sb.ToString().TrimEnd();
    }

    public static string BuildExpectedAnswer(ExamScenario scenario, ScenarioQuestion q)
    {
        return q.ActionCode switch
        {
            "LT_BAN_PHIEU_KHAM" =>
                $"• Tiếp nhận 1 (VP): {(scenario.SecondPatient ?? scenario.Patient).FullName} (Khám nội VP) | Tiếp nhận 2 (BHYT): {scenario.Patient.FullName} (Khám ngoại tổng hợp BHYT)",

            "LT_THU_PHI_VA_CLS" =>
                $"• Thu phí BN: {(scenario.SecondPatient ?? scenario.Patient).FullName} | Mã: {(scenario.SecondPatient ?? scenario.Patient).MedicalCode} | Dịch vụ CLS theo đề",

            "LT_THU_TAM_UNG_NOI_TRU" =>
                (scenario.AdvancePaymentCase1 is not null
                    ? $"• BN nội trú: {scenario.AdvancePaymentCase1.FullName} (BA: {scenario.AdvancePaymentCase1.SoBenhAn}, Khoa: {scenario.AdvancePaymentCase1.DepartmentName}, Số tiền: 1.500.000đ)"
                    : "• BN nội trú (1.500.000đ)") +
                (scenario.AdvancePaymentOutpatientCase is not null
                    ? $" | BN ngoại trú: {scenario.AdvancePaymentOutpatientCase.FullName} (BA: {scenario.AdvancePaymentOutpatientCase.SoBenhAn}, Số tiền: 500.000đ)"
                    : (scenario.AdvancePaymentCase2 is not null ? $" | BN ngoại trú: {scenario.AdvancePaymentCase2.FullName} (Số tiền: 500.000đ)" : "")),

            "LT_DOI_DICH_VU_CLS" =>
                $"• Đổi dịch vụ CLS BN: {(scenario.SecondPatient ?? scenario.Patient).FullName} (Mã: {(scenario.SecondPatient ?? scenario.Patient).MedicalCode}) | Thao tác đổi chỉ định kỹ thuật",

            "LT_TRA_CUU_THONG_TIN" =>
                (scenario.InpatientLookupCase is not null
                    ? $"• Tra cứu BN nội trú: {scenario.InpatientLookupCase.FullName} (BA: {scenario.InpatientLookupCase.SoBenhAn}, Khoa: {scenario.InpatientLookupCase.DepartmentName}, Giường: {scenario.InpatientLookupCase.BedCode}, Ngày vào: {scenario.InpatientLookupCase.AdmissionDate})"
                    : "") +
                (scenario.OutpatientLookupCase is not null
                    ? $" | Tra cứu BN ngoại trú: {scenario.OutpatientLookupCase.FullName} (Mã: {scenario.OutpatientLookupCase.MedicalCode}, Số BA: {scenario.OutpatientLookupCase.SoBenhAn}, Khoa: {scenario.OutpatientLookupCase.DepartmentName})"
                    : ""),

            "LT_THANH_TOAN_RA_VIEN" =>
                scenario.DischargeCase is not null
                    ? $"• Thanh toán viện phí ra viện BN: {scenario.DischargeCase.FullName} (BA: {scenario.DischargeCase.SoBenhAn}, Khoa: {scenario.DischargeCase.DepartmentName}, Vào viện: {scenario.DischargeCase.AdmissionDate})"
                    : "• Thanh toán ra viện bệnh nhân nội trú theo đề bài",

            "LT_TAO_THE_TAM_BHYT" =>
                $"• Cấp thẻ tạm BHYT: Bé gái (Sinh {scenario.ExamDate.AddMonths(-1):dd/MM/yyyy}) | Mã BHYT tạm: TE1... | Hạn thẻ: 30 ngày",

            "YL_CHI_DINH_THUOC_VTYT" =>
                $"• BN: {scenario.Patient.FullName} | Thuốc: " +
                (scenario.OrderedDrugs.Count > 0 ? string.Join("; ", scenario.OrderedDrugs.Select(d => $"{d.Name} ({d.Code}, SL:{d.Quantity})")) : "Theo chỉ định"),

            "TK_KIEM_TON_KHO" =>
                scenario.InventoryCheckPeriod is not null
                    ? $"• Kiểm tồn kho: {scenario.InventoryCheckPeriod.DrugName} (Từ {scenario.InventoryCheckPeriod.FromDate:dd/MM/yyyy} đến {scenario.InventoryCheckPeriod.ToDate:dd/MM/yyyy}, Kho: {scenario.InventoryCheckPeriod.WarehouseName}) -> TỒN CUỐI KỲ CHUẨN: {scenario.InventoryCheckPeriod.ClosingStock:N0} {scenario.InventoryCheckPeriod.Unit}"
                    : (scenario.InventoryCheckDrug is not null
                        ? $"• Kiểm tồn kho: {scenario.InventoryCheckDrug.Name} (Kho: {scenario.InventoryCheckDrug.WarehouseName})"
                        : "• Kiểm tra tồn kho theo đề bài"),

            "KB_BENH_AN_NGOAI_TRU" =>
                $"• Lập bệnh án ngoại trú cho BN: {scenario.Patient.FullName} (Mã: {scenario.Patient.MedicalCode}) | Thí sinh điền Số bệnh án ngoại trú",

            "KB_CHI_DINH_NOI_TRU" =>
                $"• Chỉ định nhập viện nội trú BN: {scenario.Patient.FullName} (Mã: {scenario.Patient.MedicalCode}) | Thí sinh điền Số bệnh án nội trú",

            "HC_TRA_CUU_THANH_TOAN" =>
                $"• Tra cứu viện phí của BN tại {scenario.DepartmentName} | Đối soát 5 thông tin: Họ tên, Mã YT/Số BA, Tổng chi phí, Tạm ứng, Còn lại",

            "TT_TUONG_TRINH_PTTT" =>
                $"• Tường trình PT-TT BN: {scenario.Patient.FullName} (Mã: {scenario.Patient.MedicalCode}) | Đối soát nội dung tường trình trên phân hệ PT-TT HIS",

            "TC_TRA_CUU_BENH_SU" =>
                $"• Tra cứu lịch sử KCB BN: {scenario.Patient.FullName} (Mã: {scenario.Patient.MedicalCode}) | Đối soát thông tin các đợt khám",

            "NT_NHAN_BENH_KHOA" =>
                $"• Nhận bệnh vào khoa BN: {scenario.Patient.FullName} (Mã: {scenario.Patient.MedicalCode}) | Thí sinh ghi lại Số vào viện và Buồng/Giường đã nhận",

            _ => $"• BN: {scenario.Patient.FullName} (Mã: {scenario.Patient.MedicalCode})"
        };
    }

    public static void SavePackage(ClientExamPackage pkg, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var json = JsonSerializer.Serialize(pkg, JsonOptions);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    public static ClientExamPackage LoadPackage(string filePath)
    {
        var json = File.ReadAllText(filePath, Encoding.UTF8);
        return JsonSerializer.Deserialize<ClientExamPackage>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Không thể giải mã gói đề thi từ file: {filePath}");
    }

    public static void SaveSubmission(ClientSubmissionPackage sub, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var json = JsonSerializer.Serialize(sub, JsonOptions);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    public static ClientSubmissionPackage LoadSubmission(string filePath)
    {
        var json = File.ReadAllText(filePath, Encoding.UTF8);
        return JsonSerializer.Deserialize<ClientSubmissionPackage>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Không thể giải mã bài nộp từ file: {filePath}");
    }

    public static IReadOnlyList<string> ExportPackages(
        IEnumerable<ExamScenario> scenarios,
        string outputDirectory,
        string supervisorPassword = "bvth@2026")
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var exportedFiles = new List<string>();
        var manifestList = new List<object>();

        foreach (var s in scenarios)
        {
            var pkg = CreatePackage(s, supervisorPassword);
            var safeCandidate = string.IsNullOrWhiteSpace(s.CandidateId) ? "NO_SBD" : s.CandidateId.Trim();
            var fileName = $"{safeCandidate}_{s.DepartmentCode}_{s.ScenarioId[..6]}.exam_pkg";
            var fullPath = Path.Combine(outputDirectory, fileName);

            SavePackage(pkg, fullPath);
            exportedFiles.Add(fullPath);

            manifestList.Add(new
            {
                pkg.CandidateId,
                pkg.CandidateName,
                pkg.DepartmentName,
                pkg.DurationMinutes,
                pkg.PaperCode,
                pkg.HisUserCode,
                FileName = fileName
            });
        }

        var manifestJson = JsonSerializer.Serialize(manifestList, JsonOptions);
        File.WriteAllText(Path.Combine(outputDirectory, "exams_manifest.json"), manifestJson, Encoding.UTF8);

        return exportedFiles;
    }
}
