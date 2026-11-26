using System.Reflection;
using System.Security.Claims;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.CampaignCustomer;
using G54.BLL.Dtos.DataImport;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace G54.UnitTests;

public sealed class CampaignCustomersControllerTests
{
    private static readonly byte[] XlsxBytes = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00];

    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid campaignId = Guid.NewGuid();
    private readonly FakeCustomerService customers = new();
    private readonly FakeImportService import = new();

    private CampaignCustomersController CreateController(bool authenticated = true) =>
        new(import, customers, Options.Create(new SystemParametersOptions()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = authenticated
                        ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test"))
                        : new ClaimsPrincipal(new ClaimsIdentity()),
                },
            },
        };

    private static FormFile File(byte[] content, string name) =>
        new(new MemoryStream(content), 0, content.Length, "file", name);

    [Fact]
    public async Task GetCustomers_ForwardsFiltersAndReturnsOk()
    {
        var assignee = Guid.NewGuid();

        var result = await CreateController().GetCustomers(campaignId, "an", "CLOSED", assignee, true, 2, 50);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal((campaignId, "an", "CLOSED", assignee, true, 2, 50), customers.LastList);
    }

    [Fact]
    public async Task Allocate_PassesRuleEmployeesAndCurrentUser()
    {
        var employee = Guid.NewGuid();

        var result = await CreateController().Allocate(
            campaignId, new AllocateCampaignCustomersRequestDto("EVEN", [employee]), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("EVEN", customers.LastRule);
        Assert.Equal([employee], customers.LastEmployeeIds);
        Assert.Equal(userId, customers.LastUserId);
    }

    [Fact]
    public async Task Allocate_Throws_WhenUserIsNotAuthenticated() =>
        await Assert.ThrowsAsync<UnauthorizedException>(() => CreateController(false).Allocate(
            campaignId, new AllocateCampaignCustomersRequestDto(null, null), CancellationToken.None));

    [Fact]
    public async Task Reassign_PassesIdsAndReturnsOk()
    {
        var customerId = Guid.NewGuid();
        var employee = Guid.NewGuid();

        var result = await CreateController().Reassign(
            campaignId, customerId, new ReassignCampaignCustomerRequestDto(employee), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal((campaignId, customerId, employee, userId), customers.LastReassign);
    }

    [Fact]
    public void GetTemplate_ReturnsTheXlsxFile()
    {
        var result = Assert.IsType<FileContentResult>(CreateController().GetTemplate());

        Assert.Equal("campaign-customers-template.xlsx", result.FileDownloadName);
        Assert.Equal(import.Template, result.FileContents);
        Assert.Contains("spreadsheetml", result.ContentType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportActions_RejectMissingEmptyWrongExtensionAndBadSignatureFiles(bool commit)
    {
        var controller = CreateController();
        IFormFile?[] files =
        [
            null,
            File([], "empty.xlsx"),
            File(XlsxBytes, "customers.csv"),
            File("not a zip file"u8.ToArray(), "customers.xlsx"),
        ];

        foreach (var file in files)
        {
            var result = commit
                ? await controller.Commit(campaignId, file, CancellationToken.None)
                : await controller.Preview(campaignId, file, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        Assert.False(import.Called);
    }

    [Fact]
    public async Task Preview_ForwardsTheFileName()
    {
        var result = await CreateController().Preview(campaignId, File(XlsxBytes, "customers.xlsx"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal((campaignId, "customers.xlsx"), import.LastPreview);
    }

    [Fact]
    public async Task Commit_ForwardsTheUserAndDisablesCaching()
    {
        var controller = CreateController();

        var result = await controller.Commit(campaignId, File(XlsxBytes, "customers.xlsx"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal((campaignId, userId), import.LastCommit);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void ReadsRequireCampaignReadAndWritesRequireCampaignManage()
    {
        static string Permission(string method)
        {
            var attribute = typeof(CampaignCustomersController).GetMethod(method)!.GetCustomAttribute<HasPermissionAttribute>();
            return attribute is null ? "none" : $"{attribute.Resource}.{attribute.Action}";
        }

        Assert.Equal("campaign.read", Permission(nameof(CampaignCustomersController.GetCustomers)));
        Assert.Equal("campaign.read", Permission(nameof(CampaignCustomersController.GetTemplate)));
        Assert.Equal("campaign.manage", Permission(nameof(CampaignCustomersController.Allocate)));
        Assert.Equal("campaign.manage", Permission(nameof(CampaignCustomersController.Reassign)));
        Assert.Equal("campaign.manage", Permission(nameof(CampaignCustomersController.Preview)));
        Assert.Equal("campaign.manage", Permission(nameof(CampaignCustomersController.Commit)));
    }

    private sealed class FakeCustomerService : ICampaignCustomerService
    {
        public (Guid, string?, string?, Guid?, bool, int, int) LastList { get; private set; }
        public string? LastRule { get; private set; }
        public IReadOnlyList<Guid>? LastEmployeeIds { get; private set; }
        public Guid LastUserId { get; private set; }
        public (Guid, Guid, Guid, Guid) LastReassign { get; private set; }

        public Task<CampaignCustomerListResultDto> ListAsync(
            Guid campaignId, string? search, string? status, Guid? assigneeId, bool unassignedOnly, int page, int pageSize,
            CancellationToken cancellationToken = default)
        {
            LastList = (campaignId, search, status, assigneeId, unassignedOnly, page, pageSize);
            return Task.FromResult(new CampaignCustomerListResultDto(
                new PagedResult<CampaignCustomerDto>([], 0, page, pageSize),
                new CampaignCustomerSummaryDto(0, 0, [], [])));
        }

        public Task<AllocateCampaignCustomersResultDto> AllocateAsync(
            Guid campaignId, string? rule, IReadOnlyList<Guid>? employeeIds, Guid currentUserId,
            CancellationToken cancellationToken = default)
        {
            (LastRule, LastEmployeeIds, LastUserId) = (rule, employeeIds, currentUserId);
            return Task.FromResult(new AllocateCampaignCustomersResultDto(0, []));
        }

        public Task<CampaignCustomerDto> ReassignAsync(
            Guid campaignId, Guid customerId, Guid employeeId, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            LastReassign = (campaignId, customerId, employeeId, currentUserId);
            return Task.FromResult(new CampaignCustomerDto(
                customerId, "CIF1", "Name", null, null, employeeId, null, "NOT_CONTACTED", "", 0,
                null, null, null, null, null, null, DateTimeOffset.UtcNow));
        }
    }

    private sealed class FakeImportService : ICampaignCustomerImportService
    {
        public byte[] Template { get; } = [1, 2, 3];
        public bool Called { get; private set; }
        public (Guid, string) LastPreview { get; private set; }
        public (Guid, Guid) LastCommit { get; private set; }

        public byte[] GetTemplate() => Template;

        public Task<ImportPreviewDto> PreviewAsync(Guid campaignId, string fileName, Stream file, CancellationToken cancellationToken)
        {
            Called = true;
            LastPreview = (campaignId, fileName);
            return Task.FromResult(new ImportPreviewDto(fileName, new ImportSummaryDto(0, 0, 0, 0, 0), []));
        }

        public Task<ImportCommitResultDto> CommitAsync(Guid campaignId, Stream file, Guid currentUserId, CancellationToken cancellationToken)
        {
            Called = true;
            LastCommit = (campaignId, currentUserId);
            return Task.FromResult(new ImportCommitResultDto(0, 0, 0, [], []));
        }
    }
}
