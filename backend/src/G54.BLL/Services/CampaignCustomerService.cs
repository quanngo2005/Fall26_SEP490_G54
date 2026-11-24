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
/// Manager-side campaign customer operations: list with filters/summary, auto allocation, and manual reassignment.
/// Contract: docs/api/CAMPAIGN_CONTRACT.md §5. The allocation rule logic is shared with
/// <see cref="CampaignService.BatchGenerateWorkAsync"/> via <see cref="CampaignAllocation"/>.
/// </summary>
public sealed class CampaignCustomerService(AppDbContext db) : ICampaignCustomerService
{
    public async Task<CampaignCustomerListResultDto> ListAsync(
        Guid campaignId,
        string? search,
        string? status,
        Guid? assigneeId,
        bool unassignedOnly,
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

        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.CampaignCustomers.AsNoTracking()
            .Where(c => c.BusinessCampaignId == campaignId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search.Trim());
            query = query.Where(c =>
                EF.Functions.ILike(c.CustomerCif, pattern, LikePattern.Escape) ||
                EF.Functions.ILike(c.CustomerName, pattern, LikePattern.Escape) ||
                (c.Phone != null && EF.Functions.ILike(c.Phone, pattern, LikePattern.Escape)));
        }

        var parsedStatus = TryParseStatus(status);
        if (parsedStatus.HasValue)
        {
            query = query.Where(c => c.ContactStatus == parsedStatus.Value);
        }

        if (assigneeId.HasValue)
        {
            query = query.Where(c => c.AssigneeEmployeeId == assigneeId.Value);
        }

        if (unassignedOnly)
        {
            query = query.Where(c => c.AssigneeEmployeeId == null);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Include(c => c.AssigneeEmployee)
            .Include(c => c.ClosedWorkType)
            .OrderBy(c => c.CustomerCif)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(c => ToDto(c, c.AssigneeEmployee, c.ClosedWorkType)).ToList();
        var summary = await BuildSummaryAsync(campaignId, cancellationToken);

        return new CampaignCustomerListResultDto(
            new PagedResult<CampaignCustomerDto>(dtos, total, safePage, safePageSize),
            summary);
    }

    public async Task<AllocateCampaignCustomersResultDto> AllocateAsync(
        Guid campaignId,
        string? rule,
        IReadOnlyList<Guid>? employeeIds,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await db.BusinessCampaigns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken);
        if (campaign is null)
        {
            throw new NotFoundException("Chiến dịch", campaignId);
        }

        EnsureCampaignOpen(campaign);

        var participantQuery = db.Participants
            .Where(p => p.BusinessCampaignId == campaignId)
            .Include(p => p.Employee)
            .Where(p => p.Employee.Status == EmployeeStatus.Active);

        if (employeeIds is { Count: > 0 })
        {
            participantQuery = participantQuery.Where(p => employeeIds.Contains(p.EmployeeId));
        }

        var activeParticipants = await participantQuery
            .OrderBy(p => p.Employee.EmployeeCode)
            .ToListAsync(cancellationToken);

        if (activeParticipants.Count == 0)
        {
            throw new BusinessRuleException(
                ErrorCodes.ValidationError,
                "Chiến dịch chưa có nhân sự tham gia hợp lệ ở trạng thái hoạt động để phân bổ khách hàng.");
        }

        var unassigned = await db.CampaignCustomers
            .Where(c => c.BusinessCampaignId == campaignId && c.AssigneeEmployeeId == null)
            .OrderBy(c => c.CustomerCif)
            .ToListAsync(cancellationToken);

        if (unassigned.Count == 0)
        {
            return new AllocateCampaignCustomersResultDto(0, []);
        }

        var participantIds = activeParticipants.Select(p => p.EmployeeId).ToList();
        var normalizedRule = (rule ?? "ROUND_ROBIN").Trim().ToUpperInvariant();

        IReadOnlyDictionary<Guid, int>? loads = null;
        if (normalizedRule == "LOAD_BALANCING")
        {
            loads = await db.CampaignCustomers
                .Where(c => c.BusinessCampaignId == campaignId
                    && c.AssigneeEmployeeId != null
                    && participantIds.Contains(c.AssigneeEmployeeId.Value)
                    && c.ContactStatus != CampaignContactStatus.Closed)
                .GroupBy(c => c.AssigneeEmployeeId!.Value)
                .Select(g => new { EmployeeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EmployeeId, x => x.Count, cancellationToken);
        }

        var assigneeIds = CampaignAllocation.PickAssignees(normalizedRule, participantIds, unassigned.Count, loads);

        var now = DateTimeOffset.UtcNow;
        var countsByEmployee = new Dictionary<Guid, int>();
        for (var i = 0; i < unassigned.Count; i++)
        {
            var assigneeId = assigneeIds[i];
            unassigned[i].AssigneeEmployeeId = assigneeId;
            unassigned[i].AssignedAt = now;
            unassigned[i].UpdatedAt = now;
            unassigned[i].UpdatedBy = currentUserId;
            countsByEmployee[assigneeId] = countsByEmployee.GetValueOrDefault(assigneeId) + 1;
        }

        await db.SaveChangesAsync(cancellationToken);

        var employeeLookup = activeParticipants.ToDictionary(p => p.EmployeeId, p => p.Employee);
        var byEmployee = countsByEmployee
            .Select(kv => new CampaignCustomerAllocationCountDto(
                kv.Key,
                employeeLookup[kv.Key].EmployeeCode,
                employeeLookup[kv.Key].FullName,
                kv.Value))
            .OrderBy(x => x.EmployeeCode, StringComparer.Ordinal)
            .ToList();

        return new AllocateCampaignCustomersResultDto(unassigned.Count, byEmployee);
    }

