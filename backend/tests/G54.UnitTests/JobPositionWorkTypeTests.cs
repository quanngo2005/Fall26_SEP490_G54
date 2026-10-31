using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.JobPosition;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace G54.UnitTests;

/// <summary>
/// The job position to work type link (<c>job_position_work_type</c>) as read by <see cref="JobPositionService"/>:
/// which work types and KPIs a position shows. Complements <see cref="JobPositionServiceTests"/>.
/// </summary>
public sealed class JobPositionWorkTypeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 18);

    [Fact]
    public async Task DetailListsOnlyActiveWorkTypesOrderedByCode()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        world.Link(teller, world.AddWorkType("C_LAST", RecordStatus.Active));
        world.Link(teller, world.AddWorkType("A_FIRST", RecordStatus.Active));
        world.Link(teller, world.AddWorkType("B_RETIRED", RecordStatus.Inactive));
        await world.SaveAsync();

        var detail = await world.Service().GetJobPositionByIdAsync(teller.Id);

        Assert.NotNull(detail);
        Assert.Equal(["A_FIRST", "C_LAST"], detail.WorkTypes.Select(w => w.Code));
    }

    [Fact]
    public async Task DetailKeepsEachPositionsLinksSeparateWhileSharingOneWorkType()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        var sales = world.AddPosition("SALES");
        var shared = world.AddWorkType("SHARED", RecordStatus.Active);
        var tellerOnly = world.AddWorkType("TELLER_ONLY", RecordStatus.Active);
        world.Link(teller, shared);
        world.Link(teller, tellerOnly);
        world.Link(sales, shared);
        await world.SaveAsync();
        var service = world.Service();

        var tellerDetail = await service.GetJobPositionByIdAsync(teller.Id);
        var salesDetail = await service.GetJobPositionByIdAsync(sales.Id);

        Assert.Equal(["SHARED", "TELLER_ONLY"], tellerDetail!.WorkTypes.Select(w => w.Code));
        Assert.Equal(["SHARED"], salesDetail!.WorkTypes.Select(w => w.Code));
    }

    [Fact]
    public async Task GetWorkTypesListsOnlyThisPositionsActiveWorkTypesOrderedByCodeWithTheirCurrentKpis()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        var sales = world.AddPosition("SALES");
        var last = world.AddWorkType("C_LAST", RecordStatus.Active);
        var first = world.AddWorkType("A_FIRST", RecordStatus.Active);
        var retired = world.AddWorkType("B_RETIRED", RecordStatus.Inactive);
        var salesOnly = world.AddWorkType("D_SALES", RecordStatus.Active);
        world.Link(teller, last);
        world.Link(teller, first);
        world.Link(teller, retired);
        world.Link(sales, salesOnly);
        world.MapKpi(first, world.AddKpi("KH_MOI", "Khach hang moi"), true, Today.AddDays(-1), null);
        await world.SaveAsync();

        var workTypes = await world.Service().GetWorkTypesAsync(teller.Id);

        Assert.Equal(["A_FIRST", "C_LAST"], workTypes.Select(w => w.Code));
        Assert.Equal(1, workTypes[0].KpiCount);
        Assert.Equal("KH_MOI", Assert.Single(workTypes[0].Kpis).Code);
        Assert.Empty(workTypes[1].Kpis);
    }

    [Fact]
    public async Task GetWorkTypesReturnsAnEmptyListForAPositionWithoutLinksAndMatchesTheDetail()
    {
        var world = new World();
        var bare = world.AddPosition("BARE");
        var teller = world.AddPosition("TELLER");
        world.Link(teller, world.AddWorkType("MO_TK", RecordStatus.Active));
        await world.SaveAsync();
        var service = world.Service();

        Assert.Empty(await service.GetWorkTypesAsync(bare.Id));
        var list = await service.GetWorkTypesAsync(teller.Id);
        var detail = await service.GetJobPositionByIdAsync(teller.Id);
        Assert.Equal(detail!.WorkTypes.Select(w => w.Code), list.Select(w => w.Code));
    }

    [Fact]
    public async Task GetWorkTypesRejectsAnUnknownPositionAndTheDevelopmentBootstrapPosition()
    {
        var world = new World();
        var bootstrap = world.AddPosition("DEV_ADMIN");
        world.Link(bootstrap, world.AddWorkType("MO_TK", RecordStatus.Active));
        await world.SaveAsync();
        var service = world.Service();

        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => service.GetWorkTypesAsync(Guid.NewGuid()));
        var bootstrapError = await Assert.ThrowsAsync<NotFoundException>(() => service.GetWorkTypesAsync(bootstrap.Id));

        Assert.Equal(404, unknown.StatusCode);
        Assert.Equal(404, bootstrapError.StatusCode);
    }

    [Fact]
    public async Task AssignWorkTypeLinksItAndReturnsTheUpdatedDetail()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        var workType = world.AddWorkType("MO_TK", RecordStatus.Active);
        await world.SaveAsync();

        var detail = await world.Service().AssignWorkTypeAsync(teller.Id, new AssignJobPositionWorkTypeDto(workType.Id));

        Assert.Equal(["MO_TK"], detail.WorkTypes.Select(w => w.Code));
    }

    [Fact]
    public async Task AssignWorkTypeRejectsAnUnknownPositionAndTheDevelopmentBootstrapPosition()
    {
        var world = new World();
        var bootstrap = world.AddPosition("DEV_ADMIN");
        var workType = world.AddWorkType("MO_TK", RecordStatus.Active);
        await world.SaveAsync();
        var service = world.Service();
        var request = new AssignJobPositionWorkTypeDto(workType.Id);

        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => service.AssignWorkTypeAsync(Guid.NewGuid(), request));
        var bootstrapError = await Assert.ThrowsAsync<NotFoundException>(() => service.AssignWorkTypeAsync(bootstrap.Id, request));

        Assert.Equal(404, unknown.StatusCode);
        Assert.Equal(404, bootstrapError.StatusCode);
    }

    [Fact]
    public async Task AssignWorkTypeRejectsAMissingOrInactiveWorkType()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        var inactive = world.AddWorkType("OLD", RecordStatus.Inactive);
        await world.SaveAsync();
        var service = world.Service();

        var empty = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AssignWorkTypeAsync(teller.Id, new AssignJobPositionWorkTypeDto(Guid.Empty)));
        var unknown = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AssignWorkTypeAsync(teller.Id, new AssignJobPositionWorkTypeDto(Guid.NewGuid())));
        var retired = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AssignWorkTypeAsync(teller.Id, new AssignJobPositionWorkTypeDto(inactive.Id)));

        Assert.All([empty, unknown, retired], error => Assert.Equal(400, error.StatusCode));
    }

    [Fact]
    public async Task AssignWorkTypeRejectsAnAlreadyLinkedWorkTypeWithConflict()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        var workType = world.AddWorkType("MO_TK", RecordStatus.Active);
        world.Link(teller, workType);
        await world.SaveAsync();

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            world.Service().AssignWorkTypeAsync(teller.Id, new AssignJobPositionWorkTypeDto(workType.Id)));

        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task RemoveWorkTypeUnlinksOnlyThatPairAndReturnsTheUpdatedDetail()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        var sales = world.AddPosition("SALES");
        var shared = world.AddWorkType("SHARED", RecordStatus.Active);
        var other = world.AddWorkType("OTHER", RecordStatus.Active);
        world.Link(teller, shared);
        world.Link(teller, other);
        world.Link(sales, shared);
        await world.SaveAsync();
        var service = world.Service();

        var detail = await service.RemoveWorkTypeAsync(teller.Id, shared.Id);

        Assert.Equal(["OTHER"], detail.WorkTypes.Select(w => w.Code));
        Assert.Equal(["SHARED"], (await service.GetJobPositionByIdAsync(sales.Id))!.WorkTypes.Select(w => w.Code));
    }

    [Fact]
    public async Task RemoveWorkTypeStillUnlinksAnInactiveWorkType()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        var retired = world.AddWorkType("OLD", RecordStatus.Inactive);
        world.Link(teller, retired);
        await world.SaveAsync();

        await world.Service().RemoveWorkTypeAsync(teller.Id, retired.Id);

        Assert.Empty(await world.LinksAsync());
    }

    [Fact]
    public async Task RemoveWorkTypeRejectsAnUnknownPositionAndTheDevelopmentBootstrapPosition()
    {
        var world = new World();
        var bootstrap = world.AddPosition("DEV_ADMIN");
        var workType = world.AddWorkType("MO_TK", RecordStatus.Active);
        world.Link(bootstrap, workType);
        await world.SaveAsync();
        var service = world.Service();

        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => service.RemoveWorkTypeAsync(Guid.NewGuid(), workType.Id));
        var bootstrapError = await Assert.ThrowsAsync<NotFoundException>(() => service.RemoveWorkTypeAsync(bootstrap.Id, workType.Id));

        Assert.Equal(404, unknown.StatusCode);
        Assert.Equal(404, bootstrapError.StatusCode);
        Assert.Single(await world.LinksAsync());
    }

    [Fact]
    public async Task RemoveWorkTypeRejectsAWorkTypeThatIsNotAssignedWith404()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        var sales = world.AddPosition("SALES");
        var workType = world.AddWorkType("MO_TK", RecordStatus.Active);
        world.Link(sales, workType);
        await world.SaveAsync();
        var service = world.Service();

        var notAssigned = await Assert.ThrowsAsync<NotFoundException>(() => service.RemoveWorkTypeAsync(teller.Id, workType.Id));
        var unknownWorkType = await Assert.ThrowsAsync<NotFoundException>(() => service.RemoveWorkTypeAsync(teller.Id, Guid.NewGuid()));

        Assert.Equal(404, notAssigned.StatusCode);
        Assert.Equal(404, unknownWorkType.StatusCode);
        Assert.Single(await world.LinksAsync());
    }

    [Fact]
    public async Task DetailShowsAWorkTypeWithoutAnyCurrentKpiAsEmpty()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        var bare = world.AddWorkType("BARE", RecordStatus.Active);
        var expiredOnly = world.AddWorkType("EXPIRED_ONLY", RecordStatus.Active);
        world.Link(teller, bare);
        world.Link(teller, expiredOnly);
        world.MapKpi(expiredOnly, world.AddKpi("OLD", "Cũ"), true, Today.AddDays(-30), Today.AddDays(-1));
        await world.SaveAsync();

        var detail = await world.Service().GetJobPositionByIdAsync(teller.Id);

        Assert.Equal(2, detail!.WorkTypes.Count);
        Assert.All(detail.WorkTypes, workType =>
        {
            Assert.Equal(0, workType.KpiCount);
            Assert.Empty(workType.Kpis);
        });
    }

    [Fact]
    public async Task DetailKpiWindowIncludesTheBoundaryDaysAndExcludesFutureAndExpiredMappings()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        var workType = world.AddWorkType("MO_TK", RecordStatus.Active);
        world.Link(teller, workType);
        world.MapKpi(workType, world.AddKpi("STARTS_TODAY", "Bắt đầu hôm nay"), false, Today, null);
        world.MapKpi(workType, world.AddKpi("ENDS_TODAY", "Hết hạn hôm nay"), true, Today.AddDays(-10), Today);
        world.MapKpi(workType, world.AddKpi("STARTS_TOMORROW", "Bắt đầu ngày mai"), false, Today.AddDays(1), null);
        world.MapKpi(workType, world.AddKpi("ENDED_YESTERDAY", "Hết hạn hôm qua"), false, Today.AddDays(-10), Today.AddDays(-1));
        await world.SaveAsync();

        var detail = await world.Service().GetJobPositionByIdAsync(teller.Id);

        var shown = Assert.Single(detail!.WorkTypes);
        Assert.Equal(2, shown.KpiCount);
        Assert.Equal(["ENDS_TODAY", "STARTS_TODAY"], shown.Kpis.Select(k => k.Code)); // ordered by KPI code
        Assert.True(shown.Kpis.Single(k => k.Code == "ENDS_TODAY").IsRequired);
        Assert.False(shown.Kpis.Single(k => k.Code == "STARTS_TODAY").IsRequired);
    }

    [Fact]
    public async Task ListCountsEachPositionsActiveWorkTypesAndNamesEverySharedKpiOnce()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        var sales = world.AddPosition("SALES");
        var accounts = world.AddWorkType("MO_TK", RecordStatus.Active);
        var cards = world.AddWorkType("MO_THE", RecordStatus.Active);
        var retired = world.AddWorkType("OLD", RecordStatus.Inactive);
        var csat = world.AddKpi("CSAT", "Điểm hài lòng");
        var newCustomers = world.AddKpi("KH_MOI", "Khách hàng mới");
        world.Link(teller, accounts);
        world.Link(teller, cards);
        world.Link(teller, retired);
        world.Link(sales, cards);
        world.MapKpi(accounts, newCustomers, true, Today, null);
        world.MapKpi(accounts, csat, false, Today, null);
        world.MapKpi(cards, csat, false, Today, null); // same KPI reached through a second work type
        await world.SaveAsync();

        var result = await world.Service().GetJobPositionsAsync(new JobPositionFilterDto());

        var tellerRow = result.Items.Single(i => i.Code == "TELLER");
        Assert.Equal(2, tellerRow.WorkTypeCount); // the inactive link is not counted
        Assert.Equal(["Điểm hài lòng", "Khách hàng mới"], tellerRow.KpiItems); // distinct and sorted
        var salesRow = result.Items.Single(i => i.Code == "SALES");
        Assert.Equal(1, salesRow.WorkTypeCount);
        Assert.Equal(["Điểm hài lòng"], salesRow.KpiItems);
    }

    [Fact]
    public async Task ListSkipsAKpiNameThatOnlyAnInactiveWorkTypeCarries()
    {
        var world = new World();
        var teller = world.AddPosition("TELLER");
        var retired = world.AddWorkType("OLD", RecordStatus.Inactive);
        world.Link(teller, retired);
        world.MapKpi(retired, world.AddKpi("HIDDEN", "KPI ẩn"), false, Today, null);
        await world.SaveAsync();

        var row = Assert.Single((await world.Service().GetJobPositionsAsync(new JobPositionFilterDto())).Items);

        Assert.Equal(0, row.WorkTypeCount);
        Assert.Empty(row.KpiItems);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class World
    {
        private readonly KpiGroup _group;

        public World()
        {
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
            _group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "Group", Status = KpiStatus.Active, UpdatedAt = Now };
            Db.KpiGroups.Add(_group);
        }

        private AppDbContext Db { get; }

        public JobPositionService Service() => new(Db, new FixedTime(Now));

        public Task<int> SaveAsync() => Db.SaveChangesAsync();

        public Task<List<JobPositionWorkType>> LinksAsync() => Db.JobPositionWorkTypes.AsNoTracking().ToListAsync();

        public JobPosition AddPosition(string code)
        {
            var position = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = code,
                Title = code,
                Status = RecordStatus.Active,
                LastSyncAt = Now,
                UpdatedAt = Now,
            };
            Db.JobPositions.Add(position);
            return position;
        }

        public WorkType AddWorkType(string code, RecordStatus status)
        {
            var workType = new WorkType { Id = Guid.NewGuid(), Code = code, Name = code, Status = status, UpdatedAt = Now };
            Db.WorkTypes.Add(workType);
            return workType;
        }

        public void Link(JobPosition position, WorkType workType) =>
            Db.JobPositionWorkTypes.Add(new JobPositionWorkType
            {
                Id = Guid.NewGuid(),
                JobPositionId = position.Id,
                WorkTypeId = workType.Id,
                UpdatedAt = Now,
            });

        public KpiDefinition AddKpi(string code, string name)
        {
            var kpi = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = _group.Id,
                Code = code,
                Name = name,
                Unit = "đơn vị",
                Status = KpiStatus.Active,
                UpdatedAt = Now,
            };
            Db.KpiDefinitions.Add(kpi);
            return kpi;
        }

        public void MapKpi(WorkType workType, KpiDefinition kpi, bool required, DateOnly from, DateOnly? to) =>
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

