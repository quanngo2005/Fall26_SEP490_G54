using System.Text.Json;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.KpiDefinition;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>The catalog side of a KPI: credit role, data source, product, applicable positions and validity, plus the list summary, usage and history.</summary>
public sealed partial class KpiDefinitionService
{
    private const int MaxProductNameLength = 100;

    private sealed record CatalogInput(
        KpiCreditRole CreditRole,
        KpiDataSource DataSource,
        List<JobPosition> JobPositions);

    public async Task<KpiCodeSuggestionDto> SuggestCodeAsync(string? name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Tên KPI là bắt buộc để sinh mã.");
        }

        var baseCode = KpiCodeGenerator.FromName(name);
        var taken = await db.KpiDefinitions.AsNoTracking()
            .Where(kpi => kpi.Code.StartsWith(baseCode))
            .Select(kpi => kpi.Code)
            .ToListAsync(cancellationToken);

        var code = baseCode;
        for (var number = 2; taken.Contains(code); number++)
        {
            code = $"{baseCode}_{number}";
        }

        return new KpiCodeSuggestionDto(code);
    }

    public async Task<KpiSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var counts = await db.KpiDefinitions.AsNoTracking()
            .GroupBy(kpi => kpi.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int Of(KpiStatus status) => counts.FirstOrDefault(entry => entry.Status == status)?.Count ?? 0;

        return new KpiSummaryDto(
            counts.Sum(entry => entry.Count),
            Of(KpiStatus.Draft),
            Of(KpiStatus.PendingApproval),
            Of(KpiStatus.Rejected),
            Of(KpiStatus.Active),
            Of(KpiStatus.Inactive),
            Of(KpiStatus.Archived));
    }

    public async Task<KpiUsageDto?> GetUsageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var kpi = await db.KpiDefinitions.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new { item.Id, item.ProductName })
            .SingleOrDefaultAsync(cancellationToken);
        if (kpi is null)
        {
            return null;
        }

        var objectives = await db.ObjectiveKpis.AsNoTracking()
            .Where(link => link.KpiDefinitionId == id)
            .OrderBy(link => link.BusinessObjective.Code)
            .Select(link => new KpiUsageObjectiveDto(link.BusinessObjectiveId, link.BusinessObjective.Code))
            .ToListAsync(cancellationToken);

        var targets = await db.TargetAssignments.AsNoTracking()
            .CountAsync(target => target.KpiDefinitionId == id && target.Status == TargetAssignmentStatus.Active, cancellationToken);

        var related = new List<KpiRelatedItemDto>();
        if (kpi.ProductName is not null)
        {
            var items = await db.KpiDefinitions.AsNoTracking()
                .Where(item => item.ProductName == kpi.ProductName)
                .OrderBy(item => item.CreditRole)
                .ThenBy(item => item.Code)
                .Select(item => new
                {
                    item.Id,
                    item.Code,
                    item.Name,
                    item.CreditRole,
                    item.DataSource,
                    Positions = item.JobPositions.OrderBy(position => position.Code).Select(position => position.Code).ToList(),
                })
                .ToListAsync(cancellationToken);
            related = items
                .Select(item => new KpiRelatedItemDto(item.Id, item.Code, item.Name, item.CreditRole.ToEnumString(), item.DataSource.ToEnumString(), item.Positions))
                .ToList();
        }

        return new KpiUsageDto(objectives, targets, related);
    }

    public async Task<IReadOnlyList<KpiHistoryEntryDto>?> GetHistoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.KpiDefinitions.AnyAsync(kpi => kpi.Id == id, cancellationToken))
        {
            return null;
        }

        var key = id.ToString();
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(log => log.EntityName == nameof(KpiDefinition) && log.EntityId == key)
            .OrderBy(log => log.OccurredAt)
            .ToListAsync(cancellationToken);

        return ToHistory(logs);
    }

    /// <summary>
    /// Walks the audit log oldest first and follows the status from entry to entry. The log's own "old value" of an update is not
    /// trusted: the status before a step is the status the previous step left. An update that only touched the bookkeeping
    /// timestamp is left out.
    /// </summary>
    private static List<KpiHistoryEntryDto> ToHistory(IEnumerable<AuditLog> logs)
    {
        var entries = new List<KpiHistoryEntryDto>();
        KpiStatus? status = null;
        foreach (var log in logs)
        {
            if (log.Action == "Create")
            {
                status = TryReadStatus(log.NewValues, out var created) ? created : null;
                entries.Add(new KpiHistoryEntryDto(log.OccurredAt, log.ActorName, "CREATE", "Tạo"));
                continue;
            }

            var changed = ParseColumns(log.ChangedColumns);
            if (changed.All(column => column is nameof(KpiDefinition.UpdatedAt)))
            {
                continue;
            }

            if (changed.Contains(nameof(KpiDefinition.Status)) && TryReadStatus(log.NewValues, out var to))
            {
                var from = status ?? PreviousStatusBefore(to);
                var (action, summary) = DescribeTransition(from, to, ReadText(log.NewValues, nameof(KpiDefinition.RejectReason)));
                entries.Add(new KpiHistoryEntryDto(log.OccurredAt, log.ActorName, action, summary));
                status = to;
                continue;
            }

            entries.Add(new KpiHistoryEntryDto(log.OccurredAt, log.ActorName, "UPDATE", "Chỉnh sửa"));
        }

        return entries;
    }

    /// <summary>
    /// For a KPI whose creation is not in the log (one loaded by a script): the status it can only have come from, so the first
    /// logged step still reads right (a rejection always follows "awaiting approval").
    /// </summary>
    private static KpiStatus PreviousStatusBefore(KpiStatus to) => to switch
    {
        KpiStatus.Rejected or KpiStatus.Active => KpiStatus.PendingApproval,
        KpiStatus.PendingApproval => KpiStatus.Draft,
        KpiStatus.Inactive => KpiStatus.Active,
        _ => to,
    };

    private static (string Action, string Summary) DescribeTransition(KpiStatus from, KpiStatus to, string? reason) => (from, to) switch
    {
        (_, KpiStatus.PendingApproval) => ("SUBMIT", "Gửi duyệt"),
        (KpiStatus.PendingApproval, KpiStatus.Active) => ("APPROVE", "Duyệt, bắt đầu hiệu lực"),
        (KpiStatus.PendingApproval, KpiStatus.Rejected) => ("REJECT", string.IsNullOrWhiteSpace(reason) ? "Từ chối" : $"Từ chối: {reason}"),
        (KpiStatus.Active, KpiStatus.Inactive) => ("DEACTIVATE", "Ngừng hoạt động"),
        (KpiStatus.Inactive, KpiStatus.Active) => ("REACTIVATE", "Kích hoạt lại"),
        (_, KpiStatus.Archived) => ("ARCHIVE", "Lưu trữ"),
        _ => ("UPDATE", $"Chuyển trạng thái {from.ToEnumString()} → {to.ToEnumString()}"),
    };

    private static HashSet<string> ParseColumns(string? json)
    {
        try
        {
            return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The audit snapshot stores the status as its enum number.</summary>
    private static bool TryReadStatus(string? json, out KpiStatus status)
    {
        status = default;
        try
        {
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(nameof(KpiDefinition.Status), out var value) && value.TryGetInt32(out var number))
            {
                status = (KpiStatus)number;
                return Enum.IsDefined(status);
            }
        }
        catch (JsonException)
        {
            // An unreadable snapshot is shown as a plain edit.
        }

        return false;
    }

    private static string? ReadText(string? json, string property)
    {
        try
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Keeps the KPIs in force in a period; a KPI with no first or last period is in force on that side.</summary>
    private async Task<IQueryable<KpiDefinition>> FilterInForceAsync(
        IQueryable<KpiDefinition> query,
        Guid periodId,
        CancellationToken cancellationToken)
    {
        var start = await db.EvaluationPeriods.AsNoTracking()
            .Where(period => period.Id == periodId)
            .Select(period => (DateOnly?)period.StartDate)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", periodId);

        return query.Where(kpi =>
            (kpi.EffectiveFromPeriod == null || kpi.EffectiveFromPeriod.StartDate <= start)
            && (kpi.EffectiveToPeriod == null || kpi.EffectiveToPeriod.StartDate >= start));
    }

    /// <summary>
    /// Checks the catalog fields of a save request. <paramref name="current"/> is the KPI being edited (null on create): a period it
    /// already uses stays valid even after the period was locked, so an unrelated edit is never blocked by it.
    /// </summary>
    private async Task<CatalogInput> ValidateCatalogAsync(KpiDefinitionSaveDto dto, KpiDefinition? current, CancellationToken cancellationToken)
    {
        var creditRole = EnumApiFormat.ParseEnum<KpiCreditRole>(dto.CreditRole, "creditRole") ?? current?.CreditRole ?? KpiCreditRole.Processor;
        var dataSource = EnumApiFormat.ParseEnum<KpiDataSource>(dto.DataSource, "dataSource") ?? current?.DataSource ?? KpiDataSource.Transaction;

        if (dto.ProductName?.Trim().Length > MaxProductNameLength)
        {
            throw new ValidationException($"Sản phẩm tối đa {MaxProductNameLength} ký tự.");
        }

        if (dto.BaselineValue is < 0)
        {
            throw new ValidationException("Baseline không được âm.");
        }

        var positions = await LoadPositionsAsync(dto.JobPositionIds, cancellationToken);
        await ValidatePeriodsAsync(dto, current, cancellationToken);
        return new CatalogInput(creditRole, dataSource, positions);
    }

    private async Task<List<JobPosition>> LoadPositionsAsync(IReadOnlyList<Guid>? requested, CancellationToken cancellationToken)
    {
        var ids = (requested ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var positions = await db.JobPositions.Where(position => ids.Contains(position.Id)).ToListAsync(cancellationToken);
        if (positions.Count != ids.Count)
        {
            throw new ValidationException("Có vị trí áp dụng không tồn tại.");
        }

        if (positions.Any(position => position.Status != RecordStatus.Active))
        {
            throw new ValidationException("Chỉ được chọn vị trí đang hoạt động.");
        }

        return positions;
    }

    private async Task ValidatePeriodsAsync(KpiDefinitionSaveDto dto, KpiDefinition? current, CancellationToken cancellationToken)
    {
        var from = dto.EffectiveFromPeriodId;
        var to = dto.EffectiveToPeriodId;
        if (to.HasValue && !from.HasValue)
        {
            throw new ValidationException("Phải chọn Từ kỳ trước khi chọn Đến kỳ.");
        }

        var ids = new[] { from, to }.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var periods = await db.EvaluationPeriods.AsNoTracking()
            .Where(period => ids.Contains(period.Id))
            .ToDictionaryAsync(period => period.Id, cancellationToken);
        if (periods.Count != ids.Count)
        {
            throw new ValidationException("Kỳ áp dụng không tồn tại.");
        }

        if (periods.Values.Any(period => period.PeriodType != PeriodType.Quarter))
        {
            throw new ValidationException("Kỳ áp dụng phải là kỳ quý.");
        }

        var kept = new HashSet<Guid?> { current?.EffectiveFromPeriodId, current?.EffectiveToPeriodId };
        if (ids.Any(id => periods[id].Status is PeriodStatus.Locked or PeriodStatus.Closed && !kept.Contains(id)))
        {
            throw new ValidationException("Không chọn kỳ đã khóa (MSG27).");
        }

        if (from.HasValue && to.HasValue && periods[to.Value].StartDate < periods[from.Value].StartDate)
        {
            throw new ValidationException("Đến kỳ phải lớn hơn hoặc bằng Từ kỳ (MSG27).");
        }
    }

    /// <summary>The System Admin who sent the KPI (and the Branch Manager who asked for it) hear the decision.</summary>
    private async Task TellMakerAsync(KpiDefinition kpi, Guid approverId, bool approved, CancellationToken cancellationToken)
    {
        if (notifier is null)
        {
            return;
        }

        var recipients = new[] { kpi.SubmittedBy, kpi.RequestedBy }.OfType<Guid>().Where(id => id != approverId).Distinct();
        var type = approved ? NotificationType.KpiApproved : NotificationType.KpiRejected;
        var title = approved ? "KPI đã được duyệt" : "KPI bị từ chối";
        var body = approved
            ? $"KPI {kpi.Code} - {kpi.Name} đã được duyệt và có hiệu lực."
            : $"KPI {kpi.Code} - {kpi.Name} bị từ chối: {kpi.RejectReason}";
        var drafts = recipients
            .Select(id => new NotificationDraft(id, type, title, body, NotificationSeverity.Important, NotificationRelatedType.Kpi, kpi.Id))
            .ToList();
        await notifier.TryPublishAsync(drafts, cancellationToken);
    }

    private static void ApplyCatalog(KpiDefinition kpi, KpiDefinitionSaveDto dto, CatalogInput catalog)
    {
        kpi.CreditRole = catalog.CreditRole;
        kpi.DataSource = catalog.DataSource;
        kpi.ProductName = NormalizeOptional(dto.ProductName);
        kpi.BaselineValue = dto.BaselineValue;
        kpi.EffectiveFromPeriodId = dto.EffectiveFromPeriodId;
        kpi.EffectiveToPeriodId = dto.EffectiveToPeriodId;

        kpi.JobPositions.Clear();
        foreach (var position in catalog.JobPositions)
        {
            kpi.JobPositions.Add(position);
        }
    }
}
