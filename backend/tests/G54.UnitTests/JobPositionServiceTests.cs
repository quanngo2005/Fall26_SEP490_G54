using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.JobPosition;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class JobPositionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 8, 30, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedKpiNames = ["Điểm hài lòng", "Khách hàng mới"];

    [Fact]
    public async Task ListReturnsRealPositionsExcludingTheDevelopmentBootstrapOne()
    {
        var world = new World();
        world.AddPosition("TELLER", "Giao dịch viên", "Giao dịch tại quầy");
        world.AddPosition("SALES", "Chuyên viên Kinh doanh", "Bán hàng");
        world.AddPosition("DEV_ADMIN", "Development Administrator", null);
        await world.SaveAsync();

        var result = await world.Service().GetJobPositionsAsync(new JobPositionFilterDto());

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(["SALES", "TELLER"], result.Items.Select(i => i.Code)); // ordered by code, codes untouched
        Assert.DoesNotContain(result.Items, i => i.Code == "DEV_ADMIN");
        var sales = result.Items.Single(i => i.Code == "SALES");
        Assert.Equal("Chuyên viên Kinh doanh", sales.Title);
        Assert.Equal("Bán hàng", sales.Description);
        Assert.Equal("ACTIVE", sales.Status);
        Assert.Equal(Now, sales.LastSyncAt);
    }

    [Fact]
    public async Task ListCountsOnlyActiveEmployeesAndNeverInventsFigures()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER", "Teller", null);
        world.AddEmployee("E1", teller.Id, EmployeeStatus.Active);
        world.AddEmployee("E2", teller.Id, EmployeeStatus.Active);
        world.AddEmployee("E3", teller.Id, EmployeeStatus.OnLeave);
        world.AddEmployee("E4", teller.Id, EmployeeStatus.Terminated);
        world.AddPosition("EMPTY", "Empty", null);
        await world.SaveAsync();

        var result = await world.Service().GetJobPositionsAsync(new JobPositionFilterDto());

        var row = result.Items.Single(i => i.Code == "TELLER");
        Assert.Equal(2, row.EmployeeCount);
        Assert.Null(row.Description);
        Assert.Null(row.EvaluationProfileId);
        Assert.Null(row.EvaluationProfileName);
        Assert.Equal(0, row.WorkTypeCount);
        Assert.Empty(row.KpiItems);
        var empty = result.Items.Single(i => i.Code == "EMPTY");
        Assert.Equal(0, empty.EmployeeCount);
    }

    [Fact]
    public async Task ListUsesTheCurrentActiveProfileAndRealWorkTypeAndKpiNames()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER", "Teller", null);
        world.AddProfile(teller.Id, "EP-OLD", "Old profile", 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), EvaluationProfileStatus.Active);
        var current = world.AddProfile(teller.Id, "EP-NOW", "Current profile", 2, new DateOnly(2026, 1, 1), null, EvaluationProfileStatus.Active);
        world.AddProfile(teller.Id, "EP-DRAFT", "Draft profile", 3, new DateOnly(2026, 6, 1), null, EvaluationProfileStatus.Draft);
        world.AddProfile(teller.Id, "EP-FUTURE", "Future profile", 4, new DateOnly(2027, 1, 1), null, EvaluationProfileStatus.Active);

        var accounts = world.AddWorkType("MO_TK", "Mở tài khoản", RecordStatus.Active);
        var retired = world.AddWorkType("OLD", "Ngừng dùng", RecordStatus.Inactive);
        world.Link(teller.Id, accounts);
        world.Link(teller.Id, retired);
        world.AddKpi(accounts, "KH_MOI", "Khách hàng mới", "TK", true, new DateOnly(2026, 1, 1), null);
        world.AddKpi(accounts, "CSAT", "Điểm hài lòng", "Điểm", false, new DateOnly(2026, 1, 1), null);
        world.AddKpi(accounts, "EXPIRED", "KPI hết hạn", "%", false, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        world.AddKpi(retired, "HIDDEN", "KPI của loại việc ngừng dùng", "%", false, new DateOnly(2026, 1, 1), null);
        await world.SaveAsync();

        var row = Assert.Single((await world.Service().GetJobPositionsAsync(new JobPositionFilterDto())).Items);

        Assert.Equal(current.Id, row.EvaluationProfileId);
        Assert.Equal("Current profile", row.EvaluationProfileName); // no fabricated "· Q4/2026" suffix
        Assert.Equal(1, row.WorkTypeCount);
        Assert.Equal(ExpectedKpiNames, row.KpiItems);
    }

    [Fact]
    public async Task ListFiltersByKeywordCaseInsensitivelyAndByStatus()
    {
        var world = new World();
        world.AddPosition("TELLER", "Giao dịch viên", null);
        world.AddPosition("SALES", "Chuyên viên Kinh doanh", null);
        world.AddPosition("OLD", "Vị trí cũ", null, RecordStatus.Inactive);
        await world.SaveAsync();
        var service = world.Service();

        var byCode = await service.GetJobPositionsAsync(new JobPositionFilterDto(Keyword: "  teller "));
        Assert.Equal(["TELLER"], byCode.Items.Select(i => i.Code));

        var byTitle = await service.GetJobPositionsAsync(new JobPositionFilterDto(Keyword: "KINH DOANH"));
        Assert.Equal(["SALES"], byTitle.Items.Select(i => i.Code));

        var inactive = await service.GetJobPositionsAsync(new JobPositionFilterDto(Status: "inactive"));
        Assert.Equal(["OLD"], inactive.Items.Select(i => i.Code));
        Assert.Equal(1, inactive.TotalCount);

        var none = await service.GetJobPositionsAsync(new JobPositionFilterDto(Keyword: "zzz"));
        Assert.Empty(none.Items);
        Assert.Equal(0, none.TotalCount);
    }

    [Fact]
    public async Task ListRejectsAnUnknownStatusInsteadOfIgnoringIt()
    {
        var world = new World();
        world.AddPosition("TELLER", "Teller", null);
        await world.SaveAsync();

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            world.Service().GetJobPositionsAsync(new JobPositionFilterDto(Status: "BOGUS")));

        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task ListPagesAndClampsPageSize()
    {
        var world = new World();
        for (var i = 1; i <= 5; i++)
        {
            world.AddPosition($"P{i}", $"Position {i}", null);
        }

        await world.SaveAsync();
        var service = world.Service();

        var second = await service.GetJobPositionsAsync(new JobPositionFilterDto(Page: 2, PageSize: 2));
        Assert.Equal(["P3", "P4"], second.Items.Select(i => i.Code));
        Assert.Equal(5, second.TotalCount);
        Assert.Equal(2, second.Page);
        Assert.Equal(2, second.PageSize);

        var clamped = await service.GetJobPositionsAsync(new JobPositionFilterDto(Page: -4, PageSize: 10_000));
        Assert.Equal(1, clamped.Page);
        Assert.Equal(100, clamped.PageSize);
        Assert.Equal(5, clamped.Items.Count);
    }

    [Fact]
    public async Task DetailReturnsNullForUnknownAndForTheDevelopmentBootstrapPosition()
    {
        var world = new World();
        var bootstrap = world.AddPosition("DEV_ADMIN", "Development Administrator", null);
        await world.SaveAsync();
        var service = world.Service();

        Assert.Null(await service.GetJobPositionByIdAsync(Guid.NewGuid()));
        Assert.Null(await service.GetJobPositionByIdAsync(bootstrap.Id));
    }

    [Fact]
    public async Task DetailReturnsTheRealProfileWorkTypesAndKpis()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER", "Giao dịch viên", "Giao dịch tại quầy");
        world.AddEmployee("E1", teller.Id, EmployeeStatus.Active);
        world.AddEmployee("E2", teller.Id, EmployeeStatus.Terminated);
        var profile = world.AddProfile(teller.Id, "EP-TELLER", "Hồ sơ Giao dịch viên", 3, new DateOnly(2026, 1, 1), null, EvaluationProfileStatus.Active);
        var quality = world.AddGroup("CLDV", "Chất lượng dịch vụ");
        var business = world.AddGroup("KD", "Kinh doanh");
        world.AddProfileItem(profile, quality, 50m, 2);
        world.AddProfileItem(profile, business, 20.5m, 1);

        var accounts = world.AddWorkType("MO_TK", "Mở tài khoản", RecordStatus.Active, "Mở tài khoản thanh toán");
        world.Link(teller.Id, accounts);
        world.AddKpi(accounts, "KH_MOI", "Khách hàng mới", "Tài khoản", true, new DateOnly(2026, 1, 1), null);
        world.AddKpi(accounts, "CSAT", "Điểm hài lòng", "Điểm", false, new DateOnly(2026, 1, 1), null);
        world.AddKpi(accounts, "OLD", "KPI hết hạn", "%", false, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        await world.SaveAsync();

        var detail = await world.Service().GetJobPositionByIdAsync(teller.Id);

        Assert.NotNull(detail);
        Assert.Equal("TELLER", detail.Code);
        Assert.Equal("Giao dịch viên", detail.Title);
        Assert.Equal("Giao dịch tại quầy", detail.Description);
        Assert.Equal(1, detail.EmployeeCount);
        Assert.Equal("ACTIVE", detail.Status);
        Assert.Equal(Now, detail.LastSyncAt);

        var profileDto = Assert.IsType<JobPositionEvaluationProfileDto>(detail.EvaluationProfile);
        Assert.Equal("EP-TELLER", profileDto.Code);
        Assert.Equal("Hồ sơ Giao dịch viên", profileDto.Name);
        Assert.Equal(3, profileDto.Version);
        Assert.Equal("ACTIVE", profileDto.Status);
        Assert.Equal(new DateOnly(2026, 1, 1), profileDto.EffectiveFrom);
        Assert.Equal(["Kinh doanh", quality.Name], profileDto.Items.Select(i => i.GroupName)); // by display order
        Assert.Equal(70.5m, profileDto.TotalWeightPercent); // decimals are not truncated
        Assert.Equal(20.5m, profileDto.Items[0].WeightPercent);

        var workType = Assert.Single(detail.WorkTypes);
        Assert.Equal("MO_TK", workType.Code);
        Assert.Equal("Mở tài khoản thanh toán", workType.Description);
        Assert.Equal(2, workType.KpiCount); // expired mapping excluded
        Assert.Equal(["CSAT", "KH_MOI"], workType.Kpis.Select(k => k.Code));
        Assert.True(workType.Kpis.Single(k => k.Code == "KH_MOI").IsRequired);
        Assert.Equal("Điểm", workType.Kpis.Single(k => k.Code == "CSAT").Unit);
    }

    [Fact]
    public async Task DetailProfileGroupCodeIsTheCatalogGroupOfItsFirstKpiInDisplayOrder()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER", "Giao dịch viên", null);
        var profile = world.AddProfile(teller.Id, "EP-TELLER", "Hồ sơ", 1, new DateOnly(2026, 1, 1), null, EvaluationProfileStatus.Active);
        var business = world.AddGroup("KD", "Kinh doanh");
        var service = world.AddGroup("CLDV", "Chất lượng dịch vụ");
        var mixed = world.AddProfileItem(profile, world.AddGroup("MIX", "Nhóm hỗn hợp"), 100m, 1);
        world.AddProfileLeaf(mixed, service, "LEAF_SECOND", 2);
        world.AddProfileLeaf(mixed, business, "LEAF_FIRST", 1);
        await world.SaveAsync();

        var detail = await world.Service().GetJobPositionByIdAsync(teller.Id);

        var item = Assert.Single(Assert.IsType<JobPositionEvaluationProfileDto>(detail!.EvaluationProfile).Items);
        Assert.Equal("Nhóm hỗn hợp", item.GroupName);
        Assert.Equal("KD", item.GroupCode);
    }

    [Fact]
    public async Task DetailProfileGroupCodeIsEmptyWhenTheGroupHasNoKpi()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER", "Giao dịch viên", null);
        var profile = world.AddProfile(teller.Id, "EP-TELLER", "Hồ sơ", 1, new DateOnly(2026, 1, 1), null, EvaluationProfileStatus.Active);
        world.AddProfileItem(profile, world.AddGroup("KD", "Kinh doanh"), 100m, 1);
        await world.SaveAsync();

        var detail = await world.Service().GetJobPositionByIdAsync(teller.Id);

        var item = Assert.Single(Assert.IsType<JobPositionEvaluationProfileDto>(detail!.EvaluationProfile).Items);
        Assert.Equal(string.Empty, item.GroupCode);
    }

    [Fact]
    public async Task DetailHasNoProfileAndNoWorkTypesWhenNoneAreConfigured()
    {
        var world = new World();
        var position = world.AddPosition("BARE", "Bare", null);
        world.AddProfile(position.Id, "EP-DRAFT", "Draft", 1, new DateOnly(2026, 1, 1), null, EvaluationProfileStatus.Draft);
        await world.SaveAsync();

        var detail = await world.Service().GetJobPositionByIdAsync(position.Id);

        Assert.NotNull(detail);
        Assert.Null(detail.EvaluationProfile);
        Assert.Empty(detail.WorkTypes);
        Assert.Equal(0, detail.EmployeeCount);
        Assert.Null(detail.Description);
    }

    [Fact]
    public async Task DetailReportsAnInactivePositionAsInactive()
    {
        var world = new World();
        var position = world.AddPosition("OLD", "Old", null, RecordStatus.Inactive);
        await world.SaveAsync();

        var detail = await world.Service().GetJobPositionByIdAsync(position.Id);

        Assert.Equal("INACTIVE", detail?.Status);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class World
    {
        private readonly Guid _department = Guid.NewGuid();
        private readonly Dictionary<Guid, EvaluationProfile> _profiles = [];

        public World()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            Db = new AppDbContext(options);
            Db.Departments.Add(new Department
            {
                Id = _department,
                Code = "D",
                Name = "D",
                LevelType = DepartmentLevelType.Department,
                Status = RecordStatus.Active,
                LastSyncAt = Now,
                UpdatedAt = Now,
            });
        }

        public AppDbContext Db { get; }

        public JobPositionService Service() => new(Db, new FixedTime(Now));

        public Task<int> SaveAsync() => Db.SaveChangesAsync();

        public JobPosition AddPosition(string code, string title, string? description, RecordStatus status = RecordStatus.Active)
        {
            var position = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = code,
                Title = title,
                Description = description,
                Status = status,
                LastSyncAt = Now,
                UpdatedAt = Now,
            };
            Db.JobPositions.Add(position);
            return position;
        }

        public void AddEmployee(string code, Guid positionId, EmployeeStatus status) =>
            Db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = code,
                FullName = code,
                Email = $"{code.ToLowerInvariant()}@test.local",
                DepartmentId = _department,
                JobPositionId = positionId,
                LastSyncAt = Now,
                UpdatedAt = Now,
                Status = status,
            });

        public EvaluationProfile AddProfile(
            Guid positionId, string code, string name, int version, DateOnly from, DateOnly? to, EvaluationProfileStatus status)
        {
            var profile = new EvaluationProfile
            {
                Id = Guid.NewGuid(),
                JobPositionId = positionId,
                Code = code,
                Name = name,
                Version = version,
                EffectiveFrom = from,
                EffectiveTo = to,
                Status = status,
                UpdatedAt = Now,
            };
            Db.EvaluationProfiles.Add(profile);
            _profiles[profile.Id] = profile;
            return profile;
        }

        public KpiGroup AddGroup(string code, string name)
        {
            var group = new KpiGroup { Id = Guid.NewGuid(), Code = code, Name = name, Status = KpiStatus.Active, UpdatedAt = Now };
            Db.KpiGroups.Add(group);
            return group;
        }

        public EvaluationProfileItem AddProfileItem(EvaluationProfile profile, KpiGroup group, decimal weight, int order)
        {
            var item = new EvaluationProfileItem
            {
                Id = Guid.NewGuid(),
                EvaluationProfileId = profile.Id,
                Name = group.Name,
                WeightPercent = weight,
                DisplayOrder = order,
                UpdatedAt = Now,
            };
            Db.EvaluationProfileItems.Add(item);
            return item;
        }

        /// <summary>A KPI leaf of <paramref name="catalogGroup"/> under the group node <paramref name="parent"/>.</summary>
        public void AddProfileLeaf(EvaluationProfileItem parent, KpiGroup catalogGroup, string kpiCode, int order)
        {
            var kpi = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = catalogGroup.Id,
                Code = kpiCode,
                Name = kpiCode,
                Unit = "unit",
                Status = KpiStatus.Active,
                UpdatedAt = Now,
            };
            Db.KpiDefinitions.Add(kpi);
            Db.EvaluationProfileItems.Add(new EvaluationProfileItem
            {
                Id = Guid.NewGuid(),
                EvaluationProfileId = parent.EvaluationProfileId,
                ParentItemId = parent.Id,
                KpiDefinitionId = kpi.Id,
                WeightPercent = 100m,
                DisplayOrder = order,
                UpdatedAt = Now,
            });
        }

        public WorkType AddWorkType(string code, string name, RecordStatus status, string? description = null)
        {
            var workType = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = description,
                Status = status,
                UpdatedAt = Now,
            };
            Db.WorkTypes.Add(workType);
            return workType;
        }

        public void Link(Guid positionId, WorkType workType) =>
            Db.JobPositionWorkTypes.Add(new JobPositionWorkType
            {
                Id = Guid.NewGuid(),
                JobPositionId = positionId,
                WorkTypeId = workType.Id,
                UpdatedAt = Now,
            });

        private KpiGroup? _kpiGroup;

        public void AddKpi(WorkType workType, string code, string name, string unit, bool required, DateOnly from, DateOnly? to)
        {
            _kpiGroup ??= AddGroup("KPI-G", "KPI group");
            var kpi = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = _kpiGroup.Id,
                Code = code,
                Name = name,
                Unit = unit,
                Status = KpiStatus.Active,
                UpdatedAt = Now,
            };
            Db.KpiDefinitions.Add(kpi);
            Db.WorkTypeKpis.Add(new WorkTypeKpi
            {
                Id = Guid.NewGuid(),
                WorkTypeId = workType.Id,
                KpiDefinitionId = kpi.Id,
                IsRequired = required,
                EffectiveFrom = from,
                EffectiveTo = to,
                UpdatedAt = Now,
            });
        }
    }
}
