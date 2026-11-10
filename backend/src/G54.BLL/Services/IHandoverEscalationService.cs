namespace G54.BLL.Services;

public interface IHandoverEscalationService
{
    /// <summary>
    /// Escalates every PENDING handover older than <paramref name="sla"/> that has not been escalated yet to the manager of
    /// its receiving employee (BR-21). A handover is escalated once; a receiver without a manager is skipped.
    /// Returns the number of handovers escalated.
    /// </summary>
    Task<int> EscalateOverdueAsync(TimeSpan sla, CancellationToken cancellationToken = default);
}
