import random
from datetime import datetime, timedelta

def format_date_str(date_obj):
    if isinstance(date_obj, datetime):
        return date_obj.strftime("%d/%m/%Y")
    return str(date_obj)

def get_cutoff_date(exam_date_str, days_back=30):
    try:
        exam_dt = datetime.strptime(exam_date_str, "%d/%m/%Y")
    except Exception:
        exam_dt = datetime.now()
    cutoff_dt = exam_dt - timedelta(days=days_back)
    start_dt = cutoff_dt - timedelta(days=60)
    return start_dt.strftime("%d/%m/%Y"), cutoff_dt.strftime("%d/%m/%Y")

# 1. GENERATOR FOR NURSE_INPATIENT (Điều dưỡng / Nữ hộ sinh Nội trú)
def generate_nurse_inpatient(cm, exam_date, dept_name):
    bn_bhyt = cm.get_patients(1, must_have_bhyt=True, valid_on=exam_date)[0]
    bn_vp = cm.get_patients(1, must_have_bhyt=False)[0]
    
    img_services = cm.get_services(2, nhom=["Siêu âm", "X-quang", "CT Scan"], khoa=dept_name)
    lab_services = cm.get_services(5, nhom="Xét nghiệm")
    
    bhyt_drugs = cm.get_drugs(3, nguon="BH", min_ton=10.0, khoa=dept_name)
    vp_supplies = cm.get_drugs(2, nguon="VP", min_ton=10.0, khoa=dept_name)
    
    returned_drug = bhyt_drugs[0] if bhyt_drugs else cm.get_drugs(1, nguon="BH")[0]
    
    swap_out_service = img_services[0] if img_services else cm.get_services(1, nhom="Siêu âm")[0]
    exclude_codes = [s["MaDichVu"] for s in img_services]
    swap_in_pool = [s for s in cm.services if s["NhomDichVu"] in ["Siêu âm", "X-quang", "CT Scan"] and s["MaDichVu"] not in exclude_codes]
    swap_in_service = random.choice(swap_in_pool) if swap_in_pool else cm.get_services(1, nhom="Siêu âm")[0]
    
    added_service_pool = [s for s in cm.services if s["MaDichVu"] not in exclude_codes and s["MaDichVu"] != swap_in_service["MaDichVu"]]
    added_service = random.choice(added_service_pool) if added_service_pool else cm.get_services(1, nhom="Thủ thuật")[0]
    
    check_drug = cm.get_drugs(1, khoa=dept_name)[0]
    start_date, end_date = get_cutoff_date(exam_date, days_back=30)
    
    target_depts = [
        "Khoa Ngoại tổng hợp",
        "Khoa Chấn thương chỉnh hình",
        "Khoa Hồi sức tích cực",
        "Khoa Nội",
    ]
    target_dept = random.choice([d for d in target_depts if d != dept_name] or target_depts)
    
    user = cm.get_user_for_dept(dept_name)
    
    return {
        "bn_bhyt": bn_bhyt,
        "bn_vp": bn_vp,
        "imaging_services": img_services,
        "lab_services": lab_services,
        "bhyt_drugs": bhyt_drugs,
        "vp_supplies": vp_supplies,
        "returned_drug": returned_drug,
        "swap_out_service": swap_out_service,
        "swap_in_service": swap_in_service,
        "added_service": added_service,
        "check_drug": check_drug,
        "check_start_date": start_date,
        "check_end_date": end_date,
        "transfer_target_dept": target_dept,
        "user": user,
        "exam_date": exam_date
    }

# 2. GENERATOR FOR RECEPTIONIST_INPATIENT (Lễ tân Khoa Điều trị Nội trú)
def generate_receptionist_inpatient(cm, exam_date, dept_name):
    bn_bhyt = cm.get_patients(1, must_have_bhyt=True, valid_on=exam_date)[0]
    bn_vp = cm.get_patients(1, must_have_bhyt=False)[0]
    
    deposit_cases = [
        {"ma_ba": "26004776", "tien": "1,500,000"},
        {"ma_ba": "26001724/NG", "tien": "500,000"}
    ]
    search_patient = cm.get_patients(1)[0]
    user = cm.get_user_for_dept(dept_name)
    
    return {
        "bn_bhyt": bn_bhyt,
        "bn_vp": bn_vp,
        "deposit_cases": deposit_cases,
        "search_patient": search_patient,
        "user": user,
        "exam_date": exam_date
    }

