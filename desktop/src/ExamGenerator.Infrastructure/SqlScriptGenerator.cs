using System.Text;
using System.Text.RegularExpressions;
using ExamGenerator.Domain;

namespace ExamGenerator.Infrastructure;

public sealed class SqlScriptGenerator
{
    private readonly string _admissionScriptTemplate;

    public SqlScriptGenerator(string? scriptTemplateContent = null)
    {
        _admissionScriptTemplate = scriptTemplateContent ?? LoadTemplateFromDiskOrEmbedded();
    }

    public string Generate(ExamScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var sb = new StringBuilder();
        sb.AppendLine("/* ============================================================================");
        sb.AppendLine("   BỆNH VIỆN ĐA KHOA THIỆN HẠNH - HỘI ĐỒNG TUYỂN DỤNG");
        sb.AppendLine($"   ĐỢT THI: {scenario.BatchName} - NGÀY THI: {scenario.ExamDate:dd/MM/yyyy}");
        sb.AppendLine($"   THÍ SINH: {scenario.CandidateName} | SBD: {scenario.CandidateId}");
        sb.AppendLine($"   KHOA/PHÒNG: {scenario.DepartmentName} | TÀI KHOẢN HIS: {scenario.HisUserCode}");
        sb.AppendLine($"   MẪU ĐỀ: {scenario.TemplateName}");
        sb.AppendLine("   ----------------------------------------------------------------------------");
        sb.AppendLine("   CẢNH BÁO BẢO MẬT VÀ AN TOÀN DỮ LIỆU:");
        sb.AppendLine("   - SCRIPT NÀY CHỈ ĐƯỢC CHẠY THỦ CÔNG TRÊN MÁY CHỦ THI KHẢO THÍ (SERVER THI).");
        sb.AppendLine("   - TUYỆT ĐỐI KHÔNG THỰC THI TRÊN CƠ SỞ DỮ LIỆU HIS VẬN HÀNH THẬT!");
        sb.AppendLine("   ============================================================================ */");
        sb.AppendLine();

        if (scenario.RequiresDirectReception)
        {
            sb.AppendLine("-- [QUY TRÌNH TIẾP NHẬN TRỰC TIẾP]");
            sb.AppendLine("-- Đề thi yêu cầu thí sinh tự thực hiện toàn bộ nghiệp vụ Tiếp nhận trên phần mềm HIS.");
            sb.AppendLine("-- Hệ thống KHÔNG tạo sẵn ca bệnh trên máy chủ để đảm bảo đánh giá đúng kỹ năng của thí sinh.");
            sb.AppendLine($"PRINT N'Chuẩn bị tài khoản [{scenario.HisUserCode}] cho thí sinh [{scenario.CandidateName}] tại [{scenario.DepartmentName}]. Sẵn sàng cho phần thi Tiếp nhận trực tiếp.';");
            sb.AppendLine("GO");
            return sb.ToString();
        }

        if (!scenario.RequiresExamSetupSql)
        {
            sb.AppendLine("-- Đề thi chuyên môn/văn phòng không yêu cầu nạp dữ liệu ca bệnh vào hàng chờ khoa.");
            sb.AppendLine($"PRINT N'Tài khoản [{scenario.HisUserCode}] của thí sinh [{scenario.CandidateName}] đã sẵn sàng.';");
            sb.AppendLine("GO");
            return sb.ToString();
        }

        sb.AppendLine("-- [QUY TRÌNH NẠP CA BỆNH VÀO HÀNG CHỜ NHẬN KHOA]");
        var rendered = RenderAdmissionScript(scenario, scenario.Patient);
        sb.AppendLine(rendered);

        if (scenario.SecondPatient is not null)
        {
            sb.AppendLine("GO");
            sb.AppendLine("-- [BỆNH NHÂN THỨ 2 (VIỆN PHÍ)]");
            var rendered2 = RenderAdmissionScript(scenario, scenario.SecondPatient);
            sb.AppendLine(rendered2);
        }

        return sb.ToString();
    }

    private string RenderAdmissionScript(ExamScenario scenario, ScenarioPatient p)
    {
        var script = _admissionScriptTemplate;
        var birthDate = p.DateOfBirth ?? new DateOnly(scenario.ExamDate.Year - (p.Age > 0 ? p.Age : 30), 1, 1);
        var genderCode = p.Gender.Trim().Equals("Nữ", StringComparison.OrdinalIgnoreCase) || p.Gender.Trim().Equals("G", StringComparison.OrdinalIgnoreCase) ? "G" : "T";
        var isBhyt = !string.IsNullOrWhiteSpace(p.InsuranceNumber);
        var loaiHoSo = isBhyt ? "BHYT" : "VIEN_PHI";
        var cardFrom = p.InsurancePeriod?.From ?? new DateOnly(scenario.ExamDate.Year, 1, 1);
        var cardTo = p.InsurancePeriod?.To ?? new DateOnly(scenario.ExamDate.Year, 12, 31);
        var dkkcb = string.IsNullOrWhiteSpace(p.InitialRegistrationCode) ? "66232" : p.InitialRegistrationCode;

        var address = p.Address?.Trim() ?? string.Empty;
        var parts = address.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var province = parts.Length > 0 ? parts[^1] : "Đắk Lắk";
        var house = parts.Length > 1 ? string.Join(", ", parts[..^1]) : address;
        if (string.IsNullOrWhiteSpace(house)) house = address;

        var diagnosis = string.IsNullOrWhiteSpace(p.Diagnosis)
            ? $"THEO DÕI NHẬP VIỆN {scenario.DepartmentName.ToUpperInvariant()}"
            : p.Diagnosis;
        var isCapCuu = scenario.DepartmentName.Contains("cấp cứu", StringComparison.OrdinalIgnoreCase) ? "1" : "0";

        script = SetSqlVariable(script, "TenDangNhap", "nvarchar(100)", $"N'{EscapeSql(scenario.HisUserCode)}'");
        script = SetSqlVariable(script, "TenKhoaDieuTri", "nvarchar(200)", $"N'{EscapeSql(scenario.DepartmentName)}'");
        script = SetSqlVariable(script, "TenTinhThanh", "nvarchar(150)", $"N'{EscapeSql(province)}'");
        script = SetSqlVariable(script, "MaICD", "varchar(20)", "'R69'");
        script = SetSqlVariable(script, "MaBenhVienKCB", "varchar(20)", $"'{EscapeSql(dkkcb)}'");

        script = SetSqlVariable(script, "LoaiHoSo", "varchar(20)", $"'{loaiHoSo}'");
        script = SetSqlVariable(script, "Commit", "bit", "1");
        script = SetSqlVariable(script, "NgayGioNghiepVu", "datetime", $"'{scenario.ExamDate:yyyy-MM-dd} 07:30:00'");
        script = SetSqlVariable(script, "TenBenhNhan", "nvarchar(40)", $"N'{EscapeSql(p.FullName)}'");
        script = SetSqlVariable(script, "GioiTinh", "char(1)", $"'{genderCode}'");
        script = SetSqlVariable(script, "NgaySinh", "smalldatetime", $"'{birthDate:yyyy-MM-dd}'");
        script = SetSqlVariable(script, "SoNha", "nvarchar(150)", $"N'{EscapeSql(house)}'");
        script = SetSqlVariable(script, "DiaChiThuongTru", "nvarchar(150)", $"N'{EscapeSql(address)}'");
        script = SetSqlVariable(script, "ChanDoan", "nvarchar(200)", $"N'{EscapeSql(diagnosis)}'");
        script = SetSqlVariable(script, "CapCuu", "bit", isCapCuu);

        if (isBhyt)
        {
            var insNum = p.InsuranceNumber ?? string.Empty;
            script = SetSqlVariable(script, "SoBHYT", "varchar(30)", $"'{EscapeSql(insNum)}'");
            script = SetSqlVariable(script, "BHYTTuNgay", "smalldatetime", $"'{cardFrom:yyyy-MM-dd}'");
            script = SetSqlVariable(script, "BHYTDenNgay", "smalldatetime", $"'{cardTo:yyyy-MM-dd}'");
        }
        else
        {
            script = SetSqlVariable(script, "SoBHYT", "varchar(30)", "NULL");
            script = SetSqlVariable(script, "BHYTTuNgay", "smalldatetime", "NULL");
            script = SetSqlVariable(script, "BHYTDenNgay", "smalldatetime", "NULL");
        }

        return script;
    }

    private static string SetSqlVariable(string script, string variableName, string dataType, string valueExpression)
    {
        var pattern = $@"(?m)^DECLARE\s+@{Regex.Escape(variableName)}\b[^\r\n]*\r?$";
        var replacement = $"DECLARE @{variableName} {dataType} = {valueExpression};";
        if (Regex.IsMatch(script, pattern))
        {
            return Regex.Replace(script, pattern, replacement, RegexOptions.None);
        }

        const string marker = "/* =========================================================================\r\n   1. THAM SỐ CẦN ĐIỀN\r\n   ========================================================================= */";
        const string markerAlt = "/* =========================================================================\n   1. THAM SỐ CẦN ĐIỀN\n   ========================================================================= */";
        if (script.Contains(marker))
        {
            return script.Replace(marker, marker + "\r\n" + replacement);
        }
        if (script.Contains(markerAlt))
        {
            return script.Replace(markerAlt, markerAlt + "\n" + replacement);
        }

        return $"DECLARE @{variableName} {dataType} = {valueExpression};\r\n" + script;
    }

