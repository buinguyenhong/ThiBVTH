"""Sinh T-SQL nạp dữ liệu thi cho CSDL eHospital_ThienHanh.

Luồng tạo bệnh nhân/tiếp nhận/chỉ định vào khoa được render trực tiếp từ tài
liệu nghiệp vụ ``Scripts/02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql``.
Nhờ đó tài liệu vận hành và mã do ứng dụng sinh ra dùng chung một nguồn.
"""

from __future__ import annotations

from datetime import date, datetime
from pathlib import Path
import re
from typing import Any, Optional


PROJECT_DIR = Path(__file__).resolve().parents[1]
ADMISSION_SCRIPT_PATH = (
    PROJECT_DIR
    / "Scripts"
    / "02_tao_benh_nhan_tiep_nhan_chi_dinh_vao_khoa.sql"
)

ADMISSION_QUEUE_TYPES = {
    "NURSE_INPATIENT",
    "RECEPTIONIST_INPATIENT",
}

DIRECT_RECEPTION_TYPES = {
    "NURSE_DIRECT_RECEPTION",
    "RECEPTIONIST_DIRECT",
    "NURSE_OUTPATIENT",
    "CASHIER_COUNTER",
}


class SqlGenerationError(ValueError):
    """Lỗi dữ liệu đầu vào khiến script HIS không thể được sinh an toàn."""


def _sql_text(value: Any, *, unicode: bool = True) -> str:
    if value is None:
        return "NULL"
    escaped = str(value).replace("'", "''").replace("\r", " ").replace("\n", " ")
    prefix = "N" if unicode else ""
    return f"{prefix}'{escaped}'"


def _comment_text(value: Any) -> str:
    return str(value or "").replace("\r", " ").replace("\n", " ").strip()


def _parse_date(value: Any, field_name: str) -> date:
    if isinstance(value, datetime):
        return value.date()
    if isinstance(value, date):
        return value

    raw = str(value or "").strip()
    for fmt in (
        "%d/%m/%Y",
        "%Y-%m-%d",
        "%Y-%m-%d %H:%M:%S",
        "%Y-%m-%dT%H:%M:%S",
    ):
        try:
            return datetime.strptime(raw, fmt).date()
        except ValueError:
            continue
    raise SqlGenerationError(
        f"{field_name}='{raw}' không đúng định dạng dd/mm/yyyy hoặc yyyy-mm-dd."
    )


def _gender_code(value: Any) -> str:
    normalized = str(value or "").strip().lower()
    if normalized in {"t", "nam", "male", "m"}:
        return "T"
    if normalized in {"g", "nữ", "nu", "female", "f"}:
        return "G"
    raise SqlGenerationError(f"Giới tính '{value}' không thể ánh xạ sang T/G.")


def _province_and_house(address: Any) -> tuple[str, str]:
    raw = str(address or "").strip()
    if not raw:
        raise SqlGenerationError("Bệnh nhân thiếu DiaChiLienHe.")

    parts = [part.strip() for part in raw.split(",") if part.strip()]
    province = parts[-1] if parts else ""
    house = ", ".join(parts[:-1]) if len(parts) > 1 else raw
    return province, house


def _set_declaration(script: str, variable: str, sql_type: str, expression: str) -> str:
    pattern = rf"(?m)^DECLARE @{re.escape(variable)}\b[^\r\n]*$"
    replacement = f"DECLARE @{variable} {sql_type} = {expression};"
    updated, count = re.subn(pattern, lambda m: replacement, script, count=1)
    if count != 1:
        raise SqlGenerationError(
            f"Không tìm thấy khai báo @{variable} trong tài liệu SQL nguồn."
        )
    return updated


def _auto_lookup_sql() -> str:
    """Khối tự phân giải các ID danh mục từ tên/mã có trong đề thi."""

    return r"""
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
"""


