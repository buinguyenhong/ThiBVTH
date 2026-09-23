namespace ExamGenerator.Domain;

public enum ReceptionMode
{
    DirectReception,
    WardAdmissionPreparation
}

public sealed record InsurancePeriod(DateOnly From, DateOnly To)
{
    public static InsurancePeriod ForExamYear(int examYear, bool isTe1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(examYear, 2000);

        var endYear = isTe1 ? examYear + 4 : examYear;
        return new InsurancePeriod(
            new DateOnly(examYear, 1, 1),
            new DateOnly(endYear, 12, 31));
    }
}

public sealed record ExamTemplate(
    string Id,
    string Name,
    string DepartmentCode,
    string RoleCode,
    ReceptionMode ReceptionMode)
{
    public bool RequiresDirectReception => ReceptionMode == ReceptionMode.DirectReception;
    public bool RequiresExamSetupSql => ReceptionMode == ReceptionMode.WardAdmissionPreparation;
}

public sealed record SystemActionDefinition(
    string Code,
    string Name,
    double DefaultScore,
    string Category,
    string Description,
    bool IsBuiltIn = false)
{
    public string DisplayText => $"[{Code}] {Name} ({DefaultScore:0.0}đ) - {Category}";
}

public static class SystemActionCatalog
{
    public static readonly IReadOnlyList<SystemActionDefinition> DefaultActions = new List<SystemActionDefinition>
    {
        new("TN_TIEP_NHAN", "Tiếp nhận trực tiếp người bệnh (BHYT / Viện phí)", 2.0, "Tiếp đón & Nhập viện", "Thí sinh nhập hồ sơ hành chính, số thẻ BHYT, nơi ĐKKCB và chẩn đoán ban đầu.", true),
        new("NT_NHAN_BENH_KHOA", "Nhận bệnh vào khoa điều trị", 1.0, "Điều trị Nội trú", "Tìm bệnh nhân trong hàng chờ, xác nhận vào khoa và ghi lại số vào viện.", true),
        new("YL_CHI_DINH_CLS", "Chỉ định dịch vụ Cận lâm sàng (CLS)", 2.0, "Chỉ định & Y lệnh", "Chỉ định các dịch vụ Siêu âm, X-quang, CT/MRI, Xét nghiệm theo chuyên khoa.", true),
        new("YL_CHI_DINH_THUOC_VTYT", "Lên y lệnh thuốc & vật tư y tế", 3.0, "Chỉ định & Y lệnh", "Lên y lệnh thuốc BH/VP, lập phiếu lĩnh dược, nhập nội bộ và xuất sử dụng cho người bệnh.", true),
        new("YL_TRA_THUOC", "Trả thuốc thừa & lập phiếu trả", 1.0, "Dược & Vật tư", "Thực hiện trả lại thuốc thừa cho khoa dược và tổng hợp phiếu trả thuốc.", true),
        new("YL_DOI_THEM_DICH_VU", "Thay đổi chỉ định CLS (Hủy / Đổi / Thêm)", 1.0, "Chỉ định & Y lệnh", "Hủy 1 dịch vụ CLS đã chỉ định, đổi sang dịch vụ khác và bổ sung dịch vụ mới.", true),
        new("TK_KIEM_TON_KHO", "Kiểm tra tồn kho dược cơ số tại khoa", 1.0, "Dược & Vật tư", "Tra cứu thẻ kho/tồn kho cuối kỳ của thuốc/vật tư nguồn Viện phí trong khoảng ngày thi.", true),
        new("CK_CHUYEN_KHOA", "Chuyển khoa điều trị", 1.0, "Điều trị Nội trú", "Thực hiện thủ tục chuyển bệnh nhân sang khoa điều trị khác.", true),
        new("RV_CHO_RA_VIEN", "Cho bệnh nhân ra viện", 1.0, "Điều trị Nội trú", "Thực hiện các thao tác kết thúc điều trị và cho bệnh nhân ra viện.", true),
        new("TC_THU_TAM_UNG", "Thu tạm ứng viện phí", 2.0, "Viện phí & Thu ngân", "Lập biên lai thu tiền tạm ứng viện phí cho người bệnh.", true),
        new("TC_THANH_TOAN_RA_VIEN", "Thanh toán viện phí ra viện", 2.0, "Viện phí & Thu ngân", "Thực hiện quyết toán và in hóa đơn thanh toán viện phí ra viện.", true),
        new("TC_TRA_CUU_BENH_SU", "Tra cứu lịch sử KCB / Bệnh sử", 2.0, "Hành chính & Tra cứu", "Tra cứu thông tin các đợt khám chữa bệnh trước đây của người bệnh.", true),
        new("KQ_TRA_KET_QUA_CLS", "Trả kết quả CLS (Xét nghiệm / CĐHA)", 7.0, "Kỹ thuật viên CLS", "Nhập kết quả xét nghiệm hoặc mô tả kết quả chẩn đoán hình ảnh.", true),
        new("NT_CAP_GIUONG", "Cấp giường cho bệnh nhân", 1.0, "Điều trị Nội trú", "Cấp giường cho các bệnh nhân được tiếp nhận ở câu 1", true),
        new("KB_CHI_DINH_CLS", "Chỉ định CLS ngoại trú", 2.0, "Khám bệnh & Ngoại trú", "Trên màn hình khám bệnh Chỉ định CLS – Thủ thuật cho bệnh nhân : Trần Tâm, Mã Y tế : 23000059\nChụp răng\nĐiều trị tủy răng và hàn kín hệ thống ống tủy bằng Gutta percha nguội (Điều trị tủy răng số 1)\nSố điểm đạt được : ……………", true),
        new("KB_BENH_AN_NGOAI_TRU", "Tạo bệnh án ngoại trú", 2.0, "Khám bệnh & Ngoại trú", "Chỉ định điều trị ngoại trú và lập bệnh án ngoại trú cho bệnh nhân Trần Tâm, Mã Y tế : 23000059. (2 đ)\nSố bệnh án ngoại trú : …………………………………………………\nSố điểm đạt được : ……………", true),
        new("KB_CHI_DINH_NOI_TRU", "Chỉ định điều trị nội trú", 1.0, "Khám bệnh & Ngoại trú", "Chỉ định nhập viện điều trị nội trú cho bệnh nhân : Thân Thu Hà, Mã y tế : 23000061 (1 đ)\nNhập khoa : Khoa phẫu thuật GMHS\nSố bệnh án nội trú : ……………………………………………………\nSố điểm đạt được : ……………", true),
        new("HC_TRA_CUU_THANH_TOAN", "Tra cứu thông tin thanh toán", 2.0, "Hành chính & Tra cứu", "Kiểm tra thông tin thanh toán cho bệnh nhân : ................, Mã y tế : ...........................\nSố bệnh án nội trú : ……………………………………………………\nSố tiền người bệnh đã thanh toán : ……………………………………………………\nSố tiền người bệnh đã tạm ứng : ……………………………………………………\nSố tiền còn phải thu người bệnh : ……………………………………………………\nTổng số tiền người bệnh trả : ……………\n( tên bệnh nhân và mã y tế sẽ do tôi điền sau )", true)
    };

