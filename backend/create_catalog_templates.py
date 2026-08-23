import openpyxl
import os

def create_folders():
    paths = [
        r"C:\Users\Admin\.gemini\antigravity\scratch\ExamGenerator\backend",
        r"C:\Users\Admin\.gemini\antigravity\scratch\ExamGenerator\frontend",
        r"C:\Users\Admin\.gemini\antigravity\scratch\ExamGenerator\data\catalogs",
        r"C:\Users\Admin\.gemini\antigravity\scratch\ExamGenerator\data\output"
    ]
    for p in paths:
        if not os.path.exists(p):
            os.makedirs(p)
            print(f"Created folder: {p}")

def create_patients_catalog():
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.title = "Patients"
    
    headers = ["MaYTe", "TenBenhNhan", "NgaySinh", "GioiTinh", "DiaChiLienHe", "SoBHYT", "BHYTTuNgay", "BHYTDenNgay", "DKKCB"]
    ws.append(headers)
    
    sample_data = [
        ["26034346", "Lê Rất Vui", "1991-11-02", "Nữ", "Thôn 1 , Xã Hòa Phú, Thành phố Buôn Ma Thuột, Đắk Lắk", "DN4666622153422", "2024-01-01", "2026-12-31", "66232"],
        ["26000270", "Nguyễn Thị Hường", "1996-02-07", "Nữ", "Thôn 10, Xã Nam Bình , Huyện Đắk Song, Đắk Nông", "DN4666624113158", "2024-01-01", "2026-12-31", "66232"],
        ["1127779", "Đồng Viết Thanh", "1994-06-23", "Nam", "21/1 Lý Tự Trọng, Phường Tân An, Thành phố Buôn Ma Thuột, Đắk Lắk", "DN4666621963533", "2024-01-01", "2026-12-31", "66232"],
        ["26034344", "Lưu Thành Đạt", "1992-06-14", "Nam", "Thôn Sơn Cường , Xã Buôn Triết, Huyện Lắk, Đắk Lắk", "DK2666623568069", "2024-06-04", "2027-01-03", "66232"],
        ["25032332", "Nguyễn Thị Tốt", "1988-12-15", "Nữ", "Tân Lợi, Buôn Ma Thuột, Đắk Lắk", "GD4666621303867", "2024-01-01", "2026-12-31", "66068"],
        ["25032336", "Ksor H' Trâm", "1984-05-11", "Nữ", "EaTam, Buôn Ma Thuột, Đắk Lắk", "GD4666621303864", "2024-01-01", "2026-12-31", "66068"],
        ["24047923", "Nguyễn Thị Hương", "1990-03-12", "Nữ", "Tân Lập, Buôn Ma Thuột, Đắk Lắk", "TE1666624722228", "2024-01-01", "2026-12-31", "66068"],
        ["26029578", "Nguyễn Duy Bền", "1985-08-20", "Nam", "Tự An, Buôn Ma Thuột, Đắk Lắk", "", "", "", ""],
        ["20161400", "Lê Phúc Thùy Linh", "2016-04-18", "Nữ", "Tân Tiến, Buôn Ma Thuột, Đắk Lắk", "TE1676624722370", "2024-01-01", "2026-12-31", "66068"],
        ["26029570", "Hà Thị Phàng", "1975-10-02", "Nữ", "Khánh Xuân, Buôn Ma Thuột, Đắk Lắk", "", "", "", ""],
        ["26029550", "Bùi Khắc Quý", "1968-11-22", "Nam", "Ea Kar, Đắk Lắk", "DN4666621963599", "2024-01-01", "2026-12-31", "66232"],
        ["22000008", "Bùi Minh Thiên", "1983-07-14", "Nam", "Krông Pắc, Đắk Lắk", "GD4666621303899", "2024-01-01", "2026-12-31", "66068"],
        ["19011994", "Bùi Thị Lừng", "1994-01-19", "Nữ", "Thành Nhất, Buôn Ma Thuột, Đắk Lắk", "DN4666622153400", "2024-01-01", "2026-12-31", "66232"],
        ["23027807", "Bùi Thị Thận", "1955-09-05", "Nữ", "Ea H'leo, Đắk Lắk", "", "", "", ""]
    ]
    for row in sample_data:
        ws.append(row)
        
    file_path = r"C:\Users\Admin\.gemini\antigravity\scratch\ExamGenerator\data\catalogs\patients.xlsx"
    wb.save(file_path)
    print(f"Saved patients catalog template to: {file_path}")

