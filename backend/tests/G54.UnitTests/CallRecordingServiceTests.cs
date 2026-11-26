using System.Security.Claims;
using System.Security.Cryptography;
using G54.Api.Controllers;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.CallRecording;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

/// <summary>Upload, read and stream of call recordings (docs/api/CALL_RECORDING_AI_TRIAGE_CONTRACT.md §2–§4).</summary>
public sealed class CallRecordingServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 3, 0, 0, TimeSpan.Zero);
    private static readonly byte[] M4a = [0x00, 0x00, 0x00, 0x20, .. "ftypM4A audio content"u8];
    private static readonly UserPermissionDto PlayAll = new("call_recording", "play", "All", "CALL_RECORDING_PLAY");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "G54_CallRecording_" + Guid.NewGuid().ToString("N"));
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly Guid _departmentId = Guid.NewGuid();

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private CallRecordingService ServiceFor(
        UserPermissionDto[]? grants = null,
        CallRecordingOptions? options = null,
        DateTimeOffset? now = null) => new(
        _db,
        new FakePermissionService(null, grants ?? []),
        new StorageService(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:EvidencePath"] = _folder })
                .Build(),
            NullLogger<StorageService>.Instance),
        Options.Create(options ?? new CallRecordingOptions()),
        new FixedTime(now ?? Now),
        NullLogger<CallRecordingService>.Instance);

    private static UploadCallRecordingRequestDto Request(string name, byte[] content, string? productCode = null) => new()
    {
        File = new FormFile(new MemoryStream(content), 0, content.Length, "file", name),
        ProductCode = productCode,
    };

    private async Task<Employee> AddEmployeeAsync(string code)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = code,
            Email = $"{code}@bank.test",
            DepartmentId = _departmentId,
            JobPositionId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        _db.Employees.Add(employee);
        _db.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = code,
            PasswordHash = "hash",
            LastSyncAt = Now,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync();
        return employee;
    }

    /// <summary>A submission of <paramref name="submitter"/> on a work of type TERM_DEPOSIT with one matched transaction.</summary>
    private async Task<ResultSubmission> SeedSubmissionAsync(Employee submitter, SubmissionStatus status = SubmissionStatus.Submitted)
    {
        var workType = new WorkType { Id = Guid.NewGuid(), Code = "WT", Name = "Tiền gửi", ProductCode = "TERM_DEPOSIT", UpdatedAt = Now };
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "W-1",
            Title = "Work",
            WorkTypeId = workType.Id,
            AssigneeEmployeeId = submitter.Id,
            Status = WorkStatus.Submitted,
            Priority = WorkPriority.Medium,
            UpdatedAt = Now,
        };
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = submitter.Id,
            SubmissionNo = 1,
            Status = status,
            SubmittedAt = Now,
            UpdatedAt = Now,
        };
        _db.WorkTypes.Add(workType);
        _db.Works.Add(work);
        _db.ResultSubmissions.Add(submission);
        _db.WorkTransactions.AddRange(
            new WorkTransaction { Id = Guid.NewGuid(), WorkId = work.Id, TransactionCode = "GD-REVIEW", Status = WorkTransactionStatus.InReview, CreatedAt = Now.AddHours(-2), UpdatedAt = Now },
            new WorkTransaction { Id = Guid.NewGuid(), WorkId = work.Id, TransactionCode = "GD2610001", Status = WorkTransactionStatus.Matched, CreatedAt = Now.AddHours(-1), UpdatedAt = Now });
        await _db.SaveChangesAsync();
        return submission;
    }

    private async Task MarkReadyAsync(Guid recordingId)
    {
        var recording = await _db.CallRecordings.SingleAsync(r => r.Id == recordingId);
        recording.ProcessingStatus = CallRecordingStatus.Ready;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Upload_ByTheSubmitter_StoresTheFileWithItsHashAndSnapshotsTheWork()
    {
        var submitter = await AddEmployeeAsync("NV001");
        var submission = await SeedSubmissionAsync(submitter);

        var dto = await ServiceFor().UploadAsync(submission.Id, submitter.Id, Request("goi-khach-1.m4a", M4a));

        var row = await _db.CallRecordings.SingleAsync();
        Assert.Equal($"call-recordings/2026/11/{dto.Id}.m4a", row.FilePath);
        Assert.Equal(M4a, await File.ReadAllBytesAsync(Path.Combine(_folder, row.FilePath)));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(M4a)).ToLowerInvariant(), row.Sha256);
        Assert.Equal(Now.AddYears(1), row.RetainUntil);
        Assert.Equal(submitter.Id, row.UploadedBy);
        Assert.Equal(submission.WorkId, dto.WorkId);
        Assert.Equal("goi-khach-1.m4a", dto.FileName);
        Assert.Equal("audio/mp4", dto.MimeType);
        Assert.Equal(M4a.Length, dto.SizeBytes);
        Assert.Equal("TERM_DEPOSIT", dto.ProductCode);
        Assert.Equal("GD2610001", dto.TransactionRef);
        Assert.Equal("PENDING", dto.ProcessingStatus);
        Assert.Equal(0, dto.FlagCount);
    }

    [Fact]
    public async Task Upload_WithAProductCode_OverridesTheWorkTypeDefault()
    {
        var submitter = await AddEmployeeAsync("NV001");
        var submission = await SeedSubmissionAsync(submitter);

        var dto = await ServiceFor().UploadAsync(submission.Id, submitter.Id, Request("a.m4a", M4a, " SAVINGS "));

        Assert.Equal("SAVINGS", dto.ProductCode);
    }

    [Fact]
    public async Task Upload_WhenAProductCodeIsRequiredButNoneIsKnown_IsRejected()
    {
        var submitter = await AddEmployeeAsync("NV001");
        var submission = await SeedSubmissionAsync(submitter);
        (await _db.WorkTypes.SingleAsync()).ProductCode = null;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => ServiceFor(options: new CallRecordingOptions { RequireProductCode = true })
            .UploadAsync(submission.Id, submitter.Id, Request("a.m4a", M4a)));
        Assert.Empty(_db.CallRecordings);
    }

    [Fact]
    public async Task Upload_ByAnotherUser_IsForbidden_EvenWithThePlayPermission()
    {
        var submission = await SeedSubmissionAsync(await AddEmployeeAsync("NV001"));
        var qa = await AddEmployeeAsync("QA01");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceFor([PlayAll]).UploadAsync(submission.Id, qa.Id, Request("a.m4a", M4a)));
    }

    [Fact]
    public async Task Upload_ToAWithdrawnSubmission_IsAConflict()
    {
        var submitter = await AddEmployeeAsync("NV001");
        var submission = await SeedSubmissionAsync(submitter, SubmissionStatus.Withdrawn);

        var thrown = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceFor().UploadAsync(submission.Id, submitter.Id, Request("a.m4a", M4a)));

        Assert.Equal(ErrorCodes.CallRecordingSubmissionClosed, thrown.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, thrown.StatusCode);
    }

    public static TheoryData<string, byte[]> InvalidFiles => new()
    {
        { "a.pdf", "%PDF-1.7"u8.ToArray() },
        { "a.exe", M4a },
        { "spoofed.mp3", "MZ not audio at all"u8.ToArray() },
        { "spoofed.wav", M4a },
        { "empty.m4a", [] },
    };

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public async Task Upload_WithAnInvalidFile_IsRejectedAndNothingIsStored(string name, byte[] content)
    {
        var submitter = await AddEmployeeAsync("NV001");
        var submission = await SeedSubmissionAsync(submitter);

        var thrown = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceFor().UploadAsync(submission.Id, submitter.Id, Request(name, content)));

        Assert.Equal(ErrorCodes.CallRecordingInvalidFile, thrown.ErrorCode);
        Assert.Equal(StatusCodes.Status400BadRequest, thrown.StatusCode);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public async Task Upload_OverTheConfiguredSize_IsRejected()
    {
        var submitter = await AddEmployeeAsync("NV001");
        var submission = await SeedSubmissionAsync(submitter);
        var oversized = new UploadCallRecordingRequestDto
        {
            File = new FormFile(new MemoryStream(M4a), 0, CallRecordingOptions.MaxUploadBytes + 1, "file", "big.m4a"),
        };

        var thrown = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceFor().UploadAsync(submission.Id, submitter.Id, oversized));

        Assert.Equal(ErrorCodes.CallRecordingInvalidFile, thrown.ErrorCode);
    }

    [Theory]
    [InlineData(".mp3", new byte[] { 0x49, 0x44, 0x33, 0x04 })]
    [InlineData(".MP3", new byte[] { 0xFF, 0xFB, 0x90, 0x64 })]
    [InlineData(".mp4", new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D })]
    [InlineData(".wav", new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45 })]
    public void Validator_AcceptsTheSignatureOfEachAudioType(string extension, byte[] header) =>
        Assert.True(CallRecordingFileValidator.HasValidSignature(extension, header));

    [Fact]
    public async Task GetBySubmission_ListsNewestFirst_ForTheSubmitterAndHoldersOfThePlayPermission()
    {
        var submitter = await AddEmployeeAsync("NV001");
        var qa = await AddEmployeeAsync("QA01");
        var submission = await SeedSubmissionAsync(submitter);
        var first = await ServiceFor().UploadAsync(submission.Id, submitter.Id, Request("1.m4a", M4a));
        var later = await ServiceFor(now: Now.AddMinutes(5)).UploadAsync(submission.Id, submitter.Id, Request("2.m4a", M4a));

        var mine = await ServiceFor().GetBySubmissionAsync(submission.Id, submitter.Id);
        var qaView = await ServiceFor([PlayAll]).GetBySubmissionAsync(submission.Id, qa.Id);

        Assert.Equal([later.Id, first.Id], mine.Select(r => r.Id));
        Assert.Equal([later.Id, first.Id], qaView.Select(r => r.Id));
    }

    [Fact]
    public async Task Reads_ByAnotherUserWithoutAllScopePlay_AreForbidden()
    {
        var submitter = await AddEmployeeAsync("NV001");
        var colleague = await AddEmployeeAsync("NV002");
        var submission = await SeedSubmissionAsync(submitter);
        var recording = await ServiceFor().UploadAsync(submission.Id, submitter.Id, Request("a.m4a", M4a));
        var deptOnly = ServiceFor([new UserPermissionDto("call_recording", "play", "Dept", "CALL_RECORDING_PLAY")]);

        await Assert.ThrowsAsync<ForbiddenException>(() => deptOnly.GetBySubmissionAsync(submission.Id, colleague.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => deptOnly.GetAsync(recording.Id, colleague.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => deptOnly.OpenStreamAsync(recording.Id, colleague.Id));
    }

    [Fact]
    public async Task Stream_WhilePending_IsAConflict()
    {
        var submitter = await AddEmployeeAsync("NV001");
        var submission = await SeedSubmissionAsync(submitter);
        var recording = await ServiceFor().UploadAsync(submission.Id, submitter.Id, Request("a.m4a", M4a));

        var thrown = await Assert.ThrowsAsync<BusinessRuleException>(() => ServiceFor().OpenStreamAsync(recording.Id, submitter.Id));

        Assert.Equal(ErrorCodes.CallRecordingNotReady, thrown.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, thrown.StatusCode);
    }

    [Fact]
    public async Task Controller_Upload_Answers201WithTheRecordingLocation()
    {
        var submitter = await AddEmployeeAsync("NV001");
        var submission = await SeedSubmissionAsync(submitter);

        var result = await ControllerFor(ServiceFor(), submitter.Id).Upload(submission.Id, Request("a.m4a", M4a), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<CallRecordingDto>>(created.Value);
        Assert.Equal(StatusCodes.Status201Created, envelope.Status);
        Assert.Equal(nameof(CallRecordingsController.GetCallRecording), created.ActionName);
        Assert.Equal(envelope.Data!.Id, created.RouteValues!["id"]);
    }

    [Fact]
    public async Task Controller_StreamOfAReadyRecording_AnswersARangeWith206()
    {
        var submitter = await AddEmployeeAsync("NV001");
        var submission = await SeedSubmissionAsync(submitter);
        var recording = await ServiceFor().UploadAsync(submission.Id, submitter.Id, Request("a.m4a", M4a));
        await MarkReadyAsync(recording.Id);
        var controller = ControllerFor(ServiceFor(), submitter.Id);
        var http = controller.HttpContext;
        http.RequestServices = new ServiceCollection().AddLogging().AddMvcCore().Services.BuildServiceProvider();
        http.Request.Method = HttpMethods.Get;
        http.Request.Headers.Range = "bytes=0-3";
        using var body = new MemoryStream();
        http.Response.Body = body;

        var result = await controller.Stream(recording.Id, CancellationToken.None);
        await result.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));

        Assert.Equal(StatusCodes.Status206PartialContent, http.Response.StatusCode);
        Assert.Equal("bytes", http.Response.Headers.AcceptRanges.ToString());
        Assert.Equal("audio/mp4", http.Response.ContentType);
        Assert.Equal("private, no-store", http.Response.Headers.CacheControl.ToString());
        Assert.Equal(M4a[..4], body.ToArray());
    }

    private static CallRecordingsController ControllerFor(CallRecordingService service, Guid userId) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "TestAuth")),
            },
        },
    };

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
