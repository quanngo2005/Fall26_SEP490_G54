using G54.BLL.Common.Workflow;

namespace G54.BLL.Dtos.Work;

public sealed record TransitionWorkRequestDto(
    WorkTrigger Trigger,
    string? Note = null);
