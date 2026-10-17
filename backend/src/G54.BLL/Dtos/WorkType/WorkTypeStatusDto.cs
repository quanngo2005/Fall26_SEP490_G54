using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.WorkType;

public sealed record WorkTypeStatusDto
{
    [EnumDataType(typeof(WorkTypeStatus))]
    public WorkTypeStatus Status { get; init; }
}
