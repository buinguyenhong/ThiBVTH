import sys
if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

from fastapi import FastAPI, HTTPException, BackgroundTasks, UploadFile, File
from fastapi.responses import FileResponse, JSONResponse, Response
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel
from typing import List, Optional
import os
import re
import shutil
import zipfile
import hashlib
from datetime import datetime

from mapping_manager import MappingManager
from template_manager import TemplateManager
from exam_actions import ACTION_REGISTRY, get_all_actions
from catalog_manager import CatalogManager
from exam_templates import EXAM_TEMPLATES
from exam_generator import generate_docx_file, generate_office_excel_file
from sql_generator import generate_sql_script

app = FastAPI(title="Exam Generator & HIS Data Populator API")

# Define paths
BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CATALOG_DIR = os.path.join(BASE_DIR, "data", "catalogs")
CONFIG_DIR = os.path.join(BASE_DIR, "data", "config")
OUTPUT_DIR = os.path.join(BASE_DIR, "data", "output")
FRONTEND_DIR = os.path.join(BASE_DIR, "frontend")
CATALOG_SCRIPTS = {
    "patients.xlsx": os.path.join(
        BASE_DIR, "Scripts", "04_export_1000_benh_nhan.sql"
    ),
    "drugs.xlsx": os.path.join(
        BASE_DIR, "Scripts", "03_export_danh_muc_thuoc_vat_tu.sql"
    ),
    "services.xlsx": os.path.join(
        BASE_DIR, "Scripts", "05_export_danh_muc_dich_vu.sql"
    ),
    "users.xlsx": os.path.join(
        BASE_DIR, "Scripts", "06_export_user_theo_khoa_phong.sql"
    ),
    "service_mappings.xlsx": os.path.join(
        BASE_DIR, "Scripts", "07_export_phan_quyen_dich_vu_theo_khoa.sql"
    ),
    "pharmacy_mappings.xlsx": os.path.join(
        BASE_DIR, "Scripts", "08_export_anh_xa_kho_duoc_theo_khoa.sql"
    ),
}

# Initialize Managers
cm = CatalogManager(CATALOG_DIR)
mm = MappingManager(CONFIG_DIR)
tm = TemplateManager(CONFIG_DIR)

class TemplateActionModel(BaseModel):
    action_code: str
    score: float = 1.0
    params: dict = {}

class TemplateCreateModel(BaseModel):
    name: str
    dept: str
    position: str = "Điều dưỡng"
    actions: List[TemplateActionModel]

class TemplateCloneModel(BaseModel):
    target_dept: str
    new_name: Optional[str] = None

class Candidate(BaseModel):
    name: str
    id: str = ""  # SBD can be empty
    position: Optional[str] = ""
    dept: Optional[str] = ""
    template_id: Optional[str] = None
    scores: List[float] = [] # Custom scores per question

class GenerateRequest(BaseModel):
    candidates: List[Candidate]
    exam_date: str

# --- ACTION REGISTRY & METADATA ENDPOINTS ---

@app.get("/api/actions")
def get_actions():
    """Returns list of available atomic question actions."""
    return get_all_actions()

@app.get("/api/metadata/service-groups")
def get_service_groups():
    """Returns unique service categories from services.xlsx."""
    return cm.get_service_groups()

@app.get("/api/metadata/warehouses")
def get_warehouses():
    """Returns unique warehouse codes from drugs.xlsx."""
    return cm.get_warehouses()

@app.get("/api/metadata/departments")
def get_departments():
    """Returns unique department names from users and mappings."""
    dept_set = set(cm.get_departments_from_users())
    dept_set.update(mm.get_all_service_mappings().keys())
    dept_set.update(mm.get_all_pharmacy_mappings().keys())
    for t in tm.get_all_templates():
        if t.get("dept"):
            dept_set.add(t["dept"])
    return sorted(list(dept_set))

# --- TEMPLATE MANAGEMENT ENDPOINTS ---

@app.get("/api/templates")
def get_templates(dept: Optional[str] = None):
    """Returns all exam templates, optionally filtered by department."""
    templates = tm.get_all_templates(dept=dept)
    # Check HIS user availability for HIS templates
    result = []
    for t in templates:
        t_copy = dict(t)
        has_user = cm.has_user_for_dept(t.get("dept", ""))
        t_copy["has_user"] = has_user
        result.append(t_copy)
    return result

