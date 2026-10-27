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

    private static KpiGroup AddGroup(AppDbContext db, string code)
    {
        var group = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Group {code}",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.KpiGroups.Add(group);
        db.SaveChanges();
        return group;
    }

    private static EvaluationProfileItemInputDto Input(KpiGroup group, decimal weight, int order = 1) =>
        new() { KpiGroupId = group.Id, WeightPercent = weight, DisplayOrder = order };

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
    public async Task GetItems_ReturnsTheItemsOrderedByDisplayOrderThenGroupCode()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A_GROUP");
            var b = AddGroup(db, "B_GROUP");
            var c = AddGroup(db, "C_GROUP");
            var profile = await CreateDraftAsync(service, position, Input(c, 20, 2), Input(b, 30, 1), Input(a, 50, 2));

            var items = await service.GetItemsAsync(profile.Id);

            Assert.NotNull(items);
            Assert.Equal(["B_GROUP", "A_GROUP", "C_GROUP"], items.Select(i => i.KpiGroupCode));
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
    public async Task AddItem_ToADraftProfile_StoresTheItemAndTouchesTheProfile()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var group = AddGroup(db, "SERVICE_QUALITY");
            var profile = await CreateDraftAsync(service, position);

            var item = await service.AddItemAsync(profile.Id, Input(group, 30, 2));

            Assert.Equal(group.Id, item.KpiGroupId);
            Assert.Equal("SERVICE_QUALITY", item.KpiGroupCode);
            Assert.Equal(30m, item.WeightPercent);
            Assert.Equal(2, item.DisplayOrder);
            var stored = await db.EvaluationProfileItems.SingleAsync(i => i.EvaluationProfileId == profile.Id);
            Assert.Equal(group.Id, stored.KpiGroupId);
            var storedProfile = await db.EvaluationProfiles.SingleAsync(p => p.Id == profile.Id);
            Assert.True(storedProfile.UpdatedAt > profile.UpdatedAt);
        }
    }

    [Fact]
    public async Task AddItem_ToAnInactiveProfile_IsAllowed()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var group = AddGroup(db, "OPERATIONS");
            var profile = await CreateDraftAsync(service, position);
            await SetStatusAsync(db, profile.Id, EvaluationProfileStatus.Inactive);

            var item = await service.AddItemAsync(profile.Id, Input(group, 40));

            Assert.Equal(40m, item.WeightPercent);
        }
    }

    [Fact]
    public async Task AddItem_DoesNotEnforceTheTotalWeight()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A_GROUP");
            var b = AddGroup(db, "B_GROUP");
            var profile = await CreateDraftAsync(service, position, Input(a, 80, 1));

            await service.AddItemAsync(profile.Id, Input(b, 80, 2));

            var total = await db.EvaluationProfileItems
                .Where(i => i.EvaluationProfileId == profile.Id)
                .SumAsync(i => i.WeightPercent);
            Assert.Equal(160m, total);
        }
    }

    [Fact]
    public async Task AddItem_WithAGroupAlreadyInTheProfile_IsAConflict()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var group = AddGroup(db, "A_GROUP");
            var profile = await CreateDraftAsync(service, position, Input(group, 50));

            await Assert.ThrowsAsync<ConflictException>(() => service.AddItemAsync(profile.Id, Input(group, 10)));

            Assert.Equal(1, await db.EvaluationProfileItems.CountAsync());
        }
    }

    [Fact]
    public async Task AddItem_WithAnUnknownGroup_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db));
            var unknown = new EvaluationProfileItemInputDto { KpiGroupId = Guid.NewGuid(), WeightPercent = 10, DisplayOrder = 1 };

            await Assert.ThrowsAsync<NotFoundException>(() => service.AddItemAsync(profile.Id, unknown));
        }
    }

    [Fact]
    public async Task AddItem_ToAnUnknownProfile_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var group = AddGroup(db, "A_GROUP");

            await Assert.ThrowsAsync<NotFoundException>(() => service.AddItemAsync(Guid.NewGuid(), Input(group, 10)));
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
            var group = AddGroup(db, "A_GROUP");
            var profile = await CreateDraftAsync(service, AddPosition(db));
            var dto = Input(group, decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture), order);

            await Assert.ThrowsAsync<ValidationException>(() => service.AddItemAsync(profile.Id, dto));

            Assert.Empty(db.EvaluationProfileItems);
        }
    }

    [Fact]
    public async Task AddItem_WithAnEmptyGroupId_IsAValidationError()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateDraftAsync(service, AddPosition(db));
            var dto = new EvaluationProfileItemInputDto { KpiGroupId = Guid.Empty, WeightPercent = 10, DisplayOrder = 1 };

            await Assert.ThrowsAsync<ValidationException>(() => service.AddItemAsync(profile.Id, dto));
        }
    }

    // ----- UpdateItem -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateItem_ChangesTheWeightAndOrder()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var group = AddGroup(db, "A_GROUP");
            var profile = await CreateDraftAsync(service, position, Input(group, 50, 1));

            var item = await service.UpdateItemAsync(
                profile.Id,
                group.Id,
                new UpdateEvaluationProfileItemDto { WeightPercent = 35.5m, DisplayOrder = 3 });

            Assert.Equal(35.5m, item.WeightPercent);
            Assert.Equal(3, item.DisplayOrder);
            var stored = await db.EvaluationProfileItems.SingleAsync();
            Assert.Equal(35.5m, stored.WeightPercent);
            Assert.Equal(3, stored.DisplayOrder);
        }
    }

    [Fact]
    public async Task UpdateItem_ForAGroupNotInTheProfile_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var other = AddGroup(db, "OTHER");
            var profile = await CreateDraftAsync(service, position);

            await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateItemAsync(
                profile.Id,
                other.Id,
                new UpdateEvaluationProfileItemDto { WeightPercent = 10, DisplayOrder = 1 }));
        }
    }

    [Fact]
    public async Task UpdateItem_WithInvalidValues_IsAValidationErrorAndKeepsTheItem()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var group = AddGroup(db, "A_GROUP");
            var profile = await CreateDraftAsync(service, position, Input(group, 50, 1));

            await Assert.ThrowsAsync<ValidationException>(() => service.UpdateItemAsync(
                profile.Id,
                group.Id,
                new UpdateEvaluationProfileItemDto { WeightPercent = 0, DisplayOrder = 1 }));

            Assert.Equal(50m, (await db.EvaluationProfileItems.SingleAsync()).WeightPercent);
        }
    }

    // ----- RemoveItem -----------------------------------------------------------------------

    [Fact]
    public async Task RemoveItem_DeletesOnlyTheRequestedItem()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A_GROUP");
            var b = AddGroup(db, "B_GROUP");
            var profile = await CreateDraftAsync(service, position, Input(a, 60, 1), Input(b, 40, 2));

            await service.RemoveItemAsync(profile.Id, a.Id);

            var remaining = await db.EvaluationProfileItems.SingleAsync();
            Assert.Equal(b.Id, remaining.KpiGroupId);
        }
    }

    [Fact]
    public async Task RemoveItem_ForAGroupNotInTheProfile_IsNotFound()
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

    private static async Task<(AppDbContext Db, EvaluationProfileService Service, EvaluationProfileDto Profile, KpiGroup Group)>
        ProfileWithOneItemAsync()
    {
        var (db, service) = CreateEnvironment();
        var position = AddPosition(db);
        var group = AddGroup(db, "A_GROUP");
        AddGroup(db, "SPARE");
        var profile = await CreateDraftAsync(service, position, Input(group, 100, 1));
        return (db, service, profile, group);
    }

    [Theory]
    [InlineData(EvaluationProfileStatus.Active)]
    [InlineData(EvaluationProfileStatus.Archived)]
    public async Task EveryWrite_OnAnActiveOrArchivedProfile_IsAGenericConflictAndChangesNothing(EvaluationProfileStatus status)
    {
        var (db, service, profile, group) = await ProfileWithOneItemAsync();
        using (db)
        {
            await SetStatusAsync(db, profile.Id, status);
            var spare = await db.KpiGroups.SingleAsync(g => g.Code == "SPARE");

            var add = await Assert.ThrowsAsync<ConflictException>(() => service.AddItemAsync(profile.Id, Input(spare, 10, 2)));
            var update = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateItemAsync(
                profile.Id, group.Id, new UpdateEvaluationProfileItemDto { WeightPercent = 50, DisplayOrder = 1 }));
            var remove = await Assert.ThrowsAsync<ConflictException>(() => service.RemoveItemAsync(profile.Id, group.Id));

            Assert.All([add, update, remove], e => Assert.Equal(ErrorCodes.Conflict, e.ErrorCode));
            var stored = await db.EvaluationProfileItems.SingleAsync();
            Assert.Equal(100m, stored.WeightPercent);
        }
    }

    [Fact]
    public async Task EveryWrite_OnAProfileInUse_IsAConflictWithTheInUseCode()
    {
        var (db, service, profile, group) = await ProfileWithOneItemAsync();
        using (db)
        {
            MarkInUse(db, profile.Id);
            var spare = await db.KpiGroups.SingleAsync(g => g.Code == "SPARE");

            var add = await Assert.ThrowsAsync<ConflictException>(() => service.AddItemAsync(profile.Id, Input(spare, 10, 2)));
            var update = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateItemAsync(
                profile.Id, group.Id, new UpdateEvaluationProfileItemDto { WeightPercent = 50, DisplayOrder = 1 }));
            var remove = await Assert.ThrowsAsync<ConflictException>(() => service.RemoveItemAsync(profile.Id, group.Id));

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
            var group = AddGroup(db, "A_GROUP");

            await Assert.ThrowsAsync<ValidationException>(() => CreateDraftAsync(service, position, Input(group, 0, 1)));
        }
    }
}
