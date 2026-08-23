from fastapi.testclient import TestClient
import io
import os
import re
import json
import tempfile
import zipfile
import sys

sys.stdout.reconfigure(encoding='utf-8')

# Import the app
import app as app_module
from app import app, cm, sanitize_filename_component

client = TestClient(app)


def test_sanitize_filename_component():
    value = sanitize_filename_component("Điều dưỡng / Nữ hộ sinh: Sản")
    assert value == "Điều_dưỡng___Nữ_hộ_sinh__Sản"
    assert not re.search(r'[<>:"/\\|?*]', value)


def test_get_templates():
    print("Testing GET /api/templates...")
    response = client.get("/api/templates")
    assert response.status_code == 200
    templates = response.json()
    assert templates
    assert all(
        not t.get("uses_his", True) or cm.has_user_for_dept(t["dept"])
        for t in templates
    )
    departments = {t["dept"] for t in templates}
    assert "Khoa Phụ Sản" in departments
    assert "Khoa Chấn thương chỉnh hình" in departments
    assert "Khoa Phẫu thuật - GMHS" in departments
    assert "Phòng Tài chính kế toán" in departments
    assert "Phòng Tổ chức - Hành chính" in departments
    assert "Phòng Chăm sóc khách hàng" in departments
    office_templates = [t for t in templates if not t.get("uses_his", True)]
    assert len(office_templates) >= 2
    assert all(not t["uses_his"] for t in office_templates)
    print(f"Success! Found {len(templates)} templates:")
    for t in templates:
        print(f"  - {t['name']}")
    print()

def test_get_catalogs_status():
    print("Testing GET /api/catalogs/status...")
    response = client.get("/api/catalogs/status")
    assert response.status_code == 200
    status = response.json()
    print("Success! Catalog status:")
    for filename, info in status.items():
        print(f"  - {filename}: exists={info['exists']}, count={info['count']}, modified={info['last_modified']}")
    print()

def test_get_catalog_scripts():
    print("Testing GET /api/catalogs/script/{filename}...")
    patient_response = client.get("/api/catalogs/script/patients.xlsx")
    assert patient_response.status_code == 200
    patient_sql = patient_response.content.decode("utf-8")
    assert "DECLARE @TongSoBenhNhan int = 1000;" in patient_sql
    assert "DM_BenhNhan_BHYT" in patient_sql
    assert "MaYTe" in patient_sql

    drug_response = client.get("/api/catalogs/script/drugs.xlsx")
    assert drug_response.status_code == 200
    drug_sql = drug_response.content.decode("utf-8")
    assert "DuocTonKho" in drug_sql
    assert "HAVING SUM(ISNULL(ton.SoLuong, 0)) > 0" in drug_sql
    assert "SET TRANSACTION ISOLATION LEVEL READ COMMITTED" in drug_sql
    assert "KhoaPhong" in drug_sql
    assert "ThoiDiemChotTon" in drug_sql
    assert "TrangThai" in drug_sql

    service_response = client.get("/api/catalogs/script/services.xlsx")
    assert service_response.status_code == 200
    service_sql = service_response.content.decode("utf-8")
    assert "DM_PhongBan_DichVu" in service_sql
    assert "KhoaThucHien" in service_sql
    assert "ISNULL(dv.TamNgung, 0) = 0" in service_sql

    user_response = client.get("/api/catalogs/script/users.xlsx")
    assert user_response.status_code == 200
    user_sql = user_response.content.decode("utf-8")
    assert "NhanVien_User_Mapping" in user_sql
    assert "KhoaPhong" in user_sql
    assert "DECLARE @MatKhauThi" in user_sql
    assert "users.User_Password" not in user_sql

    missing_response = client.get("/api/catalogs/script/unknown.xlsx")
    assert missing_response.status_code == 404
    print("Success! Catalog export scripts are downloadable.")
    print()


