using System.ComponentModel.DataAnnotations;
using G54.DAL.Entities;

namespace G54.BLL.Dtos.KpiDefinition;

public sealed record KpiStatusDto
{
    [EnumDataType(typeof(KpiStatus))]
    public KpiStatus Status { get; init; }
}
