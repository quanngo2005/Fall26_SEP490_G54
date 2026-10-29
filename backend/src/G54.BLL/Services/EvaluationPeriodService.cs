using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.EvaluationPeriod;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class EvaluationPeriodService(AppDbContext db, TimeProvider time) : IEvaluationPeriodService
{
    private const int MaxCodeLength = 50;
    private const int MaxNameLength = 150;

    /// <summary>The type a period must sit directly under (mirrors <c>validate_evaluation_period</c>); YEAR has no parent.</summary>
    private static readonly Dictionary<PeriodType, PeriodType> ParentTypes = new()
    {
        [PeriodType.HalfYear] = PeriodType.Year,
        [PeriodType.Quarter] = PeriodType.HalfYear,
        [PeriodType.Month] = PeriodType.Quarter,
        [PeriodType.Day] = PeriodType.Month,
    };

    public async Task<EvaluationPeriodStatusDto> CreateAsync(CreateEvaluationPeriodRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (code, name, type, start, end) = Validate(request);

        var parent = request.ParentPeriodId is { } parentId
            ? await db.EvaluationPeriods.AsNoTracking().SingleOrDefaultAsync(p => p.Id == parentId, cancellationToken)
                ?? throw new NotFoundException("Kỳ đánh giá cha", parentId)
            : null;
        ValidateHierarchy(type, parent, start, end);

        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            PeriodType = type,
            ParentPeriodId = request.ParentPeriodId,
            StartDate = start,
            EndDate = end,
            Status = PeriodStatus.Open,
            UpdatedAt = time.GetUtcNow(),
        };
        db.EvaluationPeriods.Add(period);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            throw new ConflictException($"Mã kỳ đánh giá '{code}' đã tồn tại.");
        }

        return new EvaluationPeriodStatusDto(
            period.Id, period.Code, period.Name, type.ToEnumString(), period.ParentPeriodId, start, end, period.Status.ToEnumString());
    }

    public async Task<ClosePeriodResultDto> CloseAsync(Guid periodId, CancellationToken cancellationToken = default)
    {
        var period = await db.EvaluationPeriods.SingleOrDefaultAsync(p => p.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", periodId);
        if (period.Status != PeriodStatus.Open)
        {
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.EvaluationPeriodInvalidState),
                ErrorCodes.EvaluationPeriodInvalidState);
        }

        var from = StartOfDay(period.StartDate);
        var to = StartOfDay(period.EndDate.AddDays(1));
        var pendingResults = await db.ResultSubmissions.CountAsync(
            r => (r.Status == SubmissionStatus.Submitted || r.Status == SubmissionStatus.UnderReview)
                && r.SubmittedAt >= from && r.SubmittedAt < to,
            cancellationToken);
        var openDisputes = await db.Appeals.CountAsync(
            a => (a.Status == AppealStatus.Open || a.Status == AppealStatus.UnderReview)
                && ((a.PerformanceEvaluation != null && a.PerformanceEvaluation.EvaluationPeriodId == periodId)
                    || (a.ResultSubmission != null && a.ResultSubmission.SubmittedAt >= from && a.ResultSubmission.SubmittedAt < to)),
            cancellationToken);

        period.Status = PeriodStatus.InReview;
        period.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new ClosePeriodResultDto(period.Id, period.Status.ToEnumString(), pendingResults, openDisputes);
    }

    private static void ValidateHierarchy(PeriodType type, EvaluationPeriod? parent, DateOnly start, DateOnly end)
    {
        var error = (type, parent) switch
        {
            (PeriodType.Year, not null) => "Kỳ YEAR không được có kỳ cha.",
            (PeriodType.Year, null) => null,
            (_, null) => "Kỳ không phải YEAR phải có kỳ cha (parentPeriodId).",
            _ when ParentTypes.TryGetValue(type, out var expected) && parent.PeriodType != expected =>
                $"Kỳ {type.ToEnumString()} phải nằm trực tiếp dưới kỳ {expected.ToEnumString()}.",
            _ when start < parent.StartDate || end > parent.EndDate => "Ngày của kỳ phải nằm trong khoảng ngày của kỳ cha.",
            _ => null,
        };
        if (error is not null)
        {
            throw new ValidationException(error);
        }
    }

    private static DateTimeOffset StartOfDay(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeProviderExtensions.BusinessUtcOffset).ToUniversalTime();

    private static (string Code, string Name, PeriodType Type, DateOnly Start, DateOnly End) Validate(CreateEvaluationPeriodRequestDto request)
    {
        var errors = new List<string>();
        var code = request.Code?.Trim();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength)
        {
            errors.Add($"Mã kỳ (code) là bắt buộc, tối đa {MaxCodeLength} ký tự.");
        }

        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            errors.Add($"Tên kỳ (name) là bắt buộc, tối đa {MaxNameLength} ký tự.");
        }

        var type = EnumApiFormat.ParseEnum<PeriodType>(request.PeriodType, "periodType");
        if (type is null)
        {
            errors.Add("Loại kỳ (periodType) là bắt buộc: YEAR, HALF_YEAR, QUARTER, MONTH, WEEK hoặc DAY.");
        }

        if (request.StartDate is null || request.EndDate is null)
        {
            errors.Add("Ngày bắt đầu (startDate) và ngày kết thúc (endDate) là bắt buộc.");
        }
        else if (request.EndDate < request.StartDate)
        {
            errors.Add("Ngày kết thúc (endDate) phải không sớm hơn ngày bắt đầu (startDate).");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return (code!, name!, type!.Value, request.StartDate!.Value, request.EndDate!.Value);
    }
}
