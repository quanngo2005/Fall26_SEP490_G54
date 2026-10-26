using G54.BLL.Dtos.Profile;

namespace G54.BLL.Services;

public interface IProfileService
{
    /// <summary>Sets (or clears, when blank) the phone number. No other field can be changed here.</summary>
    /// <exception cref="G54.BLL.Common.Exceptions.ValidationException">The number is not exactly 10 digits starting with 0.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.NotFoundException">The account does not exist.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.ForbiddenException">The account is locked or the employee is not active.</exception>
    Task<MyPhoneDto> UpdatePhoneAsync(Guid accountId, UpdateMyProfileDto request, CancellationToken cancellationToken = default);
}