# 3. GENERATOR FOR NURSE_DIRECT_RECEPTION (Điều dưỡng Cấp cứu, Sản, Nhi)
def generate_nurse_direct_reception(cm, exam_date, dept_name):
    bn_bhyt = cm.get_patients(1, must_have_bhyt=True, valid_on=exam_date)[0]
    bn_vp = cm.get_patients(1, must_have_bhyt=False)[0]
    
    img_services = cm.get_services(2, nhom=["Siêu âm", "X-quang"], khoa=dept_name)
    lab_services = cm.get_services(4, nhom="Xét nghiệm")
    
    drugs = cm.get_drugs(3, nguon="BH", khoa=dept_name)
    supplies = cm.get_drugs(2, nguon="VP", khoa=dept_name)
    returned_drug = drugs[0] if drugs else cm.get_drugs(1)[0]
    
    check_drug = cm.get_drugs(1, khoa=dept_name)[0]
    start_date, end_date = get_cutoff_date(exam_date, days_back=30)
    
    user = cm.get_user_for_dept(dept_name)
    
    return {
        "bn_bhyt": bn_bhyt,
        "bn_vp": bn_vp,
        "img_services": img_services,
        "lab_services": lab_services,
        "drugs": drugs,
        "supplies": supplies,
        "returned_drug": returned_drug,
        "check_drug": check_drug,
        "check_start_date": start_date,
        "check_end_date": end_date,
        "user": user,
        "exam_date": exam_date
    }

# 4. GENERATOR FOR RECEPTIONIST_DIRECT (Lễ tân Cấp cứu, Sản, Nhi, Khám bệnh)
def generate_receptionist_direct(cm, exam_date, dept_name):
    bn_bhyt = cm.get_patients(1, must_have_bhyt=True, valid_on=exam_date)[0]
    bn_vp = cm.get_patients(1, must_have_bhyt=False)[0]
    
    img_services = cm.get_services(2, nhom=["Siêu âm", "X-quang"])
    lab_services = cm.get_services(3, nhom="Xét nghiệm")
    
    deposit_cases = [
        {"ma_ba": "26004776", "tien": "1,500,000"},
        {"ma_ba": "26001724/NG", "tien": "500,000"}
    ]
    search_patient = cm.get_patients(1)[0]
    user = cm.get_user_for_dept(dept_name)
    
    return {
        "bn_bhyt": bn_bhyt,
        "bn_vp": bn_vp,
        "img_services": img_services,
        "lab_services": lab_services,
        "deposit_cases": deposit_cases,
        "search_patient": search_patient,
        "user": user,
        "exam_date": exam_date
    }

# 5. GENERATOR FOR NURSE_OUTPATIENT (Điều dưỡng Ngoại trú)
def generate_nurse_outpatient(cm, exam_date, dept_name):
    bn_bhyt = cm.get_patients(1, must_have_bhyt=True, valid_on=exam_date)[0]
    bn_vp = cm.get_patients(1, must_have_bhyt=False)[0]
    
    services = cm.get_services(4, khoa=dept_name)
    drugs = cm.get_drugs(4, nguon="BH", khoa=dept_name)
    
    user = cm.get_user_for_dept(dept_name)
    
    return {
        "bn_bhyt": bn_bhyt,
        "bn_vp": bn_vp,
        "services": services,
        "drugs": drugs,
        "user": user,
        "exam_date": exam_date
    }

# 6. GENERATOR FOR TECHNICIAN (Kỹ thuật viên Cận lâm sàng & Dược sĩ)
def generate_technician(cm, exam_date, dept_name):
    patients = cm.get_patients(5)
    services = cm.get_services(5, khoa=dept_name)
    
    list_patients_services = []
    for idx, p in enumerate(patients):
        list_patients_services.append({
            "patient": p,
            "service": services[idx] if idx < len(services) else services[0]
        })
        
    supplies = cm.get_drugs(4, nguon="VP", min_ton=10.0, khoa=dept_name)
    user = cm.get_user_for_dept(dept_name)
    
    return {
        "list_patients_services": list_patients_services,
        "supplies": supplies,
        "user": user,
        "exam_date": exam_date
    }

