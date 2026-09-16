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
}
