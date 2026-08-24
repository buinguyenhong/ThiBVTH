import re
import sys
from pathlib import Path
import unittest

BACKEND_DIR = Path(__file__).resolve().parent
PROJECT_DIR = BACKEND_DIR.parent
sys.path.insert(0, str(BACKEND_DIR))

from catalog_manager import CatalogManager
from exam_templates import (
    generate_nurse_direct_reception,
    generate_nurse_inpatient,
)
from sql_generator import SqlGenerationError, generate_sql_script


class SqlGeneratorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalog_manager = CatalogManager(
            str(PROJECT_DIR / "data" / "catalogs")
        )

    def test_inpatient_sql_uses_real_his_workflow(self):
        data = generate_nurse_inpatient(
            self.catalog_manager, "25/07/2026", "Khoa Nội"
        )
        sql = generate_sql_script(
            data,
            "NURSE_INPATIENT",
            "Nguyễn Văn Kiểm",
            "SBD001",
            "Điều dưỡng Khoa Nội",
            "Khoa Nội",
        )

        self.assertEqual(sql.count("USE [eHospital_ThienHanh]"), 2)
        self.assertEqual(sql.count("EXEC dbo.sp_DM_BENHNHAN\n"), 2)
        # Mỗi khối giữ nguyên nhánh BHYT của tài liệu; khối viện phí không chạy
        # nhánh này vì @LoaiHoSo = 'VIEN_PHI'.
        self.assertEqual(sql.count("EXEC dbo.sp_DM_BENHNHAN_BHYT\n"), 2)
        self.assertEqual(sql.count("EXEC dbo.sp_NOITRU_NHAPVIEN\n"), 2)
        self.assertEqual(sql.count("DECLARE @Commit bit = 0;"), 2)
        self.assertIn("EXEC dbo.sp_TIEPNHAN", sql)
        self.assertIn(
            "DECLARE @TenKhoaDieuTri nvarchar(200) = N'Khoa Nội';", sql
        )
        self.assertIn("@BenhAn_Id = NULL", sql)

        self.assertNotIn("HS_TIEPNHAN", sql)
        self.assertNotIn("CLS_YLENH", sql)
        self.assertNotIn("DM_DUOC_KHO", sql)
        self.assertIsNone(
            re.search(
                r"EXEC\s+dbo\.sp_NOITRU_NHAPVIEN.*?"
                r"@Action\s*=\s*N'AddNewAndCreateBenhAn'",
                sql,
                flags=re.DOTALL | re.IGNORECASE,
            )
        )

    def test_sql_escapes_vietnamese_patient_name_with_apostrophe(self):
        data = generate_nurse_inpatient(
            self.catalog_manager, "25/07/2026", "Khoa Nội"
        )
        apostrophe_patient = next(
            patient
            for patient in self.catalog_manager.patients
            if "'" in patient["TenBenhNhan"] and patient["SoBHYT"]
        )
        data["bn_bhyt"] = apostrophe_patient

        sql = generate_sql_script(
            data,
            "NURSE_INPATIENT",
            "Thí sinh O'Neil",
            "",
            "Điều dưỡng Khoa Nội",
            "Khoa Nội",
        )

        escaped_patient_name = apostrophe_patient["TenBenhNhan"].replace(
            "'", "''"
        )
        self.assertIn(escaped_patient_name, sql)
        self.assertIn("-- Thí sinh: Thí sinh O'Neil", sql)

    def test_direct_reception_only_runs_read_only_preflight(self):
        data = generate_nurse_direct_reception(
            self.catalog_manager, "25/07/2026", "Khoa Cấp cứu"
        )
        sql = generate_sql_script(
            data,
            "NURSE_DIRECT_RECEPTION",
            "Nguyễn Văn A",
            "SBD002",
            "Điều dưỡng Cấp cứu",
            "Khoa Cấp cứu",
        )

        self.assertIn("script KHÔNG tạo trước hồ sơ", sql)
        self.assertIn("IF EXISTS", sql)
        self.assertIn("SELECT", sql)
        self.assertNotIn("INSERT INTO", sql)
        self.assertNotIn("UPDATE ", sql)
        self.assertNotIn("DELETE ", sql)
        self.assertNotIn("EXEC dbo.", sql)

    def test_expired_bhyt_is_rejected(self):
        data = generate_nurse_inpatient(
            self.catalog_manager, "25/07/2026", "Khoa Nội"
        )
        data["bn_bhyt"] = {
            **data["bn_bhyt"],
            "BHYTTuNgay": "2020-01-01",
            "BHYTDenNgay": "2020-12-31",
        }

        with self.assertRaisesRegex(SqlGenerationError, "không hiệu lực"):
            generate_sql_script(
                data,
                "NURSE_INPATIENT",
                "Nguyễn Văn A",
                "SBD003",
                "Điều dưỡng Khoa Nội",
                "Khoa Nội",
            )

    def test_exam_year_normalizes_standard_and_te1_card_periods(self):
        standard_patient = next(
            patient
            for patient in self.catalog_manager.patients
            if patient["SoBHYT"]
            and not patient["SoBHYT"].upper().startswith("TE1")
        )
        child_patient = next(
            patient
            for patient in self.catalog_manager.patients
            if patient["SoBHYT"].upper().startswith("TE1")
        )

        standard = self.catalog_manager.normalize_patient_for_exam(
            standard_patient, 2026
        )
        child = self.catalog_manager.normalize_patient_for_exam(
            child_patient, 2026
        )

        self.assertEqual(standard["BHYTTuNgay"], "01/01/2026")
        self.assertEqual(standard["BHYTDenNgay"], "31/12/2026")
        self.assertEqual(child["BHYTTuNgay"], "01/01/2026")
        self.assertEqual(child["BHYTDenNgay"], "31/12/2026")


if __name__ == "__main__":
    unittest.main()