def create_drugs_catalog():
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.title = "Drugs_Supplies"
    
    headers = [
        "MaDuoc",
        "TenDuoc",
        "DVTTinh",
        "MaKho",
        "TenKho",
        "KhoaPhong",
        "Nguon",
        "SoLuongTon",
        "TrangThai",
        "ThoiDiemChotTon",
    ]
    ws.append(headers)
    
    sample_data = [
        ["jart1", "Jardiance 10mg", "Viên", "KHO_LE_NOITRU", "BH", 1500, 1],
        ["MEDT32", "MEDOLEB 200mg", "Viên", "KHO_LE_NOITRU", "BH", 800, 1],
        ["NEMT1", "NEXIUM MUPS 40mg", "Gói", "KHO_LE_NOITRU", "BH", 1200, 1],
        ["BoTV65", "Bơm tiêm MPV sử dụng 1 lần 20ml", "Cái", "KHO_VTYT_NOITRU", "VP", 300, 1],
        ["KiTV7", "Kim tiêm MPV", "Cái", "KHO_VTYT_NOITRU", "VP", 500, 1],
        ["ALLER1", "ALLERMINE 4mg", "Viên", "KHO_LE_NOITRU", "VP", 600, 1],
        ["BARO1", "BAROLE 20 20mg", "Viên", "KHO_LE_NOITRU", "VP", 1000, 1],
        ["BaTV2", "Băng thun 3 móc Urgoband 10cmx4,5m", "Cuộn", "KHO_VTYT_NOITRU", "VP", 250, 1],
        ["GaPV", "Gạc PT 10x10x12 lớp vô trùng KCQ", "Miếng", "KHO_VTYT_NOITRU", "VP", 2000, 1],
        ["GaTV15", "Gạc tẩm cồn 5cm x 6 cm x 4 lớp", "Miếng", "KHO_VTYT_NOITRU", "VP", 1500, 1],
        ["BoTV24", "Bơm tiêm MPV 3ml 25G x 1\"", "Cái", "KHO_VTYT_NOITRU", "VP", 800, 1],
        ["GaTV5", "Găng tay phẫu thuật tiệt trùng số 7.5", "Đôi", "KHO_VTYT_NOITRU", "VP", 1000, 1],
        ["GLU24", "GLUCOSE 5%, 5%/500ml, Chai (Fresenius Kabi VN)", "Chai", "KHO_LE_NOITRU", "BH", 400, 1],
        ["VAIT3", "VAMINOLACT, 6.5%, 100ml, Chai (Fresenius Kabi Áo)", "Chai", "KHO_LE_NOITRU", "BH", 150, 1],
        ["NuCT18", "NƯỚC CẤT TIÊM, 10ml, Ống (Dược Hải Dương)", "Ống", "KHO_LE_NOITRU", "BH", 3000, 1],
        ["BOTV50", "Bơm tiêm MPV 3ml, Cái", "Cái", "KHO_VTYT_NOITRU", "BH", 1200, 1],
        ["Rect1", "Rectiofar 3ml", "Tuýp", "KHO_LE_NOITRU", "VP", 200, 1],
        ["BaCV20", "Bao cao su An Phú", "Hộp", "KHO_VTYT_NOITRU", "VP", 50, 1],
        ["BoTV17", "Bơm tiêm 10ml MPV-VN", "Cái", "KHO_VTYT_NOITRU", "VP", 1000, 1],
        ["CoSV3", "Cồn sát trùng 70 độ", "Lít", "KHO_VTYT_NOITRU", "VP", 10, 1],
        ["GaTV26", "Găng tay hút đàm", "Đôi", "KHO_VTYT_NOITRU", "VP", 500, 1],
        ["QuXT2", "Que thử nước tiểu 10 thông số", "Que", "KHO_VTYT_XN", "VP", 400, 1],
        ["ThXT2", "Thuốc thử Glucose", "Hộp", "KHO_VTYT_XN", "VP", 15, 1],
        ["ThXT3", "Thuốc thử Creatinin", "Hộp", "KHO_VTYT_XN", "VP", 12, 1],
        ["ThXT24", "Thuốc thử CRP", "Hộp", "KHO_VTYT_XN", "VP", 8, 1],
        ["ThXT4", "Thuốc thử Triglycerid", "Hộp", "KHO_VTYT_XN", "VP", 10, 1]
    ]
    for row in sample_data:
        is_lab_stock = row[3] == "KHO_VTYT_XN"
        warehouse_name = (
            "Kho vật tư Xét nghiệm"
            if is_lab_stock
            else "Kho lẻ Nội trú"
        )
        department_name = "Khoa Xét Nghiệm" if is_lab_stock else "Khoa Nội"
        ws.append(
            row[:4]
            + [warehouse_name, department_name]
            + row[4:]
            + [""]
        )
        
    file_path = r"C:\Users\Admin\.gemini\antigravity\scratch\ExamGenerator\data\catalogs\drugs.xlsx"
    wb.save(file_path)
    print(f"Saved drugs catalog template to: {file_path}")

