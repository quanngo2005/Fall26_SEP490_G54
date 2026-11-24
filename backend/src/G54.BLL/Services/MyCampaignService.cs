using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.CampaignCustomer;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// Employee-side campaign customer operations ("My campaigns"): list my campaigns, list my customers in a
/// campaign, and update a customer's contact status. Contract: docs/api/CAMPAIGN_CONTRACT.md §5.
/// Reuses <see cref="CampaignCustomerService.ToDto"/> / <see cref="CampaignCustomerResultText.Compute"/> so the
/// employee view renders the same <c>resultText</c> as the manager list, and <see cref="CampaignWorkCode"/> so a
/// closed customer's generated Work code follows the same sequence as
/// <see cref="CampaignService.BatchGenerateWorkAsync"/>.
/// </summary>
public sealed class MyCampaignService(AppDbContext db, TimeProvider time) : IMyCampaignService
{
    public async Task<IReadOnlyList<MyCampaignSummaryDto>> GetMyCampaignsAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var employee = await ResolveEmployeeAsync(currentUserId, cancellationToken)
            ?? throw new ForbiddenException("Không xác định được nhân sự thực hiện thao tác.");

        var campaignIds = await db.CampaignCustomers.AsNoTracking()
            .Where(c => c.AssigneeEmployeeId == employee.Id)
            .Select(c => c.BusinessCampaignId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (campaignIds.Count == 0)
        {
            return [];
        }

        var campaigns = await db.BusinessCampaigns.AsNoTracking()
            .Where(c => campaignIds.Contains(c.Id))
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken);

        var rows = await db.CampaignCustomers.AsNoTracking()
            .Where(c => c.AssigneeEmployeeId == employee.Id && campaignIds.Contains(c.BusinessCampaignId))
            .Select(c => new { c.BusinessCampaignId, c.ContactStatus })
            .ToListAsync(cancellationToken);

        var byCampaign = rows.GroupBy(r => r.BusinessCampaignId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<MyCampaignSummaryDto>(campaigns.Count);
        foreach (var campaign in campaigns)
        {
            var myRows = byCampaign.GetValueOrDefault(campaign.Id, []);
            var byStatus = Enum.GetValues<CampaignContactStatus>()
                .Select(s => new CampaignCustomerStatusCountDto(s.ToEnumString(), myRows.Count(r => r.ContactStatus == s)))
                .ToList();

            result.Add(new MyCampaignSummaryDto(
                campaign.Id,
                campaign.Code,
                campaign.Name,
                campaign.Status.ToString().ToUpperInvariant(),
                campaign.StartDate,
                campaign.EndDate,
                myRows.Count,
                myRows.Count(r => r.ContactStatus == CampaignContactStatus.Closed),
                byStatus));
        }

        return result;
    }

    public async Task<PagedResult<CampaignCustomerDto>> GetMyCustomersAsync(
        Guid campaignId,
        Guid currentUserId,
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var campaignExists = await db.BusinessCampaigns.AsNoTracking()
            .AnyAsync(c => c.Id == campaignId, cancellationToken);
        if (!campaignExists)
        {
            throw new NotFoundException("Chiến dịch", campaignId);
        }

        var employee = await ResolveEmployeeAsync(currentUserId, cancellationToken)
            ?? throw new ForbiddenException("Không xác định được nhân sự thực hiện thao tác.");

        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.CampaignCustomers.AsNoTracking()
            .Where(c => c.BusinessCampaignId == campaignId && c.AssigneeEmployeeId == employee.Id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search.Trim());
            query = query.Where(c =>
                EF.Functions.ILike(c.CustomerCif, pattern, LikePattern.Escape) ||
                EF.Functions.ILike(c.CustomerName, pattern, LikePattern.Escape) ||
                (c.Phone != null && EF.Functions.ILike(c.Phone, pattern, LikePattern.Escape)));
        }

        var parsedStatus = CampaignCustomerService.TryParseStatus(status);
        if (parsedStatus.HasValue)
        {
            query = query.Where(c => c.ContactStatus == parsedStatus.Value);
        }

        var total = await query.CountAsync(cancellationToken);

        // Needs-action rows first (NOT_CONTACTED, CALLED, APPOINTMENT_SET, MET, in that order), CLOSED last,
        // then by CIF. The enum's declared order already matches that sequence.
        var items = await query
            .Include(c => c.ClosedWorkType)
            .OrderBy(c => c.ContactStatus)
            .ThenBy(c => c.CustomerCif)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(c => CampaignCustomerService.ToDto(c, employee, c.ClosedWorkType)).ToList();