@app.get("/api/templates/{template_id}")
def get_template(template_id: str):
    """Returns a single template by ID."""
    t = tm.get_template_by_id(template_id)
    if not t:
        raise HTTPException(status_code=404, detail="Template not found.")
    t["has_user"] = cm.has_user_for_dept(t.get("dept", ""))
    return t

@app.post("/api/templates")
def create_template(data: TemplateCreateModel):
    """Creates a new custom exam template."""
    new_t = tm.create_template(data.dict())
    return new_t

@app.put("/api/templates/{template_id}")
def update_template(template_id: str, data: TemplateCreateModel):
    """Updates an existing exam template."""
    updated = tm.update_template(template_id, data.dict())
    if not updated:
        raise HTTPException(status_code=404, detail="Template not found.")
    return updated

@app.delete("/api/templates/{template_id}")
def delete_template(template_id: str):
    """Deletes an exam template."""
    success = tm.delete_template(template_id)
    if not success:
        raise HTTPException(status_code=404, detail="Template not found.")
    return {"status": "success", "message": "Template deleted successfully."}

@app.post("/api/templates/{template_id}/clone")
def clone_template(template_id: str, data: TemplateCloneModel):
    """Clones an exam template to another department."""
    cloned = tm.clone_template(template_id, data.target_dept, data.new_name)
    if not cloned:
        raise HTTPException(status_code=404, detail="Source template not found.")
    return cloned

# --- MAPPING ENDPOINTS ---

@app.get("/api/mappings/services")
def get_service_mappings():
    """Returns department-to-service-groups mappings."""
    return mm.get_all_service_mappings()

@app.post("/api/mappings/services")
def update_service_mappings(mappings: dict):
    """Updates department-to-service-groups mappings."""
    mm.update_all_service_mappings(mappings)
    return {"status": "success", "message": "Service mappings updated successfully."}

@app.get("/api/mappings/pharmacies")
def get_pharmacy_mappings():
    """Returns department-to-pharmacy-warehouses mappings."""
    return mm.get_all_pharmacy_mappings()

@app.post("/api/mappings/pharmacies")
def update_pharmacy_mappings(mappings: dict):
    """Updates department-to-pharmacy-warehouses mappings."""
    mm.update_all_pharmacy_mappings(mappings)
    return {"status": "success", "message": "Pharmacy mappings updated successfully."}

@app.get("/api/mappings/script/services")
def get_service_mapping_script():
    """Returns Script 07 for exporting department service mappings from HIS."""
    script_path = os.path.join(BASE_DIR, "Scripts", "07_export_phan_quyen_dich_vu_theo_khoa.sql")
    if not os.path.exists(script_path):
        raise HTTPException(status_code=404, detail="Không tìm thấy script trích xuất dịch vụ.")
    with open(script_path, "r", encoding="utf-8") as f:
        return Response(content=f.read(), media_type="text/plain; charset=utf-8")

@app.get("/api/mappings/script/pharmacies")
def get_pharmacy_mapping_script():
    """Returns Script 08 for exporting department pharmacy mappings from HIS."""
    script_path = os.path.join(BASE_DIR, "Scripts", "08_export_anh_xa_kho_duoc_theo_khoa.sql")
    if not os.path.exists(script_path):
        raise HTTPException(status_code=404, detail="Không tìm thấy script trích xuất kho dược.")
    with open(script_path, "r", encoding="utf-8") as f:
        return Response(content=f.read(), media_type="text/plain; charset=utf-8")

@app.post("/api/mappings/upload/services")
def upload_service_mappings(file: UploadFile = File(...)):
    """Uploads Excel file to batch import service mappings."""
    temp_path = os.path.join(CONFIG_DIR, ".service_mapping_upload.xlsx")
    try:
        with open(temp_path, "wb") as f:
            shutil.copyfileobj(file.file, f)
        num_depts, row_count = mm.import_services_from_excel(temp_path)
        return {
            "status": "success",
            "message": f"Đã nạp thành công {row_count} liên kết dịch vụ cho {num_depts} khoa phòng.",
            "departments_count": num_depts,
            "rows_count": row_count
        }
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Lỗi nạp file phân quyền dịch vụ: {str(e)}")
    finally:
        if os.path.exists(temp_path):
            os.remove(temp_path)

