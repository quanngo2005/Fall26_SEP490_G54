namespace G54.BLL.Dtos.Department;

public sealed record DepartmentMetricsDto(
    int EmployeeCount,
    int ActiveEmployeeCount,
    int OnLeaveEmployeeCount,
    int OpenWorkCount,
    int AttentionWorkCount,
    int PendingReviewCount);
