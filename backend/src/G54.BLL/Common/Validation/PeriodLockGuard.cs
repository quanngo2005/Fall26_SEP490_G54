using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.DAL.Entities;

namespace G54.BLL.Common.Validation;

/// <summary>
/// KPI data of a locked evaluation period is read-only. Contract: docs/api/PERIOD_LOCK_CONTRACT.md.
/// A period is locked when its status is <see cref="PeriodStatus.Locked"/> or <see cref="PeriodStatus.Closed"/>.
/// </summary>
internal static class PeriodLockGuard
{
    /// <summary>Throws 409 <see cref="ErrorCodes.PeriodLocked"/> when the period is locked.</summary>
    public static void EnsureWritable(EvaluationPeriod? period)
    {
        if (period?.Status is PeriodStatus.Locked or PeriodStatus.Closed)
        {
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.PeriodLocked),
                ErrorCodes.PeriodLocked);
        }
    }

    /// <summary>
    /// Throws 409 <see cref="ErrorCodes.MonthHardLocked"/> (BR-29) when <paramref name="date"/> falls in or before
    /// the hard-lock watermark month of <paramref name="period"/>. A target cannot be made effective in a month
    /// that is already hard-locked.
    /// </summary>
    public static void EnsureMonthOpen(EvaluationPeriod? period, DateOnly date)
    {
        if (period?.HardLockedThrough is not { } lockedThrough)
        {
            return;
        }

        var dateMonth = date.Year * 12 + date.Month;
        var lockedMonth = lockedThrough.Year * 12 + lockedThrough.Month;
        if (dateMonth <= lockedMonth)
        {
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.MonthHardLocked),
                ErrorCodes.MonthHardLocked);
        }
    }
}