@app.post("/api/mappings/upload/pharmacies")
def upload_pharmacy_mappings(file: UploadFile = File(...)):
    """Uploads Excel file to batch import pharmacy mappings."""
    temp_path = os.path.join(CONFIG_DIR, ".pharmacy_mapping_upload.xlsx")
    try:
        with open(temp_path, "wb") as f:
            shutil.copyfileobj(file.file, f)
        num_depts, row_count = mm.import_pharmacies_from_excel(temp_path)
        return {
            "status": "success",
            "message": f"Đã nạp thành công {row_count} liên kết kho dược cho {num_depts} khoa phòng.",
            "departments_count": num_depts,
            "rows_count": row_count
        }
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Lỗi nạp file ánh xạ kho dược: {str(e)}")
    finally:
        if os.path.exists(temp_path):
            os.remove(temp_path)

@app.get("/api/catalogs/status")
def get_catalogs_status():
    """Returns the status and record counts of the catalog Excel files."""
    status = {}
    files = {
        "patients.xlsx": len(cm.patients),
        "drugs.xlsx": len(cm.drugs),
        "services.xlsx": len(cm.services),
        "users.xlsx": len(cm.users),
        "service_mappings.xlsx": sum(len(v) for v in mm.service_mappings.values()),
        "pharmacy_mappings.xlsx": sum(len(v) for v in mm.pharmacy_mappings.values())
    }
    
    for filename, count in files.items():
        path = os.path.join(CATALOG_DIR, filename)
        exists = os.path.exists(path)
        last_modified = ""
        if exists:
            mtime = os.path.getmtime(path)
            last_modified = datetime.fromtimestamp(mtime).strftime("%d/%m/%Y %H:%M:%S")
            
        status[filename] = {
            "exists": exists,
            "count": count,
            "last_modified": last_modified,
            "script_available": filename in CATALOG_SCRIPTS or filename in ["service_mappings.xlsx", "pharmacy_mappings.xlsx"],
        }
        if filename == "patients.xlsx":
            status[filename].update(cm.get_patient_usage_status())
    return status

@app.post("/api/catalogs/reload")
def reload_catalogs():
    """Reloads Excel files into memory."""
    try:
        cm.reload_all()
        return {"status": "success", "message": "Catalogs reloaded successfully."}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Failed to reload catalogs: {str(e)}")

def clean_output_dir():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    for item in os.listdir(OUTPUT_DIR):
        item_path = os.path.join(OUTPUT_DIR, item)
        try:
            if os.path.isfile(item_path) or os.path.islink(item_path):
                os.unlink(item_path)
            elif os.path.isdir(item_path):
                shutil.rmtree(item_path, ignore_errors=True)
        except Exception:
            pass


def sanitize_filename_component(value):
    """Makes user/template text safe as one Windows filename component."""
    text = re.sub(r'[<>:"/\\|?*\x00-\x1f]+', "_", str(value or ""))
    text = re.sub(r"\s+", "_", text.strip())
    return text.strip(" ._") or "khong_ten"


