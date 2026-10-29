using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.EvaluationProfile;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class EvaluationProfileServiceTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Jul1 = new(2026, 7, 1);

    private static (AppDbContext Db, EvaluationProfileService Service) CreateEnvironment()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        return (db, new EvaluationProfileService(db, NullLogger<EvaluationProfileService>.Instance));
    }

    private static JobPosition AddPosition(AppDbContext db, string code = "TELLER", RecordStatus status = RecordStatus.Active)
    {
        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = $"Title {code}",
            Status = status,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.JobPositions.Add(position);
        db.SaveChanges();
        return position;
    }

    /// <summary>A KPI that a test puts under a root group named after it; <see cref="Code"/> is the group name.</summary>
    private sealed record Kpi(Guid Id, string Code);

    private static Kpi AddGroup(AppDbContext db, string code)
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
        return new Kpi(kpi.Id, code);
    }

    /// <summary>A root group (named after the KPI code) with the weight, holding that KPI as its only leaf.</summary>
    private static EvaluationProfileItemInputDto Item(Kpi kpi, decimal weight, int order = 1) =>
        new()
        {
            Name = kpi.Code,
            WeightPercent = weight,
            DisplayOrder = order,
            Children = [new() { KpiDefinitionId = kpi.Id, WeightPercent = 100, DisplayOrder = 1 }],
        };

    private static List<EvaluationProfileItemInputDto> FullItems(Kpi a, Kpi b) =>
        [Item(a, 60, 1), Item(b, 40, 2)];

    private static EvaluationProfileItemInputDto ToInput(EvaluationProfileItemDto item) => new()
    {
        Name = item.Name,
        KpiDefinitionId = item.KpiDefinitionId,
        WeightPercent = item.WeightPercent,
        DisplayOrder = item.DisplayOrder,
        Children = item.Children.Select(ToInput).ToList(),
    };

    private static CreateEvaluationProfileDto CreateDto(
        JobPosition position,
        List<EvaluationProfileItemInputDto> items,
        string code = "TELLER",
        DateOnly? from = null,
        DateOnly? to = null) => new()
        {
            Code = code,
            Name = $"Profile {code}",
            JobPositionId = position.Id,
            EffectiveFrom = from ?? Jan1,
            EffectiveTo = to,
            Items = items,
        };

    private static UpdateEvaluationProfileDto UpdateDto(
        EvaluationProfileDto current,
        List<EvaluationProfileItemInputDto>? items = null,
        string? name = null,
        DateOnly? effectiveTo = null,
        DateOnly? effectiveFrom = null,
        Guid? jobPositionId = null) => new()
        {
            Name = name ?? current.Name,
            Description = current.Description,
            JobPositionId = jobPositionId ?? current.JobPositionId,
            EffectiveFrom = effectiveFrom ?? current.EffectiveFrom,
            EffectiveTo = effectiveTo ?? current.EffectiveTo,
            Items = items ?? current.Items.Select(ToInput).ToList(),
        };

    private static EvaluationProfileStatusDto Status(string value) => new() { Status = value };

    private static async Task<EvaluationProfileDto> CreateActiveAsync(
        EvaluationProfileService service,
        JobPosition position,
        Kpi a,
        Kpi b,
        string code = "TELLER",
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var created = await service.CreateProfileAsync(CreateDto(position, FullItems(a, b), code, from, to));
        await service.ChangeStatusAsync(created.Id, Status("ACTIVE"));
        return (await service.GetProfileByIdAsync(created.Id))!;
    }

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

    // ----- Create ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_ValidInput_CreatesADraftVersionOneWithOrderedItems()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");

            var result = await service.CreateProfileAsync(CreateDto(position, [Item(b, 40, 2), Item(a, 60, 1)]) with
            {
                Code = "  TELLER  ",
                Name = "  Teller profile  ",
            });

            Assert.Equal("TELLER", result.Code);
            Assert.Equal("Teller profile", result.Name);
            Assert.Equal(1, result.Version);
            Assert.Equal("DRAFT", result.Status);
            Assert.False(result.IsInUse);
            Assert.Equal(100m, result.TotalWeightPercent);
            Assert.Equal(["A", "B"], result.Items.Select(i => i.Name));
            Assert.Equal(position.Code, result.JobPositionCode);
        }
    }

    [Fact]
    public async Task Create_DraftMayHaveNoItemsOrAnIncompleteTotal()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");

            var empty = await service.CreateProfileAsync(CreateDto(position, [], "EMPTY", Jan1));
            var partial = await service.CreateProfileAsync(CreateDto(position, [Item(a, 30)], "PART", Jul1));

            Assert.Empty(empty.Items);
            Assert.Equal(30m, partial.TotalWeightPercent);
        }
    }

    [Fact]
    public async Task Create_SameCode_CreatesTheNextVersionForTheSamePosition()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            await service.CreateProfileAsync(CreateDto(position, FullItems(a, b), from: Jan1));

            var second = await service.CreateProfileAsync(CreateDto(position, FullItems(a, b), from: Jul1));

            Assert.Equal(2, second.Version);
        }
    }

    [Fact]
    public async Task Create_NewVersionForAnotherPosition_IsAConflict()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var first = AddPosition(db, "TELLER");
            var second = AddPosition(db, "SALES");
            var a = AddGroup(db, "A");
            await service.CreateProfileAsync(CreateDto(first, [Item(a, 100)]));

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.CreateProfileAsync(CreateDto(second, [Item(a, 100)], from: Jul1)));

            Assert.Equal(ErrorCodes.Conflict, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task Create_SamePositionAndEffectiveFrom_IsAConflict()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            await service.CreateProfileAsync(CreateDto(position, [Item(a, 100)], "ONE"));

            await Assert.ThrowsAsync<ConflictException>(() =>
                service.CreateProfileAsync(CreateDto(position, [Item(a, 100)], "TWO")));
        }
    }

    [Fact]
    public async Task Create_UnknownJobPosition_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var a = AddGroup(db, "A");
            var dto = CreateDto(new JobPosition { Id = Guid.NewGuid() }, [Item(a, 100)]);

            var ex = await Assert.ThrowsAsync<NotFoundException>(() => service.CreateProfileAsync(dto));

            Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task Create_UnknownKpi_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var unknown = new Kpi(Guid.NewGuid(), "UNKNOWN");

            await Assert.ThrowsAsync<NotFoundException>(() =>
                service.CreateProfileAsync(CreateDto(position, [Item(unknown, 100)])));
        }
    }

    [Theory]
    [InlineData("", "Name")]
    [InlineData("   ", "Name")]
    [InlineData("CODE", "")]
    [InlineData("CODE", "   ")]
    public async Task Create_BlankCodeOrName_IsAValidationError(string code, string name)
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.CreateProfileAsync(CreateDto(position, []) with { Code = code, Name = name }));

            Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task Create_TooLongCodeNameOrDescription_IsAValidationError()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var dto = CreateDto(position, []);

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.CreateProfileAsync(dto with { Code = new string('C', 51) }));
            await Assert.ThrowsAsync<ValidationException>(() =>
                service.CreateProfileAsync(dto with { Name = new string('N', 201) }));
            await Assert.ThrowsAsync<ValidationException>(() =>
                service.CreateProfileAsync(dto with { Description = new string('D', 2001) }));
        }
    }

    [Fact]
    public async Task Create_EffectiveToBeforeEffectiveFrom_IsAValidationError()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.CreateProfileAsync(CreateDto(position, [], from: Jul1, to: Jan1)));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.0001)]
    [InlineData(10.12345)]
    public async Task Create_InvalidWeight_IsAValidationError(double weight)
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.CreateProfileAsync(CreateDto(position, [Item(a, (decimal)weight)])));
        }
    }

    [Fact]
    public async Task Create_DisplayOrderBelowOne_IsAValidationError()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.CreateProfileAsync(CreateDto(position, [Item(a, 100, 0)])));
        }
    }

    [Fact]
    public async Task Create_DuplicateGroupNameAmongSiblings_IsAValidationError()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.CreateProfileAsync(CreateDto(position, [Item(a, 50, 1), Item(a, 50, 2)])));
        }
    }

    // ----- Read -----------------------------------------------------------------------------

    [Fact]
    public async Task GetById_UnknownId_ReturnsNull()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            Assert.Null(await service.GetProfileByIdAsync(Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task GetById_ReportsWhetherThePerformanceEvaluationsUseIt()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var created = await service.CreateProfileAsync(CreateDto(position, [Item(a, 100)]));
            Assert.False((await service.GetProfileByIdAsync(created.Id))!.IsInUse);

            MarkInUse(db, created.Id);

            Assert.True((await service.GetProfileByIdAsync(created.Id))!.IsInUse);
        }
    }

    [Fact]
    public async Task List_FiltersByKeywordStatusAndPosition_AndPages()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var teller = AddPosition(db, "TELLER");
            var sales = AddPosition(db, "SALES");
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            await CreateActiveAsync(service, teller, a, b, "TELLER_STD");
            await service.CreateProfileAsync(CreateDto(sales, [Item(a, 100)], "SALES_STD", Jan1));
            await service.CreateProfileAsync(CreateDto(sales, [Item(a, 100)], "SALES_NEW", Jul1));

            var byKeyword = await service.GetProfilesAsync(new EvaluationProfileFilterDto { Keyword = "SALES" });
            var byStatus = await service.GetProfilesAsync(new EvaluationProfileFilterDto { Status = "active" });
            var byPosition = await service.GetProfilesAsync(new EvaluationProfileFilterDto { JobPositionId = teller.Id });
            var paged = await service.GetProfilesAsync(new EvaluationProfileFilterDto { Page = 2, PageSize = 2 });

            Assert.Equal(2, byKeyword.TotalCount);
            Assert.Equal("TELLER_STD", Assert.Single(byStatus.Items).Code);
            Assert.Equal("TELLER_STD", Assert.Single(byPosition.Items).Code);
            Assert.Equal(3, paged.TotalCount);
            Assert.Single(paged.Items);
            Assert.Equal(2, paged.Page);
            Assert.Equal(2, paged.PageSize);
        }
    }

    [Fact]
    public async Task List_ReportsItemCountAndTotalWeight()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            await service.CreateProfileAsync(CreateDto(position, FullItems(a, b)));

            var item = Assert.Single((await service.GetProfilesAsync(new EvaluationProfileFilterDto())).Items);

            Assert.Equal(2, item.ItemCount);
            Assert.Equal(100m, item.TotalWeightPercent);
            Assert.Equal("DRAFT", item.Status);
        }
    }

    [Fact]
    public async Task List_ClampsThePageSizeAndPage()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var result = await service.GetProfilesAsync(new EvaluationProfileFilterDto { Page = -3, PageSize = 5000 });

            Assert.Equal(1, result.Page);
            Assert.Equal(100, result.PageSize);
        }
    }

    [Fact]
    public async Task List_UnknownStatus_IsAValidationError()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            await Assert.ThrowsAsync<ValidationException>(() =>
                service.GetProfilesAsync(new EvaluationProfileFilterDto { Status = "1" }));
        }
    }

    // ----- Activation -----------------------------------------------------------------------

    [Fact]
    public async Task Activate_CompleteDraft_BecomesActive()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var profile = await CreateActiveAsync(service, position, AddGroup(db, "A"), AddGroup(db, "B"));

            Assert.Equal("ACTIVE", profile.Status);
        }
    }

    [Theory]
    [InlineData(99.9999)]
    [InlineData(60)]
    public async Task Activate_TotalWeightNotExactlyOneHundred_IsAValidationError(double total)
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var created = await service.CreateProfileAsync(CreateDto(position, [Item(a, (decimal)total)]));

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.ChangeStatusAsync(created.Id, Status("ACTIVE")));

            Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
            Assert.Equal("DRAFT", (await service.GetProfileByIdAsync(created.Id))!.Status);
        }
    }

    [Theory]
    [InlineData(100.0001)]
    [InlineData(99.5)]
    public async Task Activate_TotalWeightJustOutsideOneHundredAcrossSeveralGroups_IsRejected(double lastWeight)
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            var c = AddGroup(db, "C");
            var created = await service.CreateProfileAsync(
                CreateDto(position, [Item(a, 33.3333m, 1), Item(b, 33.3333m, 2), Item(c, (decimal)lastWeight - 66.6666m, 3)]));

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.ChangeStatusAsync(created.Id, Status("ACTIVE")));

            Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
            Assert.Equal("DRAFT", (await service.GetProfileByIdAsync(created.Id))!.Status);
        }
    }

    [Fact]
    public async Task Activate_FractionalWeightsAddingUpToExactlyOneHundred_AreAccepted()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            var c = AddGroup(db, "C");
            var created = await service.CreateProfileAsync(
                CreateDto(position, [Item(a, 33.3333m, 1), Item(b, 33.3333m, 2), Item(c, 33.3334m, 3)]));

            await service.ChangeStatusAsync(created.Id, Status("ACTIVE"));

            var active = (await service.GetProfileByIdAsync(created.Id))!;
            Assert.Equal("ACTIVE", active.Status);
            Assert.Equal(100m, active.TotalWeightPercent);
        }
    }

    [Fact]
    public async Task Activate_ErrorMessageReportsTheActualTotal()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var created = await service.CreateProfileAsync(CreateDto(position, [Item(AddGroup(db, "A"), 45m)]));

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.ChangeStatusAsync(created.Id, Status("ACTIVE")));

            Assert.Contains("45", ex.Message);
        }
    }

    [Fact]
    public async Task Update_DraftMayKeepAnIncompleteTotal()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var created = await service.CreateProfileAsync(CreateDto(position, [Item(a, 10m)]));

            var updated = await service.UpdateProfileAsync(created.Id, UpdateDto(created, [Item(a, 25m)]));

            Assert.Equal("DRAFT", updated.Status);
            Assert.Equal(25m, updated.TotalWeightPercent);
        }
    }

    [Fact]
    public async Task Activate_WithoutItems_IsAValidationError()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var created = await service.CreateProfileAsync(CreateDto(AddPosition(db), []));

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.ChangeStatusAsync(created.Id, Status("ACTIVE")));
        }
    }

    [Fact]
    public async Task Activate_InactiveJobPosition_IsAValidationError()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db, status: RecordStatus.Inactive);
            var created = await service.CreateProfileAsync(
                CreateDto(position, FullItems(AddGroup(db, "A"), AddGroup(db, "B"))));

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.ChangeStatusAsync(created.Id, Status("ACTIVE")));
        }
    }

    [Fact]
    public async Task Activate_NewVersion_RetiresThePreviousActiveVersionWithoutChangingItsDates()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            var first = await CreateActiveAsync(service, position, a, b, from: Jan1);
            var second = await service.CreateProfileAsync(CreateDto(position, FullItems(a, b), from: Jul1));

            await service.ChangeStatusAsync(second.Id, Status("ACTIVE"));

            var previous = (await service.GetProfileByIdAsync(first.Id))!;
            Assert.Equal("INACTIVE", previous.Status);
            Assert.Null(previous.EffectiveTo);
            Assert.Equal(Jan1, previous.EffectiveFrom);
            Assert.Equal("ACTIVE", (await service.GetProfileByIdAsync(second.Id))!.Status);
        }
    }

    [Fact]
    public async Task Activate_OverlappingActiveProfileOfAnotherCode_IsAConflict()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            await CreateActiveAsync(service, position, a, b, "ONE", Jan1, new DateOnly(2026, 12, 31));
            var other = await service.CreateProfileAsync(CreateDto(position, FullItems(a, b), "TWO", Jul1));

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.ChangeStatusAsync(other.Id, Status("ACTIVE")));

            Assert.Equal(ErrorCodes.Conflict, ex.ErrorCode);
            Assert.Equal("DRAFT", (await service.GetProfileByIdAsync(other.Id))!.Status);
        }
    }

    [Fact]
    public async Task Activate_NonOverlappingActiveProfileOfAnotherCode_IsAllowed()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            await CreateActiveAsync(service, position, a, b, "ONE", Jan1, new DateOnly(2026, 6, 30));
            var other = await service.CreateProfileAsync(CreateDto(position, FullItems(a, b), "TWO", Jul1));

            await service.ChangeStatusAsync(other.Id, Status("ACTIVE"));

            Assert.Equal("ACTIVE", (await service.GetProfileByIdAsync(other.Id))!.Status);
        }
    }

    [Fact]
    public async Task Activate_OverlapOnAnotherPosition_IsIgnored()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var teller = AddPosition(db, "TELLER");
            var sales = AddPosition(db, "SALES");
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            await CreateActiveAsync(service, teller, a, b, "ONE");

            var other = await CreateActiveAsync(service, sales, a, b, "TWO");

            Assert.Equal("ACTIVE", other.Status);
        }
    }

    // ----- Status transitions ---------------------------------------------------------------

    [Theory]
    [InlineData("DRAFT", "ARCHIVED")]
    [InlineData("ACTIVE", "INACTIVE")]
    [InlineData("ACTIVE", "ARCHIVED")]
    [InlineData("INACTIVE", "ACTIVE")]
    [InlineData("INACTIVE", "ARCHIVED")]
    public async Task ChangeStatus_AllowedTransitions_Succeed(string from, string to)
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateInStatusAsync(db, service, from);

            await service.ChangeStatusAsync(profile.Id, Status(to));

            Assert.Equal(to, (await service.GetProfileByIdAsync(profile.Id))!.Status);
        }
    }

    [Theory]
    [InlineData("DRAFT", "INACTIVE")]
    [InlineData("ACTIVE", "DRAFT")]
    [InlineData("INACTIVE", "DRAFT")]
    [InlineData("ARCHIVED", "ACTIVE")]
    [InlineData("ARCHIVED", "DRAFT")]
    [InlineData("ARCHIVED", "INACTIVE")]
    public async Task ChangeStatus_IllegalTransitions_AreConflicts(string from, string to)
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateInStatusAsync(db, service, from);

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.ChangeStatusAsync(profile.Id, Status(to)));

            Assert.Equal(ErrorCodes.Conflict, ex.ErrorCode);
            Assert.Equal(from, (await service.GetProfileByIdAsync(profile.Id))!.Status);
        }
    }

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("ARCHIVED")]
    public async Task ChangeStatus_ToTheCurrentStatus_IsANoOp(string status)
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var profile = await CreateInStatusAsync(db, service, status);

            await service.ChangeStatusAsync(profile.Id, Status(status));

            Assert.Equal(status, (await service.GetProfileByIdAsync(profile.Id))!.Status);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("RETIRED")]
    [InlineData("1")]
    public async Task ChangeStatus_BlankOrUnknownValue_IsAValidationError(string value)
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var created = await service.CreateProfileAsync(CreateDto(AddPosition(db), []));

            await Assert.ThrowsAsync<ValidationException>(() => service.ChangeStatusAsync(created.Id, Status(value)));
        }
    }

    [Fact]
    public async Task ChangeStatus_AcceptsLowerCaseValues()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var created = await service.CreateProfileAsync(CreateDto(AddPosition(db), []));

            await service.ChangeStatusAsync(created.Id, Status("archived"));

            Assert.Equal("ARCHIVED", (await service.GetProfileByIdAsync(created.Id))!.Status);
        }
    }

    [Fact]
    public async Task ChangeStatus_UnknownProfile_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            await Assert.ThrowsAsync<NotFoundException>(() =>
                service.ChangeStatusAsync(Guid.NewGuid(), Status("ACTIVE")));
        }
    }

    private static async Task<EvaluationProfileDto> CreateInStatusAsync(
        AppDbContext db,
        EvaluationProfileService service,
        string status)
    {
        var position = AddPosition(db, $"POS_{Guid.NewGuid():N}"[..20]);
        var a = AddGroup(db, $"A_{Guid.NewGuid():N}"[..12]);
        var b = AddGroup(db, $"B_{Guid.NewGuid():N}"[..12]);
        var created = await service.CreateProfileAsync(CreateDto(position, FullItems(a, b), $"C_{Guid.NewGuid():N}"[..20]));

        switch (status)
        {
            case "ACTIVE":
                await service.ChangeStatusAsync(created.Id, Status("ACTIVE"));
                break;
            case "INACTIVE":
                await service.ChangeStatusAsync(created.Id, Status("ACTIVE"));
                await service.ChangeStatusAsync(created.Id, Status("INACTIVE"));
                break;
            case "ARCHIVED":
                await service.ChangeStatusAsync(created.Id, Status("ARCHIVED"));
                break;
        }

        return (await service.GetProfileByIdAsync(created.Id))!;
    }

    // ----- Update ---------------------------------------------------------------------------

    [Fact]
    public async Task Update_ReplacesHeaderAndItems_KeepingCodeVersionAndStatus()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            var c = AddGroup(db, "C");
            var created = await service.CreateProfileAsync(CreateDto(position, FullItems(a, b)));

            var result = await service.UpdateProfileAsync(
                created.Id,
                UpdateDto(created, [Item(b, 70, 1), Item(c, 30, 2)], name: "Renamed", effectiveTo: new DateOnly(2026, 12, 31)));

            Assert.Equal("Renamed", result.Name);
            Assert.Equal(new DateOnly(2026, 12, 31), result.EffectiveTo);
            Assert.Equal("TELLER", result.Code);
            Assert.Equal(1, result.Version);
            Assert.Equal("DRAFT", result.Status);
            Assert.Equal(["B", "C"], result.Items.Select(i => i.Name));
            Assert.Equal([70m, 30m], result.Items.Select(i => i.WeightPercent));
            // Two root groups, each holding one KPI leaf.
            Assert.Equal(4, await db.EvaluationProfileItems.CountAsync(i => i.EvaluationProfileId == created.Id));
            Assert.Equal(2, await db.EvaluationProfileItems.CountAsync(i => i.EvaluationProfileId == created.Id && i.ParentItemId == null));
        }
    }

    [Fact]
    public async Task Update_KeepsTheIdOfAnItemThatStaysInTheProfile()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            var created = await service.CreateProfileAsync(CreateDto(position, FullItems(a, b)));
            var itemId = (await db.EvaluationProfileItems.AsNoTracking().SingleAsync(i => i.Name == "A")).Id;

            await service.UpdateProfileAsync(created.Id, UpdateDto(created, [Item(a, 55, 1), Item(b, 45, 2)]));

            var kept = await db.EvaluationProfileItems.AsNoTracking().SingleAsync(i => i.Name == "A");
            Assert.Equal(itemId, kept.Id);
            Assert.Equal(55m, kept.WeightPercent);
        }
    }

    [Fact]
    public async Task Update_UnknownProfile_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var dto = new UpdateEvaluationProfileDto { Name = "x", JobPositionId = AddPosition(db).Id, EffectiveFrom = Jan1 };

            await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateProfileAsync(Guid.NewGuid(), dto));
        }
    }

    [Fact]
    public async Task Update_ArchivedProfile_IsAConflict()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var archived = await CreateInStatusAsync(db, service, "ARCHIVED");

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.UpdateProfileAsync(archived.Id, UpdateDto(archived, name: "New name")));

            Assert.Equal(ErrorCodes.Conflict, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task Update_ActiveProfile_MustKeepTheTotalAtOneHundred()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            var active = await CreateActiveAsync(service, position, a, b);

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.UpdateProfileAsync(active.Id, UpdateDto(active, [Item(a, 50, 1), Item(b, 40, 2)])));

            var ok = await service.UpdateProfileAsync(active.Id, UpdateDto(active, [Item(a, 50, 1), Item(b, 50, 2)]));
            Assert.Equal(100m, ok.TotalWeightPercent);
            Assert.Equal("ACTIVE", ok.Status);
        }
    }

    [Fact]
    public async Task Update_ActiveProfile_CannotMoveIntoAnOverlappingActivePeriod()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            await CreateActiveAsync(service, position, a, b, "ONE", Jan1, new DateOnly(2026, 6, 30));
            var two = await CreateActiveAsync(service, position, a, b, "TWO", Jul1);

            await Assert.ThrowsAsync<ConflictException>(() =>
                service.UpdateProfileAsync(two.Id, UpdateDto(two, effectiveFrom: new DateOnly(2026, 6, 1))));
        }
    }

    [Fact]
    public async Task Update_ChangingTheEffectiveFromToAnotherProfilesDate_IsAConflict()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            await service.CreateProfileAsync(CreateDto(position, [Item(a, 100)], "ONE", Jan1));
            var two = await service.CreateProfileAsync(CreateDto(position, [Item(a, 100)], "TWO", Jul1));

            await Assert.ThrowsAsync<ConflictException>(() =>
                service.UpdateProfileAsync(two.Id, UpdateDto(two, effectiveFrom: Jan1)));
        }
    }

    [Fact]
    public async Task Update_MovingToAnotherPosition_ChecksThePositionExists()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var created = await service.CreateProfileAsync(CreateDto(position, []));

            await Assert.ThrowsAsync<NotFoundException>(() =>
                service.UpdateProfileAsync(created.Id, UpdateDto(created, jobPositionId: Guid.NewGuid())));
        }
    }

    // ----- Update of a profile that is in use ------------------------------------------------

    [Fact]
    public async Task Update_InUse_AllowsNameDescriptionAndEffectiveToOnly()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var created = await service.CreateProfileAsync(CreateDto(position, FullItems(AddGroup(db, "A"), AddGroup(db, "B"))));
            MarkInUse(db, created.Id);

            var result = await service.UpdateProfileAsync(
                created.Id,
                UpdateDto(created, name: "Renamed", effectiveTo: new DateOnly(2026, 12, 31)) with { Description = "note" });

            Assert.Equal("Renamed", result.Name);
            Assert.Equal("note", result.Description);
            Assert.Equal(new DateOnly(2026, 12, 31), result.EffectiveTo);
            Assert.Equal(2, result.Items.Count);
        }
    }

    [Fact]
    public async Task Update_InUse_ChangingAWeightIsRejectedWithTheInUseCode()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            var created = await service.CreateProfileAsync(CreateDto(position, FullItems(a, b)));
            MarkInUse(db, created.Id);

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.UpdateProfileAsync(created.Id, UpdateDto(created, [Item(a, 50, 1), Item(b, 50, 2)])));

            Assert.Equal(ErrorCodes.EvaluationProfileInUse, ex.ErrorCode);
            Assert.Equal(409, ex.StatusCode);
            Assert.Equal(60m, (await service.GetProfileByIdAsync(created.Id))!.Items[0].WeightPercent);
        }
    }

    [Fact]
    public async Task Update_InUse_ChangingTheOrderTheGroupsOrTheirNumberIsRejected()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var a = AddGroup(db, "A");
            var b = AddGroup(db, "B");
            var c = AddGroup(db, "C");
            var created = await service.CreateProfileAsync(CreateDto(position, FullItems(a, b)));
            MarkInUse(db, created.Id);

            foreach (var items in new[]
            {
                new List<EvaluationProfileItemInputDto> { Item(a, 60, 2), Item(b, 40, 1) },
                [Item(a, 60, 1), Item(c, 40, 2)],
                [Item(a, 100, 1)],
                [Item(a, 60, 1), Item(b, 30, 2), Item(c, 10, 3)],
            })
            {
                var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                    service.UpdateProfileAsync(created.Id, UpdateDto(created, items)));
                Assert.Equal(ErrorCodes.EvaluationProfileInUse, ex.ErrorCode);
            }
        }
    }

    [Fact]
    public async Task Update_InUse_ChangingThePositionOrEffectiveFromIsRejected()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db, "TELLER");
            var other = AddPosition(db, "SALES");
            var created = await service.CreateProfileAsync(CreateDto(position, FullItems(AddGroup(db, "A"), AddGroup(db, "B"))));
            MarkInUse(db, created.Id);

            var byPosition = await Assert.ThrowsAsync<ConflictException>(() =>
                service.UpdateProfileAsync(created.Id, UpdateDto(created, jobPositionId: other.Id)));
            var byDate = await Assert.ThrowsAsync<ConflictException>(() =>
                service.UpdateProfileAsync(created.Id, UpdateDto(created, effectiveFrom: Jul1)));

            Assert.Equal(ErrorCodes.EvaluationProfileInUse, byPosition.ErrorCode);
            Assert.Equal(ErrorCodes.EvaluationProfileInUse, byDate.ErrorCode);
        }
    }

    // ----- Delete ---------------------------------------------------------------------------

    [Fact]
    public async Task Delete_ArchivesTheProfileAndKeepsItsRows()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var created = await service.CreateProfileAsync(CreateDto(position, FullItems(AddGroup(db, "A"), AddGroup(db, "B"))));

            await service.DeleteProfileAsync(created.Id);

            var archived = (await service.GetProfileByIdAsync(created.Id))!;
            Assert.Equal("ARCHIVED", archived.Status);
            Assert.Equal(2, archived.Items.Count);
        }
    }

    [Fact]
    public async Task Delete_ActiveProfileUsedByEvaluations_IsStillAnArchive()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var position = AddPosition(db);
            var active = await CreateActiveAsync(service, position, AddGroup(db, "A"), AddGroup(db, "B"));
            MarkInUse(db, active.Id);

            await service.DeleteProfileAsync(active.Id);

            Assert.Equal("ARCHIVED", (await service.GetProfileByIdAsync(active.Id))!.Status);
            Assert.Equal(1, await db.PerformanceEvaluations.CountAsync());
        }
    }

    [Fact]
    public async Task Delete_AlreadyArchived_IsANoOp()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            var archived = await CreateInStatusAsync(db, service, "ARCHIVED");

            await service.DeleteProfileAsync(archived.Id);

            Assert.Equal("ARCHIVED", (await service.GetProfileByIdAsync(archived.Id))!.Status);
        }
    }

    [Fact]
    public async Task Delete_UnknownProfile_IsNotFound()
    {
        var (db, service) = CreateEnvironment();
        using (db)
        {
            await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteProfileAsync(Guid.NewGuid()));
        }
    }
}