        return new PagedResult<CampaignCustomerDto>(dtos, total, safePage, safePageSize);
    }

    public async Task<CampaignCustomerDto> UpdateContactAsync(
        Guid campaignId,
        Guid customerId,
        Guid currentUserId,
        UpdateCampaignCustomerContactRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var employee = await ResolveEmployeeAsync(currentUserId, cancellationToken)
            ?? throw new ForbiddenException("Không xác định được nhân sự thực hiện thao tác.");

        var campaign = await db.BusinessCampaigns.FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken);
        if (campaign is null)
        {
            throw new NotFoundException("Chiến dịch", campaignId);
        }

        // Filter on campaign id, customer id and assignee together: a row from another campaign, or another
        // employee's row, must 404 rather than leak into this one (IDOR).
        var customer = await db.CampaignCustomers
            .FirstOrDefaultAsync(
                c => c.Id == customerId && c.BusinessCampaignId == campaignId && c.AssigneeEmployeeId == employee.Id,
                cancellationToken);
        if (customer is null)
        {
            throw new NotFoundException("Khách hàng chiến dịch", customerId);
        }

        if (campaign.Status != BusinessCampaignStatus.Running)
        {
            throw new BusinessRuleException(ErrorCodes.CampaignNotRunning, null, 409);
        }

        if (customer.ContactStatus == CampaignContactStatus.Closed)
        {
            throw new BusinessRuleException(ErrorCodes.CampaignCustomerClosed, null, 409);
        }

        // BR-41: a removed or inactive participant keeps their old rows but can no longer work them.
        var isActiveParticipant = await db.Participants.AnyAsync(
            p => p.BusinessCampaignId == campaignId && p.EmployeeId == employee.Id && p.Employee.Status == EmployeeStatus.Active,
            cancellationToken);
        if (!isActiveParticipant)
        {
            throw new BusinessRuleException(ErrorCodes.EmployeeNotParticipant, null, 400);
        }

        var status = CampaignCustomerService.TryParseStatus(request.Status)
            ?? throw new ValidationException("Trạng thái liên hệ không hợp lệ.");

        var now = DateTimeOffset.UtcNow;
        WorkType? closedWorkType = null;

        switch (status)
        {
            case CampaignContactStatus.Called:
                if (request.CallAnswered is null)
                {
                    throw new ValidationException("Trường callAnswered là bắt buộc khi trạng thái là CALLED.");
                }

                customer.CallAnswered = request.CallAnswered;
                if (request.CallAnswered == false)
                {
                    customer.MissedCallCount++;
                }

                break;

            case CampaignContactStatus.AppointmentSet:
                if (request.AppointmentAt is null)
                {
                    throw new ValidationException("Trường appointmentAt là bắt buộc khi trạng thái là APPOINTMENT_SET.");
                }

                customer.AppointmentAt = request.AppointmentAt;
                break;

            case CampaignContactStatus.Met:
                if (request.MetAt is null)
                {
                    throw new ValidationException("Trường metAt là bắt buộc khi trạng thái là MET.");
                }

                customer.MetAt = request.MetAt;
                break;

            case CampaignContactStatus.Closed:
                if (request.WorkTypeId is null)
                {
                    throw new ValidationException("Trường workTypeId là bắt buộc khi trạng thái là CLOSED.");
                }

                closedWorkType = await db.WorkTypes.AsNoTracking()
                    .FirstOrDefaultAsync(wt => wt.Id == request.WorkTypeId, cancellationToken);
                var isLinkedAndActive = closedWorkType is { Status: RecordStatus.Active } && await db.CampaignWorkTypes
                    .AnyAsync(cwt => cwt.BusinessCampaignId == campaignId && cwt.WorkTypeId == request.WorkTypeId, cancellationToken);
                if (!isLinkedAndActive)
                {
                    throw new BusinessRuleException(ErrorCodes.WorkTypeNotInCampaign, null, 400);
                }

                var today = time.GetBusinessToday();
                var plannedEnd = campaign.EndDate < today ? today : campaign.EndDate;
                var codePrefix = CampaignWorkCode.PrefixFor(campaign, today);
                var sequenceStart = await db.Works.CountAsync(w => w.Code.StartsWith(codePrefix), cancellationToken);

                var work = new Work
                {
                    Id = Guid.NewGuid(),
                    Code = $"{codePrefix}{sequenceStart + 1:D4}",
                    Title = $"[Chiến dịch {campaign.Code}] Chốt {customer.CustomerName}",
                    Description = BuildClosedWorkDescription(campaign, customer, closedWorkType!),
                    BusinessCampaignId = campaignId,
                    WorkTypeId = request.WorkTypeId,
                    AssigneeEmployeeId = employee.Id,
                    CreatedBy = employee.Id,
                    CustomerCif = customer.CustomerCif,
                    CustomerName = customer.CustomerName,
                    Status = WorkStatus.New,
                    Priority = WorkPriority.Medium,
                    PlannedStartDate = today,
                    PlannedEndDate = plannedEnd,
                    AssignedAt = now,
                    UpdatedAt = now,
                };

                db.Works.Add(work);
                customer.ClosedWorkTypeId = request.WorkTypeId;
                customer.WorkId = work.Id;
                break;
        }

        customer.ContactStatus = status;
        customer.UpdatedAt = now;
        customer.UpdatedBy = currentUserId;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            // A concurrent close took the same work-code sequence number; nothing was saved, so the caller can retry.
            throw new ConflictException("Có một lượt tạo công việc khác đang chạy cho chiến dịch này, vui lòng thử lại.");
        }

        return CampaignCustomerService.ToDto(customer, employee, closedWorkType);
    }

    private static string BuildClosedWorkDescription(BusinessCampaign campaign, CampaignCustomer customer, WorkType workType)
    {
        var description = $"Nguồn: chiến dịch {campaign.Code} - {campaign.Name}. Mã KH: {customer.CustomerCif}, Tên KH: {customer.CustomerName}. Sản phẩm đã chốt: {workType.Name}.";
        return string.IsNullOrWhiteSpace(customer.Note) ? description : $"{description} Ghi chú: {customer.Note}";
    }

    private async Task<Employee?> ResolveEmployeeAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await db.Employees
            .AsNoTracking()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == userId || (e.Account != null && e.Account.Id == userId), cancellationToken);
    }
}