def prepare_modular_candidate_data(
    template,
    exam_date,
    candidate,
    candidate_index,
    used_bhyt_cards
):
    dept_name = template.get("dept", "Khoa Ngoại tổng hợp")
    user = cm.get_user_for_dept(dept_name) if template.get("uses_his", True) else None
    
    # 1. Pre-allocate primary BHYT and VP patients for this candidate if HIS is used
    primary_bn_bhyt = None
    primary_bn_vp = None
    if template.get("uses_his", True):
        try:
            raw_bhyt = cm.get_patients(1, must_have_bhyt=True, valid_on=exam_date)[0]
            source_card = str(raw_bhyt.get("SoBHYT") or "").strip().upper()
            if len(source_card) == 15:
                prefix = source_card[:5]
                seed = f"{exam_date}|{candidate.id}|{candidate.name}|primary|{candidate_index}"
                salt = 0
                while True:
                    digest = hashlib.sha256(f"{seed}|{salt}".encode("utf-8")).hexdigest()
                    suffix = f"{int(digest[:16], 16) % 10_000_000_000:010d}"
                    exam_card = prefix + suffix
                    if exam_card != source_card and exam_card not in used_bhyt_cards:
                        break
                    salt += 1
                used_bhyt_cards.add(exam_card)
                primary_bn_bhyt = {
                    **raw_bhyt,
                    "SoBHYT": exam_card,
                    "SoBHYT_Nguon": source_card,
                }
            else:
                primary_bn_bhyt = raw_bhyt
        except Exception:
            primary_bn_bhyt = None
            
        try:
            primary_bn_vp = cm.get_patients(1, must_have_bhyt=False)[0]
        except Exception:
            primary_bn_vp = None

    context = {
        "bn_bhyt": primary_bn_bhyt,
        "bn_vp": primary_bn_vp,
        "ordered_services": [],
        "ordered_drugs": [],
        "ordered_supplies": []
    }

    cand_data = {
        "exam_date": exam_date,
        "dept_name": dept_name,
        "user": user,
        "uses_his": template.get("uses_his", True),
        "bn_bhyt": primary_bn_bhyt,
        "bn_vp": primary_bn_vp,
        "action_results": [],
        "action_codes": []
    }
    
    for action_item in template.get("actions", []):
        code = action_item["action_code"]
        score = action_item.get("score", 1.0)
        params = action_item.get("params", {})
        cand_data["action_codes"].append(code)
        
        action_spec = ACTION_REGISTRY.get(code)
        if action_spec and "prepare_data" in action_spec:
            act_data = action_spec["prepare_data"](cm, mm, dept_name, exam_date, params, context)
            
            if "bn_bhyt" in act_data:
                cand_data["bn_bhyt"] = act_data["bn_bhyt"]
            if "bn_vp" in act_data:
                cand_data["bn_vp"] = act_data["bn_vp"]
                
            cand_data["action_results"].append({
                "action_code": code,
                "score": score,
                "data": act_data
            })
            
    return cand_data

def prepare_candidate_data(
    generator,
    exam_date,
    dept_name,
    candidate,
    candidate_index,
    used_bhyt_cards
):
    """Generate data and replace real BHYT identifiers with unique exam cards."""
    data = generator(cm, exam_date, dept_name)
    bhyt_patient = data.get("bn_bhyt")
    if not bhyt_patient:
        return data

    source_card = str(bhyt_patient.get("SoBHYT") or "").strip().upper()
    if len(source_card) != 15:
        raise ValueError(
            f"Số BHYT nguồn của {bhyt_patient.get('TenBenhNhan')} "
            "phải đúng 15 ký tự."
        )

    # Preserve the benefit/province prefix used by HIS to resolve DM_DoiTuong.
    prefix = source_card[:5]
    seed = "|".join(
        [
            exam_date,
            candidate.id,
            candidate.name,
            candidate.position or "",
            str(candidate_index),
        ]
    )
    salt = 0
    while True:
        digest = hashlib.sha256(f"{seed}|{salt}".encode("utf-8")).hexdigest()
        suffix = f"{int(digest[:16], 16) % 10_000_000_000:010d}"
        exam_card = prefix + suffix
        if exam_card != source_card and exam_card not in used_bhyt_cards:
            break
        salt += 1

    used_bhyt_cards.add(exam_card)
    data["bn_bhyt"] = {
        **bhyt_patient,
        "SoBHYT": exam_card,
        "SoBHYT_Nguon": source_card,
    }
    return data

