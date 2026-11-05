using G54.BLL.Common.Models;
using G54.BLL.Dtos.KpiDefinition;

namespace G54.BLL.Services;

public interface IKpiDefinitionService
{
    Task<PagedResult<KpiDefinitionDto>> GetKpisAsync(KpiFilterDto filter, CancellationToken cancellationToken = default);
    Task<KpiDefinitionDto?> GetKpiByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<KpiGroupOptionDto>> GetGroupOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The unused code a KPI with this name would get: its name without accents, and a number when that is taken.</summary>
    Task<KpiCodeSuggestionDto> SuggestCodeAsync(string? name, CancellationToken cancellationToken = default);

    /// <summary>How many KPIs are in each status, for the tabs of the KPI list.</summary>
    Task<KpiSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Where a KPI is used (objectives, active targets) and the other KPIs of its product; null when it does not exist.</summary>
    Task<KpiUsageDto?> GetUsageAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The change history of a KPI, oldest first, taken from the audit log; null when the KPI does not exist.</summary>
    Task<IReadOnlyList<KpiHistoryEntryDto>?> GetHistoryAsync(Guid id, CancellationToken cancellationToken = default);
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