def _render_admission_block(
    patient: dict[str, Any],
    record_type: str,
    exam_date: Any,
    user_login: str,
    department_name: str,
    candidate_name: str,
    candidate_id: str,
    position: str,
) -> str:
    if not ADMISSION_SCRIPT_PATH.exists():
        raise SqlGenerationError(
            f"Thiếu tài liệu SQL nguồn: {ADMISSION_SCRIPT_PATH}"
        )

    business_date = _parse_date(exam_date, "exam_date")
    birth_date = _parse_date(patient.get("NgaySinh"), "NgaySinh")
    if birth_date >= business_date:
        raise SqlGenerationError(
            f"Ngày sinh của {patient.get('TenBenhNhan')} phải trước ngày thi."
        )

    province, house = _province_and_house(patient.get("DiaChiLienHe"))
    record_type = record_type.upper()
    if record_type not in {"BHYT", "VIEN_PHI"}:
        raise SqlGenerationError(f"Loại hồ sơ không hỗ trợ: {record_type}")

    card_number = str(patient.get("SoBHYT") or "").strip()
    card_from = None
    card_to = None
    hospital_code = str(patient.get("DKKCB") or "").strip() or "66232"

    if record_type == "BHYT":
        if len(card_number) != 15 or " " in card_number or "-" in card_number:
            raise SqlGenerationError(
                f"Số BHYT của {patient.get('TenBenhNhan')} phải đúng 15 ký tự."
            )
        card_from = _parse_date(patient.get("BHYTTuNgay"), "BHYTTuNgay")
        card_to = _parse_date(patient.get("BHYTDenNgay"), "BHYTDenNgay")
        if card_from >= card_to:
            raise SqlGenerationError("Ngày hết hạn BHYT phải sau ngày hiệu lực.")
        if not card_from <= business_date <= card_to:
            raise SqlGenerationError(
                f"Thẻ BHYT {card_number} không hiệu lực tại ngày {business_date:%d/%m/%Y}."
            )
        if not hospital_code:
            raise SqlGenerationError(
                f"Bệnh nhân {patient.get('TenBenhNhan')} thiếu mã DKKCB."
            )

    script = ADMISSION_SCRIPT_PATH.read_text(encoding="utf-8-sig")

    generated_usage = """    Cách dùng bản sinh tự động:
      1. Các ID danh mục được tự tra cứu từ tài khoản, khoa, tỉnh và mã DKKCB.
      2. Chạy nguyên khối lần đầu với @Commit = 0.
      3. Kiểm tra BenhAn_Id luôn là NULL và các ID được phân giải đúng.
      4. Chỉ đổi @Commit = 1 sau khi kết quả chạy thử đạt yêu cầu.
*/"""
    script, usage_count = re.subn(
        r"    Cách dùng:\s*.*?\*/",
        generated_usage,
        script,
        count=1,
        flags=re.DOTALL,
    )
    if usage_count != 1:
        raise SqlGenerationError("Không thay được phần hướng dẫn trong tài liệu SQL.")

    declarations = (
        "\n"
        f"DECLARE @TenDangNhap nvarchar(100) = {_sql_text(user_login)};\n"
        f"DECLARE @TenKhoaDieuTri nvarchar(200) = {_sql_text(department_name)};\n"
        f"DECLARE @TenTinhThanh nvarchar(150) = {_sql_text(province)};\n"
        "DECLARE @MaICD varchar(20) = 'R69';\n"
        f"DECLARE @MaBenhVienKCB varchar(20) = {_sql_text(hospital_code, unicode=False)};\n"
    )
    marker = "/* Người thực hiện và nơi tiếp nhận. */"
    if marker not in script:
        raise SqlGenerationError("Không tìm thấy vị trí chèn cấu hình tự động.")
    script = script.replace(marker, declarations + "\n" + marker, 1)

    script = _set_declaration(
        script, "LoaiHoSo", "varchar(20)", _sql_text(record_type, unicode=False)
    )
    script = _set_declaration(script, "Commit", "bit", "0")
    script = _set_declaration(
        script,
        "NgayGioNghiepVu",
        "datetime",
        _sql_text(f"{business_date:%Y%m%d} 08:00:00", unicode=False),
    )
    script = _set_declaration(
        script,
        "TenBenhNhan",
        "nvarchar(40)",
        _sql_text(patient.get("TenBenhNhan")),
    )
    script = _set_declaration(
        script,
        "GioiTinh",
        "char(1)",
        _sql_text(_gender_code(patient.get("GioiTinh")), unicode=False),
    )
    script = _set_declaration(
        script,
        "NgaySinh",
        "smalldatetime",
        _sql_text(f"{birth_date:%Y%m%d}", unicode=False),
    )
    script = _set_declaration(script, "SoNha", "nvarchar(150)", _sql_text(house))
    script = _set_declaration(
        script,
        "DiaChiThuongTru",
        "nvarchar(150)",
        _sql_text(patient.get("DiaChiLienHe")),
    )
    script = _set_declaration(
        script,
        "ChanDoan",
        "nvarchar(200)",
        _sql_text(f"THEO DÕI NHẬP VIỆN {department_name.upper()}"),
    )
    script = _set_declaration(
        script,
        "CapCuu",
        "bit",
        "1" if "cấp cứu" in department_name.lower() else "0",
    )
    script = _set_declaration(
        script,
        "SoBHYT",
        "varchar(30)",
        _sql_text(card_number, unicode=False) if record_type == "BHYT" else "NULL",
    )
    script = _set_declaration(
        script,
        "BHYTTuNgay",
        "smalldatetime",
        _sql_text(f"{card_from:%Y%m%d}", unicode=False)
        if card_from
        else "NULL",
    )
    script = _set_declaration(
        script,
        "BHYTDenNgay",
        "smalldatetime",
        _sql_text(f"{card_to:%Y%m%d}", unicode=False) if card_to else "NULL",
    )
    script = _set_declaration(
        script, "TN_TuyenKhamBenh_Id", "int", "NULL"
    )
    script = _set_declaration(script, "TN_LoaiBHYT", "int", "NULL")

    lookup_marker = """/* =========================================================================
   3. KIỂM TRA TRƯỚC KHI GHI DỮ LIỆU
   ========================================================================= */"""
    if lookup_marker not in script:
        raise SqlGenerationError("Không tìm thấy vị trí chèn khối tra cứu ID.")
    script = script.replace(
        lookup_marker, _auto_lookup_sql() + "\n" + lookup_marker, 1
    )

    metadata = "\n".join(
        [
            "-- ============================================================================",
            "-- KHỐI DỮ LIỆU THI SINH TỰ ĐỘNG TỪ TÀI LIỆU NGHIỆP VỤ 02",
            f"-- Thí sinh: {_comment_text(candidate_name)}",
            f"-- SBD: {_comment_text(candidate_id) or 'Không có SBD'}",
            f"-- Vị trí: {_comment_text(position)}",
            f"-- Khoa đích: {_comment_text(department_name)}",
            f"-- Hồ sơ: {record_type} - {_comment_text(patient.get('TenBenhNhan'))}",
            f"-- Mã y tế nguồn chỉ để đối chiếu danh mục: {_comment_text(patient.get('MaYTe'))}",
            "-- Mặc định @Commit = 0; khối này sẽ ROLLBACK sau khi hiển thị kết quả.",
            "-- ============================================================================",
            "",
        ]
    )
    return metadata + script.rstrip() + "\nGO"