@app.post("/api/generate")
def generate_exams(req: GenerateRequest, background_tasks: BackgroundTasks):
    """Generates Word exam papers and SQL scripts for all candidates, packaging them in a ZIP."""
    if not req.candidates:
        raise HTTPException(status_code=400, detail="Danh sách thí sinh không được rỗng.")

    # Resolve template for each candidate
    resolved_candidates = []
    for cand in req.candidates:
        template = None
        if cand.template_id:
            template = tm.get_template_by_id(cand.template_id)
        if not template and cand.position:
            # Check by template name or ID in TemplateManager
            for t in tm.get_all_templates():
                if t["id"] == cand.position or t["name"] == cand.position:
                    template = t
                    break
        if not template and cand.position in EXAM_TEMPLATES:
            legacy_t = EXAM_TEMPLATES[cand.position]
            template = {
                "name": cand.position,
                "dept": legacy_t["dept_name"],
                "template_type": legacy_t["template_type"],
                "uses_his": legacy_t.get("uses_his", True),
                "legacy_generator": legacy_t["generator"],
                "default_scores": legacy_t.get("default_scores", [])
            }
            
        if not template:
            raise HTTPException(
                status_code=400,
                detail=f"Không tìm thấy mẫu đề thi cho thí sinh '{cand.name}'.",
            )
        resolved_candidates.append((cand, template))

    his_candidate_count = sum(
        t.get("uses_his", True) for _, t in resolved_candidates
    )
    
    # 1. Clean output folder
    clean_output_dir()
    
    patient_session = (
        cm.start_patient_selection() if his_candidate_count else None
    )
    try:
        generated_files = []
        combined_sql = []
        used_bhyt_cards = set()
        
        if his_candidate_count:
            combined_sql.append(f"-- ==========================================================================")
            combined_sql.append(f"-- KỊCH BẢN TỔNG HỢP NẠP DỮ LIỆU THI - eHospital_ThienHanh")
            combined_sql.append(f"-- Số lượng thí sinh dùng HIS: {his_candidate_count}")
            combined_sql.append(f"-- Ngày thi: {req.exam_date}")
            combined_sql.append(f"-- Sinh ngày: {datetime.now().strftime('%d/%m/%Y %H:%M:%S')}")
            combined_sql.append(f"--")
            combined_sql.append(f"-- AN TOÀN: Các khối tạo dữ liệu mặc định @Commit = 0 và sẽ ROLLBACK.")
            combined_sql.append(f"-- Chạy thử toàn bộ, kiểm tra BenhAn_Id = NULL và các ID danh mục.")
            combined_sql.append(f"-- Chỉ thay @Commit = 1 sau khi kết quả chạy thử đạt yêu cầu.")
            combined_sql.append(f"-- ==========================================================================\n")
        
        for candidate_index, (cand, tmpl) in enumerate(resolved_candidates, start=1):
            is_modular = "actions" in tmpl and tmpl["actions"]
            
            if is_modular:
                # Prepare data using modular action handlers
                cand_data = prepare_modular_candidate_data(
                    tmpl,
                    req.exam_date,
                    cand,
                    candidate_index,
                    used_bhyt_cards
                )
            else:
                # Legacy generator
                cand_data = prepare_candidate_data(
                    tmpl["legacy_generator"],
                    req.exam_date,
                    tmpl["dept"],
                    cand,
                    candidate_index,
                    used_bhyt_cards
                )
            
            # Sanitise filename
            position_title = tmpl.get("name", cand.position or "De_Thi")
            safe_name = sanitize_filename_component(cand.name)
            safe_position = sanitize_filename_component(position_title)
            if cand.id:
                safe_id = sanitize_filename_component(cand.id)
                doc_filename = f"{safe_position}_{safe_id}_{safe_name}.docx"
            else:
                doc_filename = f"{safe_position}_{safe_name}.docx"
            doc_path = os.path.join(OUTPUT_DIR, doc_filename)
            
            # Extract scores
            if cand.scores:
                scores_to_use = cand.scores
            elif is_modular:
                scores_to_use = [a.get("score", 1.0) for a in tmpl.get("actions", [])]
            else:
                scores_to_use = tmpl.get("default_scores", [])
            
            # Generate docx
            generate_docx_file(
                cand_data, 
                tmpl.get("template_type", "MODULAR"), 
                cand.name, 
                cand.id, 
                tmpl.get("position", tmpl.get("name", "Thí sinh")), 
                doc_path,
                scores=scores_to_use
            )
            generated_files.append(doc_path)

            has_excel_action = (
                is_modular and any(a["action_code"] == "VP_XU_LY_EXCEL" for a in tmpl.get("actions", []))
            ) or (not tmpl.get("uses_his", True))
            
            if has_excel_action:
                excel_filename = (
                    f"Du_Lieu_Excel_{safe_id}_{safe_name}.xlsx"
                    if cand.id
                    else f"Du_Lieu_Excel_{safe_name}.xlsx"
                )
                excel_path = os.path.join(OUTPUT_DIR, excel_filename)
                generate_office_excel_file(
                    excel_path,
                    row_count=cand_data.get("excel_dataset_count", 20),
                )
                generated_files.append(excel_path)
            
            if tmpl.get("uses_his", True):
                cand_sql = generate_sql_script(
                    cand_data, 
                    tmpl.get("template_type", "MODULAR"), 
                    cand.name, 
                    cand.id, 
                    tmpl.get("position", tmpl.get("name", "Thí sinh")),
                    tmpl.get("dept", "Khoa"),
                    actions=tmpl.get("actions", [])
                )
                combined_sql.append(cand_sql)
                combined_sql.append("\n-- --------------------------------------------------------------------------\n")
            
        if his_candidate_count:
            sql_filename = f"Nap_Du_Lieu_Thi_{datetime.now().strftime('%d%m%Y_%H%M%S')}.sql"
            sql_path = os.path.join(OUTPUT_DIR, sql_filename)
            with open(sql_path, "w", encoding="utf-8") as f:
                f.write("\n".join(combined_sql))
            generated_files.append(sql_path)

            lookup_script = os.path.join(
                BASE_DIR, "Scripts", "01_lookup_tiep_nhan_nhap_vien.sql"
            )
            if os.path.exists(lookup_script):
                generated_files.append(lookup_script)
        
        # Package everything into ZIP
        zip_prefix = (
            "De_Thi_Va_SQL" if his_candidate_count
            else "De_Thi_Tin_Hoc_Van_Phong"
        )
        zip_filename = f"{zip_prefix}_{datetime.now().strftime('%Y%m%d_%H%M%S')}.zip"
        zip_path = os.path.join(OUTPUT_DIR, zip_filename)
        
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zip_file:
            for file_path in generated_files:
                arcname = os.path.basename(file_path)
                zip_file.write(file_path, arcname)

        if patient_session:
            patient_session.commit()
                
        return FileResponse(
            zip_path, 
            media_type="application/x-zip-compressed", 
            filename=zip_filename
        )
        
    except Exception as e:
        import traceback
        traceback.print_exc()
        raise HTTPException(status_code=500, detail=f"Lỗi sinh đề thi: {str(e)}")
    finally:
        if patient_session:
            patient_session.close()

