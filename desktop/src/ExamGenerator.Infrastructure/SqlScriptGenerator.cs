using System.Text;
using System.Text.RegularExpressions;
using ExamGenerator.Domain;

namespace ExamGenerator.Infrastructure;

public sealed class SqlScriptGenerator
{
    private readonly string _admissionScriptTemplate;

    public SqlScriptGenerator(string? scriptTemplateContent = null)
    {
        _admissionScriptTemplate = scriptTemplateContent ?? DefaultAdmissionScriptTemplate();
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
        sb.AppendLine("USE [eHospital_ThienHanh];");
        sb.AppendLine("GO");
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine("SET XACT_ABORT ON;");
        sb.AppendLine();

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

        script = SetSqlVariable(script, "LoaiHoSo", "varchar(20)", $"'{loaiHoSo}'");
        script = SetSqlVariable(script, "Commit", "bit", "1");
        script = SetSqlVariable(script, "NgayGioNghiepVu", "datetime", $"'{scenario.ExamDate:yyyy-MM-dd} 07:30:00'");
        script = SetSqlVariable(script, "TenBenhNhan", "nvarchar(40)", $"N'{EscapeSql(p.FullName)}'");
        script = SetSqlVariable(script, "GioiTinh", "char(1)", $"'{genderCode}'");
        script = SetSqlVariable(script, "NgaySinh", "smalldatetime", $"'{birthDate:yyyy-MM-dd}'");
        script = SetSqlVariable(script, "SoNha", "nvarchar(150)", $"N'{EscapeSql(p.Address)}'");
        script = SetSqlVariable(script, "TenUserDangNhap", "nvarchar(100)", $"N'{EscapeSql(scenario.HisUserCode)}'");
        script = SetSqlVariable(script, "TenKhoaDieuTri", "nvarchar(250)", $"N'{EscapeSql(scenario.DepartmentName)}'");
        script = SetSqlVariable(script, "ChanDoanVaoKhoa", "nvarchar(250)", $"N'{EscapeSql(p.Diagnosis)}'");

        if (isBhyt)
        {
            script = SetSqlVariable(script, "SoBHYT", "varchar(15)", $"'{EscapeSql(p.InsuranceNumber!)}'");
            script = SetSqlVariable(script, "BHYT_TuNgay", "date", $"'{cardFrom:yyyy-MM-dd}'");
            script = SetSqlVariable(script, "BHYT_DenNgay", "date", $"'{cardTo:yyyy-MM-dd}'");
            script = SetSqlVariable(script, "MaBenhVienKCB", "varchar(20)", $"'{EscapeSql(dkkcb)}'");
        }

        return script;
    }

    private static string SetSqlVariable(string script, string variableName, string dataType, string valueExpression)
    {
        var pattern = $@"(?m)^DECLARE\s+@{Regex.Escape(variableName)}\b[^\r\n]*$";
        var replacement = $"DECLARE @{variableName} {dataType} = {valueExpression};";
        if (Regex.IsMatch(script, pattern))
        {
            return Regex.Replace(script, pattern, replacement, RegexOptions.None);
        }
        return $"DECLARE @{variableName} {dataType} = {valueExpression};\r\n" + script;
    }

    private static string EscapeSql(string value) => (value ?? string.Empty).Replace("'", "''");

