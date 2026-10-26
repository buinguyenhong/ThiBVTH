using Microsoft.Data.Sqlite;
using ExamGenerator.Domain;

namespace ExamGenerator.Infrastructure;

public sealed class SqliteDatabase
{
    private readonly LocalApplicationStorage _storage;

    public SqliteDatabase(LocalApplicationStorage storage) => _storage = storage;

    public string DatabasePath => _storage.SqliteDatabasePath();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS DepartmentConfiguration (
                DepartmentCode TEXT PRIMARY KEY NOT NULL,
                DepartmentName TEXT NOT NULL,
                WarehouseCode TEXT NOT NULL,
                WarehouseName TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS DepartmentWarehouseMapping (
                DepartmentCode TEXT NOT NULL,
                DepartmentName TEXT NOT NULL,
                WarehouseCode TEXT NOT NULL,
                WarehouseName TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                PRIMARY KEY(DepartmentCode, WarehouseCode)
            );
            CREATE TABLE IF NOT EXISTS DepartmentUserMapping (
                DepartmentCode TEXT NOT NULL,
                DepartmentName TEXT NOT NULL,
                UserId TEXT NOT NULL,
                UserName TEXT NOT NULL,
                FullName TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                PRIMARY KEY(DepartmentCode, UserId)
            );
            CREATE TABLE IF NOT EXISTS ExamBatch (
                BatchId TEXT PRIMARY KEY NOT NULL,
                BatchName TEXT NOT NULL,
                ExamDate TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS GeneratedFile (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BatchId TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY(BatchId) REFERENCES ExamBatch(BatchId)
            );
            CREATE TABLE IF NOT EXISTS CatalogItem (
                CatalogType TEXT NOT NULL,
                ItemId TEXT NOT NULL,
                ItemCode TEXT NOT NULL,
                ItemName TEXT NOT NULL,
                ParentId TEXT NULL,
                ImportedAt TEXT NOT NULL,
                IsDisabledForExam INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY(CatalogType, ItemId)
            );
            CREATE TABLE IF NOT EXISTS ExamTemplate (
                TemplateId TEXT PRIMARY KEY NOT NULL,
                Name TEXT NOT NULL,
                Department TEXT NOT NULL,
                Position TEXT NOT NULL,
                QuestionCount INTEGER NOT NULL,
                TotalScore REAL NOT NULL,
                ReceptionMode TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS TemplateAction (
                TemplateId TEXT NOT NULL,
                ActionCode TEXT NOT NULL,
                ActionName TEXT NOT NULL,
                Score REAL NOT NULL,
                PRIMARY KEY(TemplateId, ActionCode),
                FOREIGN KEY(TemplateId) REFERENCES ExamTemplate(TemplateId) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS DepartmentServiceGroup (
                DepartmentCode TEXT NOT NULL,
                ServiceGroupCode TEXT NOT NULL,
                ServiceGroupName TEXT NOT NULL,
                PRIMARY KEY(DepartmentCode, ServiceGroupCode)
            );
            CREATE TABLE IF NOT EXISTS UsedPatient (
                MedicalCode TEXT PRIMARY KEY NOT NULL,
                FullName TEXT NOT NULL,
                BatchId TEXT NOT NULL,
                UsedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS SnapshotUser (
                UserId TEXT PRIMARY KEY NOT NULL,
                UserName TEXT NOT NULL,
                FullName TEXT NOT NULL,
                DepartmentCode TEXT NOT NULL,
                DepartmentName TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS SnapshotDrug (
                DrugId TEXT PRIMARY KEY NOT NULL,
                DrugCode TEXT NOT NULL,
                DrugName TEXT NOT NULL,
                Unit TEXT NOT NULL,
                WarehouseCode TEXT NOT NULL,
                WarehouseName TEXT NOT NULL,
                FundingSource TEXT NOT NULL,
                QuantityOnHand REAL NOT NULL,
                DepartmentCode TEXT NULL,
                DepartmentName TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS SnapshotPatient (
                PatientId TEXT PRIMARY KEY NOT NULL,
                MedicalCode TEXT NOT NULL,
                FullName TEXT NOT NULL,
                DateOfBirth TEXT NOT NULL,
                Gender TEXT NOT NULL,
                Address TEXT NOT NULL,
                InsuranceNumber TEXT NULL,
                Diagnosis TEXT NOT NULL,
                PaymentType TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ActionDefinition (
                ActionCode TEXT PRIMARY KEY NOT NULL,
                ActionName TEXT NOT NULL,
                DefaultScore REAL NOT NULL,
                Category TEXT NOT NULL,
                Description TEXT NOT NULL,
                IsBuiltIn INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS SnapshotInpatientCase (
                BenhAnId TEXT PRIMARY KEY NOT NULL,
                SoBenhAn TEXT NOT NULL,
                MedicalCode TEXT NOT NULL,
                FullName TEXT NOT NULL,
                Gender TEXT NOT NULL,
                DateOfBirth TEXT NOT NULL,
                Address TEXT NULL,
                DepartmentCode TEXT NOT NULL,
                DepartmentName TEXT NOT NULL,
                RoomCode TEXT NULL,
                RoomName TEXT NULL,
                BedCode TEXT NULL,
                Floor TEXT NULL,
                AdmissionDate TEXT NOT NULL,
                AdmissionTime TEXT NULL,
                BedInTime TEXT NULL,
                DepartmentInDate TEXT NULL,
                DaysInHospital INTEGER NOT NULL,
                InsuranceNumber TEXT NULL,
                Diagnosis TEXT NULL,
                DoctorName TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Inpatient_Dept ON SnapshotInpatientCase(DepartmentCode, DepartmentName);

            CREATE TABLE IF NOT EXISTS SnapshotOutpatientCase (
                BenhAnNgoaiTruId TEXT PRIMARY KEY NOT NULL,
                SoBenhAn TEXT NOT NULL,
                SoLuuTru TEXT NULL,
                MedicalCode TEXT NOT NULL,
                FullName TEXT NOT NULL,
                Gender TEXT NOT NULL,
                DateOfBirth TEXT NOT NULL,
                Address TEXT NULL,
                DepartmentCode TEXT NOT NULL,
                DepartmentName TEXT NOT NULL,
                RoomCode TEXT NULL,
                RoomName TEXT NULL,
                BedCode TEXT NULL,
                TreatmentFromDate TEXT NULL,
                TreatmentToDate TEXT NULL,
                AdmissionDate TEXT NOT NULL,
                DaysInTreatment INTEGER NOT NULL,
                InsuranceNumber TEXT NULL,
                Diagnosis TEXT NULL,
                DoctorName TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Outpatient_Dept ON SnapshotOutpatientCase(DepartmentCode, DepartmentName);

            CREATE TABLE IF NOT EXISTS SnapshotInventoryPeriod (
                PeriodCode TEXT NOT NULL,
                PeriodName TEXT NOT NULL,
                FromDate TEXT NOT NULL,
                ToDate TEXT NOT NULL,
                WarehouseCode TEXT NOT NULL,
                WarehouseName TEXT NOT NULL,
                DepartmentCode TEXT NOT NULL,
                DepartmentName TEXT NOT NULL,
                DrugCode TEXT NOT NULL,
                DrugName TEXT NOT NULL,
                Unit TEXT NOT NULL,
                ClosingStock REAL NOT NULL,
                FundingSource TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_InvPeriod_Dept_Wh ON SnapshotInventoryPeriod(DepartmentCode, WarehouseCode);

            CREATE TABLE IF NOT EXISTS CandidateGrading (
                SubmissionId TEXT PRIMARY KEY NOT NULL,
                CandidateId TEXT NOT NULL,
                CandidateName TEXT NOT NULL,
                PaperCode TEXT NOT NULL,
                DepartmentName TEXT NOT NULL,
                HisUserCode TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                SubmittedAt TEXT NOT NULL,
                TotalExtraMinutes INTEGER NOT NULL,
                TotalScore REAL NOT NULL,
                MaxTotalScore REAL NOT NULL,
                ExaminerName TEXT NOT NULL,
                GeneralRemark TEXT NOT NULL,
                GradedAt TEXT NOT NULL,
                ItemsJson TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CandidateGrading_Cand ON CandidateGrading(CandidateId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Safe migrations for newly added columns
        try
        {
            var alter1 = connection.CreateCommand();
            alter1.CommandText = "ALTER TABLE SnapshotDrug ADD COLUMN DepartmentCode TEXT NULL";
            await alter1.ExecuteNonQueryAsync(cancellationToken);
        }
        catch { /* column may already exist */ }

        try
        {
            var alter2 = connection.CreateCommand();
            alter2.CommandText = "ALTER TABLE SnapshotDrug ADD COLUMN DepartmentName TEXT NULL";
            await alter2.ExecuteNonQueryAsync(cancellationToken);
        }
        catch { /* column may already exist */ }

        try
        {
            var alter3 = connection.CreateCommand();
            alter3.CommandText = "ALTER TABLE CatalogItem ADD COLUMN IsDisabledForExam INTEGER NOT NULL DEFAULT 0";
            await alter3.ExecuteNonQueryAsync(cancellationToken);
        }
        catch { /* column may already exist */ }

        // Tự động bảo lưu và di chuyển mapping kho cũ sang bảng đa kho DepartmentWarehouseMapping
        try
        {
            var migrateWh = connection.CreateCommand();
            migrateWh.CommandText = """
                INSERT OR IGNORE INTO DepartmentWarehouseMapping (DepartmentCode, DepartmentName, WarehouseCode, WarehouseName, UpdatedAt)
                SELECT DepartmentCode, DepartmentName, WarehouseCode, WarehouseName, UpdatedAt FROM DepartmentConfiguration;
            """;
            await migrateWh.ExecuteNonQueryAsync(cancellationToken);
        }
        catch { }

        // Seed default action catalog items if not present or refresh built-in definitions
        foreach (var def in SystemActionCatalog.DefaultActions)
        {
            try
            {
                var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = """
                    INSERT INTO ActionDefinition (ActionCode, ActionName, DefaultScore, Category, Description, IsBuiltIn, CreatedAt)
                    VALUES (@code, @name, @score, @cat, @desc, 1, @created)
                    ON CONFLICT(ActionCode) DO UPDATE SET
                        ActionName = excluded.ActionName,
                        Category = excluded.Category,
                        Description = excluded.Description,
                        IsBuiltIn = 1
                    WHERE ActionDefinition.IsBuiltIn = 1;
                """;
                insertCmd.Parameters.AddWithValue("@code", def.Code);
                insertCmd.Parameters.AddWithValue("@name", def.Name);
                insertCmd.Parameters.AddWithValue("@score", def.DefaultScore);
                insertCmd.Parameters.AddWithValue("@cat", def.Category);
                insertCmd.Parameters.AddWithValue("@desc", def.Description);
                insertCmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("O"));
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch { }
        }
    }

    /// <summary>
    /// Chỉ khởi tạo khung cấu trúc mẫu đề (ExamTemplate & TemplateAction) theo họ đề chuẩn.
    /// TUYỆT ĐỐI KHÔNG nạp dữ liệu danh mục giả/mẫu (Bệnh nhân, Thuốc, User, Dịch vụ).
    /// Toàn bộ danh mục chỉ được tạo ra và cập nhật khi người dùng đồng bộ từ SQL Server HIS.
    /// </summary>
    public async Task EnsureDefaultTemplatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);

        // 1. Standard Exam Templates structure
        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO ExamTemplate (TemplateId, Name, Department, Position, QuestionCount, TotalScore, ReceptionMode, CreatedAt) VALUES " +
            "('tpl-noi','Đề Điều dưỡng nội trú chuẩn (8 câu)','Khoa Nội','Điều dưỡng',8,10,'WardAdmissionPreparation',$at), " +
            "('tpl-ngoai','Đề Điều dưỡng Ngoại tổng hợp','Khoa Ngoại tổng hợp','Điều dưỡng',8,10,'WardAdmissionPreparation',$at), " +
            "('tpl-ctch','Đề Điều dưỡng Chấn thương chỉnh hình','Khoa Chấn thương chỉnh hình','Điều dưỡng',8,10,'WardAdmissionPreparation',$at), " +
            "('tpl-hs-tc','Đề Điều dưỡng Hồi sức tích cực','Khoa Hồi sức tích cực','Điều dưỡng',8,10,'WardAdmissionPreparation',$at), " +
            "('tpl-san','Đề Nữ hộ sinh / Điều dưỡng Sản','Khoa Phụ Sản','Điều dưỡng',7,10,'DirectReception',$at), " +
            "('tpl-nhi','Đề Điều dưỡng Nhi (Tiếp nhận trực tiếp)','Khoa Nhi','Điều dưỡng',7,10,'DirectReception',$at), " +
            "('tpl-cc','Đề Điều dưỡng Cấp cứu','Khoa Cấp cứu','Điều dưỡng',6,10,'DirectReception',$at), " +
            "('tpl-lt-noitru','Đề Lễ tân Khoa Nội trú','Khoa Ngoại tổng hợp','Lễ tân',4,10,'WardAdmissionPreparation',$at), " +
            "('tpl-kkb','Đề Lễ tân phòng khám / Tiếp đón','Khoa Khám bệnh','Lễ tân',5,10,'DirectReception',$at), " +
            "('tpl-ktv-xn','Đề Kỹ thuật viên Xét nghiệm','Khoa Xét Nghiệm','Kỹ thuật viên',2,10,'WardAdmissionPreparation',$at), " +
            "('tpl-ktv-cdha','Đề Kỹ thuật viên Chẩn đoán hình ảnh','Khoa Chẩn đoán hình ảnh','Kỹ thuật viên',2,10,'WardAdmissionPreparation',$at), " +
            "('tpl-thu-ngan','Đề Thu ngân Quầy viện phí','Phòng Tài chính kế toán','Thu ngân',3,10,'DirectReception',$at), " +
            "('tpl-lt-thuphi','Đề Lễ tân - Thu phí phòng khám (7 câu)','Khoa Khám bệnh','Lễ tân',7,10,'DirectReception',$at), " +
            "('tpl-ktv-phcn','Đề Kỹ thuật viên Phục hồi chức năng (4 câu)','Khoa Phục hồi chức năng','Kỹ thuật viên',4,10,'DirectReception',$at), " +
            "('tpl-ktv-gmhs','Đề Kỹ thuật viên Gây mê hồi sức (2 câu)','Khoa Gây mê hồi sức','Kỹ thuật viên',2,10,'WardAdmissionPreparation',$at), " +
            "('tpl-dd-tnt','Đề Điều dưỡng Thận nhân tạo (5 câu)','Khoa Thận nhân tạo','Điều dưỡng',5,10,'DirectReception',$at)";
        command.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        // 2. Standard template actions
        var actions = connection.CreateCommand();
        actions.CommandText = """
            INSERT OR IGNORE INTO TemplateAction (TemplateId, ActionCode, ActionName, Score) VALUES
            ('tpl-noi','NT_NHAN_BENH_KHOA','Nhận bệnh vào khoa',1),
            ('tpl-noi','YL_CHI_DINH_CLS','Chỉ định CLS',1),
            ('tpl-noi','YL_CHI_DINH_THUOC_VTYT','Y lệnh thuốc & VTYT',3),
            ('tpl-noi','YL_TRA_THUOC','Trả thuốc thừa',1),
            ('tpl-noi','YL_DOI_THEM_DICH_VU','Đổi/thêm dịch vụ',1),
            ('tpl-noi','TK_KIEM_TON_KHO','Kiểm tra tồn kho',1),
            ('tpl-noi','CK_CHUYEN_KHOA','Chuyển khoa điều trị',1),
            ('tpl-noi','RV_CHO_RA_VIEN','Cho ra viện',1),
            ('tpl-ngoai','NT_NHAN_BENH_KHOA','Nhận bệnh vào khoa',1),
            ('tpl-ngoai','YL_CHI_DINH_CLS','Chỉ định CLS',1),
            ('tpl-ngoai','YL_CHI_DINH_THUOC_VTYT','Y lệnh thuốc & VTYT',3),
            ('tpl-ngoai','YL_TRA_THUOC','Trả thuốc thừa',1),
            ('tpl-ngoai','YL_DOI_THEM_DICH_VU','Đổi/thêm dịch vụ',1),
            ('tpl-ngoai','TK_KIEM_TON_KHO','Kiểm tra tồn kho',1),
            ('tpl-ngoai','CK_CHUYEN_KHOA','Chuyển khoa điều trị',1),
            ('tpl-ngoai','RV_CHO_RA_VIEN','Cho ra viện',1),
            ('tpl-ctch','NT_NHAN_BENH_KHOA','Nhận bệnh vào khoa',1),
            ('tpl-ctch','YL_CHI_DINH_CLS','Chỉ định CLS',1),
            ('tpl-ctch','YL_CHI_DINH_THUOC_VTYT','Y lệnh thuốc & VTYT',3),
            ('tpl-ctch','YL_TRA_THUOC','Trả thuốc thừa',1),
            ('tpl-ctch','YL_DOI_THEM_DICH_VU','Đổi/thêm dịch vụ',1),
            ('tpl-ctch','TK_KIEM_TON_KHO','Kiểm tra tồn kho',1),
            ('tpl-ctch','CK_CHUYEN_KHOA','Chuyển khoa điều trị',1),
            ('tpl-ctch','RV_CHO_RA_VIEN','Cho ra viện',1),
            ('tpl-hs-tc','NT_NHAN_BENH_KHOA','Nhận bệnh vào khoa',1),
            ('tpl-hs-tc','YL_CHI_DINH_CLS','Chỉ định CLS',1),
            ('tpl-hs-tc','YL_CHI_DINH_THUOC_VTYT','Y lệnh thuốc & VTYT',3),
            ('tpl-hs-tc','YL_TRA_THUOC','Trả thuốc thừa',1),
            ('tpl-hs-tc','YL_DOI_THEM_DICH_VU','Đổi/thêm dịch vụ',1),
            ('tpl-hs-tc','TK_KIEM_TON_KHO','Kiểm tra tồn kho',1),
            ('tpl-hs-tc','CK_CHUYEN_KHOA','Chuyển khoa điều trị',1),
            ('tpl-hs-tc','RV_CHO_RA_VIEN','Cho ra viện',1),
            ('tpl-san','TN_TIEP_NHAN','Tiếp nhận trực tiếp',2),
            ('tpl-san','YL_CHI_DINH_CLS','Chỉ định CLS',1),
            ('tpl-san','YL_CHI_DINH_THUOC_VTYT','Y lệnh thuốc & VTYT',3),
            ('tpl-san','YL_TRA_THUOC','Trả thuốc thừa',1),
            ('tpl-san','YL_DOI_THEM_DICH_VU','Đổi/thêm dịch vụ',1),
            ('tpl-san','TK_KIEM_TON_KHO','Kiểm tra tồn kho',1),
            ('tpl-san','RV_CHO_RA_VIEN','Cho ra viện',1),
            ('tpl-nhi','TN_TIEP_NHAN','Tiếp nhận trực tiếp',2),
            ('tpl-nhi','YL_CHI_DINH_CLS','Chỉ định CLS',1),
            ('tpl-nhi','YL_CHI_DINH_THUOC_VTYT','Y lệnh thuốc & VTYT',3),
            ('tpl-nhi','YL_TRA_THUOC','Trả thuốc thừa',1),
            ('tpl-nhi','YL_DOI_THEM_DICH_VU','Đổi/thêm dịch vụ',1),
            ('tpl-nhi','TK_KIEM_TON_KHO','Kiểm tra tồn kho',1),
            ('tpl-nhi','RV_CHO_RA_VIEN','Cho ra viện',1),
            ('tpl-cc','TN_TIEP_NHAN','Tiếp nhận cấp cứu',2),
            ('tpl-cc','YL_CHI_DINH_CLS','Chỉ định CLS khẩn',2),
            ('tpl-cc','YL_CHI_DINH_THUOC_VTYT','Y lệnh thuốc cấp cứu & VTYT',3),
            ('tpl-cc','TK_KIEM_TON_KHO','Kiểm tra cơ số tủ trực',1),
            ('tpl-cc','CK_CHUYEN_KHOA','Chuyển khoa điều trị',2),
            ('tpl-lt-noitru','NT_NHAN_BENH_KHOA','Nhận bệnh vào khoa',3),
            ('tpl-lt-noitru','TC_THU_TAM_UNG','Thu tạm ứng',4),
            ('tpl-lt-noitru','TK_KIEM_TON_KHO','Kiểm tra tồn kho',1),
            ('tpl-lt-noitru','RV_CHO_RA_VIEN','Cho ra viện',2),
            ('tpl-kkb','TN_TIEP_NHAN','Tiếp nhận trực tiếp',2),
            ('tpl-kkb','TC_THU_TAM_UNG','Thu tạm ứng',2),
            ('tpl-kkb','TC_THANH_TOAN_RA_VIEN','Thanh toán viện phí',2),
            ('tpl-kkb','TC_TRA_CUU_BENH_SU','Tra cứu lịch sử KCB',2),
            ('tpl-kkb','YL_CHI_DINH_CLS','Chỉ định CLS ban đầu',2),
            ('tpl-ktv-xn','KQ_TRA_KET_QUA_CLS','Trả kết quả xét nghiệm huyết học/sinh hóa',7),
            ('tpl-ktv-xn','TK_KIEM_TON_KHO','Kiểm tra tồn hóa chất/vật tư',3),
            ('tpl-ktv-cdha','KQ_TRA_KET_QUA_CLS','Trả kết quả chẩn đoán hình ảnh',7),
            ('tpl-ktv-cdha','TK_KIEM_TON_KHO','Kiểm tra tồn phim/vật tư',3),
            ('tpl-thu-ngan','TC_THU_TAM_UNG','Thu tạm ứng viện phí',4),
            ('tpl-thu-ngan','TC_THANH_TOAN_RA_VIEN','Thanh toán ra viện',4),
            ('tpl-thu-ngan','TK_KIEM_TON_KHO','Kiểm tra biên lai/ấn chỉ',2),
            ('tpl-lt-thuphi','LT_BAN_PHIEU_KHAM','Tiếp nhận & bán phiếu khám',1),
            ('tpl-lt-thuphi','LT_THU_PHI_VA_CLS','Thu phí khám & tạo/thu phí CLS',2),
            ('tpl-lt-thuphi','LT_THU_TAM_UNG_NOI_TRU','Thu tạm ứng nội trú',2),
            ('tpl-lt-thuphi','LT_DOI_DICH_VU_CLS','Hủy và đổi dịch vụ CLS',1),
            ('tpl-lt-thuphi','LT_TRA_CUU_THONG_TIN','Tra cứu buồng giường & lịch sử KCB',2),
            ('tpl-lt-thuphi','LT_THANH_TOAN_RA_VIEN','Thu thanh toán ra viện',1),
            ('tpl-lt-thuphi','LT_TAO_THE_TAM_BHYT','Tạo mã thẻ BHYT tạm trẻ em',1),
            ('tpl-ktv-phcn','KB_BENH_AN_NGOAI_TRU','Nhận bệnh vào khoa - tạo Bệnh án ngoại trú, nhập nội dung bệnh án',3),
            ('tpl-ktv-phcn','YL_CHI_DINH_CLS','Chỉ định các dịch vụ CLS, thủ thuật, phẫu thuật',2),
            ('tpl-ktv-phcn','TT_TUONG_TRINH_PTTT','Nhập nội dung tường trình PT-TT cho các dịch vụ đã chỉ định',4),
            ('tpl-ktv-phcn','TK_KIEM_TON_KHO','Báo số tồn kho dược cơ số (Nguồn Viện phí)',1),
            ('tpl-ktv-gmhs','YL_CHI_DINH_THUOC_VTYT','Thực hiện thao tác lên các loại Dược – VTYT và chu trình Lĩnh - Nhập - Xuất',8),
            ('tpl-ktv-gmhs','TK_KIEM_TON_KHO','Báo số tồn kho dược trong kho Gây mê (Nguồn viện phí)',2),
            ('tpl-dd-tnt','KB_BENH_AN_NGOAI_TRU','Nhận bệnh vào khoa - tạo Bệnh án ngoại trú, nhập nội dung bệnh án',2),
            ('tpl-dd-tnt','YL_CHI_DINH_CLS','Chỉ định các dịch vụ CLS, thủ thuật, phẫu thuật',1),
            ('tpl-dd-tnt','TT_TUONG_TRINH_PTTT','Nhập nội dung tường trình PT-TT và Chu trình Dược',4),
            ('tpl-dd-tnt','TK_KIEM_TON_KHO','Báo số tồn kho của một loại dược trong kho cơ số (Nguồn Viện phí)',1),
            ('tpl-dd-tnt','HC_TRA_CUU_THANH_TOAN','Kiểm tra viện phí của bệnh nhân đang nằm viện',2);
            """;
        await actions.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(string Name, double Score)>> GetTemplateActionsAsync(string templateId, CancellationToken cancellationToken = default)
    {
        var items = new List<(string, double)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT ActionName, Score FROM TemplateAction WHERE TemplateId = $id ORDER BY rowid";
        command.Parameters.AddWithValue("$id", templateId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add((reader.GetString(0), reader.GetDouble(1)));
        return items;
    }

    public async Task<IReadOnlyList<(string Code, string Name, double Score)>> GetTemplateActionDetailsAsync(string templateId, CancellationToken cancellationToken = default)
    {
        var items = new List<(string, string, double)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT ActionCode, ActionName, Score FROM TemplateAction WHERE TemplateId = $id ORDER BY rowid";
        command.Parameters.AddWithValue("$id", templateId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add((reader.GetString(0), reader.GetString(1), reader.GetDouble(2)));
        return items;
    }

    public async Task ReplaceDepartmentServiceGroupsAsync(string departmentCode, IReadOnlyList<(string Code, string Name)> groups, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM DepartmentServiceGroup WHERE DepartmentCode = $department";
        clear.Parameters.AddWithValue("$department", departmentCode);
        await clear.ExecuteNonQueryAsync(cancellationToken);
        foreach (var group in groups)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO DepartmentServiceGroup (DepartmentCode, ServiceGroupCode, ServiceGroupName) VALUES ($department, $code, $name)";
            insert.Parameters.AddWithValue("$department", departmentCode);
            insert.Parameters.AddWithValue("$code", group.Code);
            insert.Parameters.AddWithValue("$name", group.Name);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetDepartmentServiceGroupCodesAsync(string departmentCode, CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT ServiceGroupCode FROM DepartmentServiceGroup WHERE DepartmentCode = $department";
        command.Parameters.AddWithValue("$department", departmentCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task<IReadOnlyList<(string Id, string Name, string Department, string Position, int QuestionCount, double TotalScore, string ReceptionMode)>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<(string, string, string, string, int, double, string)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT TemplateId, Name, Department, Position, QuestionCount, TotalScore, ReceptionMode FROM ExamTemplate ORDER BY Department, Name";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetDouble(5), reader.GetString(6)));
        return items;
    }

    public async Task SaveTemplateAsync(string id, string name, string department, string position, string receptionMode, IReadOnlyList<(string Code, string Name, double Score)> actions, CancellationToken cancellationToken = default)
    {
        var total = actions.Sum(x => x.Score);
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var template = connection.CreateCommand();
        template.Transaction = transaction;
        template.CommandText = "INSERT INTO ExamTemplate (TemplateId,Name,Department,Position,QuestionCount,TotalScore,ReceptionMode,CreatedAt) VALUES ($id,$name,$department,$position,$count,$total,$mode,$at) ON CONFLICT(TemplateId) DO UPDATE SET Name=excluded.Name,Department=excluded.Department,Position=excluded.Position,QuestionCount=excluded.QuestionCount,TotalScore=excluded.TotalScore,ReceptionMode=excluded.ReceptionMode";
        template.Parameters.AddWithValue("$id", id);
        template.Parameters.AddWithValue("$name", name);
        template.Parameters.AddWithValue("$department", department);
        template.Parameters.AddWithValue("$position", position);
        template.Parameters.AddWithValue("$count", actions.Count);
        template.Parameters.AddWithValue("$total", total);
        template.Parameters.AddWithValue("$mode", receptionMode);
        template.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
        await template.ExecuteNonQueryAsync(cancellationToken);

        var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM TemplateAction WHERE TemplateId=$id";
        clear.Parameters.AddWithValue("$id", id);
        await clear.ExecuteNonQueryAsync(cancellationToken);

        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var counter = 2;
        foreach (var action in actions)
        {
            var code = action.Code;
            if (seenCodes.Contains(code))
            {
                code = $"{code}_{counter++}";
            }
            seenCodes.Add(code);

            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO TemplateAction (TemplateId,ActionCode,ActionName,Score) VALUES ($id,$code,$name,$score)";
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$code", code);
            insert.Parameters.AddWithValue("$name", action.Name);
            insert.Parameters.AddWithValue("$score", action.Score);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var actions = connection.CreateCommand();
        actions.CommandText = "DELETE FROM TemplateAction WHERE TemplateId=$id";
        actions.Parameters.AddWithValue("$id", id);
        await actions.ExecuteNonQueryAsync(cancellationToken);
        var template = connection.CreateCommand();
        template.CommandText = "DELETE FROM ExamTemplate WHERE TemplateId=$id";
        template.Parameters.AddWithValue("$id", id);
        await template.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SystemActionDefinition>> GetActionDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT ActionCode, ActionName, DefaultScore, Category, Description, IsBuiltIn FROM ActionDefinition ORDER BY IsBuiltIn DESC, ActionCode ASC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<SystemActionDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SystemActionDefinition(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDouble(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5) == 1
            ));
        }
        return list;
    }

    public async Task SaveActionDefinitionAsync(SystemActionDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Code)) throw new ArgumentException("Mã dạng câu hỏi không được để trống.", nameof(definition));
        if (string.IsNullOrWhiteSpace(definition.Name)) throw new ArgumentException("Tên dạng câu hỏi không được để trống.", nameof(definition));

        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ActionDefinition (ActionCode, ActionName, DefaultScore, Category, Description, IsBuiltIn, CreatedAt)
            VALUES (@code, @name, @score, @cat, @desc, @builtIn, @created)
            ON CONFLICT(ActionCode) DO UPDATE SET
                ActionName = excluded.ActionName,
                DefaultScore = excluded.DefaultScore,
                Category = excluded.Category,
                Description = excluded.Description;
        """;
        command.Parameters.AddWithValue("@code", definition.Code.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@name", definition.Name.Trim());
        command.Parameters.AddWithValue("@score", definition.DefaultScore);
        command.Parameters.AddWithValue("@cat", string.IsNullOrWhiteSpace(definition.Category) ? "Nghiệp vụ chung" : definition.Category.Trim());
        command.Parameters.AddWithValue("@desc", definition.Description ?? string.Empty);
        command.Parameters.AddWithValue("@builtIn", definition.IsBuiltIn ? 1 : 0);
        command.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteActionDefinitionAsync(string actionCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actionCode)) return false;

        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        // Bảo vệ dạng câu hỏi mặc định của hệ thống
        command.CommandText = "DELETE FROM ActionDefinition WHERE ActionCode = @code AND IsBuiltIn = 0";
        command.Parameters.AddWithValue("@code", actionCode.Trim());
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<IReadOnlyList<(string BatchName, string ExamDate, string FilePath, string CreatedAt)>> GetGeneratedFilesAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<(string, string, string, string)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(b.BatchName, 'Đợt thi'), COALESCE(b.ExamDate, ''), f.FilePath, f.CreatedAt FROM GeneratedFile f LEFT JOIN ExamBatch b ON b.BatchId = f.BatchId ORDER BY f.Id DESC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return items;
    }

    public async Task ReplaceCatalogAsync(string catalogType, IReadOnlyList<(string Id, string Code, string Name, string? ParentId)> items, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM CatalogItem WHERE CatalogType = $type";
        clear.Parameters.AddWithValue("$type", catalogType);
        await clear.ExecuteNonQueryAsync(cancellationToken);
        foreach (var item in items)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR REPLACE INTO CatalogItem (CatalogType, ItemId, ItemCode, ItemName, ParentId, ImportedAt) VALUES ($type, $id, $code, $name, $parent, $at)";
            insert.Parameters.AddWithValue("$type", catalogType);
            insert.Parameters.AddWithValue("$id", item.Id);
            insert.Parameters.AddWithValue("$code", item.Code);
            insert.Parameters.AddWithValue("$name", item.Name);
            insert.Parameters.AddWithValue("$parent", (object?)item.ParentId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReplaceSnapshotUsersAsync(IReadOnlyList<SnapshotUserRow> users, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM SnapshotUser";
        await clear.ExecuteNonQueryAsync(cancellationToken);

        foreach (var u in users)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR REPLACE INTO SnapshotUser (UserId, UserName, FullName, DepartmentCode, DepartmentName) VALUES ($id, $userName, $fullName, $deptCode, $deptName)";
            insert.Parameters.AddWithValue("$id", u.UserId);
            insert.Parameters.AddWithValue("$userName", u.UserName);
            insert.Parameters.AddWithValue("$fullName", u.FullName);
            insert.Parameters.AddWithValue("$deptCode", u.DepartmentCode);
            insert.Parameters.AddWithValue("$deptName", u.DepartmentName);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReplaceSnapshotDrugsAsync(IReadOnlyList<SnapshotDrugRow> drugs, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM SnapshotDrug";
        await clear.ExecuteNonQueryAsync(cancellationToken);

        foreach (var d in drugs)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR REPLACE INTO SnapshotDrug (DrugId, DrugCode, DrugName, Unit, WarehouseCode, WarehouseName, FundingSource, QuantityOnHand, DepartmentCode, DepartmentName) VALUES ($id, $code, $name, $unit, $whCode, $whName, $src, $qty, $deptCode, $deptName)";
            insert.Parameters.AddWithValue("$id", d.DrugId);
            insert.Parameters.AddWithValue("$code", d.DrugCode);
            insert.Parameters.AddWithValue("$name", d.DrugName);
            insert.Parameters.AddWithValue("$unit", d.Unit);
            insert.Parameters.AddWithValue("$whCode", d.WarehouseCode);
            insert.Parameters.AddWithValue("$whName", d.WarehouseName);
            insert.Parameters.AddWithValue("$src", d.FundingSource);
            insert.Parameters.AddWithValue("$qty", d.QuantityOnHand);
            insert.Parameters.AddWithValue("$deptCode", (object?)d.DepartmentCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$deptName", (object?)d.DepartmentName ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReplaceSnapshotPatientsAsync(IReadOnlyList<SnapshotPatientRow> patients, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM SnapshotPatient";
        await clear.ExecuteNonQueryAsync(cancellationToken);

        foreach (var p in patients)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR REPLACE INTO SnapshotPatient (PatientId, MedicalCode, FullName, DateOfBirth, Gender, Address, InsuranceNumber, Diagnosis, PaymentType) VALUES ($id, $med, $name, $dob, $gender, $addr, $ins, $diag, $pay)";
            insert.Parameters.AddWithValue("$id", p.PatientId);
            insert.Parameters.AddWithValue("$med", p.MedicalCode);
            insert.Parameters.AddWithValue("$name", p.FullName);
            insert.Parameters.AddWithValue("$dob", p.DateOfBirth);
            insert.Parameters.AddWithValue("$gender", p.Gender);
            insert.Parameters.AddWithValue("$addr", p.Address);
            insert.Parameters.AddWithValue("$ins", (object?)p.InsuranceNumber ?? DBNull.Value);
            insert.Parameters.AddWithValue("$diag", p.Diagnosis);
            insert.Parameters.AddWithValue("$pay", p.PaymentType);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(string Code, string Name)>> GetCatalogItemsAsync(string catalogType, CancellationToken cancellationToken = default)
    {
        var items = new List<(string, string)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT ItemCode, ItemName FROM CatalogItem WHERE CatalogType = $type ORDER BY ItemName";
        command.Parameters.AddWithValue("$type", catalogType);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add((reader.GetString(0), reader.GetString(1)));
        return items;
    }

    /// <summary>
    /// Đồng bộ Khoa/Phòng theo cơ chế Cộng dồn (Additive / Non-destructive):
    /// - Chỉ thêm mới những khoa có mã hoặc ID chưa tồn tại trong SQLite.
    /// - Tuyệt đối không xóa và không sửa đổi các khoa phòng hiện hữu.
    /// - Bảo lưu toàn bộ cấu hình Mapping Kho thi, Nhóm dịch vụ và Mẫu đề thi gắn với khoa.
    /// </summary>
    public async Task<(int TotalCount, int AddedCount, int PreservedCount)> SyncDepartmentsAdditiveAsync(
        IReadOnlyList<(string Id, string Code, string Name, string? ParentId)> hisDepartments,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);

        var existingCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readCmd = connection.CreateCommand();
        readCmd.CommandText = "SELECT ItemId, ItemCode FROM CatalogItem WHERE CatalogType = 'Departments'";
        await using (var reader = await readCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                existingIds.Add(reader.GetString(0));
                existingCodes.Add(reader.GetString(1));
            }
        }

        int preservedCount = existingIds.Count;
        int addedCount = 0;

        await using var transaction = connection.BeginTransaction();
        foreach (var item in hisDepartments)
        {
            if (!existingIds.Contains(item.Id) && !existingCodes.Contains(item.Code))
            {
                var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO CatalogItem (CatalogType, ItemId, ItemCode, ItemName, ParentId, ImportedAt, IsDisabledForExam) VALUES ('Departments', $id, $code, $name, $parent, $at, 0)";
                insert.Parameters.AddWithValue("$id", item.Id);
                insert.Parameters.AddWithValue("$code", item.Code);
                insert.Parameters.AddWithValue("$name", item.Name);
                insert.Parameters.AddWithValue("$parent", (object?)item.ParentId ?? DBNull.Value);
                insert.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
                await insert.ExecuteNonQueryAsync(cancellationToken);

                existingIds.Add(item.Id);
                existingCodes.Add(item.Code);
                addedCount++;
            }
        }
        await transaction.CommitAsync(cancellationToken);

        int totalCount = preservedCount + addedCount;
        return (totalCount, addedCount, preservedCount);
    }

    /// <summary>
    /// Bật / Tắt trạng thái tham gia khảo thí của một Khoa/Phòng (thực hiện thủ công bởi người dùng).
    /// </summary>
    public async Task SetDepartmentExamStatusAsync(string departmentCode, bool isDisabled, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE CatalogItem SET IsDisabledForExam = $disabled WHERE CatalogType = 'Departments' AND (ItemCode = $code OR ItemId = $code)";
        cmd.Parameters.AddWithValue("$disabled", isDisabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$code", departmentCode);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Lấy danh sách Khoa/Phòng kèm trạng thái Tạm ngưng do người dùng thiết lập thủ công.
    /// </summary>
    public async Task<IReadOnlyList<(string Code, string Name, bool IsDisabled)>> GetDepartmentsWithStatusAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<(string, string, bool)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT ItemCode, ItemName, COALESCE(IsDisabledForExam, 0) FROM CatalogItem WHERE CatalogType = 'Departments' ORDER BY ItemName";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1));
        }
        return items;
    }

    public async Task<IReadOnlyList<(string Code, string Name, string GroupName)>> GetCatalogServicesWithGroupAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<(string, string, string)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.ItemCode, s.ItemName, COALESCE(g.ItemName, 'Cận lâm sàng') AS GroupName
            FROM CatalogItem s
            LEFT JOIN CatalogItem g ON g.CatalogType = 'ServiceGroups' AND (g.ItemId = s.ParentId OR g.ItemCode = s.ParentId)
            WHERE s.CatalogType = 'Services'
            ORDER BY s.ItemName
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return items;
    }

    public async Task<IReadOnlyList<(string CatalogType, int Count)>> GetCatalogSummaryAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<(string, int)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);

        // CatalogItem
        var command = connection.CreateCommand();
        command.CommandText = "SELECT CatalogType, COUNT(*) FROM CatalogItem GROUP BY CatalogType ORDER BY CatalogType";
        var existingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var type = reader.GetString(0);
                existingTypes.Add(type);
                result.Add((type, reader.GetInt32(1)));
            }
        }

        foreach (var standard in new[] { "Departments", "Warehouses", "ServiceGroups", "Services" })
        {
            if (!existingTypes.Contains(standard))
                result.Add((standard, 0));
        }

        // Snapshot counts
        var pCmd = connection.CreateCommand(); pCmd.CommandText = "SELECT COUNT(*) FROM SnapshotPatient";
        var pCount = Convert.ToInt32(await pCmd.ExecuteScalarAsync(cancellationToken) ?? 0);
        result.Add(("Patients", pCount));

        var dCmd = connection.CreateCommand(); dCmd.CommandText = "SELECT COUNT(*) FROM SnapshotDrug";
        var dCount = Convert.ToInt32(await dCmd.ExecuteScalarAsync(cancellationToken) ?? 0);
        result.Add(("Drugs", dCount));

        var uCmd = connection.CreateCommand(); uCmd.CommandText = "SELECT COUNT(*) FROM SnapshotUser";
        var uCount = Convert.ToInt32(await uCmd.ExecuteScalarAsync(cancellationToken) ?? 0);
        result.Add(("Users", uCount));

        var inpCmd = connection.CreateCommand(); inpCmd.CommandText = "SELECT COUNT(*) FROM SnapshotInpatientCase";
        var inpCount = Convert.ToInt32(await inpCmd.ExecuteScalarAsync(cancellationToken) ?? 0);
        result.Add(("Inpatients", inpCount));

        var outCmd = connection.CreateCommand(); outCmd.CommandText = "SELECT COUNT(*) FROM SnapshotOutpatientCase";
        var outCount = Convert.ToInt32(await outCmd.ExecuteScalarAsync(cancellationToken) ?? 0);
        result.Add(("Outpatients", outCount));

        var invCmd = connection.CreateCommand(); invCmd.CommandText = "SELECT COUNT(*) FROM SnapshotInventoryPeriod";
        var invCount = Convert.ToInt32(await invCmd.ExecuteScalarAsync(cancellationToken) ?? 0);
        result.Add(("InventoryPeriods", invCount));

        return result;
    }

    public async Task SaveDepartmentConfigurationAsync(
        string departmentCode,
        string departmentName,
        string warehouseCode,
        string warehouseName,
        CancellationToken cancellationToken = default)
    {
        await SaveDepartmentWarehouseMappingsAsync(
            departmentCode,
            departmentName,
            new[] { (warehouseCode, warehouseName) },
            cancellationToken);
    }

    public async Task SaveDepartmentWarehouseMappingsAsync(
        string departmentCode,
        string departmentName,
        IReadOnlyList<(string WarehouseCode, string WarehouseName)> warehouses,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        // 1. Xóa các mapping cũ của khoa trong bảng DepartmentWarehouseMapping
        var delCmd = connection.CreateCommand();
        delCmd.Transaction = transaction;
        delCmd.CommandText = "DELETE FROM DepartmentWarehouseMapping WHERE DepartmentCode = $deptCode";
        delCmd.Parameters.AddWithValue("$deptCode", departmentCode.Trim());
        await delCmd.ExecuteNonQueryAsync(cancellationToken);

        // 2. Chèn các kho mới được chọn
        var updatedAt = DateTimeOffset.Now.ToString("O");
        foreach (var wh in warehouses)
        {
            var insCmd = connection.CreateCommand();
            insCmd.Transaction = transaction;
            insCmd.CommandText = """
                INSERT INTO DepartmentWarehouseMapping (DepartmentCode, DepartmentName, WarehouseCode, WarehouseName, UpdatedAt)
                VALUES ($deptCode, $deptName, $whCode, $whName, $updatedAt);
            """;
            insCmd.Parameters.AddWithValue("$deptCode", departmentCode.Trim());
            insCmd.Parameters.AddWithValue("$deptName", departmentName.Trim());
            insCmd.Parameters.AddWithValue("$whCode", wh.WarehouseCode.Trim());
            insCmd.Parameters.AddWithValue("$whName", wh.WarehouseName.Trim());
            insCmd.Parameters.AddWithValue("$updatedAt", updatedAt);
            await insCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 3. Đồng bộ vào DepartmentConfiguration (lấy kho đầu tiên làm đại diện tương thích ngược)
        var syncLegacy = connection.CreateCommand();
        syncLegacy.Transaction = transaction;
        if (warehouses.Count > 0)
        {
            var firstWh = warehouses[0];
            syncLegacy.CommandText = """
                INSERT INTO DepartmentConfiguration (DepartmentCode, DepartmentName, WarehouseCode, WarehouseName, UpdatedAt)
                VALUES ($deptCode, $deptName, $whCode, $whName, $updatedAt)
                ON CONFLICT(DepartmentCode) DO UPDATE SET
                    DepartmentName = excluded.DepartmentName,
                    WarehouseCode = excluded.WarehouseCode,
                    WarehouseName = excluded.WarehouseName,
                    UpdatedAt = excluded.UpdatedAt;
            """;
            syncLegacy.Parameters.AddWithValue("$deptCode", departmentCode.Trim());
            syncLegacy.Parameters.AddWithValue("$deptName", departmentName.Trim());
            syncLegacy.Parameters.AddWithValue("$whCode", firstWh.WarehouseCode.Trim());
            syncLegacy.Parameters.AddWithValue("$whName", firstWh.WarehouseName.Trim());
            syncLegacy.Parameters.AddWithValue("$updatedAt", updatedAt);
        }
        else
        {
            syncLegacy.CommandText = "DELETE FROM DepartmentConfiguration WHERE DepartmentCode = $deptCode";
            syncLegacy.Parameters.AddWithValue("$deptCode", departmentCode.Trim());
        }
        await syncLegacy.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(string DepartmentCode, string DepartmentName, string WarehouseCode, string WarehouseName)>> GetDepartmentConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        return await GetDepartmentWarehouseMappingsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(string DepartmentCode, string DepartmentName, string WarehouseCode, string WarehouseName)>> GetDepartmentWarehouseMappingsAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<(string, string, string, string)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT DepartmentCode, DepartmentName, WarehouseCode, WarehouseName FROM DepartmentWarehouseMapping ORDER BY DepartmentName, WarehouseName";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return rows;
    }

    public async Task<IReadOnlyList<(string WarehouseCode, string WarehouseName)>> GetWarehousesForDepartmentAsync(string departmentCode, CancellationToken cancellationToken = default)
    {
        var rows = new List<(string, string)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT WarehouseCode, WarehouseName FROM DepartmentWarehouseMapping WHERE DepartmentCode = $deptCode ORDER BY WarehouseName";
        command.Parameters.AddWithValue("$deptCode", departmentCode.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    public async Task<IReadOnlyList<SnapshotUserRow>> GetDepartmentUsersAsync(string departmentName, CancellationToken cancellationToken = default)
    {
        var rows = new List<SnapshotUserRow>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT UserId, UserName, FullName, DepartmentCode, DepartmentName FROM SnapshotUser WHERE DepartmentName = $dept OR DepartmentCode = $dept ORDER BY UserName";
        command.Parameters.AddWithValue("$dept", departmentName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new SnapshotUserRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    public async Task SaveDepartmentUserMappingsAsync(string deptCode, string deptName, IReadOnlyList<(string UserId, string UserName, string FullName)> users, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM DepartmentUserMapping WHERE DepartmentCode = $deptCode";
        clear.Parameters.AddWithValue("$deptCode", deptCode.Trim());
        await clear.ExecuteNonQueryAsync(cancellationToken);

        var now = DateTimeOffset.Now.ToString("O");
        foreach (var u in users)
        {
            var ins = connection.CreateCommand();
            ins.Transaction = transaction;
            ins.CommandText = "INSERT INTO DepartmentUserMapping (DepartmentCode, DepartmentName, UserId, UserName, FullName, UpdatedAt) VALUES ($deptCode, $deptName, $userId, $userName, $fullName, $updatedAt)";
            ins.Parameters.AddWithValue("$deptCode", deptCode.Trim());
            ins.Parameters.AddWithValue("$deptName", deptName.Trim());
            ins.Parameters.AddWithValue("$userId", u.UserId.Trim());
            ins.Parameters.AddWithValue("$userName", u.UserName.Trim());
            ins.Parameters.AddWithValue("$fullName", u.FullName.Trim());
            ins.Parameters.AddWithValue("$updatedAt", now);
            await ins.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SnapshotUserRow>> GetDepartmentUserMappingsAsync(string deptCode, CancellationToken cancellationToken = default)
    {
        var rows = new List<SnapshotUserRow>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT UserId, UserName, FullName, DepartmentCode, DepartmentName FROM DepartmentUserMapping WHERE DepartmentCode = $deptCode ORDER BY UserName";
        command.Parameters.AddWithValue("$deptCode", deptCode.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new SnapshotUserRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    public async Task<IReadOnlyList<SnapshotUserRow>> GetMappedUsersForDepartmentAsync(string departmentCodeOrName, CancellationToken cancellationToken = default)
    {
        var rows = new List<SnapshotUserRow>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT UserId, UserName, FullName, DepartmentCode, DepartmentName FROM DepartmentUserMapping WHERE DepartmentCode = $dept OR DepartmentName = $dept ORDER BY UserName";
        command.Parameters.AddWithValue("$dept", departmentCodeOrName.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new SnapshotUserRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    public async Task<IReadOnlyList<SnapshotDrugRow>> GetWarehouseDrugsAsync(string warehouseCode, CancellationToken cancellationToken = default)
    {
        var rows = new List<SnapshotDrugRow>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT DrugId, DrugCode, DrugName, Unit, WarehouseCode, WarehouseName, FundingSource, QuantityOnHand, DepartmentCode, DepartmentName FROM SnapshotDrug WHERE WarehouseCode = $code AND QuantityOnHand > 0 ORDER BY DrugName";
        command.Parameters.AddWithValue("$code", warehouseCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new SnapshotDrugRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)
            ));
        return rows;
    }

    public async Task<IReadOnlyList<(string Code, string Name, string? DepartmentId)>> GetWarehousesWithDepartmentAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<(string, string, string?)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT ItemCode, ItemName, ParentId FROM CatalogItem WHERE CatalogType = 'Warehouses' ORDER BY ItemName";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return items;
    }

    public async Task<IReadOnlyList<SnapshotDrugRow>> SearchInventoryAsync(string? deptName, string? warehouseCode, string? source, string? keyword, CancellationToken cancellationToken = default)
    {
        var rows = new List<SnapshotDrugRow>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();

        var query = """
            SELECT 
                d.DrugId, d.DrugCode, d.DrugName, d.Unit, d.WarehouseCode, d.WarehouseName, d.FundingSource, d.QuantityOnHand,
                COALESCE(NULLIF(d.DepartmentCode, ''), cDept.ItemCode, '') AS DepartmentCode,
                COALESCE(NULLIF(d.DepartmentName, ''), cDept.ItemName, '') AS DepartmentName
            FROM SnapshotDrug d
            LEFT JOIN CatalogItem cWh ON cWh.CatalogType = 'Warehouses' AND (cWh.ItemCode = d.WarehouseCode OR cWh.ItemId = d.WarehouseCode)
            LEFT JOIN CatalogItem cDept ON cDept.CatalogType = 'Departments' AND cDept.ItemId = cWh.ParentId
            WHERE 1=1
            """;

        if (!string.IsNullOrWhiteSpace(deptName) && deptName != "Tất cả khoa" && deptName != "ALL")
        {
            query += " AND (d.DepartmentName = $dept OR d.DepartmentCode = $dept OR cDept.ItemName = $dept OR cDept.ItemCode = $dept)";
            command.Parameters.AddWithValue("$dept", deptName.Trim());
        }
        if (!string.IsNullOrWhiteSpace(warehouseCode) && warehouseCode != "Tất cả kho" && warehouseCode != "ALL" && warehouseCode != "ALL_DEPT")
        {
            query += " AND d.WarehouseCode = $wh";
            command.Parameters.AddWithValue("$wh", warehouseCode.Trim());
        }
        if (!string.IsNullOrWhiteSpace(source) && source != "Tất cả nguồn")
        {
            query += " AND d.FundingSource = $source";
            command.Parameters.AddWithValue("$source", source.Trim());
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query += " AND (d.DrugName LIKE $kw OR d.DrugCode LIKE $kw)";
            command.Parameters.AddWithValue("$kw", $"%{keyword.Trim()}%");
        }
        query += " ORDER BY DepartmentName, WarehouseName, DrugName LIMIT 250";

        command.CommandText = query;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SnapshotDrugRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)
            ));
        }
        return rows;
    }

    public async Task<IReadOnlyList<SnapshotPatientRow>> GetAvailablePatientsAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<SnapshotPatientRow>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT p.PatientId, p.MedicalCode, p.FullName, p.DateOfBirth, p.Gender, p.Address, p.InsuranceNumber, p.Diagnosis, p.PaymentType FROM SnapshotPatient p LEFT JOIN UsedPatient u ON p.MedicalCode = u.MedicalCode WHERE u.MedicalCode IS NULL ORDER BY p.MedicalCode";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new SnapshotPatientRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetString(8)));
        return rows;
    }

    public async Task MarkPatientUsedAsync(string medicalCode, string fullName, string batchId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO UsedPatient (MedicalCode, FullName, BatchId, UsedAt) VALUES ($code, $name, $batch, $at)";
        command.Parameters.AddWithValue("$code", medicalCode);
        command.Parameters.AddWithValue("$name", fullName);
        command.Parameters.AddWithValue("$batch", batchId);
        command.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveGeneratedFileAsync(string batchId, string batchName, DateOnly examDate, string path, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var batch = connection.CreateCommand();
        batch.Transaction = transaction;
        batch.CommandText = "INSERT OR IGNORE INTO ExamBatch (BatchId, BatchName, ExamDate, CreatedAt) VALUES ($id, $name, $date, $createdAt)";
        batch.Parameters.AddWithValue("$id", batchId);
        batch.Parameters.AddWithValue("$name", batchName);
        batch.Parameters.AddWithValue("$date", examDate.ToString("yyyy-MM-dd"));
        batch.Parameters.AddWithValue("$createdAt", DateTimeOffset.Now.ToString("O"));
        await batch.ExecuteNonQueryAsync(cancellationToken);
        var file = connection.CreateCommand();
        file.Transaction = transaction;
        file.CommandText = "INSERT INTO GeneratedFile (BatchId, FilePath, CreatedAt) VALUES ($batchId, $path, $createdAt)";
        file.Parameters.AddWithValue("$batchId", batchId);
        file.Parameters.AddWithValue("$path", path);
        file.Parameters.AddWithValue("$createdAt", DateTimeOffset.Now.ToString("O"));
        await file.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReplaceSnapshotInpatientsAsync(IReadOnlyList<SnapshotInpatientRow> inpatients, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM SnapshotInpatientCase";
        await clear.ExecuteNonQueryAsync(cancellationToken);

        foreach (var p in inpatients)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT OR REPLACE INTO SnapshotInpatientCase (
                    BenhAnId, SoBenhAn, MedicalCode, FullName, Gender, DateOfBirth, Address,
                    DepartmentCode, DepartmentName, RoomCode, RoomName, BedCode, Floor,
                    AdmissionDate, AdmissionTime, BedInTime, DepartmentInDate, DaysInHospital,
                    InsuranceNumber, Diagnosis, DoctorName
                ) VALUES (
                    $baId, $soBa, $med, $name, $gender, $dob, $addr,
                    $deptCode, $deptName, $roomCode, $roomName, $bedCode, $floor,
                    $admDate, $admTime, $bedTime, $deptDate, $days,
                    $ins, $diag, $doc
                )
            """;
            insert.Parameters.AddWithValue("$baId", p.BenhAnId);
            insert.Parameters.AddWithValue("$soBa", p.SoBenhAn);
            insert.Parameters.AddWithValue("$med", p.MedicalCode);
            insert.Parameters.AddWithValue("$name", p.FullName);
            insert.Parameters.AddWithValue("$gender", p.Gender);
            insert.Parameters.AddWithValue("$dob", p.DateOfBirth);
            insert.Parameters.AddWithValue("$addr", (object?)p.Address ?? DBNull.Value);
            insert.Parameters.AddWithValue("$deptCode", p.DepartmentCode);
            insert.Parameters.AddWithValue("$deptName", p.DepartmentName);
            insert.Parameters.AddWithValue("$roomCode", (object?)p.RoomCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$roomName", (object?)p.RoomName ?? DBNull.Value);
            insert.Parameters.AddWithValue("$bedCode", (object?)p.BedCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$floor", (object?)p.Floor ?? DBNull.Value);
            insert.Parameters.AddWithValue("$admDate", p.AdmissionDate);
            insert.Parameters.AddWithValue("$admTime", (object?)p.AdmissionTime ?? DBNull.Value);
            insert.Parameters.AddWithValue("$bedTime", (object?)p.BedInTime ?? DBNull.Value);
            insert.Parameters.AddWithValue("$deptDate", (object?)p.DepartmentInDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("$days", p.DaysInHospital);
            insert.Parameters.AddWithValue("$ins", (object?)p.InsuranceNumber ?? DBNull.Value);
            insert.Parameters.AddWithValue("$diag", (object?)p.Diagnosis ?? DBNull.Value);
            insert.Parameters.AddWithValue("$doc", (object?)p.DoctorName ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReplaceSnapshotOutpatientsAsync(IReadOnlyList<SnapshotOutpatientRow> outpatients, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM SnapshotOutpatientCase";
        await clear.ExecuteNonQueryAsync(cancellationToken);

        foreach (var p in outpatients)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT OR REPLACE INTO SnapshotOutpatientCase (
                    BenhAnNgoaiTruId, SoBenhAn, SoLuuTru, MedicalCode, FullName, Gender, DateOfBirth, Address,
                    DepartmentCode, DepartmentName, RoomCode, RoomName, BedCode,
                    TreatmentFromDate, TreatmentToDate, AdmissionDate, DaysInTreatment,
                    InsuranceNumber, Diagnosis, DoctorName
                ) VALUES (
                    $baId, $soBa, $soLt, $med, $name, $gender, $dob, $addr,
                    $deptCode, $deptName, $roomCode, $roomName, $bedCode,
                    $fromD, $toD, $admDate, $days,
                    $ins, $diag, $doc
                )
            """;
            insert.Parameters.AddWithValue("$baId", p.BenhAnNgoaiTruId);
            insert.Parameters.AddWithValue("$soBa", p.SoBenhAn);
            insert.Parameters.AddWithValue("$soLt", (object?)p.SoLuuTru ?? DBNull.Value);
            insert.Parameters.AddWithValue("$med", p.MedicalCode);
            insert.Parameters.AddWithValue("$name", p.FullName);
            insert.Parameters.AddWithValue("$gender", p.Gender);
            insert.Parameters.AddWithValue("$dob", p.DateOfBirth);
            insert.Parameters.AddWithValue("$addr", (object?)p.Address ?? DBNull.Value);
            insert.Parameters.AddWithValue("$deptCode", p.DepartmentCode);
            insert.Parameters.AddWithValue("$deptName", p.DepartmentName);
            insert.Parameters.AddWithValue("$roomCode", (object?)p.RoomCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$roomName", (object?)p.RoomName ?? DBNull.Value);
            insert.Parameters.AddWithValue("$bedCode", (object?)p.BedCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$fromD", (object?)p.TreatmentFromDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("$toD", (object?)p.TreatmentToDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("$admDate", p.AdmissionDate);
            insert.Parameters.AddWithValue("$days", p.DaysInTreatment);
            insert.Parameters.AddWithValue("$ins", (object?)p.InsuranceNumber ?? DBNull.Value);
            insert.Parameters.AddWithValue("$diag", (object?)p.Diagnosis ?? DBNull.Value);
            insert.Parameters.AddWithValue("$doc", (object?)p.DoctorName ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SnapshotInpatientRow>> GetAvailableInpatientsAsync(string? departmentName = null, CancellationToken cancellationToken = default)
    {
        var rows = new List<SnapshotInpatientRow>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(departmentName))
        {
            command.CommandText = """
                SELECT BenhAnId, SoBenhAn, MedicalCode, FullName, Gender, DateOfBirth, Address,
                       DepartmentCode, DepartmentName, RoomCode, RoomName, BedCode, Floor,
                       AdmissionDate, AdmissionTime, BedInTime, DepartmentInDate, DaysInHospital,
                       InsuranceNumber, Diagnosis, DoctorName
                FROM SnapshotInpatientCase
                ORDER BY AdmissionDate DESC, BenhAnId DESC
            """;
        }
        else
        {
            command.CommandText = """
                SELECT BenhAnId, SoBenhAn, MedicalCode, FullName, Gender, DateOfBirth, Address,
                       DepartmentCode, DepartmentName, RoomCode, RoomName, BedCode, Floor,
                       AdmissionDate, AdmissionTime, BedInTime, DepartmentInDate, DaysInHospital,
                       InsuranceNumber, Diagnosis, DoctorName
                FROM SnapshotInpatientCase
                WHERE DepartmentName LIKE $deptName
                ORDER BY AdmissionDate DESC, BenhAnId DESC
            """;
            command.Parameters.AddWithValue("$deptName", "%" + departmentName.Trim() + "%");
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SnapshotInpatientRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.GetInt32(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20)
            ));
        }
        return rows;
    }

    public async Task<IReadOnlyList<SnapshotOutpatientRow>> GetAvailableOutpatientsAsync(string? departmentName = null, CancellationToken cancellationToken = default)
    {
        var rows = new List<SnapshotOutpatientRow>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(departmentName))
        {
            command.CommandText = """
                SELECT BenhAnNgoaiTruId, SoBenhAn, SoLuuTru, MedicalCode, FullName, Gender, DateOfBirth, Address,
                       DepartmentCode, DepartmentName, RoomCode, RoomName, BedCode,
                       TreatmentFromDate, TreatmentToDate, AdmissionDate, DaysInTreatment,
                       InsuranceNumber, Diagnosis, DoctorName
                FROM SnapshotOutpatientCase
                ORDER BY AdmissionDate DESC, BenhAnNgoaiTruId DESC
            """;
        }
        else
        {
            command.CommandText = """
                SELECT BenhAnNgoaiTruId, SoBenhAn, SoLuuTru, MedicalCode, FullName, Gender, DateOfBirth, Address,
                       DepartmentCode, DepartmentName, RoomCode, RoomName, BedCode,
                       TreatmentFromDate, TreatmentToDate, AdmissionDate, DaysInTreatment,
                       InsuranceNumber, Diagnosis, DoctorName
                FROM SnapshotOutpatientCase
                WHERE DepartmentName LIKE $deptName
                ORDER BY AdmissionDate DESC, BenhAnNgoaiTruId DESC
            """;
            command.Parameters.AddWithValue("$deptName", "%" + departmentName.Trim() + "%");
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SnapshotOutpatientRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.GetString(15),
                reader.GetInt32(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19)
            ));
        }
        return rows;
    }

    public async Task<int> GetInpatientCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SnapshotInpatientCase";
        var val = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(val);
    }

    public async Task<int> GetOutpatientCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SnapshotOutpatientCase";
        var val = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(val);
    }

    public async Task ReplaceSnapshotInventoryPeriodsAsync(IReadOnlyList<SnapshotInventoryPeriodRow> rows, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM SnapshotInventoryPeriod";
        await clear.ExecuteNonQueryAsync(cancellationToken);

        foreach (var r in rows)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO SnapshotInventoryPeriod (
                    PeriodCode, PeriodName, FromDate, ToDate, WarehouseCode, WarehouseName,
                    DepartmentCode, DepartmentName, DrugCode, DrugName, Unit, ClosingStock, FundingSource
                ) VALUES (
                    $pCode, $pName, $from, $to, $whCode, $whName,
                    $deptCode, $deptName, $dCode, $dName, $unit, $stock, $src
                )
            """;
            insert.Parameters.AddWithValue("$pCode", r.PeriodCode);
            insert.Parameters.AddWithValue("$pName", r.PeriodName);
            insert.Parameters.AddWithValue("$from", r.FromDate);
            insert.Parameters.AddWithValue("$to", r.ToDate);
            insert.Parameters.AddWithValue("$whCode", r.WarehouseCode);
            insert.Parameters.AddWithValue("$whName", r.WarehouseName);
            insert.Parameters.AddWithValue("$deptCode", r.DepartmentCode);
            insert.Parameters.AddWithValue("$deptName", r.DepartmentName);
            insert.Parameters.AddWithValue("$dCode", r.DrugCode);
            insert.Parameters.AddWithValue("$dName", r.DrugName);
            insert.Parameters.AddWithValue("$unit", r.Unit);
            insert.Parameters.AddWithValue("$stock", r.ClosingStock);
            insert.Parameters.AddWithValue("$src", r.FundingSource);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SnapshotInventoryPeriodRow>> GetSnapshotInventoryPeriodsAsync(string? departmentCode = null, string? warehouseCode = null, CancellationToken cancellationToken = default)
    {
        var rows = new List<SnapshotInventoryPeriodRow>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();

        if (string.IsNullOrWhiteSpace(departmentCode) && string.IsNullOrWhiteSpace(warehouseCode))
        {
            command.CommandText = """
                SELECT PeriodCode, PeriodName, FromDate, ToDate, WarehouseCode, WarehouseName,
                       DepartmentCode, DepartmentName, DrugCode, DrugName, Unit, ClosingStock, FundingSource
                FROM SnapshotInventoryPeriod
                ORDER BY PeriodCode, WarehouseName, DrugName
            """;
        }
        else
        {
            command.CommandText = """
                SELECT PeriodCode, PeriodName, FromDate, ToDate, WarehouseCode, WarehouseName,
                       DepartmentCode, DepartmentName, DrugCode, DrugName, Unit, ClosingStock, FundingSource
                FROM SnapshotInventoryPeriod
                WHERE ($deptCode IS NULL OR DepartmentCode = $deptCode)
                  AND ($whCode IS NULL OR WarehouseCode = $whCode)
                ORDER BY PeriodCode, WarehouseName, DrugName
            """;
            command.Parameters.AddWithValue("$deptCode", (object?)departmentCode ?? DBNull.Value);
            command.Parameters.AddWithValue("$whCode", (object?)warehouseCode ?? DBNull.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SnapshotInventoryPeriodRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetDouble(11),
                reader.GetString(12)
            ));
        }
        return rows;
    }

    public async Task<int> GetInventoryPeriodsCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SnapshotInventoryPeriod";
        var val = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(val);
    }

    public async Task SaveCandidateGradingAsync(CandidateGradingRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO CandidateGrading (
                SubmissionId, CandidateId, CandidateName, PaperCode, DepartmentName,
                HisUserCode, StartedAt, SubmittedAt, TotalExtraMinutes, TotalScore,
                MaxTotalScore, ExaminerName, GeneralRemark, GradedAt, ItemsJson
            ) VALUES (
                @subId, @candId, @candName, @paperCode, @deptName,
                @hisUser, @startedAt, @subAt, @extraMin, @totalScore,
                @maxTotal, @examiner, @remark, @gradedAt, @itemsJson
            )
            ON CONFLICT(SubmissionId) DO UPDATE SET
                TotalScore = excluded.TotalScore,
                ExaminerName = excluded.ExaminerName,
                GeneralRemark = excluded.GeneralRemark,
                GradedAt = excluded.GradedAt,
                ItemsJson = excluded.ItemsJson";

        command.Parameters.AddWithValue("@subId", record.SubmissionId);
        command.Parameters.AddWithValue("@candId", record.CandidateId);
        command.Parameters.AddWithValue("@candName", record.CandidateName);
        command.Parameters.AddWithValue("@paperCode", record.PaperCode);
        command.Parameters.AddWithValue("@deptName", record.DepartmentName);
        command.Parameters.AddWithValue("@hisUser", record.HisUserCode);
        command.Parameters.AddWithValue("@startedAt", record.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("@subAt", record.SubmittedAt.ToString("O"));
        command.Parameters.AddWithValue("@extraMin", record.TotalExtraMinutes);
        command.Parameters.AddWithValue("@totalScore", record.TotalScore);
        command.Parameters.AddWithValue("@maxTotal", record.MaxTotalScore);
        command.Parameters.AddWithValue("@examiner", record.ExaminerName ?? string.Empty);
        command.Parameters.AddWithValue("@remark", record.GeneralRemark ?? string.Empty);
        command.Parameters.AddWithValue("@gradedAt", (record.GradedAt ?? DateTime.Now).ToString("O"));
        command.Parameters.AddWithValue("@itemsJson", System.Text.Json.JsonSerializer.Serialize(record.Items));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CandidateGradingRecord?> GetCandidateGradingAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT SubmissionId, CandidateId, CandidateName, PaperCode, DepartmentName, HisUserCode, StartedAt, SubmittedAt, TotalExtraMinutes, TotalScore, MaxTotalScore, ExaminerName, GeneralRemark, GradedAt, ItemsJson FROM CandidateGrading WHERE SubmissionId = @subId";
        command.Parameters.AddWithValue("@subId", submissionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var itemsJson = reader.GetString(14);
            var items = string.IsNullOrWhiteSpace(itemsJson)
                ? new List<ExamGradingItem>()
                : System.Text.Json.JsonSerializer.Deserialize<List<ExamGradingItem>>(itemsJson) ?? new List<ExamGradingItem>();

            return new CandidateGradingRecord
            {
                SubmissionId = reader.GetString(0),
                CandidateId = reader.GetString(1),
                CandidateName = reader.GetString(2),
                PaperCode = reader.GetString(3),
                DepartmentName = reader.GetString(4),
                HisUserCode = reader.GetString(5),
                StartedAt = DateTime.Parse(reader.GetString(6)),
                SubmittedAt = DateTime.Parse(reader.GetString(7)),
                TotalExtraMinutes = reader.GetInt32(8),
                TotalScore = reader.GetDouble(9),
                MaxTotalScore = reader.GetDouble(10),
                ExaminerName = reader.GetString(11),
                GeneralRemark = reader.GetString(12),
                GradedAt = DateTime.Parse(reader.GetString(13)),
                Items = items
            };
        }
        return null;
    }

    public async Task<IReadOnlyList<CandidateGradingRecord>> GetAllCandidateGradingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT SubmissionId, CandidateId, CandidateName, PaperCode, DepartmentName, HisUserCode, StartedAt, SubmittedAt, TotalExtraMinutes, TotalScore, MaxTotalScore, ExaminerName, GeneralRemark, GradedAt, ItemsJson FROM CandidateGrading ORDER BY GradedAt DESC";

        var list = new List<CandidateGradingRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var itemsJson = reader.GetString(14);
            var items = string.IsNullOrWhiteSpace(itemsJson)
                ? new List<ExamGradingItem>()
                : System.Text.Json.JsonSerializer.Deserialize<List<ExamGradingItem>>(itemsJson) ?? new List<ExamGradingItem>();

            list.Add(new CandidateGradingRecord
            {
                SubmissionId = reader.GetString(0),
                CandidateId = reader.GetString(1),
                CandidateName = reader.GetString(2),
                PaperCode = reader.GetString(3),
                DepartmentName = reader.GetString(4),
                HisUserCode = reader.GetString(5),
                StartedAt = DateTime.Parse(reader.GetString(6)),
                SubmittedAt = DateTime.Parse(reader.GetString(7)),
                TotalExtraMinutes = reader.GetInt32(8),
                TotalScore = reader.GetDouble(9),
                MaxTotalScore = reader.GetDouble(10),
                ExaminerName = reader.GetString(11),
                GeneralRemark = reader.GetString(12),
                GradedAt = DateTime.Parse(reader.GetString(13)),
                Items = items
            });
        }
        return list;
    }
}

public sealed record SnapshotUserRow(string UserId, string UserName, string FullName, string DepartmentCode, string DepartmentName);
public sealed record SnapshotDrugRow(string DrugId, string DrugCode, string DrugName, string Unit, string WarehouseCode, string WarehouseName, string FundingSource, double QuantityOnHand, string? DepartmentCode = null, string? DepartmentName = null);
public sealed record SnapshotPatientRow(string PatientId, string MedicalCode, string FullName, string DateOfBirth, string Gender, string Address, string? InsuranceNumber, string Diagnosis, string PaymentType);

public sealed record SnapshotInpatientRow(
    string BenhAnId,
    string SoBenhAn,
    string MedicalCode,
    string FullName,
    string Gender,
    string DateOfBirth,
    string? Address,
    string DepartmentCode,
    string DepartmentName,
    string? RoomCode,
    string? RoomName,
    string? BedCode,
    string? Floor,
    string AdmissionDate,
    string? AdmissionTime,
    string? BedInTime,
    string? DepartmentInDate,
    int DaysInHospital,
    string? InsuranceNumber,
    string? Diagnosis,
    string? DoctorName);

public sealed record SnapshotOutpatientRow(
    string BenhAnNgoaiTruId,
    string SoBenhAn,
    string? SoLuuTru,
    string MedicalCode,
    string FullName,
    string Gender,
    string DateOfBirth,
    string? Address,
    string DepartmentCode,
    string DepartmentName,
    string? RoomCode,
    string? RoomName,
    string? BedCode,
    string? TreatmentFromDate,
    string? TreatmentToDate,
    string AdmissionDate,
    int DaysInTreatment,
    string? InsuranceNumber,
    string? Diagnosis,
    string? DoctorName);

public sealed record SnapshotInventoryPeriodRow(
    string PeriodCode,
    string PeriodName,
    string FromDate,
    string ToDate,
    string WarehouseCode,
    string WarehouseName,
    string DepartmentCode,
    string DepartmentName,
    string DrugCode,
    string DrugName,
    string Unit,
    double ClosingStock,
    string FundingSource);