@app.get("/api/catalogs/download/{filename}")
def download_catalog(filename: str):
    """Serves the catalog Excel file for download."""
    allowed = ["patients.xlsx", "drugs.xlsx", "services.xlsx", "users.xlsx", "service_mappings.xlsx", "pharmacy_mappings.xlsx"]
    if filename not in allowed:
        raise HTTPException(status_code=400, detail="Invalid catalog filename.")
    path = os.path.join(CATALOG_DIR, filename)
    if not os.path.exists(path):
        raise HTTPException(status_code=404, detail="File not found.")
    return FileResponse(
        path, 
        media_type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 
        filename=filename
    )


@app.get("/api/catalogs/script/{filename}")
def download_catalog_script(filename: str):
    """Download a read-only HIS query that exports one Excel catalog."""
    script_path = CATALOG_SCRIPTS.get(filename)
    if not script_path:
        raise HTTPException(
            status_code=404,
            detail=f"Chưa có script lấy dữ liệu cho {filename}.",
        )
    if not os.path.exists(script_path):
        raise HTTPException(status_code=500, detail="Thiếu file script trên máy chủ.")
    return FileResponse(
        script_path,
        media_type="application/sql; charset=utf-8",
        filename=os.path.basename(script_path),
    )

@app.post("/api/catalogs/upload/{filename}")
def upload_catalog(
    filename: str,
    file: UploadFile = File(...),
    exam_year: Optional[int] = None,
):
    """Uploads and overwrites a catalog Excel file, and reloads it in memory."""
    if filename not in ["patients.xlsx", "drugs.xlsx", "services.xlsx", "users.xlsx"]:
        raise HTTPException(status_code=400, detail="Invalid catalog filename.")
    path = os.path.join(CATALOG_DIR, filename)
    temp_path = os.path.join(CATALOG_DIR, f".{filename}.uploading.xlsx")
    try:
        os.makedirs(CATALOG_DIR, exist_ok=True)
        if filename == "patients.xlsx":
            exam_year = exam_year or datetime.now().year
            if not 2000 <= exam_year <= 2100:
                raise ValueError("Năm thi phải nằm trong khoảng 2000 đến 2100.")

        with open(temp_path, "wb") as f:
            shutil.copyfileobj(file.file, f)

        normalized_count = 0
        if filename == "patients.xlsx":
            normalized_count = cm.normalize_patient_catalog(
                exam_year, file_path=temp_path
            )

        os.replace(temp_path, path)
        cm.reload_all()
        message = f"{filename} uploaded and reloaded successfully."
        if filename == "patients.xlsx":
            message = (
                f"Đã tải patients.xlsx và chuẩn hóa hạn BHYT của "
                f"{normalized_count} bệnh nhân theo năm thi {exam_year}."
            )
        return {
            "status": "success",
            "message": message,
            "exam_year": exam_year if filename == "patients.xlsx" else None,
            "normalized_bhyt_count": normalized_count,
        }
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Failed to upload: {str(e)}")
    finally:
        if os.path.exists(temp_path):
            os.remove(temp_path)

