using ExamGenerator.Application;
using ExamGenerator.Domain;

namespace ExamGenerator.Tests;

public class CatalogSynchronizationTests
{
    [Fact]
    public async Task Invalid_catalog_is_not_saved()
    {
        var data = CreateDataSet(
            new[] { new Warehouse("w1", "KHO-01", "Kho 1", null) });
        var store = new MemorySnapshotStore();

        var snapshot = await new CatalogSynchronizationService(
            new MemoryCatalogProvider(data), store).SynchronizeAsync();

        Assert.False(snapshot.IsValid);
        Assert.Empty(store.Saved);
        Assert.Contains(snapshot.ValidationErrors, error => error.Contains("chưa có khoa", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Valid_catalog_is_saved_with_validation_result()
    {
        var data = CreateDataSet(
            new[] { new Warehouse("w1", "KHO-01", "Kho 1", "NOI") });
        var store = new MemorySnapshotStore();

        var snapshot = await new CatalogSynchronizationService(
            new MemoryCatalogProvider(data), store).SynchronizeAsync();

        Assert.True(snapshot.IsValid);
        Assert.Single(store.Saved);
        Assert.Equal(snapshot.Id, store.Saved[0].Snapshot.Id);
    }

    [Fact]
    public async Task Inventory_search_filters_correctly_by_department_and_keyword()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ExamGen_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var storage = new Infrastructure.LocalApplicationStorage(tempDir);
            var db = new Infrastructure.SqliteDatabase(storage);
            await db.InitializeAsync();

            await db.ReplaceSnapshotDrugsAsync(new[]
            {
                new Infrastructure.SnapshotDrugRow("d1", "PARA500", "Paracetamol 500mg", "Viên", "KHO_NOI", "Kho Nội", "BHYT", 100, "NOI", "Khoa Nội"),
                new Infrastructure.SnapshotDrugRow("d2", "AMOX500", "Amoxicillin 500mg", "Viên", "KHO_NGOAI", "Kho Ngoại", "Viện phí", 50, "NGOAI", "Khoa Ngoại"),
                new Infrastructure.SnapshotDrugRow("d3", "CEF200", "Cefixim 200mg", "Viên", "KHO_NOI", "Kho Nội", "BHYT", 80, "NOI", "Khoa Nội")
            });

            // Search by department
            var noiDrugs = await db.SearchInventoryAsync("Khoa Nội", null, null, null);
            Assert.Equal(2, noiDrugs.Count);
            Assert.All(noiDrugs, d => Assert.Equal("Khoa Nội", d.DepartmentName));

            // Search by keyword
            var para = await db.SearchInventoryAsync(null, null, null, "Para");
            Assert.Single(para);
            Assert.Equal("PARA500", para[0].DrugCode);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static CatalogDataSet CreateDataSet(IReadOnlyList<Warehouse> warehouses)
    {
        var snapshot = new CatalogSnapshot(
            "snapshot-1", DateTimeOffset.UtcNow, "test", "v1",
            new Dictionary<string, int>(), Array.Empty<string>());

        return new CatalogDataSet(
            new[] { new Department("d1", "NOI", "Khoa Nội") },
            warehouses,
            new[] { new ServiceGroup("g1", "CLS", "Cận lâm sàng") },
            new[] { new ClinicalService("s1", "SA-01", "Siêu âm", "g1") },
            new[] { new InventoryItem("i1", "D-01", "Thuốc thử", "Viên", "w1", "BHYT", 10, InventoryItemType.Drug) },
            new[] { new HisUser("u1", "user01", "User 01", "d1") },
            new[] { new PatientSource("p1", "BN-01", "Bệnh nhân 01", new DateOnly(1990, 1, 1), "Nam", "Địa chỉ", PatientPaymentType.Insurance, "TE1001") },
            snapshot);
    }

    private sealed class MemoryCatalogProvider(CatalogDataSet data) : IReadOnlyCatalogProvider
    {
        public Task<CatalogDataSet> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(data);
    }

    private sealed class MemorySnapshotStore : ICatalogSnapshotStore
    {
        public List<CatalogDataSet> Saved { get; } = [];

        public Task SaveAsync(CatalogDataSet dataSet, CancellationToken cancellationToken = default)
        {
            Saved.Add(dataSet);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Departments_additive_sync_preserves_existing_departments_and_mappings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ExamGen_AdditiveTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var storage = new Infrastructure.LocalApplicationStorage(tempDir);
            var db = new Infrastructure.SqliteDatabase(storage);
            await db.InitializeAsync();
            await db.EnsureDefaultTemplatesAsync();

            // 1. Initial sync with 2 departments
            var initialHisDepts = new (string Id, string Code, string Name, string? ParentId)[]
            {
                ("pb1", "NOI", "Khoa Nội", null),
                ("pb2", "NGOAI", "Khoa Ngoại tổng hợp", null)
            };
            var (total1, added1, preserved1) = await db.SyncDepartmentsAdditiveAsync(initialHisDepts);
            Assert.Equal(2, total1);
            Assert.Equal(2, added1);
            Assert.Equal(0, preserved1);

            // 2. Configure mapping Khoa - Kho thi
            await db.SaveDepartmentConfigurationAsync("NOI", "Khoa Nội", "KHO_NOI", "Kho trực Nội");
            var mappingsBefore = await db.GetDepartmentConfigurationsAsync();
            Assert.Single(mappingsBefore);

            // 3. Second sync: HIS only returns NGOAI and new SAN (NOI is missing or suspended on HIS)
            var secondHisDepts = new (string Id, string Code, string Name, string? ParentId)[]
            {
                ("pb2", "NGOAI", "Khoa Ngoại tổng hợp", null),
                ("pb3", "SAN", "Khoa Phụ Sản", null)
            };
            var (total2, added2, preserved2) = await db.SyncDepartmentsAdditiveAsync(secondHisDepts);
            Assert.Equal(3, total2);
            Assert.Equal(1, added2); // Only SAN is added
            Assert.Equal(2, preserved2); // NOI and NGOAI are preserved

            // 4. Verify all 3 departments exist and NOI was not deleted
            var allDepts = await db.GetDepartmentsWithStatusAsync();
            Assert.Equal(3, allDepts.Count);
            Assert.Contains(allDepts, d => d.Code == "NOI");
            Assert.Contains(allDepts, d => d.Code == "NGOAI");
            Assert.Contains(allDepts, d => d.Code == "SAN");

            // 5. Verify mappings and templates for Khoa Nội remain 100% intact
            var mappingsAfter = await db.GetDepartmentConfigurationsAsync();
            Assert.Single(mappingsAfter);
            Assert.Equal("NOI", mappingsAfter[0].DepartmentCode);
            Assert.Equal("Khoa Nội", mappingsAfter[0].DepartmentName);

            var templates = await db.GetTemplatesAsync();
            Assert.Contains(templates, t => t.Department == "Khoa Nội");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Manual_department_exam_status_toggle_is_persisted_and_not_overwritten_by_sync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ExamGen_StatusToggleTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var storage = new Infrastructure.LocalApplicationStorage(tempDir);
            var db = new Infrastructure.SqliteDatabase(storage);
            await db.InitializeAsync();

            var hisDepts = new (string Id, string Code, string Name, string? ParentId)[]
            {
                ("pb1", "NOI", "Khoa Nội", null),
                ("pb2", "NGOAI", "Khoa Ngoại tổng hợp", null)
            };
            await db.SyncDepartmentsAdditiveAsync(hisDepts);

            // User manually disables "NOI" for exam
            await db.SetDepartmentExamStatusAsync("NOI", true);

            var deptsAfterDisable = await db.GetDepartmentsWithStatusAsync();
            var noi = deptsAfterDisable.First(d => d.Code == "NOI");
            var ngoai = deptsAfterDisable.First(d => d.Code == "NGOAI");
            Assert.True(noi.IsDisabled);
            Assert.False(ngoai.IsDisabled);

            // Another sync from HIS with NOI and a new dept
            var newSyncDepts = new (string Id, string Code, string Name, string? ParentId)[]
            {
                ("pb1", "NOI", "Khoa Nội", null),
                ("pb2", "NGOAI", "Khoa Ngoại tổng hợp", null),
                ("pb4", "CC", "Khoa Cấp cứu", null)
            };
            await db.SyncDepartmentsAdditiveAsync(newSyncDepts);

            // Verify NOI is STILL disabled (manual toggle preserved)
            var deptsAfterSync = await db.GetDepartmentsWithStatusAsync();
            var noiAfterSync = deptsAfterSync.First(d => d.Code == "NOI");
            var ccAfterSync = deptsAfterSync.First(d => d.Code == "CC");
            Assert.True(noiAfterSync.IsDisabled);
            Assert.False(ccAfterSync.IsDisabled);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Template_actions_and_scores_can_be_customized_and_persisted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ExamGen_TplEdit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var storage = new Infrastructure.LocalApplicationStorage(tempDir);
            var db = new Infrastructure.SqliteDatabase(storage);
            await db.InitializeAsync();

            var customActions = new (string Code, string Name, double Score)[]
            {
                ("NT_NHAN_BENH_KHOA", "Nhận bệnh vào khoa điều trị", 1.5),
                ("YL_CHI_DINH_CLS", "Chỉ định dịch vụ Cận lâm sàng", 2.5),
                ("YL_CHI_DINH_THUOC_VTYT", "Lên y lệnh thuốc & vật tư y tế", 3.0),
                ("TK_KIEM_TON_KHO", "Kiểm tra tồn kho dược cơ số", 1.0),
                ("CK_CHUYEN_KHOA", "Chuyển khoa điều trị", 2.0)
            };

            await db.SaveTemplateAsync(
                "tpl_custom_01",
                "Đề Điều Dưỡng Nội Chỉnh Sửa",
                "Khoa Nội",
                "Điều dưỡng",
                "WardAdmissionPreparation",
                customActions);

            var templates = await db.GetTemplatesAsync();
            var tpl = templates.FirstOrDefault(x => x.Id == "tpl_custom_01");
            Assert.NotNull(tpl.Id);
            Assert.Equal("Đề Điều Dưỡng Nội Chỉnh Sửa", tpl.Name);
            Assert.Equal("Khoa Nội", tpl.Department);
            Assert.Equal(5, tpl.QuestionCount);
            Assert.Equal(10.0, tpl.TotalScore);

            var actions = await db.GetTemplateActionDetailsAsync("tpl_custom_01");
            Assert.Equal(5, actions.Count);
            Assert.Equal("NT_NHAN_BENH_KHOA", actions[0].Code);
            Assert.Equal(1.5, actions[0].Score);
            Assert.Equal("YL_CHI_DINH_CLS", actions[1].Code);
            Assert.Equal(2.5, actions[1].Score);
            Assert.Equal(10.0, actions.Sum(x => x.Score));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Template_can_be_cloned_with_new_name_and_department()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ExamGen_TplClone_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var storage = new Infrastructure.LocalApplicationStorage(tempDir);
            var db = new Infrastructure.SqliteDatabase(storage);
            await db.InitializeAsync();

            var originalActions = new (string Code, string Name, double Score)[]
            {
                ("TN_TIEP_NHAN", "Tiếp nhận trực tiếp người bệnh", 2.0),
                ("YL_CHI_DINH_CLS", "Chỉ định CLS", 3.0),
                ("YL_CHI_DINH_THUOC_VTYT", "Y lệnh thuốc", 5.0)
            };

            await db.SaveTemplateAsync(
                "tpl_orig",
                "Đề Gốc Khoa Nội",
                "Khoa Nội",
                "Điều dưỡng",
                "DirectReception",
                originalActions);

            var actionsToClone = await db.GetTemplateActionDetailsAsync("tpl_orig");

            await db.SaveTemplateAsync(
                "tpl_clone",
                "Đề Nhân Bản Khoa Ngoại",
                "Khoa Ngoại tổng hợp",
                "Điều dưỡng",
                "DirectReception",
                actionsToClone);

            var templates = await db.GetTemplatesAsync();
            var orig = templates.First(x => x.Id == "tpl_orig");
            var clone = templates.First(x => x.Id == "tpl_clone");

            Assert.Equal("Đề Gốc Khoa Nội", orig.Name);
            Assert.Equal("Khoa Nội", orig.Department);

            Assert.Equal("Đề Nhân Bản Khoa Ngoại", clone.Name);
            Assert.Equal("Khoa Ngoại tổng hợp", clone.Department);
            Assert.Equal(orig.QuestionCount, clone.QuestionCount);
            Assert.Equal(orig.TotalScore, clone.TotalScore);

            var clonedActions = await db.GetTemplateActionDetailsAsync("tpl_clone");
            Assert.Equal(3, clonedActions.Count);
            Assert.Equal(10.0, clonedActions.Sum(x => x.Score));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ActionDefinitions_are_seeded_with_defaults_and_allow_adding_custom_actions_with_sample_scores()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ExamGen_Test_ActionDef_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var storage = new Infrastructure.LocalApplicationStorage(tempDir);
            var db = new Infrastructure.SqliteDatabase(storage);
            await db.InitializeAsync();

            // 1. Verify 13 built-in actions seeded
            var initialActions = await db.GetActionDefinitionsAsync();
            Assert.True(initialActions.Count >= 13);
            Assert.Contains(initialActions, a => a.Code == "TN_TIEP_NHAN" && a.IsBuiltIn);
            Assert.Contains(initialActions, a => a.Code == "YL_CHI_DINH_CLS" && a.IsBuiltIn);

            // 2. Add custom action with sample score
            var customDef = new SystemActionDefinition(
                "XN_HUYET_HOC",
                "Nhập kết quả xét nghiệm huyết học",
                2.5,
                "Kỹ thuật viên CLS",
                "Nhập các chỉ số RBC, WBC, HGB vào phân hệ xét nghiệm HIS.",
                IsBuiltIn: false);

            await db.SaveActionDefinitionAsync(customDef);

            var updatedActions = await db.GetActionDefinitionsAsync();
            var added = updatedActions.FirstOrDefault(a => a.Code == "XN_HUYET_HOC");
            Assert.NotNull(added);
            Assert.Equal("Nhập kết quả xét nghiệm huyết học", added.Name);
            Assert.Equal(2.5, added.DefaultScore);
            Assert.Equal("Kỹ thuật viên CLS", added.Category);
            Assert.Equal("Nhập các chỉ số RBC, WBC, HGB vào phân hệ xét nghiệm HIS.", added.Description);
            Assert.False(added.IsBuiltIn);

            // 3. Try to delete built-in action -> Must be protected and return false
            var deleteBuiltInResult = await db.DeleteActionDefinitionAsync("TN_TIEP_NHAN");
            Assert.False(deleteBuiltInResult);
            var actionsAfterProtectedDelete = await db.GetActionDefinitionsAsync();
            Assert.Contains(actionsAfterProtectedDelete, a => a.Code == "TN_TIEP_NHAN");

            // 4. Delete custom action -> Must succeed
            var deleteCustomResult = await db.DeleteActionDefinitionAsync("XN_HUYET_HOC");
            Assert.True(deleteCustomResult);
            var finalActions = await db.GetActionDefinitionsAsync();
            Assert.DoesNotContain(finalActions, a => a.Code == "XN_HUYET_HOC");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Department_can_map_multiple_warehouses_and_retrieve_them()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ExamGen_WhTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var storage = new Infrastructure.LocalApplicationStorage(tempDir);
            var db = new Infrastructure.SqliteDatabase(storage);
            await db.InitializeAsync();

            // Save 3 warehouses for Department NGOAI
            await db.SaveDepartmentWarehouseMappingsAsync("NGOAI", "Khoa Ngoại", new[]
            {
                ("KHO_1", "Kho Ngoại Tổng Hợp"),
                ("KHO_2", "Kho Tủ Trực Cấp Cứu"),
                ("KHO_3", "Kho Cơ Số Ngoại")
            });

            // Retrieve warehouses for this specific department
            var whList = await db.GetWarehousesForDepartmentAsync("NGOAI");
            Assert.Equal(3, whList.Count);
            Assert.Contains(whList, w => w.WarehouseCode == "KHO_1");
            Assert.Contains(whList, w => w.WarehouseCode == "KHO_2");
            Assert.Contains(whList, w => w.WarehouseCode == "KHO_3");

            // Retrieve all department warehouse mappings
            var allMappings = await db.GetDepartmentWarehouseMappingsAsync();
            Assert.Equal(3, allMappings.Count);
            Assert.All(allMappings, m => Assert.Equal("NGOAI", m.DepartmentCode));

            // Verify backward compatibility method
            var legacy = await db.GetDepartmentConfigurationsAsync();
            Assert.Equal(3, legacy.Count);

            // Update mapping to only 1 warehouse
            await db.SaveDepartmentWarehouseMappingsAsync("NGOAI", "Khoa Ngoại", new[]
            {
                ("KHO_1", "Kho Ngoại Tổng Hợp")
            });

            var updatedWhList = await db.GetWarehousesForDepartmentAsync("NGOAI");
            Assert.Single(updatedWhList);
            Assert.Equal("KHO_1", updatedWhList[0].WarehouseCode);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Exam_generation_history_is_persisted_and_retrieved_in_descending_order()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ExamGen_HistoryTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var storage = new Infrastructure.LocalApplicationStorage(tempDir);
            var db = new Infrastructure.SqliteDatabase(storage);
            await db.InitializeAsync();

            var date1 = DateOnly.FromDateTime(DateTime.Today);
            var date2 = date1.AddDays(1);

            await db.SaveGeneratedFileAsync("b1", "Đợt Thi Sáng", date1, "D:\\output\\DeThi_Sang.zip");
            await db.SaveGeneratedFileAsync("b2", "Đợt Thi Chiều", date2, "D:\\output\\DeThi_Chieu.zip");

            var history = await db.GetGeneratedFilesAsync();
            Assert.Equal(2, history.Count);

            // Latest generated file should appear first (ORDER BY Id DESC)
            Assert.Equal("Đợt Thi Chiều", history[0].BatchName);
            Assert.Equal("D:\\output\\DeThi_Chieu.zip", history[0].FilePath);

            Assert.Equal("Đợt Thi Sáng", history[1].BatchName);
            Assert.Equal("D:\\output\\DeThi_Sang.zip", history[1].FilePath);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }
}