def test_inventory_lookup():
    print("Testing inventory lookup APIs...")
    frontend_response = client.get("/")
    assert frontend_response.status_code == 200
    assert 'id="inventory-tab"' in frontend_response.text
    assert 'id="inventoryDepartment"' in frontend_response.text

    options_response = client.get("/api/inventory/options")
    assert options_response.status_code == 200
    options = options_response.json()
    assert options["record_count"] == len(cm.drugs)
    assert options["is_live"] is False
    assert "departments" in options
    assert "warehouses" in options
    assert "snapshot_times" in options

    search_response = client.get(
        "/api/inventory/search",
        params={"min_stock": 1, "limit": 5},
    )
    assert search_response.status_code == 200
    result = search_response.json()
    assert result["returned"] <= 5
    assert result["total"] >= result["returned"]
    assert all(item["SoLuongTon"] >= 1 for item in result["items"])
    assert result["is_live"] is False

    invalid_response = client.get(
        "/api/inventory/search",
        params={"min_stock": -1},
    )
    assert invalid_response.status_code == 400
    print("Success! Inventory catalog can be filtered safely.")
    print()


def test_generate_exams():
    print("Testing POST /api/generate...")
    payload = {
        "candidates": [
            {"name": "Lê Văn Điều Dưỡng Nội", "id": "SBD001", "position": "Điều dưỡng Khoa Nội"},
            {"name": "Phạm Thị Lễ Tân Nội", "id": "SBD002", "position": "Lễ tân Khoa Nội"}
        ],
        "exam_date": "16/07/2026"
    }

    original_usage_path = cm.usage_store_path
    original_used_keys = set(cm._used_patient_keys)
    original_output_dir = app_module.OUTPUT_DIR
    with tempfile.TemporaryDirectory() as temp_dir:
        test_usage_path = os.path.join(temp_dir, "used_patients.json")
        app_module.OUTPUT_DIR = os.path.join(temp_dir, "output")
        cm.usage_store_path = test_usage_path
        cm._used_patient_keys = set()
        try:
            response = client.post("/api/generate", json=payload)
            with open(test_usage_path, encoding="utf-8") as usage_file:
                usage_payload = json.load(usage_file)
            assert len(usage_payload["used_patient_keys"]) == 5
        finally:
            app_module.OUTPUT_DIR = original_output_dir
            cm.usage_store_path = original_usage_path
            cm._used_patient_keys = original_used_keys

    assert response.status_code == 200
    assert response.headers["content-type"] == "application/x-zip-compressed"
    print("Success! Generated ZIP file.")
    
    # Inspect ZIP contents
    print("Inspecting ZIP contents:")
    with zipfile.ZipFile(io.BytesIO(response.content), "r") as zip_ref:
        files = zip_ref.namelist()
        for f in files:
            print(f"  - {f}")
        assert len([name for name in files if name.endswith(".docx")]) == 2
        assert "01_lookup_tiep_nhan_nhap_vien.sql" in files
        sql_files = [name for name in files if name.startswith("Nap_Du_Lieu_Thi_")]
        assert len(sql_files) == 1
        generated_sql = zip_ref.read(sql_files[0]).decode("utf-8")
        assert "USE [eHospital_ThienHanh]" in generated_sql
        assert "EXEC dbo.sp_NOITRU_NHAPVIEN" in generated_sql
        assert "DECLARE @Commit bit = 0;" in generated_sql
        assert "HS_TIEPNHAN" not in generated_sql
        assert "CLS_YLENH" not in generated_sql
        assert "DM_DUOC_KHO" not in generated_sql
        generated_cards = re.findall(
            r"DECLARE @SoBHYT varchar\(30\) = '([A-Z0-9]{15})'",
            generated_sql
        )
        source_cards = {
            str(patient.get("SoBHYT") or "").strip().upper()
            for patient in cm.patients
            if patient.get("SoBHYT")
        }
        assert len(generated_cards) == len(set(generated_cards))
        assert not (set(generated_cards) & source_cards)
        assert "DECLARE @BHYTTuNgay smalldatetime = '20260101';" in generated_sql
        expected_end_date = (
            "20301231" if generated_cards[0].startswith("TE1") else "20261231"
        )
        assert (
            f"DECLARE @BHYTDenNgay smalldatetime = '{expected_end_date}';"
            in generated_sql
        )
    print()

if __name__ == "__main__":
    try:
        test_get_templates()
        test_get_catalogs_status()
        test_get_catalog_scripts()
        test_inventory_lookup()
        test_generate_exams()
        print("=== ALL TESTS PASSED SUCCESSFULLY! ===")
    except Exception as e:
        print(f"=== TEST FAILED: {e} ===")
        sys.exit(1)
