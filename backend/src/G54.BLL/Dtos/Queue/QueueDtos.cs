namespace G54.BLL.Dtos.Queue;

public sealed record CounterCommand(Guid RequestId, string Action, Guid? VisitId, string? Service, string? WorkType);
public sealed record CounterVisit(Guid Id, string Number, string Service, string State, DateTimeOffset CalledAt, string? WorkType, Guid? WorkId);
public sealed record CounterState(int? Counter, CounterVisit? Visit, DateTimeOffset ServerTime, IReadOnlyList<string>? Options = null);
internal sealed record QueueEventDto(Guid Id, int Version, DateTimeOffset At, Guid? Actor, int? Counter, string Action, string Service);
internal sealed record QueueHistory(Guid Id, string Number, Guid WorkOwner, string WorkType, int Version, IReadOnlyList<QueueEventDto> Events,
    string? CustomerName = null, string? CustomerCif = null);
internal sealed record QueueRating(Guid Id, Guid Employee, DateTimeOffset RatedAt, int Score, string? Comment);
