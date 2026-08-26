import unittest
import os
import sys
import shutil
import tempfile

sys.path.insert(0, os.path.dirname(__file__))

from fastapi.testclient import TestClient
from app import app, cm, mm, tm
from mapping_manager import MappingManager
from template_manager import TemplateManager
from exam_actions import ACTION_REGISTRY, get_all_actions
from exam_generator import generate_docx_file
import docx
import app as app_module

class TestModularSystem(unittest.TestCase):
    def setUp(self):
        self.test_dir = tempfile.mkdtemp()
        self.orig_mm = app_module.mm
        app_module.mm = MappingManager(self.test_dir)
        self.client = TestClient(app)

    def tearDown(self):
        app_module.mm = self.orig_mm
        shutil.rmtree(self.test_dir, ignore_errors=True)

    def test_mapping_manager(self):
        mgr = MappingManager(self.test_dir)
        # Test initial default
        services = mgr.get_services_for_dept("Khoa Ngoại tổng hợp")
        self.assertTrue(len(services) > 0)
        
        # Test update
        mgr.set_services_for_dept("Khoa Ngoại tổng hợp", ["Siêu âm", "Xét nghiệm"])
        updated = mgr.get_services_for_dept("Khoa Ngoại tổng hợp")
        self.assertEqual(updated, ["Siêu âm", "Xét nghiệm"])
        
        # Test pharmacies
        pharmacies = mgr.get_pharmacies_for_dept("Khoa Ngoại tổng hợp")
        self.assertTrue(len(pharmacies) > 0)
        mgr.set_pharmacies_for_dept("Khoa Ngoại tổng hợp", ["KHO_TEST"])
        self.assertEqual(mgr.get_pharmacies_for_dept("Khoa Ngoại tổng hợp"), ["KHO_TEST"])

    def test_template_manager_crud_and_clone(self):
        mgr = TemplateManager(self.test_dir)
        all_tpls = mgr.get_all_templates()
        self.assertTrue(len(all_tpls) > 0)
        
        # Test create
        new_tpl = mgr.create_template({
            "name": "Đề thi Test Tạo Mới",
            "dept": "Khoa Ngoại tổng hợp",
            "position": "Điều dưỡng",
            "actions": [
                {"action_code": "NT_NHAN_BENH_KHOA", "score": 3.0},
                {"action_code": "YL_CHI_DINH_CLS", "score": 3.0},
                {"action_code": "TK_KIEM_TON_KHO", "score": 4.0}
            ]
        })
        self.assertIsNotNone(new_tpl["id"])
        self.assertEqual(new_tpl["name"], "Đề thi Test Tạo Mới")
        
        # Test get by id
        fetched = mgr.get_template_by_id(new_tpl["id"])
        self.assertEqual(fetched["name"], "Đề thi Test Tạo Mới")
        
        # Test update
        mgr.update_template(new_tpl["id"], {
            "name": "Đề thi Test Cập Nhật",
            "dept": "Khoa Ngoại tổng hợp",
            "position": "Điều dưỡng",
            "actions": [
                {"action_code": "NT_NHAN_BENH_KHOA", "score": 5.0},
                {"action_code": "TK_KIEM_TON_KHO", "score": 5.0}
            ]
        })
        updated = mgr.get_template_by_id(new_tpl["id"])
        self.assertEqual(updated["name"], "Đề thi Test Cập Nhật")
        self.assertEqual(len(updated["actions"]), 2)
        
        # Test clone
        cloned = mgr.clone_template(new_tpl["id"], "Khoa Nội", "Đề thi Test Clone cho Khoa Nội")
        self.assertIsNotNone(cloned)
        self.assertEqual(cloned["dept"], "Khoa Nội")
        self.assertEqual(cloned["name"], "Đề thi Test Clone cho Khoa Nội")
        self.assertEqual(len(cloned["actions"]), 2)
        
        # Test delete
        deleted = mgr.delete_template(new_tpl["id"])
        self.assertTrue(deleted)
        self.assertIsNone(mgr.get_template_by_id(new_tpl["id"]))

    def test_action_registry(self):
        actions = get_all_actions()
        self.assertTrue(len(actions) >= 10)
        codes = [a["code"] for a in actions]
        self.assertIn("NT_NHAN_BENH_KHOA", codes)
        self.assertIn("TN_TIEP_NHAN", codes)
        self.assertIn("YL_CHI_DINH_CLS", codes)
        self.assertIn("YL_CHI_DINH_THUOC_VTYT", codes)
        self.assertIn("TK_KIEM_TON_KHO", codes)
        self.assertIn("VP_SOAN_THAO_WORD", codes)

    def test_api_endpoints(self):
        # 1. GET /api/actions
        r = self.client.get("/api/actions")
        self.assertEqual(r.status_code, 200)
        self.assertTrue(len(r.json()) > 0)
        
        # 2. GET /api/metadata/service-groups
        r = self.client.get("/api/metadata/service-groups")
        self.assertEqual(r.status_code, 200)
        
        # 3. GET /api/metadata/departments
        r = self.client.get("/api/metadata/departments")
        self.assertEqual(r.status_code, 200)
        self.assertIn("Khoa Ngoại tổng hợp", r.json())
        
        # 4. GET /api/mappings/services
        r = self.client.get("/api/mappings/services")
        self.assertEqual(r.status_code, 200)
        self.assertIsInstance(r.json(), dict)
        
        # 5. POST /api/mappings/services
        sample_map = r.json()
        sample_map["Khoa Ngoại tổng hợp"] = ["Siêu âm", "Xét nghiệm"]
        r2 = self.client.post("/api/mappings/services", json=sample_map)
        self.assertEqual(r2.status_code, 200)
        
        # 6. GET /api/templates
        r = self.client.get("/api/templates")
        self.assertEqual(r.status_code, 200)
        templates = r.json()
        self.assertTrue(len(templates) > 0)
        
        # 7. POST /api/generate with a modular template
        chosen_tpl = templates[0]
        gen_req = {
            "exam_date": "25/07/2026",
            "candidates": [
                {
                    "name": "Nguyễn Thị Thử Nghiệm",
                    "id": "SBD888",
                    "template_id": chosen_tpl["id"]
                }
            ]
        }
        r_gen = self.client.post("/api/generate", json=gen_req)
        self.assertEqual(r_gen.status_code, 200)
        self.assertTrue(len(r_gen.content) > 0)

        # 8. GET /api/mappings/script/services
        r_svc_script = self.client.get("/api/mappings/script/services")
        self.assertEqual(r_svc_script.status_code, 200)
        self.assertIn("DM_NhomDichVu", r_svc_script.text)
        
        # 9. GET /api/mappings/script/pharmacies
        r_phar_script = self.client.get("/api/mappings/script/pharmacies")
        self.assertEqual(r_phar_script.status_code, 200)
        self.assertIn("dm_khoduoc", r_phar_script.text)

    def test_excel_mapping_import(self):
        import openpyxl
        mgr = MappingManager(self.test_dir)
        
        # 1. Test service mapping import
        svc_wb = openpyxl.Workbook()
        ws = svc_wb.active
        ws.append(["TenKhoaPhong", "NhomDichVu"])
        ws.append(["Khoa Ngoại tổng hợp", "Siêu âm"])
        ws.append(["Khoa Ngoại tổng hợp", "X-quang"])
        ws.append(["Khoa Cấp cứu", "Cấp cứu"])
        svc_file = os.path.join(self.test_dir, "test_svc.xlsx")
        svc_wb.save(svc_file)
        
        num_depts, count = mgr.import_services_from_excel(svc_file)
        self.assertEqual(num_depts, 2)
        self.assertEqual(count, 3)
        self.assertEqual(mgr.get_services_for_dept("Khoa Ngoại tổng hợp"), ["Siêu âm", "X-quang"])
        
        # 2. Test pharmacy mapping import
        phar_wb = openpyxl.Workbook()
        ws2 = phar_wb.active
        ws2.append(["TenKhoaPhong", "MaKho", "TenKho"])
        ws2.append(["Khoa Ngoại tổng hợp", "KHO_NGOAI", "Kho Ngoại"])
        ws2.append(["Khoa Ngoại tổng hợp", "KHO_CHUNG", "Kho Chung"])
        ws2.append(["Khoa Nội", "KHO_NOI", "Kho Nội"])
        phar_file = os.path.join(self.test_dir, "test_phar.xlsx")
        phar_wb.save(phar_file)
        
        num_depts_p, count_p = mgr.import_pharmacies_from_excel(phar_file)
        self.assertEqual(num_depts_p, 2)
        self.assertEqual(count_p, 3)
        self.assertEqual(mgr.get_pharmacies_for_dept("Khoa Ngoại tổng hợp"), ["KHO_CHUNG", "KHO_NGOAI"])

if __name__ == "__main__":
    unittest.main()