@app.get("/api/catalogs/data/{filename}")
def get_catalog_data(filename: str):
    """Returns the parsed records in the catalog."""
    if filename == "patients.xlsx":
        return cm.patients
    elif filename == "drugs.xlsx":
        return cm.drugs
    elif filename == "services.xlsx":
        return cm.services
    elif filename == "users.xlsx":
        return cm.users
    else:
        raise HTTPException(status_code=400, detail="Invalid catalog filename.")


def get_inventory_catalog_context():
    """Builds freshness metadata for the uploaded drugs.xlsx snapshot."""
    options = cm.get_inventory_options()
    path = os.path.join(CATALOG_DIR, "drugs.xlsx")
    catalog_updated_at = ""
    if os.path.exists(path):
        catalog_updated_at = datetime.fromtimestamp(
            os.path.getmtime(path)
        ).strftime("%d/%m/%Y %H:%M:%S")
    return {
        **options,
        "record_count": len(cm.drugs),
        "catalog_updated_at": catalog_updated_at,
        "is_live": False,
    }


@app.get("/api/inventory/options")
def get_inventory_options():
    """Returns filters and snapshot metadata from the uploaded drug catalog."""
    return get_inventory_catalog_context()


@app.get("/api/inventory/search")
def search_inventory(
    department: str = "",
    warehouse: str = "",
    query: str = "",
    source: str = "",
    min_stock: float = 0,
    limit: int = 200,
):
    """Searches the uploaded stock snapshot; this endpoint does not query HIS."""
    if min_stock < 0:
        raise HTTPException(
            status_code=400,
            detail="Tồn tối thiểu không được nhỏ hơn 0.",
        )
    if limit < 1 or limit > 500:
        raise HTTPException(
            status_code=400,
            detail="Giới hạn kết quả phải nằm trong khoảng 1 đến 500.",
        )

    result = cm.search_inventory(
        department=department,
        warehouse=warehouse,
        query=query,
        source=source,
        min_stock=min_stock,
        limit=limit,
    )
    context = get_inventory_catalog_context()
    return {
        **result,
        "snapshot_times": context["snapshot_times"],
        "catalog_updated_at": context["catalog_updated_at"],
        "has_department_mapping": context["has_department_mapping"],
        "is_live": False,
    }


# Mount frontend files
if os.path.exists(FRONTEND_DIR):
    app.mount("/", StaticFiles(directory=FRONTEND_DIR, html=True), name="frontend")
else:
    # Minimal response if frontend folder is not created yet
    @app.get("/")
    def read_root():
        return {"message": "FastAPI backend running. Please build the frontend."}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8000)