    private static string DefaultAdmissionScriptTemplate()
    {
        return """
DECLARE @LoaiHoSo varchar(20) = 'BHYT';
DECLARE @Commit bit = 1;
DECLARE @NgayGioNghiepVu datetime = GETDATE();
DECLARE @TenBenhNhan nvarchar(40) = N'BỆNH NHÂN KHẢO THÍ';
DECLARE @GioiTinh char(1) = 'T';
DECLARE @NgaySinh smalldatetime = '1985-05-15';
DECLARE @SoNha nvarchar(150) = N'Số 123 Đường Phan Chu Trinh, Buôn Ma Thuột';
DECLARE @TenUserDangNhap nvarchar(100) = N'his_user';
DECLARE @TenKhoaDieuTri nvarchar(250) = N'Khoa Nội';
DECLARE @ChanDoanVaoKhoa nvarchar(250) = N'Viêm phế quản cấp';
DECLARE @SoBHYT varchar(15) = 'GD4662329876543';
DECLARE @BHYT_TuNgay date = '2026-01-01';
DECLARE @BHYT_DenNgay date = '2026-12-31';
DECLARE @MaBenhVienKCB varchar(20) = '66232';

DECLARE @User_Id int = NULL;
DECLARE @KhoaDieuTri_Id int = NULL;
DECLARE @BenhNhan_Id int = NULL;
DECLARE @TiepNhan_Id int = NULL;

-- Tìm User và Khoa điều trị
SELECT TOP (1) @User_Id = u.User_Id FROM dbo.Sys_Users u WHERE u.User_Name = @TenUserDangNhap;
SELECT TOP (1) @KhoaDieuTri_Id = pb.PhongBan_Id FROM dbo.DM_PhongBan pb WHERE pb.TenPhongBan = @TenKhoaDieuTri AND ISNULL(pb.TamNgung,0) = 0;

IF @User_Id IS NULL OR @KhoaDieuTri_Id IS NULL
BEGIN
    PRINT N'[LỖI]: Không tìm thấy User [' + @TenUserDangNhap + N'] hoặc Khoa [' + @TenKhoaDieuTri + N'] trong CSDL thi.';
    RETURN;
END

BEGIN TRANSACTION;

-- 1. Tạo Bệnh nhân
INSERT INTO dbo.DM_BenhNhan (TenBenhNhan, GioiTinh, NgaySinh, DiaChi, NgayTao, NguoiTao_Id)
VALUES (@TenBenhNhan, @GioiTinh, @NgaySinh, @SoNha, @NgayGioNghiepVu, @User_Id);
SET @BenhNhan_Id = SCOPE_IDENTITY();

-- 2. Tạo BHYT nếu có
IF @LoaiHoSo = 'BHYT' AND @SoBHYT IS NOT NULL
BEGIN
    INSERT INTO dbo.DM_BenhNhan_BHYT (BenhNhan_Id, SoBHYT, TuNgay, DenNgay, MaDKKCB, NgayTao, NguoiTao_Id)
    VALUES (@BenhNhan_Id, @SoBHYT, @BHYT_TuNgay, @BHYT_DenNgay, @MaBenhVienKCB, @NgayGioNghiepVu, @User_Id);
END

-- 3. Tạo Tiếp nhận & Nhập khoa (Chờ nhận tại khoa)
INSERT INTO dbo.TiepNhan (BenhNhan_Id, NgayTiepNhan, DoiTuong, NoiTiepNhan_Id, NguoiTao_Id)
VALUES (@BenhNhan_Id, @NgayGioNghiepVu, CASE WHEN @LoaiHoSo = 'BHYT' THEN 1 ELSE 2 END, @KhoaDieuTri_Id, @User_Id);
SET @TiepNhan_Id = SCOPE_IDENTITY();

INSERT INTO dbo.NoiTru_NhapVien (TiepNhan_Id, BenhNhan_Id, KhoaVao_Id, NgayVaoKhoa, ChanDoanVaoKhoa, NguoiTao_Id)
VALUES (@TiepNhan_Id, @BenhNhan_Id, @KhoaDieuTri_Id, @NgayGioNghiepVu, @ChanDoanVaoKhoa, @User_Id);

IF @Commit = 1
BEGIN
    COMMIT TRANSACTION;
    PRINT N'[THÀNH CÔNG]: Đã tạo ca bệnh [' + @TenBenhNhan + N'] chờ nhận tại [' + @TenKhoaDieuTri + N'] cho tài khoản [' + @TenUserDangNhap + N'].';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT N'[ROLLBACK]: Chạy thử nghiệm thành công (không lưu thay đổi).';
END
GO
""";
    }
}
