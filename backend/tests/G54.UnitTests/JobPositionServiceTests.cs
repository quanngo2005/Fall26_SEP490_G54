using G54.Api.Controllers;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.JobPosition;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class JobPositionServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static (AppDbContext Context, JobPositionService Service) CreateTestEnvironment()
    {
        var context = CreateInMemoryDbContext();
        var service = new JobPositionService(context, NullLogger<JobPositionService>.Instance);
        return (context, service);
    }

    private static async Task SeedPositionsAsync(AppDbContext context)
    {
        var profile1 = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            Code = "EP-TELLER",
            Name = "Khung danh gia GDV",
            Description = "Tieu chuan GDV",
            Version = 1,
            Status = EvaluationProfileStatus.Active,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var profile2 = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            Code = "EP-OFFICER",
            Name = "Khung danh gia Chuyen vien",
            Description = "Tieu chuan Chuyen vien",
            Version = 1,
            Status = EvaluationProfileStatus.Active,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var pos1 = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "POS_TELLER",
            Title = "Giao dich vien quay",
            Description = "Thuc hien giao dich tai quay",
            EvaluationProfileId = profile1.Id,
            Status = RecordStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var pos2 = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "POS_RBO",
            Title = "Chuyen vien KHCN",
            Description = "Phat trien khach hang ca nhan",
            EvaluationProfileId = profile2.Id,
            Status = RecordStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var pos3 = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "POS_OLD",
            Title = "Vi tri cu ngung hoat dong",
            Description = "Da ngung ap dung",
            EvaluationProfileId = profile2.Id,
            Status = RecordStatus.Inactive,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await context.EvaluationProfiles.AddRangeAsync(profile1, profile2);
        await context.JobPositions.AddRangeAsync(pos1, pos2, pos3);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetJobPositionsAsync_NoFilter_ReturnsAllPositions()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            await SeedPositionsAsync(context);

            var result = await service.GetJobPositionsAsync(new JobPositionFilterDto());

            Assert.Equal(3, result.TotalCount);
            Assert.Equal(3, result.Items.Count);
            Assert.Equal("POS_OLD", result.Items[0].Code);
        }
    }

    [Fact]
    public async Task GetJobPositionsAsync_FilterByKeyword_MatchesCodeOrTitle()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            await SeedPositionsAsync(context);

            var byCode = await service.GetJobPositionsAsync(new JobPositionFilterDto { Keyword = "TELLER" });
            Assert.Single(byCode.Items);
            Assert.Equal("POS_TELLER", byCode.Items[0].Code);

            var byTitle = await service.GetJobPositionsAsync(new JobPositionFilterDto { Keyword = "KHCN" });
            Assert.Single(byTitle.Items);
            Assert.Equal("POS_RBO", byTitle.Items[0].Code);
        }
    }

    [Fact]
    public async Task GetJobPositionsAsync_FilterByStatus_ReturnsOnlyActive()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            await SeedPositionsAsync(context);

            var result = await service.GetJobPositionsAsync(new JobPositionFilterDto { Status = RecordStatus.Active });

            Assert.Equal(2, result.TotalCount);
            Assert.All(result.Items, item => Assert.Equal("ACTIVE", item.Status));
        }
    }

    [Fact]
    public async Task GetJobPositionsAsync_Pagination_ReturnsCorrectPage()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            await SeedPositionsAsync(context);

            var page1 = await service.GetJobPositionsAsync(new JobPositionFilterDto { Page = 1, PageSize = 2 });
            Assert.Equal(3, page1.TotalCount);
            Assert.Equal(2, page1.Items.Count);
            Assert.Equal(1, page1.Page);

            var page2 = await service.GetJobPositionsAsync(new JobPositionFilterDto { Page = 2, PageSize = 2 });
            Assert.Equal(3, page2.TotalCount);
            Assert.Single(page2.Items);
            Assert.Equal(2, page2.Page);
        }
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsDetail()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            await SeedPositionsAsync(context);
            var pos = await context.JobPositions.FirstAsync(j => j.Code == "POS_TELLER");

            var result = await service.GetByIdAsync(pos.Id);

            Assert.NotNull(result);
            Assert.Equal("POS_TELLER", result.Code);
            Assert.Equal("Giao dich vien quay", result.Title);
            Assert.Equal("EP-TELLER", result.EvaluationProfileCode);
            Assert.Equal("ACTIVE", result.Status);
        }
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingId_ReturnsNull()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var result = await service.GetByIdAsync(Guid.NewGuid());
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task JobPositionController_GetJobPositions_ReturnsOkWithPagedResult()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            await SeedPositionsAsync(context);
            var controller = new JobPositionController(service)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-jp-list" },
                },
            };

            var actionResult = await controller.GetJobPositions(new JobPositionFilterDto(), CancellationToken.None);
            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            var envelope = Assert.IsType<ApiResponse<PagedResult<JobPositionDetailDto>>>(okResult.Value);

            Assert.True(envelope.Success);
            Assert.Equal(3, envelope.Data!.TotalCount);
            Assert.Equal("trace-jp-list", envelope.TraceId);
        }
    }

    [Fact]
    public async Task JobPositionController_GetById_WhenFound_ReturnsOk()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            await SeedPositionsAsync(context);
            var pos = await context.JobPositions.FirstAsync(j => j.Code == "POS_RBO");
            var controller = new JobPositionController(service)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-jp-id" },
                },
            };

            var actionResult = await controller.GetById(pos.Id, CancellationToken.None);
            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            var envelope = Assert.IsType<ApiResponse<JobPositionDetailDto>>(okResult.Value);

            Assert.True(envelope.Success);
            Assert.Equal("POS_RBO", envelope.Data!.Code);
        }
    }

    [Fact]
    public async Task JobPositionController_GetById_WhenNotFound_ReturnsNotFound()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var controller = new JobPositionController(service)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-jp-404" },
                },
            };

            var actionResult = await controller.GetById(Guid.NewGuid(), CancellationToken.None);
            var notFoundResult = Assert.IsType<NotFoundObjectResult>(actionResult.Result);
            var envelope = Assert.IsType<ApiResponse>(notFoundResult.Value);

            Assert.False(envelope.Success);
            Assert.Equal(ErrorCodes.NotFound, envelope.ErrorCode);
        }
    }
}
