using System.Security.Claims;
using System.Text;
using G54.Api.Controllers;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Submission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class ResultSubmissionServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static (AppDbContext Context, IUnitOfWork Uow, ResultSubmissionService Service, StorageService Storage, EventBus Bus)
        CreateTestEnvironment(string? storagePath = null)
    {
        var context = CreateInMemoryDbContext();
        var uow = new UnitOfWork(context);

        var tempDir = storagePath ?? Path.Combine(Path.GetTempPath(), "G54_Tests_" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:EvidencePath"] = tempDir,
            })
            .Build();

        var storage = new StorageService(config, NullLogger<StorageService>.Instance);
        var validator = new WorkSubmissionValidator(uow);
        var cache = new MemoryDistributedCache();
        var bus = new EventBus(cache, NullLogger<EventBus>.Instance);

        var service = new ResultSubmissionService(
            uow,
            storage,
            validator,
            bus,
            NullLogger<ResultSubmissionService>.Instance);

        return (context, uow, service, storage, bus);
    }

    private static async Task<(Employee Assignee, WorkType Wt, Work Work, KpiDefinition Kpi1, KpiDefinition KpiRatio)>
        SeedWorkContextAsync(AppDbContext context, WorkStatus workStatus = WorkStatus.InProgress)
    {
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-CARD",
            Name = "The Tin Dung",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
        };
        var jobPosition = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "POS-TELLER",
            Title = "Teller",
            Status = RecordStatus.Active,
        };
        var assignee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "EMP001",
            FullName = "Nguyen Tien Thanh",
            Email = "thanh.nguyen@bank.com",
            DepartmentId = department.Id,
            JobPositionId = jobPosition.Id,
            Status = EmployeeStatus.Active,
        };
        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = assignee.Id,
            Username = "thanhnt",
            PasswordHash = "hash",
            IsLocked = false,
        };

        var workType = new WorkType
        {
            Id = Guid.NewGuid(),
            Code = "WT-CARD",
            Name = "Mo the tin dung",
            Status = RecordStatus.Active,
        };

        var kpiGroup = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "GRP-CARD",
            Name = "The tin dung",
        };

        var kpi1 = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            Code = "KPI-CARD-01",
            Name = "So the phat hanh",
            Unit = "The",
            KpiGroupId = kpiGroup.Id,
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
        };

        var kpiRatio = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            Code = "KPI-CARD-RATIO",
            Name = "Ty le the kich hoat",
            Unit = "%",
            KpiGroupId = kpiGroup.Id,
            AggregationType = KpiAggregationType.Ratio,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
        };

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var wtk1 = new WorkTypeKpi
        {
            Id = Guid.NewGuid(),
            WorkTypeId = workType.Id,
            KpiDefinitionId = kpi1.Id,
            IsRequired = true,
            EffectiveFrom = today.AddDays(-10),
            EffectiveTo = null,
        };

        var wtkRatio = new WorkTypeKpi
        {
            Id = Guid.NewGuid(),
            WorkTypeId = workType.Id,
            KpiDefinitionId = kpiRatio.Id,
            IsRequired = false,
            EffectiveFrom = today.AddDays(-10),
            EffectiveTo = null,
        };

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "WRK-2026-001",
            Title = "Mo the tin dung ca nhan",
            WorkTypeId = workType.Id,
            AssigneeEmployeeId = assignee.Id,
            CreatedBy = assignee.Id,
            Status = workStatus,
            Priority = WorkPriority.High,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await context.Departments.AddAsync(department);
        await context.JobPositions.AddAsync(jobPosition);
        await context.Employees.AddAsync(assignee);
        await context.Accounts.AddAsync(account);
        await context.WorkTypes.AddAsync(workType);
        await context.KpiGroups.AddAsync(kpiGroup);
        await context.KpiDefinitions.AddRangeAsync(kpi1, kpiRatio);
        await context.WorkTypeKpis.AddRangeAsync(wtk1, wtkRatio);
        await context.Works.AddAsync(work);
        await context.SaveChangesAsync();

        return (assignee, workType, work, kpi1, kpiRatio);
    }

    [Fact]
    public async Task GetSubmissionContextAsync_ValidWork_ReturnsContextWithKpisAndNextSubmissionNo()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, workType, work, kpi1, _) = await SeedWorkContextAsync(context);

            var result = await service.GetSubmissionContextAsync(work.Id, assignee.Id);

            Assert.NotNull(result);
            Assert.Equal(work.Id, result.WorkId);
            Assert.Equal(work.Code, result.WorkCode);
            Assert.Equal(workType.Id, result.WorkTypeId);
            Assert.Equal(1, result.NextSubmissionNo);
            Assert.Equal(0, result.PreviousSubmissionsCount);
            Assert.Equal(2, result.KpiCriteria.Count);

            var reqKpi = result.KpiCriteria.FirstOrDefault(k => k.KpiDefinitionId == kpi1.Id);
            Assert.NotNull(reqKpi);
            Assert.True(reqKpi.IsRequired);
            Assert.Equal("KPI-CARD-01", reqKpi.Code);
            Assert.NotEmpty(result.AllowedFileTypes);
        }
    }

    [Fact]
    public async Task GetSubmissionContextAsync_WorkNotFound_ThrowsNotFoundException()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, _, _, _) = await SeedWorkContextAsync(context);
            var nonExistentWorkId = Guid.NewGuid();

            await Assert.ThrowsAsync<NotFoundException>(() =>
                service.GetSubmissionContextAsync(nonExistentWorkId, assignee.Id));
        }
    }

    [Fact]
    public async Task GetSubmissionContextAsync_NotAssignee_ThrowsForbiddenException()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, work, _, _) = await SeedWorkContextAsync(context);
            var otherEmployee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP999",
                FullName = "Other Employee",
                Email = "other@bank.com",
                DepartmentId = Guid.NewGuid(),
                JobPositionId = Guid.NewGuid(),
                Status = EmployeeStatus.Active,
            };
            await context.Employees.AddAsync(otherEmployee);
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<ForbiddenException>(() =>
                service.GetSubmissionContextAsync(work.Id, otherEmployee.Id));
        }
    }

    [Fact]
    public async Task CreateSubmissionAsync_ValidDataWithoutFiles_CreatesSubmissionAndUpdatesWorkStatus()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, kpi1, _) = await SeedWorkContextAsync(context);

            var request = new CreateWorkSubmissionRequestDto
            {
                Comment = "Hoan thanh 15 the JCB cho khach hang ca nhan.",
                MetricItems = new List<SubmissionMetricInputDto>
                {
                    new(kpi1.Id, 15m, null),
                },
            };

            var result = await service.CreateSubmissionAsync(work.Id, assignee.Id, request);

            Assert.NotNull(result);
            Assert.Equal(work.Id, result.WorkId);
            Assert.Equal(1, result.SubmissionNo);
            Assert.Equal("SUBMITTED", result.Status);
            Assert.Equal(1, result.MetricCount);
            Assert.Equal(0, result.EvidenceCount);

            var updatedWork = await context.Works.FindAsync(work.Id);
            Assert.NotNull(updatedWork);
            Assert.Equal(WorkStatus.Submitted, updatedWork.Status);

            var submission = await context.ResultSubmissions.FindAsync(result.SubmissionId);
            Assert.NotNull(submission);
            Assert.Equal(SubmissionStatus.Submitted, submission.Status);
            Assert.Equal(1, submission.SubmissionNo);
            Assert.Contains("Hoan thanh 15 the JCB", submission.Comment);
        }
    }

    [Fact]
    public async Task CreateSubmissionAsync_ValidDataWithFiles_UploadsFilesAndCreatesEvidences()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "G54_Tests_Files_" + Guid.NewGuid().ToString("N"));
        var (context, _, service, _, _) = CreateTestEnvironment(tempFolder);
        using (context)
        {
            try
            {
                var (assignee, _, work, kpi1, _) = await SeedWorkContextAsync(context);

                var fileBytes = Encoding.UTF8.GetBytes("fake excel sheet content");
                var mockFile = new FormFile(
                    baseStream: new MemoryStream(fileBytes),
                    baseStreamOffset: 0,
                    length: fileBytes.Length,
                    name: "Files",
                    fileName: "danh_sach_the.xlsx")
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                };

                var request = new CreateWorkSubmissionRequestDto
                {
                    Comment = "Nop kem bao cao doi chieu CIF",
                    MetricItems = new List<SubmissionMetricInputDto>
                    {
                        new(kpi1.Id, 20m, null),
                    },
                    Files = new List<IFormFile> { mockFile },
                };

                var result = await service.CreateSubmissionAsync(work.Id, assignee.Id, request);

                Assert.NotNull(result);
                Assert.Equal(1, result.EvidenceCount);

                var evidences = await context.Evidences
                    .Where(e => e.ResultSubmissionId == result.SubmissionId)
                    .ToListAsync();
                Assert.Single(evidences);
                Assert.Equal("danh_sach_the.xlsx", evidences[0].FileName);
                Assert.Equal(fileBytes.Length, evidences[0].FileSize);
            }
            finally
            {
                if (Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, recursive: true);
                }
            }
        }
    }

    [Fact]
    public async Task CreateSubmissionAsync_MissingRequiredKpi_ThrowsValidationException()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, _, _) = await SeedWorkContextAsync(context);

            var request = new CreateWorkSubmissionRequestDto
            {
                Comment = "Khong nhap KPI bat buoc",
                MetricItems = new List<SubmissionMetricInputDto>(),
            };

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.CreateSubmissionAsync(work.Id, assignee.Id, request));

            Assert.NotNull(ex.Errors);
            Assert.Contains(ex.Errors, e => e.Contains("bắt buộc"));
        }
    }

    [Fact]
    public async Task CreateSubmissionAsync_InvalidRatioDenominator_ThrowsValidationException()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, kpi1, kpiRatio) = await SeedWorkContextAsync(context);

            var request = new CreateWorkSubmissionRequestDto
            {
                Comment = "Ratio thieu denominator",
                MetricItems = new List<SubmissionMetricInputDto>
                {
                    new(kpi1.Id, 10m, null),
                    new(kpiRatio.Id, 0.85m, 0m), // denominator <= 0
                },
            };

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.CreateSubmissionAsync(work.Id, assignee.Id, request));

            Assert.NotNull(ex.Errors);
            Assert.Contains(ex.Errors, e => e.Contains("mẫu số (denominator) lớn hơn 0"));
        }
    }

    [Fact]
    public async Task CreateSubmissionAsync_NegativeKpiValue_ThrowsValidationException()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, kpi1, _) = await SeedWorkContextAsync(context);

            var request = new CreateWorkSubmissionRequestDto
            {
                Comment = "Gia tri am",
                MetricItems = new List<SubmissionMetricInputDto>
                {
                    new(kpi1.Id, -5m, null),
                },
            };

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.CreateSubmissionAsync(work.Id, assignee.Id, request));

            Assert.NotNull(ex.Errors);
            Assert.Contains(ex.Errors, e => e.Contains("không được là số âm"));
        }
    }

    [Fact]
    public async Task CreateSubmissionAsync_WorkAlreadySubmitted_ThrowsConflictSubmissionInvalidState()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, kpi1, _) = await SeedWorkContextAsync(context, WorkStatus.Submitted);

            var request = new CreateWorkSubmissionRequestDto
            {
                Comment = "Thu nop lai khi dang cho",
                MetricItems = new List<SubmissionMetricInputDto>
                {
                    new(kpi1.Id, 10m, null),
                },
            };

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.CreateSubmissionAsync(work.Id, assignee.Id, request));

            Assert.Equal(ErrorCodes.SubmissionInvalidState, ex.ErrorCode);
            Assert.Equal(409, ex.StatusCode);
            Assert.Contains("SUBMITTED", ex.Message);
        }
    }

    [Fact]
    public async Task CreateSubmissionAsync_WorkDoneOrCancelled_ThrowsConflictSubmissionInvalidState()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, kpi1, _) = await SeedWorkContextAsync(context, WorkStatus.Done);

            var request = new CreateWorkSubmissionRequestDto
            {
                Comment = "Nop cong viec da hoan thanh",
                MetricItems = new List<SubmissionMetricInputDto>
                {
                    new(kpi1.Id, 10m, null),
                },
            };

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.CreateSubmissionAsync(work.Id, assignee.Id, request));

            Assert.Equal(ErrorCodes.SubmissionInvalidState, ex.ErrorCode);
            Assert.Contains("không thể nộp kết quả", ex.Message);
        }
    }

    [Fact]
    public async Task CreateSubmissionAsync_NotAssignee_ThrowsForbiddenException()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, work, kpi1, _) = await SeedWorkContextAsync(context);
            var otherEmployee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP888",
                FullName = "Another Employee",
                Email = "other2@bank.com",
                DepartmentId = Guid.NewGuid(),
                JobPositionId = Guid.NewGuid(),
                Status = EmployeeStatus.Active,
            };
            await context.Employees.AddAsync(otherEmployee);
            await context.SaveChangesAsync();

            var request = new CreateWorkSubmissionRequestDto
            {
                Comment = "Nguoi khac nop",
                MetricItems = new List<SubmissionMetricInputDto>
                {
                    new(kpi1.Id, 10m, null),
                },
            };

            var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
                service.CreateSubmissionAsync(work.Id, otherEmployee.Id, request));

            Assert.Equal(403, ex.StatusCode);
            Assert.Contains("Chỉ người được giao việc", ex.Message);
        }
    }

    [Fact]
    public async Task GetSubmissionsByWorkIdAsync_ReturnsAllSubmissionsWithMetrics()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, kpi1, _) = await SeedWorkContextAsync(context);

            var request1 = new CreateWorkSubmissionRequestDto
            {
                Comment = "Dot 1",
                MetricItems = new List<SubmissionMetricInputDto> { new(kpi1.Id, 10m, null) },
            };
            await service.CreateSubmissionAsync(work.Id, assignee.Id, request1);

            // Revert work to InProgress for 2nd submission
            work.Status = WorkStatus.InProgress;
            await context.SaveChangesAsync();

            var request2 = new CreateWorkSubmissionRequestDto
            {
                Comment = "Dot 2",
                MetricItems = new List<SubmissionMetricInputDto> { new(kpi1.Id, 15m, null) },
            };
            await service.CreateSubmissionAsync(work.Id, assignee.Id, request2);

            var submissions = await service.GetSubmissionsByWorkIdAsync(work.Id, assignee.Id);

            Assert.Equal(2, submissions.Count);
            Assert.Equal(2, submissions[0].SubmissionNo);
            Assert.Equal("Dot 2", submissions[0].Comment);
            Assert.Equal(1, submissions[1].SubmissionNo);
            Assert.Equal("Dot 1", submissions[1].Comment);
        }
    }

    [Fact]
    public async Task GetSubmissionByIdAsync_ExistingAndNonExisting()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, kpi1, _) = await SeedWorkContextAsync(context);

            var request = new CreateWorkSubmissionRequestDto
            {
                Comment = "Chi tiet submission",
                MetricItems = new List<SubmissionMetricInputDto> { new(kpi1.Id, 30m, null) },
            };
            var createResult = await service.CreateSubmissionAsync(work.Id, assignee.Id, request);

            var detail = await service.GetSubmissionByIdAsync(createResult.SubmissionId, assignee.Id);
            Assert.NotNull(detail);
            Assert.Equal(createResult.SubmissionId, detail.Id);
            Assert.Equal("Chi tiet submission", detail.Comment);
            Assert.Single(detail.Metrics);
            Assert.Equal(30m, detail.Metrics[0].Value);
            Assert.Equal("KPI-CARD-01", detail.Metrics[0].KpiCode);

            await Assert.ThrowsAsync<NotFoundException>(() =>
                service.GetSubmissionByIdAsync(Guid.NewGuid(), assignee.Id));
        }
    }

    [Fact]
    public async Task WithdrawSubmissionAsync_ValidSubmittedState_RevertsWorkToInProgress()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, kpi1, _) = await SeedWorkContextAsync(context);

            var request = new CreateWorkSubmissionRequestDto
            {
                Comment = "Nop truoc khi thu hoi",
                MetricItems = new List<SubmissionMetricInputDto> { new(kpi1.Id, 12m, null) },
            };
            var createResult = await service.CreateSubmissionAsync(work.Id, assignee.Id, request);

            var withdrawResult = await service.WithdrawSubmissionAsync(createResult.SubmissionId, assignee.Id);

            Assert.NotNull(withdrawResult);
            Assert.Equal("WITHDRAWN", withdrawResult.Status);

            var updatedWork = await context.Works.FindAsync(work.Id);
            Assert.NotNull(updatedWork);
            Assert.Equal(WorkStatus.InProgress, updatedWork.Status);

            var updatedSub = await context.ResultSubmissions.FindAsync(createResult.SubmissionId);
            Assert.NotNull(updatedSub);
            Assert.Equal(SubmissionStatus.Withdrawn, updatedSub.Status);
        }
    }

    [Fact]
    public async Task WithdrawSubmissionAsync_AlreadyWithdrawn_ThrowsConflictSubmissionInvalidState()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, kpi1, _) = await SeedWorkContextAsync(context);

            var request = new CreateWorkSubmissionRequestDto
            {
                Comment = "Nop roi thu hoi hai lan",
                MetricItems = new List<SubmissionMetricInputDto> { new(kpi1.Id, 12m, null) },
            };
            var createResult = await service.CreateSubmissionAsync(work.Id, assignee.Id, request);
            await service.WithdrawSubmissionAsync(createResult.SubmissionId, assignee.Id);

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.WithdrawSubmissionAsync(createResult.SubmissionId, assignee.Id));

            Assert.Equal(ErrorCodes.SubmissionInvalidState, ex.ErrorCode);
            Assert.Contains("WITHDRAWN", ex.Message);
        }
    }

    [Fact]
    public async Task WithdrawSubmissionAsync_NotSubmitter_ThrowsForbiddenException()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, kpi1, _) = await SeedWorkContextAsync(context);

            var request = new CreateWorkSubmissionRequestDto
            {
                Comment = "Nop",
                MetricItems = new List<SubmissionMetricInputDto> { new(kpi1.Id, 12m, null) },
            };
            var createResult = await service.CreateSubmissionAsync(work.Id, assignee.Id, request);

            var other = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP002",
                FullName = "Other",
                Email = "other3@bank.com",
                DepartmentId = Guid.NewGuid(),
                JobPositionId = Guid.NewGuid(),
                Status = EmployeeStatus.Active,
            };
            await context.Employees.AddAsync(other);
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<ForbiddenException>(() =>
                service.WithdrawSubmissionAsync(createResult.SubmissionId, other.Id));
        }
    }

    [Fact]
    public async Task StorageService_UploadEvidenceFilesAsync_InvalidExtension_ThrowsValidationException()
    {
        var (context, _, _, storage, _) = CreateTestEnvironment();
        using (context)
        {
            var fileBytes = Encoding.UTF8.GetBytes("fake exe");
            var mockFile = new FormFile(
                new MemoryStream(fileBytes),
                0,
                fileBytes.Length,
                "Files",
                "malicious.exe");

            await Assert.ThrowsAsync<ValidationException>(() =>
                storage.UploadEvidenceFilesAsync(new[] { mockFile }));
        }
    }

    [Fact]
    public async Task StorageService_UploadEvidenceFilesAsync_EmptyFile_ThrowsValidationException()
    {
        var (context, _, _, storage, _) = CreateTestEnvironment();
        using (context)
        {
            var mockFile = new FormFile(
                new MemoryStream(),
                0,
                0,
                "Files",
                "empty.pdf");

            await Assert.ThrowsAsync<ValidationException>(() =>
                storage.UploadEvidenceFilesAsync(new[] { mockFile }));
        }
    }

    [Fact]
    public async Task ResultSubmissionsController_GetSubmissionContext_ReturnsOk()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, _, _) = await SeedWorkContextAsync(context);

            var controller = new ResultSubmissionsController(service)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            new[] { new Claim("sub", assignee.Id.ToString()) },
                            "TestAuth")),
                    },
                },
            };

            var actionResult = await controller.GetSubmissionContext(work.Id, CancellationToken.None);
            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            var apiResponse = Assert.IsType<ApiResponse<SubmissionContextDto>>(okResult.Value);
            Assert.True(apiResponse.Success);
            Assert.NotNull(apiResponse.Data);
            Assert.Equal(work.Id, apiResponse.Data.WorkId);
        }
    }

    [Fact]
    public async Task ResultSubmissionsController_CreateSubmission_ReturnsCreated()
    {
        var (context, _, service, _, _) = CreateTestEnvironment();
        using (context)
        {
            var (assignee, _, work, kpi1, _) = await SeedWorkContextAsync(context);

            var controller = new ResultSubmissionsController(service)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            new[] { new Claim("sub", assignee.Id.ToString()) },
                            "TestAuth")),
                    },
                },
            };

            var request = new CreateWorkSubmissionRequestDto
            {
                Comment = "Controller create test",
                MetricItems = new List<SubmissionMetricInputDto> { new(kpi1.Id, 5m, null) },
            };

            var actionResult = await controller.CreateSubmission(work.Id, request, CancellationToken.None);
            var createdResult = Assert.IsType<CreatedAtActionResult>(actionResult.Result);
            var apiResponse = Assert.IsType<ApiResponse<SubmissionResultDto>>(createdResult.Value);
            Assert.True(apiResponse.Success);
            Assert.NotNull(apiResponse.Data);
            Assert.Equal("SUBMITTED", apiResponse.Data.Status);
        }
    }

    private sealed class MemoryDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _cache = new();

        public byte[]? Get(string key) => _cache.TryGetValue(key, out var val) ? val : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            _cache[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _cache.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