    public static IReadOnlyList<SystemActionDefinition> All => DefaultActions;
}

public sealed record DepartmentWarehouseMapping(string DepartmentCode, string WarehouseCode);

public sealed record CandidateAssignment(
    string CandidateId,
    string DepartmentCode,
    string HisUserCode);

public static class ExamBatchRules
{
    public static IReadOnlyList<string> ValidateTemplate(ExamTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(template.Id)) errors.Add("Template phải có mã.");
        if (string.IsNullOrWhiteSpace(template.DepartmentCode)) errors.Add("Template phải có khoa/phòng.");
        if (string.IsNullOrWhiteSpace(template.RoleCode)) errors.Add("Template phải có vị trí.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateWarehouseMappings(
        IEnumerable<DepartmentWarehouseMapping> mappings,
        IEnumerable<string> enabledDepartments)
    {
        var mappingList = mappings.ToList();
        var errors = new List<string>();

        foreach (var department in enabledDepartments.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var warehouses = mappingList
                .Where(x => string.Equals(x.DepartmentCode, department, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.WarehouseCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (warehouses.Count == 0)
                errors.Add($"Khoa '{department}' chưa được map kho thi.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateCandidateWarehouseSelections(
        IEnumerable<(string CandidateName, string DepartmentCode, string? WarehouseCode)> candidateWarehouseSelections,
        IEnumerable<DepartmentWarehouseMapping> mappings)
    {
        var mappingList = mappings.ToList();
        var errors = new List<string>();

        foreach (var c in candidateWarehouseSelections)
        {
            if (string.IsNullOrWhiteSpace(c.WarehouseCode))
            {
                errors.Add($"Thí sinh '{c.CandidateName}' chưa chọn Kho dược thi.");
                continue;
            }

            var isMapped = mappingList.Any(m =>
                string.Equals(m.DepartmentCode, c.DepartmentCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.WarehouseCode, c.WarehouseCode, StringComparison.OrdinalIgnoreCase));

            if (!isMapped)
            {
                errors.Add($"Kho dược '{c.WarehouseCode}' được chọn cho thí sinh '{c.CandidateName}' không thuộc danh sách kho đã map của khoa '{c.DepartmentCode}'.");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateUserAssignments(
        IEnumerable<CandidateAssignment> assignments)
    {
        var list = assignments.ToList();
        var errors = new List<string>();

        var duplicateUsers = list
            .Where(x => !string.IsNullOrWhiteSpace(x.HisUserCode))
            .GroupBy(x => x.HisUserCode, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key);

        foreach (var user in duplicateUsers)
            errors.Add($"User HIS '{user}' bị cấp trùng trong cùng đợt.");

        foreach (var assignment in list.Where(x => string.IsNullOrWhiteSpace(x.HisUserCode)))
            errors.Add($"Thí sinh '{assignment.CandidateId}' chưa được cấp user HIS.");

        return errors;
    }
}
