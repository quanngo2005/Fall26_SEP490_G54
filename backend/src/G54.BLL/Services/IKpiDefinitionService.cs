using G54.BLL.Common.Models;
using G54.BLL.Dtos.KpiDefinition;

namespace G54.BLL.Services;

public interface IKpiDefinitionService
{
    Task<PagedResult<KpiDefinitionDto>> GetKpisAsync(KpiFilterDto filter, CancellationToken cancellationToken = default);
    Task<KpiDefinitionDto?> GetKpiByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<KpiGroupOptionDto>> GetGroupOptionsAsync(CancellationToken cancellationToken = default);
    Task<KpiDefinitionDto> CreateKpiAsync(KpiDefinitionSaveDto dto, CancellationToken cancellationToken = default);

    /// <summary>A Branch Manager asks for a KPI missing from the catalog; it is created as DRAFT for the System Admin to complete.</summary>
    Task<KpiDefinitionDto> RequestKpiAsync(Guid accountId, KpiRequestDto dto, CancellationToken cancellationToken = default);

    Task<KpiDefinitionDto> UpdateKpiAsync(Guid id, KpiDefinitionSaveDto dto, CancellationToken cancellationToken = default);

    /// <summary>Moves a KPI along the allowed transitions; sending it for approval records <paramref name="accountId"/> as the maker.</summary>
    Task ChangeStatusAsync(Guid accountId, Guid id, KpiStatusDto dto, CancellationToken cancellationToken = default);

    /// <summary>Activates a PENDING_APPROVAL KPI; the approver must not be the one who sent it for approval.</summary>
    Task ApproveKpiAsync(Guid accountId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Sends a PENDING_APPROVAL KPI back as REJECTED with the approver's reason.</summary>
    Task RejectKpiAsync(Guid accountId, Guid id, RejectKpiDto dto, CancellationToken cancellationToken = default);
}
