using System.Text.Json;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.Evaluation;
using G54.BLL.Dtos.SystemParameter;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class RankingPolicyService(
    AppDbContext db,
    IPerformanceScoreCalculator calculator) : IRankingPolicyService
{
    public const string DefaultPolicyCode = "DEFAULT_POLICY";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<RankingPolicyDto> GetPolicyAsync(Guid? periodId = null, CancellationToken ct = default)
    {
        EvaluationPeriod? period = null;
        if (periodId.HasValue)
        {
            period = await db.EvaluationPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId.Value, ct)
                ?? throw new NotFoundException("Kỳ đánh giá", periodId.Value);

            if (period.Status is PeriodStatus.Locked or PeriodStatus.Closed
                && !string.IsNullOrWhiteSpace(period.RankingPolicySnapshotJson)
                && TryDeserialize(period.RankingPolicySnapshotJson, out var snapshotDef))
            {
                return new RankingPolicyDto(
                    period.Id,
                    $"SNAPSHOT_{period.Code}",
                    $"Bản chụp chính sách kỳ {period.Name}",
                    "Chính sách đã được đóng băng khi khóa kỳ đánh giá.",
                    1,
                    period.Id,
                    HasPeriodOverride: true,
                    IsLockedSnapshot: true,
                    period.UpdatedAt,
                    null,
                    snapshotDef!,
                    RankingRuleCatalog.ToDto());
            }

            var periodRow = await db.RankingPolicies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.EvaluationPeriodId == period.Id && !p.IsDeleted, ct);
            if (periodRow is not null && TryDeserialize(periodRow.PolicyJson, out var periodDef))
            {
                var editor = await ResolveEditorUsernameAsync(periodRow.UpdatedBy ?? periodRow.CreatedBy, ct);
                return new RankingPolicyDto(
                    periodRow.Id,
                    periodRow.Code,
                    periodRow.Name,
                    periodRow.Description,
                    periodRow.Version,
                    period.Id,
                    HasPeriodOverride: true,
                    IsLockedSnapshot: period.Status is PeriodStatus.Locked or PeriodStatus.Closed,
                    periodRow.UpdatedAt ?? periodRow.CreatedAt,
                    editor,
                    periodDef!,
                    RankingRuleCatalog.ToDto());
            }
        }

        var defaultRow = await db.RankingPolicies.AsNoTracking()
            .Where(p => p.EvaluationPeriodId == null && p.IsActive && !p.IsDeleted)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

        if (defaultRow is not null && TryDeserialize(defaultRow.PolicyJson, out var defaultDef))
        {
            var editor = await ResolveEditorUsernameAsync(defaultRow.UpdatedBy ?? defaultRow.CreatedBy, ct);
            return new RankingPolicyDto(
                defaultRow.Id,
                defaultRow.Code,
                defaultRow.Name,
                defaultRow.Description,
                defaultRow.Version,
                period?.Id,
                HasPeriodOverride: false,
                IsLockedSnapshot: period?.Status is PeriodStatus.Locked or PeriodStatus.Closed,
                defaultRow.UpdatedAt ?? defaultRow.CreatedAt,
                editor,
                defaultDef!,
                RankingRuleCatalog.ToDto());
        }

        var synthesized = await BuildFallbackDefinitionFromLegacyTablesAsync(ct);
        return new RankingPolicyDto(
            Guid.Empty,
            DefaultPolicyCode,
            "Chính sách gợi ý xếp hạng chuẩn Chi nhánh",
            "Chính sách mặc định khởi tạo từ thang điểm hệ thống.",
            1,
            period?.Id,
            HasPeriodOverride: false,
            IsLockedSnapshot: period?.Status is PeriodStatus.Locked or PeriodStatus.Closed,
            null,
            null,
            synthesized,
            RankingRuleCatalog.ToDto());
    }

    public async Task<RankingPolicyDto> UpdatePolicyAsync(UpdateRankingPolicyDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
        {
            errors.Add("Tên chính sách xếp hạng là bắt buộc và tối đa 200 ký tự.");
        }

        if (request.Description is { Length: > 1000 })
        {
            errors.Add("Mô tả chính sách tối đa 1000 ký tự.");
        }

        errors.AddRange(RankingRuleEngine.ValidateDefinition(request.Definition));
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        EvaluationPeriod? period = null;
        if (request.EvaluationPeriodId.HasValue)
        {
            period = await db.EvaluationPeriods.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == request.EvaluationPeriodId.Value, ct)
                ?? throw new NotFoundException("Kỳ đánh giá", request.EvaluationPeriodId.Value);

            if (period.Status is PeriodStatus.Locked or PeriodStatus.Closed)
            {
                throw new BusinessRuleException(
                    ErrorCodes.PeriodLocked,
                    "Kỳ đánh giá đã khóa hoặc đóng, không thể thay đổi chính sách gợi ý xếp hạng.",
                    409);
            }
        }

        var normalized = NormalizeDefinition(request.Definition);
        var json = JsonSerializer.Serialize(normalized, JsonOptions);

        RankingPolicy? existing = request.EvaluationPeriodId.HasValue
            ? await db.RankingPolicies.FirstOrDefaultAsync(p => p.EvaluationPeriodId == request.EvaluationPeriodId.Value, ct)
            : await db.RankingPolicies.FirstOrDefaultAsync(p => p.Code == DefaultPolicyCode && p.EvaluationPeriodId == null, ct);

        if (existing is null)
        {
            var code = period is not null
                ? $"POLICY_{period.Code}"[..Math.Min(50, $"POLICY_{period.Code}".Length)]
                : DefaultPolicyCode;
            existing = new RankingPolicy
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Version = 1,
                IsActive = true,
                EvaluationPeriodId = request.EvaluationPeriodId,
                PolicyJson = json,
            };
            db.RankingPolicies.Add(existing);
        }
        else
        {
            existing.Name = request.Name.Trim();
            existing.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            existing.PolicyJson = json;
            existing.IsActive = true;
            existing.IsDeleted = false;
            existing.Version += 1;
        }

        if (!request.EvaluationPeriodId.HasValue)
        {
            await SyncLegacyTierTablesAsync(normalized, ct);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Chính sách xếp hạng vừa được người khác cập nhật. Vui lòng tải lại và thử lại.");
        }
        catch (DbUpdateException ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            throw new ConflictException("Chính sách xếp hạng vừa được người khác cập nhật. Vui lòng tải lại và thử lại.");
        }

        return await GetPolicyAsync(request.EvaluationPeriodId, ct);
    }

    public async Task<RankingPolicyDefinitionDto> LoadEffectiveDefinitionAsync(EvaluationPeriod? period, CancellationToken ct = default)
    {
        if (period is not null)
        {
            if (period.Status is PeriodStatus.Locked or PeriodStatus.Closed
                && !string.IsNullOrWhiteSpace(period.RankingPolicySnapshotJson)
                && TryDeserialize(period.RankingPolicySnapshotJson, out var snapshotDef))
            {
                return snapshotDef!;
            }

            var periodPolicy = await db.RankingPolicies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.EvaluationPeriodId == period.Id && p.IsActive && !p.IsDeleted, ct);
            if (periodPolicy is not null && TryDeserialize(periodPolicy.PolicyJson, out var periodDef))
            {
                return periodDef!;
            }
        }

        var defaultPolicy = await db.RankingPolicies.AsNoTracking()
            .Where(p => p.EvaluationPeriodId == null && p.IsActive && !p.IsDeleted)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

        if (defaultPolicy is not null && TryDeserialize(defaultPolicy.PolicyJson, out var defaultDef))
        {
            return defaultDef!;
        }

        return await BuildFallbackDefinitionFromLegacyTablesAsync(ct);
    }

    public async Task<string> BuildSnapshotJsonAsync(EvaluationPeriod period, CancellationToken ct = default)
    {
        var effective = await LoadEffectiveDefinitionAsync(period, ct);
        return JsonSerializer.Serialize(NormalizeDefinition(effective), JsonOptions);
    }

    public async Task<SimulateRankingPolicyResultDto> SimulateAsync(SimulateRankingPolicyRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = RankingRuleEngine.ValidateDefinition(request.Definition);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        EvaluationPeriod? period = null;
        if (request.PeriodId.HasValue)
        {
            period = await db.EvaluationPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PeriodId.Value, ct)
                ?? throw new NotFoundException("Kỳ đánh giá", request.PeriodId.Value);
        }

        var baselinePolicy = await LoadEffectiveDefinitionAsync(period, ct);
        var draftPolicy = NormalizeDefinition(request.Definition);
        var tierCriteria = await EvaluationSettingsService.LoadCriteriaAsync(db, ct);

        var (subjects, metaByEmployee, isSynthetic, periodLabel) = await LoadSimulationCohortAsync(period, request.DepartmentId, ct);

        var beforeResults = RankingRuleEngine.EvaluateRoster(subjects, baselinePolicy, tierCriteria);
        var afterResults = RankingRuleEngine.EvaluateRoster(subjects, draftPolicy, tierCriteria);

        var rows = subjects
            .Where(s => s.TotalScore.HasValue)
            .Select(s =>
            {
                var before = beforeResults[s.EmployeeId];
                var after = afterResults[s.EmployeeId];
                var meta = metaByEmployee[s.EmployeeId];
                var beforeTier = before.ProposedTier ?? "C";
                var afterTier = after.ProposedTier ?? "C";
                return new SimulatedEmployeeRankingRowDto(
                    s.EmployeeId,
                    s.EmployeeCode,
                    meta.FullName,
                    s.JobPositionCode,
                    meta.JobPositionTitle,
                    meta.DepartmentName,
                    Math.Round(s.TotalScore!.Value, 2),
                    after.EffectiveScore ?? Math.Round(s.TotalScore.Value, 2),
                    s.BusinessScore.HasValue ? Math.Round(s.BusinessScore.Value, 2) : null,
                    s.ServiceQualityScore.HasValue ? Math.Round(s.ServiceQualityScore.Value, 2) : null,
                    s.OperationsScore.HasValue ? Math.Round(s.OperationsScore.Value, 2) : null,
                    s.QualitativeScore.HasValue ? Math.Round(s.QualitativeScore.Value, 2) : null,
                    meta.OpenRiskEvents,
                    meta.MaxSeverityLabel,
                    before.Rank ?? 0,
                    after.Rank ?? 0,
                    beforeTier,
                    afterTier,
                    Changed: !string.Equals(beforeTier, afterTier, StringComparison.OrdinalIgnoreCase) || before.Rank != after.Rank,
                    after.CoachingFlag,
                    after.WarningLabel,
                    after.MatchedRules,
                    after.Explanation);
            })
            .OrderBy(r => r.AfterRank)
            .ThenBy(r => r.EmployeeCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = rows.Count;
        var allTierCodes = draftPolicy.Tiers.OrderBy(t => t.RankOrder).Select(t => t.TierCode)
            .Concat(baselinePolicy.Tiers.OrderBy(t => t.RankOrder).Select(t => t.TierCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tierSummary = allTierCodes.Select(code =>
        {
            var bCount = rows.Count(r => string.Equals(r.BeforeTier, code, StringComparison.OrdinalIgnoreCase));
            var aCount = rows.Count(r => string.Equals(r.AfterTier, code, StringComparison.OrdinalIgnoreCase));
            return new SimulatedTierDistributionItemDto(
                code,
                bCount,
                total > 0 ? Math.Round(bCount * 100m / total, 1) : 0m,
                aCount,
                total > 0 ? Math.Round(aCount * 100m / total, 1) : 0m);
        }).ToList();

        return new SimulateRankingPolicyResultDto(
            period?.Id,
            periodLabel,
            isSynthetic,
            total,
            rows.Count(r => !string.Equals(r.BeforeTier, r.AfterTier, StringComparison.OrdinalIgnoreCase)),
            tierSummary,
            rows);
    }

    private async Task<(List<RankingSubjectInput> Subjects, Dictionary<Guid, SimulationEmployeeMeta> Meta, bool IsSynthetic, string PeriodLabel)>
        LoadSimulationCohortAsync(EvaluationPeriod? period, Guid? departmentId, CancellationToken ct)
    {
        if (period is not null)
        {
            var empQuery = db.Employees.AsNoTracking()
                .Include(e => e.Department)
                .Include(e => e.JobPosition)
                .Where(e => e.Status == EmployeeStatus.Active);

            if (departmentId.HasValue)
            {
                var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), ct);
                var subtree = hierarchy.Nodes.ContainsKey(departmentId.Value)
                    ? hierarchy.Subtree(departmentId.Value)
                    : [departmentId.Value];
                empQuery = empQuery.Where(e => subtree.Contains(e.DepartmentId));
            }

            var employees = await empQuery.OrderBy(e => e.EmployeeCode).ToListAsync(ct);
            if (employees.Count > 0)
            {
                var empIds = employees.Select(e => e.Id).ToList();
                var evaluations = await db.PerformanceEvaluations.AsNoTracking()
                    .Where(pe => pe.EvaluationPeriodId == period.Id && empIds.Contains(pe.EmployeeId))
                    .ToDictionaryAsync(pe => pe.EmployeeId, ct);

                var profiles = await LoadActiveProfilesByPositionAsync(employees.Select(e => e.JobPositionId), period, ct);
                var subjectsToCalc = employees
                    .Where(e => profiles.ContainsKey(e.JobPositionId))
                    .Select(e => new PerformanceScoreSubject(
                        e.Id,
                        e.DepartmentId,
                        profiles[e.JobPositionId],
                        new Dictionary<Guid, QualitativeScoreInputDto>()))
                    .ToList();

                var calculated = subjectsToCalc.Count > 0
                    ? await calculator.CalculateAsync(subjectsToCalc, period, ct)
                    : new Dictionary<Guid, PerformanceScoreResult>();

                var periodStart = period.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var periodEnd = period.EndDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
                var risks = (await db.RiskEvents.AsNoTracking()
                        .Where(r => empIds.Contains(r.EmployeeId)
                            && r.OccurredAt >= periodStart
                            && r.OccurredAt <= periodEnd
                            && r.Status != RiskEventStatus.Dismissed)
                        .ToListAsync(ct))
                    .GroupBy(r => r.EmployeeId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var realSubjects = new List<RankingSubjectInput>();
                var realMeta = new Dictionary<Guid, SimulationEmployeeMeta>();

                foreach (var emp in employees)
                {
                    evaluations.TryGetValue(emp.Id, out var pe);
                    calculated.TryGetValue(emp.Id, out var calc);
                    var total = pe?.TotalScore ?? calc?.TotalScore;
                    if (!total.HasValue)
                    {
                        continue;
                    }

                    var (bus, svc, ops) = calc is not null ? calc.ComponentScores() : (total, total, total);
                    var empRisks = risks.GetValueOrDefault(emp.Id) ?? [];
                    realSubjects.Add(new RankingSubjectInput(
                        emp.Id,
                        emp.EmployeeCode,
                        emp.JobPosition?.Code ?? "STAFF",
                        total,
                        calc?.WeightedScore ?? total,
                        calc?.QuantitativeScore ?? total,
                        QualitativeScore: null,
                        bus,
                        svc,
                        ops,
                        calc?.RiskPenalty ?? 0m,
                        ScoreChangeVsPrev: 0m,
                        DepartmentAvgScore: null,
                        empRisks));

                    realMeta[emp.Id] = new SimulationEmployeeMeta(
                        emp.FullName,
                        emp.JobPosition?.Title ?? "Nhân viên",
                        emp.Department?.Name ?? "Phòng nghiệp vụ",
                        empRisks.Count,
                        empRisks.Count > 0 ? empRisks.Max(r => r.Severity).ToEnumString() : "NONE");
                }

                if (realSubjects.Count > 0)
                {
                    return (realSubjects, realMeta, false, $"{period.Code} · {period.Name}");
                }
            }
        }

        // Deterministic synthetic banking cohort when the chosen period has no scored evaluations yet.
        return BuildSyntheticBankingCohort(period is not null ? $"{period.Code} (Bộ dữ liệu mô phỏng)" : "Bộ dữ liệu mô phỏng chuẩn Chi nhánh");
    }

    private static (List<RankingSubjectInput> Subjects, Dictionary<Guid, SimulationEmployeeMeta> Meta, bool IsSynthetic, string PeriodLabel)
        BuildSyntheticBankingCohort(string label)
    {
        var samples = new (string Code, string Name, string PosCode, string PosTitle, string Dept, decimal Total, decimal Bus, decimal Svc, decimal Ops, decimal Qual, RiskEventSeverity? Risk)[]
        {
            ("NV-SIM-01", "Nguyễn Minh Anh", "SALES_OFFICER", "Chuyên viên QHKH Cá nhân", "Phòng KHCN", 97.4m, 99.0m, 95.0m, 96.0m, 96.0m, null),
            ("NV-SIM-02", "Trần Quốc Bảo", "SALES_OFFICER", "Chuyên viên QHKH Cá nhân", "Phòng KHCN", 93.8m, 96.0m, 91.0m, 92.0m, 90.0m, RiskEventSeverity.Critical),
            ("NV-SIM-03", "Lê Thu Hà", "TELLER", "Giao dịch viên", "Phòng Dịch vụ KH", 91.5m, 89.0m, 96.5m, 94.0m, 93.0m, null),
            ("NV-SIM-04", "Phạm Hoàng Nam", "SALES_OFFICER", "Chuyên viên QHKH Cá nhân", "Phòng KHCN", 88.2m, 92.0m, 84.0m, 86.0m, 88.0m, null),
            ("NV-SIM-05", "Hoàng Mai Phương", "TELLER", "Giao dịch viên", "Phòng Dịch vụ KH", 88.2m, 86.0m, 92.0m, 89.0m, 90.0m, RiskEventSeverity.Low),
            ("NV-SIM-06", "Vũ Đức Thịnh", "TELLER", "Giao dịch viên", "Phòng Dịch vụ KH", 79.5m, 78.0m, 82.0m, 80.0m, 78.0m, null),
            ("NV-SIM-07", "Đặng Ngọc Lan", "SALES_OFFICER", "Chuyên viên QHKH Cá nhân", "Phòng KHCN", 72.0m, 85.0m, 46.0m, 75.0m, 74.0m, RiskEventSeverity.High),
            ("NV-SIM-08", "Bùi Quang Huy", "TELLER", "Giao dịch viên", "Phòng Dịch vụ KH", 61.5m, 60.0m, 64.0m, 62.0m, 60.0m, RiskEventSeverity.Medium),
        };

        var subjects = new List<RankingSubjectInput>(samples.Length);
        var meta = new Dictionary<Guid, SimulationEmployeeMeta>(samples.Length);
        var avg = samples.Average(s => s.Total);

        for (var i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            var id = Guid.Parse($"00000000-0000-0000-0910-{(i + 1):D12}");
            var risks = s.Risk.HasValue
                ? new List<RiskEvent>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        EmployeeId = id,
                        Severity = s.Risk.Value,
                        Status = RiskEventStatus.Confirmed,
                        OccurredAt = DateTimeOffset.UtcNow,
                    },
                }
                : [];

            subjects.Add(new RankingSubjectInput(
                id,
                s.Code,
                s.PosCode,
                s.Total,
                s.Total,
                s.Total,
                s.Qual,
                s.Bus,
                s.Svc,
                s.Ops,
                s.Risk == RiskEventSeverity.Critical ? 20m : s.Risk == RiskEventSeverity.High ? 10m : s.Risk == RiskEventSeverity.Medium ? 5m : s.Risk == RiskEventSeverity.Low ? 2m : 0m,
                ScoreChangeVsPrev: 0m,
                DepartmentAvgScore: avg,
                risks));

            meta[id] = new SimulationEmployeeMeta(
                s.Name,
                s.PosTitle,
                s.Dept,
                risks.Count,
                s.Risk.HasValue ? s.Risk.Value.ToEnumString() : "NONE");
        }

        return (subjects, meta, true, label);
    }

    private async Task<Dictionary<Guid, EvaluationProfile>> LoadActiveProfilesByPositionAsync(
        IEnumerable<Guid> positionIds,
        EvaluationPeriod period,
        CancellationToken ct)
    {
        var ids = positionIds.Distinct().ToList();
        var profiles = await db.EvaluationProfiles.AsNoTracking()
            .Include(ep => ep.EvaluationProfileItems)
                .ThenInclude(epi => epi.KpiDefinition)
                    .ThenInclude(kd => kd!.KpiGroup)
            .Where(ep => ids.Contains(ep.JobPositionId))
            .ToListAsync(ct);

        return profiles
            .GroupBy(ep => ep.JobPositionId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(ep => ep.EffectiveFrom <= period.EndDate && (ep.EffectiveTo == null || ep.EffectiveTo >= period.StartDate))
                    .ThenByDescending(ep => ep.Status == EvaluationProfileStatus.Active)
                    .ThenByDescending(ep => ep.EffectiveFrom)
                    .First());
    }

    private async Task<RankingPolicyDefinitionDto> BuildFallbackDefinitionFromLegacyTablesAsync(CancellationToken ct)
    {
        var tiers = await db.RankingTierThresholds.AsNoTracking().OrderBy(t => t.RankOrder).ToListAsync(ct);
        var downgrade = await db.Downgrades.AsNoTracking().FirstOrDefaultAsync(ct);

        var fallback = RankingRuleEngine.DefaultDefinition();
        if (tiers.Count >= 2)
        {
            var mappedTiers = tiers.Select((t, idx) => new RankingTierDefinitionDto(t.TierCode, idx + 1, t.MinScore)).ToList();
            var capCode = downgrade?.CapTierCode is { } c && mappedTiers.Any(t => t.TierCode == c)
                ? c
                : mappedTiers[Math.Min(1, mappedTiers.Count - 1)].TierCode;
            var severityLevel = downgrade?.TriggerSeverity switch
            {
                RiskEventSeverity.Low => 1m,
                RiskEventSeverity.Medium => 2m,
                RiskEventSeverity.High => 3m,
                _ => 4m,
            };

            return fallback with
            {
                Tiers = mappedTiers,
                Rules =
                [
                    new(
                        Id: "rule-critical-risk-cap",
                        Name: "Hạ trần kỷ luật khi có sự kiện rủi ro vượt ngưỡng",
                        Enabled: true,
                        StopOnMatch: false,
                        Conditions: new("AND", [new("MAX_RISK_SEVERITY", "GTE", severityLevel)], []),
                        Actions: [new("CAP_MAX_TIER", TierCode: capCode, Message: $"Hạ trần tối đa hạng {capCode} do sự kiện rủi ro")]),
                ],
            };
        }

        return fallback;
    }

    private async Task SyncLegacyTierTablesAsync(RankingPolicyDefinitionDto definition, CancellationToken ct)
    {
        var existingTiers = await db.RankingTierThresholds.ToListAsync(ct);
        db.RankingTierThresholds.RemoveRange(existingTiers);
        var now = DateTimeOffset.UtcNow;
        foreach (var tier in definition.Tiers.OrderBy(t => t.RankOrder))
        {
            db.RankingTierThresholds.Add(new RankingTierThreshold
            {
                Id = Guid.NewGuid(),
                TierCode = tier.TierCode.Trim(),
                RankOrder = tier.RankOrder,
                MinScore = tier.MinScore,
                UpdatedAt = now,
            });
        }

        var existingDowngrade = await db.Downgrades.FirstOrDefaultAsync(ct);
        var capAction = definition.Rules
            .Where(r => r.Enabled)
            .SelectMany(r => r.Actions)
            .FirstOrDefault(a => a.Type == "CAP_MAX_TIER" && !string.IsNullOrWhiteSpace(a.TierCode));
        var fallbackCap = capAction?.TierCode?.Trim()
            ?? definition.Tiers.OrderBy(t => t.RankOrder).Skip(1).FirstOrDefault()?.TierCode
            ?? definition.Tiers[0].TierCode;

        if (existingDowngrade is not null)
        {
            existingDowngrade.CapTierCode = fallbackCap;
            existingDowngrade.UpdatedAt = now;
        }
        else
        {
            db.Downgrades.Add(new Downgrade
            {
                Id = Guid.NewGuid(),
                TriggerSeverity = RiskEventSeverity.Critical,
                CapTierCode = fallbackCap,
                UpdatedAt = now,
            });
        }
    }

    private async Task<string?> ResolveEditorUsernameAsync(Guid? accountId, CancellationToken ct)
    {
        if (!accountId.HasValue) return null;
        return await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId.Value)
            .Select(a => a.Username)
            .FirstOrDefaultAsync(ct);
    }

    private static RankingPolicyDefinitionDto NormalizeDefinition(RankingPolicyDefinitionDto def) => new(
        def.ScoreFormula.Trim(),
        def.Tiers.OrderBy(t => t.RankOrder).Select((t, idx) => new RankingTierDefinitionDto(t.TierCode.Trim(), idx + 1, Math.Round(t.MinScore, 2))).ToList(),
        def.TieBreakers.Distinct(StringComparer.Ordinal).ToList(),
        (def.Rules ?? []).Select(r => new RankingAutomationRuleDto(
            string.IsNullOrWhiteSpace(r.Id) ? $"rule-{Guid.NewGuid():N}"[..13] : r.Id.Trim(),
            r.Name.Trim(),
            r.Enabled,
            r.StopOnMatch,
            r.Conditions,
            r.Actions)).ToList());

    private static bool TryDeserialize(string json, out RankingPolicyDefinitionDto? dto)
    {
        dto = null;
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RankingPolicyDefinitionDto>(json, JsonOptions);
            if (parsed is { Tiers.Count: >= 2 })
            {
                dto = parsed;
                return true;
            }
        }
#pragma warning disable CA1031
        catch
#pragma warning restore CA1031
        {
            // Corrupted JSON falls back cleanly to legacy/default configuration.
        }

        return false;
    }

    private sealed record SimulationEmployeeMeta(
        string FullName,
        string JobPositionTitle,
        string DepartmentName,
        int OpenRiskEvents,
        string MaxSeverityLabel);
}
