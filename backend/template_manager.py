import os
import json
import uuid
import threading
from exam_actions import ACTION_REGISTRY

class TemplateManager:
    """Manages custom and predefined Exam Templates with CRUD and Clone functionality."""

    def __init__(self, config_dir=None):
        if config_dir is None:
            base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
            config_dir = os.path.join(base_dir, "data", "config")
        self.config_dir = config_dir
        os.makedirs(self.config_dir, exist_ok=True)
        
        self.templates_file = os.path.join(self.config_dir, "exam_templates.json")
        self._lock = threading.RLock()
        self.templates = self._load_templates()

    def _load_templates(self):
        if not os.path.exists(self.templates_file):
            defaults = self._seed_default_templates()
            self._save_templates(defaults)
            return defaults
        try:
            with open(self.templates_file, "r", encoding="utf-8") as f:
                data = json.load(f)
            
            if isinstance(data, list) and len(data) > 0:
                # Filter out any deprecated departments (e.g. Khoa Y học cổ truyền)
                filtered_data = [
                    t for t in data 
                    if "Y học cổ truyền" not in t.get("name", "") and "Y học cổ truyền" not in t.get("dept", "")
                ]
                existing_names = {t.get("name") for t in filtered_data}
                defaults = self._seed_default_templates()
                missing = [d for d in defaults if d.get("name") not in existing_names]
                if missing or len(filtered_data) != len(data):
                    filtered_data.extend(missing)
                    self._save_templates(filtered_data)
                return filtered_data
            return self._seed_default_templates()
        except Exception as e:
            print(f"Error loading exam templates: {e}, seeding defaults.")
            defaults = self._seed_default_templates()
            return defaults

    def _save_templates(self, templates):
        temp_path = self.templates_file + ".tmp"
        try:
            with open(temp_path, "w", encoding="utf-8") as f:
                json.dump(templates, f, ensure_ascii=False, indent=2)
            os.replace(temp_path, self.templates_file)
        finally:
            if os.path.exists(temp_path):
                os.remove(temp_path)

    def _seed_default_templates(self):
        """Initial default templates based on standard hospital departments."""
        depts_inpatient = [
            "Khoa Ngoại tổng hợp", "Khoa Chấn thương chỉnh hình", 
            "Khoa Ngoại thần kinh", "Khoa Hồi sức tích cực", "Khoa Nội"
        ]
        
        templates = []
        
        # 1. Inpatient Nurse Templates for each inpatient department
        for dept in depts_inpatient:
            templates.append({
                "id": f"tpl_{uuid.uuid4().hex[:8]}",
                "name": f"Đề thi Điều dưỡng Nội trú ({dept})",
                "dept": dept,
                "position": "Điều dưỡng Nội trú",
                "uses_his": True,
                "actions": [
                    {"action_code": "NT_NHAN_BENH_KHOA", "score": 1.0, "params": {}},
                    {"action_code": "YL_CHI_DINH_CLS", "score": 2.0, "params": {}},
                    {"action_code": "YL_CHI_DINH_THUOC_VTYT", "score": 3.0, "params": {"num_drugs": 3, "num_supplies": 2}},
                    {"action_code": "YL_TRA_THUOC", "score": 1.0, "params": {}},
                    {"action_code": "YL_DOI_THEM_DICH_VU", "score": 1.0, "params": {}},
                    {"action_code": "CK_CHUYEN_KHOA", "score": 1.0, "params": {}},
                    {"action_code": "RV_CHO_RA_VIEN", "score": 1.0, "params": {}}
                ]
            })
            
            templates.append({
                "id": f"tpl_{uuid.uuid4().hex[:8]}",
                "name": f"Đề thi Lễ tân Khoa Nội trú ({dept})",
                "dept": dept,
                "position": "Lễ tân Khoa Nội trú",
                "uses_his": True,
                "actions": [
                    {"action_code": "NT_NHAN_BENH_KHOA", "score": 3.0, "params": {}},
                    {"action_code": "TC_THU_TAM_UNG", "score": 4.0, "params": {}},
                    {"action_code": "TK_KIEM_TON_KHO", "score": 1.0, "params": {}},
                    {"action_code": "RV_CHO_RA_VIEN", "score": 2.0, "params": {}}
                ]
            })

        # 2. Specialized Inpatient (Khoa Phụ Sản, Khoa Nhi, Khoa Cấp cứu, Khoa Tim mạch)
        special_inpatient = [
            ("Khoa Phụ Sản", "Nữ hộ sinh / Điều dưỡng Sản"),
            ("Khoa Nhi", "Điều dưỡng Nhi khoa"),
            ("Khoa Cấp cứu", "Điều dưỡng Cấp cứu"),
            ("Khoa Tim mạch", "Điều dưỡng Tim mạch")
        ]
        for dept, pos in special_inpatient:
            templates.append({
                "id": f"tpl_{uuid.uuid4().hex[:8]}",
                "name": f"Đề thi {pos} ({dept})",
                "dept": dept,
                "position": pos,
                "uses_his": True,
                "actions": [
                    {"action_code": "NT_NHAN_BENH_KHOA", "score": 1.0, "params": {}},
                    {"action_code": "YL_CHI_DINH_CLS", "score": 2.0, "params": {}},
                    {"action_code": "YL_CHI_DINH_THUOC_VTYT", "score": 3.0, "params": {"num_drugs": 3, "num_supplies": 2}},
                    {"action_code": "YL_TRA_THUOC", "score": 1.0, "params": {}},
                    {"action_code": "YL_DOI_THEM_DICH_VU", "score": 1.0, "params": {}},
                    {"action_code": "CK_CHUYEN_KHOA", "score": 1.0, "params": {}},
                    {"action_code": "RV_CHO_RA_VIEN", "score": 1.0, "params": {}}
                ]
            })

        # 3. Outpatient Nurse
        outpatient_depts = ["Khoa Mắt", "Khoa Tai Mũi Họng", "Khoa Răng Hàm Mặt", "Khoa Khám bệnh", "Thận nhân tạo"]
        for dept in outpatient_depts:
            templates.append({
                "id": f"tpl_{uuid.uuid4().hex[:8]}",
                "name": f"Đề thi Điều dưỡng Ngoại trú ({dept})",
                "dept": dept,
                "position": "Điều dưỡng Ngoại trú",
                "uses_his": True,
                "actions": [
                    {"action_code": "TN_TIEP_NHAN", "score": 3.0, "params": {}},
                    {"action_code": "YL_CHI_DINH_CLS", "score": 3.0, "params": {}},
                    {"action_code": "YL_CHI_DINH_THUOC_VTYT", "score": 2.0, "params": {}},
                    {"action_code": "TC_THU_TAM_UNG", "score": 2.0, "params": {}}
                ]
            })

        # 4. Outpatient Medical Record & PTTT Depts (PHCN, Thận nhân tạo)
        outpatient_ban_depts = [
            ("Khoa Phục hồi chức năng", "Kỹ thuật viên Phục hồi chức năng", "Đề thi Kỹ thuật viên Phục hồi chức năng (Khoa Phục hồi chức năng)"),
            ("Thận nhân tạo", "Điều dưỡng Thận nhân tạo", "Đề thi Điều dưỡng Thận nhân tạo (Thận nhân tạo)")
        ]
        for dept, pos, tpl_name in outpatient_ban_depts:
            templates.append({
                "id": f"tpl_{uuid.uuid4().hex[:8]}",
                "name": tpl_name,
                "dept": dept,
                "position": pos,
                "uses_his": True,
                "actions": [
                    {"action_code": "BAN_NGOAI_TRU_NHAN_BENH", "score": 3.0, "params": {}},
                    {"action_code": "PTTT_CHI_DINH", "score": 2.0, "params": {"num_services": 4}},
                    {"action_code": "PTTT_TUONG_TRINH", "score": 3.0, "params": {}},
                    {"action_code": "VP_KIEM_TRA_VIEN_PHI", "score": 1.0, "params": {}},
                    {"action_code": "TC_TRA_CUU_BENH_SU", "score": 1.0, "params": {}}
                ]
            })

        # 5. Technicians (Xét nghiệm, CĐHA, Nội soi)
        tech_depts = ["Khoa Xét Nghiệm", "Khoa Chẩn đoán hình ảnh", "Khoa Nội soi"]
        for dept in tech_depts:
            templates.append({
                "id": f"tpl_{uuid.uuid4().hex[:8]}",
                "name": f"Đề thi Kỹ thuật viên ({dept})",
                "dept": dept,
                "position": "Kỹ thuật viên Cận lâm sàng",
                "uses_his": True,
                "actions": [
                    {"action_code": "KQ_TRA_KET_QUA_CLS", "score": 7.0, "params": {"sample_type": "HUYET_HOC_18" if "Xét" in dept else "SIEU_AM"}},
                    {"action_code": "TK_KIEM_TON_KHO", "score": 3.0, "params": {}}
                ]
            })

        # 5. Cashier
        templates.append({
            "id": f"tpl_{uuid.uuid4().hex[:8]}",
            "name": "Đề thi Thu ngân Quầy viện phí (Phòng TCKT)",
            "dept": "Phòng Tài chính kế toán",
            "position": "Thu ngân Quầy thu phí",
            "uses_his": True,
            "actions": [
                {"action_code": "TC_THU_TAM_UNG", "score": 4.0, "params": {}},
                {"action_code": "TC_THANH_TOAN_RA_VIEN", "score": 4.0, "params": {}},
                {"action_code": "TK_KIEM_TON_KHO", "score": 2.0, "params": {}}
            ]
        })

        # 6. Office Admin
        office_depts = ["Phòng Chăm sóc khách hàng", "Phòng Tổ chức - Hành chính"]
        for dept in office_depts:
            templates.append({
                "id": f"tpl_{uuid.uuid4().hex[:8]}",
                "name": f"Đề thi Tin học Văn phòng ({dept})",
                "dept": dept,
                "position": "Nhân viên Văn phòng / CSKH",
                "uses_his": False,
                "actions": [
                    {"action_code": "VP_SOAN_THAO_WORD", "score": 5.0, "params": {}},
                    {"action_code": "VP_XU_LY_EXCEL", "score": 5.0, "params": {}}
                ]
            })

        return templates

    def get_all_templates(self, dept=None):
        with self._lock:
            if dept:
                return [t for t in self.templates if t.get("dept") == dept]
            return list(self.templates)

    def get_template_by_id(self, template_id):
        with self._lock:
            for t in self.templates:
                if t.get("id") == template_id:
                    return dict(t)
            return None

    def create_template(self, data):
        with self._lock:
            tpl_id = f"tpl_{uuid.uuid4().hex[:8]}"
            
            # Determine if template uses HIS based on its actions
            uses_his = any(ACTION_REGISTRY.get(a["action_code"], {}).get("uses_his", True) for a in data.get("actions", []))
            
            new_template = {
                "id": tpl_id,
                "name": data.get("name", "Mẫu đề thi mới"),
                "dept": data.get("dept", "Khoa Ngoại tổng hợp"),
                "position": data.get("position", "Điều dưỡng"),
                "uses_his": uses_his,
                "actions": data.get("actions", [])
            }
            self.templates.append(new_template)
            self._save_templates(self.templates)
            return new_template

    def update_template(self, template_id, data):
        with self._lock:
            for idx, t in enumerate(self.templates):
                if t.get("id") == template_id:
                    uses_his = any(ACTION_REGISTRY.get(a["action_code"], {}).get("uses_his", True) for a in data.get("actions", t.get("actions", [])))
                    t["name"] = data.get("name", t.get("name"))
                    t["dept"] = data.get("dept", t.get("dept"))
                    t["position"] = data.get("position", t.get("position"))
                    t["uses_his"] = uses_his
                    t["actions"] = data.get("actions", t.get("actions", []))
                    self._save_templates(self.templates)
                    return dict(t)
            return None

    def delete_template(self, template_id):
        with self._lock:
            before_count = len(self.templates)
            self.templates = [t for t in self.templates if t.get("id") != template_id]
            if len(self.templates) < before_count:
                self._save_templates(self.templates)
                return True
            return False

    def clone_template(self, source_id, target_dept, new_name=None):
        with self._lock:
            source = self.get_template_by_id(source_id)
            if not source:
                return None
            
            tpl_id = f"tpl_{uuid.uuid4().hex[:8]}"
            name = new_name or f"{source['name']} (Sao chép - {target_dept})"
            cloned = {
                "id": tpl_id,
                "name": name,
                "dept": target_dept,
                "position": source.get("position", "Điều dưỡng"),
                "uses_his": source.get("uses_his", True),
                "actions": [dict(a) for a in source.get("actions", [])]
            }
            self.templates.append(cloned)
            self._save_templates(self.templates)
            return cloned