def create_services_catalog():
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.title = "Services"
    
    headers = ["MaDichVu", "TenDichVu", "NhomDichVu", "KhoaThucHien", "TrangThai"]
    ws.append(headers)
    
    sample_data = [
        ["SADT", "Siêu âm Doppler tim", "Siêu âm", "CDHA", 1],
        ["SAOB", "Siêu âm bụng", "Siêu âm", "CDHA", 1],
        ["SATG", "Siêu âm tuyến giáp", "Siêu âm", "CDHA", 1],
        ["SAMP", "Siêu âm màng phổi", "Siêu âm", "CDHA", 1],
        ["SADPM", "Siêu âm Doppler mạch máu", "Siêu âm", "CDHA", 1],
        ["SAQT", "Siêu âm qua thóp", "Siêu âm", "CDHA", 1],
        ["XQNT", "Chụp Xquang ngực thẳng [Số hóa 1 phim]", "X-quang", "CDHA", 1],
        ["XQCC", "Chụp Xquang xương cổ chân thẳng, nghiêng hoặc chếch [Số hóa 2 phim]", "X-quang", "CDHA", 1],
        ["CTAC", "Chụp cắt lớp vi tính động mạch chủ-chậu", "CT Scan", "CDHA", 1],
        ["ABO", "Định nhóm máu hệ ABO (Kỹ thuật phiến đá)", "Xét nghiệm", "XN", 1],
        ["RH", "Định nhóm máu hệ Rh(D) (Kỹ thuật phiến đá)", "Xét nghiệm", "XN", 1],
        ["BLT", "Tổng phân tích tế bào máu ngoại vi (bằng máy đếm laser)", "Xét nghiệm", "XN", 1],
        ["CRP", "CRP Định lượng", "Xét nghiệm", "XN", 1],
        ["DGD", "Điện giải đồ (Na, K, Cl) [Máu]", "Xét nghiệm", "XN", 1],
        ["CA", "Định lượng Calci toàn phần [Máu]", "Xét nghiệm", "XN", 1],
        ["BILGT", "Định lượng Bilirubin gián tiếp [Máu]", "Xét nghiệm", "XN", 1],
        ["BILT", "Định lượng Bilirubin toàn phần [Máu]", "Xét nghiệm", "XN", 1],
        ["BILTT", "Định lượng Bilirubin trực tiếp [Máu]", "Xét nghiệm", "XN", 1],
        ["UTEN", "Tổng phân tích nước tiểu (10 thông số) bằng máy tự động", "Xét nghiệm", "XN", 1],
        ["GLUM", "Định lượng Glucose [Máu]", "Xét nghiệm", "XN", 1],
        ["CREM", "Định lượng Creatinin [Máu]", "Xét nghiệm", "XN", 1],
        ["TRIGM", "Định lượng Triglycerid [Máu]", "Xét nghiệm", "XN", 1],
        ["NSDD", "Nội soi dạ dày tá tràng", "Nội soi", "CDHA", 1],
        ["CDVD", "Chiếu đèn điều trị vàng da sơ sinh", "Thủ thuật", "Nhi", 1],
        ["TB", "Tắm bé", "Thủ thuật", "Nhi", 1],
        ["KNOI", "Khám nội", "Khám bệnh", "KKB", 1],
        ["KNG", "Khám ngoại", "Khám bệnh", "KKB", 1],
        ["KPS", "Khám phụ sản", "Khám bệnh", "KKB", 1]
    ]
    for row in sample_data:
        ws.append(row)
        
    file_path = r"C:\Users\Admin\.gemini\antigravity\scratch\ExamGenerator\data\catalogs\services.xlsx"
    wb.save(file_path)
    print(f"Saved services catalog template to: {file_path}")

def create_users_catalog():
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.title = "Users"
    
    headers = ["TenDangNhap", "MatKhau", "HoTen", "KhoaPhong"]
    ws.append(headers)
    
    sample_data = [
        ["LYCTK", "123", "Lê Yến Chi", "Nội"],
        ["TRANNNH", "123", "Trần Nguyễn Ngọc Hân", "Nội"],
        ["MENNT", "123", "Nguyễn Thị Mến", "Sản"],
        ["TOABV", "123", "Bùi Viết Tỏa", "Cấp cứu"],
        ["LACLH", "123", "Lê Anh Châu", "CDHA"],
        ["NHIENVT", "123", "Nguyễn Hoàng Nhi", "Dược"],
        ["TRUCPTN", "123", "Phan Thị Ngọc Trúc", "Xét nghiệm"],
        ["SANGVD", "123", "Vũ Đăng Sang", "Dược"],
        ["LINHHTT", "123", "Huỳnh Thị Linh", "Lâm sàng"],
        ["NHANHTT", "123", "Nguyễn Thanh Nhân", "Dược"]
    ]
    for row in sample_data:
        ws.append(row)
        
    file_path = r"C:\Users\Admin\.gemini\antigravity\scratch\ExamGenerator\data\catalogs\users.xlsx"
    wb.save(file_path)
    print(f"Saved users catalog template to: {file_path}")

if __name__ == "__main__":
    create_folders()
    create_patients_catalog()
    create_drugs_catalog()
    create_services_catalog()
    create_users_catalog()
