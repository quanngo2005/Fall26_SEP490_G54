using System.Reflection;
using System.Text.Json;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.EvaluationProfile;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace G54.UnitTests;

public sealed class EvaluationProfilesControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static EvaluationProfilesController CreateController(RecordingService service) =>
        new(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-1" },
            },
        };

    private static EvaluationProfileDto SampleProfile(Guid? id = null) => new(
        id ?? Guid.NewGuid(),
        "TELLER",
        "Teller profile",
        null,
        Guid.NewGuid(),
        "TELLER",
        "Teller",
        2,
        new DateOnly(2026, 7, 1),
        null,
        "ACTIVE",
        false,
        100m,
        DateTimeOffset.UtcNow,
        [new EvaluationProfileItemDto(Guid.NewGuid(), null, "Service quality", null, null, null, 100m, 1, [])]);

    // ----- Routes and policies --------------------------------------------------------------

    [Theory]
    [InlineData(nameof(EvaluationProfilesController.GetProfiles), typeof(HttpGetAttribute), null)]
    [InlineData(nameof(EvaluationProfilesController.GetProfileById), typeof(HttpGetAttribute), "{id:guid}")]
    [InlineData(nameof(EvaluationProfilesController.CreateProfile), typeof(HttpPostAttribute), null)]
    [InlineData(nameof(EvaluationProfilesController.UpdateProfile), typeof(HttpPutAttribute), "{id:guid}")]
    [InlineData(nameof(EvaluationProfilesController.ChangeProfileStatus), typeof(HttpPatchAttribute), "{id:guid}/status")]
    [InlineData(nameof(EvaluationProfilesController.DeleteProfile), typeof(HttpDeleteAttribute), "{id:guid}")]
    [InlineData(nameof(EvaluationProfilesController.GetItems), typeof(HttpGetAttribute), "{id:guid}/items")]
    [InlineData(nameof(EvaluationProfilesController.AddItem), typeof(HttpPostAttribute), "{id:guid}/items")]
    [InlineData(nameof(EvaluationProfilesController.UpdateItem), typeof(HttpPutAttribute), "{id:guid}/items/{itemId:guid}")]
    [InlineData(nameof(EvaluationProfilesController.RemoveItem), typeof(HttpDeleteAttribute), "{id:guid}/items/{itemId:guid}")]
    public void EveryAction_UsesTheDocumentedVerbAndRoute(string action, Type verb, string? template)
    {
        var method = typeof(EvaluationProfilesController).GetMethod(action);

        Assert.NotNull(method);
        var attribute = Assert.IsAssignableFrom<HttpMethodAttribute>(method.GetCustomAttribute(verb));
        Assert.Equal(template, attribute.Template);
    }

    [Theory]
    [InlineData(nameof(EvaluationProfilesController.GetProfiles), "read")]
    [InlineData(nameof(EvaluationProfilesController.GetProfileById), "read")]
    [InlineData(nameof(EvaluationProfilesController.CreateProfile), "manage")]
    [InlineData(nameof(EvaluationProfilesController.UpdateProfile), "manage")]
    [InlineData(nameof(EvaluationProfilesController.ChangeProfileStatus), "manage")]
    [InlineData(nameof(EvaluationProfilesController.DeleteProfile), "manage")]
    [InlineData(nameof(EvaluationProfilesController.GetItems), "read")]
    [InlineData(nameof(EvaluationProfilesController.AddItem), "manage")]
    [InlineData(nameof(EvaluationProfilesController.UpdateItem), "manage")]
    [InlineData(nameof(EvaluationProfilesController.RemoveItem), "manage")]
    public void EveryAction_RequiresTheKpiPermissionAtTheAllScope(string action, string expectedAction)
    {
        var permission = Assert.Single(
            typeof(EvaluationProfilesController).GetMethod(action)!.GetCustomAttributes<HasPermissionAttribute>());

        Assert.Equal("kpi", permission.Resource);
        Assert.Equal(expectedAction, permission.Action);
        Assert.Equal("All", permission.Scope);
    }

    // ----- Behaviour ------------------------------------------------------------------------

    [Fact]
    public async Task GetProfiles_ReturnsThePagedResultInTheCanonicalEnvelope()
    {
        var service = new RecordingService();
        var controller = CreateController(service);
        var filter = new EvaluationProfileFilterDto { Keyword = "teller", Status = "ACTIVE" };

        var result = await controller.GetProfiles(filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(filter, service.Filter);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, JsonOptions));
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(200, root.GetProperty("status").GetInt32());
        Assert.Equal("trace-1", root.GetProperty("traceId").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal("TELLER", data.GetProperty("items")[0].GetProperty("code").GetString());
        Assert.Equal("2026-07-01", data.GetProperty("items")[0].GetProperty("effectiveFrom").GetString());
    }

    [Fact]
    public async Task GetProfileById_Found_ReturnsTheProfileWithCamelCaseFields()
    {
        var id = Guid.NewGuid();
        var service = new RecordingService { Profile = SampleProfile(id) };
        var controller = CreateController(service);

        var result = await controller.GetProfileById(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, JsonOptions));
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(id, data.GetProperty("id").GetGuid());
        Assert.Equal("ACTIVE", data.GetProperty("status").GetString());
        Assert.False(data.GetProperty("isInUse").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("effectiveTo").ValueKind);
        Assert.Equal("Service quality", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetProfileById_Unknown_Returns404WithTheNotFoundCode()
    {
        var controller = CreateController(new RecordingService());

        var result = await controller.GetProfileById(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(notFound.Value, JsonOptions));
        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(404, root.GetProperty("status").GetInt32());
        Assert.Equal("NOT_FOUND", root.GetProperty("errorCode").GetString());
        Assert.Equal("trace-1", root.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task CreateProfile_Returns201WithALocationPointingAtTheDetailRoute()
    {
        var created = SampleProfile();
        var service = new RecordingService { Profile = created };
        var controller = CreateController(service);
        var dto = new CreateEvaluationProfileDto { Code = "TELLER", Name = "Teller profile" };

        var result = await controller.CreateProfile(dto, CancellationToken.None);

        var response = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Same(dto, service.CreateDto);
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Equal(nameof(EvaluationProfilesController.GetProfileById), response.ActionName);
        Assert.Equal(created.Id, response.RouteValues!["id"]);
        var envelope = Assert.IsType<ApiResponse<EvaluationProfileDto>>(response.Value);
        Assert.True(envelope.Success);
        Assert.Equal("trace-1", envelope.TraceId);
        Assert.Equal(created.Id, envelope.Data!.Id);
    }

    [Fact]
    public async Task UpdateProfile_PassesTheRouteIdAndBodyAndReturns200()
    {
        var id = Guid.NewGuid();
        var service = new RecordingService { Profile = SampleProfile(id) };
        var controller = CreateController(service);
        var dto = new UpdateEvaluationProfileDto { Name = "Renamed" };

        var result = await controller.UpdateProfile(id, dto, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, service.UpdatedId);
        Assert.Same(dto, service.UpdateDto);
        var envelope = Assert.IsType<ApiResponse<EvaluationProfileDto>>(ok.Value);
        Assert.Equal(id, envelope.Data!.Id);
    }

    [Fact]
    public async Task ChangeProfileStatus_PassesTheRouteIdAndBodyAndReturnsAnEmptyEnvelope()
    {
        var id = Guid.NewGuid();
        var service = new RecordingService();
        var controller = CreateController(service);
        var dto = new EvaluationProfileStatusDto { Status = "ACTIVE" };

        var result = await controller.ChangeProfileStatus(id, dto, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, service.StatusId);
        Assert.Same(dto, service.StatusDto);
        var envelope = Assert.IsType<ApiResponse>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Equal("trace-1", envelope.TraceId);
    }

    [Fact]
    public async Task DeleteProfile_ArchivesThroughTheServiceAndReturns200()
    {
        var id = Guid.NewGuid();
        var service = new RecordingService();
        var controller = CreateController(service);

        var result = await controller.DeleteProfile(id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, service.DeletedId);
    }

    [Fact]
    public async Task GetItems_Found_ReturnsTheItemsInTheCanonicalEnvelope()
    {
        var service = new RecordingService { Profile = SampleProfile() };
        var controller = CreateController(service);

        var result = await controller.GetItems(Guid.NewGuid(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, JsonOptions));
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());
        Assert.Equal("Service quality", data[0].GetProperty("name").GetString());
        Assert.Equal("trace-1", document.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task GetItems_UnknownProfile_Returns404WithTheNotFoundCode()
    {
        var controller = CreateController(new RecordingService());

        var result = await controller.GetItems(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(notFound.Value, JsonOptions));
        Assert.Equal("NOT_FOUND", document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task AddItem_Returns201WithALocationPointingAtTheItemsRoute()
    {
        var id = Guid.NewGuid();
        var service = new RecordingService();
        var controller = CreateController(service);
        var dto = new AddEvaluationProfileItemDto { Name = "Quality", WeightPercent = 25, DisplayOrder = 1 };

        var result = await controller.AddItem(id, dto, CancellationToken.None);

        var response = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(id, service.ItemProfileId);
        Assert.Same(dto, service.AddItemDto);
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Equal(nameof(EvaluationProfilesController.GetItems), response.ActionName);
        Assert.Equal(id, response.RouteValues!["id"]);
        var envelope = Assert.IsType<ApiResponse<EvaluationProfileItemDto>>(response.Value);
        Assert.True(envelope.Success);
        Assert.Equal("trace-1", envelope.TraceId);
    }

    [Fact]
    public async Task UpdateItem_PassesTheRouteIdsAndBodyAndReturns200()
    {
        var id = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = CreateController(service);
        var dto = new UpdateEvaluationProfileItemDto { WeightPercent = 40, DisplayOrder = 2 };

        var result = await controller.UpdateItem(id, itemId, dto, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, service.ItemProfileId);
        Assert.Equal(itemId, service.ItemId);
        Assert.Same(dto, service.UpdateItemDto);
        Assert.IsType<ApiResponse<EvaluationProfileItemDto>>(ok.Value);
    }

    [Fact]
    public async Task RemoveItem_PassesTheRouteIdsAndReturnsAnEmptyEnvelope()
    {
        var id = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = CreateController(service);

        var result = await controller.RemoveItem(id, itemId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, service.ItemProfileId);
        Assert.Equal(itemId, service.ItemId);
        Assert.True(Assert.IsType<ApiResponse>(ok.Value).Success);
    }

    // ----- Test double ----------------------------------------------------------------------

    private sealed class RecordingService : IEvaluationProfileService
    {
        public EvaluationProfileDto? Profile { get; set; }
        public EvaluationProfileFilterDto? Filter { get; private set; }
        public CreateEvaluationProfileDto? CreateDto { get; private set; }
        public Guid? UpdatedId { get; private set; }
        public UpdateEvaluationProfileDto? UpdateDto { get; private set; }
        public Guid? StatusId { get; private set; }
        public EvaluationProfileStatusDto? StatusDto { get; private set; }
        public Guid? DeletedId { get; private set; }
        public Guid? ItemProfileId { get; private set; }
        public Guid? ItemId { get; private set; }
        public AddEvaluationProfileItemDto? AddItemDto { get; private set; }
        public UpdateEvaluationProfileItemDto? UpdateItemDto { get; private set; }

        public Task<PagedResult<EvaluationProfileListItemDto>> GetProfilesAsync(
            EvaluationProfileFilterDto filter,
            CancellationToken cancellationToken = default)
        {
            Filter = filter;
            var item = new EvaluationProfileListItemDto(
                Guid.NewGuid(),
                "TELLER",
                "Teller profile",
                Guid.NewGuid(),
                "TELLER",
                "Teller",
                1,
                new DateOnly(2026, 7, 1),
                null,
                "ACTIVE",
                3,
                100m,
                DateTimeOffset.UtcNow);
            return Task.FromResult(new PagedResult<EvaluationProfileListItemDto>([item], 1, 1, 20));
        }

        public Task<EvaluationProfileDto?> GetProfileByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Profile);

        public Task<EvaluationProfileDto> CreateProfileAsync(
            CreateEvaluationProfileDto dto,
            CancellationToken cancellationToken = default)
        {
            CreateDto = dto;
            return Task.FromResult(Profile!);
        }

        public Task<EvaluationProfileDto> UpdateProfileAsync(
            Guid id,
            UpdateEvaluationProfileDto dto,
            CancellationToken cancellationToken = default)
        {
            UpdatedId = id;
            UpdateDto = dto;
            return Task.FromResult(Profile!);
        }

        public Task ChangeStatusAsync(
            Guid id,
            EvaluationProfileStatusDto dto,
            CancellationToken cancellationToken = default)
        {
            StatusId = id;
            StatusDto = dto;
            return Task.CompletedTask;
        }

        public Task DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default)
        {
            DeletedId = id;
            return Task.CompletedTask;
        }

        public Task<List<EvaluationProfileItemDto>?> GetItemsAsync(
            Guid profileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Profile is null ? null : Profile.Items.ToList());

        public Task<EvaluationProfileItemDto> AddItemAsync(
            Guid profileId,
            AddEvaluationProfileItemDto dto,
            CancellationToken cancellationToken = default)
        {
            ItemProfileId = profileId;
            AddItemDto = dto;
            return Task.FromResult(new EvaluationProfileItemDto(
                Guid.NewGuid(), dto.ParentItemId, dto.Name, dto.KpiDefinitionId, null, null, dto.WeightPercent, dto.DisplayOrder, []));
        }

        public Task<EvaluationProfileItemDto> UpdateItemAsync(
            Guid profileId,
            Guid itemId,
            UpdateEvaluationProfileItemDto dto,
            CancellationToken cancellationToken = default)
        {
            ItemProfileId = profileId;
            ItemId = itemId;
            UpdateItemDto = dto;
            return Task.FromResult(new EvaluationProfileItemDto(
                itemId, null, dto.Name, null, null, null, dto.WeightPercent, dto.DisplayOrder, []));
        }

        public Task RemoveItemAsync(Guid profileId, Guid itemId, CancellationToken cancellationToken = default)
        {
            ItemProfileId = profileId;
            ItemId = itemId;
            return Task.CompletedTask;
        }
    }
}
