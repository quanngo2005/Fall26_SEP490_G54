using System.Text.Json;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.Employee;
using G54.BLL.Dtos.KpiDefinition;
using G54.BLL.Dtos.WorkType;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// Pins the enum API format contract (docs/api/API_CONVENTIONS.md): responses carry UPPER_SNAKE strings, never
/// ordinals; requests and query filters accept the same strings case-insensitively and reject unknown values.
/// </summary>
public sealed class EnumApiFormatContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    // ----- EnumApiFormat.ParseEnum ---------------------------------------------------------

    [Theory]
    [InlineData("ACTIVE", EmployeeStatus.Active)]
    [InlineData("on_leave", EmployeeStatus.OnLeave)]
    [InlineData("  Terminated ", EmployeeStatus.Terminated)]
    public void ParseEnum_AcceptsApiValueCaseInsensitively(string raw, EmployeeStatus expected)
    {
        Assert.Equal(expected, EnumApiFormat.ParseEnum<EmployeeStatus>(raw, "status"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseEnum_BlankValue_ReturnsNull(string? raw)
    {
        Assert.Null(EnumApiFormat.ParseEnum<EmployeeStatus>(raw, "status"));
    }

    [Theory]
    [InlineData("OnLeave")]
    [InlineData("0")]
    [InlineData("UNKNOWN")]
    public void ParseEnum_UnknownValue_ThrowsValidationException(string raw)
    {
        // Ordinals and C# member names are deliberately not API values.
        Assert.Throws<ValidationException>(() => EnumApiFormat.ParseEnum<EmployeeStatus>(raw, "status"));
    }

    [Fact]
    public void ParseRequiredEnum_BlankValue_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => EnumApiFormat.ParseRequiredEnum<KpiStatus>(null, "status"));
    }

    [Fact]
    public void ParseEnum_IsInverseOfToEnumString_ForEveryDtoEnum()
    {
        AssertRoundTrip<EmployeeStatus>();
        AssertRoundTrip<RecordStatus>();
        AssertRoundTrip<KpiStatus>();
        AssertRoundTrip<KpiDirection>();
        AssertRoundTrip<KpiAggregationType>();

        static void AssertRoundTrip<TEnum>()
            where TEnum : struct, Enum
        {
            foreach (var value in Enum.GetValues<TEnum>())
            {
                Assert.Equal(value, EnumApiFormat.ParseEnum<TEnum>(value.ToEnumString(), "field"));
            }
        }
    }

    // ----- JSON shape of the response DTOs --------------------------------------------------

    [Fact]
    public void EmployeeDetailDto_SerializesStatusAsString()
    {
        var dto = new EmployeeDetailDto(
            Guid.NewGuid(), "E1", "Name", "e@x.test", null, Guid.NewGuid(), "D", "Dept", Guid.NewGuid(), "P", "Pos",
            null, null, null, EmployeeStatus.OnLeave.ToEnumString(), 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, Web));

        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("status").ValueKind);
        Assert.Equal("ON_LEAVE", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void KpiDefinitionDto_SerializesEveryEnumAsString()
    {
        var dto = new KpiDefinitionDto(
            Guid.NewGuid(), Guid.NewGuid(), "G", "C", "N", null, null, "VND",
            KpiAggregationType.NoAggregation.ToEnumString(), KpiDirection.OnTarget.ToEnumString(), null,
            KpiStatus.Archived.ToEnumString(), DateTimeOffset.UtcNow);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, Web));

        Assert.Equal("NO_AGGREGATION", json.RootElement.GetProperty("aggregationType").GetString());
        Assert.Equal("ON_TARGET", json.RootElement.GetProperty("direction").GetString());
        Assert.Equal("ARCHIVED", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void KpiDefinitionSaveDto_BindsStringEnumsFromRequestBody()
    {
        const string body = """{"kpiGroupId":"5eed0000-0000-0000-0001-000000000001","code":"C","name":"N","unit":"u","aggregationType":"RATIO","direction":"LOWER_BETTER"}""";

        var dto = JsonSerializer.Deserialize<KpiDefinitionSaveDto>(body, Web);

        Assert.NotNull(dto);
        Assert.Equal("RATIO", dto.AggregationType);
        Assert.Equal("LOWER_BETTER", dto.Direction);
    }

    // ----- Employee --------------------------------------------------------------------------

    private static async Task<(AppDbContext Db, EmployeeService Service, Department Dept, JobPosition Position)> EmployeeEnvAsync()
    {
        var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            Code = "EP",
            Name = "EP",
            Version = 1,
            EffectiveFrom = DateOnly.FromDateTime(now.UtcDateTime),
            UpdatedAt = now,
            Status = EvaluationProfileStatus.Draft,
        };
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "D1",
            Name = "Dept",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "P1",
            Title = "Pos",
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        profile.JobPositionId = position.Id;
        db.AddRange(profile, dept, position);
        await db.SaveChangesAsync();
        return (db, new EmployeeService(db, NullLogger<EmployeeService>.Instance), dept, position);
    }

    private static CreateEmployeeDto NewEmployee(Department dept, JobPosition position, string code, string? status) => new()
    {
        EmployeeCode = code,
        FullName = "Full " + code,
        Email = code.ToLowerInvariant() + "@x.test",
        DepartmentId = dept.Id,
        JobPositionId = position.Id,
        Status = status,
    };

    [Fact]
    public async Task Employee_CreateWithoutStatus_DefaultsToActiveAndReturnsApiString()
    {
        var (db, service, dept, position) = await EmployeeEnvAsync();
        using (db)
        {
            var created = await service.CreateEmployeeAsync(NewEmployee(dept, position, "E-1", null));

            Assert.Equal("ACTIVE", created.Status);
        }
    }

    [Fact]
    public async Task Employee_CreateWithInvalidStatus_ThrowsValidationException()
    {
        var (db, service, dept, position) = await EmployeeEnvAsync();
        using (db)
        {
            await Assert.ThrowsAsync<ValidationException>(
                () => service.CreateEmployeeAsync(NewEmployee(dept, position, "E-2", "OnLeave")));
        }
    }

    [Fact]
    public async Task Employee_ListFilter_IsCaseInsensitiveAndRejectsUnknownValues()
    {
        var (db, service, dept, position) = await EmployeeEnvAsync();
        using (db)
        {
            await service.CreateEmployeeAsync(NewEmployee(dept, position, "E-3", "ACTIVE"));
            await service.CreateEmployeeAsync(NewEmployee(dept, position, "E-4", "ON_LEAVE"));

            var onLeave = await service.GetEmployeesAsync(new EmployeeFilterDto { Status = "on_leave" });
            var item = Assert.Single(onLeave.Items);
            Assert.Equal("ON_LEAVE", item.Status);

            await Assert.ThrowsAsync<ValidationException>(
                () => service.GetEmployeesAsync(new EmployeeFilterDto { Status = "1" }));
        }
    }

    [Fact]
    public async Task Employee_ChangeStatus_RequiresAValidApiValue()
    {
        var (db, service, dept, position) = await EmployeeEnvAsync();
        using (db)
        {
            var created = await service.CreateEmployeeAsync(NewEmployee(dept, position, "E-5", "ACTIVE"));

            await Assert.ThrowsAsync<ValidationException>(
                () => service.ChangeStatusAsync(created.Id, new EmployeeStatusDto { Status = string.Empty }));
            await Assert.ThrowsAsync<ValidationException>(
                () => service.ChangeStatusAsync(created.Id, new EmployeeStatusDto { Status = "RETIRED" }));

            await service.ChangeStatusAsync(created.Id, new EmployeeStatusDto { Status = "inactive" });
            var reloaded = await service.GetEmployeeByIdAsync(created.Id);
            Assert.Equal("INACTIVE", reloaded!.Status);
        }
    }

    // ----- KPI definition --------------------------------------------------------------------

    private static async Task<(AppDbContext Db, KpiDefinitionService Service, KpiGroup Group)> KpiEnvAsync()
    {
        var db = CreateDb();
        var group = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "G",
            Name = "Group",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.KpiGroups.Add(group);
        await db.SaveChangesAsync();
        return (db, new KpiDefinitionService(db), group);
    }

    private static KpiDefinitionSaveDto NewKpi(KpiGroup group, string code, string aggregation, string direction) => new()
    {
        KpiGroupId = group.Id,
        Code = code,
        Name = "Name " + code,
        Unit = "VND",
        AggregationType = aggregation,
        Direction = direction,
    };

    [Fact]
    public async Task Kpi_CreateReturnsEveryEnumAsApiString()
    {
        var (db, service, group) = await KpiEnvAsync();
        using (db)
        {
            var created = await service.CreateKpiAsync(NewKpi(group, "K-1", "no_aggregation", "On_Target"));

            Assert.Equal("NO_AGGREGATION", created.AggregationType);
            Assert.Equal("ON_TARGET", created.Direction);
            Assert.Equal("DRAFT", created.Status);
        }
    }

    [Theory]
    [InlineData("", "HIGHER_BETTER")]
    [InlineData("SUM", "")]
    [InlineData("1", "HIGHER_BETTER")]
    [InlineData("SUM", "HigherBetter")]
    public async Task Kpi_CreateWithMissingOrUnknownEnum_ThrowsValidationException(string aggregation, string direction)
    {
        var (db, service, group) = await KpiEnvAsync();
        using (db)
        {
            await Assert.ThrowsAsync<ValidationException>(
                () => service.CreateKpiAsync(NewKpi(group, "K-2", aggregation, direction)));
        }
    }

    [Fact]
    public async Task Kpi_ListFilters_AreCaseInsensitiveAndRejectUnknownValues()
    {
        var (db, service, group) = await KpiEnvAsync();
        using (db)
        {
            await service.CreateKpiAsync(NewKpi(group, "K-3", "SUM", "HIGHER_BETTER"));
            await service.CreateKpiAsync(NewKpi(group, "K-4", "AVG", "LOWER_BETTER"));

            var avg = await service.GetKpisAsync(new KpiFilterDto { AggregationType = "avg" });
            Assert.Equal("K-4", Assert.Single(avg.Items).Code);

            var drafts = await service.GetKpisAsync(new KpiFilterDto { Status = "draft" });
            Assert.Equal(2, drafts.TotalCount);

            await Assert.ThrowsAsync<ValidationException>(
                () => service.GetKpisAsync(new KpiFilterDto { Status = "9" }));
        }
    }

    [Fact]
    public async Task Kpi_ChangeStatus_RejectsUnknownApiValue()
    {
        var (db, service, group) = await KpiEnvAsync();
        using (db)
        {
            var created = await service.CreateKpiAsync(NewKpi(group, "K-5", "SUM", "HIGHER_BETTER"));

            await Assert.ThrowsAsync<ValidationException>(
                () => service.ChangeStatusAsync(created.Id, new KpiStatusDto { Status = "Published" }));

            await service.ChangeStatusAsync(created.Id, new KpiStatusDto { Status = "ACTIVE" });
            Assert.Equal("ACTIVE", (await service.GetKpiByIdAsync(created.Id))!.Status);
        }
    }

    // ----- Work type -------------------------------------------------------------------------

    private static async Task<(AppDbContext Db, WorkTypeService Service)> WorkTypeEnvAsync()
    {
        var db = CreateDb();
        await db.SaveChangesAsync();
        return (db, new WorkTypeService(db, NullLogger<WorkTypeService>.Instance));
    }

    [Fact]
    public async Task WorkType_StatusIsApiStringInListAndDetail_AndFilterIsCaseInsensitive()
    {
        var (db, service) = await WorkTypeEnvAsync();
        using (db)
        {
            var active = await service.CreateWorkTypeAsync(
                new CreateWorkTypeDto { Code = "WT-A", Name = "A"});
            var archived = await service.CreateWorkTypeAsync(
                new CreateWorkTypeDto { Code = "WT-B", Name = "B"});
            await service.ChangeStatusAsync(archived.Id, new WorkTypeStatusDto { Status = "archived" });

            Assert.Equal("ACTIVE", active.Status);
            Assert.Equal("ARCHIVED", (await service.GetWorkTypeByIdAsync(archived.Id))!.Status);

            var filtered = await service.GetWorkTypesAsync(new WorkTypeFilterDto { Status = "Archived" });
            Assert.Equal("WT-B", Assert.Single(filtered.Items).Code);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("Deleted")]
    public async Task WorkType_ChangeStatusWithMissingOrUnknownValue_ThrowsValidationException(string status)
    {
        var (db, service) = await WorkTypeEnvAsync();
        using (db)
        {
            var created = await service.CreateWorkTypeAsync(
                new CreateWorkTypeDto { Code = "WT-C", Name = "C"});

            await Assert.ThrowsAsync<ValidationException>(
                () => service.ChangeStatusAsync(created.Id, new WorkTypeStatusDto { Status = status }));
        }
    }

    [Fact]
    public async Task WorkType_ListFilterWithUnknownValue_ThrowsValidationException()
    {
        var (db, service) = await WorkTypeEnvAsync();
        using (db)
        {
            await Assert.ThrowsAsync<ValidationException>(
                () => service.GetWorkTypesAsync(new WorkTypeFilterDto { Status = "1" }));
        }
    }
}
