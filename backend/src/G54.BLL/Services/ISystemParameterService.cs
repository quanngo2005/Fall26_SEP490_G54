using G54.BLL.Dtos.SystemParameters;

namespace G54.BLL.Services;

public interface ISystemParameterService
{
    /// <summary>
    /// Get all system parameters. Only admins are allowed.
    /// </summary>
    Task<IReadOnlyList<SystemParameterDto>> GetAllAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Save changes to system parameters. Only admins are allowed.
    /// </summary>
    Task<IReadOnlyList<SystemParameterDto>> SaveAsync(Guid accountId, SaveSystemParametersDto request, CancellationToken ct);
}
