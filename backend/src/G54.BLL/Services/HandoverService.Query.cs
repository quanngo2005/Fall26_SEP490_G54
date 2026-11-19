using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Handover;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>Read queries for SCR-WRK-05 (list) and SCR-WRK-05-D1 (detail drawer).</summary>
public sealed partial class HandoverService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<PagedResult<HandoverListItemDto>> ListHandoversAsync(
        Guid userId,
        HandoverListQueryDto query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var isAdmin = caller.Account is not null
            && await permissions.HasPermissionAsync(caller.Account.Id, "handover", "manage", PermissionScopes.All, cancellationToken);

        var direction = ParseDirection(query.Direction);
        var statusFilter = ParseHandoverStatusFilter(query.Status);
        var kpiFilter = ParseKpiStatusFilter(query.KpiStatus);

        var q = db.TaskHandovers.AsNoTracking();

        q = direction switch
        {
            "OUTGOING" => q.Where(h => h.FromEmployeeId == caller.Id),
            "ALL" when isAdmin => q,
            "ALL" => q.Where(h =>
                h.ToEmployeeId == caller.Id
                || h.FromEmployeeId == caller.Id
                || h.ApprovedBy == caller.Id
                || h.FromEmployee.ManagerId == caller.Id
                || h.ToEmployee.ManagerId == caller.Id),
            _ => q.Where(h => h.ToEmployeeId == caller.Id),
        };

        if (statusFilter.HasValue)
        {
            var expected = statusFilter.Value;
            q = q.Where(h => h.Status == expected);
        }

        if (query.FromEmployeeId.HasValue && query.FromEmployeeId.Value != Guid.Empty)
        {
            var fromId = query.FromEmployeeId.Value;
            q = q.Where(h => h.FromEmployeeId == fromId);
        }

        if (kpiFilter == "RECORDED")
        {
            q = q.Where(h => db.VerifiedWorkResults.Any(v =>
                v.Status == VerifiedResultStatus.Valid
                && ((v.Verification != null && v.Verification.ResultSubmission.WorkId == h.WorkId)
                    || (v.WorkTransaction != null && v.WorkTransaction.WorkId == h.WorkId))));
        }
        else if (kpiFilter == "PENDING")
        {
            q = q.Where(h => !db.VerifiedWorkResults.Any(v =>
                v.Status == VerifiedResultStatus.Valid
                && ((v.Verification != null && v.Verification.ResultSubmission.WorkId == h.WorkId)
                    || (v.WorkTransaction != null && v.WorkTransaction.WorkId == h.WorkId))));
        }

        var keyword = query.Keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            if (db.Database.IsNpgsql())
            {
                var pattern = LikePattern.Contains(keyword);
                q = q.Where(h =>
                    EF.Functions.ILike(h.Work.Code, pattern, LikePattern.Escape)
                    || EF.Functions.ILike(h.Work.Title, pattern, LikePattern.Escape)
                    || (h.Work.CustomerName != null && EF.Functions.ILike(h.Work.CustomerName, pattern, LikePattern.Escape))
                    || (h.Work.CustomerCif != null && EF.Functions.ILike(h.Work.CustomerCif, pattern, LikePattern.Escape))
                    || EF.Functions.ILike(h.FromEmployee.FullName, pattern, LikePattern.Escape)
                    || EF.Functions.ILike(h.FromEmployee.EmployeeCode, pattern, LikePattern.Escape)
                    || EF.Functions.ILike(h.ToEmployee.FullName, pattern, LikePattern.Escape)
                    || EF.Functions.ILike(h.ToEmployee.EmployeeCode, pattern, LikePattern.Escape));
            }
            else
            {
                q = q.Where(h =>
                    h.Work.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || h.Work.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (h.Work.CustomerName != null && h.Work.CustomerName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    || (h.Work.CustomerCif != null && h.Work.CustomerCif.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    || h.FromEmployee.FullName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || h.FromEmployee.EmployeeCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || h.ToEmployee.FullName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || h.ToEmployee.EmployeeCode.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? DefaultPageSize : query.PageSize, 1, MaxPageSize);

        var totalCount = await q.CountAsync(cancellationToken);
        var rows = await q
            .OrderByDescending(h => h.HandoverAt)
            .ThenByDescending(h => h.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(h => new HandoverProjection(
                h.Id,
                h.WorkId,
                h.Work.Code,
                h.Work.Title,
                h.Work.Status,
                h.Work.WorkType != null ? h.Work.WorkType.Name : null,
                h.Work.CustomerName,
                h.Work.CustomerCif,
                h.Work.TargetValue,
                h.Work.PlannedEndDate,
                h.FromEmployeeId,
                h.FromEmployee.EmployeeCode,
                h.FromEmployee.FullName,
                h.FromEmployee.JobPosition.Title,
                h.ToEmployeeId,
                h.ToEmployee.EmployeeCode,
                h.ToEmployee.FullName,
                h.ToEmployee.JobPosition.Title,
                h.Reason,
                h.ReferrerSharePercent,
                h.Status,
                db.VerifiedWorkResults.Any(v =>
                    v.Status == VerifiedResultStatus.Valid
                    && ((v.Verification != null && v.Verification.ResultSubmission.WorkId == h.WorkId)
                        || (v.WorkTransaction != null && v.WorkTransaction.WorkId == h.WorkId))),
                h.HandoverAt,
                h.AcceptedAt,
                h.UpdatedAt))
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => ToListItemDto(r, caller.Id)).ToList();
        return new PagedResult<HandoverListItemDto>(items, totalCount, page, pageSize);
    }

    public async Task<HandoverListItemDto> GetHandoverAsync(
        Guid handoverId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var isAdmin = caller.Account is not null
            && await permissions.HasPermissionAsync(caller.Account.Id, "handover", "manage", PermissionScopes.All, cancellationToken);

        var row = await db.TaskHandovers.AsNoTracking()
            .Where(h => h.Id == handoverId)
            .Select(h => new
            {
                FromManagerId = h.FromEmployee.ManagerId,
                ToManagerId = h.ToEmployee.ManagerId,
                Projection = new HandoverProjection(
                    h.Id,
                    h.WorkId,
                    h.Work.Code,
                    h.Work.Title,
                    h.Work.Status,
                    h.Work.WorkType != null ? h.Work.WorkType.Name : null,
                    h.Work.CustomerName,
                    h.Work.CustomerCif,
                    h.Work.TargetValue,
                    h.Work.PlannedEndDate,
                    h.FromEmployeeId,
                    h.FromEmployee.EmployeeCode,
                    h.FromEmployee.FullName,
                    h.FromEmployee.JobPosition.Title,
                    h.ToEmployeeId,
                    h.ToEmployee.EmployeeCode,
                    h.ToEmployee.FullName,
                    h.ToEmployee.JobPosition.Title,
                    h.Reason,
                    h.ReferrerSharePercent,
                    h.Status,
                    db.VerifiedWorkResults.Any(v =>
                        v.Status == VerifiedResultStatus.Valid
                        && ((v.Verification != null && v.Verification.ResultSubmission.WorkId == h.WorkId)
                            || (v.WorkTransaction != null && v.WorkTransaction.WorkId == h.WorkId))),
                    h.HandoverAt,
                    h.AcceptedAt,
                    h.UpdatedAt),
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Yêu cầu bàn giao", handoverId);

        var allowed = isAdmin
            || row.Projection.FromEmployeeId == caller.Id
            || row.Projection.ToEmployeeId == caller.Id
            || row.FromManagerId == caller.Id
            || row.ToManagerId == caller.Id;
        if (!allowed)
        {
            throw new ForbiddenException("Bạn không có quyền xem yêu cầu bàn giao này.");
        }

        return ToListItemDto(row.Projection, caller.Id);
    }

    private static HandoverListItemDto ToListItemDto(HandoverProjection r, Guid callerEmployeeId) => new(
        r.Id,
        $"HO-{r.Id.ToString("N")[..6].ToUpperInvariant()}",
        r.WorkId,
        r.WorkCode,
        r.WorkTitle,
        r.WorkStatus.ToEnumString(),
        r.WorkTypeName,
        r.CustomerName,
        r.CustomerCif,
        r.TargetValue,
        r.PlannedEndDate,
        r.FromEmployeeId,
        r.FromEmployeeCode,
        r.FromEmployeeName,
        r.FromJobPositionTitle,
        r.ToEmployeeId,
        r.ToEmployeeCode,
        r.ToEmployeeName,
        r.ToJobPositionTitle,
        r.Reason,
        r.ReferrerSharePercent,
        r.Status.ToEnumString(),
        r.HasVerifiedResult ? "RECORDED" : "PENDING",
        r.HandoverAt,
        r.AcceptedAt,
        r.UpdatedAt,
        r.Status != HandoverStatus.Pending || r.ToEmployeeId != callerEmployeeId);

    private static string ParseDirection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "INCOMING";
        return value.Trim().ToUpperInvariant() switch
        {
            "INCOMING" => "INCOMING",
            "OUTGOING" => "OUTGOING",
            "ALL" => "ALL",
            _ => throw new ValidationException("direction phải là INCOMING, OUTGOING hoặc ALL."),
        };
    }

    private static HandoverStatus? ParseHandoverStatusFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToUpperInvariant() switch
        {
            "PENDING" => HandoverStatus.Pending,
            "ACCEPTED" => HandoverStatus.Accepted,
            "REJECTED" => HandoverStatus.Rejected,
            _ => throw new ValidationException("status phải là PENDING, ACCEPTED hoặc REJECTED."),
        };
    }

    private static string? ParseKpiStatusFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToUpperInvariant() switch
        {
            "PENDING" => "PENDING",
            "RECORDED" => "RECORDED",
            _ => throw new ValidationException("kpiStatus phải là PENDING hoặc RECORDED."),
        };
    }

    private sealed record HandoverProjection(
        Guid Id,
        Guid WorkId,
        string WorkCode,
        string WorkTitle,
        WorkStatus WorkStatus,
        string? WorkTypeName,
        string? CustomerName,
        string? CustomerCif,
        decimal? TargetValue,
        DateOnly? PlannedEndDate,
        Guid FromEmployeeId,
        string FromEmployeeCode,
        string FromEmployeeName,
        string FromJobPositionTitle,
        Guid ToEmployeeId,
        string ToEmployeeCode,
        string ToEmployeeName,
        string ToJobPositionTitle,
        string Reason,
        decimal ReferrerSharePercent,
        HandoverStatus Status,
        bool HasVerifiedResult,
        DateTimeOffset HandoverAt,
        DateTimeOffset? AcceptedAt,
        DateTimeOffset UpdatedAt);
}