    public async Task<CampaignCustomerDto> ReassignAsync(
        Guid campaignId,
        Guid customerId,
        Guid employeeId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await db.BusinessCampaigns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken);
        if (campaign is null)
        {
            throw new NotFoundException("Chiến dịch", campaignId);
        }

        // Filter on both ids: a customer id from another campaign must 404, not leak into this one (IDOR).
        var customer = await db.CampaignCustomers
            .Include(c => c.ClosedWorkType)
            .FirstOrDefaultAsync(c => c.Id == customerId && c.BusinessCampaignId == campaignId, cancellationToken);
        if (customer is null)
        {
            throw new NotFoundException("Khách hàng chiến dịch", customerId);
        }

        if (customer.ContactStatus == CampaignContactStatus.Closed)
        {
            throw new BusinessRuleException(ErrorCodes.CampaignCustomerClosed, null, 409);
        }

        EnsureCampaignOpen(campaign);

        var isActiveParticipant = await db.Participants.AnyAsync(
            p => p.BusinessCampaignId == campaignId && p.EmployeeId == employeeId && p.Employee.Status == EmployeeStatus.Active,
            cancellationToken);
        if (!isActiveParticipant)
        {
            throw new BusinessRuleException(ErrorCodes.EmployeeNotParticipant, null, 400);
        }

        var now = DateTimeOffset.UtcNow;
        customer.AssigneeEmployeeId = employeeId;
        customer.AssignedAt = now;
        customer.UpdatedAt = now;
        customer.UpdatedBy = currentUserId;

        await db.SaveChangesAsync(cancellationToken);

        var assignee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        return ToDto(customer, assignee, customer.ClosedWorkType);
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    private static void EnsureCampaignOpen(BusinessCampaign campaign)
    {
        if (campaign.Status is not (BusinessCampaignStatus.Planned or BusinessCampaignStatus.Running))
        {
            throw new BusinessRuleException(ErrorCodes.CampaignNotRunning, null, 409);
        }
    }

    internal static CampaignContactStatus? TryParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        foreach (var candidate in Enum.GetValues<CampaignContactStatus>())
        {
            if (string.Equals(candidate.ToEnumString(), status.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static CampaignCustomerDto ToDto(CampaignCustomer c, Employee? assignee, WorkType? closedWorkType)
    {
        var resultText = CampaignCustomerResultText.Compute(
            c.ContactStatus, c.CallAnswered, c.MissedCallCount, c.AppointmentAt, c.MetAt, closedWorkType?.Name);

        return new CampaignCustomerDto(
            c.Id,
            c.CustomerCif,
            c.CustomerName,
            c.Phone,
            c.Note,
            c.AssigneeEmployeeId,
            assignee?.FullName,
            c.ContactStatus.ToEnumString(),
            resultText,
            c.MissedCallCount,
            c.AppointmentAt,
            c.MetAt,
            c.ClosedWorkTypeId,
            closedWorkType?.Name,
            c.WorkId,
            c.UpdatedAt);
    }

    private async Task<CampaignCustomerSummaryDto> BuildSummaryAsync(Guid campaignId, CancellationToken ct)
    {
        var all = db.CampaignCustomers.AsNoTracking().Where(c => c.BusinessCampaignId == campaignId);

        var total = await all.CountAsync(ct);
        var unassigned = await all.CountAsync(c => c.AssigneeEmployeeId == null, ct);

        var statusCounts = await all
            .GroupBy(c => c.ContactStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byStatus = Enum.GetValues<CampaignContactStatus>()
            .Select(s => new CampaignCustomerStatusCountDto(
                s.ToEnumString(),
                statusCounts.FirstOrDefault(x => x.Status == s)?.Count ?? 0))
            .ToList();

        var byEmployeeRaw = await all
            .Where(c => c.AssigneeEmployeeId != null)
            .GroupBy(c => c.AssigneeEmployeeId!.Value)
            .Select(g => new
            {
                EmployeeId = g.Key,
                Assigned = g.Count(),
                Closed = g.Count(c => c.ContactStatus == CampaignContactStatus.Closed),
            })
            .ToListAsync(ct);

        var employeeIds = byEmployeeRaw.Select(x => x.EmployeeId).ToList();
        var employees = await db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var byAssignee = byEmployeeRaw
            .Select(x => new CampaignCustomerAssigneeSummaryDto(
                x.EmployeeId,
                employees.TryGetValue(x.EmployeeId, out var emp) ? emp.EmployeeCode : string.Empty,
                employees.TryGetValue(x.EmployeeId, out var emp2) ? emp2.FullName : string.Empty,
                x.Assigned,
                x.Closed))
            .OrderBy(x => x.EmployeeCode, StringComparer.Ordinal)
            .ToList();

        return new CampaignCustomerSummaryDto(total, unassigned, byStatus, byAssignee);
    }
}
