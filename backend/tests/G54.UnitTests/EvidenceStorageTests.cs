using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class StorageServiceTests : IDisposable
{
    private static readonly byte[] PdfBytes = "%PDF-1.7 evidence"u8.ToArray();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "G54_Storage_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private StorageService CreateStorage(string? folder = null) => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:EvidencePath"] = folder ?? _folder })
            .Build(),
        NullLogger<StorageService>.Instance);

    private static FormFile File(string name, byte[] content, string contentType = "application/octet-stream") =>
        new(new MemoryStream(content), 0, content.Length, "Files", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };

    [Fact]
    public async Task Upload_ThenOpen_RoundTripsTheFileAndStoresOnlyItsName()
    {
        var storage = CreateStorage();

        var stored = Assert.Single(await storage.UploadEvidenceFilesAsync([File("Báo cáo.pdf", PdfBytes)]));

        Assert.Equal("Báo cáo.pdf", stored.FileName);
        Assert.Equal("application/pdf", stored.MimeType);
        Assert.Equal(PdfBytes.Length, stored.FileSize);
        Assert.Equal(Path.GetFileName(stored.FilePath), stored.FilePath);
        Assert.EndsWith(".pdf", stored.FilePath, StringComparison.Ordinal);
        await using var content = await storage.OpenEvidenceReadAsync(stored.FilePath);
        using var memory = new MemoryStream();
        await content.CopyToAsync(memory);
        Assert.Equal(PdfBytes, memory.ToArray());
    }

    [Fact]
    public async Task Upload_IgnoresTheClientContentTypeAndUsesTheVerifiedExtension()
    {
        var stored = Assert.Single(await CreateStorage().UploadEvidenceFilesAsync(
            [File("a.pdf", PdfBytes, "text/html")]));

        Assert.Equal("application/pdf", stored.MimeType);
    }

    [Theory]
    [InlineData("a.pdf", new byte[] { 0x50, 0x4B, 0x03, 0x04 })]
    [InlineData("a.png", new byte[] { 0x25, 0x50, 0x44, 0x46 })]
    [InlineData("a.jpg", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData("a.xlsx", new byte[] { 0x25, 0x50, 0x44, 0x46 })]
    [InlineData("a.doc", new byte[] { 0x50, 0x4B, 0x03, 0x04 })]
    [InlineData("a.pdf", new byte[] { 0x4D, 0x5A, 0x90, 0x00 })]
    public async Task Upload_WithContentThatDoesNotMatchTheExtension_IsRejectedAndNothingIsStored(string name, byte[] content)
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<ValidationException>(() => storage.UploadEvidenceFilesAsync([File(name, content)]));

        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any());
    }

    [Theory]
    [InlineData("a.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 })]
    [InlineData("a.PNG", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData("a.jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData("a.docx", new byte[] { 0x50, 0x4B, 0x03, 0x04 })]
    [InlineData("a.xls", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 })]
    public async Task Upload_WithMatchingContent_IsAccepted(string name, byte[] content)
    {
        Assert.Single(await CreateStorage().UploadEvidenceFilesAsync([File(name, content)]));
    }

    [Fact]
    public async Task Upload_WithAFileOverTheLimit_IsRejected()
    {
        var oversized = new FormFile(Stream.Null, 0, StorageService.DefaultMaxFileSizeBytes + 1, "Files", "big.pdf");

        await Assert.ThrowsAsync<ValidationException>(() => CreateStorage().UploadEvidenceFilesAsync([oversized]));
    }

    [Fact]
    public async Task Upload_WithAFileExactlyAtTheLimit_PassesTheSizeCheck()
    {
        // Content is shorter than the declared length, so only the size gate matters here: it must not reject 25 MB itself.
        var atLimit = new FormFile(new MemoryStream(PdfBytes), 0, StorageService.DefaultMaxFileSizeBytes, "Files", "edge.pdf");

        var thrown = await Record.ExceptionAsync(() => CreateStorage().UploadEvidenceFilesAsync([atLimit]));

        Assert.True(thrown is null || !thrown.Message.Contains("25 MB", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("noextension")]
    [InlineData("script.pdf.exe")]
    [InlineData("page.html")]
    public async Task Upload_WithADisallowedExtension_IsRejected(string name)
    {
        await Assert.ThrowsAsync<ValidationException>(() => CreateStorage().UploadEvidenceFilesAsync([File(name, PdfBytes)]));
    }

    [Fact]
    public async Task Upload_WithATooLongFileName_IsRejectedBeforeAnythingIsWritten()
    {
        var name = new string('a', 252) + ".pdf";

        await Assert.ThrowsAsync<ValidationException>(() => CreateStorage().UploadEvidenceFilesAsync([File(name, PdfBytes)]));
    }

    [Fact]
    public async Task Upload_WhenOneFileOfSeveralIsInvalid_StoresNoneOfThem()
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<ValidationException>(() => storage.UploadEvidenceFilesAsync(
            [File("good.pdf", PdfBytes), File("fake.pdf", "not a pdf"u8.ToArray())]));

        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any());
    }

    [Fact]
    public async Task Upload_NeverLetsTheClientNameChooseTheStoredPath()
    {
        var stored = Assert.Single(await CreateStorage().UploadEvidenceFilesAsync([File("..\\..\\evil.pdf", PdfBytes)]));

        Assert.DoesNotContain("..", stored.FilePath, StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(_folder));
    }

    [Theory]
    [InlineData("../secret.pdf")]
    [InlineData("..")]
    [InlineData("")]
    public async Task Open_WithAPathOutsideTheFolder_NeverEscapesIt(string path)
    {
        var storage = CreateStorage();
        await storage.UploadEvidenceFilesAsync([File("a.pdf", PdfBytes)]);

        var thrown = await Record.ExceptionAsync(() => storage.OpenEvidenceReadAsync(path));

        Assert.True(thrown is ValidationException or NotFoundException, $"got {thrown?.GetType().Name ?? "no exception"}");
    }

    [Fact]
    public async Task Open_ReadsRowsWrittenByAnOlderVersionThatStoredTheFolderInThePath()
    {
        var storage = CreateStorage();
        var stored = Assert.Single(await storage.UploadEvidenceFilesAsync([File("a.pdf", PdfBytes)]));

        await using var content = await storage.OpenEvidenceReadAsync("uploads/evidence/" + stored.FilePath);

        Assert.Equal(PdfBytes.Length, content.Length);
    }

    [Fact]
    public async Task Open_OfAMissingFile_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => CreateStorage().OpenEvidenceReadAsync("missing.pdf"));
    }

    [Fact]
    public async Task Delete_RemovesStoredFilesAndToleratesMissingOnes()
    {
        var storage = CreateStorage();
        var stored = await storage.UploadEvidenceFilesAsync([File("a.pdf", PdfBytes)]);

        await storage.DeleteEvidenceFilesAsync(stored);
        await storage.DeleteEvidenceFilesAsync(stored);

        Assert.Empty(Directory.EnumerateFiles(_folder));
    }

    [Fact]
    public async Task Upload_WorksWithARelativeFolderUnderTheApplicationDirectory()
    {
        var relative = Path.Combine("test-uploads", Guid.NewGuid().ToString("N"));
        var storage = CreateStorage(relative);
        try
        {
            var stored = Assert.Single(await storage.UploadEvidenceFilesAsync([File("a.pdf", PdfBytes)]));

            Assert.True(System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, relative, stored.FilePath)));
        }
        finally
        {
            var absolute = Path.Combine(AppContext.BaseDirectory, "test-uploads");
            if (Directory.Exists(absolute))
            {
                Directory.Delete(absolute, recursive: true);
            }
        }
    }
}

