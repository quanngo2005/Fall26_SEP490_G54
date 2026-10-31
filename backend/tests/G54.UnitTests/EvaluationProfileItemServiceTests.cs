using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.EvaluationProfile;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class EvaluationProfileItemServiceTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);

    private static (AppDbContext Db, EvaluationProfileService Service) CreateEnvironment()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        return (db, new EvaluationProfileService(db, NullLogger<EvaluationProfileService>.Instance));
    }

    private static JobPosition AddPosition(AppDbContext db)
    {
        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "TELLER",
            Title = "Teller",
            Status = RecordStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.JobPositions.Add(position);
        db.SaveChanges();
        return position;
    }

    /// <summary>A KPI of the catalog that a test can place as a leaf.</summary>
    private static KpiDefinition AddKpi(AppDbContext db, string code)
    {
        var catalog = db.KpiGroups.FirstOrDefault(g => g.Code == "CATALOG");
        if (catalog is null)
        {
            catalog = new KpiGroup
            {
                Id = Guid.NewGuid(),
                Code = "CATALOG",
                Name = "Catalog",
                Status = KpiStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.KpiGroups.Add(catalog);
        }

        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = catalog.Id,
            Code = code,
            Name = $"KPI {code}",
            Unit = "unit",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.KpiDefinitions.Add(kpi);
        db.SaveChanges();
        return kpi;
    }

    /// <summary>A root group with the weight, without children (a draft may hold it).</summary>
    private static EvaluationProfileItemInputDto Input(string name, decimal weight, int order = 1) =>
        new() { Name = name, WeightPercent = weight, DisplayOrder = order };

    private static AddEvaluationProfileItemDto AddGroupDto(string name, decimal weight, int order = 1, Guid? parent = null) =>
        new() { ParentItemId = parent, Name = name, WeightPercent = weight, DisplayOrder = order };

    private static AddEvaluationProfileItemDto AddLeafDto(KpiDefinition kpi, Guid parent, decimal weight, int order = 1) =>
        new() { ParentItemId = parent, KpiDefinitionId = kpi.Id, WeightPercent = weight, DisplayOrder = order };

    private static Task<Guid> RootIdAsync(AppDbContext db, string name) =>
        db.EvaluationProfileItems.Where(i => i.Name == name).Select(i => i.Id).SingleAsync();
    private static async Task<EvaluationProfileDto> CreateDraftAsync(
        EvaluationProfileService service,
        JobPosition position,
        params EvaluationProfileItemInputDto[] items) =>
        await service.CreateProfileAsync(new CreateEvaluationProfileDto
        {
            Code = "TELLER",
            Name = "Teller profile",
            JobPositionId = position.Id,
            EffectiveFrom = Jan1,
            Items = [.. items],
        });

    private static void MarkInUse(AppDbContext db, Guid profileId)
    {
        db.PerformanceEvaluations.Add(new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            EvaluationPeriodId = Guid.NewGuid(),
            EvaluationProfileId = profileId,
            Status = PerformanceEvaluationStatus.Calculated,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    private static async Task SetStatusAsync(AppDbContext db, Guid profileId, EvaluationProfileStatus status)
    {
        var profile = await db.EvaluationProfiles.SingleAsync(p => p.Id == profileId);
        profile.Status = status;
        await db.SaveChangesAsync();
    }

    // ----- GetItems -------------------------------------------------------------------------

    [Fact]
    public async Task GetItems_ReturnsTheRootsOrderedByDisplayOrderWithTheirChildren()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var kpi = AddKpi(db, "KPI_A");
            var profile = await CreateDraftAsync(
                service,
                AddPosition(db),
                Input("Second", 40, 2) with { Children = [new() { KpiDefinitionId = kpi.Id, WeightPercent = 100, DisplayOrder = 1 }] },
                Input("First", 60, 1));

            var items = await service.GetItemsAsync(profile.Id);

            Assert.NotNull(items);
            Assert.Equal(["First", "Second"], items.Select(i => i.Name));
            Assert.Empty(items[0].Children);
            var leaf = Assert.Single(items[1].Children);
            Assert.Equal(kpi.Id, leaf.KpiDefinitionId);
            Assert.Equal("KPI_A", leaf.KpiCode);
            Assert.Equal(items[1].Id, leaf.ParentItemId);
        }
    }

    [Fact]
    public async Task GetItems_ForAProfileWithoutItems_ReturnsAnEmptyList()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db));

            var items = await service.GetItemsAsync(profile.Id);

            Assert.NotNull(items);
            Assert.Empty(items);
        }
    }

    [Fact]
    public async Task GetItems_ForAnUnknownProfile_ReturnsNull()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            Assert.Null(await service.GetItemsAsync(Guid.NewGuid()));
        }
    }

    // ----- AddItem --------------------------------------------------------------------------

    [Fact]
    public async Task AddItem_ARootGroup_StoresItAndTouchesTheProfile()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db));

            var item = await service.AddItemAsync(profile.Id, AddGroupDto("Service quality", 30, 2));

            Assert.Equal("Service quality", item.Name);
            Assert.Null(item.ParentItemId);
            Assert.Null(item.KpiDefinitionId);
            Assert.Equal(30m, item.WeightPercent);
            Assert.Equal(2, item.DisplayOrder);
            Assert.Equal(1, await db.EvaluationProfileItems.CountAsync(i => i.EvaluationProfileId == profile.Id));
            var storedProfile = await db.EvaluationProfiles.SingleAsync(p => p.Id == profile.Id);
            Assert.True(storedProfile.UpdatedAt > profile.UpdatedAt);
        }
    }

    [Fact]
    public async Task AddItem_AKpiLeafUnderAGroup_IsStoredWithItsParent()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var kpi = AddKpi(db, "KPI_A");
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("Group", 100));
            var groupId = await RootIdAsync(db, "Group");

            var leaf = await service.AddItemAsync(profile.Id, AddLeafDto(kpi, groupId, 100));

            Assert.Equal(groupId, leaf.ParentItemId);
            Assert.Equal(kpi.Id, leaf.KpiDefinitionId);
            Assert.Null(leaf.Name);
            Assert.Equal("KPI_A", leaf.KpiCode);
        }
    }

    [Fact]
    public async Task AddItem_ToAnInactiveProfile_IsAllowed()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db));
            await SetStatusAsync(db, profile.Id, EvaluationProfileStatus.Inactive);

            var item = await service.AddItemAsync(profile.Id, AddGroupDto("Operations", 40));

            Assert.Equal(40m, item.WeightPercent);
        }
    }

    [Fact]
    public async Task AddItem_DoesNotEnforceTheTotalWeight()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("A", 80, 1));

            await service.AddItemAsync(profile.Id, AddGroupDto("B", 80, 2));

            var total = await db.EvaluationProfileItems
                .Where(i => i.EvaluationProfileId == profile.Id)
                .SumAsync(i => i.WeightPercent);
            Assert.Equal(160m, total);
        }
    }

    [Fact]
    public async Task AddItem_AKpiAlreadyInTheProfile_IsAConflict()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var kpi = AddKpi(db, "KPI_A");
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("Group", 100));
            var groupId = await RootIdAsync(db, "Group");
            await service.AddItemAsync(profile.Id, AddLeafDto(kpi, groupId, 50));

            await Assert.ThrowsAsync<ConflictException>(() => service.AddItemAsync(profile.Id, AddLeafDto(kpi, groupId, 10, 2)));

            Assert.Equal(2, await db.EvaluationProfileItems.CountAsync());
        }
    }

    [Fact]
    public async Task AddItem_AGroupNameAlreadyUsedAmongTheSiblings_IsAConflict()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("Business", 50));

            await Assert.ThrowsAsync<ConflictException>(() => service.AddItemAsync(profile.Id, AddGroupDto("business", 10, 2)));
        }
    }

    [Fact]
    public async Task AddItem_ADisplayOrderAlreadyUsedAmongTheSiblings_IsAConflictAndStoresNothing()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("Business", 50, 2));

            var ex = await Assert.ThrowsAsync<ConflictException>(() => service.AddItemAsync(profile.Id, AddGroupDto("Quality", 10, 2)));

            Assert.Equal(409, ex.StatusCode);
            Assert.Equal(1, await db.EvaluationProfileItems.CountAsync());
        }
    }

    [Fact]
    public async Task AddItem_ADisplayOrderUsedOnlyUnderAnotherParent_IsAccepted()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var kpi = AddKpi(db, "KPI_A");
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("A", 50, 1), Input("B", 50, 2));
            var groupA = await RootIdAsync(db, "A");
            var groupB = await RootIdAsync(db, "B");
            await service.AddItemAsync(profile.Id, AddLeafDto(kpi, groupA, 100, 1));

            var added = await service.AddItemAsync(profile.Id, AddGroupDto("Sub", 100, 1, groupB));

            Assert.Equal(1, added.DisplayOrder);
        }
    }

    [Fact]
    public async Task AddItem_AnUnknownKpi_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("Group", 100));
            var groupId = await RootIdAsync(db, "Group");
            var unknown = new AddEvaluationProfileItemDto { ParentItemId = groupId, KpiDefinitionId = Guid.NewGuid(), WeightPercent = 10, DisplayOrder = 1 };

            await Assert.ThrowsAsync<NotFoundException>(() => service.AddItemAsync(profile.Id, unknown));
        }
    }

    [Fact]
    public async Task AddItem_UnderAnUnknownParent_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db));

            await Assert.ThrowsAsync<NotFoundException>(() =>
                service.AddItemAsync(profile.Id, AddGroupDto("Child", 10, 1, Guid.NewGuid())));
        }
    }

    [Fact]
    public async Task AddItem_UnderAKpiLeaf_IsAValidationError()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var kpi = AddKpi(db, "KPI_A");
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("Group", 100));
            var groupId = await RootIdAsync(db, "Group");
            var leaf = await service.AddItemAsync(profile.Id, AddLeafDto(kpi, groupId, 100));

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.AddItemAsync(profile.Id, AddGroupDto("Under leaf", 10, 1, leaf.Id)));
        }
    }

    [Fact]
    public async Task AddItem_AKpiAtTheRoot_IsAValidationError()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var kpi = AddKpi(db, "KPI_A");
            var profile = await CreateDraftAsync(service, AddPosition(db));
            var rootLeaf = new AddEvaluationProfileItemDto { KpiDefinitionId = kpi.Id, WeightPercent = 100, DisplayOrder = 1 };

            await Assert.ThrowsAsync<ValidationException>(() => service.AddItemAsync(profile.Id, rootLeaf));
        }
    }

    [Fact]
    public async Task AddItem_BeyondThreeLevels_IsAValidationError()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("L1", 100));
            var l1 = await RootIdAsync(db, "L1");
            var l2 = await service.AddItemAsync(profile.Id, AddGroupDto("L2", 100, 1, l1));
            var l3 = await service.AddItemAsync(profile.Id, AddGroupDto("L3", 100, 1, l2.Id));

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.AddItemAsync(profile.Id, AddGroupDto("L4", 100, 1, l3.Id)));
        }
    }

    [Fact]
    public async Task AddItem_ToAnUnknownProfile_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            await Assert.ThrowsAsync<NotFoundException>(() => service.AddItemAsync(Guid.NewGuid(), AddGroupDto("A", 10)));
        }
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    [InlineData("100.0001", 1)]
    [InlineData("10.12345", 1)]
    [InlineData("10", 0)]
    public async Task AddItem_WithInvalidValues_IsAValidationErrorAndStoresNothing(string weight, int order)
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db));
            var dto = AddGroupDto("A", decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture), order);

            await Assert.ThrowsAsync<ValidationException>(() => service.AddItemAsync(profile.Id, dto));

            Assert.Empty(db.EvaluationProfileItems);
        }
    }

    [Fact]
    public async Task AddItem_WithBothANameAndAKpi_Or_Neither_IsAValidationError()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var kpi = AddKpi(db, "KPI_A");
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("Group", 100));
            var groupId = await RootIdAsync(db, "Group");
            var both = new AddEvaluationProfileItemDto { ParentItemId = groupId, Name = "X", KpiDefinitionId = kpi.Id, WeightPercent = 10, DisplayOrder = 1 };
            var neither = new AddEvaluationProfileItemDto { ParentItemId = groupId, WeightPercent = 10, DisplayOrder = 1 };

            await Assert.ThrowsAsync<ValidationException>(() => service.AddItemAsync(profile.Id, both));
            await Assert.ThrowsAsync<ValidationException>(() => service.AddItemAsync(profile.Id, neither));
        }
    }

    [Fact]
    public async Task AddItem_WithAnEmptyKpiId_ReportsTheEmptyIdWhetherOrNotItHasAName()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("Group", 100));
            var groupId = await RootIdAsync(db, "Group");
            var withoutName = new AddEvaluationProfileItemDto { ParentItemId = groupId, KpiDefinitionId = Guid.Empty, WeightPercent = 10, DisplayOrder = 1 };
            var withName = withoutName with { Name = "X" };

            var first = await Assert.ThrowsAsync<ValidationException>(() => service.AddItemAsync(profile.Id, withoutName));
            var second = await Assert.ThrowsAsync<ValidationException>(() => service.AddItemAsync(profile.Id, withName));

            Assert.Contains("KpiDefinitionId", first.Message);
            Assert.Contains("KpiDefinitionId", second.Message);
        }
    }

    [Fact]
    public async Task AddItem_WithBothANameAndAKpi_Or_Neither_ReportsTheGroupOrLeafRule()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var kpi = AddKpi(db, "KPI_A");
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("Group", 100));
            var groupId = await RootIdAsync(db, "Group");
            var both = new AddEvaluationProfileItemDto { ParentItemId = groupId, Name = "X", KpiDefinitionId = kpi.Id, WeightPercent = 10, DisplayOrder = 1 };
            var neither = new AddEvaluationProfileItemDto { ParentItemId = groupId, WeightPercent = 10, DisplayOrder = 1 };

            var first = await Assert.ThrowsAsync<ValidationException>(() => service.AddItemAsync(profile.Id, both));
            var second = await Assert.ThrowsAsync<ValidationException>(() => service.AddItemAsync(profile.Id, neither));

            Assert.DoesNotContain("không được để trống", first.Message);
            Assert.Equal(first.Message, second.Message);
        }
    }

    // ----- UpdateItem -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateItem_ChangesTheWeightAndOrder()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("A", 50, 1));
            var id = await RootIdAsync(db, "A");

            var item = await service.UpdateItemAsync(
                profile.Id,
                id,
                new UpdateEvaluationProfileItemDto { WeightPercent = 35.5m, DisplayOrder = 3 });

            Assert.Equal(35.5m, item.WeightPercent);
            Assert.Equal(3, item.DisplayOrder);
            var stored = await db.EvaluationProfileItems.SingleAsync();
            Assert.Equal(35.5m, stored.WeightPercent);
            Assert.Equal(3, stored.DisplayOrder);
        }
    }

    [Fact]
    public async Task UpdateItem_ToADisplayOrderUsedByASibling_IsAConflictAndChangesNothing()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("A", 60, 1), Input("B", 40, 2));
            var id = await RootIdAsync(db, "A");

            var ex = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateItemAsync(
                profile.Id, id, new UpdateEvaluationProfileItemDto { WeightPercent = 10, DisplayOrder = 2 }));

            Assert.Equal(409, ex.StatusCode);
            var stored = await db.EvaluationProfileItems.AsNoTracking().SingleAsync(i => i.Id == id);
            Assert.Equal(1, stored.DisplayOrder);
            Assert.Equal(60m, stored.WeightPercent);
        }
    }

    [Fact]
    public async Task UpdateItem_KeepingItsOwnDisplayOrder_IsAcceptedEvenWhenASiblingAlreadySharesIt()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("A", 60, 1), Input("B", 40, 2));
            var id = await RootIdAsync(db, "A");
            (await db.EvaluationProfileItems.SingleAsync(i => i.Name == "B")).DisplayOrder = 1;
            await db.SaveChangesAsync();

            var item = await service.UpdateItemAsync(
                profile.Id, id, new UpdateEvaluationProfileItemDto { WeightPercent = 55, DisplayOrder = 1 });

            Assert.Equal(55m, item.WeightPercent);
        }
    }

    [Fact]
    public async Task UpdateItem_RenamesAGroup_ButNotAKpiLeaf()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var kpi = AddKpi(db, "KPI_A");
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("A", 100));
            var groupId = await RootIdAsync(db, "A");
            var leaf = await service.AddItemAsync(profile.Id, AddLeafDto(kpi, groupId, 100));

            var renamed = await service.UpdateItemAsync(
                profile.Id, groupId, new UpdateEvaluationProfileItemDto { Name = "  Renamed ", WeightPercent = 100, DisplayOrder = 1 });

            Assert.Equal("Renamed", renamed.Name);
            await Assert.ThrowsAsync<ValidationException>(() => service.UpdateItemAsync(
                profile.Id, leaf.Id, new UpdateEvaluationProfileItemDto { Name = "Nope", WeightPercent = 100, DisplayOrder = 1 }));
        }
    }

    [Fact]
    public async Task UpdateItem_ForANodeNotInTheProfile_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db));

            await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateItemAsync(
                profile.Id,
                Guid.NewGuid(),
                new UpdateEvaluationProfileItemDto { WeightPercent = 10, DisplayOrder = 1 }));
        }
    }

    [Fact]
    public async Task UpdateItem_WithInvalidValues_IsAValidationErrorAndKeepsTheItem()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("A", 50, 1));
            var id = await RootIdAsync(db, "A");

            await Assert.ThrowsAsync<ValidationException>(() => service.UpdateItemAsync(
                profile.Id,
                id,
                new UpdateEvaluationProfileItemDto { WeightPercent = 0, DisplayOrder = 1 }));

            Assert.Equal(50m, (await db.EvaluationProfileItems.SingleAsync()).WeightPercent);
        }
    }

    // ----- RemoveItem -----------------------------------------------------------------------

    [Fact]
    public async Task RemoveItem_DeletesOnlyTheRequestedNode()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("A", 60, 1), Input("B", 40, 2));
            var aId = await RootIdAsync(db, "A");

            await service.RemoveItemAsync(profile.Id, aId);

            Assert.Equal("B", (await db.EvaluationProfileItems.SingleAsync()).Name);
        }
    }

    [Fact]
    public async Task RemoveItem_AGroupTakesItsWholeSubtreeWithIt()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var kpi = AddKpi(db, "KPI_A");
            var profile = await CreateDraftAsync(service, AddPosition(db), Input("A", 60, 1), Input("B", 40, 2));
            var aId = await RootIdAsync(db, "A");
            var sub = await service.AddItemAsync(profile.Id, AddGroupDto("Sub", 100, 1, aId));
            await service.AddItemAsync(profile.Id, AddLeafDto(kpi, sub.Id, 100));

            await service.RemoveItemAsync(profile.Id, aId);

            Assert.Equal("B", (await db.EvaluationProfileItems.SingleAsync()).Name);
        }
    }

    [Fact]
    public async Task RemoveItem_ForANodeNotInTheProfile_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db));

            await Assert.ThrowsAsync<NotFoundException>(() => service.RemoveItemAsync(profile.Id, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task RemoveItem_FromAnUnknownProfile_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            await Assert.ThrowsAsync<NotFoundException>(() => service.RemoveItemAsync(Guid.NewGuid(), Guid.NewGuid()));
        }
    }

    // ----- Profile state guards (add, update and remove share them) -------------------------

    private static async Task<(AppDbContext Db, EvaluationProfileService Service, EvaluationProfileDto Profile, Guid ItemId)>
        ProfileWithOneItemAsync()
    {
        var (db, service) = CreateEnvironment();
        var position = AddPosition(db);
        var profile = await CreateDraftAsync(service, position, Input("A", 100, 1));
        return (db, service, profile, await RootIdAsync(db, "A"));
    }

    [Theory]
    [InlineData(EvaluationProfileStatus.Active)]
    [InlineData(EvaluationProfileStatus.Archived)]
    public async Task EveryWrite_OnAnActiveOrArchivedProfile_IsAGenericConflictAndChangesNothing(EvaluationProfileStatus status)
    {
        var (db, service, profile, itemId) = await ProfileWithOneItemAsync();
        using (db)
        {
            await SetStatusAsync(db, profile.Id, status);

            var add = await Assert.ThrowsAsync<ConflictException>(() => service.AddItemAsync(profile.Id, AddGroupDto("Spare", 10, 2)));
            var update = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateItemAsync(
                profile.Id, itemId, new UpdateEvaluationProfileItemDto { WeightPercent = 50, DisplayOrder = 1 }));
            var remove = await Assert.ThrowsAsync<ConflictException>(() => service.RemoveItemAsync(profile.Id, itemId));

            Assert.All([add, update, remove], e => Assert.Equal(ErrorCodes.Conflict, e.ErrorCode));
            var stored = await db.EvaluationProfileItems.SingleAsync();
            Assert.Equal(100m, stored.WeightPercent);
        }
    }

    [Fact]
    public async Task EveryWrite_OnAProfileInUse_IsAConflictWithTheInUseCode()
    {
        var (db, service, profile, itemId) = await ProfileWithOneItemAsync();
        using (db)
        {
            MarkInUse(db, profile.Id);

            var add = await Assert.ThrowsAsync<ConflictException>(() => service.AddItemAsync(profile.Id, AddGroupDto("Spare", 10, 2)));
            var update = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateItemAsync(
                profile.Id, itemId, new UpdateEvaluationProfileItemDto { WeightPercent = 50, DisplayOrder = 1 }));
            var remove = await Assert.ThrowsAsync<ConflictException>(() => service.RemoveItemAsync(profile.Id, itemId));

            Assert.All([add, update, remove], e => Assert.Equal(ErrorCodes.EvaluationProfileInUse, e.ErrorCode));
            Assert.Equal(1, await db.EvaluationProfileItems.CountAsync());
        }
    }

    // ----- Behaviour kept from the shared validation ----------------------------------------

    [Fact]
    public async Task ProfileCreate_StillRejectsAnInvalidItem_AfterTheValidationWasShared()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);

            await Assert.ThrowsAsync<ValidationException>(() => CreateDraftAsync(service, position, Input("A", 0, 1)));
        }
    }
}