/// <summary>Database rules of <c>job_position_work_type</c>, which the in-memory provider does not enforce.</summary>
public sealed class JobPositionWorkTypePostgresTests : IAsyncLifetime
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    private string _database = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_jpwt_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddDataAccess(new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString);
        // Each test owns a unique data source; do not retain its EF provider in the process-wide cache.
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.AddScoped(provider => new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>())
            .EnableServiceProviderCaching(false).Options);
        _provider = services.BuildServiceProvider();
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is null)
        {
            return;
        }

        await _provider.DisposeAsync();
        await using var connection = new NpgsqlConnection(TestPostgres.AdminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task ADuplicatePositionAndWorkTypePairIsRejectedButOtherPairsAreAccepted()
    {
        var (positionA, positionB, workTypeA, workTypeB) = await SeedAsync();
        await LinkAsync(positionA, workTypeA);

        await LinkAsync(positionA, workTypeB); // same position, another work type
        await LinkAsync(positionB, workTypeA); // another position, same work type
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => LinkAsync(positionA, workTypeA));

        Assert.Equal(UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, await db.JobPositionWorkTypes.CountAsync());
    }

    [PostgresFact]
    public async Task ALinkToAnUnknownPositionOrWorkTypeIsRejectedByTheForeignKeys()
    {
        var (positionA, _, workTypeA, _) = await SeedAsync();

        var unknownWorkType = await Assert.ThrowsAsync<DbUpdateException>(() => LinkAsync(positionA, Guid.NewGuid()));
        var unknownPosition = await Assert.ThrowsAsync<DbUpdateException>(() => LinkAsync(Guid.NewGuid(), workTypeA));

        Assert.Equal(ForeignKeyViolation, Assert.IsType<PostgresException>(unknownWorkType.InnerException).SqlState);
        Assert.Equal(ForeignKeyViolation, Assert.IsType<PostgresException>(unknownPosition.InnerException).SqlState);
    }

    private async Task LinkAsync(Guid positionId, Guid workTypeId)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.JobPositionWorkTypes.Add(new JobPositionWorkType
        {
            Id = Guid.NewGuid(),
            JobPositionId = positionId,
            WorkTypeId = workTypeId,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<(Guid PositionA, Guid PositionB, Guid WorkTypeA, Guid WorkTypeB)> SeedAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        JobPosition Position(string code) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = code,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        WorkType WorkType(string code) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            Status = RecordStatus.Active,
            UpdatedAt = now,
        };

        var a = Position("POS_A");
        var b = Position("POS_B");
        var x = WorkType("WT_A");
        var y = WorkType("WT_B");
        db.JobPositions.AddRange(a, b);
        db.WorkTypes.AddRange(x, y);
        await db.SaveChangesAsync();
        return (a.Id, b.Id, x.Id, y.Id);
    }
}