public sealed class EvidenceFileValidatorTests
{
    [Theory]
    [InlineData(".pdf", true)]
    [InlineData(".PNG", true)]
    [InlineData(".jpg", true)]
    [InlineData(".jpeg", true)]
    [InlineData(".xlsx", false)]
    [InlineData(".docx", false)]
    [InlineData(".exe", false)]
    [InlineData(null, false)]
    public void CanPreview_IsTrueOnlyForPdfAndImages(string? extension, bool expected) =>
        Assert.Equal(expected, EvidenceFileValidator.CanPreview(extension));

    [Fact]
    public void ContentTypeFor_UnknownExtension_IsOctetStream() =>
        Assert.Equal("application/octet-stream", EvidenceFileValidator.ContentTypeFor(".exe"));

    [Fact]
    public void AllowedExtensions_MatchTheValidatorsKnownTypes()
    {
        Assert.All(StorageService.AllowedExtensions, extension => Assert.True(EvidenceFileValidator.IsKnownExtension(extension)));
    }

    [Fact]
    public void HasValidSignature_WithAHeaderShorterThanTheSignature_IsFalse() =>
        Assert.False(EvidenceFileValidator.HasValidSignature(".png", new byte[] { 0x89, 0x50 }));
}

public sealed class EvidenceServiceTests : IDisposable
{
    private static readonly byte[] PdfBytes = "%PDF-1.7 evidence"u8.ToArray();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "G54_Evidence_" + Guid.NewGuid().ToString("N"));
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _salesId = Guid.NewGuid();
    private readonly Guid _tellerId = Guid.NewGuid();
    private readonly Guid _salesTeamId = Guid.NewGuid();

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private StorageService Storage => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:EvidencePath"] = _folder })
            .Build(),
        NullLogger<StorageService>.Instance);

    private EvidenceService ServiceFor(params (string Resource, string Action, string Scope)[] grants) =>
        new(_db, new GrantingPermissions(grants), Storage);

    private async Task<Employee> AddEmployeeAsync(string code, Guid departmentId, EmployeeStatus status = EmployeeStatus.Active)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = code,
            Email = $"{code}@bank.test",
            DepartmentId = departmentId,
            JobPositionId = Guid.NewGuid(),
            Status = status,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Employees.Add(employee);
        _db.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = code,
            PasswordHash = "hash",
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return employee;
    }

    private async Task<Guid> AccountIdOf(Employee employee) =>
        (await _db.Accounts.SingleAsync(a => a.EmployeeId == employee.Id)).Id;

    /// <summary>Branch -> Sales -> Sales team, and Branch -> Teller; a submitter in the sales team with one stored PDF.</summary>
    private async Task<(Employee Submitter, Guid EvidenceId)> SeedAsync(string fileName = "ho so.pdf", bool storeFile = true)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _db.Departments.AddRange(
            new Department { Id = _branchId, Code = "BR", Name = "Branch", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active, UpdatedAt = now },
            new Department { Id = _salesId, Code = "SA", Name = "Sales", ParentDepartmentId = _branchId, LevelType = DepartmentLevelType.Department, Status = RecordStatus.Active, UpdatedAt = now },
            new Department { Id = _tellerId, Code = "TE", Name = "Teller", ParentDepartmentId = _branchId, LevelType = DepartmentLevelType.Department, Status = RecordStatus.Active, UpdatedAt = now },
            new Department { Id = _salesTeamId, Code = "ST", Name = "Sales team", ParentDepartmentId = _salesId, LevelType = DepartmentLevelType.Team, Status = RecordStatus.Active, UpdatedAt = now });
        var submitter = await AddEmployeeAsync("SUBMITTER", _salesTeamId);

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "W-1",
            Title = "Work",
            AssigneeEmployeeId = submitter.Id,
            Status = WorkStatus.Submitted,
            Priority = WorkPriority.Medium,
            UpdatedAt = now,
        };
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = submitter.Id,
            SubmissionNo = 1,
            Status = SubmissionStatus.Submitted,
            SubmittedAt = now,
            UpdatedAt = now,
        };
        _db.Works.Add(work);
        _db.ResultSubmissions.Add(submission);

        var filePath = "missing.pdf";
        if (storeFile)
        {
            var formFile = new FormFile(new MemoryStream(PdfBytes), 0, PdfBytes.Length, "Files", fileName);
            filePath = (await Storage.UploadEvidenceFilesAsync([formFile])).Single().FilePath;
        }

        var evidence = new Evidence
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            FileName = fileName,
            FilePath = filePath,
            MimeType = "application/pdf",
            FileSize = PdfBytes.Length,
            UploadedAt = now,
            UpdatedAt = now,
        };
        _db.Evidences.Add(evidence);
        await _db.SaveChangesAsync();
        return (submitter, evidence.Id);
    }

    private static async Task<byte[]> ReadAll(EvidenceFileDto file)
    {
        await using (file.Content)
        {
            using var memory = new MemoryStream();
            await file.Content.CopyToAsync(memory);
            return memory.ToArray();
        }
    }

    [Fact]
    public async Task TheSubmitter_CanOpenTheirOwnEvidence_ByAccountOrEmployeeId()
    {
        var (submitter, evidenceId) = await SeedAsync();
        var service = ServiceFor();

        var byEmployee = await service.OpenAsync(submitter.Id, evidenceId);
        var byAccount = await service.OpenAsync(await AccountIdOf(submitter), evidenceId);

        Assert.Equal(PdfBytes, await ReadAll(byEmployee));
        Assert.Equal("ho so.pdf", byAccount.FileName);
        Assert.Equal("application/pdf", byAccount.ContentType);
        Assert.True(byAccount.CanPreview);
        await byAccount.Content.DisposeAsync();
    }

    [Fact]
    public async Task AVerifierWithDepartmentScope_CanOpenEvidenceFromTheirOwnSubtree()
    {
        var (_, evidenceId) = await SeedAsync();
        var verifier = await AddEmployeeAsync("VERIFIER", _salesId);

        var file = await ServiceFor(("verification", "decide", "Department")).OpenAsync(verifier.Id, evidenceId);

        Assert.Equal(PdfBytes, await ReadAll(file));
    }

    [Fact]
    public async Task AVerifierWithDepartmentScope_CannotOpenEvidenceFromASiblingDepartment()
    {
        var (_, evidenceId) = await SeedAsync();
        var verifier = await AddEmployeeAsync("VERIFIER", _tellerId);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceFor(("verification", "decide", "Department")).OpenAsync(verifier.Id, evidenceId));
    }

    [Fact]
    public async Task AVerifierWithDepartmentScope_CannotOpenEvidenceFromADepartmentAboveThem()
    {
        var (_, evidenceId) = await SeedAsync();
        var teamLead = await AddEmployeeAsync("TEAMLEAD", _salesTeamId);
        // Make the submitter belong to the branch root, which sits above the team lead's department scope.
        var submission = await _db.ResultSubmissions.SingleAsync();
        var branchEmployee = await AddEmployeeAsync("BRANCHER", _branchId);
        submission.SubmitterEmployeeId = branchEmployee.Id;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceFor(("verification", "decide", "Department")).OpenAsync(teamLead.Id, evidenceId));
    }

    [Fact]
    public async Task AVerifierWithBranchScope_CanOpenEvidenceFromAnyDepartmentOfTheBranch()
    {
        var (_, evidenceId) = await SeedAsync();
        var verifier = await AddEmployeeAsync("VERIFIER", _tellerId);

        var file = await ServiceFor(("verification", "decide", "Branch")).OpenAsync(verifier.Id, evidenceId);

        await file.Content.DisposeAsync();
    }

    [Fact]
    public async Task AdministratorsAndGlobalScope_CanOpenAnyEvidence()
    {
        var (_, evidenceId) = await SeedAsync();
        var admin = await AddEmployeeAsync("ADMIN", _tellerId);
        var global = await AddEmployeeAsync("GLOBAL", _tellerId);

        var viaWildcard = await ServiceFor(("*", "*", "All")).OpenAsync(admin.Id, evidenceId);
        var viaAllScope = await ServiceFor(("verification", "decide", "All")).OpenAsync(global.Id, evidenceId);

        await viaWildcard.Content.DisposeAsync();
        await viaAllScope.Content.DisposeAsync();
    }

    [Fact]
    public async Task AnotherEmployee_WithoutVerificationRights_IsForbidden()
    {
        var (_, evidenceId) = await SeedAsync();
        var colleague = await AddEmployeeAsync("COLLEAGUE", _salesTeamId);

        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceFor().OpenAsync(colleague.Id, evidenceId));
    }

    [Fact]
    public async Task AVerifierWithOnlyOwnScope_IsForbidden()
    {
        var (_, evidenceId) = await SeedAsync();
        var verifier = await AddEmployeeAsync("VERIFIER", _salesTeamId);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceFor(("verification", "decide", "Own")).OpenAsync(verifier.Id, evidenceId));
    }

    [Fact]
    public async Task APermissionForAnotherResource_DoesNotGrantAccess()
    {
        var (_, evidenceId) = await SeedAsync();
        var manager = await AddEmployeeAsync("MANAGER", _salesId);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceFor(("evaluation", "read", "All")).OpenAsync(manager.Id, evidenceId));
    }

    [Fact]
    public async Task AnInactiveCaller_IsForbiddenEvenWhenTheySubmittedIt()
    {
        var (submitter, evidenceId) = await SeedAsync();
        var tracked = await _db.Employees.SingleAsync(e => e.Id == submitter.Id);
        tracked.Status = EmployeeStatus.Inactive;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceFor().OpenAsync(submitter.Id, evidenceId));
    }

    [Fact]
    public async Task AnUnknownCaller_IsForbidden()
    {
        var (_, evidenceId) = await SeedAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceFor(("*", "*", "All")).OpenAsync(Guid.NewGuid(), evidenceId));
    }

    [Fact]
    public async Task UnknownEvidence_IsNotFound()
    {
        var (submitter, _) = await SeedAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => ServiceFor().OpenAsync(submitter.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task EvidenceWhoseFileIsGone_IsNotFound()
    {
        var (submitter, evidenceId) = await SeedAsync(storeFile: false);

        await Assert.ThrowsAsync<NotFoundException>(() => ServiceFor().OpenAsync(submitter.Id, evidenceId));
    }

    [Fact]
    public async Task EvidenceOfARiskEvent_IsOnlyOpenToGlobalScope()
    {
        var (submitter, _) = await SeedAsync();
        var stored = (await Storage.UploadEvidenceFilesAsync(
            [new FormFile(new MemoryStream(PdfBytes), 0, PdfBytes.Length, "Files", "risk.pdf")])).Single();
        var evidence = new Evidence
        {
            Id = Guid.NewGuid(),
            RiskEventId = Guid.NewGuid(),
            FileName = "risk.pdf",
            FilePath = stored.FilePath,
            UploadedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Evidences.Add(evidence);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceFor(("verification", "decide", "Branch")).OpenAsync(submitter.Id, evidence.Id));
        var file = await ServiceFor(("*", "*", "All")).OpenAsync(submitter.Id, evidence.Id);
        await file.Content.DisposeAsync();
    }

    /// <summary>An appeal filed by <paramref name="filer"/> with one stored PDF attached to it.</summary>
    private async Task<Guid> SeedAppealEvidenceAsync(Employee filer)
    {
        var stored = (await Storage.UploadEvidenceFilesAsync(
            [new FormFile(new MemoryStream(PdfBytes), 0, PdfBytes.Length, "Files", "khieu nai.pdf")])).Single();
        var appeal = new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = filer.Id,
            ResultSubmissionId = (await _db.ResultSubmissions.SingleAsync()).Id,
            Reason = "Reason",
            Status = AppealStatus.Open,
            FiledAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var evidence = new Evidence
        {
            Id = Guid.NewGuid(),
            AppealId = appeal.Id,
            FileName = "khieu nai.pdf",
            FilePath = stored.FilePath,
            MimeType = "application/pdf",
            UploadedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Appeals.Add(appeal);
        _db.Evidences.Add(evidence);
        await _db.SaveChangesAsync();
        return evidence.Id;
    }

    [Fact]
    public async Task EvidenceOfAnAppeal_IsOpenToTheEmployeeWhoFiledIt()
    {
        var (submitter, _) = await SeedAsync();
        var evidenceId = await SeedAppealEvidenceAsync(submitter);

        var file = await ServiceFor().OpenAsync(submitter.Id, evidenceId);

        Assert.Equal(PdfBytes, await ReadAll(file));
        Assert.Equal("khieu nai.pdf", file.FileName);
    }

    [Fact]
    public async Task EvidenceOfAnAppeal_FollowsTheVerificationScopeOfTheFilersDepartment()
    {
        var (submitter, _) = await SeedAsync();
        var evidenceId = await SeedAppealEvidenceAsync(submitter);
        var salesManager = await AddEmployeeAsync("SALESMGR", _salesId);
        var tellerManager = await AddEmployeeAsync("TELLERMGR", _tellerId);
        var colleague = await AddEmployeeAsync("COLLEAGUE", _salesTeamId);

        var inside = await ServiceFor(("verification", "decide", "Department")).OpenAsync(salesManager.Id, evidenceId);
        await inside.Content.DisposeAsync();
        var branchWide = await ServiceFor(("verification", "decide", "Branch")).OpenAsync(tellerManager.Id, evidenceId);
        await branchWide.Content.DisposeAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceFor(("verification", "decide", "Department")).OpenAsync(tellerManager.Id, evidenceId));
        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceFor().OpenAsync(colleague.Id, evidenceId));
    }

    [Fact]
    public async Task EvidenceOfAnAppeal_IsNotOpenToTheSubmitterOfAnotherPerson()
    {
        var (submitter, _) = await SeedAsync();
        var evidenceId = await SeedAppealEvidenceAsync(submitter);
        var other = await AddEmployeeAsync("OTHER", _tellerId);

        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceFor().OpenAsync(other.Id, evidenceId));
    }

    [Fact]
    public async Task OfficeFiles_AreNotPreviewable_AndKeepTheContentTypeOfTheirExtension()
    {
        var (submitter, _) = await SeedAsync();
        var zip = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x01 };
        var stored = (await Storage.UploadEvidenceFilesAsync(
            [new FormFile(new MemoryStream(zip), 0, zip.Length, "Files", "bang.xlsx")])).Single();
        var evidence = new Evidence
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = (await _db.ResultSubmissions.SingleAsync()).Id,
            FileName = "bang.xlsx",
            FilePath = stored.FilePath,
            MimeType = "text/html", // a poisoned stored value must not be served
            UploadedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Evidences.Add(evidence);
        await _db.SaveChangesAsync();

        var file = await ServiceFor().OpenAsync(submitter.Id, evidence.Id);

        Assert.False(file.CanPreview);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        await file.Content.DisposeAsync();
    }

    [Fact]
    public async Task Controller_ServesDownloadAsAttachmentAndPreviewInlineWithASandbox()
    {
        var (submitter, evidenceId) = await SeedAsync();
        var controller = ControllerFor(ServiceFor(), submitter.Id);

        var download = Assert.IsType<FileStreamResult>(await controller.Download(evidenceId, CancellationToken.None));
        var preview = Assert.IsType<FileStreamResult>(await controller.Preview(evidenceId, CancellationToken.None));

        Assert.Equal("application/pdf", download.ContentType);
        Assert.Equal("ho so.pdf", download.FileDownloadName);
        Assert.Equal("application/pdf", preview.ContentType);
        Assert.True(string.IsNullOrEmpty(preview.FileDownloadName));
        var headers = controller.Response.Headers;
        Assert.Equal("sandbox", headers.ContentSecurityPolicy.ToString());
        Assert.StartsWith("inline", headers.ContentDisposition.ToString(), StringComparison.Ordinal);
        Assert.Equal("private, no-store", headers.CacheControl.ToString());
        await download.FileStream.DisposeAsync();
        await preview.FileStream.DisposeAsync();
    }

    [Fact]
    public async Task Controller_PreviewOfAnOfficeFile_FallsBackToAnAttachment()
    {
        var (submitter, _) = await SeedAsync();
        var zip = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x01 };
        var stored = (await Storage.UploadEvidenceFilesAsync(
            [new FormFile(new MemoryStream(zip), 0, zip.Length, "Files", "bang.xlsx")])).Single();
        var evidence = new Evidence
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = (await _db.ResultSubmissions.SingleAsync()).Id,
            FileName = "bang.xlsx",
            FilePath = stored.FilePath,
            UploadedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Evidences.Add(evidence);
        await _db.SaveChangesAsync();
        var controller = ControllerFor(ServiceFor(), submitter.Id);

        var preview = Assert.IsType<FileStreamResult>(await controller.Preview(evidence.Id, CancellationToken.None));

        Assert.Equal("bang.xlsx", preview.FileDownloadName);
        await preview.FileStream.DisposeAsync();
    }

    private static EvidenceController ControllerFor(EvidenceService service, Guid userId) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "TestAuth")),
            },
        },
    };

    private sealed class GrantingPermissions((string Resource, string Action, string Scope)[] grants) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid accountId, CancellationToken cancellationToken)
        {
            IReadOnlyList<UserPermissionDto> result = grants.Select(g => new UserPermissionDto(g.Resource, g.Action, g.Scope)).ToList();
            return Task.FromResult(result);
        }

        public Task<bool> HasPermissionAsync(Guid accountId, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