# 7. GENERATOR FOR CASHIER_COUNTER (Thu ngân Quầy Thu phí)
def generate_cashier_counter(cm, exam_date, dept_name):
    bn_bhyt = cm.get_patients(1, must_have_bhyt=True, valid_on=exam_date)[0]
    bn_vp = cm.get_patients(1, must_have_bhyt=False)[0]
    
    img_services = cm.get_services(2, nhom=["Siêu âm", "X-quang"])
    lab_services = cm.get_services(3, nhom="Xét nghiệm")
    
    deposit_cases = [
        {"ma_ba": "26004776", "tien": "1,500,000"},
        {"ma_ba": "26001724/NG", "tien": "500,000"}
    ]
    search_patient = cm.get_patients(1)[0]
    user = cm.get_user_for_dept(dept_name)
    
    return {
        "bn_bhyt": bn_bhyt,
        "bn_vp": bn_vp,
        "img_services": img_services,
        "lab_services": lab_services,
        "deposit_cases": deposit_cases,
        "search_patient": search_patient,
        "user": user,
        "exam_date": exam_date
    }

# 8. GENERATOR FOR OFFICE_ADMIN (Hành chính / Văn phòng)
def generate_office_admin(cm, exam_date, dept_name):
    return {
        "doc_title": "TỜ TRÌNH VỀ VIỆC MUA SẮM THIẾT BỊ VĂN PHÒNG",
        "excel_dataset_count": 20,
        "exam_date": exam_date
    }