def _render_direct_reception_preflight(
    data: dict[str, Any],
    template_type: str,
    candidate_name: str,
    candidate_id: str,
    position: str,
) -> str:
    patient = data.get("bn_bhyt") or {}
    card_number = str(patient.get("SoBHYT") or "").strip()
    if not card_number:
        raise SqlGenerationError(
            f"{template_type} cần bệnh nhân BHYT để kiểm tra dữ liệu tiếp nhận mới."
        )

    return f"""-- ============================================================================
-- KIỂM TRA TRƯỚC THI: BỆNH NHÂN PHẢI CHƯA ĐƯỢC TẠO/TIẾP NHẬN
-- Thí sinh: {_comment_text(candidate_name)}
-- SBD: {_comment_text(candidate_id) or "Không có SBD"}
-- Vị trí: {_comment_text(position)}
-- Loại đề này yêu cầu thí sinh tự tiếp nhận, nên script KHÔNG tạo trước hồ sơ.
-- ============================================================================
USE [eHospital_ThienHanh];
GO

IF EXISTS (
    SELECT 1
    FROM dbo.DM_BenhNhan_BHYT
    WHERE SoThe = {_sql_text(card_number, unicode=False)}
      AND ISNULL(TamNgung, 0) = 0
)
    THROW 51000, N'Thẻ BHYT dành cho bài thi đã tồn tại; hãy thay bệnh nhân trong patients.xlsx.', 1;

SELECT
    N'SẴN SÀNG - chưa có thẻ BHYT trong danh mục bệnh nhân' AS KetQua,
    {_sql_text(patient.get("TenBenhNhan"))} AS TenBenhNhan,
    {_sql_text(card_number, unicode=False)} AS SoBHYT;
GO"""


def _render_no_database_setup(
    template_type: str,
    candidate_name: str,
    candidate_id: str,
    position: str,
) -> str:
    return f"""-- ============================================================================
-- KHÔNG PHÁT SINH DỮ LIỆU TỪ QUY TRÌNH TIẾP NHẬN/VÀO KHOA
-- Thí sinh: {_comment_text(candidate_name)}
-- SBD: {_comment_text(candidate_id) or "Không có SBD"}
-- Vị trí: {_comment_text(position)}
-- Template: {_comment_text(template_type)}
-- Tài liệu nghiệp vụ 02 không bao phủ quy trình của loại đề này.
-- Không sinh bảng/câu lệnh giả để tránh ghi sai cấu trúc HIS thực tế.
-- ============================================================================
GO"""


