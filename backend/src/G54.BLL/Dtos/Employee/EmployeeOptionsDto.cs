using G54.DAL.Entities;

namespace G54.BLL.Dtos.Employee;

public sealed record DepartmentOptionDto(
    Guid Id,
    string Code,
    string Name,
    DepartmentLevelType LevelType);

public sealed record JobPositionOptionDto(
    Guid Id,
    string Code,
    string Title);
