using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>
/// BR-29: KPI data of a locked evaluation period is read-only. Contract: docs/api/PERIOD_LOCK_CONTRACT.md.
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
}