def generate_sql_script(
    data: dict[str, Any],
    template_type: str,
    candidate_name: str,
    candidate_id: str,
    position: str,
    department_name: Optional[str] = None,
    actions: Optional[list] = None,
) -> str:
    """Sinh phần T-SQL tương ứng với một thí sinh.

    - Nếu đề có nghiệp vụ Nhận bệnh vào khoa (NT_NHAN_BENH_KHOA hoặc thuộc đề nội trú):
      tạo hai bệnh nhân (BHYT và viện phí), tiếp nhận và đưa vào hàng chờ nhận khoa.
    - Nếu đề có nghiệp vụ Tiếp nhận trực tiếp (TN_TIEP_NHAN hoặc thuộc đề trực tiếp):
      chỉ kiểm tra thẻ BHYT chưa tồn tại để không làm hộ phần thi của thí sinh.
    - Các quy trình văn phòng hoặc không dùng HIS: sinh ghi chú giải thích rõ ràng.
    """

    department_name = department_name or position
    
    # Check uses_his flag
    uses_his = data.get("uses_his", True)
    if template_type == "OFFICE_ADMIN" or not uses_his:
        return _render_no_database_setup(
            template_type, candidate_name, candidate_id, position
        )

    user = data.get("user") or {}
    user_login = str(user.get("TenDangNhap") or "").strip()
    if not user_login:
        raise SqlGenerationError(f"Không có tài khoản HIS cho vị trí {position} tại {department_name}.")

    # Check for modular action codes
    action_codes = set()
    if actions:
        for act in actions:
            if isinstance(act, dict) and "action_code" in act:
                action_codes.add(act["action_code"])
            elif isinstance(act, str):
                action_codes.add(act)
    if "action_codes" in data:
        action_codes.update(data["action_codes"])

    is_admission = (
        "NT_NHAN_BENH_KHOA" in action_codes
        or template_type in ADMISSION_QUEUE_TYPES
    )
    is_direct = (
        "TN_TIEP_NHAN" in action_codes
        or template_type in DIRECT_RECEPTION_TYPES
    )

    if is_admission:
        try:
            bhyt_patient = data["bn_bhyt"]
            vp_patient = data["bn_vp"]
        except KeyError as exc:
            raise SqlGenerationError(
                f"Đề nạp dữ liệu nhập khoa thiếu {exc.args[0]}."
            ) from exc

        blocks = [
            _render_admission_block(
                bhyt_patient,
                "BHYT",
                data.get("exam_date"),
                user_login,
                department_name,
                candidate_name,
                candidate_id,
                position,
            ),
            _render_admission_block(
                vp_patient,
                "VIEN_PHI",
                data.get("exam_date"),
                user_login,
                department_name,
                candidate_name,
                candidate_id,
                position,
            ),
        ]
        return "\n\n".join(blocks)

    if "BAN_NGOAI_TRU_NHAN_BENH" in action_codes:
        patient = data.get("bn_ngoai_tru") or (data.get("modular_data", {}).get("BAN_NGOAI_TRU_NHAN_BENH", {}).get("patient"))
        patient_name = patient.get("TenBenhNhan", "Bệnh nhân") if isinstance(patient, dict) else "Bệnh nhân"
        return f"""-- ============================================================================
-- ĐỀ THI NGOẠI TRÚ / PT-TT: {candidate_name} ({candidate_id or 'SBD'}) - {position}
-- Khoa/Phòng: {department_name} | Tài khoản đăng nhập: {user_login}
-- Nghiệp vụ: Tạo Bệnh án Ngoại trú, Chỉ định CLS/PT-TT & Nhập Tường trình PT-TT
-- Ghi chú: Giám khảo tạo tiếp nhận ban đầu trên phần mềm HIS cho BN '{patient_name}'.
-- ============================================================================
PRINT N'Chuẩn bị thông tin tài khoản [{user_login}] cho thí sinh [{candidate_name}] - Khoa [{department_name}].';
GO"""

    if is_direct:
        return _render_direct_reception_preflight(
            data, template_type, candidate_name, candidate_id, position
        )

    return _render_no_database_setup(
        template_type, candidate_name, candidate_id, position
    )


if __name__ == "__main__":
    import sys

    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")

    from catalog_manager import CatalogManager
    from exam_templates import generate_nurse_inpatient

    manager = CatalogManager(str(PROJECT_DIR / "data" / "catalogs"))
    sample = generate_nurse_inpatient(manager, "25/07/2026", "Khoa Nội")
    print(
        generate_sql_script(
            sample,
            "NURSE_INPATIENT",
            "Nguyễn Văn A",
            "SBD001",
            "Điều dưỡng Khoa Nội",
            "Khoa Nội",
        )[:5000]
    )
