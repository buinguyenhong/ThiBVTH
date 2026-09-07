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
                QuantityOnHand REAL NOT NULL
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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
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
            "('tpl-san','Đề Nữ hộ sinh / Điều dưỡng Sản','Khoa Phụ Sản','Điều dưỡng',8,10,'DirectReception',$at), " +
            "('tpl-nhi','Đề Điều dưỡng Nhi (Tiếp nhận trực tiếp)','Khoa Nhi','Điều dưỡng',8,10,'DirectReception',$at), " +
            "('tpl-cc','Đề Điều dưỡng Cấp cứu','Khoa Cấp cứu','Điều dưỡng',8,10,'DirectReception',$at), " +
            "('tpl-kkb','Đề Lễ tân phòng khám / Tiếp đón','Khoa Khám bệnh','Lễ tân',5,10,'DirectReception',$at)";
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
            ('tpl-kkb','TN_TIEP_NHAN','Tiếp nhận trực tiếp',2),
            ('tpl-kkb','TC_THU_TAM_UNG','Thu tạm ứng',2),
            ('tpl-kkb','TC_THANH_TOAN_RA_VIEN','Thanh toán viện phí',2),
            ('tpl-kkb','TC_TRA_CUU_BENH_SU','Tra cứu lịch sử KCB',2),
            ('tpl-kkb','YL_CHI_DINH_CLS','Chỉ định CLS ban đầu',2);
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

        foreach (var action in actions)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO TemplateAction (TemplateId,ActionCode,ActionName,Score) VALUES ($id,$code,$name,$score)";
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$code", action.Code);
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

    public async Task<IReadOnlyList<(string BatchName, string ExamDate, string FilePath, string CreatedAt)>> GetGeneratedFilesAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<(string, string, string, string)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT b.BatchName, b.ExamDate, f.FilePath, f.CreatedAt FROM GeneratedFile f JOIN ExamBatch b ON b.BatchId = f.BatchId ORDER BY f.Id DESC";
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
            insert.CommandText = "INSERT INTO CatalogItem (CatalogType, ItemId, ItemCode, ItemName, ParentId, ImportedAt) VALUES ($type, $id, $code, $name, $parent, $at)";
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
            insert.CommandText = "INSERT INTO SnapshotUser (UserId, UserName, FullName, DepartmentCode, DepartmentName) VALUES ($id, $userName, $fullName, $deptCode, $deptName)";
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
            insert.CommandText = "INSERT INTO SnapshotDrug (DrugId, DrugCode, DrugName, Unit, WarehouseCode, WarehouseName, FundingSource, QuantityOnHand) VALUES ($id, $code, $name, $unit, $whCode, $whName, $src, $qty)";
            insert.Parameters.AddWithValue("$id", d.DrugId);
            insert.Parameters.AddWithValue("$code", d.DrugCode);
            insert.Parameters.AddWithValue("$name", d.DrugName);
            insert.Parameters.AddWithValue("$unit", d.Unit);
            insert.Parameters.AddWithValue("$whCode", d.WarehouseCode);
            insert.Parameters.AddWithValue("$whName", d.WarehouseName);
            insert.Parameters.AddWithValue("$src", d.FundingSource);
            insert.Parameters.AddWithValue("$qty", d.QuantityOnHand);
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
            insert.CommandText = "INSERT INTO SnapshotPatient (PatientId, MedicalCode, FullName, DateOfBirth, Gender, Address, InsuranceNumber, Diagnosis, PaymentType) VALUES ($id, $med, $name, $dob, $gender, $addr, $ins, $diag, $pay)";
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

        return result;
    }

    public async Task SaveDepartmentConfigurationAsync(
        string departmentCode,
        string departmentName,
        string warehouseCode,
        string warehouseName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DepartmentConfiguration (DepartmentCode, DepartmentName, WarehouseCode, WarehouseName, UpdatedAt)
            VALUES ($departmentCode, $departmentName, $warehouseCode, $warehouseName, $updatedAt)
            ON CONFLICT(DepartmentCode) DO UPDATE SET
                DepartmentName = excluded.DepartmentName,
                WarehouseCode = excluded.WarehouseCode,
                WarehouseName = excluded.WarehouseName,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$departmentCode", departmentCode.Trim());
        command.Parameters.AddWithValue("$departmentName", departmentName.Trim());
        command.Parameters.AddWithValue("$warehouseCode", warehouseCode.Trim());
        command.Parameters.AddWithValue("$warehouseName", warehouseName.Trim());
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(string DepartmentCode, string DepartmentName, string WarehouseCode, string WarehouseName)>> GetDepartmentConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<(string, string, string, string)>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT DepartmentCode, DepartmentName, WarehouseCode, WarehouseName FROM DepartmentConfiguration ORDER BY DepartmentName";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
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

    public async Task<IReadOnlyList<SnapshotDrugRow>> GetWarehouseDrugsAsync(string warehouseCode, CancellationToken cancellationToken = default)
    {
        var rows = new List<SnapshotDrugRow>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT DrugId, DrugCode, DrugName, Unit, WarehouseCode, WarehouseName, FundingSource, QuantityOnHand FROM SnapshotDrug WHERE WarehouseCode = $code AND QuantityOnHand > 0 ORDER BY DrugName";
        command.Parameters.AddWithValue("$code", warehouseCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new SnapshotDrugRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetDouble(7)));
        return rows;
    }

    public async Task<IReadOnlyList<SnapshotDrugRow>> SearchInventoryAsync(string? deptName, string? warehouseCode, string? source, string? keyword, CancellationToken cancellationToken = default)
    {
        var rows = new List<SnapshotDrugRow>();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();

        var query = "SELECT DrugId, DrugCode, DrugName, Unit, WarehouseCode, WarehouseName, FundingSource, QuantityOnHand FROM SnapshotDrug WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(warehouseCode) && warehouseCode != "Tất cả kho" && warehouseCode != "ALL")
        {
            query += " AND WarehouseCode = $wh";
            command.Parameters.AddWithValue("$wh", warehouseCode);
        }
        if (!string.IsNullOrWhiteSpace(source) && source != "Tất cả nguồn")
        {
            query += " AND FundingSource = $source";
            command.Parameters.AddWithValue("$source", source);
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query += " AND (DrugName LIKE $kw OR DrugCode LIKE $kw)";
            command.Parameters.AddWithValue("$kw", $"%{keyword.Trim()}%");
        }
        query += " ORDER BY WarehouseName, DrugName LIMIT 100";

        command.CommandText = query;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new SnapshotDrugRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetDouble(7)));
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
}

public sealed record SnapshotUserRow(string UserId, string UserName, string FullName, string DepartmentCode, string DepartmentName);
public sealed record SnapshotDrugRow(string DrugId, string DrugCode, string DrugName, string Unit, string WarehouseCode, string WarehouseName, string FundingSource, double QuantityOnHand);
public sealed record SnapshotPatientRow(string PatientId, string MedicalCode, string FullName, string DateOfBirth, string Gender, string Address, string? InsuranceNumber, string Diagnosis, string PaymentType);
