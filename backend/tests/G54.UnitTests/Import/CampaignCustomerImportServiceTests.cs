using ClosedXML.Excel;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace G54.UnitTests.Import;

public sealed class CampaignCustomerImportServiceTests
{
    private static readonly string[] Headers = ["Customer CIF*", "Tên khách hàng*", "Số điện thoại", "Ghi chú"];

    private static (AppDbContext Db, CampaignCustomerImportService Service, Guid CampaignId) Create(
        BusinessCampaignStatus status = BusinessCampaignStatus.Running)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);

        var objective = new BusinessObjective
        {
            Id = Guid.NewGuid(),
            Code = "OBJ-01",
            Name = "Mục tiêu thử nghiệm",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            Status = BusinessObjectiveStatus.Active,
        };
        db.BusinessObjectives.Add(objective);

        var campaign = new BusinessCampaign
        {
            Id = Guid.NewGuid(),
            BusinessObjectiveId = objective.Id,
            Code = "CMP-01",
            Name = "Chiến dịch thử nghiệm",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            Status = status,
        };
        db.BusinessCampaigns.Add(campaign);
        db.SaveChanges();

        var service = new CampaignCustomerImportService(
            new UnitOfWork(db),
            db,
            new ClosedXmlSheetReader(),
            Options.Create(new SystemParametersOptions()));
        return (db, service, campaign.Id);
    }

    private static MemoryStream Workbook(params (string Cif, string Name, string Phone, string Note)[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Data");
        for (var c = 0; c < Headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = Headers[c];
        }

        for (var i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            sheet.Cell(i + 2, 1).Value = r.Cif;
            sheet.Cell(i + 2, 2).Value = r.Name;
            sheet.Cell(i + 2, 3).Value = r.Phone;
            sheet.Cell(i + 2, 4).Value = r.Note;
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void GetTemplate_HasTheFourColumns()
    {
        var (_, service, _) = Create();

        var sheet = new ClosedXmlSheetReader().Read(new MemoryStream(service.GetTemplate()), 10);

        Assert.Equal(Headers.Length, sheet.Headers.Count);
        Assert.Equal(Headers, sheet.Headers);
    }

    [Fact]
    public async Task PreviewAsync_ValidFile_AllRowsCreate()
    {
        var (_, service, campaignId) = Create();

        var preview = await service.PreviewAsync(
            campaignId,
            "f.xlsx",
            Workbook(
                ("CIF-001", "Trần Văn An", "0901234567", "Khách VIP"),
                ("CIF-002", "Nguyễn Thị Bình", "", "")),
            CancellationToken.None);

        Assert.Equal(2, preview.Summary.TotalRows);
        Assert.Equal(2, preview.Summary.Valid);
        Assert.Equal(0, preview.Summary.Invalid);
        Assert.Equal(2, preview.Summary.Create);
        Assert.All(preview.Rows, r => Assert.Equal("CREATE", r.Action));
    }

    [Fact]
    public async Task CommitAsync_ValidFile_CreatesRows()
    {
        var (db, service, campaignId) = Create();

        var result = await service.CommitAsync(
            campaignId,
            Workbook(("CIF-001", "Trần Văn An", "0901234567", "Khách VIP")),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Skipped);

        var customer = await db.CampaignCustomers.SingleAsync(c => c.CustomerCif == "CIF-001");
        Assert.Equal("Trần Văn An", customer.CustomerName);
        Assert.Equal("0901234567", customer.Phone);
        Assert.Equal("Khách VIP", customer.Note);
        Assert.Equal(campaignId, customer.BusinessCampaignId);
    }

    [Fact]
    public async Task PreviewAsync_MissingCifOrName_GivesRequiredFieldErrors()
    {
        var (_, service, campaignId) = Create();

        var preview = await service.PreviewAsync(
            campaignId,
            "f.xlsx",
            Workbook(
                ("", "Thiếu CIF", "", ""),
                ("CIF-010", "", "", "")),
            CancellationToken.None);

        Assert.Equal(2, preview.Summary.Invalid);
        Assert.Contains(preview.Rows[0].Errors, e => e.Column == "Customer CIF*" && e.Code == ErrorCodes.RequiredField);
        Assert.Contains(preview.Rows[1].Errors, e => e.Column == "Tên khách hàng*" && e.Code == ErrorCodes.RequiredField);
    }

    [Theory]
    [InlineData("abc-123")]
    [InlineData("123")] // only 3 digits, below the 9-digit minimum
    [InlineData("1234567890123456")] // 16 digits, above the 15-digit maximum
    public async Task PreviewAsync_BadPhone_GivesInvalidValueError(string phone)
    {
        var (_, service, campaignId) = Create();

        var preview = await service.PreviewAsync(
            campaignId, "f.xlsx", Workbook(("CIF-020", "Khách hàng", phone, "")), CancellationToken.None);

        Assert.Contains(preview.Rows[0].Errors, e => e.Column == "Số điện thoại" && e.Code == ErrorCodes.DataImportInvalidValue);
    }

    [Fact]
    public async Task PreviewAsync_DuplicateCifInFile_FlagsSecondRow()
    {
        var (_, service, campaignId) = Create();

        var preview = await service.PreviewAsync(
            campaignId,
            "f.xlsx",
            Workbook(
                ("CIF-030", "Khách hàng A", "", ""),
                ("cif-030", "Khách hàng B", "", "")),
            CancellationToken.None);

        Assert.Equal(1, preview.Summary.Valid);
        Assert.Equal(1, preview.Summary.Invalid);
        Assert.Contains(
            preview.Rows[1].Errors,
            e => e.Column == "Customer CIF*" && e.Code == ErrorCodes.DataImportDuplicate && e.Message.Contains("dòng 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommitAsync_ExistingCif_UpdatesFieldsButKeepsAssigneeAndStatus()
    {
        var (db, service, campaignId) = Create();
        var assigneeId = Guid.NewGuid();
        db.CampaignCustomers.Add(new CampaignCustomer
        {
            Id = Guid.NewGuid(),
            BusinessCampaignId = campaignId,
            CustomerCif = "CIF-040",
            CustomerName = "Tên cũ",
            AssigneeEmployeeId = assigneeId,
            AssignedAt = DateTimeOffset.UtcNow,
            ContactStatus = CampaignContactStatus.Called,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await service.CommitAsync(
            campaignId,
            Workbook(("CIF-040", "Tên mới", "0909999999", "Ghi chú mới")),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);

        var customer = await db.CampaignCustomers.SingleAsync(c => c.CustomerCif == "CIF-040");
        Assert.Equal("Tên mới", customer.CustomerName);
        Assert.Equal("0909999999", customer.Phone);
        Assert.Equal("Ghi chú mới", customer.Note);
        Assert.Equal(assigneeId, customer.AssigneeEmployeeId);
        Assert.Equal(CampaignContactStatus.Called, customer.ContactStatus);
    }

    [Theory]
    [InlineData(BusinessCampaignStatus.Completed)]
    [InlineData(BusinessCampaignStatus.Cancelled)]
    [InlineData(BusinessCampaignStatus.Paused)]
    public async Task PreviewAsync_CampaignNotPlannedOrRunning_ThrowsCampaignNotRunning(BusinessCampaignStatus status)
    {
        var (_, service, campaignId) = Create(status);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.PreviewAsync(campaignId, "f.xlsx", Workbook(("CIF-050", "Khách hàng", "", "")), CancellationToken.None));

        Assert.Equal(ErrorCodes.CampaignNotRunning, exception.ErrorCode);
    }

    [Fact]
    public async Task PreviewAsync_UnknownCampaign_ThrowsNotFound()
    {
        var (_, service, _) = Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.PreviewAsync(Guid.NewGuid(), "f.xlsx", Workbook(("CIF-060", "Khách hàng", "", "")), CancellationToken.None));
    }

    [Fact]
    public async Task CommitAsync_MixOfValidAndInvalidRows_CommitsValidAndSkipsInvalid()
    {
        var (db, service, campaignId) = Create();

        var result = await service.CommitAsync(
            campaignId,
            Workbook(
                ("CIF-070", "Khách hàng hợp lệ", "", ""),
                ("", "Thiếu CIF", "", "")),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Skipped);
        Assert.Single(await db.CampaignCustomers.ToListAsync());
    }

    [Fact]
    public async Task CommitAsync_WhenZeroRowsAreValid_Throws422AndWritesNothing()
    {
        var (db, service, campaignId) = Create();

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CommitAsync(campaignId, Workbook(("", "", "", "Chỉ có ghi chú")), Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("DATA_IMPORT_HAS_ERRORS", exception.ErrorCode);
        Assert.Empty(db.CampaignCustomers);
    }
}
