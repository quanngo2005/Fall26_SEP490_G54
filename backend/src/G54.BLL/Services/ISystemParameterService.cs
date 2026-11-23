using G54.BLL.Dtos.SystemParameter;

namespace G54.BLL.Services;

public interface ISystemParameterService
{
    Task<IReadOnlyList<SystemParameterDto>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SystemParameterDto>> UpdateAsync(UpdateSystemParametersDto request, CancellationToken ct = default);

    /// <summary>Loads saved overrides into the live store; called at startup.</summary>
    Task RefreshAsync(CancellationToken ct = default);
}