    private static string EscapeSql(string value) => (value ?? string.Empty).Replace("'", "''");

    private static string LoadTemplateFromDiskOrEmbedded()
    {
        string[] candidatePaths =
        [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "Scripts", "02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql"),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Scripts", "02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql"),
        ];

        foreach (var p in candidatePaths)
        {
            try
            {
                var full = Path.GetFullPath(p);
                if (File.Exists(full))
                {
                    var content = File.ReadAllText(full, Encoding.UTF8);
                    if (content.Contains("sp_DM_BENHNHAN"))
                    {
                        return PrepareTemplateWithAutoLookup(content);
                    }
                }
            }
            catch
            {
                // Continue searching
            }
        }

        return DefaultAdmissionScriptTemplate();
    }

    private static string PrepareTemplateWithAutoLookup(string rawScript)
    {
        if (rawScript.Contains("2.1. TỰ ĐỘNG TRA CỨU ID TỪ DANH MỤC HIS"))
        {
            return rawScript;
        }

        const string declarations = """
DECLARE @TenDangNhap nvarchar(100) = N'ANHPH';
DECLARE @TenKhoaDieuTri nvarchar(200) = N'Khoa Ngoại tổng hợp';
DECLARE @TenTinhThanh nvarchar(150) = N'Đắk Lắk';
DECLARE @MaICD varchar(20) = 'R69';
DECLARE @MaBenhVienKCB varchar(20) = '66232';
""";

        const string autoLookupSql = """
/* =========================================================================
   2.1. TỰ ĐỘNG TRA CỨU ID TỪ DANH MỤC HIS
   ========================================================================= */

DECLARE @TenKhoaCotLoi nvarchar(200) =
    LTRIM(RTRIM(REPLACE(@TenKhoaDieuTri, N'Khoa ', N'')));

SELECT TOP (1) @User_Id = u.User_Id
FROM dbo.Sys_Users AS u
WHERE ISNULL(u.Suspend, 0) = 0
  AND (
      UPPER(LTRIM(RTRIM(u.User_Code))) = UPPER(LTRIM(RTRIM(@TenDangNhap)))
      OR UPPER(LTRIM(RTRIM(u.User_Name))) = UPPER(LTRIM(RTRIM(@TenDangNhap)))
  )
ORDER BY
    CASE WHEN UPPER(LTRIM(RTRIM(u.User_Code))) =
                   UPPER(LTRIM(RTRIM(@TenDangNhap))) THEN 0 ELSE 1 END,
    u.User_Id;

SELECT TOP (1)
    @NoiTiepNhan_Id = pb.PhongBan_Id,
    @KhoaDieuTri_Id = pb.PhongBan_Id
FROM dbo.DM_PhongBan AS pb
JOIN dbo.Lst_Dictionary AS loai
  ON loai.Dictionary_Id = pb.LoaiPhongBan_Id
WHERE ISNULL(pb.TamNgung, 0) = 0
  AND loai.Dictionary_Code = 'KhoaNoi'
  AND (
      LTRIM(RTRIM(pb.TenPhongBan)) = LTRIM(RTRIM(@TenKhoaDieuTri))
      OR LTRIM(RTRIM(ISNULL(pb.TenKhongDau, N''))) =
         LTRIM(RTRIM(@TenKhoaDieuTri))
      OR pb.TenPhongBan LIKE N'%' + @TenKhoaCotLoi + N'%'
      OR ISNULL(pb.TenKhongDau, N'') LIKE N'%' + @TenKhoaCotLoi + N'%'
  )
ORDER BY
    CASE WHEN LTRIM(RTRIM(pb.TenPhongBan)) =
                   LTRIM(RTRIM(@TenKhoaDieuTri)) THEN 0 ELSE 1 END,
    pb.PhongBan_Id;

SET @NoiNhapVien_Id = ISNULL(@NoiNhapVien_Id, @NoiTiepNhan_Id);

SELECT TOP (1) @HinhThucDenKham_Id = d.Dictionary_Id
FROM dbo.Lst_Dictionary AS d
WHERE d.Dictionary_Type_Code = 'HinhThucDenKhamBenh'
  AND ISNULL(d.Enabled, 1) = 1
  AND (
      d.Dictionary_Code IN ('TuDen', 'HinhThucDenKhamBenh_TuDen')
      OR d.Dictionary_Name LIKE N'%tự đến%'
  )
ORDER BY
    CASE WHEN d.Dictionary_Code = 'TuDen' THEN 0
         WHEN d.Dictionary_Code = 'HinhThucDenKhamBenh_TuDen' THEN 1
         ELSE 2 END,
    d.Dictionary_Id;

SELECT TOP (1) @DanToc_Id = d.Dictionary_Id
FROM dbo.Lst_Dictionary AS d
WHERE d.Dictionary_Type_Code = 'DanToc'
  AND ISNULL(d.Enabled, 1) = 1
ORDER BY
    CASE WHEN d.Dictionary_Name = N'Kinh'
              OR d.Dictionary_Code IN ('1', '01', 'Kinh') THEN 0 ELSE 1 END,
    d.Dictionary_Id;

SELECT TOP (1) @NgheNghiep_Id = d.Dictionary_Id
FROM dbo.Lst_Dictionary AS d
WHERE d.Dictionary_Type_Code = 'NgheNghiep'
  AND ISNULL(d.Enabled, 1) = 1
  AND LEN(ISNULL(d.Dictionary_Code, N'')) >= 5
ORDER BY
    CASE WHEN d.Dictionary_Name LIKE N'%khác%' THEN 0 ELSE 1 END,
    d.Dictionary_Id;

SELECT TOP (1) @TinhThanh_Id = dv.DonViHanhChinh_Id
FROM dbo.DM_DonViHanhChinh AS dv
WHERE dv.CapDonVi = 2
  AND ISNULL(dv.TamNgung, 0) = 0
  AND (
      LTRIM(RTRIM(dv.TenDonVi)) = LTRIM(RTRIM(@TenTinhThanh))
      OR LTRIM(RTRIM(ISNULL(dv.TenKhongDau, N''))) =
         LTRIM(RTRIM(@TenTinhThanh))
      OR dv.TenDonVi LIKE N'%' + LTRIM(RTRIM(@TenTinhThanh)) + N'%'
  )
ORDER BY
    CASE WHEN LTRIM(RTRIM(dv.TenDonVi)) =
                   LTRIM(RTRIM(@TenTinhThanh)) THEN 0 ELSE 1 END,
    dv.DonViHanhChinh_Id;

SELECT TOP (1) @BacSiChiDinh_Id = nv.NhanVien_Id
FROM dbo.NhanVien AS nv
LEFT JOIN dbo.Lst_Dictionary AS cd
  ON cd.Dictionary_Id = nv.ChucDanh_Id
 AND cd.Dictionary_Type_Code = 'ChucDanh'
WHERE ISNULL(nv.TamNgung, 0) = 0
  AND nv.PhongBan_Id = @KhoaDieuTri_Id
ORDER BY
    CASE WHEN cd.Dictionary_Code LIKE 'BS%'
              OR cd.Dictionary_Name LIKE N'%bác sĩ%' THEN 0 ELSE 1 END,
    nv.NhanVien_Id;

IF @BacSiChiDinh_Id IS NULL
BEGIN
    SELECT TOP (1) @BacSiChiDinh_Id = nv.NhanVien_Id
    FROM dbo.NhanVien AS nv
    JOIN dbo.Lst_Dictionary AS cd
      ON cd.Dictionary_Id = nv.ChucDanh_Id
     AND cd.Dictionary_Type_Code = 'ChucDanh'
    WHERE ISNULL(nv.TamNgung, 0) = 0
      AND (
          cd.Dictionary_Code LIKE 'BS%'
          OR cd.Dictionary_Name LIKE N'%bác sĩ%'
      )
    ORDER BY nv.NhanVien_Id;
END;

SELECT TOP (1) @LyDoNhapVien_Id = d.Dictionary_Id
FROM dbo.Lst_Dictionary AS d
WHERE d.Dictionary_Type_Code = 'LyDoNhapVien'
  AND ISNULL(d.Enabled, 1) = 1
ORDER BY
    CASE WHEN d.Dictionary_Name LIKE N'%nhập viện%' THEN 0 ELSE 1 END,
    ISNULL(d.Idx, 2147483647),
    d.Dictionary_Id;

SELECT TOP (1) @ICD_Id = icd.ICD_Id
FROM dbo.DM_ICD AS icd
WHERE UPPER(LTRIM(RTRIM(icd.MaICD))) = UPPER(LTRIM(RTRIM(@MaICD)))
  AND ISNULL(icd.TamNgung, 0) = 0
ORDER BY icd.ICD_Id;

IF @LoaiHoSo = 'BHYT'
BEGIN
    /* Phân giải @LoaiBHYT đa tầng: danh mục TiepNhanLoaiBHYT -> LoaiBHYT -> Type_Id=101 -> lịch sử TiepNhan */
    SELECT TOP (1) @LoaiBHYT = d.Dictionary_Id
    FROM dbo.Lst_Dictionary AS d
    LEFT JOIN dbo.Lst_Dictionary_Type AS dt
      ON dt.Dictionary_Type_Id = d.Dictionary_Type_Id
    WHERE (
        d.Dictionary_Type_Code IN ('TiepNhanLoaiBHYT', 'LoaiBHYT', 'BHYT_Loai')
        OR dt.Dictionary_Type_Code IN ('TiepNhanLoaiBHYT', 'LoaiBHYT', 'BHYT_Loai')
        OR d.Dictionary_Type_Id = 101
    )
      AND ISNULL(d.Enabled, 1) = 1
    ORDER BY
        CASE WHEN d.Dictionary_Name LIKE N'%bắt buộc%' THEN 0 ELSE 1 END,
        ISNULL(d.Idx, 2147483647),
        d.Dictionary_Id;

    IF @LoaiBHYT IS NULL
    BEGIN
        SELECT TOP (1) @LoaiBHYT = tn.LoaiBHYT
        FROM dbo.TiepNhan AS tn
        WHERE tn.LoaiBHYT IS NOT NULL AND tn.LoaiBHYT > 0
        ORDER BY tn.TiepNhan_Id DESC;
    END;

    IF @LoaiBHYT IS NULL
    BEGIN
        SELECT TOP (1) @LoaiBHYT = b.LoaiBHYT
        FROM dbo.DM_BenhNhan_BHYT AS b
        WHERE b.LoaiBHYT IS NOT NULL AND b.LoaiBHYT > 0
        ORDER BY b.BenhNhan_BHYT_Id DESC;
    END;

    /* Phân giải @TuyenKhamBenh_Id đa tầng: TrongTuyen -> đúng tuyến -> bất kỳ tuyến nào -> lịch sử TiepNhan */
    SELECT TOP (1) @TuyenKhamBenh_Id = d.Dictionary_Id
    FROM dbo.Lst_Dictionary AS d
    LEFT JOIN dbo.Lst_Dictionary_Type AS dt
      ON dt.Dictionary_Type_Id = d.Dictionary_Type_Id
    WHERE (
        d.Dictionary_Type_Code IN ('TuyenKhamChuaBenh', 'TuyenKhamBenh')
        OR dt.Dictionary_Type_Code IN ('TuyenKhamChuaBenh', 'TuyenKhamBenh')
    )
      AND ISNULL(d.Enabled, 1) = 1
    ORDER BY
        CASE 
            WHEN d.Dictionary_Code = 'TuyenKhamChuaBenh_TrongTuyen' THEN 0
            WHEN d.Dictionary_Code IN ('TrongTuyen', 'DungTuyen', 'DT', 'TT', '1') THEN 1
            WHEN d.Dictionary_Name LIKE N'%đúng tuyến%' OR d.Dictionary_Name LIKE N'%trong tuyến%' THEN 2
            ELSE 3
        END,
        ISNULL(d.Idx, 2147483647),
        d.Dictionary_Id;

    IF @TuyenKhamBenh_Id IS NULL
    BEGIN
        SELECT TOP (1) @TuyenKhamBenh_Id = d.Dictionary_Id
        FROM dbo.Lst_Dictionary AS d
        LEFT JOIN dbo.Lst_Dictionary_Type AS dt
          ON dt.Dictionary_Type_Id = d.Dictionary_Type_Id
        WHERE (
            d.Dictionary_Type_Code IN ('TuyenKhamChuaBenh', 'TuyenKhamBenh')
            OR dt.Dictionary_Type_Code IN ('TuyenKhamChuaBenh', 'TuyenKhamBenh')
        )
        ORDER BY d.Dictionary_Id;
    END;

    IF @TuyenKhamBenh_Id IS NULL
    BEGIN
        SELECT TOP (1) @TuyenKhamBenh_Id = tn.TuyenKhamBenh_Id
        FROM dbo.TiepNhan AS tn
        WHERE tn.TuyenKhamBenh_Id IS NOT NULL AND tn.TuyenKhamBenh_Id > 0
        ORDER BY tn.TiepNhan_Id DESC;
    END;

    /* Phân giải @BenhVien_KCB_Id đa tầng: MaBenhVien/TenBenhVien_En -> tên viện -> Sys_AppSettings -> lịch sử TiepNhan -> viện đầu tiên */
    SELECT TOP (1) @BenhVien_KCB_Id = bv.BenhVien_Id
    FROM dbo.DM_BenhVien AS bv
    WHERE ISNULL(bv.TamNgung, 0) = 0
      AND (
          LTRIM(RTRIM(ISNULL(bv.MaBenhVien, ''))) = LTRIM(RTRIM(@MaBenhVienKCB))
          OR LTRIM(RTRIM(ISNULL(bv.TenBenhVien_En, ''))) = LTRIM(RTRIM(@MaBenhVienKCB))
          OR REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(bv.MaBenhVien, ''))), '-', ''), '.', '') = REPLACE(REPLACE(LTRIM(RTRIM(@MaBenhVienKCB)), '-', ''), '.', '')
          OR REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(bv.TenBenhVien_En, ''))), '-', ''), '.', '') = REPLACE(REPLACE(LTRIM(RTRIM(@MaBenhVienKCB)), '-', ''), '.', '')
          OR bv.TenBenhVien LIKE N'%Thiện Hạnh%'
      )
    ORDER BY
        CASE 
            WHEN LTRIM(RTRIM(ISNULL(bv.MaBenhVien, ''))) = LTRIM(RTRIM(@MaBenhVienKCB)) THEN 0
            WHEN LTRIM(RTRIM(ISNULL(bv.TenBenhVien_En, ''))) = LTRIM(RTRIM(@MaBenhVienKCB)) THEN 1
            WHEN REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(bv.MaBenhVien, ''))), '-', ''), '.', '') = LTRIM(RTRIM(@MaBenhVienKCB)) THEN 2
            WHEN bv.TenBenhVien LIKE N'%Thiện Hạnh%' THEN 3
            ELSE 4
        END,
        bv.BenhVien_Id;

    IF @BenhVien_KCB_Id IS NULL
    BEGIN
        SELECT TOP (1) @BenhVien_KCB_Id = bv.BenhVien_Id
        FROM dbo.Sys_AppSettings AS cfg
        JOIN dbo.DM_BenhVien AS bv
          ON bv.MaBenhVien = cfg.Value OR bv.TenBenhVien_En = cfg.Value
        WHERE cfg.Code IN (N'MaNoiKCBBanDau', N'MaBenhVien_BHYT', N'MaCSKCB')
          AND ISNULL(bv.TamNgung, 0) = 0;
    END;

    IF @BenhVien_KCB_Id IS NULL
    BEGIN
        SELECT TOP (1) @BenhVien_KCB_Id = tn.BenhVien_KCB_Id
        FROM dbo.TiepNhan AS tn
        WHERE tn.BenhVien_KCB_Id IS NOT NULL AND tn.BenhVien_KCB_Id > 0
        ORDER BY tn.TiepNhan_Id DESC;
    END;

    IF @BenhVien_KCB_Id IS NULL
    BEGIN
        SELECT TOP (1) @BenhVien_KCB_Id = bv.BenhVien_Id
        FROM dbo.DM_BenhVien AS bv
        WHERE ISNULL(bv.TamNgung, 0) = 0
        ORDER BY bv.BenhVien_Id;
    END;

    SET @TinhThanh_CapThe_Id = ISNULL(@TinhThanh_CapThe_Id, @TinhThanh_Id);
END;
""";

        const string marker1 = "/* Người thực hiện và nơi tiếp nhận. */";
        if (rawScript.Contains(marker1))
        {
            rawScript = rawScript.Replace(marker1, declarations.Trim() + "\r\n\r\n" + marker1);
        }

        const string marker2 = "/* =========================================================================\r\n   3. KIỂM TRA TRƯỚC KHI GHI DỮ LIỆU\r\n   ========================================================================= */";
        const string marker2Alt = "/* =========================================================================\n   3. KIỂM TRA TRƯỚC KHI GHI DỮ LIỆU\n   ========================================================================= */";
        if (rawScript.Contains(marker2))
        {
            rawScript = rawScript.Replace(marker2, autoLookupSql.Trim() + "\r\n\r\n" + marker2);
        }
        else if (rawScript.Contains(marker2Alt))
        {
            rawScript = rawScript.Replace(marker2Alt, autoLookupSql.Trim() + "\n\n" + marker2Alt);
        }

        return rawScript;
    }

    private static string DefaultAdmissionScriptTemplate()
    {
        return """
/*
    TẠO BỆNH NHÂN MỚI, TIẾP NHẬN TRỰC TIẾP VÀ CHỈ ĐỊNH VÀO KHOA
    -------------------------------------------------------------------------
    Hỗ trợ:
      @LoaiHoSo = 'VIEN_PHI' : bệnh nhân không có BHYT.
      @LoaiHoSo = 'BHYT'     : bệnh nhân có đầy đủ thông tin BHYT bắt buộc.

    Phạm vi:
      - Tạo DM_BenhNhan.
      - Nếu là BHYT, tạo DM_BenhNhan_BHYT.
      - Tạo TiepNhan.
      - Tạo NoiTru_NhapVien bằng action AddNew.
      - KHÔNG gọi AddNewAndCreateBenhAn.
      - KHÔNG tạo BenhAn, NoiTru_LuuTru hoặc số bệnh án.

    An toàn:
      @Commit = 0 là mặc định: chạy toàn bộ rồi ROLLBACK.
      Chỉ đặt @Commit = 1 sau khi đã kiểm tra kết quả chạy thử.

    Cách dùng:
      1. Chạy Scripts/01_lookup_tiep_nhan_nhap_vien.sql để lấy ID.
      2. Điền phần "THAM SỐ CẦN ĐIỀN" bên dưới.
      3. Chạy lần đầu với @Commit = 0.
      4. Kiểm tra BenhAn_Id phải luôn là NULL.
*/

USE [eHospital_ThienHanh];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* =========================================================================
   1. THAM SỐ CẦN ĐIỀN
   ========================================================================= */

DECLARE @LoaiHoSo varchar(20) = 'VIEN_PHI'; -- VIEN_PHI | BHYT
DECLARE @Commit bit = 0;                    -- 0 = ROLLBACK | 1 = COMMIT
DECLARE @NgayGioNghiepVu datetime = GETDATE();

DECLARE @TenDangNhap nvarchar(100) = N'ANHPH';
DECLARE @TenKhoaDieuTri nvarchar(200) = N'Khoa Ngoại tổng hợp';
DECLARE @TenTinhThanh nvarchar(150) = N'Đắk Lắk';
DECLARE @MaICD varchar(20) = 'R69';
DECLARE @MaBenhVienKCB varchar(20) = '66232';



/* Người thực hiện và nơi tiếp nhận. */
DECLARE @User_Id int = NULL;                -- User đăng nhập, không phải NhanVien_Id
DECLARE @NoiTiepNhan_Id int = NULL;         -- DM_PhongBan.PhongBan_Id
DECLARE @HinhThucDenKham_Id int = NULL;     -- Lst_Dictionary/HinhThucDenKhamBenh
DECLARE @NoiGioiThieu_Id int = NULL;        -- bắt buộc nếu hình thức là GioiThieu_*
DECLARE @LyDoTiepNhan_Id int = NULL;        -- để NULL: tự lấy LyDoTiepNhan code 3

/* Bệnh nhân mới: các trường bắt buộc trên màn hình tiếp nhận. */
DECLARE @TenBenhNhan nvarchar(40) = N'TEST TIEP NHAN VAO KHOA';
DECLARE @GioiTinh char(1) = 'T';            -- T = nam | G = nữ
DECLARE @NgaySinh smalldatetime = '1990-01-01';
DECLARE @DanToc_Id int = NULL;               -- Lst_Dictionary/DanToc
DECLARE @QuocTich_Id int = NULL;             -- để NULL: tự lấy QuocGia/VN
DECLARE @NgheNghiep_Id int = NULL;           -- danh mục nghề nghiệp
DECLARE @TinhThanh_Id int = NULL;            -- DM_DonViHanhChinh cấp tỉnh

/* Địa chỉ chi tiết không bắt buộc; điền nếu cần. */
DECLARE @QuanHuyen_Id int = NULL;
DECLARE @XaPhuong_Id int = NULL;
DECLARE @SoNha nvarchar(150) = NULL;
DECLARE @DiaChiThuongTru nvarchar(150) = NULL;
DECLARE @SoDienThoai varchar(30) = NULL;

/* Chỉ định vào khoa, nhưng chưa nhận vào khoa và chưa tạo bệnh án. */
DECLARE @NoiNhapVien_Id int = NULL;          -- để NULL: dùng @NoiTiepNhan_Id
DECLARE @KhoaDieuTri_Id int = NULL;          -- khoa nội trú đích
DECLARE @BacSiChiDinh_Id int = NULL;         -- NhanVien.NhanVien_Id
DECLARE @LyDoNhapVien_Id int = NULL;         -- Lst_Dictionary/LyDoNhapVien
DECLARE @ICD_Id int = NULL;                  -- DM_ICD.ICD_Id
DECLARE @ChanDoan nvarchar(200) = N'TEST CHỈ ĐỊNH VÀO KHOA';
DECLARE @CapCuu bit = 0;

/*
    Chỉ điền khối này khi @LoaiHoSo = 'BHYT'.
    Số thẻ dùng định dạng hiện hành 15 ký tự, không có dấu cách/gạch nối.
*/
DECLARE @SoBHYT varchar(30) = NULL;
DECLARE @LoaiBHYT int = NULL;                -- Lst_Dictionary/TiepNhanLoaiBHYT
DECLARE @BHYTTuNgay smalldatetime = NULL;
DECLARE @BHYTDenNgay smalldatetime = NULL;
DECLARE @TuyenKhamBenh_Id int = NULL;        -- Lst_Dictionary/TuyenKhamChuaBenh
DECLARE @BenhVien_KCB_Id int = NULL;          -- DM_BenhVien.BenhVien_Id

/* Các trường BHYT sau không bắt buộc trên màn hình; điền khi thẻ có dữ liệu. */
DECLARE @TinhThanh_CapThe_Id int = NULL;
DECLARE @NoiSinhSong_Id int = NULL;          -- Lst_Dictionary/DoiTuong_NoiSinhSong
DECLARE @NgayMienDongChiTra smalldatetime = NULL;

/* =========================================================================
   2. BIẾN NỘI BỘ
   ========================================================================= */

DECLARE @FreePara nvarchar(1000) = NULL;
DECLARE @BenhNhan_Id int = NULL;
DECLARE @BenhNhan_BHYT_Id int = NULL;
DECLARE @TiepNhan_Id int = NULL;
DECLARE @NhapVien_Id int = NULL;
DECLARE @BenhAn_Id int = NULL;

DECLARE @DoiTuong_Id int = NULL;
DECLARE @MaDoiTuong nvarchar(1000) = NULL;
DECLARE @SoTiepNhan nvarchar(20) = NULL;
DECLARE @SoThuTu nvarchar(20) = NULL;
DECLARE @MaNoiNhapVien nvarchar(50) = NULL;
DECLARE @HinhThucDenKham_Code nvarchar(100) = NULL;
DECLARE @NamSinh smallint = YEAR(@NgaySinh);
DECLARE @NamTiepNhan smallint = YEAR(@NgayGioNghiepVu);
DECLARE @ThangTiepNhan tinyint = MONTH(@NgayGioNghiepVu);
DECLARE @ErrorMessage nvarchar(2048);

/* Giá trị snapshot BHYT truyền vào TiepNhan. */
DECLARE @TN_SoBHYT varchar(30) = NULL;
DECLARE @TN_LoaiBHYT int = 0;
DECLARE @TN_BHYTTuNgay smalldatetime = NULL;
DECLARE @TN_BHYTDenNgay smalldatetime = NULL;
DECLARE @TN_TuyenKhamBenh_Id int = 0;
DECLARE @TN_BenhVien_KCB_Id int = NULL;
DECLARE @TN_NoiSinhSong_Id int = NULL;
DECLARE @TN_NgayMienDongChiTra smalldatetime = NULL;

SET @LoaiHoSo = UPPER(LTRIM(RTRIM(@LoaiHoSo)));
SET @GioiTinh = UPPER(LTRIM(RTRIM(@GioiTinh)));
SET @NoiNhapVien_Id = ISNULL(@NoiNhapVien_Id, @NoiTiepNhan_Id);

/* =========================================================================
   2.1. TỰ ĐỘNG TRA CỨU ID TỪ DANH MỤC HIS
   ========================================================================= */

DECLARE @TenKhoaCotLoi nvarchar(200) =
    LTRIM(RTRIM(REPLACE(@TenKhoaDieuTri, N'Khoa ', N'')));

SELECT TOP (1) @User_Id = u.User_Id
FROM dbo.Sys_Users AS u
WHERE ISNULL(u.Suspend, 0) = 0
  AND (
      UPPER(LTRIM(RTRIM(u.User_Code))) = UPPER(LTRIM(RTRIM(@TenDangNhap)))
      OR UPPER(LTRIM(RTRIM(u.User_Name))) = UPPER(LTRIM(RTRIM(@TenDangNhap)))
  )
ORDER BY
    CASE WHEN UPPER(LTRIM(RTRIM(u.User_Code))) =
                   UPPER(LTRIM(RTRIM(@TenDangNhap))) THEN 0 ELSE 1 END,
    u.User_Id;

SELECT TOP (1)
    @NoiTiepNhan_Id = pb.PhongBan_Id,
    @KhoaDieuTri_Id = pb.PhongBan_Id
FROM dbo.DM_PhongBan AS pb
JOIN dbo.Lst_Dictionary AS loai
  ON loai.Dictionary_Id = pb.LoaiPhongBan_Id
WHERE ISNULL(pb.TamNgung, 0) = 0
  AND loai.Dictionary_Code = 'KhoaNoi'
  AND (
      LTRIM(RTRIM(pb.TenPhongBan)) = LTRIM(RTRIM(@TenKhoaDieuTri))
      OR LTRIM(RTRIM(ISNULL(pb.TenKhongDau, N''))) =
         LTRIM(RTRIM(@TenKhoaDieuTri))
      OR pb.TenPhongBan LIKE N'%' + @TenKhoaCotLoi + N'%'
      OR ISNULL(pb.TenKhongDau, N'') LIKE N'%' + @TenKhoaCotLoi + N'%'
  )
ORDER BY
    CASE WHEN LTRIM(RTRIM(pb.TenPhongBan)) =
                   LTRIM(RTRIM(@TenKhoaDieuTri)) THEN 0 ELSE 1 END,
    pb.PhongBan_Id;

SET @NoiNhapVien_Id = ISNULL(@NoiNhapVien_Id, @NoiTiepNhan_Id);

SELECT TOP (1) @HinhThucDenKham_Id = d.Dictionary_Id
FROM dbo.Lst_Dictionary AS d
WHERE d.Dictionary_Type_Code = 'HinhThucDenKhamBenh'
  AND ISNULL(d.Enabled, 1) = 1
  AND (
      d.Dictionary_Code IN ('TuDen', 'HinhThucDenKhamBenh_TuDen')
      OR d.Dictionary_Name LIKE N'%tự đến%'
  )
ORDER BY
    CASE WHEN d.Dictionary_Code = 'TuDen' THEN 0
         WHEN d.Dictionary_Code = 'HinhThucDenKhamBenh_TuDen' THEN 1
         ELSE 2 END,
    d.Dictionary_Id;

SELECT TOP (1) @DanToc_Id = d.Dictionary_Id
FROM dbo.Lst_Dictionary AS d
WHERE d.Dictionary_Type_Code = 'DanToc'
  AND ISNULL(d.Enabled, 1) = 1
ORDER BY
    CASE WHEN d.Dictionary_Name = N'Kinh'
              OR d.Dictionary_Code IN ('1', '01', 'Kinh') THEN 0 ELSE 1 END,
    d.Dictionary_Id;

SELECT TOP (1) @NgheNghiep_Id = d.Dictionary_Id
FROM dbo.Lst_Dictionary AS d
WHERE d.Dictionary_Type_Code = 'NgheNghiep'
  AND ISNULL(d.Enabled, 1) = 1
  AND LEN(ISNULL(d.Dictionary_Code, N'')) >= 5
ORDER BY
    CASE WHEN d.Dictionary_Name LIKE N'%khác%' THEN 0 ELSE 1 END,
    d.Dictionary_Id;

SELECT TOP (1) @TinhThanh_Id = dv.DonViHanhChinh_Id
FROM dbo.DM_DonViHanhChinh AS dv
WHERE dv.CapDonVi = 2
  AND ISNULL(dv.TamNgung, 0) = 0
  AND (
      LTRIM(RTRIM(dv.TenDonVi)) = LTRIM(RTRIM(@TenTinhThanh))
      OR LTRIM(RTRIM(ISNULL(dv.TenKhongDau, N''))) =
         LTRIM(RTRIM(@TenTinhThanh))
      OR dv.TenDonVi LIKE N'%' + LTRIM(RTRIM(@TenTinhThanh)) + N'%'
  )
ORDER BY
    CASE WHEN LTRIM(RTRIM(dv.TenDonVi)) =
                   LTRIM(RTRIM(@TenTinhThanh)) THEN 0 ELSE 1 END,
    dv.DonViHanhChinh_Id;

SELECT TOP (1) @BacSiChiDinh_Id = nv.NhanVien_Id
FROM dbo.NhanVien AS nv
LEFT JOIN dbo.Lst_Dictionary AS cd
  ON cd.Dictionary_Id = nv.ChucDanh_Id
 AND cd.Dictionary_Type_Code = 'ChucDanh'
WHERE ISNULL(nv.TamNgung, 0) = 0
  AND nv.PhongBan_Id = @KhoaDieuTri_Id
ORDER BY
    CASE WHEN cd.Dictionary_Code LIKE 'BS%'
              OR cd.Dictionary_Name LIKE N'%bác sĩ%' THEN 0 ELSE 1 END,
    nv.NhanVien_Id;

IF @BacSiChiDinh_Id IS NULL
BEGIN
    SELECT TOP (1) @BacSiChiDinh_Id = nv.NhanVien_Id
    FROM dbo.NhanVien AS nv
    JOIN dbo.Lst_Dictionary AS cd
      ON cd.Dictionary_Id = nv.ChucDanh_Id
     AND cd.Dictionary_Type_Code = 'ChucDanh'
    WHERE ISNULL(nv.TamNgung, 0) = 0
      AND (
          cd.Dictionary_Code LIKE 'BS%'
          OR cd.Dictionary_Name LIKE N'%bác sĩ%'
      )
    ORDER BY nv.NhanVien_Id;
END;

SELECT TOP (1) @LyDoNhapVien_Id = d.Dictionary_Id
FROM dbo.Lst_Dictionary AS d
WHERE d.Dictionary_Type_Code = 'LyDoNhapVien'
  AND ISNULL(d.Enabled, 1) = 1
ORDER BY
    CASE WHEN d.Dictionary_Name LIKE N'%nhập viện%' THEN 0 ELSE 1 END,
    ISNULL(d.Idx, 2147483647),
    d.Dictionary_Id;

SELECT TOP (1) @ICD_Id = icd.ICD_Id
FROM dbo.DM_ICD AS icd
WHERE UPPER(LTRIM(RTRIM(icd.MaICD))) = UPPER(LTRIM(RTRIM(@MaICD)))
  AND ISNULL(icd.TamNgung, 0) = 0
ORDER BY icd.ICD_Id;

IF @LoaiHoSo = 'BHYT'
BEGIN
    SELECT TOP (1) @LoaiBHYT = d.Dictionary_Id
    FROM dbo.Lst_Dictionary AS d
    WHERE d.Dictionary_Type_Code = 'TiepNhanLoaiBHYT'
      AND ISNULL(d.Enabled, 1) = 1
    ORDER BY
        CASE WHEN d.Dictionary_Name LIKE N'%bắt buộc%' THEN 0 ELSE 1 END,
        ISNULL(d.Idx, 2147483647),
        d.Dictionary_Id;

    SELECT TOP (1) @TuyenKhamBenh_Id = d.Dictionary_Id
    FROM dbo.Lst_Dictionary AS d
    WHERE d.Dictionary_Type_Code = 'TuyenKhamChuaBenh'
      AND d.Dictionary_Code = 'TuyenKhamChuaBenh_TrongTuyen'
      AND ISNULL(d.Enabled, 1) = 1
    ORDER BY d.Dictionary_Id;

    SELECT TOP (1) @BenhVien_KCB_Id = bv.BenhVien_Id
    FROM dbo.DM_BenhVien AS bv
    WHERE ISNULL(bv.TamNgung, 0) = 0
      AND (
          LTRIM(RTRIM(ISNULL(bv.MaBenhVien, ''))) =
              LTRIM(RTRIM(@MaBenhVienKCB))
          OR LTRIM(RTRIM(ISNULL(bv.TenBenhVien_En, ''))) =
              LTRIM(RTRIM(@MaBenhVienKCB))
      )
    ORDER BY
        CASE WHEN LTRIM(RTRIM(ISNULL(bv.MaBenhVien, ''))) =
                       LTRIM(RTRIM(@MaBenhVienKCB)) THEN 0 ELSE 1 END,
        bv.BenhVien_Id;

    SET @TinhThanh_CapThe_Id = ISNULL(@TinhThanh_CapThe_Id, @TinhThanh_Id);
END;



/* =========================================================================
   3. KIỂM TRA TRƯỚC KHI GHI DỮ LIỆU
   ========================================================================= */

IF @@TRANCOUNT <> 0
    THROW 51000, N'Script phải được chạy khi không có transaction đang mở.', 1;

IF ISNULL(@LoaiHoSo, '') NOT IN ('VIEN_PHI', 'BHYT')
    THROW 51000, N'@LoaiHoSo chỉ nhận VIEN_PHI hoặc BHYT.', 1;

IF @User_Id IS NULL
    THROW 51000, N'Chưa điền @User_Id.', 1;

IF NULLIF(LTRIM(RTRIM(@TenBenhNhan)), N'') IS NULL
    THROW 51000, N'Chưa điền @TenBenhNhan.', 1;

IF ISNULL(@GioiTinh, '') NOT IN ('T', 'G')
    THROW 51000, N'@GioiTinh chỉ nhận T (nam) hoặc G (nữ).', 1;

IF @NgaySinh IS NULL OR @NgaySinh >= @NgayGioNghiepVu
    THROW 51000, N'@NgaySinh phải nhỏ hơn thời gian tiếp nhận.', 1;

IF @DanToc_Id IS NULL OR @NgheNghiep_Id IS NULL OR @TinhThanh_Id IS NULL
    THROW 51000, N'Chưa đủ @DanToc_Id, @NgheNghiep_Id và @TinhThanh_Id.', 1;

IF @NoiTiepNhan_Id IS NULL OR @HinhThucDenKham_Id IS NULL
    THROW 51000, N'Chưa đủ @NoiTiepNhan_Id và @HinhThucDenKham_Id.', 1;

IF @KhoaDieuTri_Id IS NULL OR @BacSiChiDinh_Id IS NULL
   OR @LyDoNhapVien_Id IS NULL OR @ICD_Id IS NULL
    THROW 51000, N'Chưa đủ khoa, bác sĩ, lý do nhập viện và ICD.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.DM_PhongBan
    WHERE PhongBan_Id = @NoiTiepNhan_Id
      AND ISNULL(TamNgung, 0) = 0
)
    THROW 51000, N'@NoiTiepNhan_Id không tồn tại hoặc đang tạm ngưng.', 1;

SELECT @MaNoiNhapVien = MaPhongBan
FROM dbo.DM_PhongBan
WHERE PhongBan_Id = @NoiNhapVien_Id
  AND ISNULL(TamNgung, 0) = 0;

IF @MaNoiNhapVien IS NULL
    THROW 51000, N'@NoiNhapVien_Id không tồn tại hoặc đang tạm ngưng.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.DM_PhongBan AS pb
    JOIN dbo.Lst_Dictionary AS loai
      ON loai.Dictionary_Id = pb.LoaiPhongBan_Id
    WHERE pb.PhongBan_Id = @KhoaDieuTri_Id
      AND loai.Dictionary_Code = 'KhoaNoi'
      AND ISNULL(pb.TamNgung, 0) = 0
)
    THROW 51000, N'@KhoaDieuTri_Id không phải khoa nội trú hoạt động.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.NhanVien
    WHERE NhanVien_Id = @BacSiChiDinh_Id
      AND ISNULL(TamNgung, 0) = 0
)
    THROW 51000, N'@BacSiChiDinh_Id không tồn tại hoặc đang tạm ngưng.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.DM_ICD
    WHERE ICD_Id = @ICD_Id
      AND ISNULL(TamNgung, 0) = 0
)
    THROW 51000, N'@ICD_Id không tồn tại hoặc đang tạm ngưng.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.Lst_Dictionary
    WHERE Dictionary_Id = @DanToc_Id
      AND Dictionary_Type_Code = 'DanToc'
      AND ISNULL(Enabled, 1) = 1
)
    THROW 51000, N'@DanToc_Id không thuộc danh mục DanToc đang hiệu lực.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.Lst_Dictionary
    WHERE Dictionary_Id = @NgheNghiep_Id
      AND Dictionary_Type_Code = 'NgheNghiep'
      AND ISNULL(Enabled, 1) = 1
)
    THROW 51000, N'@NgheNghiep_Id không thuộc danh mục NgheNghiep đang hiệu lực.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.DM_DonViHanhChinh
    WHERE DonViHanhChinh_Id = @TinhThanh_Id
      AND CapDonVi = 2
      AND ISNULL(TamNgung, 0) = 0
)
    THROW 51000, N'@TinhThanh_Id không phải đơn vị hành chính cấp tỉnh đang hiệu lực.', 1;

IF @QuanHuyen_Id IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM dbo.DM_DonViHanhChinh
       WHERE DonViHanhChinh_Id = @QuanHuyen_Id
         AND CapTren_Id = @TinhThanh_Id
         AND ISNULL(TamNgung, 0) = 0
   )
    THROW 51000, N'@QuanHuyen_Id không thuộc @TinhThanh_Id.', 1;

IF @XaPhuong_Id IS NOT NULL AND @QuanHuyen_Id IS NULL
    THROW 51000, N'Có @XaPhuong_Id nhưng chưa điền @QuanHuyen_Id.', 1;

IF @XaPhuong_Id IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM dbo.DM_DonViHanhChinh
       WHERE DonViHanhChinh_Id = @XaPhuong_Id
         AND CapTren_Id = @QuanHuyen_Id
         AND ISNULL(TamNgung, 0) = 0
   )
    THROW 51000, N'@XaPhuong_Id không thuộc @QuanHuyen_Id.', 1;

IF @QuocTich_Id IS NULL
BEGIN
    SELECT TOP (1) @QuocTich_Id = Dictionary_Id
    FROM dbo.Lst_Dictionary
    WHERE Dictionary_Type_Code = 'QuocGia'
      AND Dictionary_Code = 'VN'
      AND ISNULL(Enabled, 1) = 1
    ORDER BY Dictionary_Id;
END;

IF @QuocTich_Id IS NULL
   OR NOT EXISTS (
       SELECT 1
       FROM dbo.Lst_Dictionary
       WHERE Dictionary_Id = @QuocTich_Id
         AND Dictionary_Type_Code = 'QuocGia'
         AND ISNULL(Enabled, 1) = 1
   )
    THROW 51000, N'@QuocTich_Id không hợp lệ và không tự tìm được quốc gia VN.', 1;

SELECT @HinhThucDenKham_Code = Dictionary_Code
FROM dbo.Lst_Dictionary
WHERE Dictionary_Id = @HinhThucDenKham_Id
  AND Dictionary_Type_Code = 'HinhThucDenKhamBenh'
  AND ISNULL(Enabled, 1) = 1;

IF @HinhThucDenKham_Code IS NULL
    THROW 51000, N'@HinhThucDenKham_Id không thuộc HinhThucDenKhamBenh.', 1;

IF @HinhThucDenKham_Code LIKE 'GioiThieu[_]%'
   AND @NoiGioiThieu_Id IS NULL
    THROW 51000, N'Hình thức đến khám là giới thiệu nhưng thiếu @NoiGioiThieu_Id.', 1;

IF @LyDoTiepNhan_Id IS NULL
BEGIN
    SELECT TOP (1) @LyDoTiepNhan_Id = Dictionary_Id
    FROM dbo.Lst_Dictionary
    WHERE Dictionary_Type_Code = 'LyDoTiepNhan'
      AND Dictionary_Code = '3'
      AND ISNULL(Enabled, 1) = 1
    ORDER BY Dictionary_Id;
END;

IF @LyDoTiepNhan_Id IS NULL
   OR NOT EXISTS (
       SELECT 1
       FROM dbo.Lst_Dictionary
       WHERE Dictionary_Id = @LyDoTiepNhan_Id
         AND Dictionary_Type_Code = 'LyDoTiepNhan'
         AND ISNULL(Enabled, 1) = 1
   )
    THROW 51000, N'Không tìm thấy lý do tiếp nhận trực tiếp hợp lệ.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.Lst_Dictionary
    WHERE Dictionary_Id = @LyDoNhapVien_Id
      AND Dictionary_Type_Code = 'LyDoNhapVien'
      AND ISNULL(Enabled, 1) = 1
)
    THROW 51000, N'@LyDoNhapVien_Id không thuộc LyDoNhapVien.', 1;

/* Xác định đối tượng viện phí hoặc BHYT. */
IF @LoaiHoSo = 'VIEN_PHI'
BEGIN
    SELECT TOP (1) @DoiTuong_Id = DoiTuong_Id
    FROM dbo.DM_DoiTuong
    WHERE MaDoiTuong = 'VP'
      AND ISNULL(TamNgung, 0) = 0
    ORDER BY DoiTuong_Id;

    IF @DoiTuong_Id IS NULL
        THROW 51000, N'Không tìm thấy đối tượng viện phí MaDoiTuong=VP.', 1;
END
ELSE
BEGIN
    IF NULLIF(LTRIM(RTRIM(@SoBHYT)), '') IS NULL
       OR LEN(LTRIM(RTRIM(@SoBHYT))) <> 15
       OR LTRIM(RTRIM(@SoBHYT)) LIKE '% %'
       OR LTRIM(RTRIM(@SoBHYT)) LIKE '%-%'
        THROW 51000, N'@SoBHYT phải có đúng 15 ký tự, không có khoảng trắng/gạch nối.', 1;

    SET @SoBHYT = LTRIM(RTRIM(@SoBHYT));

    IF @BHYTTuNgay IS NULL OR @BHYTDenNgay IS NULL
        THROW 51000, N'Thẻ BHYT phải có ngày hiệu lực và ngày hết hạn.', 1;

    IF @BHYTTuNgay >= @BHYTDenNgay
        THROW 51000, N'Ngày hết hạn BHYT phải sau ngày hiệu lực.', 1;

    IF CAST(@NgayGioNghiepVu AS date) < CAST(@BHYTTuNgay AS date)
       OR CAST(@NgayGioNghiepVu AS date) > CAST(@BHYTDenNgay AS date)
        THROW 51000, N'Thẻ BHYT không có hiệu lực tại ngày tiếp nhận.', 1;

    IF @TuyenKhamBenh_Id IS NULL
        THROW 51000, N'Không phân giải được tuyến khám chữa bệnh (@TuyenKhamBenh_Id trong Lst_Dictionary).', 1;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.Lst_Dictionary
        WHERE Dictionary_Id = @TuyenKhamBenh_Id
    )
        THROW 51000, N'@TuyenKhamBenh_Id không tồn tại trong Lst_Dictionary.', 1;

    IF @BenhVien_KCB_Id IS NULL
        THROW 51000, N'Không phân giải được nơi đăng ký KCB ban đầu (@BenhVien_KCB_Id trong DM_BenhVien).', 1;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.DM_BenhVien
        WHERE BenhVien_Id = @BenhVien_KCB_Id
          AND ISNULL(TamNgung, 0) = 0
    )
        THROW 51000, N'@BenhVien_KCB_Id không tồn tại hoặc đang tạm ngưng trong DM_BenhVien.', 1;

    IF @LoaiBHYT IS NOT NULL AND @LoaiBHYT > 0
       AND NOT EXISTS (
           SELECT 1
           FROM dbo.Lst_Dictionary
           WHERE Dictionary_Id = @LoaiBHYT
       )
        THROW 51000, N'@LoaiBHYT không tồn tại trong Lst_Dictionary.', 1;

    IF @NoiSinhSong_Id IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM dbo.Lst_Dictionary
           WHERE Dictionary_Id = @NoiSinhSong_Id
             AND Dictionary_Type_Code = 'DoiTuong_NoiSinhSong'
             AND ISNULL(Enabled, 1) = 1
       )
        THROW 51000, N'@NoiSinhSong_Id không thuộc DoiTuong_NoiSinhSong.', 1;

    /* Nếu thẻ BHYT đã tồn tại trong DM_BenhNhan_BHYT: lấy BenhNhan_Id và BenhNhan_BHYT_Id có sẵn để tiếp nhận lại */
    IF EXISTS (
        SELECT 1
        FROM dbo.DM_BenhNhan_BHYT
        WHERE SoThe = @SoBHYT
          AND ISNULL(TamNgung, 0) = 0
    )
    BEGIN
        SELECT TOP (1)
            @BenhNhan_Id = BenhNhan_Id,
            @BenhNhan_BHYT_Id = BenhNhan_BHYT_Id
        FROM dbo.DM_BenhNhan_BHYT
        WHERE SoThe = @SoBHYT
          AND ISNULL(TamNgung, 0) = 0
        ORDER BY BenhNhan_BHYT_Id DESC;
    END;

    /*
        Màn hình tiếp nhận dùng 3 ký tự đầu thẻ + tuyến để suy ra
        MaDoiTuong (P1...P8 hoặc mã cấu hình tương ứng).
        Action này sử dụng @BenhNhan_Id để nhận TuyenKhamBenh_Id.
    */
    SET @MaDoiTuong = LEFT(@SoBHYT, 3);

    EXEC dbo.sp_TIEPNHAN
        @Action = N'GetDMDoiTuong_By_MaThe_TuyenKB',
        @LanguageID = N'VI',
        @UserID = @User_Id,
        @FreePara = @MaDoiTuong OUTPUT,
        @BenhNhan_Id = @TuyenKhamBenh_Id;

    SELECT TOP (1) @DoiTuong_Id = DoiTuong_Id
    FROM dbo.DM_DoiTuong
    WHERE MaDoiTuong = @MaDoiTuong
      AND ISNULL(TamNgung, 0) = 0
    ORDER BY DoiTuong_Id;

    IF @DoiTuong_Id IS NULL AND @BenhNhan_Id IS NOT NULL
    BEGIN
        SELECT TOP (1) @DoiTuong_Id = tn.DoiTuong_Id
        FROM dbo.TiepNhan AS tn
        JOIN dbo.DM_DoiTuong AS dt ON dt.DoiTuong_Id = tn.DoiTuong_Id
        WHERE tn.BenhNhan_Id = @BenhNhan_Id
          AND tn.SoBHYT = @SoBHYT
        ORDER BY tn.TiepNhan_Id DESC;
    END;

    IF @DoiTuong_Id IS NULL
    BEGIN
        SELECT TOP (1) @DoiTuong_Id = dt.DoiTuong_Id
        FROM dbo.DM_DoiTuong AS dt
        JOIN dbo.Lst_Dictionary AS ndt ON ndt.Dictionary_Id = dt.NhomDoiTuong_Id
        WHERE ndt.Dictionary_Code = 'BHYT'
          AND ISNULL(dt.TamNgung, 0) = 0
        ORDER BY dt.DoiTuong_Id;
    END;

    IF @DoiTuong_Id IS NULL
        THROW 51000, N'Không suy ra được DM_DoiTuong từ mã thẻ BHYT và tuyến khám.', 1;

    SET @TN_SoBHYT = @SoBHYT;
    SET @TN_LoaiBHYT = @LoaiBHYT;
    SET @TN_BHYTTuNgay = @BHYTTuNgay;
    SET @TN_BHYTDenNgay = @BHYTDenNgay;
    SET @TN_TuyenKhamBenh_Id = @TuyenKhamBenh_Id;
    SET @TN_BenhVien_KCB_Id = @BenhVien_KCB_Id;
    SET @TN_NoiSinhSong_Id = @NoiSinhSong_Id;
    SET @TN_NgayMienDongChiTra = @NgayMienDongChiTra;
END;

/* =========================================================================
   4. TẠO BỆNH NHÂN, TIẾP NHẬN VÀ DÒNG CHỜ VÀO KHOA
   ========================================================================= */

BEGIN TRY
    BEGIN TRANSACTION;

    /* 4.1 Tạo bệnh nhân mới nếu chưa có; MaYTe được procedure tự sinh. */
    IF @BenhNhan_Id IS NULL
    BEGIN
        SET @FreePara = N'1'; -- họ đứng trước tên theo cách lưu của màn hình

        EXEC dbo.sp_DM_BENHNHAN
            @Action = N'AddNew',
            @LanguageID = N'VI',
            @UserID = @User_Id,
            @FreePara = @FreePara OUTPUT,
            @BenhNhan_Id = @BenhNhan_Id OUTPUT,
            @MaYTe = NULL,
            @TenBenhNhan = @TenBenhNhan,
            @GioiTinh = @GioiTinh,
            @NgaySinh = @NgaySinh,
            @NamSinh = @NamSinh,
            @SoNha = @SoNha,
            @QuocTich_Id = @QuocTich_Id,
            @TinhThanh_Id = @TinhThanh_Id,
            @QuanHuyen_Id = @QuanHuyen_Id,
            @XaPhuong_Id = @XaPhuong_Id,
            @DanToc_Id = @DanToc_Id,
            @NgheNghiep_Id = @NgheNghiep_Id,
            @VietKieu = 0,
            @NguoiNuocNgoai = 0,
            @NgayTao = @NgayGioNghiepVu,
            @NguoiTao_Id = @User_Id,
            @SoDienThoai = @SoDienThoai,
            @DiaChiThuongTru = @DiaChiThuongTru,
            @TuVong = 0,
            @TamNgung = 0;

        IF @BenhNhan_Id IS NULL
            THROW 51000, N'Không tạo được DM_BenhNhan.', 1;
    END;

    /* 4.2 Lưu thẻ bệnh nhân nếu là hồ sơ BHYT và chưa có trong DM_BenhNhan_BHYT. */
    IF @LoaiHoSo = 'BHYT' AND @BenhNhan_BHYT_Id IS NULL
    BEGIN
        SET @FreePara = NULL;

        EXEC dbo.sp_DM_BENHNHAN_BHYT
            @Action = N'AddNew',
            @LanguageID = N'VI',
            @UserID = @User_Id,
            @FreePara = @FreePara OUTPUT,
            @BenhNhan_BHYT_Id = @BenhNhan_BHYT_Id OUTPUT,
            @BenhNhan_Id = @BenhNhan_Id,
            @LoaiBHYT = @LoaiBHYT,
            @SoThe = @SoBHYT,
            @NgayCap = NULL,
            @NgayHieuLuc = @BHYTTuNgay,
            @NgayHetHieuLuc = @BHYTDenNgay,
            @TinhThanh_CapThe_Id = @TinhThanh_CapThe_Id,
            @BenhVien_KCB_Id = @BenhVien_KCB_Id,
            @TamNgung = 0,
            @NgayTao = @NgayGioNghiepVu,
            @NguoiTao_Id = @User_Id,
            @NgayCapNhat = NULL,
            @NguoiCapNhat_Id = NULL;

        IF @BenhNhan_BHYT_Id IS NULL
            THROW 51000, N'Không tạo được DM_BenhNhan_BHYT.', 1;
    END;

    /* 4.3 Sinh số tiếp nhận và số thứ tự. */
    EXEC dbo.sp_LST_KEYDATA
        @pKeyCode = @SoTiepNhan OUTPUT,
        @pKeyType = N'MaTiepNhan',
        @pGetDate = @NgayGioNghiepVu;

    EXEC dbo.sp_LST_KEYDATA
        @pKeyCode = @SoThuTu OUTPUT,
        @pKeyType = N'SoThuTuTiepNhan',
        @pGetDate = @NgayGioNghiepVu;

    IF NULLIF(@SoTiepNhan, N'') IS NULL OR NULLIF(@SoThuTu, N'') IS NULL
        THROW 51000, N'Không sinh được số tiếp nhận hoặc số thứ tự.', 1;

    /* 4.4 Tạo tiếp nhận; thông tin BHYT được snapshot trên lần tiếp nhận. */
    SET @FreePara = NULL;

    EXEC dbo.sp_TIEPNHAN
        @Action = N'AddNew',
        @LanguageID = N'VI',
        @UserID = @User_Id,
        @FreePara = @FreePara OUTPUT,
        @TiepNhan_Id = @TiepNhan_Id OUTPUT,
        @SoTiepNhan = @SoTiepNhan,
        @SoThuTu = @SoThuTu,
        @UuTien = 0,
        @BenhNhan_Id = @BenhNhan_Id,
        @NoiTiepNhan_Id = @NoiTiepNhan_Id,
        @NgayTiepNhan = @NgayGioNghiepVu,
        @ThoiGianTiepNhan = @NgayGioNghiepVu,
        @NamTiepNhan = @NamTiepNhan,
        @ThangTiepNhan = @ThangTiepNhan,
        @DoiTuong_Id = @DoiTuong_Id,
        @HinhThucDenKham_Id = @HinhThucDenKham_Id,
        @NoiGioiThieu_Id = @NoiGioiThieu_Id,
        @LyDoTiepNhan_Id = @LyDoTiepNhan_Id,
        @SoBHYT = @TN_SoBHYT,
        @BHYTTuNgay = @TN_BHYTTuNgay,
        @BHYTDenNgay = @TN_BHYTDenNgay,
        @ThuTienTruoc = 1,
        @TrangThai = NULL,
        @NgayTao = @NgayGioNghiepVu,
        @NguoiTao_Id = @User_Id,
        @TaiKham = 0,
        @TuyenKhamBenh_Id = @TN_TuyenKhamBenh_Id,
        @TinhThanh_Id = @TinhThanh_Id,
        @QuanHuyen_Id = @QuanHuyen_Id,
        @XaPhuong_Id = @XaPhuong_Id,
        @LoaiBHYT = @TN_LoaiBHYT,
        @BenhVien_KCB_Id = @TN_BenhVien_KCB_Id,
        @NoiSinhSong_Id = @TN_NoiSinhSong_Id,
        @NgayMienDongChiTra = @TN_NgayMienDongChiTra;

    IF @TiepNhan_Id IS NULL
        THROW 51000, N'Không tạo được TiepNhan.', 1;

    /*
        4.5 Chỉ tạo lệnh vào khoa.
        Cố định Action=AddNew để không thể vô tình sinh bệnh án.
    */
    SET @FreePara = @MaNoiNhapVien;

    EXEC dbo.sp_NOITRU_NHAPVIEN
        @Action = N'AddNew',
        @LanguageID = N'VI',
        @UserID = @User_Id,
        @FreePara = @FreePara OUTPUT,
        @NhapVien_Id = @NhapVien_Id OUTPUT,
        @TiepNhan_Id = @TiepNhan_Id,
        @NgayNhapVien = @NgayGioNghiepVu,
        @ThoiGianNhapVien = @NgayGioNghiepVu,
        @NoiNhapVien_Id = @NoiNhapVien_Id,
        @BacSiChiDinh_Id = @BacSiChiDinh_Id,
        @LyDoNhapVien_Id = @LyDoNhapVien_Id,
        @KhoaDieuTri_Id = @KhoaDieuTri_Id,
        @BenhAn_Id = NULL,
        @ChanDoan = @ChanDoan,
        @ICD_ChanDoan = @ICD_Id,
        @TrangThai = NULL,
        @NgayTao = @NgayGioNghiepVu,
        @NguoiTao_Id = @User_Id,
        @KhamBenh_Id = NULL,
        @CapCuu = @CapCuu;

    IF @NhapVien_Id IS NULL
        THROW 51000, N'Không tạo được NoiTru_NhapVien.', 1;

    SELECT @BenhAn_Id = BenhAn_Id
    FROM dbo.NoiTru_NhapVien
    WHERE NhapVien_Id = @NhapVien_Id;

    IF @BenhAn_Id IS NOT NULL
        THROW 51000, N'Sai phạm vi: NoiTru_NhapVien đã phát sinh BenhAn_Id.', 1;

    /* Kết quả kiểm tra trước COMMIT/ROLLBACK. */
    SELECT
        N'KET_QUA_TRUOC_KHI_KET_THUC_TRANSACTION' AS NhomKetQua,
        @LoaiHoSo AS LoaiHoSo,
        @Commit AS SeCommit,
        @BenhNhan_Id AS BenhNhan_Id,
        @BenhNhan_BHYT_Id AS BenhNhan_BHYT_Id,
        @TiepNhan_Id AS TiepNhan_Id,
        @NhapVien_Id AS NhapVien_Id,
        @BenhAn_Id AS BenhAn_Id;

    SELECT
        bn.BenhNhan_Id,
        bn.MaYTe,
        bn.TenBenhNhan,
        bn.GioiTinh,
        bn.NgaySinh,
        tn.TiepNhan_Id,
        tn.SoTiepNhan,
        tn.DoiTuong_Id,
        dt.MaDoiTuong,
        tn.SoBHYT,
        tn.BHYTTuNgay,
        tn.BHYTDenNgay,
        tn.TuyenKhamBenh_Id,
        tn.BenhVien_KCB_Id,
        nv.NhapVien_Id,
        nv.NoiNhapVien_Id,
        nv.KhoaDieuTri_Id,
        nv.BenhAn_Id,
        nv.TrangThai
    FROM dbo.DM_BenhNhan AS bn
    JOIN dbo.TiepNhan AS tn
      ON tn.BenhNhan_Id = bn.BenhNhan_Id
    JOIN dbo.DM_DoiTuong AS dt
      ON dt.DoiTuong_Id = tn.DoiTuong_Id
    JOIN dbo.NoiTru_NhapVien AS nv
      ON nv.TiepNhan_Id = tn.TiepNhan_Id
    WHERE bn.BenhNhan_Id = @BenhNhan_Id
      AND tn.TiepNhan_Id = @TiepNhan_Id
      AND nv.NhapVien_Id = @NhapVien_Id;

    IF @LoaiHoSo = 'BHYT'
    BEGIN
        SELECT
            BenhNhan_BHYT_Id,
            BenhNhan_Id,
            SoThe,
            LoaiBHYT,
            NgayHieuLuc,
            NgayHetHieuLuc,
            BenhVien_KCB_Id,
            TamNgung
        FROM dbo.DM_BenhNhan_BHYT
        WHERE BenhNhan_BHYT_Id = @BenhNhan_BHYT_Id;
    END;

    IF @Commit = 1
        COMMIT TRANSACTION;
    ELSE
        ROLLBACK TRANSACTION;

    SELECT
        CASE WHEN @Commit = 1
             THEN N'COMMIT - đã lưu bệnh nhân, tiếp nhận và dòng chờ vào khoa'
             ELSE N'ROLLBACK - chỉ chạy thử, không lưu dữ liệu'
        END AS KetQuaCuoi,
        @LoaiHoSo AS LoaiHoSo,
        @BenhNhan_Id AS BenhNhan_Id,
        @BenhNhan_BHYT_Id AS BenhNhan_BHYT_Id,
        @TiepNhan_Id AS TiepNhan_Id,
        @NhapVien_Id AS NhapVien_Id,
        @BenhAn_Id AS BenhAn_Id;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    SET @ErrorMessage = CONCAT(
        N'Lỗi tại dòng ', ERROR_LINE(),
        N', procedure ', COALESCE(ERROR_PROCEDURE(), N'(script)'),
        N': ', ERROR_MESSAGE()
    );

    THROW 51000, @ErrorMessage, 1;
END CATCH;

""";
    }
}