# FULL DEPARTMENT X POSITION MATRIX
EXAM_TEMPLATES = {
    # Khoa Cấp cứu
    "Điều dưỡng Cấp cứu": {
        "template_type": "NURSE_DIRECT_RECEPTION",
        "dept_name": "Khoa Cấp cứu",
        "generator": generate_nurse_direct_reception,
        "default_scores": [2.0, 1.5, 3.0, 1.0, 1.0, 1.5]
    },
    "Lễ tân Cấp cứu": {
        "template_type": "RECEPTIONIST_DIRECT",
        "dept_name": "Khoa Cấp cứu",
        "generator": generate_receptionist_direct,
        "default_scores": [3.0, 2.5, 2.5, 2.0]
    },

    # Khoa Sản
    "Điều dưỡng / Nữ hộ sinh Sản": {
        "template_type": "NURSE_DIRECT_RECEPTION",
        "dept_name": "Khoa Phụ Sản",
        "generator": generate_nurse_direct_reception,
        "default_scores": [2.0, 1.5, 3.0, 1.0, 1.0, 1.5]
    },
    "Lễ tân Khoa Sản": {
        "template_type": "RECEPTIONIST_DIRECT",
        "dept_name": "Khoa Phụ Sản",
        "generator": generate_receptionist_direct,
        "default_scores": [3.0, 2.5, 2.5, 2.0]
    },

    # Khoa Nhi
    "Điều dưỡng Nhi": {
        "template_type": "NURSE_DIRECT_RECEPTION",
        "dept_name": "Khoa Nhi",
        "generator": generate_nurse_direct_reception,
        "default_scores": [2.0, 1.5, 3.0, 1.0, 1.0, 1.5]
    },
    "Lễ tân Khoa Nhi": {
        "template_type": "RECEPTIONIST_DIRECT",
        "dept_name": "Khoa Nhi",
        "generator": generate_receptionist_direct,
        "default_scores": [3.0, 2.5, 2.5, 2.0]
    },

    # Khoa Nội
    "Điều dưỡng Khoa Nội": {
        "template_type": "NURSE_INPATIENT",
        "dept_name": "Khoa Nội",
        "generator": generate_nurse_inpatient,
        "default_scores": [1.0, 1.0, 3.0, 1.0, 1.0, 1.0, 1.0, 1.0]
    },
    "Lễ tân Khoa Nội": {
        "template_type": "RECEPTIONIST_INPATIENT",
        "dept_name": "Khoa Nội",
        "generator": generate_receptionist_inpatient,
        "default_scores": [3.0, 4.0, 3.0]
    },

    # Khoa Ngoại tổng hợp
    "Điều dưỡng Khoa Ngoại TH": {
        "template_type": "NURSE_INPATIENT",
        "dept_name": "Khoa Ngoại tổng hợp",
        "generator": generate_nurse_inpatient,
        "default_scores": [1.0, 1.0, 3.0, 1.0, 1.0, 1.0, 1.0, 1.0]
    },
    "Lễ tân Khoa Ngoại TH": {
        "template_type": "RECEPTIONIST_INPATIENT",
        "dept_name": "Khoa Ngoại tổng hợp",
        "generator": generate_receptionist_inpatient,
        "default_scores": [3.0, 4.0, 3.0]
    },

    # Khoa Ngoại Chấn thương chỉnh hình
    "Điều dưỡng Khoa Ngoại CTCH": {
        "template_type": "NURSE_INPATIENT",
        "dept_name": "Khoa Chấn thương chỉnh hình",
        "generator": generate_nurse_inpatient,
        "default_scores": [1.0, 1.0, 3.0, 1.0, 1.0, 1.0, 1.0, 1.0]
    },
    "Lễ tân Khoa Ngoại CTCH": {
        "template_type": "RECEPTIONIST_INPATIENT",
        "dept_name": "Khoa Chấn thương chỉnh hình",
        "generator": generate_receptionist_inpatient,
        "default_scores": [3.0, 4.0, 3.0]
    },

    # Khoa Ngoại thần kinh
    "Điều dưỡng Khoa Ngoại TK": {
        "template_type": "NURSE_INPATIENT",
        "dept_name": "Khoa Ngoại thần kinh",
        "generator": generate_nurse_inpatient,
        "default_scores": [1.0, 1.0, 3.0, 1.0, 1.0, 1.0, 1.0, 1.0]
    },
    "Lễ tân Khoa Ngoại TK": {
        "template_type": "RECEPTIONIST_INPATIENT",
        "dept_name": "Khoa Ngoại thần kinh",
        "generator": generate_receptionist_inpatient,
        "default_scores": [3.0, 4.0, 3.0]
    },

    # Khoa Hồi sức tích cực
    "Điều dưỡng Khoa HSTC": {
        "template_type": "NURSE_INPATIENT",
        "dept_name": "Khoa Hồi sức tích cực",
        "generator": generate_nurse_inpatient,
        "default_scores": [1.0, 1.0, 3.0, 1.0, 1.0, 1.0, 1.0, 1.0]
    },
    "Lễ tân Khoa HSTC": {
        "template_type": "RECEPTIONIST_INPATIENT",
        "dept_name": "Khoa Hồi sức tích cực",
        "generator": generate_receptionist_inpatient,
        "default_scores": [3.0, 4.0, 3.0]
    },

    # Khoa Khám bệnh
    "Điều dưỡng Khám bệnh": {
        "template_type": "NURSE_OUTPATIENT",
        "dept_name": "Khoa Khám bệnh",
        "generator": generate_nurse_outpatient,
        "default_scores": [2.0, 2.0, 3.0, 1.5, 1.5]
    },
    "Lễ tân Khám bệnh": {
        "template_type": "RECEPTIONIST_DIRECT",
        "dept_name": "Khoa Khám bệnh",
        "generator": generate_receptionist_direct,
        "default_scores": [3.0, 2.5, 2.5, 2.0]
    },

    # Các Khoa Ngoại trú Chuyên khoa
    "Điều dưỡng Khoa Mắt": {
        "template_type": "NURSE_OUTPATIENT",
        "dept_name": "Khoa Mắt",
        "generator": generate_nurse_outpatient,
        "default_scores": [2.0, 2.0, 3.0, 1.5, 1.5]
    },
    "Điều dưỡng Khoa TMH": {
        "template_type": "NURSE_OUTPATIENT",
        "dept_name": "Khoa Tai Mũi Họng",
        "generator": generate_nurse_outpatient,
        "default_scores": [2.0, 2.0, 3.0, 1.5, 1.5]
    },
    "Điều dưỡng Khoa RHM": {
        "template_type": "NURSE_OUTPATIENT",
        "dept_name": "Khoa Răng Hàm Mặt",
        "generator": generate_nurse_outpatient,
        "default_scores": [2.0, 2.0, 3.0, 1.5, 1.5]
    },
    "Điều dưỡng Khoa Thận nhân tạo": {
        "template_type": "NURSE_OUTPATIENT",
        "dept_name": "Thận nhân tạo",
        "generator": generate_nurse_outpatient,
        "default_scores": [2.0, 2.0, 3.0, 1.5, 1.5]
    },

    # Khoa Cận lâm sàng (KTV & Điều dưỡng)
    "Kỹ thuật viên Xét nghiệm": {
        "template_type": "TECHNICIAN",
        "dept_name": "Khoa Xét Nghiệm",
        "generator": generate_technician,
        "default_scores": [6.0, 4.0]
    },
    "Điều dưỡng Khoa Xét nghiệm": {
        "template_type": "NURSE_OUTPATIENT",
        "dept_name": "Khoa Xét Nghiệm",
        "generator": generate_nurse_outpatient,
        "default_scores": [2.0, 2.0, 3.0, 1.5, 1.5]
    },

    "Kỹ thuật viên Chẩn đoán hình ảnh": {
        "template_type": "TECHNICIAN",
        "dept_name": "Khoa Chẩn đoán hình ảnh",
        "generator": generate_technician,
        "default_scores": [6.0, 4.0]
    },
    "Điều dưỡng Khoa CĐHA": {
        "template_type": "NURSE_OUTPATIENT",
        "dept_name": "Khoa Chẩn đoán hình ảnh",
        "generator": generate_nurse_outpatient,
        "default_scores": [2.0, 2.0, 3.0, 1.5, 1.5]
    },

    "Kỹ thuật viên Nội soi": {
        "template_type": "TECHNICIAN",
        "dept_name": "Khoa Nội soi",
        "generator": generate_technician,
        "default_scores": [6.0, 4.0]
    },
    "Điều dưỡng Khoa Nội soi": {
        "template_type": "NURSE_OUTPATIENT",
        "dept_name": "Khoa Nội soi",
        "generator": generate_nurse_outpatient,
        "default_scores": [2.0, 2.0, 3.0, 1.5, 1.5]
    },

    "Kỹ thuật viên Gây mê hồi sức": {
        "template_type": "TECHNICIAN",
        "dept_name": "Khoa Phẫu thuật - GMHS",
        "generator": generate_technician,
        "default_scores": [6.0, 4.0]
    },
    "Điều dưỡng Gây mê hồi sức": {
        "template_type": "NURSE_INPATIENT",
        "dept_name": "Khoa Phẫu thuật - GMHS",
        "generator": generate_nurse_inpatient,
        "default_scores": [1.0, 1.0, 3.0, 1.0, 1.0, 1.0, 1.0, 1.0]
    },

    "Kỹ thuật viên Tim mạch": {
        "template_type": "TECHNICIAN",
        "dept_name": "Khoa Tim mạch",
        "generator": generate_technician,
        "default_scores": [6.0, 4.0]
    },
    "Điều dưỡng Khoa Tim mạch": {
        "template_type": "NURSE_INPATIENT",
        "dept_name": "Khoa Tim mạch",
        "generator": generate_nurse_inpatient,
        "default_scores": [1.0, 1.0, 3.0, 1.0, 1.0, 1.0, 1.0, 1.0]
    },

    # Khoa Dược
    "Dược sĩ Lâm sàng": {
        "template_type": "TECHNICIAN",
        "dept_name": "Khoa Dược",
        "generator": generate_technician,
        "default_scores": [6.0, 4.0]
    },

    # Quầy Thu phí
    "Thu ngân Quầy Thu phí": {
        "template_type": "CASHIER_COUNTER",
        "dept_name": "Phòng Tài chính kế toán",
        "generator": generate_cashier_counter,
        "default_scores": [3.0, 2.5, 2.5, 2.0]
    },

    # Hành chính / CSKH
    "Nhân viên Chăm sóc khách hàng": {
        "template_type": "OFFICE_ADMIN",
        "dept_name": "Phòng Tiếp thị - Chăm sóc khách hàng",
        "generator": generate_office_admin,
        "default_scores": [5.0, 5.0],
        "uses_his": False,
    },
    "Nhân viên Hành chính văn phòng": {
        "template_type": "OFFICE_ADMIN",
        "dept_name": "Phòng Tổ chức - Hành chính",
        "generator": generate_office_admin,
        "default_scores": [5.0, 5.0],
        "uses_his": False,
    }
}
